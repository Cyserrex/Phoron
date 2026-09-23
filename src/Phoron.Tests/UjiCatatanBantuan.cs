using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk 1.37.0: catatan kuning bisa ditutup (X), dan tutupan itu
        /// diingat per BARIS - catatan yang sudah beres lalu muncul lagi tampil lagi.
        /// </summary>
        static void UjiCatatanDitutup()
        {
            Bagian("Catatan kuning yang ditutup");

            const string restart = "Konfigurasi web server sudah berubah.";
            const string hosts = "13 nama situs .test belum terdaftar.";
            const string hal = "uji-catatan";

            Ok("Mula-mula semua catatan tampil",
               CatatanDitutup.Saring(hal, new[] { restart, hosts }).Count == 2, "");
            CatatanDitutup.Tutup(hal, new[] { restart, hosts });
            Ok("Sesudah X: catatan yang sama tidak tampil lagi",
               CatatanDitutup.Saring(hal, new[] { restart, hosts }).Count == 0, "");

            // Restart dilakukan: catatan restart hilang dengan sendirinya.
            CatatanDitutup.Saring(hal, new[] { hosts });
            // Kelak konfigurasi berubah lagi: catatan restart harus tampil lagi,
            // catatan hosts yang belum beres tetap tersembunyi.
            var lagi = CatatanDitutup.Saring(hal, new[] { restart, hosts });
            Ok("Catatan yang sudah beres lalu muncul lagi tampil kembali",
               lagi.Contains(restart), string.Join(" | ", lagi));
            Ok("Catatan yang masih sama dan pernah ditutup tetap tersembunyi",
               !lagi.Contains(hosts), string.Join(" | ", lagi));

            // Isinya berubah (13 -> 14 nama): itu catatan baru.
            var berubah = CatatanDitutup.Saring(hal, new[] { "14 nama situs .test belum terdaftar." });
            Ok("Catatan yang isinya berubah dianggap catatan baru", berubah.Count == 1, "");

            Ok("Tutupan satu halaman tidak menyembunyikan catatan halaman lain",
               CatatanDitutup.Saring("halaman-lain", new[] { hosts }).Count == 1, "");
        }

        /// <summary>
        /// Isi panel Bantuan: semua tab ada, keempat bahasa terisi, dan setiap
        /// tombol yang disebut benar-benar ada di halaman itu - bantuan yang
        /// menyebut tombol yang sudah tidak ada lebih buruk daripada tanpa bantuan.
        /// </summary>
        static void UjiBantuan()
        {
            Bagian("Isi Bantuan tiap tab");

            var tab = new[] { "beranda", "profil", "versi", "situs", "basisdata", "node", "ekstensi", "log", "setelan", "tentang" };
            var berkas = new Dictionary<string, string>
            {
                { "beranda", "DashboardPage" }, { "profil", "ProfilesPage" }, { "versi", "VersionsPage" },
                { "situs", "SitesPage" }, { "basisdata", "DatabasePage" }, { "node", "NodePage" },
                { "ekstensi", "ExtensionsPage" }, { "log", "LogPage" }, { "setelan", "SettingsPage" }, { "tentang", "AboutPage" },
            };
            Ok("Setiap tab punya bantuan",
               tab.All(t => Bantuan.Semua.Any(h => h.Tag == t)),
               string.Join(", ", tab.Where(t => !Bantuan.Semua.Any(h => h.Tag == t))));

            var kosong = new List<string>();
            foreach (var h in Bantuan.Semua)
            {
                if (h.Ringkas.Length != 4 || h.Ringkas.Any(string.IsNullOrWhiteSpace)) kosong.Add(h.Tag + ": ringkasan");
                foreach (var b in h.Butir)
                    if (b.Arti.Length != 4 || b.Arti.Any(string.IsNullOrWhiteSpace)) kosong.Add(h.Tag + ": " + (b.Tombol ?? "kiat"));
            }
            Ok("Keempat bahasa terisi di setiap butir", kosong.Count == 0, string.Join(" | ", kosong));

            var dirApp = Path.Combine(AkarRepo(), "src", "Phoron.App");
            var hilang = new List<string>();
            foreach (var h in Bantuan.Semua)
            {
                string nama;
                if (!berkas.TryGetValue(h.Tag, out nama)) continue;
                var xaml = WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(dirApp, "Pages", nama + ".xaml")));
                foreach (var b in h.Butir.Where(x => x.Tombol != null))
                    if (!xaml.Contains("'" + b.Tombol + "'") && !xaml.Contains("\"" + b.Tombol + "\""))
                        hilang.Add(h.Tag + ": " + b.Tombol);
            }
            Ok("Setiap tombol yang dijelaskan benar-benar ada di halamannya", hilang.Count == 0, string.Join(" | ", hilang));

            var bocor = new List<string>();
            foreach (var h in Bantuan.Semua)
                foreach (var teks in new[] { h.Ringkas[3] }.Concat(h.Butir.Select(b => b.Arti[3])))
                    foreach (var w in KataTugasIndonesia)
                        if (Regex.IsMatch(teks, "(?<![A-Za-z])" + w + "(?![A-Za-z])", RegexOptions.IgnoreCase))
                            bocor.Add("\"" + w + "\" di " + h.Tag + ": " + teks.Substring(0, Math.Min(50, teks.Length)));
            Ok("Bantuan berbahasa Banjar tidak bercampur kata tugas Indonesia", bocor.Count == 0, string.Join(" | ", bocor));
        }
    }
}
