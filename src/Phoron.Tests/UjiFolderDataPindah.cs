using System;
using System.Collections.Generic;
using System.IO;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk 1.36.0: paket MySQL yang PINDAH TEMPAT tetap memakai
        /// folder datanya sendiri.
        ///
        /// Penanda .phoron-pemilik (1.35.1) menyimpan jalur lengkap paketnya. Dulu
        /// jalur yang tidak sama persis - folder bin dipindah, huruf drive
        /// berubah, bin_roots ditulis dengan "/" - membuat folder data lama
        /// dianggap milik paket lain, dan mysqld dinyalakan di atas folder BARU
        /// yang kosong: seluruh basis data tampak hilang.
        /// </summary>
        static void UjiFolderDataPindah()
        {
            Bagian("Folder data MySQL sesudah paketnya pindah tempat");

            const string id = "mysql-uji-pindah-5.7.38-winx64";
            var polos = Path.Combine(Paths.Data, id);
            var luar = Path.Combine(Path.GetTempPath(), "phoron-uji-pindah-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Func<string, BinPackage> buat = root =>
            {
                var p = new BinPackage { Kind = BinKind.MySql, Id = id, Path = Path.Combine(root, id), SourceRoot = root, Version = "5.7.38" };
                BinScanner.TandaiKembar(new List<BinPackage> { p });
                return p;
            };
            Action<string> tanda = jalur =>
            {
                Directory.CreateDirectory(Path.Combine(polos, "mysql"));
                File.WriteAllText(Path.Combine(polos, ".phoron-pemilik"), jalur);
            };

            try
            {
                var sekarang = buat(@"E:\pindahan\bin");

                // Pemilik tercatat di jalur yang sudah tidak ada: itu paket yang
                // sama, hanya pindah tempat.
                tanda(@"X:\tidak-ada-lagi\bin\" + id);
                Ok("Paket yang pindah tempat tetap memakai folder datanya",
                   ConfigWriter.MySqlDataDir(sekarang) == polos, ConfigWriter.MySqlDataDir(sekarang));

                // Jalur yang sama, ditulis berbeda.
                tanda(@"e:/PINDAHAN/bin/" + id + "/");
                Ok("Jalur yang sama dengan penulisan lain (/, huruf besar) dikenali",
                   ConfigWriter.MySqlDataDir(sekarang) == polos, ConfigWriter.MySqlDataDir(sekarang));

                // Pemilik tercatat MASIH ADA di disk (instalasi lain yang tidak
                // dipindai): folder itu memang bukan milik paket ini.
                var lain = Path.Combine(luar, id);
                Directory.CreateDirectory(Path.Combine(lain, "bin"));
                tanda(lain);
                Ok("Folder milik MySQL lain yang masih ada tidak diambil alih",
                   ConfigWriter.MySqlDataDir(sekarang) != polos, ConfigWriter.MySqlDataDir(sekarang));

                // Pemilik tercatat masih ada, dan itu direktori yang sama dengan
                // paket ini dalam bentuk nama pendek 8.3 - tetap miliknya.
                var nyata = Path.Combine(luar, "bin-panjang-sekali", id);
                Directory.CreateDirectory(Path.Combine(nyata, "bin"));
                var pendek = ConfigWriter.NamaPendek(nyata);
                if (pendek != null && !string.Equals(pendek, nyata, StringComparison.OrdinalIgnoreCase))
                {
                    tanda(pendek);
                    var p = buat(Path.GetDirectoryName(nyata));
                    Ok("Nama pendek 8.3 dari folder paket yang sama dikenali",
                       ConfigWriter.MySqlDataDir(p) == polos, pendek + " -> " + ConfigWriter.MySqlDataDir(p));
                }
                else Console.WriteLine("     dilewati: nama pendek 8.3 tidak aktif di volume ini");

                // Penanda diperbarui sesudah mysqld jalan di jalur barunya.
                tanda(@"X:\tidak-ada-lagi\bin\" + id);
                ConfigWriter.CatatPemilikData(polos, sekarang);
                Ok("Penanda diperbarui ke jalur baru paket itu",
                   ConfigWriter.PemilikData(polos) == sekarang.Path, ConfigWriter.PemilikData(polos));
            }
            finally
            {
                try { Directory.Delete(polos, true); } catch { }
                try { Directory.Delete(luar, true); } catch { }
            }
        }
    }
}
