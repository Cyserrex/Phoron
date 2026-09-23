using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Paket yang nama foldernya kembar harus benar-benar bisa dibedakan.
        ///
        /// Di mesin pengembang: php-8.3.12-..., php-7.4.22-..., php-5.6.40-...
        /// ada di bin Phoron DAN bin Laragon; "php", "apache", dan "mysql" ada di
        /// C:\xampp DAN D:\xampp dengan versi yang BERBEDA. Dulu hanya nama folder
        /// yang disimpan, jadi memilih PHP 5.6.38 x86 dari C:\xampp diam-diam
        /// menjalankan 5.6.40 dari D:\xampp - dan kedua MariaDB XAMPP berbagi
        /// satu folder data.
        /// </summary>
        static void UjiPaketKembar()
        {
            Bagian("Paket bernama kembar");

            // --- Kunci unik, pada paket buatan: selalu jalan, juga di CI.
            var d = new BinPackage { Kind = BinKind.Php, Id = "php", Path = @"D:\xampp\php", SourceRoot = @"D:\xampp", Version = "5.6.40" };
            var c = new BinPackage { Kind = BinKind.Php, Id = "php", Path = @"C:\xampp\php", SourceRoot = @"C:\xampp", Version = "5.6.38" };
            var u = new BinPackage { Kind = BinKind.Php, Id = "php-8.3.12-Win32-vs16-x64", Path = @"C:\Phoron\bin\php\php-8.3.12-Win32-vs16-x64", SourceRoot = @"C:\Phoron\bin", Version = "8.3.12" };
            var m = new BinPackage { Kind = BinKind.MySql, Id = "php", Path = @"C:\lain\php", SourceRoot = @"C:\lain", Version = "1.0" };
            BinScanner.TandaiKembar(new List<BinPackage> { d, c, u, m });

            Ok("Nama unik: kunci = nama folder", u.Kunci == u.Id && !u.Kembar, u.Kunci);
            Ok("Nama unik: yang disimpan tetap nama folder (profil tetap bisa dibawa)", u.NilaiSimpan == u.Id, u.NilaiSimpan);
            Ok("Kembar pertama mempertahankan nama folder polos sebagai kunci", d.Kunci == "php", d.Kunci);
            Ok("Kembarannya diberi tanda asal", c.Kunci == "php@C-xampp", c.Kunci);
            Ok("Kunci kembar berbeda satu sama lain", d.Kunci != c.Kunci, d.Kunci + " / " + c.Kunci);
            Ok("Paket kembar disimpan sebagai jalur lengkap", d.NilaiSimpan == d.Path && c.NilaiSimpan == c.Path,
               d.NilaiSimpan + " / " + c.NilaiSimpan);
            Ok("Nama sama beda jenis bukan kembar", !m.Kembar && m.Kunci == "php", m.Kunci);
            Ok("Tanda asal aman untuk nama folder", BinScanner.TandaAsal(@"C:\Phoron\bin") == "C-Phoron-bin",
               BinScanner.TandaAsal(@"C:\Phoron\bin"));

            // Dua kembaran dari folder bin yang SAMA (bertingkat) tetap unik.
            var a1 = new BinPackage { Kind = BinKind.Php, Id = "php", Path = @"E:\x\a\php", SourceRoot = @"E:\x" };
            var a2 = new BinPackage { Kind = BinKind.Php, Id = "php", Path = @"E:\x\b\php", SourceRoot = @"E:\x" };
            var a3 = new BinPackage { Kind = BinKind.Php, Id = "php", Path = @"E:\x\c\php", SourceRoot = @"E:\x" };
            BinScanner.TandaiKembar(new List<BinPackage> { a1, a2, a3 });
            Ok("Tiga kembaran dari satu folder bin tetap berkunci unik",
               new[] { a1.Kunci, a2.Kunci, a3.Kunci }.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
               a1.Kunci + " / " + a2.Kunci + " / " + a3.Kunci);

            // --- Folder php.ini tidak lagi dipakai bersama.
            Ok("php.ini kembar di folder berbeda",
               ConfigWriter.FolderPhpIni(d, false) != ConfigWriter.FolderPhpIni(c, false),
               ConfigWriter.FolderPhpIni(d, false));
            Ok("php.ini paket yang selama ini dipakai tidak berpindah",
               ConfigWriter.FolderPhpIni(d, false) == Path.Combine(Paths.Etc, "php", "php"), "");

            UjiFolderDataKembar();
            UjiFindKembar();
        }

        /// <summary>
        /// Folder data MySQL tidak pernah dipakai bersama - termasuk folder lama
        /// yang sudah terlanjur ada, dan saat urutan folder bin berubah.
        /// </summary>
        static void UjiFolderDataKembar()
        {
            Bagian("Folder data MySQL untuk paket kembar");

            Func<string, string, BinPackage> buat = (root, versi) => new BinPackage
            {
                Kind = BinKind.MySql, Id = "mysql", Path = Path.Combine(root, "mysql"),
                SourceRoot = root, Version = versi,
            };
            var dLama = Path.Combine(Paths.Data, "mysql");
            try { if (Directory.Exists(dLama)) Directory.Delete(dLama, true); } catch { }

            var d = buat(@"D:\xampp", "10.1.38");
            var c = buat(@"C:\xampp", "10.1.36");
            BinScanner.TandaiKembar(new List<BinPackage> { d, c });

            Ok("Dua MariaDB kembar mendapat folder data berbeda",
               ConfigWriter.MySqlDataDir(d) != ConfigWriter.MySqlDataDir(c),
               ConfigWriter.MySqlDataDir(d) + " / " + ConfigWriter.MySqlDataDir(c));

            // Folder lama sudah ada, tanpa penanda - milik paket yang selama ini
            // dipakai (yang pertama), persis seperti keadaan di mesin pengembang.
            Directory.CreateDirectory(dLama);
            Ok("Folder lama tanpa penanda tetap milik paket yang dulu memakainya",
               ConfigWriter.MySqlDataDir(d) == dLama, ConfigWriter.MySqlDataDir(d));
            Ok("... dan TIDAK dipakai kembarannya", ConfigWriter.MySqlDataDir(c) != dLama, ConfigWriter.MySqlDataDir(c));

            ConfigWriter.CatatPemilikData(dLama, d);
            Ok("Pemilik folder data tercatat", ConfigWriter.PemilikData(dLama) == d.Path, ConfigWriter.PemilikData(dLama));
            ConfigWriter.CatatPemilikData(dLama, c);
            Ok("Catatan pemilik tidak ditimpa paket lain", ConfigWriter.PemilikData(dLama) == d.Path, ConfigWriter.PemilikData(dLama));

            // Urutan folder bin berubah: kini C:\xampp yang "pertama" dan kuncinya
            // "mysql". Folder lama tercatat milik D:\xampp - C:\xampp TIDAK boleh
            // mengambilnya, dan D:\xampp tetap menemukannya.
            var d2 = buat(@"D:\xampp", "10.1.38");
            var c2 = buat(@"C:\xampp", "10.1.36");
            BinScanner.TandaiKembar(new List<BinPackage> { c2, d2 });
            Ok("Urutan bin berubah: pemilik tetap menemukan folder datanya",
               ConfigWriter.MySqlDataDir(d2) == dLama, ConfigWriter.MySqlDataDir(d2));
            Ok("Urutan bin berubah: kembarannya tidak mengambil folder yang bukan miliknya",
               ConfigWriter.MySqlDataDir(c2) != dLama, ConfigWriter.MySqlDataDir(c2));

            try { Directory.Delete(dLama, true); } catch { }
        }

        /// <summary>
        /// Find dan penyimpanan pada kembaran SUNGGUHAN di mesin ini - XAMPP C:
        /// dan D:, bin Phoron dan bin Laragon. Dilewati bila tidak ada.
        /// </summary>
        static void UjiFindKembar()
        {
            Bagian("Memilih salah satu dari paket kembar");

            var roots = new List<string>(Settings.DefaultBinRoots()) { @"C:\laragon\bin", @"C:\xampp", @"D:\xampp" };
            var e = new Engine();
            e.Settings.BinRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            e.Reload();

            var php = e.Of(BinKind.Php).Where(p => p.Kembar)
                       .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.ToList()).FirstOrDefault(g => g.Count >= 2);
            if (php == null) { Console.WriteLine("     dilewati: tidak ada PHP bernama kembar di mesin ini"); return; }
            var pertama = php[0];
            var kedua = php[1];
            Console.WriteLine("     kembar: " + pertama.Path + "  &  " + kedua.Path);

            Ok("Memilih salinan KEDUA benar-benar menunjuk salinan kedua",
               e.Find(BinKind.Php, kedua.NilaiSimpan) == kedua,
               "yang didapat: " + (e.Find(BinKind.Php, kedua.NilaiSimpan) ?? pertama).Path);
            Ok("Memilih salinan pertama menunjuk salinan pertama",
               e.Find(BinKind.Php, pertama.NilaiSimpan) == pertama, "");
            Ok("Nilai lama berupa nama folder tetap menunjuk paket yang dulu dipakai",
               e.Find(BinKind.Php, pertama.Id) == pertama, "");
            Ok("Jalur yang tidak ada di komputer ini jatuh ke nama foldernya",
               e.Find(BinKind.Php, @"Z:\komputer-lain\bin\php\" + pertama.Id) == pertama, "");
            Ok("Nama folder lama dan jalur paket yang sama dianggap pilihan yang sama",
               e.SamaPaket(BinKind.Php, pertama.Id, pertama.NilaiSimpan), "");
            Ok("Dua salinan kembar BUKAN pilihan yang sama",
               !e.SamaPaket(BinKind.Php, pertama.NilaiSimpan, kedua.NilaiSimpan), "");

            // Pulang-pergi lewat berkas profil.
            var p = new Profile { Name = "Uji kembar", FileName = "uji-kembar", PhpId = kedua.NilaiSimpan };
            p.PhpPerSitus[@"C:\www\situs"] = kedua.NilaiSimpan;
            ProfileStore.Save(p);
            var dibaca = ProfileStore.Load(Path.Combine(Paths.Profiles, "uji-kembar.ini"));
            Ok("Profil: salinan kedua tersimpan dan terbaca kembali tepat",
               e.Find(BinKind.Php, dibaca.PhpId) == kedua, dibaca.PhpId);
            Ok("PHP per situs: salinan kedua tersimpan dan terbaca kembali tepat",
               e.Find(BinKind.Php, dibaca.PhpPerSitus[@"C:\www\situs"]) == kedua, dibaca.PhpPerSitus[@"C:\www\situs"]);
            try { File.Delete(Path.Combine(Paths.Profiles, "uji-kembar.ini")); } catch { }
        }
    }
}
