using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        [DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int jumlah);

        /// <summary>Cara Windows sendiri memecah baris perintah menjadi argumen.</summary>
        static string[] PecahArgumen(string baris)
        {
            int n;
            var p = CommandLineToArgvW("prog.exe " + baris, out n);
            if (p == IntPtr.Zero) return new string[0];
            try
            {
                var hasil = new string[n];
                for (int i = 0; i < n; i++)
                    hasil[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, i * IntPtr.Size));
                return hasil.Skip(1).ToArray();
            }
            finally { LocalFree(p); }
        }

        /// <summary>
        /// Lima bug sisa audit: ganti profil saat layanan masih menyala, dobel
        /// klik, nama basis data di baris perintah mysqldump, Alt+F4 saat
        /// mengunduh, dan keluaran Node yang hilang saat halamannya tertutup.
        /// </summary>
        static void UjiBatchTiga()
        {
            Bagian("Sisa audit: switch, dobel klik, mysqldump, unduhan, Node");

            // ------------------------------------------- Switch saat menyala
            Ok("Menyalakan dihitung hidup", Engine.Hidup(ServiceState.Menyalakan));
            Ok("Jalan dihitung hidup", Engine.Hidup(ServiceState.Jalan));
            Ok("Berhenti, Gagal, Mematikan tidak dihitung hidup",
               !Engine.Hidup(ServiceState.Berhenti) && !Engine.Hidup(ServiceState.Gagal)
               && !Engine.Hidup(ServiceState.Mematikan));
            var engine = BacaSumber("Phoron.Core", "Engine.cs");
            if (engine != null)
            {
                var sw = Potong(engine, "public async Task<List<string>> SwitchAsync", "\n        }\n");
                Ok("Switch menghentikan layanan yang masih menyala",
                   sw.Contains("Hidup(Services.WebState)") && sw.Contains("Hidup(Services.DbState)"),
                   "hanya Jalan yang dihentikan - start yang sedang berjalan selesai dengan profil lama");
                Ok("Ganti profil diantre satu per satu",
                   sw.Contains("_gantiProfil.WaitAsync()") && sw.Contains("_gantiProfil.Release()"),
                   "dua ganti profil bersamaan saling membatalkan");
            }

            // ------------------------------------------- Dobel klik
            foreach (var halaman in new[] { "ProfilesPage.xaml.cs", "ExtensionsPage.xaml.cs" })
            {
                var isi = BacaSumber("Phoron.App", "Pages", halaman);
                if (isi == null) continue;
                var nama = halaman.StartsWith("Profiles") ? "BtnSwitch_Click" : "BtnRestart_Click";
                var tubuh = Potong(isi, "async void " + nama, "\n        }\n");
                Ok(halaman + ": " + nama + " menonaktifkan tombolnya selama berjalan",
                   Regex.IsMatch(tubuh, @"IsEnabled = false;") && Regex.IsMatch(tubuh, @"finally\s*\{[^}]*IsEnabled = true"),
                   "klik kedua menjalankan prosesnya dua kali bersamaan");
            }

            // ------------------------------------------- mysqldump
            foreach (var nama in new[] { "my db", "toko", "a\"b", @"c:\x\", @"x\""y", "", "dua  spasi", "é日本" })
            {
                var argumen = PecahArgumen(MySqlKlien.KutipArgumen(nama) + " --berikut");
                Ok("Nama basis data utuh sebagai satu argumen: [" + nama + "]",
                   argumen.Length == 2 && argumen[0] == nama && argumen[1] == "--berikut",
                   string.Join(" | ", argumen));
            }
            var s = new MySqlKlien.Sambungan { Dump = "mysqldump-palsu.exe", KerjaDi = "." };
            var tolak = MySqlKlien.Ekspor(s, "--all-databases", Path.Combine(Path.GetTempPath(), "x.sql"));
            Ok("Nama berawalan minus ditolak, tidak dijadikan opsi",
               tolak != null && tolak.Contains("minus"), tolak ?? "");

            // ------------------------------------------- Alt+F4 saat mengunduh
            var unduh = BacaSumber("Phoron.App", "DownloadWindow.xaml.cs");
            if (unduh != null)
            {
                Ok("Menutup jendela unduhan membatalkan unduhannya",
                   Regex.IsMatch(unduh, @"Closing \+= \(s, e\) => \{ if \(!_selesai\) _batal\.Cancel\(\); \};"),
                   "Alt+F4 meninggalkan unduhan yang jalan terus");
                Ok("Hasil unduhan tidak dipasang ke jendela yang sudah tertutup",
                   Regex.IsMatch(unduh, @"if \(_tertutup\) return;\s*DialogResult ="),
                   "DialogResult ke jendela tertutup melempar InvalidOperationException");
            }

            // ------------------------------------------- Keluaran Node
            var node = new NodeRunner();
            var folder = @"C:\proyek-uji";
            node.Sisipkan(folder, "baris pertama");
            node.Sisipkan(folder, "galat: port 5173 dipakai");
            var riwayat = node.Riwayat(folder);
            Ok("Keluaran Node tersimpan walau tidak ada yang mendengarkan",
               riwayat.Count == 2 && riwayat[1] == "galat: port 5173 dipakai", string.Join(" | ", riwayat));
            Ok("Riwayat tidak peka huruf besar-kecil folder", node.Riwayat(folder.ToUpperInvariant()).Count == 2);
            for (int i = 0; i < NodeRunner.BatasRiwayat + 50; i++) node.Sisipkan(folder, "baris " + i);
            riwayat = node.Riwayat(folder);
            Ok("Riwayat dibatasi " + NodeRunner.BatasRiwayat + " baris terakhir",
               riwayat.Count == NodeRunner.BatasRiwayat && riwayat.Last() == "baris " + (NodeRunner.BatasRiwayat + 49));
            Ok("Riwayat folder lain kosong", node.Riwayat(@"C:\lain").Count == 0);
            var halNode = BacaSumber("Phoron.App", "Pages", "NodePage.xaml.cs");
            if (halNode != null)
                Ok("Halaman Node membaca riwayat dari NodeRunner, bukan penampung sendiri",
                   halNode.Contains("_e.Node.Riwayat(") && !halNode.Contains("Dictionary<string, Queue<string>>"));
        }
    }
}
