using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Phoron.Core
{
    /// <summary>
    /// Menanyakan versi dan arsitektur langsung ke berkas binernya.
    ///
    /// Phoron biasanya membaca keduanya dari NAMA folder - "php-8.3.12-Win32-vs16-x64"
    /// sudah menyebut semuanya, dan membacanya dari nama itu gratis. Tapi tata
    /// letak seperti XAMPP menamai foldernya cuma "php", "apache", "mysql".
    /// Di situ nama tidak menyebut apa-apa, dan hasilnya paket tampil tanpa versi:
    /// label kosong, urutan daftar kacau, dan yang paling berbahaya - peringatan
    /// beda arsitektur tidak bisa bekerja, padahal Apache 32-bit dengan PHP 64-bit
    /// mati seketika tanpa pesan apa pun.
    ///
    /// Karena itu penyelidikan ini hanya dijalankan kalau namanya memang bisu,
    /// jadi pemindaian folder bergaya Laragon tetap secepat sebelumnya.
    /// </summary>
    public static class BinProbe
    {
        /// <summary>Hasil penyelidikan disimpan supaya Reload berulang tidak menjalankan exe berkali-kali.</summary>
        static readonly Dictionary<string, string> _singgahVersi =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, string> _singgahArch =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string Kunci(string exe)
        {
            try { return exe + "|" + File.GetLastWriteTimeUtc(exe).Ticks; }
            catch { return exe; }
        }

        /// <summary>
        /// Arsitektur dibaca dari header PE - tidak menjalankan apa pun, jadi
        /// aman dan cepat sekalipun exe-nya rusak.
        /// </summary>
        public static string Arsitektur(string exe)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "";
            var k = Kunci(exe);
            string ada;
            if (_singgahArch.TryGetValue(k, out ada)) return ada;

            var hasil = "";
            try
            {
                using (var f = File.OpenRead(exe))
                using (var r = new BinaryReader(f))
                {
                    f.Seek(0x3C, SeekOrigin.Begin);
                    var awalPe = r.ReadInt32();
                    if (awalPe > 0 && awalPe < f.Length - 6)
                    {
                        f.Seek(awalPe, SeekOrigin.Begin);
                        if (r.ReadUInt32() == 0x00004550)   // "PE\0\0"
                        {
                            var mesin = r.ReadUInt16();
                            if (mesin == 0x8664) hasil = "x64";
                            else if (mesin == 0x014c) hasil = "x86";
                            else if (mesin == 0xAA64) hasil = "arm64";
                        }
                    }
                }
            }
            catch { hasil = ""; }

            _singgahArch[k] = hasil;
            return hasil;
        }

        /// <summary>
        /// Nomor versi menurut program itu sendiri. Mengembalikan "" kalau tidak
        /// bisa dipastikan - menebak lebih buruk daripada mengaku tidak tahu.
        /// </summary>
        public static string Versi(BinKind jenis, string exe)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "";
            var k = jenis + "|" + Kunci(exe);
            string ada;
            if (_singgahVersi.TryGetValue(k, out ada)) return ada;

            var hasil = "";
            try
            {
                // -n pada PHP: jangan muat php.ini. Tanpa itu, penyelidikan ikut
                // menyeret peringatan ekstensi yang gagal dimuat, dan lambat.
                string arg;
                Regex pola;
                switch (jenis)
                {
                    case BinKind.Php:
                        arg = "-n -v"; pola = new Regex(@"PHP\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); break;
                    case BinKind.Apache:
                        arg = "-v"; pola = new Regex(@"Apache/(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); break;
                    case BinKind.MySql:
                        arg = "--version"; pola = new Regex(@"Ver\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); break;
                    case BinKind.Nginx:
                        arg = "-v"; pola = new Regex(@"nginx/(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); break;
                    case BinKind.Node:
                        arg = "-v"; pola = new Regex(@"v?(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase); break;
                    default:
                        _singgahVersi[k] = ""; return "";
                }

                // Batas waktu pendek: ini berjalan saat memindai, dan satu exe
                // rusak tidak boleh menahan seluruh daftar versi.
                var res = Shell.Run(exe, arg, Path.GetDirectoryName(exe), 8000);
                var m = pola.Match(res.All ?? "");
                if (m.Success) hasil = m.Groups[1].Value;
            }
            catch { hasil = ""; }

            _singgahVersi[k] = hasil;
            return hasil;
        }
    }
}
