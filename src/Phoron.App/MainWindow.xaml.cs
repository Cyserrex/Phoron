using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Phoron.App.Pages;
using Phoron.Core;
using Wpf.Ui.Appearance;
using Forms = System.Windows.Forms;

namespace Phoron.App
{
    public partial class MainWindow
    {
        readonly Engine _engine = new Engine();
        Forms.NotifyIcon _tray;
        bool _reallyClosing;

        public MainWindow()
        {
            InitializeComponent();

            PasangIkon();
            AppState.Init(_engine);
            // Bahasa disetel SEBELUM halaman mana pun dibuat: penerjemahnya
            // bekerja saat XAML dimuat, jadi halaman yang terlanjur dibuat
            // dengan bahasa lama tidak akan berubah sendiri.
            Lang.Pakai(_engine.Settings.Bahasa);
            TerapkanTema(_engine.Settings.Tema);
            _engine.Services.StateChanged += (kind, state) => Dispatcher.Invoke(RefreshStatus);
            _engine.Reload();

            SetupTray();
            Closing += OnClosing;
            SourceInitialized += (s, e) => PasangPengawasSesi();
            PasangSinyalKeluar();
            Nav.SelectedIndex = 0;
            RefreshStatus();

            // Bukan lewat event Loaded: kalau Phoron dijalankan Windows dengan
            // --tray, jendelanya tidak pernah ditampilkan sehingga Loaded tidak
            // pernah menyala - dan konfigurasi tidak akan pernah ditulis.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(async () => await MulaiAsync()));
        }

        async System.Threading.Tasks.Task MulaiAsync()
        {
            _engine.Apply();
            RefreshStatus();
            if (_engine.Settings.AutoStartServices)
            {
                _engine.Say("Auto-start aktif - menyalakan layanan...");
                await _engine.StartAllAsync();
            }

            // Pengecekan rilis dilakukan PALING AKHIR dan tanpa kotak pesan.
            // Menyalakan Apache dan MySQL jauh lebih mendesak daripada menanyakan
            // GitHub, dan jaringan yang lambat tidak boleh menahan keduanya.
            // Hasilnya muncul sebagai baris di panel Perhatian, bukan dialog yang
            // menghadang pekerjaan orang begitu jendela terbuka.
            try
            {
                var hasil = await _engine.CekPembaruanAsync(false);
                if (hasil != null)
                {
                    var dash = Host.Content as DashboardPage;
                    if (dash != null) dash.RefreshState();
                }
            }
            catch { /* tidak ada internet bukan alasan aplikasi gagal dibuka */ }
        }

        /// <summary>
        /// Ikon jendela dan bilah judul diambil dari ikon Win32 exe-nya sendiri,
        /// bukan dari berkas .ico yang ditanam ulang sebagai Resource WPF.
        /// ApplicationIcon di csproj sudah menaruhnya di dalam exe; menanamkannya
        /// kedua kali hanya menggandakan 140 KB di dalam berkas yang sama.
        /// </summary>
        /// <summary>
        /// Terapkan tema. "sistem" mengikut setelan terang/gelap Windows, dan
        /// itulah bawaannya - sebagian besar orang sudah menentukan pilihannya di
        /// tingkat sistem, dan aplikasi yang mengabaikannya terasa asing.
        /// </summary>
        public static void TerapkanTema(string tema)
        {
            switch ((tema ?? "sistem").ToLowerInvariant())
            {
                case "terang":
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case "gelap":
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                default:
                    ApplicationThemeManager.ApplySystemTheme();
                    break;
            }
        }

        /// <summary>
        /// Gambar ulang seluruh label setelah bahasa berganti. Navigasi dan
        /// panel status tinggal di jendela ini dan tidak ikut dibuat ulang saat
        /// berpindah halaman, jadi keduanya harus disentuh sendiri.
        /// </summary>
        public void TerapkanBahasa()
        {
            var terpilih = Nav.SelectedIndex;
            foreach (ListBoxItem item in Nav.Items)
            {
                var panel = item.Content as StackPanel;
                if (panel == null || panel.Children.Count < 2) continue;
                var teks = panel.Children[1] as TextBlock;
                if (teks == null) continue;
                switch ((item.Tag ?? "").ToString())
                {
                    case "beranda": teks.Text = Lang.T("Beranda"); break;
                    case "profil": teks.Text = Lang.T("Profil"); break;
                    case "versi": teks.Text = Lang.T("Versi"); break;
                    case "situs": teks.Text = Lang.T("Situs"); break;
                    case "node": teks.Text = Lang.T("Node / TS"); break;
                    case "ekstensi": teks.Text = Lang.T("Ekstensi PHP"); break;
                    case "log": teks.Text = Lang.T("Log"); break;
                    case "setelan": teks.Text = Lang.T("Pengaturan"); break;
                    case "tentang": teks.Text = Lang.T("Tentang"); break;
                }
            }
            BtnKeluar.Content = Lang.T("Keluar");
            RefreshStatus();

            // Halaman yang sedang terbuka dibuat ulang supaya teksnya ikut
            // berganti - penerjemahnya bekerja saat XAML dimuat, bukan lewat
            // pengikatan hidup.
            Nav.SelectedIndex = -1;
            Nav.SelectedIndex = terpilih;
        }

