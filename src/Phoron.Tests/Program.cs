using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Harness uji tanpa kerangka kerja pihak ketiga. Uji yang penting di sini
    /// bukan uji unit murni: yang benar-benar ingin dipastikan adalah httpd.exe
    /// SUNGGUHAN menerima konfigurasi yang dihasilkan Phoron, dan php.exe
    /// SUNGGUHAN memuat php.ini yang ditulis Phoron. Menirunya dengan mock hanya
    /// akan menguji tiruan itu sendiri.
    /// </summary>
    public static class Program
    {
        static int _lulus, _gagal;
        static readonly List<string> _kegagalan = new List<string>();

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("live", StringComparison.OrdinalIgnoreCase))
                return LiveTest.Jalankan();

            // Semua uji jalan di folder sementara supaya instalasi Phoron milik
            // pengguna tidak pernah tersentuh.
            var sandbox = Path.Combine(Path.GetTempPath(), "phoron-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sandbox);
            Paths.Root = sandbox;
            Console.WriteLine("Sandbox: " + sandbox);
            Console.WriteLine();

            try
            {
                UjiIni();
                UjiPemindai();
                UjiProfil();
                UjiSitus();
                UjiBanyakFolderProyek();
                UjiPhpIni();
                UjiDaftarEkstensiTersedia();
                UjiWarisanPhpIni();
                UjiPhpIniKeFolderPhp();
                UjiBeranda();
                UjiHalamanSambutan();
                UjiKonfigurasiApache();
                UjiHosts();
                UjiPortCheck();
                UjiNodeApps();
                UjiPembaruan();
                UjiAutostart();
                UjiLabelVersi();
                UjiBahasa();
                UjiHostsTool();
                UjiTataLetakBin();
                UjiOracle();
                UjiRuntimeVc();
                UjiKonfigurasiNginx();
                UjiLogWarna();
                UjiRahasia();
                UjiUmpanAtom();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Harness meledak: " + ex);
                _gagal++;
            }
            finally
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("=== " + _lulus + " lulus, " + _gagal + " gagal ===");
            foreach (var f in _kegagalan) Console.WriteLine("  GAGAL: " + f);
            return _gagal == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ Utilitas

        static void Ok(string nama, bool syarat, string keterangan = "")
        {
            if (syarat) { _lulus++; Console.WriteLine("  ok    " + nama); }
            else
            {
                _gagal++;
                _kegagalan.Add(nama + (keterangan.Length > 0 ? " - " + keterangan : ""));
                Console.WriteLine("  GAGAL " + nama + (keterangan.Length > 0 ? " - " + keterangan : ""));
            }
        }

        static void Bagian(string judul)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + judul);
        }

        /// <summary>Paket asli dari folder bin yang ada di mesin ini; uji yang butuh biner dilewati bila kosong.</summary>
        static List<BinPackage> Nyata()
        {
            return BinScanner.ScanAll(Settings.DefaultBinRoots()
                .Concat(new[] { @"C:\laragon\bin" }).Distinct());
        }

        // ---------------------------------------------------------------- Uji

        static void UjiIni()
        {
            Bagian("Ini");
            var path = Path.Combine(Paths.Tmp, "coba.ini");
            var ini = new Ini();
            ini.Set("umum", "nama", "Phoron");
            ini.Set("umum", "angka", "42");
            ini.Set("lain", "kosong", "");
            ini.Save(path);

            var lagi = Ini.Load(path);
            Ok("Ini bolak-balik teks", lagi.Get("umum", "nama") == "Phoron");
            Ok("Ini baca angka", lagi.GetInt("umum", "angka", 0) == 42);
            Ok("Ini nilai bawaan", lagi.GetInt("umum", "tidakada", 7) == 7);
            Ok("Ini seksi kedua", lagi.Get("lain", "kosong") == "");

            lagi.Set("umum", "nama", "Baru");
            Ok("Ini menimpa bukan menggandakan",
                lagi.Items("umum").Count(kv => kv.Key == "nama") == 1);
        }

        static void UjiPemindai()
        {
            Bagian("BinScanner");
            var pkgs = Nyata();
            Console.WriteLine("     (" + pkgs.Count + " paket terdeteksi di mesin ini)");

            var php = pkgs.Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            Ok("PHP punya nomor versi", php.All(p => p.Parsed.Major > 0));
            Ok("PHP punya php.exe", php.All(p => File.Exists(p.MainExe)));
            Ok("PHP terurut versi menurun",
                php.Select(p => p.Parsed).SequenceEqual(php.Select(p => p.Parsed).OrderByDescending(v => v)));

            var php5 = php.FirstOrDefault(p => p.Parsed.Major == 5);
            var php7 = php.FirstOrDefault(p => p.Parsed.Major == 7);
            var php8 = php.FirstOrDefault(p => p.Parsed.Major == 8);
            if (php5 != null) Ok("Modul Apache PHP 5 = php5_module", BinScanner.ApacheModuleName(php5) == "php5_module");
            if (php7 != null) Ok("Modul Apache PHP 7 = php7_module", BinScanner.ApacheModuleName(php7) == "php7_module");
            if (php8 != null) Ok("Modul Apache PHP 8 = php_module", BinScanner.ApacheModuleName(php8) == "php_module");

            if (php5 != null)
                Ok("Ekstensi PHP 5 pakai nama berkas DLL",
                    ConfigWriter.ExtensionValue(php5, "curl") == "php_curl.dll");
            if (php8 != null)
                Ok("Ekstensi PHP 8 pakai nama telanjang",
                    ConfigWriter.ExtensionValue(php8, "curl") == "curl");

            var apache = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
            if (apache.Count > 0)
            {
                Ok("Apache punya httpd.exe", apache.All(a => File.Exists(a.MainExe)));
                // Bukan "semua Apache punya toolset": XAMPP menamai foldernya
                // cuma "apache", jadi toolsetnya memang tidak tertulis di mana
                // pun. Yang diuji adalah PENGURAINYA - folder yang menyebut
                // toolset harus terbaca.
                var bertoolset = apache.Where(a => a.Id.IndexOf("-vc", StringComparison.OrdinalIgnoreCase) >= 0
                                               || a.Id.IndexOf("-vs", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                Ok("Toolset terurai dari nama folder yang menyebutnya",
                    bertoolset.All(a => a.Compiler.Length > 0),
                    string.Join(", ", bertoolset.Where(a => a.Compiler.Length == 0).Select(a => a.Id).ToArray()));
            }

            // Pasangan toolset adalah alasan utama Apache gagal start; ini inti
            // dari saran otomatis yang diberikan Phoron saat membuat profil.
            if (php.Count > 0 && apache.Count > 0)
            {
                var cocok = ProfileStore.PickApache(php[0], apache);
                Ok("Apache dipasangkan dengan toolset yang sama",
                    cocok != null && (apache.All(a => a.Compiler != php[0].Compiler)
                                      || cocok.Compiler == php[0].Compiler),
                    "PHP " + php[0].Compiler + " -> Apache " + (cocok != null ? cocok.Compiler : "null"));
            }

            Ok("Folder acak bukan paket", BinScanner.Identify(Paths.Tmp) == null);
        }

        static void UjiProfil()
        {
            Bagian("ProfileStore");
            var p = new Profile
            {
                Name = "Uji PHP 7",
                PhpId = "php-7.4.22-Win32-VC15-x64",
                ApacheId = "httpd-2.4.46-win64-VC15",
                MySqlId = "mysql-5.7.38-winx64",
                HttpPort = 8080,
                MySqlPort = 3307,
                SiteSuffix = "lokal",
                Notes = "catatan uji",
            };
            p.PhpExtensions.AddRange(new[] { "curl", "mbstring", "pdo_mysql" });
            p.PhpIniOverrides["memory_limit"] = "512M";
            p.FileName = ProfileStore.UniqueFileName(p.Name);
            ProfileStore.Save(p);

            var muat = ProfileStore.LoadAll().Single();
            Ok("Profil bolak-balik nama", muat.Name == p.Name);
            Ok("Profil bolak-balik port", muat.HttpPort == 8080 && muat.MySqlPort == 3307);
            Ok("Profil bolak-balik ekstensi",
                muat.PhpExtensions.SequenceEqual(new[] { "curl", "mbstring", "pdo_mysql" }));
            Ok("Profil bolak-balik php.ini", muat.PhpIniOverrides["memory_limit"] == "512M");
            Ok("Profil bolak-balik akhiran situs", muat.SiteSuffix == "lokal");

            var kedua = ProfileStore.UniqueFileName(p.Name);
            Ok("Nama berkas profil tidak menabrak yang ada", kedua != p.FileName);

            var salinan = muat.Clone();
            salinan.PhpExtensions.Add("gd");
            Ok("Clone tidak berbagi daftar ekstensi", muat.PhpExtensions.Count == 3);

            ProfileStore.Delete(muat);
            Ok("Hapus profil", ProfileStore.LoadAll().Count == 0);
        }

        static void UjiSitus()
        {
            Bagian("SiteScanner");
            var www = Paths.Www;
            Directory.CreateDirectory(Path.Combine(www, "Proyek Satu"));
            Directory.CreateDirectory(Path.Combine(www, "laravel-app", "public"));
            File.WriteAllText(Path.Combine(www, "laravel-app", "public", "index.php"), "<?php");
            Directory.CreateDirectory(Path.Combine(www, "punya-aset", "public"));  // tanpa index
            Directory.CreateDirectory(Path.Combine(www, ".git"));

            var profil = new Profile { SiteSuffix = "test" };
            var situs = SiteScanner.Scan(profil);

            Ok("Folder titik dilewati", situs.All(s => !s.Folder.StartsWith(".")));
            Ok("Spasi jadi tanda hubung di nama host",
                situs.Any(s => s.HostName == "proyek-satu.test"),
                string.Join(", ", situs.Select(s => s.HostName)));
            var laravel = situs.FirstOrDefault(s => s.Folder == "laravel-app");
            Ok("public/ berisi index.php jadi document root",
                laravel != null && laravel.DocRoot.EndsWith("public"));
            var aset = situs.FirstOrDefault(s => s.Folder == "punya-aset");
            Ok("public/ tanpa index.php TIDAK jadi document root",
                aset != null && !aset.DocRoot.EndsWith("public"));

            Ok("Tanpa folder proyek, www bawaan yang dipakai",
                SiteScanner.Roots(new Profile()).Single() == Paths.Www);
        }

        static void UjiBanyakFolderProyek()
        {
            Bagian("Banyak folder proyek");
            var kedua = Path.Combine(Paths.Root, "proyek-lain");
            Directory.CreateDirectory(Path.Combine(kedua, "toko"));
            Directory.CreateDirectory(Path.Combine(kedua, "gudang"));
            // Sengaja bernama sama dengan folder di www - inilah kasus yang
            // membuat dua vhost berebut ServerName yang sama.
            Directory.CreateDirectory(Path.Combine(kedua, "laravel-app"));

            var profil = new Profile { SiteSuffix = "test" };
            profil.ProjectRoots.Add(Paths.Www);
            profil.ProjectRoots.Add(kedua);

            var peringatan = new List<string>();
            var situs = SiteScanner.Scan(profil, peringatan);

            Ok("Situs dari folder kedua ikut terbaca",
                situs.Any(s => s.HostName == "toko.test" && s.Root == kedua),
                string.Join(", ", situs.Select(s => s.HostName)));
            Ok("Situs dari folder pertama tetap ada",
                situs.Any(s => s.HostName == "proyek-satu.test" && s.Root == Paths.Www));
            Ok("Akar utama tetap yang pertama", SiteScanner.DocumentRoot(profil) == Paths.Www);

            var bentrok = situs.Where(s => s.Folder == "laravel-app").ToList();
            Ok("Folder bernama sama tidak saling menghapus", bentrok.Count == 2);
            Ok("Folder pertama memegang nama aslinya",
                bentrok.Any(s => s.HostName == "laravel-app.test" && s.Root == Paths.Www));
            Ok("Folder kedua diberi nama berangka, bukan dibuang",
                bentrok.Any(s => s.HostName == "laravel-app-2.test" && s.Root == kedua),
                string.Join(", ", bentrok.Select(s => s.HostName)));
            Ok("Bentrok nama dilaporkan sebagai peringatan",
                peringatan.Any(w => w.Contains("laravel-app.test")),
                string.Join(" | ", peringatan));
            Ok("Nama host tetap unik seluruhnya",
                situs.Select(s => s.HostName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == situs.Count);

            // Folder yang salah ketik harus berbunyi, bukan diam-diam kosong.
            var salah = new Profile { SiteSuffix = "test" };
            salah.ProjectRoots.Add(Path.Combine(Paths.Root, "tidak-ada-folder-ini"));
            var p2 = new List<string>();
            SiteScanner.Scan(salah, p2);
            Ok("Folder proyek yang tidak ada dilaporkan",
                p2.Any(w => w.Contains("tidak ada")), string.Join(" | ", p2));

            // Bolak-balik ke berkas profil: daftar folder harus utuh.
            profil.Name = "Uji banyak folder";
            profil.FileName = ProfileStore.UniqueFileName(profil.Name);
            ProfileStore.Save(profil);
            var muat = ProfileStore.Load(Path.Combine(Paths.Profiles, profil.FileName + ".ini"));
            Ok("Daftar folder proyek bolak-balik utuh",
                muat.ProjectRoots.SequenceEqual(new[] { Paths.Www, kedua }),
                string.Join(";", muat.ProjectRoots));
            ProfileStore.Delete(muat);

            // Profil lama memakai kunci document_root; membacanya harus tetap jalan.
            var lama = Path.Combine(Paths.Profiles, "profil-lama.ini");
            var ini = new Ini();
            ini.Set("profil", "nama", "Profil lama");
            ini.Set("profil", "document_root", kedua);
            ini.Save(lama);
            var dibaca = ProfileStore.Load(lama);
            Ok("Profil lama dengan document_root tetap terbaca",
                dibaca.ProjectRoots.SequenceEqual(new[] { kedua }),
                string.Join(";", dibaca.ProjectRoots));
            File.Delete(lama);

            Directory.Delete(kedua, true);
        }

        static void UjiPhpIni()
        {
            Bagian("php.ini");
            var php = Nyata().Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            foreach (var v in php)
            {
                var profil = new Profile { Name = "Uji " + v.Version };
                // Ekstensi dipilih dari yang benar-benar ada supaya uji ini menguji
                // penulisan php.ini, bukan ketersediaan berkas DLL.
                var tersedia = ConfigWriter.AvailableExtensions(v);
                profil.PhpExtensions = tersedia.Where(x => x == "curl" || x == "mbstring" || x == "openssl").ToList();
                profil.PhpIniOverrides["memory_limit"] = "333M";

                var hasil = new ConfigWriter.Result();
                var dir = ConfigWriter.WritePhpIni(profil, v, hasil);
                var isi = File.ReadAllText(Path.Combine(dir, "php.ini"));

                Ok(v.Version + ": extension_dir menunjuk folder ext yang benar",
                    isi.Contains("extension_dir = \"" + Paths.Fwd(Path.Combine(v.Path, "ext")) + "\""));
                Ok(v.Version + ": memory_limit dari profil dipakai", isi.Contains("333M"));

                // php.exe sendiri yang jadi hakim: kalau ada direktif salah bentuk
                // atau DLL tidak cocok, ia mencetak peringatan ke stderr.
                var res = Shell.Run(v.MainExe, "-c \"" + dir + "\" -m", v.Path, 30000);
                var modul = res.StdOut.Split('\n').Select(l => l.Trim().ToLowerInvariant()).ToList();
                Ok(v.Version + ": php.exe menerima php.ini tanpa keluhan",
                    !res.StdErr.ToLowerInvariant().Contains("unable to load"),
                    res.StdErr.Trim());
                foreach (var ext in profil.PhpExtensions)
                    Ok(v.Version + ": ekstensi " + ext + " benar-benar dimuat",
                        modul.Contains(ext), string.Join(",", modul.Take(6)));

                var limit = Shell.Run(v.MainExe, "-c \"" + dir + "\" -r \"echo ini_get('memory_limit');\"",
                                      v.Path, 30000);
                Ok(v.Version + ": memory_limit terbaca PHP sebagai 333M",
                    limit.StdOut.Trim() == "333M", limit.All);
            }
        }

        static void UjiDaftarEkstensiTersedia()
        {
            Bagian("Daftar ekstensi tersedia");
            var palsu = Path.Combine(Paths.Root, "php-ext-daftar");
            var ext = Path.Combine(palsu, "ext");
            Directory.CreateDirectory(ext);
            File.WriteAllText(Path.Combine(ext, "php_mbstring.dll"), "x");
            File.WriteAllText(Path.Combine(ext, "php_oci8_12c.dll"), "x");
            // Cara orang menonaktifkan ekstensi: ganti nama berkasnya. Pola
            // "*.dll" Windows masih menjaringnya lewat pencocokan nama 8.3.
            File.WriteAllText(Path.Combine(ext, "php_oci8_12c.dllaaa"), "x");
            File.WriteAllText(Path.Combine(ext, "php_curl.dll.mati"), "x");
            File.WriteAllText(Path.Combine(ext, "catatan.txt"), "x");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-ext", Path = palsu, Version = "5.6.40" };

            var daftar = ConfigWriter.AvailableExtensions(php);
            Ok("Hanya berkas .dll sungguhan yang terdaftar",
                daftar.SequenceEqual(new[] { "mbstring", "oci8_12c" }),
                string.Join(",", daftar));
            Ok("Berkas yang dinonaktifkan (.dllaaa) tidak ikut",
                daftar.Count(x => x == "oci8_12c") == 1, string.Join(",", daftar));

            // --- daftar yang disarankan untuk profil baru ---
            // php.ini-development bawaan: semua ekstensi dikomentari, jadi
            // daftar baku yang harus dipakai - kalau tidak, profil baru lahir
            // tanpa satu pun ekstensi.
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                ";extension=php_mbstring.dll\n;extension=php_curl.dll\n");
            foreach (var e in new[] { "curl", "openssl", "gd2", "mysqli", "tidak_disarankan" })
                File.WriteAllText(Path.Combine(ext, "php_" + e + ".dll"), "x");

            var baku = ConfigWriter.EkstensiDisarankan(php);
            Ok("PHP baru unduh dapat daftar baku, bukan kosong", baku.Count > 0,
                string.Join(",", baku));
            Ok("Daftar baku memuat mbstring", baku.Contains("mbstring"), string.Join(",", baku));
            Ok("Daftar baku disaring ke DLL yang ada",
                !baku.Contains("intl") && !baku.Contains("tidak_disarankan"),
                string.Join(",", baku));
            Ok("Urutan baku menjaga exif setelah mbstring",
                !baku.Contains("exif")
                || baku.IndexOf("mbstring") < baku.IndexOf("exif"),
                string.Join(",", baku));

            // php.ini yang SUDAH mengaktifkan sesuatu selalu menang atas daftar baku.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), "extension=php_oci8_12c.dll\n");
            var dariIni = ConfigWriter.EkstensiDisarankan(php);
            Ok("php.ini yang sudah ada mengalahkan daftar baku",
                dariIni.SequenceEqual(new[] { "oci8_12c" }), string.Join(",", dariIni));

            Directory.Delete(palsu, true);
        }

        static void UjiWarisanPhpIni()
        {
            Bagian("php.ini yang sudah ada dijadikan dasar");
            // Kasus nyata: folder PHP dipinjam dari Laragon, yang menyetel
            // short_open_tag=On bertahun-tahun. Memulai dari php.ini-development
            // bawaan vendor mengembalikannya ke Off diam-diam, dan CodeIgniter
            // beralih ke jalur eval() lalu gagal mengurai view-nya.
            var palsu = Path.Combine(Paths.Root, "php-warisan");
            Directory.CreateDirectory(Path.Combine(palsu, "ext"));
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                "short_open_tag = Off\nmemory_limit = 128M\nmax_input_vars = 1000\n");
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "short_open_tag = On\nmemory_limit = 128M\nmax_input_vars = 5000\n");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-warisan", Path = palsu, Version = "5.6.40" };

            var profil = new Profile { Name = "Uji warisan" };
            var dir = ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result());
            var isi = File.ReadAllText(Path.Combine(dir, "php.ini"));

            Ok("short_open_tag dari php.ini yang dipakai ikut terbawa",
                Regex.IsMatch(isi, @"(?m)^short_open_tag\s*=\s*On"),
                Baris(isi, "short_open_tag"));
            Ok("Setelan lain dari php.ini itu juga ikut",
                Regex.IsMatch(isi, @"(?m)^max_input_vars\s*=\s*5000"),
                Baris(isi, "max_input_vars"));

            // Ekstensi ikut diambil alih ketika profil belum punya daftar sendiri.
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "short_open_tag = On\nmax_input_vars = 5000\n"
                + "extension=php_mbstring.dll\n"
                + "extension=php_exif.dll   ; harus setelah mbstring\n"
                + ";extension=php_tidak_dipakai.dll\n"
                + "extension=php_oci8_12c.dll\n");
            foreach (var e in new[] { "mbstring", "exif", "oci8_12c" })
                File.WriteAllText(Path.Combine(palsu, "ext", "php_" + e + ".dll"), "x");

            var profilKosong = new Profile { Name = "Belum punya daftar" };
            var r2 = new ConfigWriter.Result();
            var isiAdopsi = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profilKosong, php, r2), "php.ini"));
            Ok("Ekstensi aktif diambil alih dari php.ini dasar",
                r2.AdoptedExtensions != null
                && r2.AdoptedExtensions.SequenceEqual(new[] { "mbstring", "exif", "oci8_12c" }),
                r2.AdoptedExtensions == null ? "null" : string.Join(",", r2.AdoptedExtensions));
            Ok("Ekstensi yang dikomentari tidak ikut",
                r2.AdoptedExtensions != null && !r2.AdoptedExtensions.Contains("tidak_dipakai"));
            Ok("mbstring benar-benar ditulis ke php.ini hasil",
                Regex.IsMatch(isiAdopsi, @"(?m)^extension\s*=\s*php_mbstring\.dll"),
                Baris(isiAdopsi, "extension = php_mbstring"));
            Ok("Urutan dipertahankan (exif setelah mbstring)",
                isiAdopsi.IndexOf("php_mbstring.dll", StringComparison.Ordinal)
                    < isiAdopsi.IndexOf("php_exif.dll", StringComparison.Ordinal));

            // Profil yang SUDAH punya daftar tidak boleh diambil alih.
            var profilPunya = new Profile { Name = "Sudah punya" };
            profilPunya.PhpExtensions.Add("oci8_12c");
            var r2b = new ConfigWriter.Result();
            var isiPunya = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profilPunya, php, r2b), "php.ini"));
            Ok("Profil yang sudah punya daftar tidak diambil alih", r2b.AdoptedExtensions == null);
            Ok("Daftar profil yang dipakai, bukan daftar berkas dasar",
                !Regex.IsMatch(isiPunya, @"(?m)^extension\s*=\s*php_mbstring\.dll"));

            // Profil tetap berkuasa di atas berkas dasar.
            profil.PhpIniOverrides["short_open_tag"] = "Off";
            var isi2 = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result()), "php.ini"));
            Ok("Penimpaan profil mengalahkan php.ini dasar",
                Regex.IsMatch(isi2, @"(?m)^short_open_tag\s*=\s*Off"),
                Baris(isi2, "short_open_tag"));

            // php.ini yang ternyata keluaran Phoron tidak boleh dipakai sebagai dasar.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), isi);
            var isi3 = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(new Profile { Name = "Uji" }, php, new ConfigWriter.Result()), "php.ini"));
            Ok("Keluaran Phoron tidak dijadikan dasar (tidak menumpuk)",
                isi3.Length < isi.Length + 200,
                isi.Length + " lalu " + isi3.Length);

            Directory.Delete(palsu, true);
        }

        /// <summary>Baris pertama yang memuat sebuah kunci - dipakai untuk pesan kegagalan.</summary>
        static string Baris(string teks, string kunci)
        {
            foreach (var l in teks.Split('\n'))
                if (l.TrimStart().StartsWith(kunci, StringComparison.OrdinalIgnoreCase)) return l.Trim();
            return "(tidak ada baris " + kunci + ")";
        }

        static void UjiPhpIniKeFolderPhp()
        {
            Bagian("php.ini di dalam folder PHP");
            // Folder PHP TIRUAN di dalam sandbox. Uji ini tidak boleh menyentuh
            // folder PHP sungguhan: di mesin pengembang, folder itu milik Laragon.
            var palsu = Path.Combine(Paths.Root, "php-palsu");
            Directory.CreateDirectory(Path.Combine(palsu, "ext"));
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                "memory_limit = 128M\nextension=php_bawaan.dll\n");
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "; punya pengelola lain\nmemory_limit = 999M\nextension=php_punya_orang.dll\n");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-palsu", Path = palsu, Version = "8.3.0" };

            var profil = new Profile { Name = "Uji ini" };
            profil.PhpIniOverrides["memory_limit"] = "256M";

            var r1 = new ConfigWriter.Result();
            var dir = ConfigWriter.WritePhpIni(profil, php, r1, true);
            Ok("php.ini ditulis ke folder PHP", dir == palsu, dir);

            var cadangan = Path.Combine(palsu, "php.ini.sebelum-phoron");
            Ok("php.ini asli dicadangkan", File.Exists(cadangan));
            Ok("Cadangan berisi berkas asli, bukan hasil Phoron",
                File.ReadAllText(cadangan).Contains("punya pengelola lain"));

            var isi1 = File.ReadAllText(Path.Combine(palsu, "php.ini"));
            Ok("Nilai dari profil dipakai", isi1.Contains("256M"));
            Ok("Ekstensi pengelola lain dimatikan",
                !isi1.Contains("\nextension=php_punya_orang.dll"));

            // Ditulis dua kali: berkas tidak boleh menumpuk karena memakai
            // keluarannya sendiri sebagai dasar.
            ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result(), true);
            var isi2 = File.ReadAllText(Path.Combine(palsu, "php.ini"));
            Ok("Penulisan kedua tidak menggandakan isi", isi1.Length == isi2.Length,
                isi1.Length + " lalu " + isi2.Length);
            Ok("Cadangan tidak ditimpa hasil Phoron",
                File.ReadAllText(cadangan).Contains("punya pengelola lain"));

            // Folder PHP di luar bin milik Phoron harus memicu peringatan.
            var r3 = new ConfigWriter.Result();
            ConfigWriter.WritePhpIni(profil, php, r3, true);
            Ok("Folder PHP pinjaman diperingatkan",
                r3.Warnings.Any(w => w.Contains("TIDAK milik Phoron")),
                string.Join(" | ", r3.Warnings));

            // Baku (mati) tetap menulis ke etc\ dan tidak menyentuh folder PHP.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), "; disentuh pengelola lain\n");
            var r4 = new ConfigWriter.Result();
            var dir4 = ConfigWriter.WritePhpIni(profil, php, r4, false);
            Ok("Baku menulis ke etc\\php", dir4 == Path.Combine(Paths.Etc, "php", php.Id), dir4);
            Ok("Baku tidak menyentuh php.ini folder PHP",
                File.ReadAllText(Path.Combine(palsu, "php.ini")) == "; disentuh pengelola lain\n");

            Directory.Delete(palsu, true);
        }

        static void UjiBeranda()
        {
            Bagian("Beranda Phoron");
            var profil = new Profile { Name = "Uji beranda", HttpPort = 8080, MySqlPort = 3307 };
            var situs = new List<Site>
            {
                new Site { Folder = "toko", Path = @"C:\proyek\toko", HostName = "toko.test" },
                // Nama yang memuat petik dan garis miring terbalik: satu saja
                // cukup merusak seluruh halaman kalau kutipnya tidak diamankan.
                new Site { Folder = "d'Art" + @"\beta", Path = @"C:\proyek\d'Art", HostName = "d-art.test" },
            };
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-uji", Version = "8.3.0" };
            Beranda.Tulis(profil, situs, php, null, null);

            var berkas = Path.Combine(Beranda.Folder, "index.php");
            Ok("Beranda ditulis ke etc\\dashboard", File.Exists(berkas), berkas);
            var isi = File.ReadAllText(berkas);
            Ok("Nama profil ikut disuntikkan", isi.Contains("Uji beranda"));
            Ok("Port situs mengikuti profil", isi.Contains("http://toko.test:8080/"));
            Ok("Petik dalam nama diamankan", isi.Contains(@"d\'Art"), Baris(isi, "  array('nama'"));
            Ok("Beranda TIDAK ditulis ke folder proyek",
                !File.Exists(Path.Combine(SiteScanner.DocumentRoot(profil), "index.php"))
                || !File.ReadAllText(Path.Combine(SiteScanner.DocumentRoot(profil), "index.php")).Contains("Uji beranda"));

            var daftarPhp = Nyata().Where(x => x.Kind == BinKind.Php).ToList();
            if (daftarPhp.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }
            foreach (var v in daftarPhp)
            {
                var res = Shell.Run(v.MainExe, "-n -l \"" + berkas + "\"", v.Path, 30000);
                Ok(v.Version + ": beranda lolos php -l", res.Ok, res.All.Trim());
            }
        }

        static void UjiHalamanSambutan()
        {
            Bagian("Halaman sambutan");
            var php = Nyata().Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            var file = Path.Combine(Paths.Tmp, "sambutan.php");
            File.WriteAllText(file, Engine.HalamanSambutan(), new System.Text.UTF8Encoding(false));

            // Halaman ini ikut dikirim ke pengguna, jadi harus sah di SEMUA versi
            // PHP yang bisa dipilih - termasuk 5.6, yang tidak mengenal "??".
            foreach (var v in php)
            {
                var res = Shell.Run(v.MainExe, "-n -l \"" + file + "\"", v.Path, 30000);
                Ok(v.Version + ": halaman sambutan lolos php -l", res.Ok, res.All.Trim());
            }
        }

        static void UjiUmpanAtom()
        {
            Bagian("Umpan Atom rilis");
            // Umpan ini dilayani github.com, bukan api.github.com, jadi TIDAK
            // tunduk pada batas 60 permintaan per jam - itulah sebabnya ia jadi
            // jalur utama dan token GitHub tidak dibutuhkan siapa pun.
            var contoh =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<feed xmlns=\"http://www.w3.org/2005/Atom\">\n" +
                "  <title>Release notes from Phoron</title>\n" +
                "  <entry>\n" +
                "    <id>tag:github.com,2008:Repository/1365049606/v1.20.0</id>\n" +
                "    <link rel=\"alternate\" type=\"text/html\" " +
                "href=\"https://github.com/Cyserrex/Phoron/releases/tag/v1.20.0\"/>\n" +
                "    <title>Phoron 1.20.0</title>\n" +
                "    <content type=\"html\">&lt;p&gt;&lt;strong&gt;Full Changelog&lt;/strong&gt;: v1.19.0...v1.20.0&lt;/p&gt;</content>\n" +
                "  </entry>\n" +
                "  <entry>\n" +
                "    <link rel=\"alternate\" type=\"text/html\" " +
                "href=\"https://github.com/Cyserrex/Phoron/releases/tag/v1.19.0\"/>\n" +
                "    <title>Phoron 1.19.0</title>\n" +
                "  </entry>\n" +
                "</feed>";

            var h = Updater.UraiAtom(contoh);
            Ok("Tidak ada galat pada umpan yang sah", h.Galat == null, h.Galat);
            Ok("Versi diambil dari entri PERTAMA, bukan sembarang entri",
               h.Versi == "1.20.0", h.Versi);
            Ok("Awalan v dibuang dari nomor versi", !h.Versi.StartsWith("v"), h.Versi);
            Ok("Halaman rilis menunjuk tag yang benar",
               h.UrlHalaman.EndsWith("/releases/tag/v1.20.0"), h.UrlHalaman);
            // Umpan Atom tidak menyebut berkas aset, jadi alamatnya disusun dari
            // pola penamaan CI. Kalau pola itu berubah, uji ini yang berbunyi.
            Ok("Alamat installer disusun sesuai pola CI",
               h.UrlInstaller == "https://github.com/Cyserrex/Phoron/releases/download/"
                                 + "v1.20.0/Phoron-1.20.0-Setup.exe", h.UrlInstaller);
            Ok("Alamat installer memakai github.com, BUKAN api.github.com",
               h.UrlInstaller.IndexOf("api.github.com", StringComparison.OrdinalIgnoreCase) < 0);
            Ok("Catatan rilis dibersihkan dari tag HTML",
               h.Catatan.IndexOf('<') < 0 && h.Catatan.IndexOf("Full Changelog", StringComparison.Ordinal) >= 0,
               h.Catatan);

            Ok("Umpan kosong dilaporkan sebagai galat", Updater.UraiAtom("").Galat != null);
            Ok("Teks sampah dilaporkan sebagai galat, bukan versi karangan",
               Updater.UraiAtom("bukan xml sama sekali").Galat != null);

            // Perbandingan versi tetap dipakai jalur ini.
            Ok("Versi umpan dibandingkan dengan yang terpasang",
               Updater.LebihBaru("1.20.1", "1.20.0") && !Updater.LebihBaru("1.20.0", "1.20.0"));
        }

        static void UjiRahasia()
        {
            Bagian("Token tersandi");
            // Token GitHub tidak boleh tersimpan apa adanya: phoron.ini berkas
            // teks biasa yang dibuka orang dan ikut dalam backup.
            var token = "ghp_ContohTokenPalsu1234567890abcdefGH";

            var sandi = Rahasia.Sandi(token);
            Ok("Hasil sandi tidak memuat tokennya", sandi.IndexOf(token, StringComparison.Ordinal) < 0);
            Ok("Hasil sandi tidak kosong", sandi.Length > 0);
            Ok("Bisa dibuka kembali utuh", Rahasia.Buka(sandi) == token, Rahasia.Buka(sandi));
            Ok("Kosong tetap kosong", Rahasia.Sandi("") == "" && Rahasia.Buka("") == "");
            Ok("Teks sampah tidak membuat pembukanya meledak",
               Rahasia.Buka("bukan-base64-sama-sekali") == "");
            Ok("Base64 sah tapi bukan milik kita juga aman",
               Rahasia.Buka(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })) == "");

            var samar = Rahasia.Samar(token);
            Ok("Bentuk samar tidak memuat token utuh", samar.IndexOf(token, StringComparison.Ordinal) < 0);
            Ok("Bentuk samar menyisakan ujungnya untuk dikenali",
               samar.StartsWith("ghp_") && samar.EndsWith(token.Substring(token.Length - 4)), samar);
            Ok("Token pendek disamarkan seluruhnya",
               Rahasia.Samar("pendek") == "******", Rahasia.Samar("pendek"));

            // Setelan bolak-balik lewat berkas: inilah jalur yang sebenarnya
            // dipakai, dan di sinilah token bisa terlanjur bocor ke teks polos.
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-token-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                var s1 = Settings.Load();
                s1.GithubToken = token;
                s1.Save();

                var isiIni = File.ReadAllText(Paths.SettingsFile);
                Ok("phoron.ini TIDAK memuat token apa adanya",
                   isiIni.IndexOf(token, StringComparison.Ordinal) < 0);
                Ok("phoron.ini memuat barisnya", isiIni.IndexOf("token_github", StringComparison.Ordinal) >= 0);

                var s2 = Settings.Load();
                Ok("Token terbaca kembali saat dimuat ulang", s2.GithubToken == token, s2.GithubToken);
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiLogWarna()
        {
            Bagian("Warna log");
            // Baris-baris di bawah ini disalin dari layar Aktivitas yang
            // sebenarnya, bukan dikarang. Aturan berbasis kata kunci mudah sekali
            // salah tangkap, dan salah warna pada baris galat lebih buruk
            // daripada tidak berwarna sama sekali.
            Action<string, JenisPesan, string> cek = (baris, harap, nama) =>
                Ok(nama, LogWarna.Golongkan(baris) == harap,
                   "dapat " + LogWarna.Golongkan(baris) + " untuk: " + baris);

            cek("Konfigurasi Nginx ditolak:", JenisPesan.Galat, "\"ditolak\" = galat");
            cek("nginx: [emerg] CreateFile() \"fastcgi_params\" failed (2: The system cannot find the file specified)",
                JenisPesan.Galat, "[emerg] nginx = galat");
            cek("Apache berhenti seketika (kode 1).", JenisPesan.Galat, "berhenti seketika = galat");
            cek("Cannot load php5apache2_4.dll into server: %1 is not a valid Win32 application.",
                JenisPesan.Galat, "gagal muat modul = galat");

            cek("Peringatan: Nama balas-api.test sudah dipakai C:\\laragon\\www\\balas_api",
                JenisPesan.Peringatan, "\"Peringatan:\" = peringatan");
            cek("[mysql] 2026-09-15T02:42:55 0 [Warning] TIMESTAMP with implicit DEFAULT value is deprecated.",
                JenisPesan.Peringatan, "[Warning] mysqld = peringatan");
            cek("Peringatan: Nama folder \"php-5.6.40-Win32-VC11-x64\" ada di lebih dari satu folder bin",
                JenisPesan.Peringatan, "folder kembar = peringatan");

            cek("Apache 2.4.38 jalan di port 80 (PID 23804).", JenisPesan.Berhasil, "jalan di port = berhasil");
            cek("Konfigurasi profil \"PHP 5.6.40 + Apache 2.4.38\" ditulis ulang.",
                JenisPesan.Berhasil, "ditulis ulang = berhasil");

            cek("MySQL dimatikan.", JenisPesan.Biasa, "baris netral tetap biasa");
            cek("", JenisPesan.Biasa, "baris kosong tidak meledak");

            // Satu baris bisa memuat kata dari dua golongan sekaligus; yang
            // menang harus galat, karena itulah yang dicari orang.
            cek("Peringatan: berkas hosts gagal ditulis", JenisPesan.Galat,
                "galat menang atas peringatan dalam satu baris");

            Ok("Tiap golongan punya warna, kecuali Biasa",
               LogWarna.Heks(JenisPesan.Galat).Length > 0
               && LogWarna.Heks(JenisPesan.Peringatan).Length > 0
               && LogWarna.Heks(JenisPesan.Berhasil).Length > 0
               && LogWarna.Heks(JenisPesan.Biasa).Length == 0);
            Ok("Warnanya berbeda satu sama lain",
               LogWarna.Heks(JenisPesan.Galat) != LogWarna.Heks(JenisPesan.Peringatan)
               && LogWarna.Heks(JenisPesan.Peringatan) != LogWarna.Heks(JenisPesan.Berhasil));
        }

        static void UjiKonfigurasiNginx()
        {
            Bagian("nginx.conf (diuji nginx.exe sungguhan)");
            // Selama ini hanya httpd.conf yang diuji dengan binernya sendiri, dan
            // nginx.conf lolos begitu saja - padahal ia meng-include
            // "fastcgi_params" sebagai nama telanjang, yang membuat nginx menolak
            // start dengan "CreateFile() ... failed (2)". Uji inilah yang
            // seharusnya menangkapnya sejak awal.
            var nginx = Nyata().FirstOrDefault(p => p.Kind == BinKind.Nginx);
            if (nginx == null) { Console.WriteLine("     dilewati: tidak ada Nginx terpasang"); return; }
            var php = Nyata().FirstOrDefault(p => p.Kind == BinKind.Php);

            Directory.CreateDirectory(Path.Combine(Paths.Www, "situs-nginx"));
            File.WriteAllText(Path.Combine(Paths.Www, "situs-nginx", "index.php"), "<?php echo 1;");
            // Nama panjang yang nyata: "bandarmasih-mobile-pm-service.test" 34
            // karakter, sedangkan baku server_names_hash_bucket_size cuma 32.
            // nginx menolak SELURUH konfigurasi, bukan cuma situs itu.
            Directory.CreateDirectory(Path.Combine(Paths.Www, "bandarmasih-mobile-pm-service"));
            File.WriteAllText(Path.Combine(Paths.Www, "bandarmasih-mobile-pm-service", "index.php"),
                              "<?php echo 1;");

            var profil = new Profile
            {
                Name = "Uji Nginx",
                WebServer = "nginx",
                NginxId = nginx.Id,
                PhpId = php != null ? php.Id : "",
                HttpPort = 8080,
            };
            var situs = SiteScanner.Scan(profil);
            var hasil = ConfigWriter.Build(profil, php, null, null, nginx, situs);

            Ok("nginx.conf terbentuk", hasil.NginxConf != null && File.Exists(hasil.NginxConf));
            if (hasil.NginxConf == null) return;

            var isi = File.ReadAllText(hasil.NginxConf);
            Ok("fastcgi_params di-include dengan jalur penuh, bukan nama telanjang",
               !Regex.IsMatch(isi, @"include\s+fastcgi_params\s*;"), "masih ada nama telanjang");
            Ok("Situs bernama panjang ikut terdaftar",
               isi.IndexOf("bandarmasih-mobile-pm-service", StringComparison.OrdinalIgnoreCase) >= 0);
            Ok("Ukuran ember hash cukup untuk nama terpanjang",
               ConfigWriter.EmberHash(new[] { "bandarmasih-mobile-pm-service.test" }) >= 64,
               ConfigWriter.EmberHash(new[] { "bandarmasih-mobile-pm-service.test" }).ToString());
            Ok("Nama sangat panjang menaikkan embernya lagi",
               ConfigWriter.EmberHash(new[] { new string('a', 120) }) >= 128,
               ConfigWriter.EmberHash(new[] { new string('a', 120) }).ToString());
            Ok("Banyak situs menaikkan kapasitas tabel",
               ConfigWriter.MaksHash(500) > ConfigWriter.MaksHash(10));

            foreach (Match m in Regex.Matches(isi, "include\\s+\"([^\"]+)\""))
                Ok("Berkas yang di-include ada: " + Path.GetFileName(m.Groups[1].Value),
                   File.Exists(m.Groups[1].Value), m.Groups[1].Value);

            // Hakim sesungguhnya: nginx sendiri. -t menguraikan seluruh berkas,
            // termasuk setiap include, lalu menolak kalau ada yang tidak ada.
            var res = Shell.Run(nginx.MainExe, "-t -c \"" + hasil.NginxConf + "\" -p \"" + nginx.Path + "\"",
                                nginx.Path, 30000);
            Ok("nginx.exe -t menerima konfigurasi",
               res.All.IndexOf("test is successful", StringComparison.OrdinalIgnoreCase) >= 0,
               res.All);
        }

        static void UjiKonfigurasiApache()
        {
            Bagian("httpd.conf (diuji httpd.exe sungguhan)");
            var pkgs = Nyata();
            var apaches = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
            var phps = pkgs.Where(p => p.Kind == BinKind.Php).ToList();
            if (apaches.Count == 0) { Console.WriteLine("     dilewati: tidak ada Apache terpasang"); return; }

            Directory.CreateDirectory(Path.Combine(Paths.Www, "situs-uji"));
            File.WriteAllText(Path.Combine(Paths.Www, "situs-uji", "index.php"), "<?php echo 1;");

            foreach (var apache in apaches)
            {
                // Tiap Apache dipasangkan dengan PHP yang ARSITEKTURNYA sepadan
                // dan toolsetnya sama - aturan yang sama persis dipakai
                // ProfileStore.PickApache untuk pengguna. Mencocokkan toolset
                // saja pernah memasangkan PHP x86 milik XAMPP dengan Apache x64,
                // dan httpd menolaknya dengan "%1 is not a valid Win32
                // application" yang tidak menyebut sebabnya.
                var php = phps.FirstOrDefault(p => ProfileStore.ArsitekturSepadan(p.Arch, apache.Arch)
                                               && string.Equals(p.Compiler, apache.Compiler,
                                                                StringComparison.OrdinalIgnoreCase));
                var profil = new Profile
                {
                    Name = "Uji " + apache.Id,
                    ApacheId = apache.Id,
                    PhpId = php != null ? php.Id : "",
                    HttpPort = 8080,
                    HttpsPort = 8443,
                };
                var situs = SiteScanner.Scan(profil);
                var hasil = ConfigWriter.Build(profil, php, apache, null, null, situs);

                Ok(apache.Id + ": httpd.conf terbentuk",
                    hasil.HttpdConf != null && File.Exists(hasil.HttpdConf));
                if (hasil.HttpdConf == null) continue;

                var isi = File.ReadAllText(hasil.HttpdConf);
                Ok(apache.Id + ": ServerRoot menunjuk paket yang dipilih",
                    isi.Contains(Paths.Fwd(apache.Path)));
                Ok(apache.Id + ": port profil dipakai", isi.Contains("Listen 8080"));
                Ok(apache.Id + ": tidak ada Include ke luar milik pengelola lain",
                    !isi.Contains("C:/laragon/etc") && !isi.Contains("C:\\laragon\\etc"));

                if (php != null)
                {
                    var modPhp = File.ReadAllText(Path.Combine(Paths.EtcApache, "mod_php.conf"));
                    Ok(apache.Id + ": mod_php menunjuk DLL PHP " + php.Version,
                        modPhp.Contains(BinScanner.ApacheModuleName(php)));
                }

                // Hakim sesungguhnya.
                var res = Shell.Run(Path.Combine(apache.Path, "bin", "httpd.exe"),
                    "-f \"" + hasil.HttpdConf + "\" -d \"" + apache.Path + "\" -t",
                    apache.Path, 60000, ServiceManager.EnvFor(php));
                Ok(apache.Id + ": httpd.exe -t menerima konfigurasi", res.Ok, res.All.Trim());

                // Vhost otomatis harus benar-benar dikenali Apache, bukan sekadar
                // ada berkasnya.
                var vhosts = Shell.Run(Path.Combine(apache.Path, "bin", "httpd.exe"),
                    "-f \"" + hasil.HttpdConf + "\" -d \"" + apache.Path + "\" -S",
                    apache.Path, 60000, ServiceManager.EnvFor(php));
                Ok(apache.Id + ": vhost situs-uji.test terdaftar",
                    vhosts.All.Contains("situs-uji.test"), vhosts.All.Trim());
            }

            // Vhost milik folder yang sudah dihapus harus ikut hilang, kalau tidak
            // Apache menolak start karena DocumentRoot-nya tidak ada lagi.
            var sisa = Directory.GetFiles(Paths.SitesEnabled, "auto.*.conf");
            Directory.Delete(Path.Combine(Paths.Www, "situs-uji"), true);
            var apache2 = apaches[0];
            var profil2 = new Profile { ApacheId = apache2.Id, HttpPort = 8080 };
            ConfigWriter.Build(profil2, null, apache2, null, null, SiteScanner.Scan(profil2));
            Ok("Vhost folder yang dihapus ikut dibersihkan",
                !Directory.GetFiles(Paths.SitesEnabled, "auto.*.conf")
                          .Any(f => f.Contains("situs-uji")),
                "sebelumnya " + sisa.Length + " berkas");
        }

        static void UjiHosts()
        {
            Bagian("HostsFile");
            // Berkas hosts sungguhan tidak disentuh; yang diuji adalah penyusunan
            // bloknya, yang merupakan bagian paling gampang salah.
            var contoh = new[]
            {
                "127.0.0.1 localhost",
                "# komentar",
                HostsFile.Begin,
                "127.0.0.1\tlama.test",
                HostsFile.End,
                "10.0.0.1 kantor.internal",
            };
            var tanpaBlok = new List<string>(contoh);
            int b = tanpaBlok.IndexOf(HostsFile.Begin);
            int e = tanpaBlok.IndexOf(HostsFile.End);
            tanpaBlok.RemoveRange(b, e - b + 1);
            Ok("Blok Phoron bisa dipotong utuh", tanpaBlok.Count == 3);
            Ok("Baris milik orang lain tidak ikut terpotong",
                tanpaBlok.Contains("10.0.0.1 kantor.internal")
                && tanpaBlok.Contains("127.0.0.1 localhost"));
            Ok("Penanda blok tidak berubah bentuk",
                HostsFile.Begin.StartsWith("#") && HostsFile.End.StartsWith("#"));
        }

        static void UjiLabelVersi()
        {
            Bagian("Label versi");
            // Penjaga kerusakan pengodean: set_version.ps1 pernah membaca berkas
            // sumber dengan codepage ANSI lalu menulisnya sebagai UTF-8, dan
            // titik tengah di label ini berubah jadi "Â·". Kerusakannya hanya
            // terlihat di layar aplikasi, jauh dari skrip penyebabnya.
            var pkg = new BinPackage
            {
                Kind = BinKind.Php,
                Version = "8.3.12",
                Compiler = "vs16",
                Arch = "x64",
                ThreadSafe = true,
            };
            var label = pkg.Label;
            Ok("Label memakai titik tengah yang benar", label == "8.3.12 · VS16 · x64 · TS", label);
            Ok("Label tidak mengandung sisa mojibake", !label.Contains("Â"), label);
        }

        static void UjiRuntimeVc()
        {
            Bagian("Runtime Visual C++");
            // Gejala yang diuji di sini: satu profil gagal sementara profil lain
            // di komputer yang sama jalan mulus, karena tiap toolset butuh
            // redistributable berbeda dan yang kurang tidak pernah disebut
            // namanya oleh Windows.
            var vc11 = new BinPackage
            {
                Kind = BinKind.Php, Id = "php-5.6.40-Win32-VC11-x64", Version = "5.6.40",
                Compiler = "VC11", Arch = "x64", Path = Paths.Tmp,
            };
            var vs16 = new BinPackage
            {
                Kind = BinKind.Php, Id = "php-8.3.12-Win32-vs16-x64", Version = "8.3.12",
                Compiler = "VS16", Arch = "x64", Path = Paths.Tmp,
            };
            var takKenal = new BinPackage
            {
                Kind = BinKind.MySql, Id = "mysql-8.0.30-winx64", Version = "8.0.30",
                Compiler = "", Arch = "x64", Path = Paths.Tmp,
            };

            Ok("VC11 dipetakan ke msvcr110.dll",
               RuntimeVc.Periksa(vc11).Dll == "msvcr110.dll", RuntimeVc.Periksa(vc11).Dll);
            Ok("VS16 dipetakan ke vcruntime140.dll",
               RuntimeVc.Periksa(vs16).Dll == "vcruntime140.dll", RuntimeVc.Periksa(vs16).Dll);
            Ok("Paket tanpa toolset tidak dituntut runtime apa pun",
               !RuntimeVc.Periksa(takKenal).Perlu);

            // Mesin pengembang lazimnya punya semua redistributable. Yang penting
            // dipastikan: kalau ADA, tidak boleh ada tuduhan palsu.
            var h11 = RuntimeVc.Periksa(vc11);
            var adaSungguhan = File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msvcr110.dll"));
            Ok("Runtime yang ADA tidak dilaporkan kurang",
               !adaSungguhan || h11.Ada, "ada=" + adaSungguhan + " lapor=" + h11.Ada);

            // Yang bisa dipastikan di mesin mana pun: DLL karangan pasti tidak ada,
            // dan pesannya harus menyebut nama paket yang dicari orang, bukan
            // sekadar nama DLL-nya.
            var palsu = new BinPackage
            {
                Kind = BinKind.Apache, Id = "httpd-2.4.38-win64-VC9", Version = "2.4.38",
                Compiler = "VC9", Arch = "x64", Path = Paths.Tmp,
            };
            var hp = RuntimeVc.Periksa(palsu);
            if (!File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msvcr90.dll")))
            {
                Ok("Runtime yang tidak ada dilaporkan kurang", !hp.Ada);
                Ok("Pesannya menyebut nama paket Microsoft, bukan cuma nama DLL",
                   hp.Pesan.IndexOf("Visual C++ 2008", StringComparison.OrdinalIgnoreCase) >= 0, hp.Pesan);
            }

            Ok("Redistributable yang sama tidak diadukan dua kali",
               RuntimeVc.PeriksaSemua(vs16, vs16).Count <= 1);
        }

        static void UjiOracle()
        {
            Bagian("Oracle Instant Client");
            // PE sungguhan berarsitektur pasti, tersedia di setiap Windows 64-bit:
            // System32 berisi x64, SysWOW64 berisi x86. Memakai berkas palsu tidak
            // membuktikan apa pun - pembaca header PE akan mengembalikan "" dan
            // "" selalu dianggap sepadan.
            var x64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                   "System32", "kernel32.dll");
            var x86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                   "SysWOW64", "kernel32.dll");
            if (!File.Exists(x64) || !File.Exists(x86))
            { Console.WriteLine("     dilewati: bukan Windows 64-bit"); return; }

            Ok("Pembaca PE membedakan x64 dan x86",
               BinProbe.Arsitektur(x64) == "x64" && BinProbe.Arsitektur(x86) == "x86",
               BinProbe.Arsitektur(x64) + " / " + BinProbe.Arsitektur(x86));

            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-oracle-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var d86 = Path.Combine(akar, "client86");
                var d64 = Path.Combine(akar, "client64");
                Directory.CreateDirectory(d86);
                Directory.CreateDirectory(d64);
                File.Copy(x86, Path.Combine(d86, "oci.dll"));
                File.Copy(x64, Path.Combine(d64, "oci.dll"));

                var phpX64 = new BinPackage
                {
                    Kind = BinKind.Php, Id = "php-uji", Version = "5.6.40",
                    Arch = "x64", Path = akar, MainExe = x64,
                };

                var hanya86 = Oracle.Periksa(phpX64, d86);
                Ok("Client x86 saja dengan PHP x64 dinyatakan TIDAK layak",
                   !hanya86.Layak && hanya86.Ada, hanya86.Pesan);
                Ok("Pesannya menyebut arsitektur yang harus dipasang",
                   hanya86.Pesan.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0);

                // Inti uji ini: Windows MELEWATI client yang tidak sepadan dan
                // meneruskan pencarian - dibuktikan dengan php.exe sungguhan.
                // Berhenti di yang pertama akan menuduh keadaan yang sehat.
                var duaduanya = Oracle.Periksa(phpX64, d86 + ";" + d64);
                Ok("x86 di depan tapi x64 ada di belakang tetap dinyatakan layak",
                   duaduanya.Layak, duaduanya.Pesan);
                Ok("Yang dilaporkan adalah client yang sepadan, bukan yang pertama",
                   duaduanya.JalurDll != null
                   && duaduanya.JalurDll.IndexOf("client64", StringComparison.OrdinalIgnoreCase) >= 0,
                   duaduanya.JalurDll);

                var kosong = Oracle.Periksa(phpX64, Path.Combine(akar, "tidak-ada"));
                Ok("Tanpa client mana pun dinyatakan tidak layak", !kosong.Layak && !kosong.Ada);
                Ok("Pesannya menyuruh memasang, bukan menyalahkan DLL ekstensi",
                   kosong.Pesan.IndexOf("Instant Client", StringComparison.OrdinalIgnoreCase) >= 0);

                Ok("Nama ekstensi Oracle dikenali",
                   Oracle.AdalahEkstensiOracle("oci8_11g") && Oracle.AdalahEkstensiOracle("pdo_oci")
                   && !Oracle.AdalahEkstensiOracle("mysqli"));
            }
            finally { try { Directory.Delete(akar, true); } catch { } }
        }

        static void UjiTataLetakBin()
        {
            Bagian("Tata letak folder bin");
            // Tiap pengelola menata foldernya sendiri-sendiri. Yang menentukan
            // sebuah folder itu paket atau bukan adalah ADA TIDAKNYA exe, bukan
            // namanya - jadi tata letak baru tidak perlu ditambahkan satu per satu.
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-binlayout-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Action<string> buat = jalur =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jalur));
                    File.WriteAllText(jalur, "");
                };

                // Gaya Laragon: <akar>\php\<folder berversi>
                buat(Path.Combine(akar, @"laragon\php\php-8.3.12-Win32-vs16-x64\php.exe"));
                // Folder versi langsung di akar
                buat(Path.Combine(akar, @"lepas\php-7.4.22-Win32-VC15-x64\php.exe"));
                // Gaya XAMPP: folder tanpa nomor versi sama sekali
                buat(Path.Combine(akar, @"xampp\php\php.exe"));
                buat(Path.Combine(akar, @"xampp\apache\bin\httpd.exe"));
                buat(Path.Combine(akar, @"xampp\mysql\bin\mysqld.exe"));
                // Gaya WAMP: satu tingkat lebih dalam, di bawah "bin"
                buat(Path.Combine(akar, @"wamp64\bin\php\php8.1.0\php.exe"));
                buat(Path.Combine(akar, @"wamp64\bin\apache\apache2.4.51\bin\httpd.exe"));
                // Jebakan: folder proyek pengguna tidak boleh ikut dirayapi.
                buat(Path.Combine(akar, @"wamp64\www\proyek-saya\php.exe"));

                Func<string, List<BinPackage>> pindai =
                    r => BinScanner.ScanAll(new[] { Path.Combine(akar, r) });

                var laragon = pindai("laragon");
                Ok("Gaya Laragon terbaca",
                   laragon.Any(x => x.Kind == BinKind.Php && x.Version == "8.3.12"),
                   laragon.Count + " paket");

                var lepas = pindai("lepas");
                Ok("Folder versi langsung di akar terbaca",
                   lepas.Any(x => x.Kind == BinKind.Php && x.Version == "7.4.22"),
                   lepas.Count + " paket");

                var xampp = pindai("xampp");
                Ok("Gaya XAMPP: PHP terbaca walau folder tak berversi",
                   xampp.Any(x => x.Kind == BinKind.Php), xampp.Count + " paket");
                Ok("Gaya XAMPP: Apache terbaca",
                   xampp.Any(x => x.Kind == BinKind.Apache), xampp.Count + " paket");
                Ok("Gaya XAMPP: MySQL terbaca",
                   xampp.Any(x => x.Kind == BinKind.MySql), xampp.Count + " paket");

                var wamp = pindai("wamp64");
                Ok("Gaya WAMP: PHP di bawah bin terbaca",
                   wamp.Any(x => x.Kind == BinKind.Php && x.Version == "8.1.0"),
                   string.Join(", ", wamp.Select(x => x.Kind + ":" + x.Id).ToArray()));
                Ok("Gaya WAMP: Apache di bawah bin terbaca",
                   wamp.Any(x => x.Kind == BinKind.Apache && x.Version == "2.4.51"),
                   string.Join(", ", wamp.Select(x => x.Kind + ":" + x.Id).ToArray()));
                Ok("Folder www pengguna TIDAK ikut dirayapi",
                   !wamp.Any(x => x.Path.IndexOf("proyek-saya", StringComparison.OrdinalIgnoreCase) >= 0),
                   string.Join(", ", wamp.Select(x => x.Path).ToArray()));

                // Penjaga arsitektur: header PE dibaca dari exe sungguhan, karena
                // salah arsitektur membuat Apache mati tanpa pesan apa pun.
                var php = Nyata().FirstOrDefault(x => x.Kind == BinKind.Php);
                if (php != null)
                    Ok("Arsitektur terbaca dari header PE exe sungguhan",
                       BinProbe.Arsitektur(php.MainExe) == "x64"
                       || BinProbe.Arsitektur(php.MainExe) == "x86",
                       BinProbe.Arsitektur(php.MainExe));
                Ok("Berkas bukan PE tidak membuat penyelidik meledak",
                   BinProbe.Arsitektur(Path.Combine(akar, @"xampp\php\php.exe")) == "");
            }
            finally { try { Directory.Delete(akar, true); } catch { } }
        }

        static void UjiHostsTool()
        {
            Bagian("HostsTool");
            // Yang dijaga di sini: nama dikumpulkan dari SELURUH profil, bukan
            // cuma yang aktif. Kalau cuma yang aktif, berganti profil berarti
            // minta hak Administrator lagi - persis yang ingin dihindari.
            //
            // BelumTerdaftar() sengaja TIDAK diuji: ia membaca berkas hosts
            // mesin yang sedang dipakai, jadi hasilnya bergantung keadaan mesin.
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-hoststool-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;

                var satu = Path.Combine(akar, "proyek-satu");
                var dua = Path.Combine(akar, "proyek-dua");
                Directory.CreateDirectory(Path.Combine(satu, "alfa"));
                Directory.CreateDirectory(Path.Combine(satu, "beta"));
                Directory.CreateDirectory(Path.Combine(dua, "gama"));

                var pA = new Profile { Name = "A", SiteSuffix = "test" };
                pA.ProjectRoots.Add(satu);
                ProfileStore.Save(pA);

                var pB = new Profile { Name = "B", SiteSuffix = "dev" };
                pB.ProjectRoots.Add(dua);
                ProfileStore.Save(pB);

                // Profil ketiga menunjuk folder yang sama dengan A: namanya
                // harus menyatu, bukan berlipat.
                var pC = new Profile { Name = "C", SiteSuffix = "test" };
                pC.ProjectRoots.Add(satu);
                ProfileStore.Save(pC);

                var nama = HostsTool.SemuaNamaSitus();
                var gabung = string.Join(", ", nama.ToArray());

                Ok("Nama dari profil pertama ikut", nama.Contains("alfa.test"), gabung);
                Ok("Nama dari profil KEDUA ikut juga", nama.Contains("gama.dev"), gabung);
                Ok("Akhiran tiap profil dihormati", nama.Contains("beta.test"), gabung);
                Ok("Nama yang sama tidak berlipat", nama.Count == 3, gabung);
                Ok("Terurut", nama.SequenceEqual(nama.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), gabung);
                Ok("localhost tidak ikut", !nama.Contains("localhost"), gabung);
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiBahasa()
        {
            Bagian("Bahasa");

            // Kamus dibangun di konstruktor statis. Satu kunci ganda saja membuat
            // SELURUH aplikasi mati begitu teks pertama diterjemahkan, dan
            // kompilasi tidak melihatnya sama sekali - sudah pernah terjadi.
            string ledak = null;
            try
            {
                foreach (var kode in Lang.Semua)
                {
                    Lang.Pakai(kode);
                    Lang.T("Beranda");
                }
            }
            catch (Exception ex)
            {
                var akar = ex;
                while (akar.InnerException != null) akar = akar.InnerException;
                ledak = akar.Message;
            }
            finally { Lang.Pakai(Lang.Indonesia); }
            Ok("Kamus tiap bahasa terbentuk (tidak ada kunci ganda)", ledak == null, ledak);

            Lang.Pakai(Lang.Inggris);
            Ok("Teks diterjemahkan", Lang.T("Beranda") == "Home", Lang.T("Beranda"));
            Ok("Teks tanpa padanan jatuh ke Indonesia",
               Lang.T("Kalimat yang tidak ada di kamus") == "Kalimat yang tidak ada di kamus");
            Lang.Pakai(Lang.Indonesia);
            Ok("Bahasa Indonesia mengembalikan kuncinya sendiri", Lang.T("Beranda") == "Beranda");

            // Parser markup extension WPF memakan "\" di dalam argumen, jadi
            // {loc:T 'etc\catalog.ini'} tampil sebagai "etccatalog.ini" tanpa
            // galat apa pun. Teks berbackslash harus memakai bentuk elemen
            // <loc:T Teks="..."/>.
            var dirApp = CariFolderApp();
            if (dirApp == null)
            {
                Ok("Folder sumber XAML ditemukan", false, "tidak ketemu dari " + AppDomain.CurrentDomain.BaseDirectory);
                return;
            }
            var nakal = new List<string>();
            foreach (var f in Directory.GetFiles(dirApp, "*.xaml", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{loc:T\s+'([^']*)'\}"))
                    if (m.Groups[1].Value.IndexOf('\\') >= 0)
                        nakal.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
            }
            Ok("Tidak ada loc:T berbentuk atribut yang memuat backslash",
               nakal.Count == 0, string.Join(" | ", nakal.ToArray()));

            UjiBanjarTidakBercampur(dirApp);
        }

        static void UjiBanjarTidakBercampur(string dirApp)
        {
            // Keluhan yang menimbulkan uji ini: bahasa Banjarnya "bercampur".
            // Penyebabnya bukan kata yang salah, melainkan kata tugas Indonesia
            // yang lolos di tengah kalimat - satu "yang" atau "dan" saja sudah
            // membuat seluruh kalimat terasa bukan Banjar. Kata di bawah ini
            // punya padanan Banjar yang wajib dipakai (nang, wan, matan, gasan,
            // kada, atawa, amun, samunyaan, barakas, daptar, surang, kawa,
            // lawan, hanyar, rancak, suah, ngaran, laman, kulihan, janis).
            var terlarang = new[]
            {
                "yang", "dan", "dari", "untuk", "tidak", "atau", "kalau", "semua",
                "berkas", "daftar", "sendiri", "bisa", "dengan", "baru", "sering",
                "pernah", "nama", "halaman", "hasil", "jenis", "harus", "setiap",
            };

            Lang.Pakai(Lang.Banjar);
            var bocor = new List<string>();
            foreach (var f in Directory.GetFiles(dirApp, "*.xaml", SearchOption.AllDirectories))
            {
                var isi = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(isi, @"\{loc:T\s+'([^']*)'\}"))
                    PeriksaBocor(m.Groups[1].Value, terlarang, bocor);
                foreach (Match m in Regex.Matches(isi, "<loc:T\\s+Teks=\"([^\"]*)\"\\s*/>"))
                    PeriksaBocor(WebUtility.HtmlDecode(m.Groups[1].Value), terlarang, bocor);
            }
            Lang.Pakai(Lang.Indonesia);

            Ok("Terjemahan Banjar tidak bercampur kata tugas Indonesia",
               bocor.Count == 0, string.Join(" | ", bocor.ToArray()));
        }

        static void PeriksaBocor(string kunci, string[] terlarang, List<string> bocor)
        {
            var hasil = Lang.T(kunci);
            if (hasil == kunci) return;   // memang belum/tidak perlu diterjemahkan
            foreach (var w in terlarang)
                if (Regex.IsMatch(hasil, "(?<![A-Za-z])" + w + "(?![A-Za-z])", RegexOptions.IgnoreCase))
                    bocor.Add("\"" + w + "\" di: " + hasil.Substring(0, Math.Min(50, hasil.Length)));
        }

        static string CariFolderApp()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null)
            {
                var calon = Path.Combine(d.FullName, Path.Combine("src", "Phoron.App"));
                if (Directory.Exists(calon)) return calon;
                d = d.Parent;
            }
            return null;
        }

        static void UjiNodeApps()
        {
            Bagian("Proyek Node");
            var proyek = Path.Combine(Paths.Root, "proyek-node");
            Directory.CreateDirectory(proyek);
            // package.json sungguhan punya nilai yang memuat kurung kurawal dan
            // tanda kutip di dalamnya - pengurai sederhana gampang tersandung di
            // situ, jadi contoh ini sengaja dibuat menyerupai yang asli.
            File.WriteAllText(Path.Combine(proyek, "package.json"),
                "{\n" +
                "  \"name\": \"contoh\",\n" +
                "  \"scripts\": {\n" +
                "    \"dev\": \"next dev --experimental-https\",\n" +
                "    \"build\": \"astro check && astro build\",\n" +
                "    \"aneh\": \"node -e \\\"console.log({a:1})\\\"\",\n" +
                "    \"start\": \"node server.mjs\"\n" +
                "  },\n" +
                "  \"dependencies\": { \"next\": \"^15.0.0\" }\n" +
                "}\n");

            var skrip = PackageJson.Scripts(proyek);
            Ok("Nama skrip terbaca sesuai urutan",
                skrip.SequenceEqual(new[] { "dev", "build", "aneh", "start" }),
                string.Join(",", skrip));
            Ok("Kurung kurawal di dalam nilai tidak mengacaukan pembacaan",
                skrip.Count == 4, string.Join(",", skrip));
            Ok("Kerangka terdeteksi dari dependensi",
                PackageJson.DetectFramework(proyek) == "next",
                PackageJson.DetectFramework(proyek));
            Ok("Tanpa berkas kunci, pengelolanya npm",
                PackageJson.DetectManager(proyek) == "npm");

            File.WriteAllText(Path.Combine(proyek, "pnpm-lock.yaml"), "");
            Ok("pnpm-lock.yaml menentukan pnpm", PackageJson.DetectManager(proyek) == "pnpm");
            File.Delete(Path.Combine(proyek, "pnpm-lock.yaml"));

            Ok("Folder tanpa package.json bukan proyek Node",
                !PackageJson.LooksLikeNodeProject(Paths.Tmp));
            Ok("Daftar skrip folder bukan proyek itu kosong",
                PackageJson.Scripts(Paths.Tmp).Count == 0);

            // Bolak-balik ke apps.ini - jalur Windows memuat ":" dan "\",
            // keduanya harus selamat sebagai nama seksi.
            var apps = new List<NodeApp>
            {
                new NodeApp { Path = proyek, Name = "Contoh", Script = "build", Manager = "pnpm", NodeId = "node-v18" },
                new NodeApp { Path = @"D:\kerjaan\situs", Script = "dev", Manager = "npm" },
            };
            NodeAppStore.SaveAll(apps);
            var muat = NodeAppStore.LoadAll();
            Ok("Dua proyek tersimpan dan terbaca lagi", muat.Count == 2, muat.Count.ToString());
            var satu = muat.FirstOrDefault(x => x.Path == proyek);
            Ok("Jalur, skrip, pengelola, dan Node bolak-balik utuh",
                satu != null && satu.Name == "Contoh" && satu.Script == "build"
                && satu.Manager == "pnpm" && satu.NodeId == "node-v18",
                satu == null ? "null" : satu.Script + "/" + satu.Manager + "/" + satu.NodeId);
            var dua = muat.FirstOrDefault(x => x.Path == @"D:\kerjaan\situs");
            Ok("Jalur berhuruf drive lain tetap utuh", dua != null, "");
            Ok("Nama tampilan jatuh ke nama folder bila kosong",
                dua != null && dua.DisplayName == "situs",
                dua == null ? "null" : dua.DisplayName);

            File.Delete(Path.Combine(Paths.Root, "apps.ini"));
            Directory.Delete(proyek, true);
        }

        static void UjiPembaruan()
        {
            Bagian("Cek pembaruan");
            // Perbandingan angka per bagian, bukan teks. Secara abjad "1.10.0"
            // lebih kecil daripada "1.9.0" - kalau dibandingkan sebagai teks,
            // pembaruan justru berhenti ditawarkan persis saat versi minor
            // menembus angka sepuluh.
            Ok("1.10.0 lebih baru daripada 1.9.0", Updater.LebihBaru("1.10.0", "1.9.0"));
            Ok("1.7.1 lebih baru daripada 1.7.0", Updater.LebihBaru("1.7.1", "1.7.0"));
            Ok("Versi sama bukan pembaruan", !Updater.LebihBaru("1.7.1", "1.7.1"));
            Ok("Versi lebih tua bukan pembaruan", !Updater.LebihBaru("1.6.0", "1.7.0"));
            Ok("Awalan v diabaikan", Updater.LebihBaru("v1.8.0", "1.7.1"));
            // Versi rakitan membawa ekor "+<sha>"; System.Version menolaknya
            // mentah-mentah, dan tanpa pembersihan ini pengecekan selalu diam.
            Ok("Ekor +sha tidak mengacaukan perbandingan",
                Updater.LebihBaru("1.8.0", "1.7.1+efdc376628af09d14093dd6a1113dcc0"));
            Ok("Teks sampah tidak dianggap pembaruan", !Updater.LebihBaru("entah", "1.0.0"));

            var json = "{\"tag_name\":\"v1.9.0\","
                     + "\"html_url\":\"https://github.com/Cyserrex/Phoron/releases/tag/v1.9.0\","
                     + "\"body\":\"Baris satu\r\nBaris \\\"dua\\\"\","
                     + "\"assets\":[{\"browser_download_url\":\"https://x/Phoron.exe\"},"
                     + "{\"browser_download_url\":\"https://x/Phoron-1.9.0-Setup.exe\"}]}";
            var h = Updater.Urai(json);
            Ok("Versi terurai dari tag_name", h.Versi == "1.9.0", h.Versi);
            Ok("Halaman rilis terurai", h.UrlHalaman.EndsWith("/releases/tag/v1.9.0"), h.UrlHalaman);
            // Rilis memuat beberapa aset; yang diambil harus installer-nya,
            // bukan aset pertama yang kebetulan Phoron.exe.
            Ok("Aset yang diambil adalah installer, bukan exe biasa",
                h.UrlInstaller.EndsWith("Phoron-1.9.0-Setup.exe"), h.UrlInstaller);
            Ok("Catatan rilis dibaca dan escape-nya dipulihkan",
                h.Catatan.Contains("Baris satu") && h.Catatan.Contains("\"dua\""), h.Catatan);
            Ok("Tidak ada galat pada jawaban yang sah", h.Galat == null, h.Galat ?? "");

            var rusak = Updater.Urai("{\"pesan\":\"apa pun\"}");
            Ok("Jawaban tanpa tag_name dilaporkan sebagai galat", rusak.Galat != null);
            Ok("Jawaban kosong dilaporkan sebagai galat", Updater.Urai("").Galat != null);
        }

        static void UjiAutostart()
        {
            Bagian("Autostart");
            // Registri mesin ini TIDAK disentuh - yang diuji adalah penguraian
            // nilai Run, bagian yang menentukan apakah sakelar di Pengaturan
            // tampil menyala atau padam.
            const string exe = @"C:\Phoron\Phoron.exe";
            Ok("Nilai berkutip dengan argumen dikenali",
                Autostart.MenunjukKe("\"" + exe + "\" --tray", exe));
            Ok("Nilai berkutip tanpa argumen dikenali",
                Autostart.MenunjukKe("\"" + exe + "\"", exe));
            Ok("Nilai tanpa kutip dikenali",
                Autostart.MenunjukKe(exe + " --tray", exe));
            Ok("Beda huruf besar-kecil tetap dikenali",
                Autostart.MenunjukKe("\"c:\\phoron\\PHORON.EXE\" --tray", exe));
            Ok("Salinan di folder lain TIDAK dianggap milik kita",
                !Autostart.MenunjukKe("\"D:\\Lain\\Phoron.exe\" --tray", exe));
            Ok("Nilai kosong tidak dianggap terdaftar", !Autostart.MenunjukKe("", exe));
            Ok("Nama nilai registri sama dengan yang ditulis installer",
                Autostart.NamaNilai == "Phoron");
        }

        static void UjiPortCheck()
        {
            Bagian("PortCheck");
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            try
            {
                var pakai = PortCheck.Check(port);
                Ok("Port yang sedang didengarkan terdeteksi terpakai", pakai.InUse);
                Ok("Pesan konflik menyebut nomor port", pakai.Describe().Contains(port.ToString()));
            }
            finally { l.Stop(); }

            // Port yang baru saja dilepas harus kembali terbaca bebas.
            Ok("Port yang sudah dilepas terbaca bebas", PortCheck.IsFree(port));
        }
    }
}
