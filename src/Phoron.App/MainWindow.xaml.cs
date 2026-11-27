using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Phoron.App.Pages;
using Phoron.Core;
using Wpf.Ui.Appearance;

namespace Phoron.App
{
    public partial class MainWindow
    {
        readonly Engine _engine;

        /// <param name="tab">Tab yang dibuka, mis. "situs" - dipakai saat jendela dibuat ulang dari baki.</param>
        public MainWindow(Engine engine, string tab = null)
        {
            // Engine disuntikkan, tidak dibuat di sini: pembuatannya bisa gagal
            // membaca phoron.ini, dan kegagalan itu harus punya satu pemilik yang
            // bisa menampilkan pesannya - lihat Program.Start.
            //
            // Jendela ini tampilan sekali pakai: dibuat saat dibuka dan dibuang
            // saat ditutup. Ikon baki, sinyal antarproses, dan jalur keluar
            // tinggal di Pengendali - JANGAN melanggan peristiwa Engine di
            // sini, karena pelanggan itu menahan jendela ini tetap hidup.
            _engine = engine;
            InitializeComponent();

            PasangIkon();
            BtnKeluar.ToolTip = Lang.T("Tutup Phoron sepenuhnya dan matikan semua layanan");
            // Diterapkan lagi tiap kali jendela dibuat: setelan "sistem" hanya
            // membaca tema Windows saat dipanggil, dan temanya bisa saja sudah
            // berganti selama Phoron hanya ada di baki.
            TerapkanTema(_engine.Settings.Tema);

            Closing += OnClosing;
            Nav.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(tab) && tab != "beranda") GoTo(tab);
            RefreshStatus();
        }

