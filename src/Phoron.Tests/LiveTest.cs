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

                var pkgs = BinScanner.ScanAll(Settings.DefaultBinRoots()
                    .Concat(new[] { @"C:\laragon\bin" }).Distinct());
                var apaches = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
                var phps = pkgs.Where(p => p.Kind == BinKind.Php).ToList();

                if (apaches.Count == 0 || phps.Count == 0)
                {
                    Console.WriteLine("Dilewati: butuh minimal satu Apache dan satu PHP.");
                    return 0;
                }
                if (!PortCheck.IsFree(Port))
                {
                    Console.WriteLine("Dilewati: port " + Port + " sedang dipakai.");
                    return 0;
                }

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
            var situs = SiteScanner.Scan(profil);
            var cfg = ConfigWriter.Build(profil, php, apache, null, null, situs);
            foreach (var w in cfg.Warnings) Console.WriteLine("   peringatan: " + w);

            var svc = new ServiceManager();
            svc.Log += t => Console.WriteLine("   " + t);
            try
            {
                var ok = svc.StartWebAsync(profil, apache, php, cfg).GetAwaiter().GetResult();
                if (!ok) { Console.WriteLine("   GAGAL: Apache tidak menyala."); return false; }

                var body = Ambil("http://127.0.0.1:" + Port + "/");
                Console.WriteLine("   jawaban: " + body);

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

        static string Ambil(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 15000;
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
