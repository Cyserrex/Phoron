using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Terjemahan teks yang dipasang dari kode, bukan dari XAML.
        ///
        /// Uji cakupan yang lama hanya memindai {loc:T} di XAML. Teks yang
        /// dipasang dari code-behind - label status, pesan, tooltip, kotak
        /// dialog - lolos semuanya, dan pengguna berbahasa Inggris melihat
        /// "belum dipilih di profil" di Beranda tanpa satu pun uji yang gagal.
        /// </summary>
        static void UjiTerjemahanKode()
        {
            Bagian("Terjemahan teks dari kode");

            var dirApp = Path.Combine(AkarRepo(), "src", "Phoron.App");
            var berkas = Directory.GetFiles(dirApp, "*.cs", SearchOption.AllDirectories)
                .Where(f => !Regex.IsMatch(f, @"[\\/]obj[\\/]")).ToList();

            // 1. Setiap Lang.T("...") yang berupa kalimat punya padanan di ketiga bahasa.
            var kunci = new List<string>();
            // Kunci boleh ditulis bersambung ("a " + "b ") di beberapa baris;
            // yang dicari di kamus adalah gabungannya, bukan potongan pertamanya.
            const string satu = "\"(?:[^\"\\\\]|\\\\.)*\"";
            var lit = new Regex("Lang\\.T\\(\\s*(" + satu + "(?:\\s*\\+\\s*" + satu + ")*)");
            var potongan = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (var f in berkas)
                foreach (Match m in lit.Matches(File.ReadAllText(f)))
                    kunci.Add(string.Concat(potongan.Matches(m.Groups[1].Value).Cast<Match>()
                                                   .Select(p => Regex.Unescape(p.Groups[1].Value))));
            var kalimat = kunci.Where(t => t.Length >= 25).Distinct().ToList();
            Ok("Kalimat Lang.T terkumpul dari kode", kalimat.Count >= 50, kalimat.Count.ToString());
            foreach (var kode in new[] { Lang.Inggris, Lang.Jawa, Lang.Banjar })
            {
                var bocor = kalimat.Where(t => !Lang.Punya(kode, t)).ToList();
                Ok("Kalimat dari kode punya padanan " + Lang.NamaBahasa(kode), bocor.Count == 0,
                   bocor.Count + ": " + string.Join(" | ", bocor.Take(8).Select(
                       t => t.Substring(0, Math.Min(60, t.Length))).ToArray()));
            }

            // 2. Teks mentah tidak boleh dipasang langsung ke tempat yang dilihat
            //    pengguna. Yang ditangkap: literal yang huruf pertamanya huruf
            //    (bukan "", " - ", ikon) di .Text/.Content/ToolTip/Header/Title,
            //    dan di kotak pesan AppState.
            var tujuan = new Regex(
                "(\\.(Text|Content|ToolTip|Header|Title|Description)\\s*=\\s*|AppState\\.(Info|Warn|Ask)\\(\\s*|MessageBox\\.Show\\(\\s*)\"[A-Za-z(]");
            var mentah = new List<string>();
            foreach (var f in berkas)
            {
                var baris = File.ReadAllLines(f);
                for (int i = 0; i < baris.Length; i++)
                {
                    var b = baris[i].Trim();
                    if (b.StartsWith("//")) continue;
                    if (tujuan.IsMatch(b))
                        mentah.Add(Path.GetFileName(f) + ":" + (i + 1));
                }
            }
            Ok("Tidak ada teks mentah yang dipasang langsung ke layar", mentah.Count == 0,
               mentah.Count + ": " + string.Join(", ", mentah.Take(15).ToArray()));

            // 3. Kamus tambahan per layar: lengkap, tidak ganda, dan placeholder
            //    {0} {1} yang sama di keempat bahasa - kalau satu hilang,
            //    string.Format melempar dan layarnya mati.
            var semua = Lang.BarisLayar().ToList();
            var cacat = semua.Where(r => r == null || r.Length != 4 || r.Any(string.IsNullOrEmpty))
                             .Select(r => r == null ? "(null)" : r[0]).ToList();
            Ok("Baris kamus layar lengkap empat bahasa", cacat.Count == 0, string.Join(" | ", cacat.ToArray()));
            var ganda = semua.Where(r => r != null && r.Length == 4).GroupBy(r => r[0])
                             .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Ok("Tidak ada kunci ganda di kamus layar", ganda.Count == 0, string.Join(" | ", ganda.ToArray()));
            var ph = new Regex(@"\{\d+\}");
            var pincang = semua.Where(r => r != null && r.Length == 4)
                .Where(r => r.Skip(1).Any(t => !ph.Matches(t ?? "").Cast<Match>().Select(m => m.Value)
                                                  .OrderBy(x => x)
                                                  .SequenceEqual(ph.Matches(r[0]).Cast<Match>()
                                                                   .Select(m => m.Value).OrderBy(x => x))))
                .Select(r => r[0]).ToList();
            Ok("Placeholder {n} sama di keempat bahasa", pincang.Count == 0,
               string.Join(" | ", pincang.Take(8).ToArray()));

            var semula = Lang.Kode;
            Lang.Pakai(Lang.Inggris);
            Ok("Versi di halaman Tentang memakai kata tunggal", Lang.T("Versi {0}", "1.0") == "Version 1.0");
            Lang.Pakai(semula);
        }
    }
}