        /// <summary>Tag tab yang sedang terbuka, mis. "beranda".</summary>
        public string TagAktif { get { return _tagAktif; } }

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
            foreach (var item in Nav.Items.OfType<ListBoxItem>())
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
                    case "basisdata": teks.Text = Lang.T("Basis data"); break;
                    case "node": teks.Text = Lang.T("Node / TS"); break;
                    case "ekstensi": teks.Text = Lang.T("Ekstensi PHP"); break;
                    case "log": teks.Text = Lang.T("Log"); break;
                    case "setelan": teks.Text = Lang.T("Pengaturan"); break;
                    case "tentang": teks.Text = Lang.T("Tentang"); break;
                }
            }
            TxtKeluar.Text = Lang.T("Keluar");
            BtnKeluar.ToolTip = Lang.T("Tutup Phoron sepenuhnya dan matikan semua layanan");
            RefreshStatus();

            // Halaman yang sedang terbuka dibuat ulang supaya teksnya ikut
            // berganti - penerjemahnya bekerja saat XAML dimuat, bukan lewat
            // pengikatan hidup.
            Nav.SelectedIndex = -1;
            Nav.SelectedIndex = terpilih;
        }

        void PasangIkon() { PasangIkon(this, BilahJudul); }

        /// <summary>Dipakai juga oleh panel ringkas: satu sumber ikon untuk kedua jendela.</summary>
        internal static void PasangIkon(Window w, Wpf.Ui.Controls.TitleBar bilah)
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
                    w.Icon = sumber;
                    if (bilah != null)
                        bilah.Icon = new Wpf.Ui.Controls.ImageIcon { Source = sumber, Width = 16, Height = 16 };
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
                case "basisdata": Host.Content = new DatabasePage(); break;
                case "node": Host.Content = new NodePage(); break;
                case "ekstensi": Host.Content = new ExtensionsPage(); break;
                case "log": Host.Content = new LogPage(); break;
                case "setelan": Host.Content = new SettingsPage(); break;
                case "tentang": Host.Content = new AboutPage(); break;
                default: Host.Content = new DashboardPage(); break;
            }
            _tagAktif = (item.Tag ?? "beranda").ToString();
            // Panel yang sedang terbuka ikut berganti ke bantuan tab baru -
            // bantuan tab lama di sebelah halaman lain hanya menyesatkan.
            if (PanelBantuan.Visibility == Visibility.Visible) IsiPanelBantuan();
        }

        // -------------------------------------------------------------- Bantuan

        string _tagAktif = "beranda";

        void BtnBantuan_Click(object sender, RoutedEventArgs e)
        {
            if (PanelBantuan.Visibility == Visibility.Visible) { PanelBantuan.Visibility = Visibility.Collapsed; return; }
            IsiPanelBantuan();
            PanelBantuan.Visibility = Visibility.Visible;
        }

        void BtnTutupBantuan_Click(object sender, RoutedEventArgs e)
        {
            PanelBantuan.Visibility = Visibility.Collapsed;
        }

        void IsiPanelBantuan()
        {
            var h = Bantuan.Untuk(_tagAktif);
            int i = Bantuan.IndeksBahasa(Lang.Kode);
            TxtBantuanJudul.Text = Lang.T("Bantuan") + ": " + Lang.T(h.Judul);
            IsiBantuan.Children.Clear();
            IsiBantuan.Children.Add(new TextBlock
            {
                Text = h.Ringkas[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), Opacity = 0.85,
            });
            foreach (var b in h.Butir)
            {
                var kotak = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                if (b.Tombol != null)
                    kotak.Children.Add(new TextBlock
                    {
                        Text = Lang.T(b.Tombol), FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2),
                    });
                kotak.Children.Add(new TextBlock
                {
                    Text = b.Arti[i], TextWrapping = TextWrapping.Wrap,
                    Opacity = b.Tombol == null ? 0.75 : 1.0,
                    FontStyle = b.Tombol == null ? FontStyles.Italic : FontStyles.Normal,
                });
                IsiBantuan.Children.Add(kotak);
            }
        }

        public void GoTo(string tag)
        {
            foreach (var item in Nav.Items.OfType<ListBoxItem>())
                if ((item.Tag ?? "").ToString() == tag) { Nav.SelectedItem = item; return; }
        }

        // --------------------------------------------------------------- Status

        public void RefreshStatus()
        {
            var web = _engine.Services.WebState;
            var db = _engine.Services.DbState;
            var webName = _engine.Active != null && _engine.Active.WebServer == "nginx" ? "Nginx" : "Apache";

            // Layanan yang memang tidak dipakai profil ini dikatakan apa
            // adanya, bukan "berhenti" - yang terbaca seolah ia seharusnya
            // jalan - apalagi merah.
            var pakaiWeb = _engine.Active == null || _engine.Active.PakaiWeb;
            var pakaiDb = _engine.Active == null || _engine.Active.PakaiMySql;

            DotWeb.Fill = pakaiWeb ? Dot(web) : Dot(ServiceState.Berhenti);
            DotDb.Fill = pakaiDb ? Dot(db) : Dot(ServiceState.Berhenti);
            TxtWeb.Text = webName + " " + Lang.T(pakaiWeb
                ? web.ToString().ToLowerInvariant() : "tidak dipakai");
            TxtDb.Text = Lang.T("MySQL") + " " + Lang.T(pakaiDb
                ? db.ToString().ToLowerInvariant() : "tidak dipakai");
            TxtProfil.Text = _engine.Active != null ? _engine.Active.Name : Lang.T("(belum ada profil)");
            // Di rail, keadaannya dibawa titik; kalimatnya ada di tooltip.
            DotWeb.ToolTip = TxtProfil.Text + Environment.NewLine + TxtWeb.Text;
            DotDb.ToolTip = TxtProfil.Text + Environment.NewLine + TxtDb.Text;

            bool anyRunning = web == ServiceState.Jalan || db == ServiceState.Jalan;
            TxtPower.Text = Lang.T(anyRunning ? "Matikan semua" : "Nyalakan semua");
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

            if (Pengendali.Ini != null) Pengendali.Ini.SegarkanBaki();

            var dash = Host.Content as DashboardPage;
            if (dash != null) dash.RefreshState();
        }

        // Warnanya dipakai bersama dengan kartu di Beranda - lihat WarnaLayanan.
        static Brush Dot(ServiceState s) { return WarnaLayanan.Titik(s); }

        async void BtnPower_Click(object sender, RoutedEventArgs e)
        {
            await TogglePower();
        }

        /// <summary>Tutup Phoron sepenuhnya karena pemasang pembaruan akan berjalan.</summary>
        public void TutupUntukPembaruan()
        {
            Pengendali.Ini.Keluar();
        }

        void BtnKeluar_Click(object sender, RoutedEventArgs e)
        {
            if (Pengendali.Ini.AdaYangJalan()
                && !AppState.Ask(Lang.T("Masih ada layanan yang berjalan. Semuanya akan dimatikan.\n\nKeluar dari Phoron sekarang?"))) return;
            Pengendali.Ini.Keluar();
        }

        public System.Threading.Tasks.Task TogglePower()
        {
            return Pengendali.Ini.TogglePower();
        }

        /// <summary>Majukan jendela ke depan, dipanggil Pengendali saat diminta tampil.</summary>
        public void Majukan()
        {
            if (!IsVisible) Show();
            // Dipulihkan HANYA bila memang sedang dikecilkan: memaksa Normal
            // pada jendela yang sedang dimaksimalkan akan mengecilkannya, dan
            // pengguna kehilangan ukuran jendela yang ia pilih sendiri.
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            // Kedipan Topmost: jaring pengaman kalau Windows tetap menolak
            // memajukan jendela ini (lihat AllowSetForegroundWindow di
            // Program.cs). Tanpa ini jendelanya bisa muncul di belakang jendela
            // yang sedang aktif dan seolah-olah tidak terjadi apa-apa.
            if (!IsActive)
            {
                var semulaTopmost = Topmost;
                Topmost = true;
                Topmost = semulaTopmost;
            }
            Focus();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            // Jendela TIDAK lagi disembunyikan saat ditutup - ia dibuang, dan
            // Pengendali yang memutuskan apakah itu berarti "ke baki" atau
            // keluar (lihat Pengendali.JendelaTertutup).
            //
            // Halaman dilepas di sini, selagi jendelanya masih utuh, supaya
            // Unloaded-nya pasti menyala: halaman Beranda, Basis data, dan Node
            // melanggan peristiwa Engine dan baru melepasnya di Unloaded.
            // Pelanggan yang tertinggal membuat Engine menahan halaman itu -
            // dan seluruh jendela di atasnya - tetap hidup.
            //
            // Alasan KEDUA, sama pentingnya: kotak teks di Profil, Ekstensi,
            // Situs, Versi, dan Pengaturan baru menyimpan isinya saat LostFocus.
            // Menekan X tidak memindahkan fokus, jadi isian yang sedang diketik
            // dulu selamat hanya karena jendelanya disembunyikan. Melepas halaman
            // mengeluarkan kotak yang sedang fokus dari pohon visual, dan WPF
            // memicu LostFocus-nya saat itu juga - isiannya tersimpan sebelum
            // jendela dibuang. Dibuktikan lewat UI Automation pada 1.38.0:
            // TxtDbPengguna, TxtCatatan, dan TxtPortMysql tersimpan sesudah X.
            Host.Content = null;
        }
    }
}
