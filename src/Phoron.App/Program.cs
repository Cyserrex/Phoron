using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
            // BARIS PERTAMA, sebelum apa pun yang bisa gagal. Galat yang terjadi
            // sebelum kait ini terpasang tetap berujung pada kotak galat mentah
            // Windows tanpa meninggalkan jejak.
            App.PasangPenangkapGalat();
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
            bool tertutup = false;
            try
            {
                _single = ObjekAntarProses.BuatMutex("Phoron.SingleInstance", true, out baru);
            }
            catch (UnauthorizedAccessException)
            {
                // Mutexnya ADA, hanya tidak bisa dibuka proses ini - lazimnya
                // karena Phoron yang berjalan dinaikkan haknya oleh versi lama
                // yang belum memberi izin apa pun pada objek bernamanya.
                // Tanpa penjaga ini, menekan ikon taskbar berakhir di kotak
                // galat: sudah ada yang berjalan, tapi jalur sopannya tidak
                // pernah tercapai.
                baru = false;
                tertutup = true;
            }
            if (!baru)
            {
                // Tetapi menekan ikon Phoron di taskbar sementara Phoron sudah
                // menyusut ke baki sistem BUKAN kekeliruan yang perlu ditegur.
                // Yang dimaksud pengguna jelas: "tampilkan Phoron". Salinan
                // kedua membangunkan yang pertama lalu keluar tanpa sepatah kata.
                if (BangunkanYangSudahJalan()) return 0;
                MessageBox.Show(
                    "Phoron sudah berjalan. Lihat ikonnya di baki sistem (system tray)."
                    + (tertutup
                        ? Environment.NewLine + Environment.NewLine
                          + "Phoron yang berjalan tampaknya dijalankan sebagai Administrator, "
                          + "jadi jendelanya hanya bisa dimunculkan lewat ikon itu."
                        : ""),
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
            // Engine dibuat DI SINI, bukan sebagai penginisialisasi medan di
            // MainWindow. Penginisialisasi medan jalan sebelum badan konstruktor
            // mana pun, jadi tidak ada satu pun tempat yang bisa menangkapnya:
            // phoron.ini yang terkunci - berkas di drive jaringan yang putus,
            // antivirus yang sedang memindainya - membuat Phoron mati saat start
            // tanpa sepatah pun keterangan.
            Phoron.Core.Engine engine;
            try { engine = new Phoron.Core.Engine(); }
            catch (Exception ex)
            {
                var jalur = Phoron.Core.Crash.Tulis(ex, "membuat Engine saat start");
                MessageBox.Show(
                    "Phoron tidak bisa membaca setelannya, jadi tidak bisa dijalankan."
                    + "\n\n" + ex.Message
                    + (jalur != null ? "\n\nRincian tersimpan di:\n" + jalur : ""),
                    "Phoron", MessageBoxButton.OK, MessageBoxImage.Error);
                return KeluarGalat;
            }

            var win = new MainWindow(engine);
            app.MainWindow = win;
            if (!keTray) win.Show();
            return app.Run();
        }

        /// <summary>
        /// Nama event yang ditunggu instans yang sedang berjalan (lihat
        /// MainWindow.PasangSinyalTampil). Disetel salinan kedua untuk meminta
        /// jendelanya dimunculkan dari baki sistem.
        /// </summary>
        public const string NamaSinyalTampil = "Phoron.Tampilkan";

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllowSetForegroundWindow(int dwProcessId);

        /// <summary>
        /// Minta Phoron yang sudah berjalan memunculkan jendelanya.
        /// Mengembalikan false bila permintaannya tidak sampai - misalnya
        /// salinan pertama masih dalam proses start dan belum memasang
        /// sinyalnya - sehingga pemanggil bisa kembali ke kotak pesan.
        /// </summary>
        static bool BangunkanYangSudahJalan()
        {
            // Mutex dibuat di awal start, sedangkan sinyalnya baru dipasang saat
            // MainWindow terbentuk. Di antara keduanya ada celah beberapa ratus
            // milidetik: klik kedua yang jatuh tepat di situ harus menunggu
            // sebentar, bukan langsung menyerah.
            for (int coba = 0; coba < 20; coba++)
            {
                try
                {
                    EventWaitHandle sinyal;
                    if (EventWaitHandle.TryOpenExisting(NamaSinyalTampil, out sinyal))
                    {
                        // Tanpa izin ini Windows menolak permintaan proses lain
                        // untuk maju ke depan: jendelanya memang tampil, tapi
                        // hanya berkedip di taskbar dan tetap tertutup jendela
                        // yang sedang aktif.
                        IzinkanMajuKeDepan();
                        using (sinyal) sinyal.Set();
                        return true;
                    }
                }
                catch { return false; }
                Thread.Sleep(100);
            }
            return false;
        }

        static void IzinkanMajuKeDepan()
        {
            try
            {
                var aku = Process.GetCurrentProcess();
                foreach (var lain in Process.GetProcessesByName(aku.ProcessName))
                {
                    if (lain.Id == aku.Id) continue;
                    try { AllowSetForegroundWindow(lain.Id); } catch { }
                    lain.Dispose();
                }
            }
            catch { /* izin ini penyedap, bukan syarat */ }
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
