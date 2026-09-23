using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        const string VarTunda = "PHORON_UJI_MYSQLD_TUNDA_MS";

        /// <summary>
        /// mysqld tiruan: harness ini menjalankan salinan dirinya bernama
        /// mysqld.exe. Ia membuka port-nya baru sesudah jeda (seperti mysqld yang
        /// sedang memulihkan InnoDB), dan berhenti DENGAN RAPI saat event
        /// "MySQLShutdown&lt;PID&gt;" disetel - persis jalan yang dipakai Phoron.
        /// Berhenti rapi meninggalkan berkas "rapi-&lt;pid&gt;" di sebelah exe-nya;
        /// dibunuh tidak meninggalkan apa pun.
        /// </summary>
        static int MysqldTiruan(string[] args)
        {
            var ini = args[0].Substring("--defaults-file=".Length).Trim('"');
            var m = Regex.Match(File.ReadAllText(ini), @"(?m)^port=(\d+)");
            int port = int.Parse(m.Groups[1].Value);
            int tunda;
            if (!int.TryParse(Environment.GetEnvironmentVariable(VarTunda), out tunda)) tunda = 0;

            var pid = Process.GetCurrentProcess().Id;
            using (var berhenti = new EventWaitHandle(false, EventResetMode.ManualReset, "MySQLShutdown" + pid))
            {
                TcpListener dengar = null;
                var mulai = DateTime.Now;
                try
                {
                    // Batas umur: tiruan yang terlupa tidak boleh hidup selamanya.
                    while ((DateTime.Now - mulai).TotalSeconds < 120)
                    {
                        if (dengar == null && tunda >= 0 && (DateTime.Now - mulai).TotalMilliseconds >= tunda)
                        {
                            dengar = new TcpListener(IPAddress.Loopback, port);
                            dengar.Start();
                        }
                        if (berhenti.WaitOne(100))
                        {
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rapi-" + pid), "");
                            return 0;
                        }
                    }
                    return 1;
                }
                finally { if (dengar != null) dengar.Stop(); }
            }
        }

        /// <summary>
        /// Penjaga untuk 1.36.0: MySQL yang lambat siap tidak dibunuh.
        ///
        /// Tanda siap mysqld adalah port-nya terbuka, dan itu baru terjadi
        /// sesudah pemulihan InnoDB selesai. Dulu start menyerah pada detik ke-20
        /// lalu MEMBUNUH mysqld: sesudah komputer mati mendadak dengan basis data
        /// besar, pemulihan terputus, start berikutnya memulihkan lagi lalu
        /// dibunuh lagi - MySQL tidak pernah bisa nyala dari Phoron.
        /// </summary>
        static void UjiMysqlLambatSiap()
        {
            Bagian("MySQL yang lambat siap");

            var akar = Path.Combine(Path.GetTempPath(), "phoron-uji-mysqld-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var paket = Path.Combine(akar, "mysql-tiruan-9.9.9-winx64");
            var bin = Path.Combine(paket, "bin");
            Directory.CreateDirectory(bin);
            var sini = AppDomain.CurrentDomain.BaseDirectory;
            File.Copy(Path.Combine(sini, "Phoron.Tests.exe"), Path.Combine(bin, "mysqld.exe"));
            File.Copy(Path.Combine(sini, "Phoron.Core.dll"), Path.Combine(bin, "Phoron.Core.dll"));
            if (File.Exists(Path.Combine(sini, "Phoron.Tests.exe.config")))
                File.Copy(Path.Combine(sini, "Phoron.Tests.exe.config"), Path.Combine(bin, "mysqld.exe.config"));

            var pkg = new BinPackage { Kind = BinKind.MySql, Id = "mysql-tiruan-9.9.9-winx64", Path = paket, SourceRoot = akar, Version = "9.9.9" };
            BinScanner.TandaiKembar(new List<BinPackage> { pkg });
            var port = 39000 + new Random().Next(500);
            var profil = new Profile { Name = "uji", MySqlPort = port };
            Directory.CreateDirectory(Paths.EtcMysql);
            File.WriteAllText(Path.Combine(Paths.EtcMysql, "my.ini"), "[mysqld]\r\nport=" + port + "\r\n");
            var dataDir = ConfigWriter.MySqlDataDir(pkg);
            Directory.CreateDirectory(Path.Combine(dataDir, "mysql"));   // sudah diinisialisasi

            Func<int, int, Tuple<bool, List<string>, long>> jalankan = (tunda, batasDetik) =>
            {
                Environment.SetEnvironmentVariable(VarTunda, tunda.ToString());
                var svc = new ServiceManager();
                var f = typeof(ServiceManager).GetField("BatasSiapDbDetik", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && batasDetik > 0) f.SetValue(svc, batasDetik);
                var log = new List<string>();
                svc.Log += s => { lock (log) log.Add(s); };
                var sw = Stopwatch.StartNew();
                bool ok = svc.StartDbAsync(profil, pkg).GetAwaiter().GetResult();
                if (ok) svc.StopDbGracefullyAsync(profil, pkg).GetAwaiter().GetResult();
                return Tuple.Create(ok, log, sw.ElapsedMilliseconds);
            };

            try
            {
                // Siap pada detik ke-22: lewat dari batas lama.
                var lambat = jalankan(22000, 0);
                Ok("mysqld yang baru siap pada detik ke-22 tidak dibunuh, dan start berhasil",
                   lambat.Item1, lambat.Item3 + " ms; " + string.Join(" | ", lambat.Item2));
                Ok("... dan pengguna diberi tahu bahwa MySQL sedang dipulihkan",
                   lambat.Item2.Any(s => s.Contains("memulihkan")), string.Join(" | ", lambat.Item2));

                // Tidak pernah siap, batas dipendekkan: yang diharapkan berhenti RAPI.
                foreach (var f in Directory.GetFiles(bin, "rapi-*")) File.Delete(f);
                var macet = jalankan(-1, 3);
                Ok("mysqld yang tidak pernah siap: start gagal pada batasnya", !macet.Item1 && macet.Item3 < 30000,
                   macet.Item3 + " ms");
                Ok("... dan dihentikan dengan rapi (event MySQLShutdown), bukan dibunuh",
                   Directory.GetFiles(bin, "rapi-*").Length == 1, string.Join(" | ", macet.Item2));
            }
            finally
            {
                Environment.SetEnvironmentVariable(VarTunda, null);
                foreach (var p in Process.GetProcessesByName("mysqld"))
                    try { if (p.MainModule.FileName.StartsWith(akar, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
                Thread.Sleep(300);
                try { Directory.Delete(akar, true); } catch { }
                try { Directory.Delete(dataDir, true); } catch { }
            }
        }
    }
}
