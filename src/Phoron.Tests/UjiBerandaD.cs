using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Beranda versi D: baris PHP yang tenang, kartu proyek Node, situs PHP
        /// terakhir, rail navigasi berlabel, dan menu baki yang bisa menjalankan
        /// proyek Node tanpa membuka jendela.
        /// </summary>
        static void UjiBerandaD(string sandbox)
        {
            Bagian("Beranda versi D dan menu baki");

            // ------------------------------------------- Situs terakhir tersimpan
            var dir = Path.Combine(sandbox, "beranda-d");
            Directory.CreateDirectory(dir);
            var dulu = Paths.Root;
            try
            {
                Paths.Root = dir;
                var s = Settings.Load();
                Ok("Situs terakhir kosong di awal", s.SitusTerakhir.Count == 0);
                s.SitusTerakhir.Add(@"C:\laragon\www\apotek");
                s.SitusTerakhir.Add(@"D:\kerja\klien-a\api lama");
                s.Save();
                var ulang = Settings.Load();
                Ok("Situs terakhir kembali utuh, jalur berspasi juga",
                   ulang.SitusTerakhir.SequenceEqual(new[] { @"C:\laragon\www\apotek", @"D:\kerja\klien-a\api lama" }),
                   string.Join(" | ", ulang.SitusTerakhir));
            }
            finally { Paths.Root = dulu; }

            // ------------------------------------------- Kabel-kabelnya
            var engine = BacaSumber("Phoron.Core", "Engine.cs");
            if (engine != null)
            {
                Ok("Satu jalur untuk menjalankan proyek Node", engine.Contains("public string JalankanNode(NodeApp app)"));
                Ok("Membuka situs lewat Engine ikut mencatatnya", Regex.IsMatch(engine,
                   @"public string BukaSitus\(Site site\)[\s\S]*?CatatSitusDibuka\(site\.Path\);"));
            }
            var node = BacaSumber("Phoron.App", "Pages", "NodePage.xaml.cs");
            if (node != null)
                Ok("Halaman Node memakai jalur yang sama", node.Contains("_e.JalankanNode(a)"));
            var situs = BacaSumber("Phoron.App", "Pages", "SitesPage.xaml.cs");
            if (situs != null)
                Ok("Halaman Situs membuka lewat Engine", situs.Contains("_e.BukaSitus(s)"));

            var baki = BacaSumber("Phoron.App", "Pengendali.cs");
            if (baki != null)
            {
                Ok("Menu baki dirakit ulang setiap kali dibuka", baki.Contains("menu.Opening += (s, e) => IsiMenuBaki(menu);"),
                   "menu yang dirakit sekali menawarkan proyek dan situs yang sudah lewat");
                Ok("Menu baki bisa menjalankan proyek Node", baki.Contains("_engine.JalankanNode(app)"));
            }

            var beranda = BacaSumber("Phoron.App", "Pages", "DashboardPage.xaml.cs");
            if (beranda != null)
            {
                var ktor = Potong(beranda, "public DashboardPage()", "\n        }\n");
                Ok("Beranda melanggan keluaran Node di Loaded, bukan di konstruktor",
                   Regex.IsMatch(ktor, @"Loaded \+= \(s, ev\) =>\s*\{\s*LepasNode\(\);\s*_e\.Node\.Output \+= OnNode;")
                   && ktor.Contains("Unloaded += (s, ev) => LepasNode();"));
                Ok("Kartu Node membaca keluaran dari NodeRunner", beranda.Contains("_e.Node.Riwayat(a.Path)"));
                Ok("Kartu Node digambar ulang secara digabung",
                   Regex.IsMatch(beranda, @"Interlocked\.Exchange\(ref _nodeTertunda, 1\) == 1\) return;"));
            }

            var jendela = BacaSumber("Phoron.App", "MainWindow.xaml.cs");
            if (jendela != null)
                Ok("Rail navigasi aman dari butir pemisah",
                   !jendela.Contains("foreach (ListBoxItem item in Nav.Items)"),
                   "butir pemisah di rail bukan ListBoxItem berisi label");
        }
    }
}
