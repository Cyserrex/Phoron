using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class SitesPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public class Baris
        {
            public string Alamat { get; set; }
            public string Folder { get; set; }
            public string DocRoot { get; set; }
            public string Hosts { get; set; }
            public string Vhost { get; set; }
            public Site Situs;
        }

        public SitesPage()
        {
            InitializeComponent();
            Isi();
        }

        void Isi()
        {
            _e.RefreshSites();
            var root = SiteScanner.DocumentRoot(_e.Active);
            TxtInfo.Text = "Tiap subfolder di " + root + " otomatis dapat alamat sendiri. "
                         + "Klik ganda untuk membuka di browser.";
            Daftar.ItemsSource = _e.Sites.Select(s => new Baris
            {
                Alamat = _e.SiteUrl(s),
                Folder = s.Folder,
                // DocumentRoot ditampilkan relatif supaya kolomnya terbaca; yang
                // penting terlihat adalah apakah Phoron memilih subfolder public/.
                DocRoot = s.DocRoot != null && s.DocRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? s.DocRoot.Substring(root.Length).TrimStart('\\')
                    : s.DocRoot,
                Hosts = s.InHosts ? "ada" : "-",
                Vhost = s.HasVhost ? "ada" : "-",
                Situs = s,
            }).ToList();
        }

        Site Terpilih()
        {
            var b = Daftar.SelectedItem as Baris;
            return b != null ? b.Situs : null;
        }

        void Daftar_DoubleClick(object sender, RoutedEventArgs e) { BtnBuka_Click(sender, e); }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            if (s == null) return;
            if (_e.Services.WebState != ServiceState.Jalan
                && !AppState.Ask("Web server belum jalan, jadi halamannya kemungkinan besar tidak terbuka. "
                                 + "Tetap buka?")) return;
            Shell.Open(_e.SiteUrl(s));
        }

        void BtnFolder_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            Shell.Open(s != null ? s.Path : SiteScanner.DocumentRoot(_e.Active));
        }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            Shell.OpenTerminal(_e.Settings.Terminal,
                s != null ? s.Path : SiteScanner.DocumentRoot(_e.Active), _e.ToolEnv());
        }

        void BtnSegarkan_Click(object sender, RoutedEventArgs e)
        {
            var warnings = _e.Apply();
            Isi();
            if (warnings.Any(w => w.Contains("Administrator")))
            {
                if (Program.RestartAsAdmin("Berkas hosts hanya bisa disunting dengan hak Administrator.")) return;
            }
            AppState.ShowWarnings(warnings);
        }

        void BtnHosts_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.HostsFile); }

        void BtnBuat_Click(object sender, RoutedEventArgs e)
        {
            var nama = (TxtSitusBaru.Text ?? "").Trim();
            if (nama.Length == 0) { AppState.Warn("Isi dulu nama proyeknya."); return; }
            if (nama.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { AppState.Warn("Nama folder mengandung karakter yang tidak boleh dipakai."); return; }

            var dir = Path.Combine(SiteScanner.DocumentRoot(_e.Active), nama);
            if (Directory.Exists(dir)) { AppState.Warn("Folder itu sudah ada."); return; }
            try
            {
                Directory.CreateDirectory(dir);
                var host = SiteScanner.SafeHost(nama) + "."
                           + (_e.Active != null ? _e.Active.SiteSuffix : "test");
                File.WriteAllText(Path.Combine(dir, "index.php"),
                    "<?php\n"
                    + "// Dibuat oleh Phoron.\n"
                    + "echo '<h1>" + nama + "</h1>';\n"
                    + "echo '<p>PHP ' . PHP_VERSION . ' lewat ' . php_sapi_name() . '</p>';\n",
                    new UTF8Encoding(false));
                TxtSitusBaru.Text = "";
                _e.Apply();
                Isi();
                _e.Say("Situs " + host + " dibuat.");
                AppState.Info("Situs siap di " + host + ".");
            }
            catch (Exception ex) { AppState.Warn("Gagal membuat situs: " + ex.Message); }
        }
    }
}
