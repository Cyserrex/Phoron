using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Panel kontrol ringkas: tampilan utama Phoron.
    ///
    /// Jendela lengkap (rail dan semua halaman) berukuran hampir selayar, padahal
    /// pekerjaan sehari-hari cuma tiga: memastikan PHP jalan, menjalankan proyek
    /// Node, membuka situs. Ketiganya muat di sini. Halaman lengkap dibuka dari
    /// laci, langsung di tab yang dituju.
    ///
    /// Sama seperti jendela lengkap, panel ini dibuang saat ditutup ke baki;
    /// langganannya dipasang di Loaded dan dilepas di Unloaded.
    /// </summary>
    public partial class PanelRingkas
    {
        readonly Engine _e;
        bool _memuat;
        int _tertunda;

        public PanelRingkas(Engine engine, bool laciTerbuka)
        {
            _e = engine;
            InitializeComponent();
            MainWindow.PasangIkon(this, BilahJudul);
            MainWindow.TerapkanTema(_e.Settings.Tema);
            AturLaci(laciTerbuka);

            Loaded += (s, ev) =>
            {
                Lepas();
                _e.Log += OnLog;
                _e.Node.Output += OnNode;
                _e.Node.StateChanged += OnNodeState;
                _e.Node.UrlFound += OnNode;
                Segarkan();
            };
            Unloaded += (s, ev) => Lepas();
            Closing += (s, ev) => Lepas();
            IsiProfil();
            Segarkan();
        }

        /// <summary>Apakah laci sedang terbuka - diingat Pengendali untuk panel berikutnya.</summary>
        public bool LaciTerbuka { get { return Laci.Visibility == Visibility.Visible; } }

        void Lepas()
        {
            _e.Log -= OnLog;
            _e.Node.Output -= OnNode;
            _e.Node.StateChanged -= OnNodeState;
            _e.Node.UrlFound -= OnNode;
        }

        void OnLog(string teks) { MintaSegarkan(); }
        void OnNode(string folder, string baris) { MintaSegarkan(); }
        void OnNodeState(string folder, bool jalan) { MintaSegarkan(); }

        /// <summary>
        /// Peristiwa datang dari utas layanan dan pembaca keluaran, sering
        /// beruntun. Permintaan yang datang selagi satu penggambaran mengantre
        /// ikut menumpang padanya, pada prioritas Background.
        /// </summary>
        void MintaSegarkan()
        {
            if (System.Threading.Interlocked.Exchange(ref _tertunda, 1) == 1) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                System.Threading.Interlocked.Exchange(ref _tertunda, 0);
                if (IsLoaded) Segarkan();
            }));
        }

        // ------------------------------------------------------------ Profil

        public void IsiProfil()
        {
            _memuat = true;
            CmbProfil.ItemsSource = _e.Profiles;
            CmbProfil.DisplayMemberPath = "Name";
            CmbProfil.SelectedItem = _e.Active;
            _memuat = false;
            BtnGanti.Visibility = Visibility.Collapsed;
        }

        void CmbProfil_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_memuat) return;
            BtnGanti.Visibility = CmbProfil.SelectedItem != null && !ReferenceEquals(CmbProfil.SelectedItem, _e.Active)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        async void BtnGanti_Click(object sender, RoutedEventArgs e)
        {
            var p = CmbProfil.SelectedItem as Profile;
            if (p == null) return;
            BtnGanti.IsEnabled = false;
            try
            {
                await _e.SwitchAsync(p);
                AppState.RaiseChanged();
            }
            finally
            {
                BtnGanti.IsEnabled = true;
                IsiProfil();
                if (Pengendali.Ini != null) Pengendali.Ini.SegarkanStatus();
            }
        }

        // -------------------------------------------------------- Keadaan

        /// <summary>Gambar ulang baris, tombol daya, laci, dan bilah status.</summary>
        public void Segarkan()
        {
            IsiBaris();

            var web = _e.Services.WebState;
            var db = _e.Services.DbState;
            bool jalan = web == ServiceState.Jalan || db == ServiceState.Jalan;
            bool sibuk = web == ServiceState.Menyalakan || web == ServiceState.Mematikan
                      || db == ServiceState.Menyalakan || db == ServiceState.Mematikan;
            BtnDaya.Content = Lang.T(jalan ? "Matikan semua" : "Nyalakan semua");
            BtnDaya.Appearance = jalan ? Wpf.Ui.Controls.ControlAppearance.Secondary
                                       : Wpf.Ui.Controls.ControlAppearance.Primary;
            BtnDaya.IsEnabled = !sibuk && _e.Active != null;

            var bagian = new List<string>();
            if (_e.Php != null) bagian.Add("PHP " + _e.Php.Version);
            if (_e.Sites != null) bagian.Add(Lang.T("{0} situs", _e.Sites.Count));
            int lain = _e.Active != null && _e.Active.PhpPerSitus != null ? _e.Active.PhpPerSitus.Count : 0;
            if (lain > 0) bagian.Add(Lang.T("{0} memakai PHP lain", lain));
            TxtStatus.Text = string.Join("  ·  ", bagian);

            if (LaciTerbuka) IsiLog();
        }

        void IsiBaris()
        {
            DaftarBaris.Children.Clear();
            var p = _e.Active;
            if (p == null)
            {
                DaftarBaris.Children.Add(new TextBlock { Text = Lang.T("Belum ada profil"), Margin = new Thickness(12, 10, 12, 10), Opacity = 0.8 });
                return;
            }

            if (p.PakaiWeb)
            {
                var paket = p.WebServer == "nginx" ? _e.Nginx : _e.Apache;
                var keadaan = _e.Services.WebState;
                DaftarBaris.Children.Add(Baris(
                    p.WebServer == "nginx" ? "Nginx" : "Apache", paket != null ? paket.Version : "-",
                    keadaan, ":" + p.HttpPort,
                    TombolLayanan(keadaan, () => _e.StartWebAsync(), () => _e.StopWebAsync())));
            }

            var kDb = p.PakaiMySql ? _e.Services.DbState : ServiceState.Berhenti;
            DaftarBaris.Children.Add(Baris(
                "MySQL", _e.MySql != null ? _e.MySql.Version : "-",
                kDb, p.PakaiMySql ? ":" + p.MySqlPort : "",
                p.PakaiMySql ? TombolLayanan(kDb, () => _e.StartDbAsync(), () => _e.StopDbAsync()) : null,
                p.PakaiMySql ? null : WarnaLayanan.Besar(Lang.T("tidak dipakai"))));

            foreach (var a in (_e.NodeAppsList ?? new List<NodeApp>()).Take(4))
            {
                var app = a;
                bool jalan = _e.Node.IsRunning(app.Path);
                var url = jalan ? _e.Node.UrlOf(app.Path) : null;
                var tombol = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                if (!string.IsNullOrEmpty(url))
                {
                    var buka = Tombol(Lang.T("Buka"), true);
                    buka.Click += (s, e) => Shell.Open(url);
                    buka.Margin = new Thickness(0, 0, 4, 0);
                    tombol.Children.Add(buka);
                }
                var aksi = Tombol(Lang.T(jalan ? "Hentikan" : "Jalankan"), !jalan);
                aksi.Click += (s, e) =>
                {
                    if (_e.Node.IsRunning(app.Path)) { _e.Node.Stop(app.Path); return; }
                    if (!PackageJson.HasNodeModules(app.Path)
                        && !AppState.Ask(Lang.T("Folder node_modules belum ada di proyek ini, jadi perintahnya kemungkinan besar gagal.")
                                         + "\n\n" + Lang.T("Jalankan juga?"))) return;
                    var err = _e.JalankanNode(app);
                    if (err != null) AppState.Warn(err);
                };
                tombol.Children.Add(aksi);
                DaftarBaris.Children.Add(Baris(app.DisplayName, "Node", jalan ? ServiceState.Jalan : ServiceState.Berhenti,
                                               Port(url), tombol));
            }

            // Garis pemisah: semua baris kecuali yang terakhir.
            for (int i = 0; i < DaftarBaris.Children.Count - 1; i++)
            {
                var b = DaftarBaris.Children[i] as Border;
                if (b != null) b.BorderThickness = new Thickness(0, 0, 0, 1);
            }
        }

        static string Port(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try { return ":" + new Uri(url).Port; } catch { return ""; }
        }

        FrameworkElement TombolLayanan(ServiceState keadaan, Func<System.Threading.Tasks.Task> nyalakan,
                                       Func<System.Threading.Tasks.Task> matikan)
        {
            bool hidup = keadaan == ServiceState.Jalan || keadaan == ServiceState.Menyalakan;
            var b = Tombol(Lang.T(hidup ? "Hentikan" : "Nyalakan"), false);
            b.IsEnabled = keadaan != ServiceState.Menyalakan && keadaan != ServiceState.Mematikan;
            b.Click += async (s, e) =>
            {
                b.IsEnabled = false;
                try { await (hidup ? matikan() : nyalakan()); }
                finally { if (Pengendali.Ini != null) Pengendali.Ini.SegarkanStatus(); }
            };
            return b;
        }

        static Wpf.Ui.Controls.Button Tombol(string teks, bool utama)
        {
            return new Wpf.Ui.Controls.Button
            {
                Content = teks, Padding = new Thickness(10, 3, 10, 4), FontSize = 12,
                Appearance = utama ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary,
            };
        }

        Border Baris(string nama, string versi, ServiceState keadaan, string port, FrameworkElement aksi, string teksKeadaan = null)
        {
            var g = new Grid { Height = 40, Margin = new Thickness(12, 0, 10, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 });

            var judul = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            judul.Inlines.Add(new System.Windows.Documents.Run(nama) { FontWeight = FontWeights.SemiBold });
            judul.Inlines.Add(new System.Windows.Documents.Run("  " + versi) { Foreground = Brushes.Gray });
            g.Children.Add(judul);

            var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = WarnaLayanan.Titik(keadaan), VerticalAlignment = VerticalAlignment.Center });
            status.Children.Add(new TextBlock { Text = teksKeadaan ?? WarnaLayanan.Teks(keadaan), Margin = new Thickness(6, 0, 0, 0), FontSize = 12 });
            Grid.SetColumn(status, 1);
            g.Children.Add(status);

            var tp = new TextBlock { Text = port, Opacity = 0.7, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tp, 2);
            g.Children.Add(tp);

            if (aksi != null)
            {
                aksi.VerticalAlignment = VerticalAlignment.Center;
                aksi.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(aksi, 3);
                g.Children.Add(aksi);
            }
            return new Border
            {
                Child = g,
                BorderBrush = (Brush)FindResource("ControlStrokeColorDefaultBrush"),
                ToolTip = null,
            };
        }

        // ----------------------------------------------------------- Laci

        void AturLaci(bool buka)
        {
            Laci.Visibility = buka ? Visibility.Visible : Visibility.Collapsed;
            TxtLaci.Text = Lang.T(buka ? "Tutup laci" : "Menu lengkap");
            IkonLaci.Text = buka ? "" : "";
            if (buka) IsiLog();
        }

        void BtnLaci_Click(object sender, RoutedEventArgs e) { AturLaci(!LaciTerbuka); }

        void IsiLog()
        {
            var baris = _e.Riwayat();
            var sb = new StringBuilder();
            foreach (var b in baris.Skip(Math.Max(0, baris.Count - 4)))
            {
                if (sb.Length > 0) sb.Append(Environment.NewLine);
                sb.Append(b.Waktu.ToString("HH.mm.ss")).Append("  ").Append((b.Teks ?? "").Replace(Environment.NewLine, " "));
            }
            TxtLog.Text = sb.ToString();
        }

        void BtnHalaman_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement) != null ? ((FrameworkElement)sender).Tag as string : null;
            if (Pengendali.Ini != null) Pengendali.Ini.TampilkanLengkap(tag ?? "beranda");
        }

        // -------------------------------------------------------- Pintasan

        void BtnLocalhost_Click(object sender, RoutedEventArgs e) { Shell.Open(_e.RootUrl()); }

        void BtnDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (Pengendali.Ini != null) Pengendali.Ini.TampilkanLengkap("basisdata");
        }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            Shell.OpenTerminal(_e.Settings.Terminal, SiteScanner.DocumentRoot(_e.Active), _e.ToolEnv());
        }

        void BtnFolder_Click(object sender, RoutedEventArgs e) { Shell.Open(SiteScanner.DocumentRoot(_e.Active)); }

        async void BtnDaya_Click(object sender, RoutedEventArgs e)
        {
            BtnDaya.IsEnabled = false;
            try { if (Pengendali.Ini != null) await Pengendali.Ini.TogglePower(); }
            finally { Segarkan(); }
        }

        /// <summary>Majukan jendela ke depan, sama seperti jendela lengkap.</summary>
        public void Majukan()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (!IsActive) { var t = Topmost; Topmost = true; Topmost = t; }
            Focus();
        }
    }
}
