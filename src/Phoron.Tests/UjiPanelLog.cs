using System;
using System.IO;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Panel log tidak boleh menahan utas yang mencetak barisnya, dan tidak
        /// boleh menggambar ulang sekali per baris.
        ///
        /// Keduanya pernah terjadi di satu baris kode yang sama:
        /// Dispatcher.Invoke(GambarLog). Invoke MEMBLOKIR pemanggilnya, dan
        /// pemanggilnya adalah utas pembaca keluaran httpd dan mysqld; setiap
        /// baris memicu satu penggambaran penuh dua ratus paragraf. Diukur pada
        /// 300 baris beruntun: 12.213 ms dan 300 penggambaran. Sesudah
        /// digabung: 528 ms dan 21 penggambaran.
        ///
        /// Yang diperiksa di sini hanya bahwa kabelnya masih terpasang - bukan
        /// bahwa ia bekerja. Perilakunya dibuktikan dengan menyemburkan 300
        /// baris ke halaman yang sungguhan dan menghitung TextChanged; uji itu
        /// butuh WPF, dan harness ini sengaja tidak menyeretnya masuk.
        /// </summary>
        static void UjiPanelLog()
        {
            Bagian("Panel log tidak menahan utas layanan");

            PeriksaPanel("DashboardPage.xaml.cs", "Beranda");
            PeriksaPanel("NodePage.xaml.cs", "Node");

            // Kuas warna dirakit sekali lalu dibekukan. Sebelumnya tiap baris
            // membuat SolidColorBrush baru dan mengurai warnanya dari teks.
            var kuas = Path.Combine(AkarRepo(), Path.Combine("src", Path.Combine("Phoron.App", "KuasLog.cs")));
            Ok("Kuas log dirakit di satu tempat", File.Exists(kuas), kuas);
            if (File.Exists(kuas))
            {
                var isi = File.ReadAllText(kuas);
                Ok("Kuasnya dibekukan", isi.Contains("Freeze()"),
                   "kuas yang tidak beku disalin diam-diam tiap kali dipakai");
            }
        }

        static void PeriksaPanel(string berkas, string nama)
        {
            var jalur = Path.Combine(AkarRepo(),
                Path.Combine("src", Path.Combine("Phoron.App", Path.Combine("Pages", berkas))));
            if (!File.Exists(jalur)) { Ok(nama + ": berkas halaman ada", false, jalur); return; }
            var isi = File.ReadAllText(jalur);

            Ok(nama + ": keluaran layanan tidak menahan utasnya",
               !isi.Contains("Dispatcher.Invoke("),
               "masih ada Dispatcher.Invoke - utas pembaca keluaran ikut berhenti");
            Ok(nama + ": penggambaran ulang digabung",
               isi.Contains("Interlocked.Exchange"),
               "tidak ada penggabungan - satu semburan berarti ratusan penggambaran");
            Ok(nama + ": digambar pada prioritas Background",
               isi.Contains("DispatcherPriority.Background"),
               "tanpa prioritas rendah, penggambaran mendahului masukan pengguna");
            Ok(nama + ": warnanya dari kuas beku, bukan dirakit per baris",
               !isi.Contains("ColorConverter.ConvertFromString"),
               "masih mengurai warna dari teks di dalam gelung");
        }
    }
}
