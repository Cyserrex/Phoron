using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoron.Core
{
    /// <summary>
    /// Membangun seluruh berkas konfigurasi untuk sebuah profil di folder etc\.
    /// Prinsipnya: JANGAN pernah menyunting berkas di dalam folder bin. Bin bisa
    /// milik Laragon atau dipakai bersama, dan menulis ke sana berarti dua
    /// pengelola saling menimpa. Semua yang dihasilkan Phoron ada di etc\.
    /// </summary>
    public static class ConfigWriter
    {
        public const string GeneratedHeader =
            "# Berkas ini DIBUAT OTOMATIS oleh Phoron. Suntingan tangan akan hilang\n" +
            "# saat profil di-switch. Ubah lewat profil atau folder etc\\apache2\\alias.";

        public class Result
        {
            public string HttpdConf;
            public string MyIni;
            public string PhpIniDir;
            public string NginxConf;
            public readonly List<string> Warnings = new List<string>();
            public bool PhpFastCgi;
            public int FastCgiPort = 9123;
            /// <summary>true bila konfigurasi ini benar-benar membuka port HTTPS.</summary>
            public bool SslEnabled;
            /// <summary>
            /// Ekstensi yang diambil alih dari php.ini dasar karena profilnya
            /// belum punya daftar sendiri. Null bila tidak ada pengambilalihan.
            /// </summary>
            public List<string> AdoptedExtensions;
            /// <summary>
            /// Kolam php-cgi untuk situs yang memakai versi PHP sendiri - yang
            /// benar-benar ditulis ke konfigurasi, dengan port dan folder
            /// php.ini-nya. Kosong bila tidak ada penimpaan, atau bila web
            /// server-nya tidak sanggup (Apache di bawah 2.4.26).
            /// </summary>
            public readonly List<Kolam> Kolam = new List<Kolam>();
        }

        /// <summary>
        /// Satu versi PHP tambahan yang melayani sebagian situs lewat FastCGI,
        /// berdampingan dengan PHP profil.
        ///
        /// SATU kolam per VERSI, bukan per situs: enam proyek Laravel di PHP 8.3
        /// dilayani satu kolam yang sama. Tiap kolam adalah satu php-cgi dengan
        /// dua pekerja - diukur di mesin pengembang, 76 MB menganggur dan 0 ms
        /// waktu CPU dalam 30 detik.
        /// </summary>
        public class Kolam
        {
            public BinPackage Php;
            /// <summary>Ekstensi untuk php.ini kolam - daftar milik versi ITU, bukan milik profil.</summary>
            public List<string> Ekstensi = new List<string>();
            public Dictionary<string, string> Timpa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<Site> Situs = new List<Site>();
            /// <summary>Diisi Build.</summary>
            public int Port;
            /// <summary>Diisi Build: folder php.ini kolam, untuk PHPRC.</summary>
            public string FolderIni;
        }

        public static Result Build(Profile profile, BinPackage php, BinPackage apache,
                                   BinPackage mysql, BinPackage nginx, List<Site> sites,
                                   bool phpIniKeFolderPhp = false, bool logAkses = true,
                                   bool berandaDiAkar = true, bool opcache = true,
                                   List<Kolam> kolam = null)
        {
            var r = new Result();
            if (php != null) r.PhpIniDir = WritePhpIni(profile, php, r, phpIniKeFolderPhp, opcache);
            // SEBELUM web server ditulis: modul proxy Apache dan blok server
            // Nginx bergantung pada kolam mana yang benar-benar jadi.
            if (kolam != null && kolam.Count > 0 && profile.PakaiWeb)
                SiapkanKolam(profile, kolam, apache, r, opcache);
            // Dipilah menurut web server yang DIPILIH profil, bukan menurut
            // paket mana yang kebetulan tersedia.
            //
            // Bentuk lamanya - "nginx kalau ada, kalau tidak Apache" - membuat
            // profil bernginx yang versinya dikosongkan diam-diam menghasilkan
            // konfigurasi APACHE. Dan kalau tidak ada keduanya, keluhannya
            // selalu menyebut Apache, walau yang dipilih profil itu Nginx.
            if (!profile.PakaiWeb)
            {
                // Sengaja tidak memakai web server. Tidak ada yang perlu
                // ditulis, dan tidak ada yang perlu dikeluhkan - profil yang
                // hanya menjalankan MySQL adalah pemakaian yang sah.
            }
            else if (profile.WebServer == "nginx")
            {
                if (nginx != null) r.NginxConf = WriteNginx(profile, nginx, php, sites, r, logAkses, berandaDiAkar);
                else r.Warnings.Add("Versi Nginx yang dicatat profil tidak ada di komputer ini, "
                                    + "dan tidak ada Nginx lain sebagai gantinya.");
            }
            else
            {
                if (apache != null)
                    r.HttpdConf = WriteApache(profile, apache, php, sites, r, logAkses, berandaDiAkar);
                else r.Warnings.Add("Versi Apache yang dicatat profil tidak ada di komputer ini, "
                                    + "dan tidak ada Apache lain sebagai gantinya.");
            }
            if (mysql != null) r.MyIni = WriteMyIni(profile, mysql, r);
            Beranda.Tulis(profile, sites, php, apache ?? nginx, mysql, s =>
            {
                foreach (var k in r.Kolam)
                    foreach (var x in k.Situs)
                        if (string.Equals(x.Path, s.Path, StringComparison.OrdinalIgnoreCase))
                            return k.Php.Version;
                return php != null ? php.Version : "";
            });
            return r;
        }

        // ---------------------------------------------------------------- Apache

        static string WriteApache(Profile profile, BinPackage apache, BinPackage php,
                                  List<Site> sites, Result r, bool logAkses,
                                  bool berandaDiAkar)
        {
            var baseConf = PristineConf(apache);
            if (baseConf == null)
            {
                r.Warnings.Add("httpd.conf bawaan tidak ditemukan di " + apache.Id + ".");
                return null;
            }

            var sb = new StringBuilder();
            var srvroot = Paths.Fwd(apache.Path);
            bool listenReplaced = false;
            foreach (var line in baseConf)
            {
                var t = line.TrimStart();
                if (t.StartsWith("Define SRVROOT", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("Define SRVROOT \"" + srvroot + "\"");
                    continue;
                }
                if (t.StartsWith("ServerRoot", StringComparison.OrdinalIgnoreCase))
                {
                    // Build lama menaruh jalur harfiah di ServerRoot, bukan ${SRVROOT}.
                    sb.AppendLine("ServerRoot \"" + srvroot + "\"");
                    continue;
                }
                if (t.StartsWith("Listen ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!listenReplaced) { sb.AppendLine("Listen " + profile.HttpPort); listenReplaced = true; }
                    continue;
                }
                // Include milik pengelola lain (mis. Laragon) dibuang: jalur mutlak
                // ke luar folder Apache pasti bukan milik kita.
                if (Regex.IsMatch(t, "^Include(Optional)?\\s+\"?[A-Za-z]:", RegexOptions.IgnoreCase)) continue;
                // DocumentRoot bawaan template dinonaktifkan. Phoron menuliskan
                // miliknya sendiri di blok penutup, jadi baris ini toh tidak
                // menentukan apa-apa - tapi Apache MEMERIKSA keberadaan foldernya
                // saat mengurai dan menolak start kalau tidak ada. XAMPP menaruh
                // htdocs di C:/xampp/htdocs, di LUAR folder apache-nya, sehingga
                // "${SRVROOT}/htdocs" bawaan menunjuk folder yang tidak pernah ada.
                if (t.StartsWith("DocumentRoot", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("# " + line + "   # dinonaktifkan Phoron - lihat blok di bawah");
                    continue;
                }
                sb.AppendLine(line);
            }
            if (!listenReplaced) sb.AppendLine("Listen " + profile.HttpPort);

            sb.AppendLine();
            sb.AppendLine("### ================= Phoron =================");
            sb.AppendLine("# Blok ini ditambahkan di akhir dengan sengaja: direktif Apache yang");
            sb.AppendLine("# muncul belakangan menimpa yang di atasnya, jadi bawaan vendor tidak");
            sb.AppendLine("# perlu diobrak-abrik sama sekali.");
            foreach (var mod in ApacheModules(php, r.Kolam.Count > 0))
            {
                var so = Path.Combine(apache.Path, "modules", "mod_" + mod + ".so");
                // LoadModule untuk berkas yang tidak ada = Apache gagal start total,
                // jadi tiap modul dicek dulu ada berkasnya.
                if (File.Exists(so)) sb.AppendLine("LoadModule " + mod + "_module modules/mod_" + mod + ".so");
            }

            var docRoot = Paths.Fwd(SiteScanner.DocumentRoot(profile));
            sb.AppendLine("DefaultRuntimeDir \"" + Paths.Fwd(Paths.Tmp) + "/\"");
            sb.AppendLine("PidFile \"" + Paths.Fwd(Path.Combine(Paths.Tmp, "httpd.pid")) + "\"");
            sb.AppendLine("ErrorLog \"" + Paths.Fwd(Path.Combine(Paths.Logs, "apache-error.log")) + "\"");
            // Log akses hanya ditulis kalau diminta. Log GALAT selalu menyala:
            // itulah yang menjelaskan kenapa sesuatu rusak, dan mematikannya
            // demi keringanan berarti menukar beberapa megabita dengan
            // kebutaan total saat ada masalah.
            if (logAkses)
                sb.AppendLine("CustomLog \"" + Paths.Fwd(Path.Combine(Paths.Logs, "apache-access.log")) + "\" common");
            else
                sb.AppendLine("# Log akses dimatikan (Pengaturan > Catat log rinci).");
            sb.AppendLine("ServerName localhost:" + profile.HttpPort);
            sb.AppendLine("ServerSignature Off");
            sb.AppendLine("DocumentRoot \"" + docRoot + "\"");
            sb.AppendLine("<Directory \"" + docRoot + "\">");
            sb.AppendLine("    Options Indexes FollowSymLinks ExecCGI");
            sb.AppendLine("    AllowOverride All");
            sb.AppendLine("    Require all granted");
            sb.AppendLine("</Directory>");
            sb.AppendLine("<IfModule dir_module>");
            sb.AppendLine("    DirectoryIndex index.php index.html index.htm");
            sb.AppendLine("</IfModule>");
            sb.AppendLine("AddDefaultCharset UTF-8");
            // Beranda Phoron dijangkau lewat alias, bukan dengan menaruh berkas
            // di folder proyek. Folder itu milik pengguna - sering kali www
            // milik Laragon yang sudah punya index.php sendiri - dan menimpanya
            // berarti menghapus pekerjaan orang. Dengan alias, /phoron selalu
            // ada apa pun isi folder proyeknya.
            var beranda = Paths.Fwd(Beranda.Folder);
            sb.AppendLine("Alias " + Beranda.Alias + " \"" + beranda + "\"");
            sb.AppendLine("<Directory \"" + beranda + "\">");
            sb.AppendLine("    Options FollowSymLinks ExecCGI");
            sb.AppendLine("    AllowOverride None");
            sb.AppendLine("    Require all granted");
            sb.AppendLine("    DirectoryIndex index.php");
            sb.AppendLine("</Directory>");

            // Pengalihan akar ke beranda Phoron TIDAK ditaruh di sini, melainkan
            // di dalam VirtualHost bawaan - lihat DefaultBlock. Aturan mod_rewrite
            // di konteks server tidak diwarisi VirtualHost mana pun kecuali
            // diminta dengan RewriteOptions Inherit, dan kegagalannya senyap:
            // konfigurasi tetap lolos httpd -t, Apache tetap menyala, aturannya
            // saja yang tidak pernah dipakai.
            sb.AppendLine("Include \"" + Paths.Fwd(Path.Combine(Paths.EtcApache, "mod_php.conf")) + "\"");
            sb.AppendLine("Include \"" + Paths.Fwd(Path.Combine(Paths.EtcApache, "ssl.conf")) + "\"");
            sb.AppendLine("IncludeOptional \"" + Paths.Fwd(Path.Combine(Paths.EtcApache, "alias")) + "/*.conf\"");
            sb.AppendLine("IncludeOptional \"" + Paths.Fwd(Paths.SitesEnabled) + "/*.conf\"");

            var confPath = Path.Combine(Paths.EtcApache, "httpd.conf");
            Directory.CreateDirectory(Path.Combine(Paths.EtcApache, "alias"));
            WriteIfChanged(confPath, sb.ToString());

            WriteModPhp(profile, apache, php, r);
            WriteSslConf(profile, apache, r);
            WriteVhosts(profile, sites, r, berandaDiAkar);
            return confPath;
        }

        /// <summary>Salinan pristine (conf\original) lebih disukai: conf\httpd.conf mungkin sudah diacak pengelola lain.</summary>
        static string[] PristineConf(BinPackage apache)
        {
            foreach (var rel in new[] { "conf\\original\\httpd.conf", "conf\\httpd.conf" })
            {
                var p = Path.Combine(apache.Path, rel);
                if (File.Exists(p)) return File.ReadAllLines(p);
            }
            return null;
        }

        /// <summary>
        /// ProxyFCGISetEnvIf baru ada sejak Apache 2.4.26. Menuliskannya ke
        /// Apache yang lebih tua bukan sekadar tidak berguna - direktif yang
        /// tidak dikenal membuat httpd MENOLAK START sama sekali, dan yang
        /// terlihat orang cuma "Apache tidak mau nyala".
        ///
        /// Versi yang tidak terbaca dianggap cukup baru: Apache yang dipaketkan
        /// Phoron sendiri jauh di atas 2.4.26, dan menolak menulisnya berarti
        /// PHP non-thread-safe pasti tidak jalan.
        /// </summary>
        static bool ProxyFcgiSetEnvIfAda(BinPackage apache)
        {
            if (apache == null) return true;
            var v = apache.Parsed;
            if (v == null || v.Major == 0) return true;
            return v >= new Version(2, 4, 26);
        }

        /// <summary>
        /// Apakah Apache harus melayani PHP ini lewat FastCGI - yaitu karena
        /// build-nya tidak membawa modul Apache.
        ///
        /// SATU-SATUNYA tempat keputusan ini diambil. Dulu ada dua syarat yang
        /// berbeda: modul proxy dimuat bila "!ThreadSafe", sedangkan jalur
        /// FastCGI dipakai bila "tidak ada DLL modul Apache". ThreadSafe sendiri
        /// ditebak dari nama folder bila DLL-nya tidak ada, jadi PHP NTS yang
        /// diekstrak ke folder tanpa kata "nts" - "php-8.3", misalnya - memakai
        /// jalur FastCGI TANPA modulnya, dan httpd menolak start dengan
        /// "Invalid command 'ProxyFCGISetEnvIf'".
        ///
        /// Yang menentukan memang DLL-nya: tanpa php*apache2_4.dll tidak ada
        /// apa pun yang bisa dimuat LoadModule, apa pun kata nama foldernya.
        /// </summary>
        public static bool PakaiFastCgi(BinPackage php)
        {
            return php != null && string.IsNullOrEmpty(php.ApacheModuleDll);
        }

        /// <summary>
        /// Baris ProxyFCGISetEnvIf yang membuang awalan "proxy:fcgi://host:port/"
        /// dari SCRIPT_FILENAME, menyisakan jalur berkas yang sudah dipetakan
        /// Apache - termasuk hasil Alias.
        ///
        /// Port apa pun (\d+): satu baris melayani php-cgi profil DAN setiap
        /// kolam PHP per situs sekaligus.
        /// </summary>
        public static string SetelNamaBerkasFcgi()
        {
            // String verbatim: titik di alamat IP harus sampai ke Apache sebagai
            // "\." - titik mentah di ungkapan reguler cocok dengan aksara apa pun.
            return @"ProxyFCGISetEnvIf ""reqenv('SCRIPT_FILENAME') =~ m#^proxy:fcgi://127\.0\.0\.1:\d+/(.*)$#"" SCRIPT_FILENAME ""$1""";
        }

        /// <summary>
        /// Tulis php.ini tiap kolam, beri port, dan simpan yang jadi ke r.Kolam.
        ///
        /// Tiap kolam memakai Result-nya SENDIRI untuk WritePhpIni. Result milik
        /// profil tidak boleh dipakai: WritePhpIni menaruh daftar ekstensi yang
        /// diambil alih ke Result.AdoptedExtensions, dan Engine menyimpannya ke
        /// PROFIL AKTIF - jadi daftar ekstensi PHP 8.3 akan masuk ke profil 5.6.
        /// </summary>
        static void SiapkanKolam(Profile profile, List<Kolam> kolam, BinPackage apache,
                                 Result r, bool opcache)
        {
            if (profile.WebServer != "nginx" && !ProxyFcgiSetEnvIfAda(apache))
            {
                r.Warnings.Add("Versi PHP per situs butuh Apache 2.4.26 ke atas; Apache "
                               + (apache != null ? apache.Version : "ini")
                               + " belum mengenal ProxyFCGISetEnvIf. Semua situs memakai PHP profil.");
                return;
            }
            int port = r.FastCgiPort + 1;
            foreach (var k in kolam)
            {
                if (k == null || k.Php == null || k.Situs.Count == 0) continue;
                if (!File.Exists(Path.Combine(k.Php.Path, "php-cgi.exe")))
                {
                    r.Warnings.Add("php-cgi.exe tidak ada di " + k.Php.Id + ", jadi "
                                   + string.Join(", ", k.Situs.Select(s => s.Folder))
                                   + " memakai PHP profil.");
                    continue;
                }
                var pk = new Profile
                {
                    Name = profile.Name + " - PHP " + k.Php.Version + " per situs",
                    PhpId = k.Php.NilaiSimpan,
                    PhpExtensions = new List<string>(k.Ekstensi),
                    PhpIniOverrides = new Dictionary<string, string>(k.Timpa, StringComparer.OrdinalIgnoreCase),
                };
                var rk = new Result();
                k.FolderIni = WritePhpIni(pk, k.Php, rk, false, opcache);
                foreach (var w in rk.Warnings) r.Warnings.Add("PHP " + k.Php.Version + " (per situs): " + w);
                k.Port = port++;
                r.Kolam.Add(k);
            }
        }

        /// <summary>Port kolam yang melayani situs ini, atau 0 bila ia ikut PHP profil.</summary>
        static int PortKolam(Result r, Site s)
        {
            if (s == null || s.Path == null) return 0;
            foreach (var k in r.Kolam)
                foreach (var x in k.Situs)
                    if (string.Equals(x.Path, s.Path, StringComparison.OrdinalIgnoreCase)) return k.Port;
            return 0;
        }

        /// <summary>
        /// Blok Apache untuk tiap situs yang dilayani kolam.
        ///
        /// Satu Directory per situs, di konteks server - jadi berlaku untuk akses
        /// lewat jalur (localhost/proyek/) dan diwarisi vhost proyek.test. SetHandler
        /// di dalamnya mengalahkan AddType milik mod_php; dibuktikan pada Apache
        /// 2.4.38 dengan PHP 5.6 sebagai modul dan php-cgi 8.3 di satu folder.
        ///
        /// CGIPassAuth: tanpa ini Apache TIDAK meneruskan header Authorization ke
        /// FastCGI, dan API dengan token Bearer menolak setiap permintaan. Lima
        /// dari enam proyek Laravel di mesin pengembang menanganinya sendiri di
        /// .htaccess; yang keenam tidak.
        /// </summary>
        static void TulisKolamApache(StringBuilder sb, Result r, bool setelSudahAda)
        {
            if (r.Kolam.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine("# --- Versi PHP per situs (FastCGI), berdampingan dengan PHP profil ---");
            foreach (var k in r.Kolam)
            {
                foreach (var s in k.Situs)
                {
                    sb.AppendLine("# " + s.Folder + " -> PHP " + k.Php.Version);
                    sb.AppendLine("<Directory \"" + Paths.Fwd(s.Path) + "\">");
                    sb.AppendLine("    CGIPassAuth On");
                    sb.AppendLine("    <FilesMatch \\.php$>");
                    sb.AppendLine("        SetHandler \"proxy:fcgi://127.0.0.1:" + k.Port + "/\"");
                    sb.AppendLine("    </FilesMatch>");
                    sb.AppendLine("</Directory>");
                }
            }
            if (!setelSudahAda) sb.AppendLine(SetelNamaBerkasFcgi());
        }

        static IEnumerable<string> ApacheModules(BinPackage php, bool adaKolam = false)
        {
            var mods = new List<string> { "rewrite", "deflate", "expires", "headers", "ssl", "socache_shmcb", "vhost_alias" };
            // FastCGI butuh mod_proxy + mod_proxy_fcgi. Syaratnya HARUS sama
            // persis dengan yang dipakai WriteModPhp - lihat PakaiFastCgi. Kolam
            // PHP per situs juga FastCGI, walau PHP profilnya mod_php.
            if (PakaiFastCgi(php) || adaKolam) { mods.Add("proxy"); mods.Add("proxy_fcgi"); }
            return mods;
        }

        static void WriteModPhp(Profile profile, BinPackage apache, BinPackage php, Result r)
        {
            var path = Path.Combine(Paths.EtcApache, "mod_php.conf");
            var sb = new StringBuilder();
            sb.AppendLine(GeneratedHeader);
            if (php == null)
            {
                sb.AppendLine("# Profil ini tidak memakai PHP.");
                TulisKolamApache(sb, r, false);
                WriteIfChanged(path, sb.ToString());
                return;
            }

            bool setelSudahAda = false;
            if (!PakaiFastCgi(php))
            {
                sb.AppendLine("LoadModule " + BinScanner.ApacheModuleName(php)
                              + " \"" + Paths.Fwd(php.ApacheModuleDll) + "\"");
                sb.AppendLine("PHPIniDir \"" + Paths.Fwd(r.PhpIniDir ?? php.Path) + "\"");
                sb.AppendLine("<IfModule mime_module>");
                sb.AppendLine("    AddType application/x-httpd-php .php");
                sb.AppendLine("    AddType application/x-httpd-php-source .phps");
                sb.AppendLine("</IfModule>");
            }
            else
            {
                // Jalur FastCGI. php-cgi.exe dijalankan ServiceManager sebagai
                // proses terpisah di port ini.
                r.PhpFastCgi = true;
                sb.AppendLine("# Build PHP ini NTS (tanpa modul Apache) - dilayani lewat FastCGI.");
                sb.AppendLine("#");
                sb.AppendLine("# DUA HAL DI BAWAH KHAS WINDOWS, DAN TANPA KEDUANYA TIDAK SATU PUN");
                sb.AppendLine("# BERKAS .php BISA DIBUKA.");
                sb.AppendLine("#");
                sb.AppendLine("# Garis miring di akhir alamat. Apache menyambung jalur berkas langsung");
                sb.AppendLine("# ke belakang alamat itu. Di Linux jalurnya diawali \"/\" sehingga yang");
                sb.AppendLine("# terbentuk sah; di Windows ia diawali \"C:\", jadi jadinya");
                sb.AppendLine("# \"fcgi://127.0.0.1:9123C:/...\" dan Apache menjawab 400 dengan");
                sb.AppendLine("# keterangan \"URI cannot be parsed\" yang tidak menyebut PHP sama sekali.");
                sb.AppendLine("#");
                sb.AppendLine("# SCRIPT_FILENAME yang disetel sendiri. Dengan garis miring tadi, Apache");
                sb.AppendLine("# mengirim SELURUH alamat proxy sebagai nama berkas yang harus dijalankan");
                sb.AppendLine("# PHP - \"proxy:fcgi://127.0.0.1:9123/C:/...\" - dan php-cgi menjawab");
                sb.AppendLine("# \"No input file specified\" untuk setiap permintaan.");
                sb.AppendLine("#");
                sb.AppendLine("# Awalan proxy-nya DIKUPAS dari jalur yang sudah dipetakan Apache, bukan");
                sb.AppendLine("# dirakit ulang dari DOCUMENT_ROOT + REQUEST_URI. Rakitan itu salah untuk");
                sb.AppendLine("# setiap Alias - termasuk /phoron, yang dilayani dari etc/dashboard dan");
                sb.AppendLine("# bukan dari folder proyek - dan pernah membuat beranda Phoron sendiri");
                sb.AppendLine("# menjawab \"No input file specified\".");
                sb.AppendLine("<FilesMatch \\.php$>");
                sb.AppendLine("    SetHandler \"proxy:fcgi://127.0.0.1:" + r.FastCgiPort + "/\"");
                sb.AppendLine("</FilesMatch>");
                if (ProxyFcgiSetEnvIfAda(apache))
                {
                    sb.AppendLine(SetelNamaBerkasFcgi());
                    setelSudahAda = true;
                }
                else
                    r.Warnings.Add("Apache " + (apache != null ? apache.Version : "ini")
                        + " belum mengenal ProxyFCGISetEnvIf (ada sejak 2.4.26), jadi PHP "
                        + "non-thread-safe tidak bisa dilayani lewat FastCGI di Windows. "
                        + "Pilih build PHP thread-safe, atau Apache yang lebih baru.");
                if (!File.Exists(Path.Combine(php.Path, "php-cgi.exe")))
                    r.Warnings.Add("php-cgi.exe tidak ada di " + php.Id + "; PHP tidak akan jalan lewat Apache.");
            }
            TulisKolamApache(sb, r, setelSudahAda);
            WriteIfChanged(path, sb.ToString());
        }

        static void WriteSslConf(Profile profile, BinPackage apache, Result r)
        {
            var crt = Path.Combine(Paths.EtcSsl, "phoron.crt");
            var key = Path.Combine(Paths.EtcSsl, "phoron.key");
            var sb = new StringBuilder();
            sb.AppendLine(GeneratedHeader);
            sb.AppendLine("<IfModule ssl_module>");
            if (File.Exists(crt) && File.Exists(key))
            {
                r.SslEnabled = true;
                sb.AppendLine("    Listen " + profile.HttpsPort);
                sb.AppendLine("    SSLCipherSuite HIGH:MEDIUM:!MD5:!RC4:!3DES");
                sb.AppendLine("    SSLProtocol all -SSLv3");
                sb.AppendLine("    SSLSessionCache \"shmcb:" + Paths.Fwd(Path.Combine(Paths.Tmp, "ssl_scache")) + "(512000)\"");
                sb.AppendLine("    SSLCertificateFile \"" + Paths.Fwd(crt) + "\"");
                sb.AppendLine("    SSLCertificateKeyFile \"" + Paths.Fwd(key) + "\"");
            }
            else
            {
                // Tanpa sertifikat, "Listen 443" tetap membuka port tapi tiap
                // permintaan HTTPS gagal - lebih baik port-nya tidak dibuka.
                sb.AppendLine("    # Sertifikat belum dibuat; HTTPS dimatikan.");
            }
            sb.AppendLine("</IfModule>");
            WriteIfChanged(Path.Combine(Paths.EtcApache, "ssl.conf"), sb.ToString());
        }

        static void WriteVhosts(Profile profile, List<Site> sites, Result r, bool berandaDiAkar)
        {
            Directory.CreateDirectory(Paths.SitesEnabled);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Syaratnya HARUS sama dengan WriteSslConf: sertifikat DAN kuncinya.
            // Dulu di sini cukup .crt saja, jadi .key yang hilang - terhapus,
            // pembuatan openssl yang gagal di tengah - membuat vhost menunjuk
            // SSLCertificateKeyFile yang tidak ada, dan Apache menolak start
            // sementara ssl.conf sendiri sudah dengan benar mematikan HTTPS.
            bool ssl = SslTool.Exists;

            WriteDefaultVhost(profile, ssl, berandaDiAkar);

            foreach (var s in sites ?? new List<Site>())
            {
                var file = SiteScanner.VhostPath(s);
                wanted.Add(Path.GetFileName(file));
                var root = Paths.Fwd(s.DocRoot ?? s.Path);
                var sb = new StringBuilder();
                sb.AppendLine(GeneratedHeader);
                sb.AppendLine("define ROOT \"" + root + "\"");
                sb.AppendLine("define SITE \"" + s.HostName + "\"");
                sb.AppendLine();
                sb.AppendLine(VhostBlock(profile.HttpPort, false));
                if (ssl) sb.AppendLine(VhostBlock(profile.HttpsPort, true));
                WriteIfChanged(file, sb.ToString());
            }

            // Vhost otomatis milik folder yang sudah dihapus harus ikut hilang,
            // kalau tidak Apache menolak start karena DocumentRoot tidak ada.
            foreach (var f in Directory.GetFiles(Paths.SitesEnabled, "auto.*.conf"))
                if (!wanted.Contains(Path.GetFileName(f)))
                    try { File.Delete(f); CatatBerubah(f); } catch { }
        }

        /// <summary>
        /// VirtualHost bawaan untuk permintaan yang tidak cocok dengan nama situs
        /// mana pun - http://localhost dan http://127.0.0.1.
        ///
        /// Wajib ada, dan namanya wajib diurutkan paling awal: Apache memakai
        /// VirtualHost PERTAMA sebagai jawaban baku, jadi tanpa berkas ini
        /// localhost akan dilayani situs yang kebetulan pertama menurut abjad -
        /// bukan folder proyek utama. Gejalanya membingungkan (localhost tiba-tiba
        /// menampilkan isi salah satu proyek) dan baru muncul setelah situs
        /// pertama dibuat, jadi mudah disangka kesalahan lain.
        /// </summary>
        static void WriteDefaultVhost(Profile profile, bool ssl, bool berandaDiAkar)
        {
            var root = Paths.Fwd(SiteScanner.DocumentRoot(profile));
            var sb = new StringBuilder();
            sb.AppendLine(GeneratedHeader);
            sb.AppendLine("define ROOT \"" + root + "\"");
            sb.AppendLine();
            sb.AppendLine(DefaultBlock(profile.HttpPort, false, berandaDiAkar));
            if (ssl) sb.AppendLine(DefaultBlock(profile.HttpsPort, true, berandaDiAkar));
            // Awalan "000-" menjamin urutannya sebelum semua berkas "auto.*.conf".
            WriteIfChanged(Path.Combine(Paths.SitesEnabled, "000-default.conf"), sb.ToString());
        }

        static string DefaultBlock(int port, bool ssl, bool berandaDiAkar)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<VirtualHost _default_:" + port + ">");
            sb.AppendLine("    DocumentRoot \"${ROOT}\"");
            sb.AppendLine("    ServerName localhost");
            if (berandaDiAkar)
            {
                // Polanya mengikat alamat akar PERSIS. DirectoryIndex tidak bisa
                // dipakai di sini: ia berlaku untuk SETIAP folder di bawahnya,
                // jadi menaruh beranda di urutan pertama akan membajak /simpdam/
                // dan semua subfolder lain; menaruhnya di urutan terakhir hanya
                // berlaku kalau foldernya tidak punya index sendiri - padahal
                // justru folder yang SUDAH punya index.php yang jadi pangkal
                // pertanyaannya. index.php milik folder proyek tetap terjangkau
                // di /index.php.
                sb.AppendLine("    <IfModule rewrite_module>");
                sb.AppendLine("        RewriteEngine On");
                sb.AppendLine("        RewriteRule \"^/?$\" \"" + Beranda.Alias + "/index.php\" [PT,L]");
                sb.AppendLine("    </IfModule>");
            }
            sb.AppendLine("    <Directory \"${ROOT}\">");
            sb.AppendLine("        Options Indexes FollowSymLinks ExecCGI");
            sb.AppendLine("        AllowOverride All");
            sb.AppendLine("        Require all granted");
            sb.AppendLine("    </Directory>");
            if (ssl)
            {
                sb.AppendLine("    SSLEngine on");
                sb.AppendLine("    SSLCertificateFile \"" + Paths.Fwd(Path.Combine(Paths.EtcSsl, "phoron.crt")) + "\"");
                sb.AppendLine("    SSLCertificateKeyFile \"" + Paths.Fwd(Path.Combine(Paths.EtcSsl, "phoron.key")) + "\"");
            }
            sb.AppendLine("</VirtualHost>");
            return sb.ToString();
        }

        static string VhostBlock(int port, bool ssl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<VirtualHost *:" + port + ">");
            sb.AppendLine("    DocumentRoot \"${ROOT}\"");
            sb.AppendLine("    ServerName ${SITE}");
            sb.AppendLine("    ServerAlias *.${SITE}");
            sb.AppendLine("    <Directory \"${ROOT}\">");
            sb.AppendLine("        Options Indexes FollowSymLinks ExecCGI");
            sb.AppendLine("        AllowOverride All");
            sb.AppendLine("        Require all granted");
            sb.AppendLine("    </Directory>");
            if (ssl)
            {
                sb.AppendLine("    SSLEngine on");
                sb.AppendLine("    SSLCertificateFile \"" + Paths.Fwd(Path.Combine(Paths.EtcSsl, "phoron.crt")) + "\"");
                sb.AppendLine("    SSLCertificateKeyFile \"" + Paths.Fwd(Path.Combine(Paths.EtcSsl, "phoron.key")) + "\"");
            }
            sb.AppendLine("</VirtualHost>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ PHP

        /// <summary>
        /// Menulis php.ini milik profil ke etc\php\&lt;versi&gt;\php.ini dan mengembalikan
        /// foldernya (dipakai PHPIniDir). php.ini di dalam folder bin tidak disentuh.
        /// </summary>
        /// <summary>
        /// Folder tempat php.ini profil ini ditulis - SATU-SATUNYA tempat hal ini
        /// diputuskan. PHPRC untuk php-cgi, php.exe di terminal Phoron, dan
        /// halaman Ekstensi semuanya harus menunjuk ke sini.
        ///
        /// Dulu PHPRC selalu menunjuk etc\php\&lt;versi&gt;, walau sakelar "Tulis
        /// php.ini ke dalam folder PHP" menyala. Berkas lama dari sebelum sakelar
        /// itu dinyalakan masih tertinggal di sana, dan php-cgi - Nginx, atau
        /// Apache dengan PHP NTS - serta terminal membacanya lebih dulu: ekstensi
        /// dan opcache yang diubah tidak pernah sampai ke mereka.
        /// </summary>
        public static string FolderPhpIni(BinPackage php, bool keFolderPhp)
        {
            // Kunci, bukan Id: dua PHP XAMPP sama-sama bernama "php" dan dulu
            // berbagi satu php.ini. Paket yang selama ini dipakai tetap di
            // folder lamanya - kuncinya Id polos.
            return keFolderPhp ? php.Path : Path.Combine(Paths.Etc, "php", php.Kunci);
        }

        public static string WritePhpIni(Profile profile, BinPackage php, Result r,
                                         bool keFolderPhp = false, bool opcache = true)
        {
            var dir = FolderPhpIni(php, keFolderPhp);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "php.ini");

            string cadangan = null;
            if (keFolderPhp)
            {
                // php.ini asli disalin sekali sebelum ditimpa. Tanpa ini, setelan
                // yang sudah ditulis pengguna (atau Laragon) lenyap tanpa jejak
                // pada penulisan pertama - dan tidak ada cara mengembalikannya.
                cadangan = Path.Combine(php.Path, "php.ini.sebelum-phoron");
                try
                {
                    if (File.Exists(target) && !File.Exists(cadangan)) File.Copy(target, cadangan);
                }
                catch (Exception ex) { r.Warnings.Add("Gagal mencadangkan php.ini asli: " + ex.Message); }

                if (!EhUnderRoot(php.Path, Paths.Bin))
                    r.Warnings.Add("php.ini ditulis ke " + php.Path + ", folder yang TIDAK milik Phoron. "
                                   + "Pengelola lain (mis. Laragon) memakai berkas yang sama.");
            }

            var baseText = PhpIniTemplate(php, cadangan);
            if (baseText == null)
            {
                r.Warnings.Add("Tidak ada php.ini contoh di " + php.Id + "; dibuat dari nol.");
                baseText = "";
            }

            var lines = baseText.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var extDir = Paths.Fwd(Path.Combine(php.Path, "ext"));

            var set = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "extension_dir", "\"" + extDir + "\"" },
                { "error_log", "\"" + Paths.Fwd(Path.Combine(Paths.Logs, "php-error.log")) + "\"" },
                { "upload_tmp_dir", "\"" + Paths.Fwd(Paths.Tmp) + "\"" },
                { "sys_temp_dir", "\"" + Paths.Fwd(Paths.Tmp) + "\"" },
                { "session.save_path", "\"" + Paths.Fwd(Paths.Tmp) + "\"" },
                { "date.timezone", "Asia/Makassar" },
                { "display_errors", "On" },
                { "log_errors", "On" },
            };
            var cacert = Path.Combine(Paths.EtcSsl, "cacert.pem");
            if (File.Exists(cacert))
            {
                set["curl.cainfo"] = "\"" + Paths.Fwd(cacert) + "\"";
                set["openssl.cafile"] = "\"" + Paths.Fwd(cacert) + "\"";
            }
            if (opcache && AdaOpcache(php)) foreach (var kv in SetelanOpcache) set[kv.Key] = kv.Value;
            // Ditaruh SESUDAH setelan opcache: profil tetap berhak menimpanya.
            foreach (var kv in profile.PhpIniOverrides) set[kv.Key] = kv.Value;

            var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].TrimStart();
                // Baris ekstensi bawaan dinonaktifkan seluruhnya; daftar yang
                // berlaku hanya yang di profil, supaya switch profil benar-benar
                // menentukan keadaan akhir dan bukan menumpuk.
                if (Regex.IsMatch(t, "^;?\\s*(zend_)?extension\\s*=", RegexOptions.IgnoreCase))
                {
                    if (!t.StartsWith(";")) lines[i] = ";" + lines[i];
                    continue;
                }
                var m = Regex.Match(t, "^;?\\s*([A-Za-z0-9_.]+)\\s*=");
                if (!m.Success) continue;
                var key = m.Groups[1].Value;
                string val;
                if (!set.TryGetValue(key, out val)) continue;
                // Kunci yang sama muncul berkali-kali di php.ini contoh (sekali per
                // seksi); hanya kemunculan pertama yang disetel, sisanya dimatikan
                // supaya tidak ada nilai belakangan yang menimpa diam-diam.
                if (!applied.Add(key))
                {
                    if (!t.StartsWith(";")) lines[i] = ";" + lines[i];
                    continue;
                }
                lines[i] = key + " = " + val;
            }

            var sb = new StringBuilder();
            sb.AppendLine("; php.ini untuk profil \"" + profile.Name + "\" - dibuat otomatis oleh Phoron.");
            sb.AppendLine("; Sunting lewat Phoron; berkas ini ditulis ulang tiap kali profil dipakai.");
            sb.AppendLine(string.Join(Environment.NewLine, lines));
            sb.AppendLine();
            sb.AppendLine("; --- disetel Phoron ---");
            foreach (var kv in set)
                if (!applied.Contains(kv.Key)) sb.AppendLine(kv.Key + " = " + kv.Value);

            // Profil yang belum punya daftar ekstensi mengambil alih daftar dari
            // php.ini dasar. Tanpa ini, php.ini hasil mewarisi seluruh setelan
            // tapi TIDAK satu pun ekstensinya - dan aplikasi yang selama ini
            // jalan mati dengan "Call to undefined function mb_strlen()" yang,
            // kalau galatnya disembunyikan aplikasi, hanya berwujud halaman putih.
            var daftarExt = profile.PhpExtensions;
            if (daftarExt.Count == 0)
            {
                var tersedia = new HashSet<string>(AvailableExtensions(php), StringComparer.OrdinalIgnoreCase);
                var dariDasar = EkstensiAktif(baseText).Where(tersedia.Contains).ToList();
                // php.ini dasar yang tidak mengaktifkan apa pun (PHP yang baru
                // diunduh) tetap harus menghasilkan profil yang bisa dipakai.
                if (dariDasar.Count == 0)
                    dariDasar = BakuDisarankan.Where(tersedia.Contains).ToList();
                if (dariDasar.Count > 0)
                {
                    daftarExt = dariDasar;
                    r.AdoptedExtensions = dariDasar;
                }
            }

            // Daftar ekstensi di profil adalah keadaan komputer tempat profil
            // dibuat. Di komputer lain, build PHP-nya bisa tidak membawa DLL
            // yang sama - dan menulis baris extension untuk DLL yang tidak ada
            // membuat SETIAP permintaan halaman diawali "PHP Startup: Unable to
            // load dynamic library", termasuk di php -m dan di browser.
            //
            // Yang dilewati TIDAK dihapus dari profil: dibawa kembali ke
            // komputer asalnya, daftarnya harus utuh seperti semula.
            // oci8 dan pdo_oci butuh Oracle Instant Client, yang TIDAK ikut dalam
            // paket PHP. Menulisnya ke php.ini saat client-nya tidak ada atau
            // salah arsitektur membuat SETIAP permintaan halaman diawali
            // peringatan Windows yang tidak menyebut Oracle sama sekali - dan
            // ekstensinya toh tetap tidak termuat. Jadi dilewati saja, dengan
            // penjelasan yang menyebut apa yang sebenarnya kurang.
            var oracle = Oracle.Periksa(php);
            if (!oracle.Layak)
            {
                var kena = daftarExt.Where(Oracle.AdalahEkstensiOracle).ToList();
                if (kena.Count > 0)
                {
                    daftarExt = daftarExt.Where(x => !Oracle.AdalahEkstensiOracle(x)).ToList();
                    r.Warnings.Add(string.Join(", ", kena) + " tidak ditulis ke php.ini. "
                                   + oracle.Pesan);
                }
            }

            var adaDll = new HashSet<string>(AvailableExtensions(php), StringComparer.OrdinalIgnoreCase);
            if (adaDll.Count > 0)
            {
                var lewat = daftarExt.Where(x => !adaDll.Contains(x)).ToList();
                if (lewat.Count > 0)
                {
                    daftarExt = daftarExt.Where(adaDll.Contains).ToList();
                    r.Warnings.Add("Ekstensi ini tidak ada DLL-nya di " + php.Id
                        + ", jadi tidak ditulis ke php.ini: " + string.Join(", ", lewat)
                        + ". Daftar di profil dibiarkan utuh.");
                }
            }

            sb.AppendLine();
            sb.AppendLine("; --- ekstensi profil ---");
            foreach (var ext in daftarExt.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var dll = Path.Combine(php.Path, "ext", "php_" + ext + ".dll");
                if (!File.Exists(dll))
                {
                    r.Warnings.Add("Ekstensi " + ext + " tidak ada di " + php.Id + " - dilewati.");
                    continue;
                }
                var directive = EkstensiZend.Contains(ext) ? "zend_extension" : "extension";
                sb.AppendLine(directive + " = " + ExtensionValue(php, ext));
            }

            // opcache dimuat TERPISAH dari daftar profil. Kalau ia diperlakukan
            // sebagai ekstensi biasa, hanya profil yang dibuat sesudah hari ini
            // yang memuatnya - sedangkan profil yang sudah ada milik orang tetap
            // mengurai ulang seluruh kerangka kerjanya tiap permintaan, dan tidak
            // ada di layar yang menjelaskan kenapa.
            bool sudahDidaftar = daftarExt.Any(x => x.Equals("opcache", StringComparison.OrdinalIgnoreCase));
            if (opcache && !sudahDidaftar && AdaOpcache(php))
            {
                sb.AppendLine();
                sb.AppendLine("; --- opcache (setelan Phoron, bukan daftar ekstensi profil) ---");
                sb.AppendLine("zend_extension = " + ExtensionValue(php, "opcache"));
            }

            WriteIfChanged(target, sb.ToString());
            return dir;
        }

        /// <summary>
        /// PHP 7.2 ke atas menerima nama ekstensi telanjang; sebelum itu nilainya
        /// harus nama berkas DLL lengkap. Menulis bentuk yang salah membuat PHP
        /// diam-diam tidak memuat ekstensinya.
        /// </summary>
        public static string ExtensionValue(BinPackage php, string ext)
        {
            var v = php.Parsed;
            bool modern = v.Major > 7 || (v.Major == 7 && v.Minor >= 2);
            return modern ? ext : "php_" + ext + ".dll";
        }

        /// <summary>Penanda di baris awal berkas yang dihasilkan Phoron sendiri.</summary>
        const string PenandaBuatanPhoron = "dibuat otomatis oleh Phoron";

        /// <summary>
        /// php.ini contoh yang jadi dasar, menurut urutan kepercayaan:
        ///
        ///   1. php.ini.sebelum-phoron  - salinan asli yang Phoron simpan sendiri
        ///   2. php.ini                 - yang SELAMA INI dipakai di folder itu
        ///   3. php.ini-development / -production - bawaan vendor
        ///
        /// php.ini yang sudah ada didahulukan di atas bawaan vendor dengan
        /// sengaja. Folder PHP sering dipinjam dari pengelola lain yang sudah
        /// menyetelnya bertahun-tahun; memulai dari bawaan vendor berarti
        /// setelan seperti short_open_tag diam-diam kembali ke Off, dan proyek
        /// yang tadinya jalan rusak dengan galat yang jejaknya tidak menunjuk
        /// ke sini sama sekali. Ini bukan hipotesis: CodeIgniter beralih ke
        /// jalur eval() saat short_open_tag mati, dan view-nya gagal diurai.
        /// </summary>
        static string PhpIniTemplate(BinPackage php, string cadangan = null)
        {
            if (cadangan != null && File.Exists(cadangan)) return File.ReadAllText(cadangan);

            var asli = Path.Combine(php.Path, "php.ini.sebelum-phoron");
            if (File.Exists(asli)) return File.ReadAllText(asli);

            foreach (var name in new[] { "php.ini", "php.ini-development", "php.ini-production" })
            {
                var p = Path.Combine(php.Path, name);
                if (!File.Exists(p)) continue;
                var teks = File.ReadAllText(p);
                // php.ini yang ternyata keluaran Phoron sendiri dilewati: memakai
                // keluaran sebagai dasar membuat berkasnya menumpuk tiap penulisan.
                if (name == "php.ini" && teks.Length > 0
                    && teks.Substring(0, Math.Min(300, teks.Length)).Contains(PenandaBuatanPhoron))
                    continue;
                return teks;
            }
            return null;
        }

        /// <summary>Apakah sebuah folder berada di dalam folder lain.</summary>
        static bool EhUnderRoot(string path, string root)
        {
            try
            {
                var a = Path.GetFullPath(path).TrimEnd('\\') + "\\";
                var b = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Ekstensi yang AKTIF (tidak dikomentari) di sebuah teks php.ini, dalam
        /// nama tanpa awalan php_ dan tanpa akhiran .dll - bentuk yang dipakai
        /// profil. Urutannya dipertahankan: di PHP, ekstensi tertentu harus
        /// dimuat setelah ekstensi yang jadi sandarannya (exif butuh mbstring).
        /// </summary>
        public static List<string> EkstensiAktif(string iniText)
        {
            var hasil = new List<string>();
            if (string.IsNullOrEmpty(iniText)) return hasil;
            var rx = new Regex(@"^\s*(?:zend_)?extension\s*=\s*""?([^"";\r\n]+)",
                               RegexOptions.IgnoreCase);
            foreach (var baris in iniText.Split('\n'))
            {
                var t = baris.TrimStart();
                if (t.StartsWith(";")) continue;
                var m = rx.Match(t);
                if (!m.Success) continue;
                var nama = m.Groups[1].Value.Trim();
                if (nama.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    nama = nama.Substring(0, nama.Length - 4);
                if (nama.StartsWith("php_", StringComparison.OrdinalIgnoreCase))
                    nama = nama.Substring(4);
                nama = nama.ToLowerInvariant();
                if (nama.Length > 0 && !hasil.Contains(nama)) hasil.Add(nama);
            }
            return hasil;
        }

        /// <summary>
        /// Ekstensi yang aktif di php.ini dasar milik sebuah paket PHP - yaitu
        /// keadaan yang benar-benar berlaku sebelum Phoron ikut campur. Dipakai
        /// tombol "Ambil dari php.ini asli" di halaman Ekstensi PHP.
        /// </summary>
        public static List<string> EkstensiDariPhpIniDasar(BinPackage php)
        {
            if (php == null) return new List<string>();
            return EkstensiAktif(PhpIniTemplate(php));
        }

        /// <summary>
        /// Ekstensi yang HARUS dimuat lewat zend_extension, bukan extension.
        ///
        /// Keduanya menempel ke mesin Zend sendiri, bukan sekadar menambah
        /// fungsi. Dimuat dengan "extension =", xdebug menolak dan mencetak
        /// "Xdebug MUST be loaded as a Zend extension" di awal SETIAP
        /// permintaan - dan tetap tidak termuat. Pernah terjadi: hanya opcache
        /// yang dikenali, jadi xdebug yang dicentang atau diambil alih dari
        /// php.ini Laragon selalu ditulis dengan cara yang salah.
        /// </summary>
        static readonly HashSet<string> EkstensiZend =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "opcache", "xdebug" };

        /// <summary>Apakah build PHP ini membawa php_opcache.dll.</summary>
        public static bool AdaOpcache(BinPackage php)
        {
            if (php == null || string.IsNullOrEmpty(php.Path)) return false;
            try { return File.Exists(Path.Combine(php.Path, Path.Combine("ext", "php_opcache.dll"))); }
            catch { return false; }
        }

        /// <summary>
        /// Setelan opcache yang dipakai Phoron.
        ///
        /// DUA BARIS TERAKHIR ADALAH SYARATNYA, BUKAN HIASAN. Bawaan PHP
        /// memeriksa ulang stempel waktu berkas paling sering sekali tiap dua
        /// detik. Artinya berkas yang baru disimpan bisa belum berlaku saat
        /// halamannya dimuat ulang - orang menyimpan, menyegarkan, dan melihat
        /// kode lamanya. Dari situlah datangnya keluhan "opcache merusak
        /// lingkungan ngoding saya".
        ///
        /// Dengan revalidate_freq = 0, PHP memeriksa stempel waktu pada SETIAP
        /// permintaan: simpan, muat ulang, langsung berlaku. Yang dihemat tetap
        /// utuh, sebab yang mahal bukan memeriksa tanggal satu berkas melainkan
        /// mengurai ulang isinya.
        ///
        /// Menyetel salah satu dari keduanya ke nilai lain - demi angka tolok
        /// ukur yang lebih cantik - akan menukar beberapa milidetik dengan
        /// kebingungan yang sungguhan. Ada uji yang menjaganya.
        /// </summary>
        static readonly Dictionary<string, string> SetelanOpcache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "opcache.enable", "1" },
                // CLI dibiarkan mati: umurnya sependek satu perintah, jadi
                // singgahannya tidak pernah sempat dipakai lagi.
                { "opcache.enable_cli", "0" },
                { "opcache.memory_consumption", "128" },
                { "opcache.interned_strings_buffer", "8" },
                { "opcache.max_accelerated_files", "10000" },
                { "opcache.validate_timestamps", "1" },
                { "opcache.revalidate_freq", "0" },
            };

        /// <summary>
        /// Daftar baku untuk build PHP yang php.ini-nya belum mengaktifkan apa
        /// pun - yaitu PHP yang baru diunduh, yang hanya membawa
        /// php.ini-development dengan semua ekstensi dikomentari. Tanpa daftar
        /// ini, profil baru lahir tanpa satu pun ekstensi dan aplikasi apa pun
        /// langsung mati di pemanggilan fungsi pertama.
        ///
        /// Urutannya penting: exif menyandarkan diri pada mbstring, jadi harus
        /// dimuat sesudahnya. gd2 (PHP 5/7) dan gd (PHP 8) dua-duanya disebut;
        /// yang tidak punya DLL-nya tersaring sendiri.
        /// </summary>
        static readonly string[] BakuDisarankan =
        {
            "curl", "fileinfo", "openssl", "mbstring", "exif", "intl",
            "gd2", "gd", "mysqli", "pdo_mysql", "pdo_sqlite", "sqlite3", "zip",
        };

        /// <summary>
        /// Ekstensi yang sepatutnya dipakai profil baru: apa yang sudah aktif di
        /// php.ini paket itu, atau - kalau tidak ada - daftar baku yang masuk akal.
        /// Selalu disaring ke DLL yang benar-benar ada di build tersebut.
        /// </summary>
        public static List<string> EkstensiDisarankan(BinPackage php)
        {
            if (php == null) return new List<string>();
            var dariIni = EkstensiDariPhpIniDasar(php);
            var calon = dariIni.Count > 0 ? dariIni : new List<string>(BakuDisarankan);
            var tersedia = new HashSet<string>(AvailableExtensions(php), StringComparer.OrdinalIgnoreCase);
            return calon.Where(tersedia.Contains).ToList();
        }

        /// <summary>Daftar ekstensi yang tersedia di sebuah build PHP (nama tanpa awalan php_).</summary>
        public static List<string> AvailableExtensions(BinPackage php)
        {
            if (php == null) return new List<string>();
            var dir = Path.Combine(php.Path, "ext");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "php_*.dll")
                // Pola "*.dll" di Windows JUGA cocok dengan php_oci8_12c.dllaaa:
                // FindFirstFile masih mencocokkan nama pendek 8.3, jadi akhiran
                // apa pun setelah .dll ikut terjaring. Berkas semacam itu justru
                // sengaja dinonaktifkan orang dengan mengganti namanya - kalau
                // ikut terdaftar, daftarnya berisi entri kembar yang tidak bisa
                // dipakai sama sekali.
                .Where(f => string.Equals(Path.GetExtension(f), ".dll", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring(4).ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---------------------------------------------------------------- MySQL

        public static string WriteMyIni(Profile profile, BinPackage mysql, Result r)
        {
            var dataDir = MySqlDataDir(mysql);
            var path = Path.Combine(Paths.EtcMysql, "my.ini");
            var sb = new StringBuilder();
            sb.AppendLine("# Dibuat otomatis oleh Phoron untuk profil \"" + profile.Name + "\".");
            sb.AppendLine("[client]");
            sb.AppendLine("port=" + profile.MySqlPort);
            sb.AppendLine("default-character-set=utf8mb4");
            sb.AppendLine();
            sb.AppendLine("[mysqld]");
            sb.AppendLine("port=" + profile.MySqlPort);
            sb.AppendLine("basedir=\"" + Paths.Fwd(mysql.Path) + "\"");
            sb.AppendLine("datadir=\"" + Paths.Fwd(dataDir) + "\"");
            sb.AppendLine("tmpdir=\"" + Paths.Fwd(Paths.Tmp) + "\"");
            sb.AppendLine("log-error=\"" + Paths.Fwd(Path.Combine(Paths.Logs, "mysql-error.log")) + "\"");
            sb.AppendLine("character-set-server=utf8mb4");
            sb.AppendLine("collation-server=utf8mb4_general_ci");
            sb.AppendLine("max_allowed_packet=64M");
            sb.AppendLine("# sql_mode dilonggarkan: banyak proyek PHP lama menulis kolom");
            sb.AppendLine("# tanggal '0000-00-00' yang ditolak mode ketat bawaan MySQL 5.7+.");
            sb.AppendLine("sql_mode=\"NO_ENGINE_SUBSTITUTION\"");
            WriteIfChanged(path, sb.ToString());
            return path;
        }

        /// <summary>Tiap versi database punya folder data sendiri - tabel sistem MySQL 5.7 dan 8.0 tidak saling baca.</summary>
        /// <summary>
        /// Folder data MySQL untuk paket ini. TIDAK PERNAH dipakai bersama paket lain.
        ///
        /// Dulu cukup data\&lt;nama folder&gt;. Kedua MariaDB XAMPP bernama "mysql" -
        /// 10.1.36 dan 10.1.38 - jadi keduanya menulis ke satu folder data, dan
        /// dua versi di satu folder data adalah jalan menuju basis data rusak.
        ///
        /// Urutannya:
        ///   1. folder yang TERCATAT milik paket ini (penanda .phoron-pemilik);
        ///   2. folder kuncinya, selama belum dimiliki paket lain - untuk paket
        ///      yang selama ini dipakai, itu folder lamanya sendiri;
        ///   3. kalau folder kuncinya ternyata milik paket lain (urutan folder bin
        ///      berubah), folder bertanda asal yang hanya bisa jadi miliknya.
        /// Tidak ada data yang dipindah atau dihapus.
        ///
        /// Paket yang PINDAH TEMPAT (folder bin dipindah, huruf drive berubah,
        /// Phoron dibawa ke komputer lain) tetap memakai folder lamanya. Dulu
        /// penanda berjalur lama membuat folder itu dianggap milik paket lain,
        /// dan mysqld dinyalakan di atas folder baru yang kosong: seluruh basis
        /// data tampak hilang. Pengambilalihan itu hanya untuk paket yang namanya
        /// UNIK - di antara paket kembar, jalur lama yang hilang tidak
        /// menjelaskan kembaran yang mana yang pindah.
        /// </summary>
        public static string MySqlDataDir(BinPackage mysql)
        {
            var polos = Path.Combine(Paths.Data, mysql.Id);
            var bertanda = Path.Combine(Paths.Data, mysql.Id + "@" + BinScanner.TandaAsal(mysql.SourceRoot));
            foreach (var d in new[] { polos, bertanda })
                if (SamaJalur(PemilikData(d), mysql.Path)) return d;
            if (!mysql.Kembar)
            {
                // Folder bertanda asal hanya bisa milik paket dari folder bin ini.
                if (Directory.Exists(Path.Combine(bertanda, "mysql"))) return bertanda;
                if (PemilikPindah(PemilikData(polos))) return polos;
            }
            var kunci = Path.Combine(Paths.Data, mysql.Kunci);
            if (PemilikData(kunci) == null) return kunci;
            return bertanda;
        }

        const string BerkasPemilik = ".phoron-pemilik";

        /// <summary>Jalur paket MySQL pemilik folder data ini, atau null bila belum tercatat.</summary>
        public static string PemilikData(string folderData)
        {
            try
            {
                var f = Path.Combine(folderData, BerkasPemilik);
                if (!File.Exists(f)) return null;
                var isi = File.ReadAllText(f).Trim();
                return isi.Length > 0 ? isi : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Catat pemilik folder data - sekali saja, saat mysqld sudah jalan di
        /// atasnya. Berkas di akar folder data tidak dianggap basis data oleh
        /// MySQL maupun MariaDB; yang dianggap basis data hanya subfolder.
        /// </summary>
        public static void CatatPemilikData(string folderData, BinPackage mysql)
        {
            try
            {
                if (mysql == null || !Directory.Exists(folderData)) return;
                var f = Path.Combine(folderData, BerkasPemilik);
                var lama = PemilikData(folderData);
                // Ditulis sekali; diperbarui hanya bila paket unik ini pindah
                // tempat - lihat MySqlDataDir.
                if (lama == null || (!mysql.Kembar && !SamaJalur(lama, mysql.Path) && PemilikPindah(lama)))
                    File.WriteAllText(f, mysql.Path);
            }
            catch { /* penanda penjaga, bukan syarat jalan */ }
        }

        /// <summary>Pemilik tercatat sudah tidak ada di disk (paketnya dipindah atau dihapus).</summary>
        static bool PemilikPindah(string pemilik)
        {
            if (pemilik == null) return true;
            try { return !Directory.Exists(pemilik); } catch { return true; }
        }

        /// <summary>
        /// Dua jalur menunjuk folder yang sama, apa pun penulisannya: "/" atau
        /// "\", huruf besar atau kecil, garis miring di ujung, atau nama pendek
        /// 8.3 (C:\PROGRA~1). Penanda .phoron-pemilik ditulis dengan jalur
        /// sebagaimana bin_roots mengejanya, jadi ejaan lain untuk folder yang
        /// sama tidak boleh dianggap paket lain.
        /// </summary>
        static bool SamaJalur(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Normalkan(a), Normalkan(b), StringComparison.OrdinalIgnoreCase);
        }

        static string Normalkan(string jalur)
        {
            var j = jalur.Trim().Replace('/', '\\');
            try { j = Path.GetFullPath(j); } catch { }
            j = j.TrimEnd('\\');
            try
            {
                if (Directory.Exists(j))
                {
                    var sb = new StringBuilder(1024);
                    if (GetLongPathName(j, sb, sb.Capacity) > 0) j = sb.ToString().TrimEnd('\\');
                }
            }
            catch { }
            return j;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetShortPathName(string panjang, StringBuilder pendek, int ukuran);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetLongPathName(string pendek, StringBuilder panjang, int ukuran);

        /// <summary>Nama pendek 8.3 dari jalur yang ada, atau null.</summary>
        public static string NamaPendek(string jalur)
        {
            try
            {
                var sb = new StringBuilder(1024);
                return GetShortPathName(jalur, sb, sb.Capacity) > 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Nginx

        /// <summary>
        /// Jalur fastcgi_params yang PASTI ada.
        ///
        /// Ditulis sebagai nama telanjang, nginx mencarinya relatif ke folder
        /// berkas konfigurasi - yaitu etc\nginx, tempat Phoron tidak pernah
        /// menaruhnya - lalu menolak start seluruhnya dengan
        /// "CreateFile() ... fastcgi_params failed (2)". mime.types di baris
        /// atasnya sudah benar memakai jalur penuh; yang ini terlewat.
        ///
        /// Paket nginx membawa berkas itu di conf-nya sendiri, jadi itulah yang
        /// dipakai. Kalau paketnya tidak membawa, Phoron menuliskan yang minimal
        /// ke etc\nginx supaya profil Nginx tidak pernah lagi gagal karena satu
        /// berkas pendukung yang hilang.
        /// </summary>
        static string PastikanFastcgiParams(BinPackage nginx, string dirEtc)
        {
            if (nginx != null)
            {
                var bawaan = Path.Combine(nginx.Path, "conf", "fastcgi_params");
                if (File.Exists(bawaan)) return bawaan;
            }
            var milikKita = Path.Combine(dirEtc, "fastcgi_params");
            WriteIfChanged(milikKita, FastcgiParamsBaku());
            return milikKita;
        }

        /// <summary>Isi fastcgi_params baku nginx.</summary>
        static string FastcgiParamsBaku()
        {
            var b = new StringBuilder();
            b.AppendLine("# Dibuat otomatis oleh Phoron karena paket nginx tidak membawanya.");
            b.AppendLine("fastcgi_param  QUERY_STRING       $query_string;");
            b.AppendLine("fastcgi_param  REQUEST_METHOD     $request_method;");
            b.AppendLine("fastcgi_param  CONTENT_TYPE       $content_type;");
            b.AppendLine("fastcgi_param  CONTENT_LENGTH     $content_length;");
            b.AppendLine("fastcgi_param  SCRIPT_NAME        $fastcgi_script_name;");
            b.AppendLine("fastcgi_param  REQUEST_URI        $request_uri;");
            b.AppendLine("fastcgi_param  DOCUMENT_URI       $document_uri;");
            b.AppendLine("fastcgi_param  DOCUMENT_ROOT      $document_root;");
            b.AppendLine("fastcgi_param  SERVER_PROTOCOL    $server_protocol;");
            b.AppendLine("fastcgi_param  REQUEST_SCHEME     $scheme;");
            b.AppendLine("fastcgi_param  GATEWAY_INTERFACE  CGI/1.1;");
            b.AppendLine("fastcgi_param  SERVER_SOFTWARE    nginx/$nginx_version;");
            b.AppendLine("fastcgi_param  REMOTE_ADDR        $remote_addr;");
            b.AppendLine("fastcgi_param  REMOTE_PORT        $remote_port;");
            b.AppendLine("fastcgi_param  SERVER_ADDR        $server_addr;");
            b.AppendLine("fastcgi_param  SERVER_PORT        $server_port;");
            b.AppendLine("fastcgi_param  SERVER_NAME        $server_name;");
            b.AppendLine("fastcgi_param  REDIRECT_STATUS    200;");
            return b.ToString();
        }

        /// <summary>
        /// Ukuran ember hash nama server: pangkat dua terkecil yang memuat nama
        /// terpanjang berikut ruang lebih untuk keperluan dalam nginx sendiri.
        /// Dimulai dari 64, bukan 32, karena 32 adalah nilai baku yang justru
        /// bikin gagal begitu ada satu nama panjang.
        /// </summary>
        public static int EmberHash(IEnumerable<string> namaSitus)
        {
            int terpanjang = 0;
            foreach (var n in namaSitus ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(n) && n.Length > terpanjang) terpanjang = n.Length;

            var ember = 64;
            while (ember < terpanjang + 8 && ember < 1024) ember *= 2;
            return ember;
        }

        /// <summary>Kapasitas tabel hash; tumbuh mengikuti banyaknya situs.</summary>
        public static int MaksHash(int jumlahSitus)
        {
            var maks = 512;
            while (maks < jumlahSitus * 8 && maks < 16384) maks *= 2;
            return maks;
        }

        static string WriteNginx(Profile profile, BinPackage nginx, BinPackage php,
                                 List<Site> sites, Result r, bool logAkses = true,
                                 bool berandaDiAkar = true)
        {
            if (php == null || !File.Exists(Path.Combine(php.Path, "php-cgi.exe")))
                r.Warnings.Add("Nginx melayani PHP lewat FastCGI; php-cgi.exe tidak ditemukan.");
            r.PhpFastCgi = true;

            var dir = Paths.EtcNginx;
            Directory.CreateDirectory(dir);
            var docRoot = Paths.Fwd(SiteScanner.DocumentRoot(profile));
            var fcgiParams = Paths.Fwd(PastikanFastcgiParams(nginx, dir));
            var sb = new StringBuilder();
            sb.AppendLine("# Dibuat otomatis oleh Phoron.");
            sb.AppendLine("worker_processes  1;");
            sb.AppendLine("error_log \"" + Paths.Fwd(Path.Combine(Paths.Logs, "nginx-error.log")) + "\";");
            sb.AppendLine("pid \"" + Paths.Fwd(Path.Combine(Paths.Tmp, "nginx.pid")) + "\";");
            sb.AppendLine("events { worker_connections 1024; }");
            sb.AppendLine("http {");
            // Baku nginx untuk ember hash nama server cuma 32 karakter, dan
            // nama seperti "bandarmasih-mobile-pm-service.test" (34) sudah
            // melewatinya - nginx lalu MENOLAK SELURUH konfigurasi dengan
            // "could not build server_names_hash". Ukurannya dihitung dari
            // nama terpanjang yang benar-benar ada, bukan ditebak.
            var namaSitus = new List<string> { "localhost" };
            foreach (var st in sites ?? new List<Site>()) namaSitus.Add(st.HostName ?? "");
            sb.AppendLine("    server_names_hash_bucket_size " + EmberHash(namaSitus) + ";");
            sb.AppendLine("    server_names_hash_max_size " + MaksHash(namaSitus.Count) + ";");
            sb.AppendLine("    include \"" + Paths.Fwd(Path.Combine(nginx.Path, "conf", "mime.types")) + "\";");
            sb.AppendLine("    default_type application/octet-stream;");
            sb.AppendLine("    sendfile on;");
            sb.AppendLine("    client_max_body_size 128m;");
            // Dua sakelar di bawah dulu hanya dihormati Apache: dengan Nginx, log
            // akses tetap ditulis walau "Catat log rinci" mati, dan beranda
            // Phoron tidak pernah muncul di http://localhost/ walau sakelarnya
            // menyala. Perilakunya kini sama untuk kedua web server.
            if (logAkses)
                sb.AppendLine("    access_log \"" + Paths.Fwd(Path.Combine(Paths.Logs, "nginx-access.log")) + "\";");
            else
                sb.AppendLine("    access_log off;   # Pengaturan > Catat log rinci");
            sb.AppendLine("    client_body_temp_path \"" + Paths.Fwd(Path.Combine(Paths.Tmp, "nginx-body")) + "\";");
            sb.AppendLine("    proxy_temp_path \"" + Paths.Fwd(Path.Combine(Paths.Tmp, "nginx-proxy")) + "\";");
            sb.AppendLine("    fastcgi_temp_path \"" + Paths.Fwd(Path.Combine(Paths.Tmp, "nginx-fcgi")) + "\";");
            // Beranda di akar hanya untuk server bawaan, sama seperti Apache yang
            // hanya menaruhnya di vhost _default_: situs .test milik proyek
            // tetap menampilkan index-nya sendiri.
            sb.Append(NginxServer(profile.HttpPort, "localhost", docRoot, r.FastCgiPort, fcgiParams, berandaDiAkar,
                                  LokasiKolamNginx(r, SiteScanner.DocumentRoot(profile), fcgiParams)));
            foreach (var s in sites ?? new List<Site>())
            {
                // Situs yang memakai versi PHP sendiri diarahkan ke kolamnya.
                var portSitus = PortKolam(r, s);
                sb.Append(NginxServer(profile.HttpPort, s.HostName, Paths.Fwd(s.DocRoot ?? s.Path),
                                      portSitus > 0 ? portSitus : r.FastCgiPort, fcgiParams, false));
            }
            sb.AppendLine("}");
            var path = Path.Combine(dir, "nginx.conf");
            WriteIfChanged(path, sb.ToString());
            return path;
        }

        /// <summary>
        /// Untuk akses lewat jalur (localhost/proyek/): situs di folder proyek
        /// UTAMA yang memakai kolam dapat location-nya sendiri. Situs di folder
        /// proyek lain memang tidak terjangkau lewat jalur, jadi tidak diberi.
        /// </summary>
        static string LokasiKolamNginx(Result r, string akarUtama, string fcgiParams)
        {
            if (r.Kolam.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var k in r.Kolam)
                foreach (var s in k.Situs)
                {
                    if (!string.Equals((s.Root ?? "").TrimEnd('\\'), (akarUtama ?? "").TrimEnd('\\'),
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    sb.AppendLine("        location ^~ /" + s.Folder + "/ {   # PHP " + k.Php.Version);
                    sb.AppendLine("            location ~ \\.php$ {");
                    sb.AppendLine("                fastcgi_pass   127.0.0.1:" + k.Port + ";");
                    sb.AppendLine("                fastcgi_param  SCRIPT_FILENAME $document_root$fastcgi_script_name;");
                    sb.AppendLine("                include        \"" + fcgiParams + "\";");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                }
            return sb.ToString();
        }

        static string NginxServer(int port, string name, string root, int fcgiPort, string fcgiParams,
                                  bool berandaDiAkar = false, string lokasiTambahan = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine("    server {");
            sb.AppendLine("        listen       " + port + ";");
            sb.AppendLine("        server_name  " + name + ";");
            sb.AppendLine("        root         \"" + root + "\";");
            sb.AppendLine("        index        index.php index.html index.htm;");
            if (berandaDiAkar)
                // "location = /" hanya cocok dengan alamat akar PERSIS - setara
                // RewriteRule "^/?$" di Apache. /simpdam/ dan subfolder lain
                // tidak tersentuh. "last" mengulang pencarian location dengan
                // alamat baru, jadi yang melayaninya blok beranda di bawah.
                sb.AppendLine("        location = / { rewrite ^ " + Beranda.Alias + "/index.php last; }");
            // Prefiks ^~ didahulukan di atas "location ~ \.php$" di bawah, jadi
            // berkas PHP situs itu pergi ke kolamnya, bukan ke PHP profil.
            if (!string.IsNullOrEmpty(lokasiTambahan)) sb.Append(lokasiTambahan);
            sb.AppendLine("        location / { try_files $uri $uri/ /index.php?$query_string; }");
            // Setara Alias di Apache - lihat catatan di sana.
            sb.AppendLine("        location " + Beranda.Alias + "/ {");
            sb.AppendLine("            alias \"" + Paths.Fwd(Beranda.Folder) + "/\";");
            sb.AppendLine("            index index.php;");
            sb.AppendLine("            location ~ \\.php$ {");
            sb.AppendLine("                fastcgi_pass   127.0.0.1:" + fcgiPort + ";");
            sb.AppendLine("                fastcgi_param  SCRIPT_FILENAME $request_filename;");
            sb.AppendLine("                include        \"" + fcgiParams + "\";");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        location ~ \\.php$ {");
            sb.AppendLine("            fastcgi_pass   127.0.0.1:" + fcgiPort + ";");
            sb.AppendLine("            fastcgi_index  index.php;");
            sb.AppendLine("            fastcgi_param  SCRIPT_FILENAME $document_root$fastcgi_script_name;");
            sb.AppendLine("            include        \"" + fcgiParams + "\";");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            return sb.ToString();
        }

        // --------------------------------------------------------------- Utilitas

        /// <summary>
        /// Tulis hanya kalau isinya berubah. Menyentuh berkas konfigurasi tanpa
        /// perubahan isi memicu editor dan pengawas berkas tanpa alasan, dan
        /// mengaburkan jejak "kapan konfigurasi ini terakhir berubah".
        /// </summary>
        // Berapa kali konfigurasi yang dibaca server BENAR-BENAR berubah di
        // cakram, dipisah untuk web server dan MySQL. Engine mencatat nilainya
        // saat layanan menyala; nilai yang bergeser sesudahnya berarti server
        // yang sedang jalan masih memakai konfigurasi lama.
        //
        // Dulu tidak ada yang tahu. Sakelar Virtual Host, folder proyek, situs
        // baru - semuanya memanggil Apply, berkasnya berubah, dan Apache tetap
        // melayani keadaan lama tanpa satu pun tanda. "Situs siap di toko.test"
        // lalu dijawab vhost bawaan.
        static int _versiWeb, _versiDb;
        public static int VersiKonfigWeb { get { return System.Threading.Volatile.Read(ref _versiWeb); } }
        public static int VersiKonfigDb { get { return System.Threading.Volatile.Read(ref _versiDb); } }

        /// <summary>
        /// Berkas yang dibaca web server berubah di luar WriteIfChanged -
        /// misalnya sertifikat SSL yang dibuat ulang. httpd memuat sertifikat
        /// hanya saat menyala, jadi tanpa ini Apache terus menyajikan yang lama
        /// dan tidak ada tanda perlu dinyalakan ulang.
        /// </summary>
        public static void TandaiWebBerubah()
        {
            System.Threading.Interlocked.Increment(ref _versiWeb);
        }

        static void CatatBerubah(string path)
        {
            // Beranda Phoron berupa berkas PHP biasa: berlaku pada permintaan
            // berikutnya tanpa menyalakan ulang apa pun.
            try
            {
                var beranda = Path.GetFullPath(Beranda.Folder).TrimEnd('\\') + "\\";
                if (Path.GetFullPath(path).StartsWith(beranda, StringComparison.OrdinalIgnoreCase)) return;
            }
            catch { }
            if (string.Equals(Path.GetFileName(path), "my.ini", StringComparison.OrdinalIgnoreCase))
                System.Threading.Interlocked.Increment(ref _versiDb);
            else
                System.Threading.Interlocked.Increment(ref _versiWeb);
        }

        public static bool WriteIfChanged(string path, string content)
        {
            content = content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            // Pembacaan pembanding boleh gagal - berkasnya bisa sedang dipegang
            // httpd. Anggap saja berubah, lalu tulis; lebih baik menulis sekali
            // tanpa perlu daripada menggagalkan seluruh Apply().
            try { if (File.Exists(path) && File.ReadAllText(path) == content) return false; }
            catch { }
            AtomicFile.WriteAllText(path, content, new UTF8Encoding(false));
            CatatBerubah(path);
            return true;
        }
    }
}
