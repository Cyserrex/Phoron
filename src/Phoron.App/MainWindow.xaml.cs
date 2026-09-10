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
            ApplicationThemeManager.ApplySystemTheme();

            PasangIkon();
            AppState.Init(_engine);
            _engine.Services.StateChanged += (kind, state) => Dispatcher.Invoke(RefreshStatus);
            _engine.Reload();

            SetupTray();
            Closing += OnClosing;
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
        }

        /// <summary>
        /// Ikon jendela dan bilah judul diambil dari ikon Win32 exe-nya sendiri,
        /// bukan dari berkas .ico yang ditanam ulang sebagai Resource WPF.
        /// ApplicationIcon di csproj sudah menaruhnya di dalam exe; menanamkannya
        /// kedua kali hanya menggandakan 140 KB di dalam berkas yang sama.
        /// </summary>
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
                case "ekstensi": Host.Content = new ExtensionsPage(); break;
                case "log": Host.Content = new LogPage(); break;
                case "setelan": Host.Content = new SettingsPage(); break;
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
            TxtWeb.Text = webName + " " + web.ToString().ToLowerInvariant();
            TxtDb.Text = "MySQL " + db.ToString().ToLowerInvariant();
            TxtProfil.Text = _engine.Active != null ? _engine.Active.Name : "(belum ada profil)";

            bool anyRunning = web == ServiceState.Jalan || db == ServiceState.Jalan;
            BtnPower.Content = anyRunning ? "Matikan semua" : "Nyalakan semua";
            bool busy = web == ServiceState.Menyalakan || web == ServiceState.Mematikan
                     || db == ServiceState.Menyalakan || db == ServiceState.Mematikan;
            BtnPower.IsEnabled = !busy;

            if (_tray != null)
            {
                _tray.Text = "Phoron - " + (anyRunning ? "berjalan" : "berhenti");
                if (_tray.ContextMenuStrip != null && _tray.ContextMenuStrip.Items.Count > 0)
                    _tray.ContextMenuStrip.Items[0].Text = anyRunning ? "Matikan semua" : "Nyalakan semua";
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
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            // ShutdownMode aplikasi ini OnExplicitShutdown (lihat Program.cs),
            // jadi menutup jendela saja tidak mengakhiri prosesnya.
            Application.Current.Shutdown();
        }
    }
}
