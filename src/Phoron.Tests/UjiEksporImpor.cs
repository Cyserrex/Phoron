using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        const string VarDump = "PHORON_UJI_DUMP";

        /// <summary>
        /// mysqldump/mysql tiruan (argumen pertama "--no-defaults"). Menurut
        /// PHORON_UJI_DUMP: "sukses" menulis BARU ke --result-file, "gagal"
        /// menulis setengah isi lalu keluar dengan galat, "tidur" diam lama.
        /// </summary>
        static int DumpTiruan(string[] args)
        {
            var mode = Environment.GetEnvironmentVariable(VarDump) ?? "";
            var m = Regex.Match(string.Join(" ", args), "--result-file=\"?([^\"]+?)\"?(\\s|$)");
            var berkas = m.Success ? m.Groups[1].Value : null;
            if (mode == "sukses") { File.WriteAllText(berkas, "BARU"); return 0; }
            if (mode == "gagal")
            {
                if (berkas != null) File.WriteAllText(berkas, "-- SETENGAH");
                Console.Error.WriteLine("mysqldump: Got error: 2013: Lost connection");
                return 2;
            }
            // "tidur": baca stdin sampai habis (seperti mysql.exe), lalu diam.
            try { Console.OpenStandardInput().CopyTo(Stream.Null); } catch { }
            Thread.Sleep(60000);
            return 0;
        }

        /// <summary>
        /// Penjaga untuk 1.36.0: ekspor dan impor.
        ///
        /// Dulu mysqldump menulis LANGSUNG ke berkas tujuan. Ekspor yang gagal di
        /// tengah meninggalkan berkas setengah jadi bernama sama - dan bila
        /// pengguna menimpa cadangan lama, cadangan itu ikut hancur. Ekspor dan
        /// impor juga dibunuh pada menit ke-30; impor yang terbunuh menyisakan
        /// basis data setengah terisi. Kini: berkas sementara, dan pembatalan
        /// oleh pengguna sebagai pengganti batas waktu keras.
        /// </summary>
        static void UjiEksporImpor()
        {
            Bagian("Ekspor dan impor basis data");

            var akar = Path.Combine(Path.GetTempPath(), "phoron-uji-dump-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(akar);
            var sini = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var nama in new[] { "mysqldump", "mysql" })
            {
                File.Copy(Path.Combine(sini, "Phoron.Tests.exe"), Path.Combine(akar, nama + ".exe"));
                if (File.Exists(Path.Combine(sini, "Phoron.Tests.exe.config")))
                    File.Copy(Path.Combine(sini, "Phoron.Tests.exe.config"), Path.Combine(akar, nama + ".exe.config"));
            }
            File.Copy(Path.Combine(sini, "Phoron.Core.dll"), Path.Combine(akar, "Phoron.Core.dll"));
            var s = new MySqlKlien.Sambungan
            {
                Klien = Path.Combine(akar, "mysql.exe"), Dump = Path.Combine(akar, "mysqldump.exe"),
                KerjaDi = akar, Port = 3399,
            };
            var tujuan = Path.Combine(akar, "cadangan.sql");

            try
            {
                // Cadangan lama yang ditimpa pengguna; ekspor barunya gagal.
                File.WriteAllText(tujuan, "LAMA-UTUH");
                Environment.SetEnvironmentVariable(VarDump, "gagal");
                var galat = MySqlKlien.Ekspor(s, "billing", tujuan);
                Ok("Ekspor yang gagal melaporkan galat", galat != null, "tidak ada galat");
                Ok("Ekspor yang gagal TIDAK merusak berkas lama yang hendak ditimpa",
                   File.ReadAllText(tujuan) == "LAMA-UTUH", File.ReadAllText(tujuan));
                Ok("... dan tidak meninggalkan berkas sementara",
                   Directory.GetFiles(akar, "cadangan.sql*").Length == 1,
                   string.Join(", ", Directory.GetFiles(akar, "cadangan.sql*").Select(Path.GetFileName)));

                Environment.SetEnvironmentVariable(VarDump, "sukses");
                galat = MySqlKlien.Ekspor(s, "billing", tujuan);
                Ok("Ekspor yang berhasil menggantikan berkas lama",
                   galat == null && File.ReadAllText(tujuan) == "BARU", galat ?? File.ReadAllText(tujuan));

                // Impor yang dibatalkan pengguna berhenti SEKARANG, prosesnya ikut mati.
                Environment.SetEnvironmentVariable(VarDump, "tidur");
                var impor = typeof(MySqlKlien).GetMethods().FirstOrDefault(x => x.Name == "Impor" && x.GetParameters().Length == 4);
                Ok("Impor bisa dibatalkan (menerima CancellationToken)", impor != null, "Impor tanpa pembatalan");
                if (impor != null)
                {
                    using (var batal = new CancellationTokenSource(800))
                    {
                        var sw = Stopwatch.StartNew();
                        galat = (string)impor.Invoke(null, new object[] { s, "billing", tujuan, batal.Token });
                        Ok("Impor yang dibatalkan berhenti dalam hitungan detik",
                           sw.ElapsedMilliseconds < 8000 && galat != null, sw.ElapsedMilliseconds + " ms; " + galat);
                    }
                    Thread.Sleep(300);
                    Ok("... dan mysql.exe-nya ikut mati",
                       !Process.GetProcessesByName("mysql").Any(p => { try { return p.MainModule.FileName.StartsWith(akar, StringComparison.OrdinalIgnoreCase); } catch { return false; } }),
                       "mysql.exe tiruan masih hidup");
                }

                var sumber = File.ReadAllText(Path.Combine(AkarRepo(), "src", "Phoron.Core", "MySqlKlien.cs"));
                Ok("Ekspor dan impor tidak lagi dibunuh pada menit ke-30",
                   !sumber.Contains("30 * 60 * 1000"), "batas keras 30 menit masih ada");
            }
            finally
            {
                Environment.SetEnvironmentVariable(VarDump, null);
                foreach (var nama in new[] { "mysql", "mysqldump" })
                    foreach (var p in Process.GetProcessesByName(nama))
                        try { if (p.MainModule.FileName.StartsWith(akar, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
                Thread.Sleep(300);
                try { Directory.Delete(akar, true); } catch { }
            }
        }
    }
}
