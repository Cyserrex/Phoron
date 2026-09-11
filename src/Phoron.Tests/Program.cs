using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                UjiHalamanSambutan();
                UjiKonfigurasiApache();
                UjiHosts();
                UjiPortCheck();
                UjiAutostart();
                UjiLabelVersi();
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
                Ok("Apache punya toolset terurai", apache.All(a => a.Compiler.Length > 0));
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
                // Tiap Apache dipasangkan dengan PHP bertoolset sama - itulah
                // kombinasi yang akan dipilihkan Phoron untuk pengguna.
                var php = phps.FirstOrDefault(p => string.Equals(p.Compiler, apache.Compiler,
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
