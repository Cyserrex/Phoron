using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>php-cgi tiruan: mendengarkan di port "-b" sampai dibunuh.</summary>
        static int PhpCgiTiruan(string[] args)
        {
            var port = int.Parse(args[1].Substring(args[1].LastIndexOf(':') + 1));
            var dengar = new TcpListener(IPAddress.Loopback, port);
            dengar.Start();
            Thread.Sleep(120000);   // batas umur: tiruan yang terlupa tidak hidup selamanya
            return 0;
        }

        /// <summary>
        /// Penjaga untuk 1.36.0: php-cgi per situs yang mati sendiri dinyalakan
        /// ulang.
        ///
        /// Dulu kolam yang crash (access violation di oci8, paling mungkin pada
        /// PHP 5.6 yang hanya satu proses) cukup dicatat satu baris; situs itu
        /// membalas 503 sampai web server dinyalakan ulang, sementara lampu
        /// Apache tetap hijau. Kini ia dinyalakan ulang di port yang sama, paling
        /// banyak tiga kali per menit - crash yang berulang terus dihentikan dan
        /// dilaporkan, bukan diputar tanpa akhir.
        /// </summary>
        static void UjiKolamBangkit()
        {
            Bagian("php-cgi per situs yang mati sendiri");

            var akar = Path.Combine(Path.GetTempPath(), "phoron-uji-kolam-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var paket = Path.Combine(akar, "php-9.9.9-Win32-vs17-x64");
            Directory.CreateDirectory(paket);
            var sini = AppDomain.CurrentDomain.BaseDirectory;
            File.Copy(Path.Combine(sini, "Phoron.Tests.exe"), Path.Combine(paket, "php-cgi.exe"));
            File.Copy(Path.Combine(sini, "Phoron.Core.dll"), Path.Combine(paket, "Phoron.Core.dll"));
            if (File.Exists(Path.Combine(sini, "Phoron.Tests.exe.config")))
                File.Copy(Path.Combine(sini, "Phoron.Tests.exe.config"), Path.Combine(paket, "php-cgi.exe.config"));

            var php = new BinPackage { Kind = BinKind.Php, Id = "php-9.9.9-Win32-vs17-x64", Path = paket, SourceRoot = akar, Version = "9.9.9", Arch = "x64" };
            var port = 39600 + new Random().Next(300);
            var cfg = new ConfigWriter.Result();
            cfg.Kolam.Add(new ConfigWriter.Kolam { Php = php, Port = port, FolderIni = akar, Situs = new List<Site> { new Site { Folder = "situs-uji" } } });

            var svc = new ServiceManager();
            var log = new List<string>();
            svc.Log += s => { lock (log) log.Add(s); };
            var bf = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ServiceManager).GetMethod("SetState", bf).Invoke(svc, new object[] { ServiceKind.Web, ServiceState.Jalan });

            Func<Process> pendengar = () =>
            {
                for (int i = 0; i < 40; i++)
                {
                    var p = Process.GetProcessesByName("php-cgi").FirstOrDefault(x =>
                    {
                        try { return x.MainModule.FileName.StartsWith(akar, StringComparison.OrdinalIgnoreCase) && !x.HasExited; }
                        catch { return false; }
                    });
                    if (p != null && !PortCheck.IsFree(port)) return p;
                    Thread.Sleep(100);
                }
                return null;
            };

            try
            {
                ((Task)typeof(ServiceManager).GetMethod("MulaiKolamAsync", bf).Invoke(svc, new object[] { cfg })).GetAwaiter().GetResult();
                var awal = pendengar();
                Ok("Kolam tiruan menyala", awal != null, string.Join(" | ", log));
                if (awal == null) return;

                awal.Kill(); awal.WaitForExit(5000);
                var baru = pendengar();
                Ok("Kolam yang mati sendiri dinyalakan ulang di port yang sama",
                   baru != null && baru.Id != awal.Id, string.Join(" | ", log));

                // Terus mati: sesudah tiga kebangkitan dalam semenit, berhenti.
                for (int i = 0; i < 3 && baru != null; i++)
                {
                    baru.Kill(); baru.WaitForExit(5000);
                    baru = pendengar();
                }
                Thread.Sleep(1500);
                Ok("Crash yang berulang tidak diputar tanpa akhir",
                   pendengar() == null, "masih ada php-cgi yang hidup");
                Ok("... dan pengguna diberi tahu kenapa situs itu berhenti",
                   log.Any(s => s.Contains("berulang kali")), string.Join(" | ", log));
            }
            finally
            {
                try { typeof(ServiceManager).GetMethod("StopFastCgi", bf).Invoke(svc, null); } catch { }
                foreach (var p in Process.GetProcessesByName("php-cgi"))
                    try { if (p.MainModule.FileName.StartsWith(akar, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
                Thread.Sleep(300);
                try { Directory.Delete(akar, true); } catch { }
            }
        }
    }
}
