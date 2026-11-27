using System;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk "tutup ke baki" yang membuang jendela, bukan
        /// menyembunyikannya.
        ///
        /// Dulu menutup jendela berarti Hide(): Phoron yang hanya tampil sebagai
        /// ikon baki tetap memegang seluruh pohon visual WPF - 152 MB terukur di
        /// 1.37.1. Sekarang jendela dibuang dan dibuat ulang saat dibuka. Itu
        /// hanya bertahan selama tidak ada apa pun yang diam-diam menahan
        /// jendelanya tetap hidup, dan selama ikon baki, sinyal antarproses,
        /// serta pendengar akhir sesi tidak ikut terbuang bersama jendelanya.
        /// </summary>
        static void UjiJendelaDibuang()
        {
            Bagian("Jendela dibuang saat ditutup ke baki");

            var mw = BacaSumber("Phoron.App", "MainWindow.xaml.cs");
            var pg = BacaSumber("Phoron.App", "Pengendali.cs");
            var pr = BacaSumber("Phoron.App", "Program.cs");
            if (mw == null || pg == null || pr == null) return;

            Ok("Menutup jendela tidak lagi menyembunyikannya",
               !mw.Contains("Hide();") && !mw.Contains("e.Cancel = true"),
               "jendela tersembunyi menahan seluruh pohon visualnya di memori");
            Ok("Jendela tidak melanggan peristiwa Engine",
               !mw.Contains("StateChanged +=") && !mw.Contains(".Log +="),
               "pelanggan menahan jendela tetap hidup selama Engine hidup");
            // Dilepas di Closing, BUKAN Closed: sesudah Closed pohon visualnya
            // sudah hancur, LostFocus tidak lagi menyala, dan isian kotak teks
            // yang sedang diketik saat X ditekan hilang tanpa jejak.
            var tutup = Potong(mw, "void OnClosing(", "\n        }");
            Ok("Halaman dilepas sebelum jendela hancur",
               tutup.Contains("Host.Content = null;"),
               "Unloaded halaman tidak menyala dan langganannya tertinggal");
            Ok("Isian yang sedang diketik tersimpan saat jendela ditutup",
               tutup.Contains("Host.Content = null;") && !mw.Contains("Closed += (s, e) => Host.Content = null"),
               "LostFocus kotak teks tidak menyala, isian yang belum disimpan hilang");
            Ok("Ikon baki milik Pengendali, bukan jendela",
               pg.Contains("new Forms.NotifyIcon") && !mw.Contains("NotifyIcon"), "");
            Ok("Program memulai lewat Pengendali",
               pr.Contains("new Pengendali(engine).Mulai(!keTray)") && !pr.Contains("new MainWindow("), "");
            // Jendela message-only tidak menerima pesan siaran, jadi WM_ENDSESSION
            // tidak pernah sampai dan pemasang mematikan MySQL paksa.
            Ok("Pendengar akhir sesi bukan jendela message-only",
               pg.Contains("new HwndSource(") && !pg.Contains("HWND_MESSAGE =")
               && !pg.Contains("ParentWindow = new IntPtr(-3)"), "");
            Ok("Jalur Shutdown apa pun tetap membereskan layanan dan ikon baki",
               pg.Contains("Application.Current.Exit +="), "");
            Ok("Keputusan ke-baki ditunda sampai sesudah Closed",
               Potong(pg, "void JendelaTertutup(", "void JadwalkanLepasMemori(").Contains("Dispatcher.BeginInvoke"),
               "Closed saat Shutdown akan memanggil Keluar() kedua di tengah dirinya sendiri");
            Ok("Memori dikembalikan ke Windows sesudah jendela dibuang",
               pg.Contains("SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1))"), "");

            // Start --tray tidak pernah membuat jendela, jadi tidak pernah
            // melewati JendelaTertutup. Pemangkasannya harus di akhir start,
            // dan hanya bila memang tidak ada jendela.
            var mulai = Potong(pg, "async Task MulaiAsync()", "async Task MulaiIntiAsync()");
            Ok("Start tanpa jendela ikut memangkas memori sesudah start selesai",
               mulai.Contains("finally") && mulai.Contains("if (!AdaJendela && !_keluar) JadwalkanLepasMemori();"),
               "start --tray tetap memegang memori yang tidak dipakai");
            Ok("Membuka jendela membatalkan pemangkasan yang terjadwal",
               Potong(pg, "public void Tampilkan()", "void JendelaTertutup(").Contains("_pelepasMemori.Stop()"),
               "GC penuh bisa berjalan di tengah jendela yang baru dibuka");
            Ok("Pemangkasan memeriksa ulang jendela saat waktunya tiba",
               pg.Contains("if (AdaJendela || _keluar) return;"), "");
        }
    }
}
