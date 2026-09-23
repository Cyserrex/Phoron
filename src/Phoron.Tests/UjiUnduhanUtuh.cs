using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk 1.36.0: installer yang diunduh diperiksa keutuhannya
        /// sebelum dijalankan.
        ///
        /// Dulu unduhan yang terputus di tengah diterima begitu saja (ukurannya
        /// tidak dicocokkan dengan Content-Length), dan SHA256SUMS.txt yang
        /// diterbitkan CI di setiap rilis tidak pernah dibaca.
        /// </summary>
        static void UjiUnduhanUtuh()
        {
            Bagian("Keutuhan installer yang diunduh");

            var periksa = typeof(Updater).GetMethod("PeriksaBerkas", BindingFlags.Public | BindingFlags.Static);
            Ok("Updater punya pemeriksaan keutuhan", periksa != null, "Updater.PeriksaBerkas tidak ada");
            if (periksa == null) return;

            var berkas = Path.Combine(Path.GetTempPath(), "phoron-uji-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            var isi = Encoding.ASCII.GetBytes("installer phoron tiruan, isinya tidak penting");
            File.WriteAllBytes(berkas, isi);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(isi)).Replace("-", "").ToLowerInvariant();
            const string nama = "Phoron-9.9.9-Setup.exe";
            // Persis bentuk yang ditulis CI: BOM UTF-8, dua spasi, CRLF.
            var daftar = "﻿" + new string('0', 64) + "  Phoron.exe\r\n" + hash + "  " + nama + "\r\n";
            Func<long, string, string> jalankan = (total, d) =>
                (string)periksa.Invoke(null, new object[] { berkas, total, d, nama });

            try
            {
                Ok("Installer utuh dengan hash yang cocok diterima",
                   jalankan(isi.Length, daftar) == null, jalankan(isi.Length, daftar));
                Ok("Unduhan yang terputus (lebih pendek dari Content-Length) ditolak",
                   jalankan(isi.Length + 1000, daftar) != null, "diterima");
                Ok("Hash yang tidak cocok ditolak",
                   jalankan(isi.Length, daftar.Replace(hash, new string('f', 64))) != null, "diterima");
                Ok("Daftar hash yang tidak memuat installer ini ditolak",
                   jalankan(isi.Length, new string('0', 64) + "  Phoron.exe\r\n") != null, "diterima");
                Ok("Rilis lama tanpa SHA256SUMS.txt tetap bisa dipasang (ukuran saja)",
                   jalankan(isi.Length, null) == null, jalankan(isi.Length, null));
            }
            finally { try { File.Delete(berkas); } catch { } }
        }
    }
}
