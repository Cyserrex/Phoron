using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Laporan galat yang tidak tertangkap.
    ///
    /// Sebelum berkas ini ada, galat di luar dugaan membuat Phoron mati dengan
    /// kotak galat mentah Windows dan tidak meninggalkan jejak apa pun - tidak
    /// di layar, tidak di berkas. Orang yang mengalaminya tidak punya satu pun
    /// keterangan untuk diceritakan, dan yang memperbaikinya tidak punya satu
    /// pun petunjuk untuk ditelusuri.
    ///
    /// Bagian paling berguna dari laporan ini bukan jejak tumpukannya, melainkan
    /// riwayat Aktivitas di bawahnya: ia menceritakan apa yang sedang dikerjakan
    /// Phoron ketika semuanya berantakan.
    /// </summary>
    public static class Crash
    {
        /// <summary>Sebanyak ini laporan disimpan; yang tertua dibuang.</summary>
        public const int LaporanMaks = 20;

        /// <summary>Sebanyak ini baris terakhir riwayat Aktivitas ikut dilampirkan.</summary>
        public const int BarisRiwayat = 40;

        /// <summary>
        /// Tulis satu laporan galat. Mengembalikan jalur berkasnya, atau null
        /// bila tidak ada tempat yang bisa ditulis.
        ///
        /// Tidak pernah melempar, apa pun yang terjadi. Ia dipanggil dari
        /// penangan galat terakhir, dan penangan yang ikut meledak adalah cara
        /// paling umum membuat lingkaran tak berujung.
        /// </summary>
        public static string Tulis(Exception ex, string konteks, IEnumerable<BarisLog> riwayat = null)
        {
            try
            {
                var isi = Susun(ex, konteks, riwayat);
                var nama = "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";

                foreach (var folder in FolderCalon())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(folder)) continue;
                        Directory.CreateDirectory(folder);
                        var jalur = Path.Combine(folder, nama);
                        var n = 1;
                        while (File.Exists(jalur))
                            jalur = Path.Combine(folder, Path.GetFileNameWithoutExtension(nama)
                                                         + "-" + (++n) + ".log");
                        AtomicFile.WriteAllText(jalur, isi);
                        Pangkas(folder);
                        return jalur;
                    }
                    catch { /* coba folder berikutnya */ }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Tempat menulis, dari yang paling pantas ke yang paling pasti bisa.
        /// Folder logs bisa saja justru yang bermasalah - misalnya akar Phoron
        /// ada di drive jaringan yang putus - dan laporan galat adalah hal
        /// terakhir yang boleh ikut gagal.
        /// </summary>
        static IEnumerable<string> FolderCalon()
        {
            string logs = null;
            try { logs = Paths.Logs; } catch { }
            if (logs != null) yield return logs;

            string sebelahExe = null;
            try { sebelahExe = AppDomain.CurrentDomain.BaseDirectory; } catch { }
            if (sebelahExe != null) yield return sebelahExe;

            string lokal = null;
            try
            {
                lokal = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Phoron");
            }
            catch { }
            if (lokal != null) yield return lokal;
        }

        /// <summary>Isi laporan. Dipisah supaya bisa diuji tanpa menulis berkas.</summary>
        public static string Susun(Exception ex, string konteks, IEnumerable<BarisLog> riwayat = null)
        {
            var sb = new StringBuilder();
            Aman(sb, "Waktu", () => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Aman(sb, "Phoron", () => AppInfo.Version);
            Aman(sb, "Windows", () => Environment.OSVersion.VersionString);
            Aman(sb, "Proses", () => (Environment.Is64BitProcess ? "64-bit" : "32-bit")
                                    + " pada " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"));
            Aman(sb, ".NET", () => Environment.Version.ToString());
            Aman(sb, "Administrator", () => HostsFile.IsAdmin() ? "ya" : "tidak");
            Aman(sb, "Akar", () => Paths.Root);
            sb.AppendLine("Konteks       : " + (string.IsNullOrWhiteSpace(konteks) ? "(tidak disebut)" : konteks));
            sb.AppendLine();

            sb.AppendLine("--- Galat ---");
            if (ex == null) sb.AppendLine("(tidak ada objek galat - penangan dipanggil tanpa exception)");
            else
            {
                var agg = ex as AggregateException;
                if (agg != null) ex = agg.Flatten();
                var lapis = 0;
                for (var e = ex; e != null; e = e.InnerException)
                {
                    sb.AppendLine(new string(' ', lapis * 2) + e.GetType().FullName + ": " + e.Message);
                    lapis++;
                }
                sb.AppendLine();
                try { sb.AppendLine(ex.ToString()); } catch { }
            }

            if (riwayat != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- Aktivitas terakhir ---");
                try
                {
                    // Inilah bagian yang paling menolong: apa yang sedang
                    // dikerjakan Phoron tepat sebelum semuanya berantakan.
                    var baris = riwayat.ToList();
                    foreach (var b in baris.Skip(Math.Max(0, baris.Count - BarisRiwayat)))
                        sb.AppendLine(b.Waktu.ToString("HH:mm:ss") + "  " + b.Teks);
                }
                catch { sb.AppendLine("(riwayat tidak bisa dibaca)"); }
            }
            return sb.ToString();
        }

        static void Aman(StringBuilder sb, string label, Func<string> ambil)
        {
            string nilai;
            try { nilai = ambil() ?? ""; }
            catch (Exception e) { nilai = "(gagal dibaca: " + e.GetType().Name + ")"; }
            sb.AppendLine(label.PadRight(14) + ": " + nilai);
        }

        static void Pangkas(string folder)
        {
            try
            {
                var berkas = Directory.GetFiles(folder, "crash-*.log")
                                      .Select(f => new FileInfo(f))
                                      .OrderByDescending(f => f.LastWriteTime)
                                      .ToList();
                for (int i = LaporanMaks; i < berkas.Count; i++)
                    try { berkas[i].Delete(); } catch { }
            }
            catch { }
        }
    }
}
