using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Uji ujung-ke-ujung: nyalakan Apache sungguhan untuk tiap kombinasi
    /// PHP+Apache yang ada, ambil halaman PHP lewat HTTP, dan pastikan versi
    /// yang menjawab persis versi yang diminta profil. Inilah satu-satunya cara
    /// membuktikan bahwa "switch" benar-benar bekerja - konfigurasi yang lolos
    /// httpd -t pun masih bisa melayani versi PHP yang salah.
    ///
    /// Dipisah dari uji biasa karena mengikat port dan makan waktu; dijalankan
    /// dengan: build.bat live
    /// </summary>
    public static class LiveTest
    {
        const int Port = 8931;   // jauh dari 80/8080 supaya tidak menabrak apa pun

        static string FolderKedua { get { return Path.Combine(Paths.Root, "proyek-lain"); } }

        public static int Jalankan()
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "phoron-live-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sandbox);
            Paths.Root = sandbox;
            Console.WriteLine("Sandbox: " + sandbox);

            int lulus = 0, gagal = 0;
            try
            {
                Directory.CreateDirectory(Paths.Www);
                File.WriteAllText(Path.Combine(Paths.Www, "index.php"),
                    "<?php echo 'PHORON|' . PHP_VERSION . '|' . php_sapi_name();",
                    new UTF8Encoding(false));

                // Folder proyek KEDUA, di luar www: inilah yang membuktikan
                // dukungan banyak folder benar-benar sampai ke Apache, bukan
                // sekadar muncul di daftar situs.
                // Subfolder DI DALAM akar utama - inilah bentuk alamat yang
                // dipakai orang sehari-hari (http://localhost/simpdam/), dan
                // yang paling mudah rusak oleh pengalihan akar yang dipasang
                // terlalu luas.
                Directory.CreateDirectory(Path.Combine(Paths.Www, "subproyek"));
                File.WriteAllText(Path.Combine(Paths.Www, "subproyek", "index.php"),
                    "<?php echo 'SUB|' . PHP_VERSION;", new UTF8Encoding(false));

                Directory.CreateDirectory(Path.Combine(FolderKedua, "tokolive"));
                File.WriteAllText(Path.Combine(FolderKedua, "tokolive", "index.php"),
                    "<?php echo 'KEDUA|' . __DIR__;", new UTF8Encoding(false));

                var pkgs = BinScanner.ScanAll(Settings.DefaultBinRoots()
                    .Concat(new[] { @"C:\laragon\bin" }).Distinct());
                var apaches = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
                var phps = pkgs.Where(p => p.Kind == BinKind.Php).ToList();

                if (apaches.Count == 0 || phps.Count == 0)
                {
                    Console.WriteLine("Dilewati: butuh minimal satu Apache dan satu PHP.");
                    return 0;
                }
                if (!PortCheck.IsFree(Port) || !PortCheck.IsFree(Port + 1))
                {
                    Console.WriteLine("Dilewati: port " + Port + "/" + (Port + 1) + " sedang dipakai.");
                    return 0;
                }

                // Sertifikat uji ini self-signed dan baru dibuat detik ini, jadi
                // tidak akan pernah lolos validasi rantai. Yang diuji di sini
                // adalah Apache melayani HTTPS-nya, bukan kepercayaan Windows.
                ServicePointManager.ServerCertificateValidationCallback =
                    (a, b, c, d) => true;

                foreach (var apache in apaches)
                {
                    var php = phps.FirstOrDefault(p => string.Equals(p.Compiler, apache.Compiler,
                                                                     StringComparison.OrdinalIgnoreCase));
                    if (php == null) continue;
                    if (Coba(apache, php)) lulus++; else gagal++;
                }

                var mysql = pkgs.FirstOrDefault(p => p.Kind == BinKind.MySql);
                if (mysql != null) { if (CobaMySql(mysql)) lulus++; else gagal++; }
            }
            finally
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("=== uji hidup: " + lulus + " lulus, " + gagal + " gagal ===");
            return gagal == 0 ? 0 : 1;
        }

        static bool Coba(BinPackage apache, BinPackage php)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + apache.Id + " + " + php.Id);
            var profil = new Profile
            {
                Name = "Live " + php.Version,
                ApacheId = apache.Id,
                PhpId = php.Id,
                HttpPort = Port,
                HttpsPort = Port + 1,
            };
            profil.ProjectRoots.Add(Paths.Www);
            profil.ProjectRoots.Add(FolderKedua);

            // Sertifikat dibuat lebih dulu, persis seperti yang dilakukan Engine
            // pada Apply pertama - tanpa itu port HTTPS tidak dibuka sama sekali.
            var errSsl = SslTool.Generate(apache, profil.SiteSuffix);
            if (errSsl != null) Console.WriteLine("   peringatan SSL: " + errSsl);

            var situs = SiteScanner.Scan(profil);
            var cfg = ConfigWriter.Build(profil, php, apache, null, null, situs);
            foreach (var w in cfg.Warnings) Console.WriteLine("   peringatan: " + w);

            var svc = new ServiceManager();
            svc.Log += t => Console.WriteLine("   " + t);
            try
            {
                var ok = svc.StartWebAsync(profil, apache, php, cfg).GetAwaiter().GetResult();
                if (!ok) { Console.WriteLine("   GAGAL: Apache tidak menyala."); return false; }

                // Akar kini menampilkan beranda Phoron. Yang WAJIB tetap utuh:
                // berkas milik folder proyek masih terjangkau, dan subfolder
                // tidak ikut dibajak - dua hal itulah yang bisa rusak oleh
                // pengalihan yang dipasang terlalu luas.
                var akar = Ambil("http://127.0.0.1:" + Port + "/");
                if (akar.IndexOf("Profil aktif", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("   GAGAL: akar tidak menampilkan beranda Phoron. Jawaban: "
                                      + akar.Substring(0, Math.Min(120, akar.Length)));
                    return false;
                }
                Console.WriteLine("   ok: akar / menampilkan beranda Phoron");

                var sub = Ambil("http://127.0.0.1:" + Port + "/subproyek/");
                if (!sub.StartsWith("SUB|"))
                {
                    Console.WriteLine("   GAGAL: subfolder ikut dibajak pengalihan akar. Jawaban: "
                                      + sub.Substring(0, Math.Min(120, sub.Length)));
                    return false;
                }
                Console.WriteLine("   ok: subfolder /subproyek/ tetap milik proyek");

                var body = Ambil("http://127.0.0.1:" + Port + "/index.php");
                Console.WriteLine("   jawaban /index.php: " + body);

                if (!body.StartsWith("PHORON|"))
                {
                    // Tanda paling khas: PHP tidak dieksekusi, jadi kode sumbernya
                    // ikut terkirim ke browser.
                    Console.WriteLine("   GAGAL: PHP tidak dijalankan oleh Apache.");
                    return false;
                }
                var versi = body.Split('|')[1];
                if (versi != php.Version)
                {
                    Console.WriteLine("   GAGAL: yang menjawab PHP " + versi
                                      + ", padahal profil meminta " + php.Version + ".");
                    return false;
                }
                Console.WriteLine("   ok: PHP " + versi + " (" + body.Split('|')[2] + ") menjawab dari port " + Port);

                // Vhost dari folder proyek kedua. Berkas hosts tidak disentuh -
                // nama situsnya dikirim lewat header Host, yang persis itulah yang
                // dipakai Apache untuk memilih VirtualHost.
                var kedua = Ambil("http://127.0.0.1:" + Port + "/", "tokolive." + SiteScanner.Suffix(profil));
                Console.WriteLine("   folder kedua: " + kedua);
                if (!kedua.StartsWith("KEDUA|") || !kedua.Contains("proyek-lain"))
                {
                    Console.WriteLine("   GAGAL: situs di folder proyek kedua tidak dilayani.");
                    return false;
                }
                Console.WriteLine("   ok: situs di luar www ikut dilayani Apache");

                // HTTPS. Inilah yang gagal diam-diam sebelum sertifikat dibuat
                // otomatis: browser hanya menjawab "tidak dapat tersambung".
                if (!cfg.SslEnabled)
                {
                    Console.WriteLine("   GAGAL: sertifikat ada tapi HTTPS tidak diaktifkan.");
                    return false;
                }
                // Lewat /index.php, bukan akar: akar kini dialihkan ke beranda
                // Phoron, dan yang perlu dibuktikan di sini adalah PHP benar-benar
                // dijalankan di atas TLS.
                var aman = Ambil("https://127.0.0.1:" + (Port + 1) + "/index.php");
                Console.WriteLine("   https: " + aman);
                if (!aman.StartsWith("PHORON|"))
                {
                    Console.WriteLine("   GAGAL: HTTPS tidak melayani halaman.");
                    return false;
                }
                Console.WriteLine("   ok: HTTPS melayani di port " + (Port + 1));

                // Beranda Phoron lewat Alias. Inilah yang membuatnya tetap
                // terjangkau walau folder proyeknya milik orang lain dan sudah
                // punya index.php sendiri.
                var brnd = Ambil("http://127.0.0.1:" + Port + Phoron.Core.Beranda.Alias + "/");
                if (brnd.IndexOf("Phoron", StringComparison.Ordinal) < 0
                    || brnd.IndexOf("Profil aktif", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("   GAGAL: beranda /phoron tidak dilayani. Jawaban: "
                                      + brnd.Substring(0, Math.Min(160, brnd.Length)));
                    return false;
                }
                Console.WriteLine("   ok: beranda Phoron dilayani di " + Phoron.Core.Beranda.Alias);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("   GAGAL: " + ex.Message);
                return false;
            }
            finally
            {
                svc.StopWebAsync().GetAwaiter().GetResult();
                // Beri jeda supaya port benar-benar dilepas sebelum kombinasi
                // berikutnya mencoba mengikatnya.
                Task.Delay(1500).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Menyalakan MySQL dari nol: folder data belum ada, jadi jalur
        /// --initialize-insecure ikut teruji. Inilah bagian yang paling sering
        /// gagal senyap - mysqld yang gagal inisialisasi tetap keluar dengan
        /// kode 0 pada sebagian versi.
        /// </summary>
        static bool CobaMySql(BinPackage mysql)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + mysql.Id);
            const int PortDb = 3398;
            if (!PortCheck.IsFree(PortDb))
            {
                Console.WriteLine("   dilewati: port " + PortDb + " dipakai.");
                return true;
            }

            var profil = new Profile { Name = "Live db", MySqlId = mysql.Id, MySqlPort = PortDb };
            var hasil = new ConfigWriter.Result();
            ConfigWriter.WriteMyIni(profil, mysql, hasil);

            var svc = new ServiceManager();
            svc.Log += t => Console.WriteLine("   " + t);
            try
            {
                if (!svc.StartDbAsync(profil, mysql).GetAwaiter().GetResult())
                {
                    Console.WriteLine("   GAGAL: MySQL tidak menyala.");
                    return false;
                }
                var klien = Path.Combine(mysql.Path, "bin", "mysql.exe");
                var res = Shell.Run(klien,
                    "--protocol=tcp --port=" + PortDb + " -u root -N -B -e \"SELECT VERSION();\"",
                    mysql.Path, 30000);
                var versi = res.StdOut.Trim();
                Console.WriteLine("   SELECT VERSION() -> " + versi);
                if (!versi.StartsWith(mysql.Version))
                {
                    Console.WriteLine("   GAGAL: versi yang menjawab bukan " + mysql.Version + ".");
                    return false;
                }
                Console.WriteLine("   ok: MySQL " + versi + " melayani kueri di port " + PortDb);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("   GAGAL: " + ex.Message);
                return false;
            }
            finally
            {
                svc.StopDbGracefullyAsync(profil, mysql).GetAwaiter().GetResult();
                Task.Delay(1500).GetAwaiter().GetResult();
            }
        }

        static string Ambil(string url, string host = null)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 15000;
                if (host != null) req.Host = host;
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                    return r.ReadToEnd().Trim();
            }
            catch (WebException ex)
            {
                if (ex.Response == null) return "(tidak ada jawaban: " + ex.Message + ")";
                using (var r = new StreamReader(ex.Response.GetResponseStream()))
                    return "(HTTP error) " + r.ReadToEnd().Trim();
            }
        }
    }
}
