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
                _single = ObjekAntarProses.BuatMutex(NamaMutex, true, out baru);
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
                // Catatan: bahasa belum diterapkan di titik ini (Engine dan
                // setelannya belum dimuat), jadi Lang.T masih jatuh ke teks
                // sumber berbahasa Indonesia.
                MessageBox.Show(
                    Phoron.Core.Lang.T("Phoron sudah berjalan. Lihat ikonnya di baki sistem (system tray).")
                    + (tertutup
                        ? Environment.NewLine + Environment.NewLine
                          + Phoron.Core.Lang.T("Phoron yang berjalan tampaknya dijalankan sebagai Administrator, jadi jendelanya hanya bisa dimunculkan lewat ikon itu.")
                        : ""),
                    "Phoron", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            // --tray: dijalankan Windows saat boot (lihat Autostart). Jendelanya
            // tidak dimunculkan, cukup ikon di baki sistem.
            bool keTray = Array.Exists(args, a =>
                string.Equals(a, Phoron.Core.Autostart.ArgumenTray, StringComparison.OrdinalIgnoreCase));

            // Mode daemon: phorond.exe ada di sebelah exe ini. Layanan dan ikon
            // baki milik daemon; UI hanya hidup selama jendelanya terbuka.
            bool modeDaemon = Phoron.Core.Jarak.ModeDaemon.Aktif;
            Phoron.Core.Jarak.KlienDaemon klien = null;
            if (modeDaemon)
            {
                klien = SambungDaemon();
                if (klien == null) return KeluarGalat;
                // Entri autostart lama (Phoron.exe --tray): cukup pastikan
                // daemonnya hidup - ikon baki memang miliknya.
                if (keTray) { klien.Dispose(); return 0; }
            }

            var app = new App();
            // Konteks sinkronisasi WPF dipasang SEJAK SEKARANG, bukan menunggu
            // app.Run(). Jendela dan Beranda sudah dibuat sebelum loop pesan
            // berjalan, dan "await" yang dimulai di sana (ShowConflicts) tanpa
            // konteks ini melanjutkan di thread pool lalu menyentuh kontrol WPF -
            // dalam mode daemon, Phoron mati tiga detik sesudah dibuka.
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
            app.InitializeComponent();
            // Tanpa ini, mulai dalam keadaan tersembunyi berarti tidak ada jendela
            // terbuka sama sekali, dan WPF akan menutup aplikasinya seketika.
            // Penutupan sekarang jadi urusan Pengendali yang memanggil Shutdown().
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Engine dibuat DI SINI, bukan sebagai penginisialisasi medan di
            // MainWindow. Penginisialisasi medan jalan sebelum badan konstruktor
            // mana pun, jadi tidak ada satu pun tempat yang bisa menangkapnya:
            // phoron.ini yang terkunci - berkas di drive jaringan yang putus,
            // antivirus yang sedang memindainya - membuat Phoron mati saat start
            // tanpa sepatah pun keterangan.
            Phoron.Core.Engine engine;
            if (klien != null)
            {
                try
                {
                    var d = app.Dispatcher;
                    engine = new Phoron.Core.Engine(klien, kerja => d.BeginInvoke(kerja));
                }
                catch (Exception ex) { klien.Dispose(); return GagalMembuatEngine(ex); }
            }
            else
            {
                try { engine = new Phoron.Core.Engine(); }
                catch (Exception ex) { return GagalMembuatEngine(ex); }
            }

            // Pengendali, bukan jendela, yang memiliki umur aplikasi: ikon baki
            // dan sinyal antarproses harus tetap hidup selagi jendelanya dibuang.
            new Pengendali(engine).Mulai(!keTray);
            return app.Run();
        }

        static int GagalMembuatEngine(Exception ex)
        {
            var jalur = Phoron.Core.Crash.Tulis(ex, "membuat Engine saat start");
            // Setelan (termasuk bahasanya) justru gagal dibaca di sini, jadi
            // pesan ini selalu tampil dalam bahasa sumber.
            MessageBox.Show(
                Phoron.Core.Lang.T("Phoron tidak bisa membaca setelannya, jadi tidak bisa dijalankan.")
                + "\n\n" + ex.Message
                + (jalur != null ? "\n\n" + Phoron.Core.Lang.T("Rincian tersimpan di:") + "\n" + jalur : ""),
                "Phoron", MessageBoxButton.OK, MessageBoxImage.Error);
            return KeluarGalat;
        }

        /// <summary>
        /// Sambung ke phorond, menjalankannya bila belum hidup. Null (sesudah
        /// kotak pesan) bila tidak bisa - UI tanpa daemon tidak punya apa pun
        /// untuk dikendalikan, dan diam-diam memegang layanan sendiri justru
        /// membuat dua pemilik Apache.
        /// </summary>
        static Phoron.Core.Jarak.KlienDaemon SambungDaemon()
        {
            var klien = new Phoron.Core.Jarak.KlienDaemon(Phoron.Core.Jarak.ModeDaemon.NamaPipe)
            {
                JalurDaemon = Phoron.Core.Jarak.ModeDaemon.JalurDaemon,
                AkarHarapan = Phoron.Core.Paths.Root,
            };
            try
            {
                klien.Sambung();
                return klien;
            }
            catch (Exception ex)
            {
                klien.Dispose();
                // Bahasa belum diterapkan di titik ini (setelan belum dimuat).
                MessageBox.Show(
                    Phoron.Core.Lang.T("Phoron tidak bisa tersambung ke daemonnya (phorond.exe).")
                    + "\n\n" + ex.Message,
                    "Phoron", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Nama event yang ditunggu instans yang sedang berjalan (lihat
        /// Pengendali.PasangSinyalTampil). Disetel salinan kedua untuk meminta
        /// jendelanya dimunculkan dari baki sistem.
        /// </summary>
        public static readonly string NamaSinyalTampil = "Phoron.Tampilkan" + Phoron.Core.Jarak.ModeDaemon.AkhiranObjek;

        /// <summary>
        /// Mutex instans tunggal UI. Akhiran dari PHORON_INSTANS hanya untuk uji:
        /// UI uji bisa berjalan di samping Phoron yang sedang dipakai.
        /// </summary>
        static readonly string NamaMutex = "Phoron.SingleInstance" + Phoron.Core.Jarak.ModeDaemon.AkhiranObjek;

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
            // Pengendali dimulai. Di antara keduanya ada celah beberapa ratus
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
                reason + "\n\n" + Phoron.Core.Lang.T("Jalankan ulang Phoron sebagai Administrator sekarang?"),
                Phoron.Core.Lang.T("Perlu hak Administrator"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (jawab != MessageBoxResult.Yes) return false;
            // Mode daemon: hosts dan sertifikat ditulis phorond, jadi DAEMON yang
            // dijalankan ulang ber-Administrator. UI ini tetap jalan apa adanya.
            var mesin = AppState.Engine;
            if (mesin != null && mesin.ModeJarak) { NaikkanDaemon(mesin); return false; }
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
                // Lazimnya UAC dibatalkan (Win32Exception 1223). Mutexnya sudah
                // ditutup di atas, dan Phoron ini tetap berjalan - tanpa
                // mengambilnya lagi, klik ikon berikutnya menyalakan Phoron
                // KEDUA yang berebut port 80 dan 3306 dengan yang ini.
                AmbilMutexLagi();
                bool dibatalkan = ex is System.ComponentModel.Win32Exception
                                  && ((System.ComponentModel.Win32Exception)ex).NativeErrorCode == 1223;
                if (!dibatalkan)
                    MessageBox.Show(Phoron.Core.Lang.T("Gagal menaikkan hak akses: {0}", ex.Message), "Phoron",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Kirim mulai_ulang_admin, tunggu daemon barunya tersambung, lalu
        /// segarkan halaman. Tidak menahan utas layar: dialog UAC milik daemon
        /// bisa menunggu orangnya lama.
        /// </summary>
        static async void NaikkanDaemon(Phoron.Core.Engine mesin)
        {
            string galat;
            try { galat = await mesin.MulaiUlangAdminAsync(); }
            catch (Exception ex) { galat = ex.Message; }
            AppState.RaiseChanged();
            if (Pengendali.Ini != null) Pengendali.Ini.SegarkanStatus();
            // UAC yang dibatalkan juga sampai di sini sebagai galat dari
            // daemon; pesannya dari daemon, bukan tebakan UI.
            if (galat != null)
                MessageBox.Show(Phoron.Core.Lang.T("Gagal menaikkan hak akses: {0}", galat), "Phoron",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        static void AmbilMutexLagi()
        {
            try
            {
                bool baru;
                _single = ObjekAntarProses.BuatMutex(NamaMutex, true, out baru);
            }
            catch { }
        }
    }
}
