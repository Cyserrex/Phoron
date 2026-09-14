using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace Phoron.App
{
    public static class Program
    {
        static Mutex _single;

        [STAThread]
        public static int Main(string[] args)
        {
            EmbeddedAssemblies.Install();
            return Start(args);
        }

        /// <summary>Argumen proses pembantu yang HANYA menulis berkas hosts lalu keluar.</summary>
        public const string ArgumenSinkronHosts = "--sinkron-hosts";

        /// <summary>Kode keluar proses pembantu, dibaca pemanggilnya untuk melaporkan hasil.</summary>
        public const int KeluarBerhasil = 0;
        public const int KeluarTidakBerhak = 2;
        public const int KeluarGalat = 3;

        static int Start(string[] args)
        {
            // Dijalankan berhak Administrator lewat "runas", menulis hosts, lalu
            // keluar. Sengaja diperiksa SEBELUM mutex instans tunggal: Phoron yang
            // biasa masih berjalan saat ini dipanggil, dan pembantu ini bukan
            // salinan kedua aplikasi - ia tidak membuka jendela dan tidak
            // menyentuh Apache, MySQL, atau berkas konfigurasi apa pun.
            if (Array.Exists(args, a => string.Equals(a, ArgumenSinkronHosts, StringComparison.OrdinalIgnoreCase)))
                return SinkronHostsSaja();

            // Dua salinan Phoron berarti dua Apache berebut port 80 dan dua
            // penulis berkas konfigurasi yang sama. Instans kedua langsung keluar.
            bool baru;
            _single = new Mutex(true, "Phoron.SingleInstance", out baru);
            if (!baru)
            {
                MessageBox.Show("Phoron sudah berjalan. Lihat ikonnya di baki sistem (system tray).",
                    "Phoron", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            // --tray: dijalankan Windows saat boot (lihat Autostart). Jendelanya
            // tidak dimunculkan, cukup ikon di baki sistem.
            bool keTray = Array.Exists(args, a =>
                string.Equals(a, Phoron.Core.Autostart.ArgumenTray, StringComparison.OrdinalIgnoreCase));

            var app = new App();
            app.InitializeComponent();
            // Tanpa ini, mulai dalam keadaan tersembunyi berarti tidak ada jendela
            // terbuka sama sekali, dan WPF akan menutup aplikasinya seketika.
            // Penutupan sekarang jadi urusan MainWindow yang memanggil Shutdown().
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var win = new MainWindow();
            app.MainWindow = win;
            if (!keTray) win.Show();
            return app.Run();
        }

        static int SinkronHostsSaja()
        {
            try
            {
                if (!Phoron.Core.HostsFile.IsAdmin()) return KeluarTidakBerhak;
                Phoron.Core.HostsTool.DaftarkanSemua();
                return KeluarBerhasil;
            }
            catch (UnauthorizedAccessException) { return KeluarTidakBerhak; }
            catch { return KeluarGalat; }
        }

        /// <summary>
        /// Jalankan ulang dengan hak Administrator. Dibutuhkan untuk menyunting
        /// berkas hosts dan memasang sertifikat ke Trusted Root - dua hal yang
        /// tidak sering dilakukan, jadi tidak sepadan memaksa UAC di tiap start.
        /// </summary>
        public static bool RestartAsAdmin(string reason)
        {
            var jawab = MessageBox.Show(
                reason + "\n\nJalankan ulang Phoron sebagai Administrator sekarang?",
                "Perlu hak Administrator", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (jawab != MessageBoxResult.Yes) return false;
            try
            {
                var psi = new ProcessStartInfo(Assembly.GetEntryAssembly().Location)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                };
                // Handle mutex DITUTUP, bukan sekadar dilepas, dan dilakukan
                // SEBELUM salinan baru dijalankan. Penjaga instans tunggal
                // bersandar pada ada atau tidaknya objek mutex, bukan pada
                // kepemilikannya - selama proses ini masih memegang handle-nya,
                // salinan yang baru naik hak akan mengira Phoron sudah berjalan
                // lalu keluar seketika dengan kotak pesan yang membingungkan.
                try { _single.ReleaseMutex(); } catch { }
                try { _single.Close(); } catch { }
                Process.Start(psi);
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal menaikkan hak akses: " + ex.Message, "Phoron",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}
