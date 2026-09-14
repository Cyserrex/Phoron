using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class AboutPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public AboutPage()
        {
            InitializeComponent();
            Isi();
        }

        void Isi()
        {
            TxtVersi.Text = Lang.T("Versi") + " " + AppInfo.Version;
            TxtHakCipta.Text = AppInfo.HakCipta;
            PasangLogo();
            PasangRincian();
        }

        void PasangLogo()
        {
            try
            {
                // Autostart.ExePath membaca MainModule proses, bukan assembly
                // masuk - jadi tetap menunjuk exe yang benar walau halaman ini
                // dimuat dari proses lain (mis. saat diuji).
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(Autostart.ExePath))
                {
                    if (ico == null) return;
                    Logo.Source = Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch
            {
                // Ikon cuma hiasan. Gagal mengambilnya tidak boleh membuat
                // seluruh halaman Tentang ikut gagal terbuka.
                Logo.Visibility = Visibility.Collapsed;
            }
        }

        void PasangRincian()
        {
            PanelRinci.Children.Clear();
            Baris(Lang.T("Profil aktif"), _e.Active != null ? _e.Active.Name : Lang.T("(belum ada profil)"));
            Baris("PHP", Paket(_e.Php));
            Baris("Apache / Nginx", Paket(_e.WebPackage));
            Baris("MySQL / MariaDB", Paket(_e.MySql));
            Baris(Lang.T("Folder Phoron"), Paths.Root);
            Baris(".NET", Environment.Version.ToString());
            Baris("Windows", Environment.OSVersion.Version.ToString()
                             + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"));
            Baris(Lang.T("Status hak akses"),
                  HostsFile.IsAdmin() ? "Administrator" : Lang.T("pengguna biasa"));
        }

        static string Paket(BinPackage p)
        {
            return p == null ? "-" : p.Label;
        }

        void Baris(string kiri, string kanan)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var a = new TextBlock { Text = kiri, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
            var b = new TextBlock { Text = kanan ?? "-", TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(b, 1);
            grid.Children.Add(a);
            grid.Children.Add(b);
            PanelRinci.Children.Add(grid);
        }

        // ------------------------------------------------------------------ Aksi

        void BtnRepo_Click(object sender, RoutedEventArgs e) { Shell.Open(AppInfo.Repo); }

        void BtnRilis_Click(object sender, RoutedEventArgs e) { Shell.Open(AppInfo.HalamanRilis); }

        async void BtnCek_Click(object sender, RoutedEventArgs e)
        {
            var tombol = sender as Wpf.Ui.Controls.Button;
            if (tombol != null) tombol.IsEnabled = false;
            try
            {
                // Dipaksa: tombol ini ditekan justru ketika orang ingin tahu
                // SEKARANG, jadi jeda enam jam tidak berlaku di sini.
                var hasil = await _e.CekPembaruanAsync(true);
                if (hasil != null && hasil.Galat == null && hasil.LebihBaru)
                    UpdateDialog.Tawarkan(_e, hasil);
                else if (hasil != null && hasil.Galat != null) AppState.Warn(hasil.Galat);
                else AppState.Info("Phoron " + AppInfo.Version + " sudah versi terbaru.");
            }
            finally { if (tombol != null) tombol.IsEnabled = true; }
        }

        void BtnSalin_Click(object sender, RoutedEventArgs e)
        {
            // Melaporkan masalah selalu dimulai dari "versi berapa, PHP apa".
            // Menyediakannya sebagai satu blok siap tempel menghemat satu
            // putaran tanya-jawab.
            var sb = new StringBuilder();
            sb.AppendLine("Phoron " + AppInfo.Version);
            sb.AppendLine(AppInfo.HakCipta);
            sb.AppendLine("Profil : " + (_e.Active != null ? _e.Active.Name : "-"));
            sb.AppendLine("PHP    : " + Paket(_e.Php));
            sb.AppendLine("Web    : " + Paket(_e.WebPackage));
            sb.AppendLine("MySQL  : " + Paket(_e.MySql));
            sb.AppendLine("Windows: " + Environment.OSVersion.Version
                          + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"));
            sb.AppendLine(".NET   : " + Environment.Version);
            sb.AppendLine("Admin  : " + (HostsFile.IsAdmin() ? "ya" : "tidak"));
            try
            {
                Clipboard.SetText(sb.ToString());
                AppState.Info(Lang.T("Info versi sudah disalin ke papan klip."));
            }
            catch (Exception ex) { AppState.Warn("Gagal menyalin: " + ex.Message); }
        }
    }
}
