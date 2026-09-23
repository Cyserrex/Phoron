using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Catatan kuning (Beranda, Situs) yang sudah ditutup pengguna dengan X.
    ///
    /// Yang diingat adalah BARIS catatannya, bukan panelnya. Satu panel memuat
    /// beberapa catatan yang lahir dan hilang sendiri-sendiri; mengingat seluruh
    /// panel sebagai satu kesatuan membuat catatan baru ikut tersembunyi hanya
    /// karena kebetulan bersanding dengan catatan lama yang pernah ditutup.
    ///
    /// Catatan yang ditutup tetap tersembunyi selama ia masih ada. Begitu
    /// keadaannya beres - catatannya tidak lagi dihasilkan - ingatannya dibuang,
    /// jadi bila masalah yang sama muncul lagi kelak, catatannya tampil lagi.
    /// Menutup sebuah peringatan berarti "sudah saya baca", bukan "jangan pernah
    /// beri tahu saya lagi".
    /// </summary>
    public static class CatatanDitutup
    {
        static readonly object Kunci = new object();

        static string Berkas { get { return Path.Combine(Paths.Etc, "catatan-ditutup.ini"); } }

        static string Sidik(string catatan)
        {
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes((catatan ?? "").Trim()));
                return BitConverter.ToString(b, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }

        static Ini Muat()
        {
            try { return File.Exists(Berkas) ? Ini.Load(Berkas) : new Ini(); }
            catch { return new Ini(); }
        }

        static void Simpan(Ini ini)
        {
            try
            {
                Directory.CreateDirectory(Paths.Etc);
                ini.Save(Berkas, "Catatan yang ditutup pengguna (tombol X). Boleh dihapus kapan saja.");
            }
            catch { /* mengingat tutupan hanya kenyamanan */ }
        }

        /// <summary>
        /// Catatan yang masih perlu ditampilkan di halaman ini. Sekaligus
        /// melupakan tutupan untuk catatan yang sudah tidak ada lagi.
        /// </summary>
        public static List<string> Saring(string halaman, IEnumerable<string> catatan)
        {
            var semua = (catatan ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            lock (Kunci)
            {
                var ini = Muat();
                var ada = new HashSet<string>(semua.Select(Sidik));
                bool berubah = false;
                foreach (var kv in ini.Items(halaman).ToList())
                    if (!ada.Contains(kv.Key)) { ini.Remove(halaman, kv.Key); berubah = true; }
                if (berubah) Simpan(ini);
                var tutup = new HashSet<string>(ini.Items(halaman).Select(kv => kv.Key));
                return semua.Where(c => !tutup.Contains(Sidik(c))).ToList();
            }
        }

        /// <summary>Tutup catatan-catatan ini (yang sedang tampil saat X ditekan).</summary>
        public static void Tutup(string halaman, IEnumerable<string> catatan)
        {
            lock (Kunci)
            {
                var ini = Muat();
                foreach (var c in catatan ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(c)) ini.Set(halaman, Sidik(c), "1");
                Simpan(ini);
            }
        }
    }
}