        void PasangIkon()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                {
                    if (ico == null) return;
                    var sumber = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    Icon = sumber;
                    BilahJudul.Icon = new Wpf.Ui.Controls.ImageIcon { Source = sumber, Width = 16, Height = 16 };
                }
            }
            catch { /* ikon hilang bukan alasan aplikasi gagal dibuka */ }
        }

        // ------------------------------------------------------------- Navigasi

        void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = Nav.SelectedItem as ListBoxItem;
            if (item == null) return;
            switch ((item.Tag ?? "").ToString())
            {
                case "profil": Host.Content = new ProfilesPage(); break;
                case "versi": Host.Content = new VersionsPage(); break;
                case "situs": Host.Content = new SitesPage(); break;
                case "node": Host.Content = new NodePage(); break;
                case "ekstensi": Host.Content = new ExtensionsPage(); break;
                case "log": Host.Content = new LogPage(); break;
                case "setelan": Host.Content = new SettingsPage(); break;
                case "tentang": Host.Content = new AboutPage(); break;
                default: Host.Content = new DashboardPage(); break;
            }
        }

        public void GoTo(string tag)
        {
            foreach (ListBoxItem item in Nav.Items)
                if ((item.Tag ?? "").ToString() == tag) { Nav.SelectedItem = item; return; }
        }

        // --------------------------------------------------------------- Status

        public void RefreshStatus()
        {
            var web = _engine.Services.WebState;
            var db = _engine.Services.DbState;
            var webName = _engine.Active != null && _engine.Active.WebServer == "nginx" ? "Nginx" : "Apache";

            DotWeb.Fill = Dot(web);
            DotDb.Fill = Dot(db);
            TxtWeb.Text = webName + " " + Lang.T(web.ToString().ToLowerInvariant());
            TxtDb.Text = "MySQL " + Lang.T(db.ToString().ToLowerInvariant());
            TxtProfil.Text = _engine.Active != null ? _engine.Active.Name : Lang.T("(belum ada profil)");

            bool anyRunning = web == ServiceState.Jalan || db == ServiceState.Jalan;
            BtnPower.Content = Lang.T(anyRunning ? "Matikan semua" : "Nyalakan semua");
            // Merah saat tombolnya berarti "matikan": warnanya harus menyatakan
            // akibat penekanan, bukan sekadar menonjol. Biru untuk aksi yang
            // menghidupkan dan untuk aksi yang mematikan membuat keduanya
            // gampang tertukar saat diklik cepat.
            BtnPower.Appearance = anyRunning
                ? Wpf.Ui.Controls.ControlAppearance.Danger
                : Wpf.Ui.Controls.ControlAppearance.Primary;
            bool busy = web == ServiceState.Menyalakan || web == ServiceState.Mematikan
                     || db == ServiceState.Menyalakan || db == ServiceState.Mematikan;
            BtnPower.IsEnabled = !busy;

            if (_tray != null)
            {
                _tray.Text = "Phoron - " + (anyRunning ? "berjalan" : "berhenti");
                if (_tray.ContextMenuStrip != null && _tray.ContextMenuStrip.Items.Count > 0)
                    _tray.ContextMenuStrip.Items[0].Text = Lang.T(anyRunning ? "Matikan semua" : "Nyalakan semua");
            }

            var dash = Host.Content as DashboardPage;
            if (dash != null) dash.RefreshState();
        }

        static Brush Dot(ServiceState s)
        {
            switch (s)
            {
                case ServiceState.Jalan: return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                case ServiceState.Gagal: return new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x4A));
                case ServiceState.Menyalakan:
                case ServiceState.Mematikan: return new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x20));
                default: return new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
            }
        }

        async void BtnPower_Click(object sender, RoutedEventArgs e)
        {
            await TogglePower();
        }

        /// <summary>
        /// Mendengarkan WM_QUERYENDSESSION.
        ///
        /// Restart Manager - yang dipakai pemasang Inno Setup untuk menutup
        /// aplikasi yang sedang berjalan - mengirim pesan itu ke jendela
        /// aplikasi. Tanpa penanganan ini, permintaan tutupnya jatuh ke jalur
        /// penutupan biasa, dan perilaku "mengecil ke baki sistem" justru
        /// MEMBATALKAN penutupan. Pemasang lalu menghentikan prosesnya paksa,
        /// dan Apache serta MySQL tidak pernah sempat dimatikan dengan rapi.
        /// </summary>
        void PasangPengawasSesi()
        {
            var sumber = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (sumber == null) return;
            sumber.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WM_QUERYENDSESSION = 0x0011;
                const int WM_ENDSESSION = 0x0016;
                if (msg == WM_QUERYENDSESSION || msg == WM_ENDSESSION)
                {
                    // Ditandai sebagai penutupan sungguhan supaya OnClosing tidak
                    // mengalihkannya jadi "sembunyi ke baki sistem".
                    _reallyClosing = true;
                    _engine.Say("Diminta menutup diri oleh sistem (pemasang atau shutdown) - "
                                + "mematikan layanan dulu.");
                    try { _engine.Services.StopAll(); _engine.Node.StopAll(); } catch { }
                }
                return IntPtr.Zero;
            });
        }

        /// <summary>
        /// Event bernama yang bisa disetel pihak lain - khususnya pemasang
        /// pembaruan - untuk meminta Phoron menutup diri dengan rapi.
        ///
        /// Restart Manager saja tidak cukup: ia hanya menyasar aplikasi yang
        /// mengunci berkas yang akan ditimpa, dan permintaannya bisa tidak
        /// terjawab kalau versi yang sedang berjalan belum mengenal pesan itu.
        /// Sinyal ini memberi pemasang jalan yang lugas untuk meminta - bukan
        /// memaksa - Phoron berhenti, sehingga Apache dan MySQL sempat dimatikan
        /// dengan rapi sebelum berkasnya diganti.
        /// </summary>
        System.Threading.EventWaitHandle _sinyalKeluar;
        System.Threading.RegisteredWaitHandle _daftarSinyal;

        void PasangSinyalKeluar()
        {
            try
            {
                bool baru;
                _sinyalKeluar = new System.Threading.EventWaitHandle(
                    false, System.Threading.EventResetMode.AutoReset,
                    "Phoron.KeluarSekarang", out baru);
                _daftarSinyal = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _sinyalKeluar,
                    (keadaan, kehabisanWaktu) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _engine.Say("Diminta menutup diri oleh pemasang - mematikan layanan dulu.");
                        TutupUntukPembaruan();
                    })),
                    null, System.Threading.Timeout.Infinite, true);
            }
            catch { /* tanpa sinyal, pemasang masih punya jalur paksa */ }
        }

        /// <summary>Tutup Phoron sepenuhnya karena pemasang pembaruan akan berjalan.</summary>
        public void TutupUntukPembaruan()
        {
            _reallyClosing = true;
            Close();
        }

        void BtnKeluar_Click(object sender, RoutedEventArgs e)
        {
            bool ada = _engine.Services.WebState == ServiceState.Jalan
                    || _engine.Services.DbState == ServiceState.Jalan
                    || _engine.Node.RunningFolders.Any();
            if (ada && !AppState.Ask("Masih ada layanan yang berjalan. Semuanya akan dimatikan.\n\n"
                                     + "Keluar dari Phoron sekarang?")) return;
            _reallyClosing = true;
            Close();
        }

        public async System.Threading.Tasks.Task TogglePower()
        {
            bool anyRunning = _engine.Services.WebState == ServiceState.Jalan
                           || _engine.Services.DbState == ServiceState.Jalan;
            if (anyRunning) await _engine.StopAllAsync();
            else await _engine.StartAllAsync();
            RefreshStatus();
        }

        // ----------------------------------------------------------------- Tray

        void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Nyalakan semua", null, async (s, e) => await TogglePower());
            menu.Items.Add("Buka folder www", null, (s, e) => Shell.Open(Paths.Www));
            menu.Items.Add("Buka localhost", null, (s, e) => Shell.Open(_engine.RootUrl()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Tampilkan Phoron", null, (s, e) => ShowFromTray());
            menu.Items.Add("Keluar", null, (s, e) => { _reallyClosing = true; Close(); });

            _tray = new Forms.NotifyIcon
            {
                Visible = true,
                Text = "Phoron",
                ContextMenuStrip = menu,
                // Ikon exe dipakai apa adanya supaya tidak ada berkas .ico lepas
                // yang harus ikut dikirim.
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetEntryAssembly().Location),
            };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_reallyClosing && _engine.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            // Layanan adalah proses anak; membiarkannya hidup setelah aplikasi
            // ditutup berarti port 80 tetap terpakai tanpa ada yang mengaku.
            _engine.Services.StopAll();
            // Server pengembangan Node juga proses anak; kalau ditinggal hidup,
            // port 3000/4321 tetap terpakai oleh proses tanpa jendela.
            _engine.Node.StopAll();
            if (_daftarSinyal != null) { try { _daftarSinyal.Unregister(null); } catch { } }
            if (_sinyalKeluar != null) { try { _sinyalKeluar.Close(); } catch { } }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            // ShutdownMode aplikasi ini OnExplicitShutdown (lihat Program.cs),
            // jadi menutup jendela saja tidak mengakhiri prosesnya.
            Application.Current.Shutdown();
        }
    }
}
