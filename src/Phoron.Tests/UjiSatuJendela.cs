using System;
using System.IO;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Satu jendela utama yang ukurannya pas, dengan tombol berteks.
        ///
        /// Panel ringkas terpisah (1.41.0) dibuang: untuk mengatur apa pun orang
        /// tetap harus membuka jendela lengkap, jadi panel itu menambah langkah.
        /// Tombol daya dan keluar berupa ikon juga dikembalikan ke teks.
        /// </summary>
        static void UjiSatuJendela()
        {
            Bagian("Satu jendela utama");

            var pg = BacaSumber("Phoron.App", "Pengendali.cs");
            if (pg != null)
            {
                Ok("Tidak ada panel ringkas terpisah", !pg.Contains("PanelRingkas"),
                   "tampilan kedua menambah langkah, bukan mengurangi");
                Ok("Tampilkan membuka jendela utama",
                   Regex.IsMatch(Potong(pg, "public void Tampilkan(string tab)", "void UkuranAwal("), @"new MainWindow\(_engine, _tab\)"));
                Ok("Ukuran jendela dibatasi layar",
                   pg.Contains("area.Width * 0.70") && pg.Contains("area.Height * 0.82"));
            }
            Ok("Berkas panel ringkas sudah tidak ada",
               !File.Exists(Path.Combine(AkarRepo(), "src", "Phoron.App", "PanelRingkas.xaml")));

            var xaml = BacaSumber("Phoron.App", "MainWindow.xaml");
            if (xaml != null)
                Ok("Tombol daya dan keluar berteks, bukan ikon saja",
                   xaml.Contains("x:Name=\"TxtPower\"") && xaml.Contains("x:Name=\"TxtKeluar\"")
                   && !xaml.Contains("x:Name=\"IkonPower\""));

            // Ukuran tersimpan bertahan lintas sesi.
            var dir = Path.Combine(Path.GetTempPath(), "phoron-uji-ukuran-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            var dulu = Paths.Root;
            try
            {
                Paths.Root = dir;
                var s = Settings.Load();
                Ok("Ukuran jendela belum tersimpan di awal", s.JendelaLebar == 0 && s.JendelaTinggi == 0);
                s.JendelaLebar = 900; s.JendelaTinggi = 600;
                s.Save();
                var u = Settings.Load();
                Ok("Ukuran jendela tersimpan lintas sesi", u.JendelaLebar == 900 && u.JendelaTinggi == 600,
                   u.JendelaLebar + "x" + u.JendelaTinggi);
            }
            finally
            {
                Paths.Root = dulu;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
