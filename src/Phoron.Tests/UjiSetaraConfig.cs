using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Bahan uji setara fitur "tulis konfigurasi" (daemon\inti\src\config.rs).
    ///
    /// Paketnya dari folder bin SUNGGUHAN di mesin ini (hanya DIBACA), ditambah
    /// paket palsu di dalam sandbox untuk kasus yang menulis ke folder PHP.
    /// Tiap kasus disiapkan di masuk\&lt;kasus&gt;, disalin ke kerja\&lt;kasus&gt;,
    /// lalu ConfigWriter.Build dijalankan di sana dan hasilnya dipindah ke
    /// csharp\&lt;kasus&gt;. Uji Rust mengulang langkah yang sama di jalur kerja
    /// yang SAMA, jadi seluruh berkas harus identik byte demi byte.
    ///
    /// Jalankan: powershell -ExecutionPolicy Bypass -File daemon\uji_setara_modul.ps1 config
    /// </summary>
    internal static class UjiSetara_config
    {
        class KolamUji
        {
            public string Php;          // kunci paket
            public string Ekstensi;     // "*" = EkstensiDisarankan
            public string Timpa = "";   // k=v;k=v
            public string Situs;        // nama folder situs, dipisah koma
        }

        class Kasus
        {
            public string Nama;
            public Profile Profil;
            public string Php = "", Apache = "", Nginx = "", MySql = "";
            public bool KeFolderPhp, LogAkses = true, Beranda = true, Opcache = true, Jaringan;
            public int Ulang = 1;
            public List<KolamUji> Kolam = new List<KolamUji>();
            public Action<string, string> Siapkan;   // (masuk, kerja)
        }

        static string _akar;
        static readonly List<BinPackage> Paket = new List<BinPackage>();
        static readonly List<string> BarisPaket = new List<string>();

        public static int Jalankan(string akar)
        {
            _akar = akar;
            if (Directory.Exists(akar)) Directory.Delete(akar, true);
            Directory.CreateDirectory(akar);
            var duluRoot = Paths.Root;
            var duluHosts = Paths.HostsFile;
            try
            {
                var roots = new List<string>();
                if (Directory.Exists(@"C:\Phoron\bin")) roots.Add(@"C:\Phoron\bin");
                roots.AddRange(Deteksi.FolderBinTerpasang());
                foreach (var p in BinScanner.ScanAll(roots)) TambahPaket(p);

                var hosts = Path.Combine(akar, "hosts-kosong");
                File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
                Paths.HostsFile = hosts;

                var daftar = SusunKasus();
                var kasusTxt = new StringBuilder();
                foreach (var k in daftar) Jalankan(k, kasusTxt);
                File.WriteAllLines(Path.Combine(akar, "paket.txt"), BarisPaket, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(akar, "kasus.txt"), kasusTxt.ToString(), new UTF8Encoding(false));
                Console.WriteLine("uji setara config: " + daftar.Count + " kasus, " + Paket.Count + " paket, di " + akar);
            }
            finally
            {
                Paths.Root = duluRoot;
                Paths.HostsFile = duluHosts;
            }
            return 0;
        }

        static void TambahPaket(BinPackage p)
        {
            Paket.Add(p);
            BarisPaket.Add(string.Join("|", p.Kind, p.Id, p.Path, p.Version, p.Compiler, p.Arch,
                p.ThreadSafe ? "TS" : "NTS", p.MainExe ?? "", p.ApacheModuleDll ?? "", p.SourceRoot,
                p.Kunci, p.Kembar ? "kembar" : ""));
        }

        static BinPackage Cari(BinKind kind, Func<BinPackage, bool> syarat)
        {
            return Paket.FirstOrDefault(p => p.Kind == kind && syarat(p));
        }

        static string Kunci(BinPackage p) { return p != null ? p.Kunci : ""; }

        static void Tulis(string path, string isi)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, isi, new UTF8Encoding(false));
        }

        static void Proyek(string masuk)
        {
            var www = Path.Combine(masuk, "www");
            foreach (var f in new[] { "blog", "my project", @"laravel\public", "o'brien", "Kafé", @"aset\public" })
                Directory.CreateDirectory(Path.Combine(www, f));
            File.WriteAllText(Path.Combine(www, @"laravel\public\index.php"), "<?php echo 1;");
        }

        static Profile ProfilBaru(string nama, string kerja)
        {
            var p = new Profile { Name = nama, FileName = ProfileStore.Slug(nama) };
            p.ProjectRoots.Add(Path.Combine(kerja, "www"));
            return p;
        }

        /// <summary>PHP palsu di dalam sandbox: cukup berkas yang diperiksa ConfigWriter.</summary>
        static BinPackage PhpPalsu(string dirMasuk, string dirKerja, string versi, bool ts, string[] ext,
                                   string phpIni, string iniDev)
        {
            foreach (var e in ext) Tulis(Path.Combine(dirMasuk, "ext", "php_" + e + ".dll"), "");
            Tulis(Path.Combine(dirMasuk, "ext", "php_mysqli.dllaaa"), "");
            Tulis(Path.Combine(dirMasuk, "php-cgi.exe"), "");
            Tulis(Path.Combine(dirMasuk, "php.exe"), "");
            if (phpIni != null) Tulis(Path.Combine(dirMasuk, "php.ini"), phpIni);
            if (iniDev != null) Tulis(Path.Combine(dirMasuk, "php.ini-development"), iniDev);
            var p = new BinPackage
            {
                Kind = BinKind.Php, Id = Path.GetFileName(dirKerja), Path = dirKerja, Version = versi,
                Compiler = "vs16", Arch = "x64", ThreadSafe = ts, MainExe = Path.Combine(dirKerja, "php.exe"),
                ApacheModuleDll = "", SourceRoot = Path.GetDirectoryName(dirKerja),
            };
            if (ts)
            {
                Tulis(Path.Combine(dirMasuk, "php7apache2_4.dll"), "");
                p.ApacheModuleDll = Path.Combine(dirKerja, "php7apache2_4.dll");
            }
            TambahPaket(p);
            return p;
        }

        static string IniDev(BinPackage nyata)
        {
            if (nyata != null)
                foreach (var n in new[] { "php.ini-development", "php.ini-production" })
                {
                    var f = Path.Combine(nyata.Path, n);
                    if (File.Exists(f)) return File.ReadAllText(f);
                }
            return "[PHP]\r\nshort_open_tag = Off\r\nmemory_limit = 128M\r\n;extension=mbstring\r\n[Date]\r\n;date.timezone =\r\n";
        }

        static List<Kasus> SusunKasus()
        {
            var daftar = new List<Kasus>();
            var php8 = Cari(BinKind.Php, p => p.Parsed.Major == 8 && !string.IsNullOrEmpty(p.ApacheModuleDll));
            var php7 = Cari(BinKind.Php, p => p.Parsed.Major == 7);
            var php5 = Cari(BinKind.Php, p => p.Parsed.Major == 5);
            var phpXampp = Cari(BinKind.Php, p => p.Id.Equals("php", StringComparison.OrdinalIgnoreCase));
            var apaches = Paket.Where(p => p.Kind == BinKind.Apache).ToList();
            var nginx = Cari(BinKind.Nginx, p => true);
            var mysql = ProfileStore.PickMySql(Paket.Where(p => p.Kind == BinKind.MySql));
            var mysqlLain = Cari(BinKind.MySql, p => p != mysql);
            var apacheUntuk = new Func<BinPackage, BinPackage>(php => ProfileStore.PickApache(php, apaches));

            // 1. Apache + PHP 8 lengkap: SSL, cacert, penimpaan, ekstensi, kolam PHP 5 dan 7.
            if (php8 != null && apacheUntuk(php8) != null)
            {
                var k = new Kasus { Nama = "apache-php8", Php = Kunci(php8), Apache = Kunci(apacheUntuk(php8)), MySql = Kunci(mysql) };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    foreach (var f in new[] { "phoron.crt", "phoron.key", "phoron-ca.crt", "cacert.pem" })
                        Tulis(Path.Combine(masuk, "etc", "ssl", f), "contoh");
                    Tulis(Path.Combine(masuk, @"etc\apache2\sites-enabled\auto.sudah-dihapus.test.conf"), "lama");
                    Tulis(Path.Combine(masuk, @"etc\apache2\sites-enabled\milik-tangan.conf"), "# tangan");
                    if (mysql != null)
                        Tulis(Path.Combine(masuk, "data", mysql.Id, ".phoron-pemilik"), @"Z:\pindah\" + mysql.Id);
                    k.Profil = ProfilBaru("PHP 8 ✓ Lengkap", kerja);
                    k.Profil.HttpPort = 8080;
                    k.Profil.HttpsPort = 8443;
                    k.Profil.MySqlPort = 3307;
                    k.Profil.PhpExtensions.AddRange(new[] { "mbstring", "curl", "openssl", "pdo_mysql", "xdebug", "tidak_ada", "oci8_12c", "opcache" });
                    k.Profil.PhpIniOverrides["Memory_Limit"] = "512M";
                    k.Profil.PhpIniOverrides["opcache.revalidate_freq"] = "2";
                    k.Profil.PhpIniOverrides["upload_max_filesize"] = "64M";
                };
                if (php5 != null) k.Kolam.Add(new KolamUji { Php = Kunci(php5), Ekstensi = "*", Timpa = "memory_limit=1G", Situs = "blog,my project" });
                if (php7 != null) k.Kolam.Add(new KolamUji { Php = Kunci(php7), Ekstensi = "mbstring,gd", Situs = "o'brien" });
                daftar.Add(k);
            }

            // 2. Apache + PHP 5 tanpa daftar ekstensi (diambil alih), sakelar dimatikan, tanpa MySQL.
            if (php5 != null && apacheUntuk(php5) != null)
            {
                var k = new Kasus { Nama = "apache-php5", Php = Kunci(php5), Apache = Kunci(apacheUntuk(php5)), LogAkses = false, Beranda = false, Opcache = false, Jaringan = true };
                k.Siapkan = (masuk, kerja) => { Proyek(masuk); k.Profil = ProfilBaru("PHP 5 lama", kerja); };
                daftar.Add(k);
            }

            // 3. PHP 7 (atau XAMPP) dengan Apache-nya, MySQL lain, dua folder proyek.
            var phpTiga = phpXampp ?? php7;
            if (phpTiga != null && apacheUntuk(phpTiga) != null)
            {
                var k = new Kasus { Nama = "apache-php7", Php = Kunci(phpTiga), Apache = Kunci(apacheUntuk(phpTiga)), MySql = Kunci(mysqlLain ?? mysql) };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    Directory.CreateDirectory(Path.Combine(masuk, @"proyek2\api"));
                    k.Profil = ProfilBaru("PHP 7", kerja);
                    k.Profil.ProjectRoots.Add(Path.Combine(kerja, "proyek2"));
                    k.Profil.SiteSuffix = "local";
                    k.Profil.PhpExtensions.AddRange(new[] { "mbstring", "gd", "mysqli" });
                };
                daftar.Add(k);
            }

            // 4. Nginx + PHP 8 dengan kolam PHP 7.
            if (nginx != null && (php8 ?? php7) != null)
            {
                var php = php8 ?? php7;
                var k = new Kasus { Nama = "nginx-php", Php = Kunci(php), Nginx = Kunci(nginx), MySql = Kunci(mysql) };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    k.Profil = ProfilBaru("Nginx", kerja);
                    k.Profil.WebServer = "nginx";
                    k.Profil.NginxId = nginx.Id;
                    k.Profil.PhpExtensions.AddRange(new[] { "mbstring", "curl" });
                };
                if (php7 != null && php7 != php) k.Kolam.Add(new KolamUji { Php = Kunci(php7), Ekstensi = "*", Situs = "my project,laravel" });
                daftar.Add(k);
            }

            // 5. Nginx palsu tanpa fastcgi_params, tanpa PHP, log akses mati.
            {
                var k = new Kasus { Nama = "nginx-palsu", LogAkses = false, Jaringan = true };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    Tulis(Path.Combine(masuk, @"bin\nginx-1.99.0\nginx.exe"), "");
                    var n = new BinPackage
                    {
                        Kind = BinKind.Nginx, Id = "nginx-1.99.0", Path = Path.Combine(kerja, @"bin\nginx-1.99.0"),
                        Version = "1.99.0", MainExe = Path.Combine(kerja, @"bin\nginx-1.99.0\nginx.exe"),
                        SourceRoot = Path.Combine(kerja, "bin"),
                    };
                    TambahPaket(n);
                    k.Nginx = n.Kunci;
                    k.Profil = ProfilBaru("Nginx palsu", kerja);
                    k.Profil.WebServer = "nginx";
                    k.Profil.NginxId = n.Id;
                };
                daftar.Add(k);
            }

            // 6. Hanya MySQL: profil tanpa web server.
            if (mysql != null)
            {
                var k = new Kasus { Nama = "hanya-mysql", MySql = Kunci(mysql), Apache = Kunci(apaches.FirstOrDefault()) };
                k.Siapkan = (masuk, kerja) =>
                {
                    k.Profil = ProfilBaru("Hanya DB", kerja);
                    Tulis(Path.Combine(masuk, "data", mysql.Id + "@" + BinScanner.TandaAsal(mysql.SourceRoot), "mysql", "user.frm"), "");
                };
                daftar.Add(k);
            }

            // 7. php.ini ke folder PHP (palsu, di bawah bin sandbox), dua kali berturut-turut.
            if (apaches.Count > 0)
            {
                var ap = apaches[0];
                var k = new Kasus { Nama = "ke-folder-php", Apache = Kunci(ap), KeFolderPhp = true, Ulang = 2 };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    var php = PhpPalsu(Path.Combine(masuk, @"bin\php-8.2.0-nts"), Path.Combine(kerja, @"bin\php-8.2.0-nts"),
                        "8.2.0", false, new[] { "mbstring", "curl", "opcache", "xdebug", "gd" },
                        "; milik pengguna\r\nshort_open_tag = On\r\nextension=mbstring\r\nzend_extension=xdebug\r\nmemory_limit=256M\r\n",
                        IniDev(php8));
                    k.Php = php.Kunci;
                    k.Profil = ProfilBaru("Ke folder PHP", kerja);
                };
                daftar.Add(k);
            }

            // 8. PHP palsu di LUAR bin Phoron tanpa php.ini contoh (dibuat dari nol), PHP 7.1.
            if (apaches.Count > 0)
            {
                var k = new Kasus { Nama = "luar-bin", Apache = Kunci(apaches[apaches.Count - 1]), KeFolderPhp = true };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    var php = PhpPalsu(Path.Combine(masuk, @"luar\php-7.1.0"), Path.Combine(kerja, @"luar\php-7.1.0"),
                        "7.1.0", true, new[] { "mbstring", "gd2" }, null, null);
                    k.Php = php.Kunci;
                    k.Profil = ProfilBaru("Luar bin", kerja);
                };
                daftar.Add(k);
            }

            // 9. Apache palsu 2.4.23 (conf asli disalin) dengan kolam: ditolak dengan peringatan.
            if (apaches.Count > 0 && (php8 ?? php7) != null && php5 != null)
            {
                var asli = apaches[0];
                var k = new Kasus { Nama = "apache-tua", Php = Kunci(php8 ?? php7) };
                k.Siapkan = (masuk, kerja) =>
                {
                    Proyek(masuk);
                    var dir = Path.Combine(masuk, @"bin\httpd-2.4.23");
                    var sumber = Path.Combine(asli.Path, @"conf\httpd.conf");
                    Tulis(Path.Combine(dir, @"conf\httpd.conf"), File.Exists(sumber) ? File.ReadAllText(sumber) : "Listen 80\r\n");
                    foreach (var m in new[] { "rewrite", "ssl", "proxy", "proxy_fcgi", "headers" })
                        Tulis(Path.Combine(dir, "modules", "mod_" + m + ".so"), "");
                    var a = new BinPackage
                    {
                        Kind = BinKind.Apache, Id = "httpd-2.4.23", Path = Path.Combine(kerja, @"bin\httpd-2.4.23"),
                        Version = "2.4.23", MainExe = Path.Combine(kerja, @"bin\httpd-2.4.23\bin\httpd.exe"),
                        SourceRoot = Path.Combine(kerja, "bin"),
                    };
                    TambahPaket(a);
                    k.Apache = a.Kunci;
                    k.Profil = ProfilBaru("Apache tua", kerja);
                };
                k.Kolam.Add(new KolamUji { Php = Kunci(php5), Ekstensi = "*", Situs = "blog" });
                daftar.Add(k);
            }

            // 10. Profil memilih Apache yang tidak ada.
            if (php7 != null)
            {
                var k = new Kasus { Nama = "apache-hilang", Php = Kunci(php7) };
                k.Siapkan = (masuk, kerja) => { k.Profil = ProfilBaru("Apache hilang", kerja); k.Profil.ApacheId = "httpd-tidak-ada"; };
                daftar.Add(k);
            }
            return daftar;
        }

        static BinPackage Paket_(string kunci)
        {
            if (string.IsNullOrEmpty(kunci)) return null;
            return Paket.First(p => p.Kunci == kunci);
        }

        static void Jalankan(Kasus k, StringBuilder kasusTxt)
        {
            var masuk = Path.Combine(_akar, "masuk", k.Nama);
            var kerja = Path.Combine(_akar, "kerja", k.Nama);
            Directory.CreateDirectory(masuk);
            k.Siapkan(masuk, kerja);
            if (string.IsNullOrEmpty(k.Profil.ApacheId) && !string.IsNullOrEmpty(k.Apache) && k.Nama != "hanya-mysql")
                k.Profil.ApacheId = Paket_(k.Apache).NilaiSimpan;
            Paths.Root = masuk;
            ProfileStore.Save(k.Profil);

            kasusTxt.AppendLine("kasus=" + k.Nama);
            kasusTxt.AppendLine("profil=" + k.Profil.FileName);
            kasusTxt.AppendLine("php=" + k.Php);
            kasusTxt.AppendLine("apache=" + k.Apache);
            kasusTxt.AppendLine("nginx=" + k.Nginx);
            kasusTxt.AppendLine("mysql=" + k.MySql);
            kasusTxt.AppendLine("opsi=" + (k.KeFolderPhp ? 1 : 0) + (k.LogAkses ? 1 : 0) + (k.Beranda ? 1 : 0) + (k.Opcache ? 1 : 0) + (k.Jaringan ? 1 : 0));
            kasusTxt.AppendLine("ulang=" + k.Ulang);
            foreach (var x in k.Kolam)
                kasusTxt.AppendLine("kolam=" + x.Php + "|" + x.Ekstensi + "|" + x.Timpa + "|" + x.Situs);

            SalinFolder(masuk, kerja);
            Paths.Root = kerja;
            var profil = ProfileStore.Load(Path.Combine(kerja, "profiles", k.Profil.FileName + ".ini"));
            var sites = SiteScanner.Scan(profil, new List<string>());
            File.WriteAllLines(Path.Combine(_akar, "situs-" + k.Nama + ".txt"),
                sites.Select(s => string.Join("|", s.Folder, s.Path, s.Root, s.HostName, s.DocRoot ?? "")),
                new UTF8Encoding(false));

            ConfigWriter.Result r = null;
            for (int i = 0; i < k.Ulang; i++)
            {
                var kolam = new List<ConfigWriter.Kolam>();
                foreach (var x in k.Kolam)
                {
                    var php = Paket_(x.Php);
                    var kl = new ConfigWriter.Kolam
                    {
                        Php = php,
                        Ekstensi = x.Ekstensi == "*" ? ConfigWriter.EkstensiDisarankan(php)
                                 : x.Ekstensi.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                    };
                    foreach (var kv in x.Timpa.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var i2 = kv.IndexOf('=');
                        kl.Timpa[kv.Substring(0, i2)] = kv.Substring(i2 + 1);
                    }
                    var nama = x.Situs.Split(',');
                    kl.Situs = sites.Where(s => nama.Contains(s.Folder)).ToList();
                    kolam.Add(kl);
                }
                r = ConfigWriter.Build(profil, Paket_(k.Php), Paket_(k.Apache), Paket_(k.MySql), Paket_(k.Nginx), sites,
                                       k.KeFolderPhp, k.LogAkses, k.Beranda, k.Opcache, kolam, k.Jaringan);
            }
            File.WriteAllLines(Path.Combine(_akar, "hasil-cs-" + k.Nama + ".txt"), Hasil(r), new UTF8Encoding(false));
            Directory.CreateDirectory(Path.Combine(_akar, "csharp"));
            Directory.Move(kerja, Path.Combine(_akar, "csharp", k.Nama));
        }

        static List<string> Hasil(ConfigWriter.Result r)
        {
            var b = new List<string>
            {
                "httpd=" + (r.HttpdConf ?? "-"),
                "myini=" + (r.MyIni ?? "-"),
                "phpinidir=" + (r.PhpIniDir ?? "-"),
                "nginx=" + (r.NginxConf ?? "-"),
                "fastcgi=" + (r.PhpFastCgi ? 1 : 0) + " port=" + r.FastCgiPort + " ssl=" + (r.SslEnabled ? 1 : 0),
                "diambil=" + (r.AdoptedExtensions != null ? string.Join(",", r.AdoptedExtensions) : "-"),
            };
            foreach (var k in r.Kolam)
                b.Add("kolam=" + k.Php.Kunci + "|" + k.Port + "|" + k.FolderIni + "|" + string.Join(",", k.Ekstensi)
                      + "|" + string.Join(",", k.Situs.Select(s => s.Folder)));
            foreach (var w in r.Warnings) b.Add("peringatan=" + w);
            return b;
        }

        static void SalinFolder(string dari, string ke)
        {
            Directory.CreateDirectory(ke);
            foreach (var d in Directory.GetDirectories(dari, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(ke + d.Substring(dari.Length));
            foreach (var f in Directory.GetFiles(dari, "*", SearchOption.AllDirectories))
                File.Copy(f, ke + f.Substring(dari.Length));
        }
    }
}
