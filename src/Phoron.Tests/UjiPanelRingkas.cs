using System;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Panel kontrol ringkas sebagai tampilan utama, jendela lengkap dari
        /// lacinya, dan ukuran jendela lengkap yang menyesuaikan layar.
        /// </summary>
        static void UjiPanelRingkas()
        {
            Bagian("Panel ringkas dan ukuran jendela");

            var pg = BacaSumber("Phoron.App", "Pengendali.cs");
            if (pg != null)
            {
                var tampil = Potong(pg, "public void Tampilkan()", "public void TampilkanRingkas()");
                Ok("Tampilkan membuka panel ringkas bila jendela lengkap tidak terbuka",
                   tampil.Contains("TampilkanRingkas();"));
                var tutup = Potong(pg, "void JendelaTertutup(Window w)", "void JadwalkanLepasMemori(");
                Ok("Berpindah antara panel dan jendela lengkap bukan ke baki",
                   tutup.Contains("if (AdaJendela) return;"),
                   "membuka jendela lengkap dari laci memangkas memori atau malah keluar");
                Ok("Panel dibuka sebelum jendela lengkap ditutup",
                   Regex.IsMatch(Potong(pg, "public void TampilkanRingkas()", "public void TampilkanLengkap("),
                                 @"_panel\.Majukan\(\);[\s\S]*?_jendela\.Close\(\);"),
                   "ada saat tanpa jendela, dan JendelaTertutup menganggapnya ke baki");
                Ok("Ukuran jendela lengkap dibatasi layar",
                   pg.Contains("area.Width * 0.70") && pg.Contains("area.Height * 0.82"));
                Ok("Keluar menutup kedua jendela", Regex.IsMatch(Potong(pg, "public void Keluar()", "\n        }\n"),
                   @"_jendela\.Close\(\)[\s\S]*_panel\.Close\(\)"));
            }

            var panel = BacaSumber("Phoron.App", "PanelRingkas.xaml.cs");
            if (panel != null)
            {
                var ktor = Potong(panel, "public PanelRingkas(", "\n        }\n");
                Ok("Panel melanggan peristiwa di Loaded, bukan di konstruktor",
                   Regex.IsMatch(ktor, @"Loaded \+= \(s, ev\) =>\s*\{\s*Lepas\(\);\s*_e\.Log \+= OnLog;")
                   && ktor.Contains("Unloaded += (s, ev) => Lepas();"));
                Ok("Panel menggambar ulang secara digabung",
                   panel.Contains("Interlocked.Exchange(ref _tertunda, 1) == 1) return;"));
                Ok("Panel menjalankan Node lewat jalur Engine", panel.Contains("_e.JalankanNode(app)"));
            }

            var xaml = BacaSumber("Phoron.App", "PanelRingkas.xaml");
            if (xaml != null)
                Ok("Panel memanjang mengikuti laci, tidak bisa dimaksimalkan",
                   xaml.Contains("SizeToContent=\"Height\"") && xaml.Contains("ResizeMode=\"CanMinimize\""));

            // Ukuran tersimpan bertahan lintas sesi.
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phoron-uji-ukuran-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            System.IO.Directory.CreateDirectory(dir);
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
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
