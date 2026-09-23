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
        /// opcache: dimuat, disetel, dan - yang paling menentukan - disetel
        /// dengan cara yang tidak merusak alur kerja ngoding.
        ///
        /// Tanpa opcache, tiap permintaan halaman mengurai ulang seluruh
        /// kerangka kerja dari nol. Diukur di mesin pengembang pada satu
        /// permintaan CodeIgniter: 34,8 ms jadi 17,0 ms. Setengahnya.
        ///
        /// Tapi kecepatan itu tidak ada gunanya kalau berkas yang baru disimpan
        /// tidak langsung berlaku, dan bawaan PHP memang begitu: stempel waktu
        /// diperiksa paling sering sekali tiap dua detik. Orang menyimpan,
        /// menyegarkan, dan melihat kode lamanya. Dua uji di bawah menjaga
        /// justru bagian itu.
        /// </summary>
        static void UjiOpcache()
        {
            Bagian("opcache");

            var php = Nyata().FirstOrDefault(p => p.Kind == BinKind.Php && ConfigWriter.AdaOpcache(p));
            if (php == null)
            {
                Console.WriteLine("     dilewati: tidak ada PHP yang membawa php_opcache.dll");
                return;
            }

            // Profil SENGAJA tidak menyebut opcache di daftar ekstensinya. Di
            // situlah perkaranya: profil milik orang yang sudah ada tidak akan
            // pernah menyebutnya, dan merekalah yang paling merasakan bedanya.
            var profil = new Profile
            {
                Name = "Uji opcache",
                PhpId = php.Id,
                PhpExtensions = new List<string> { "mbstring" },
            };

            var isi = TulisPhpIni(profil, php, true);
            Ok("Daftar ekstensi profil memang tidak menyebut opcache",
               !profil.PhpExtensions.Any(x => x.Equals("opcache", StringComparison.OrdinalIgnoreCase)));
            Ok("opcache tetap dimuat walau tidak ada di daftar profil",
               Regex.IsMatch(isi, @"(?m)^\s*zend_extension\s*=.*opcache", RegexOptions.IgnoreCase),
               "tidak ada baris zend_extension untuk opcache");
            Ok("Dimuat sebagai zend_extension, bukan extension biasa",
               !Regex.IsMatch(isi, @"(?m)^\s*extension\s*=\s*(php_)?opcache", RegexOptions.IgnoreCase),
               "opcache ditulis sebagai extension - PHP tidak akan memuatnya");
            Ok("opcache.enable = 1", NilaiIni(isi, "opcache.enable") == "1",
               NilaiIni(isi, "opcache.enable"));

            // --- Inilah dua baris yang menentukan layak-tidaknya opcache dipakai
            //     saat ngoding. Kalau salah satunya berubah demi angka tolok ukur
            //     yang lebih cantik, uji ini harus merah.
            Ok("opcache.validate_timestamps = 1 (berkas tetap diperiksa)",
               NilaiIni(isi, "opcache.validate_timestamps") == "1",
               "nilainya " + NilaiIni(isi, "opcache.validate_timestamps")
               + " - berkas yang disunting tidak akan pernah dibaca ulang");
            Ok("opcache.revalidate_freq = 0 (berlaku pada permintaan berikutnya)",
               NilaiIni(isi, "opcache.revalidate_freq") == "0",
               "nilainya " + NilaiIni(isi, "opcache.revalidate_freq")
               + " - berkas yang baru disimpan bisa tertinggal sampai sekian detik, "
               + "dan orang akan mengira Phoron yang rusak");

            // CLI tidak pernah sempat memakai singgahannya: umurnya sependek
            // satu perintah.
            Ok("opcache.enable_cli = 0", NilaiIni(isi, "opcache.enable_cli") == "0",
               NilaiIni(isi, "opcache.enable_cli"));

            // --- Sakelarnya harus benar-benar mematikan, bukan sekadar hiasan.
            var mati = TulisPhpIni(profil, php, false);
            Ok("Sakelar mati: opcache tidak dimuat",
               !Regex.IsMatch(mati, @"(?m)^\s*zend_extension\s*=.*opcache", RegexOptions.IgnoreCase),
               "masih dimuat walau sakelarnya mati");
            Ok("Sakelar mati: setelan opcache tidak ditulis",
               NilaiIni(mati, "opcache.enable") == null,
               "opcache.enable masih ditulis");

            // --- Profil tetap berhak menimpanya. Kalau tidak, orang yang punya
            //     alasan sendiri tidak punya jalan keluar sama sekali.
            var ditimpa = new Profile
            {
                Name = "Uji opcache timpa",
                PhpId = php.Id,
                PhpExtensions = new List<string> { "mbstring" },
            };
            ditimpa.PhpIniOverrides["opcache.memory_consumption"] = "256";
            var hasilTimpa = TulisPhpIni(ditimpa, php, true);
            Ok("Setelan profil menang atas setelan bawaan opcache",
               NilaiIni(hasilTimpa, "opcache.memory_consumption") == "256",
               NilaiIni(hasilTimpa, "opcache.memory_consumption"));

            // --- Profil yang MEMANG menyebut opcache tidak boleh memuatnya dua kali.
            var sebut = new Profile
            {
                Name = "Uji opcache ganda",
                PhpId = php.Id,
                PhpExtensions = new List<string> { "mbstring", "opcache" },
            };
            var hasilSebut = TulisPhpIni(sebut, php, true);
            var jumlah = Regex.Matches(hasilSebut, @"(?m)^\s*zend_extension\s*=.*opcache",
                                       RegexOptions.IgnoreCase).Count;
            Ok("Tidak dimuat dua kali bila profil juga menyebutnya", jumlah == 1,
               jumlah + " baris zend_extension opcache");
        }

        /// <summary>
        /// xdebug harus dimuat lewat zend_extension. Dengan "extension =" ia
        /// menolak, mencetak "Xdebug MUST be loaded as a Zend extension" di awal
        /// SETIAP permintaan, dan tetap tidak termuat. Dulu hanya opcache yang
        /// dikenali sebagai ekstensi Zend.
        /// </summary>
        static void UjiXdebug()
        {
            Bagian("xdebug dimuat sebagai ekstensi Zend");

            var folder = Path.Combine(Paths.Tmp, "php-xdebug-uji");
            Directory.CreateDirectory(Path.Combine(folder, "ext"));
            foreach (var dll in new[] { "php_xdebug.dll", "php_mbstring.dll" })
                File.WriteAllText(Path.Combine(folder, "ext", dll), "");
            var php = new BinPackage
            {
                Kind = BinKind.Php,
                Id = "php-8.3.12-xdebug-uji",
                Path = folder,
                Version = "8.3.12",
                Arch = "x64",
                ThreadSafe = true,
            };
            var profil = new Profile
            {
                Name = "Uji xdebug",
                PhpId = php.Id,
                PhpExtensions = new List<string> { "mbstring", "xdebug" },
            };
            try
            {
                var isi = TulisPhpIni(profil, php, false);
                Ok("xdebug ditulis sebagai zend_extension",
                   Regex.IsMatch(isi, @"(?m)^\s*zend_extension\s*=\s*xdebug\s*$"),
                   "tidak ada zend_extension = xdebug");
                Ok("xdebug TIDAK ditulis sebagai extension biasa",
                   !Regex.IsMatch(isi, @"(?m)^\s*extension\s*=\s*xdebug"),
                   "PHP akan menolaknya di setiap permintaan");
                Ok("Ekstensi biasa tetap extension",
                   Regex.IsMatch(isi, @"(?m)^\s*extension\s*=\s*mbstring\s*$"), "");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        }

        static string TulisPhpIni(Profile profil, BinPackage php, bool opcache)
        {
            var r = new ConfigWriter.Result();
            var dir = ConfigWriter.WritePhpIni(profil, php, r, false, opcache);
            return File.ReadAllText(Path.Combine(dir, "php.ini"));
        }

        /// <summary>
        /// Nilai kunci yang BERLAKU: baris terakhir yang tidak dikomentari.
        /// php.ini hasil memuat berkas dasar lebih dulu, lalu blok setelan
        /// Phoron di bawahnya - dan PHP membaca dari atas ke bawah.
        /// </summary>
        static string NilaiIni(string isi, string kunci)
        {
            string hasil = null;
            var pola = new Regex(@"(?m)^\s*" + Regex.Escape(kunci) + @"\s*=\s*(.*?)\s*$");
            foreach (Match m in pola.Matches(isi)) hasil = m.Groups[1].Value;
            return hasil;
        }
    }
}
