using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class DashboardPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        readonly Queue<string> _lines = new Queue<string>();
        bool _loading;

        public DashboardPage()
        {
            InitializeComponent();
            _e.Log += OnLog;
            Unloaded += (s, ev) => _e.Log -= OnLog;
            LoadProfiles();
            RefreshState();
        }

        void OnLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                // Hanya 200 baris terakhir yang disimpan; kotak ini untuk melihat
                // sekilas, riwayat lengkapnya ada di halaman Log.
                _lines.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
                while (_lines.Count > 200) _lines.Dequeue();
                TxtLog.Text = string.Join(Environment.NewLine, _lines);
                TxtLog.ScrollToEnd();
            });
        }

        void LoadProfiles()
        {
            _loading = true;
            CmbProfil.ItemsSource = _e.Profiles;
            CmbProfil.DisplayMemberPath = "Name";
            CmbProfil.SelectedItem = _e.Active;
            _loading = false;
        }

        public void RefreshState()
        {
            var php = _e.Php;
            var web = _e.WebPackage;
            var db = _e.MySql;
            var p = _e.Active;

            TxtPhp.Text = php != null ? php.Version : "-";
            TxtPhpSub.Text = php != null ? php.Label : "belum dipilih di profil";
            TxtWebKind.Text = p != null && p.WebServer == "nginx" ? "Nginx" : "Apache";
            TxtWeb.Text = web != null ? web.Version : "-";
            // Status HTTPS ikut ditulis: tanpa sertifikat, port 443 tidak dibuka
            // sama sekali, dan browser hanya menjawab "tidak dapat tersambung"
            // tanpa menyebutkan sebabnya di mana pun.
            var https = SslTool.Exists
                ? " · https " + (p != null ? p.HttpsPort.ToString() : "?")
                : " · https mati";
            TxtWebSub.Text = web != null
                ? web.Label + " · port " + (p != null ? p.HttpPort.ToString() : "?") + https
                : "belum dipilih di profil";
            TxtDb.Text = db != null ? db.Version : "-";
            TxtDbSub.Text = db != null
                ? db.Label + " · port " + (p != null ? p.MySqlPort.ToString() : "?")
                : "belum dipilih di profil";

            ShowConflicts();
        }

        void ShowConflicts()
        {
            var pesan = new List<string>();
            if (_e.Active == null) pesan.Add("Belum ada profil. Buat satu di halaman Profil.");
            else if (_e.Services.WebState != ServiceState.Jalan)
            {
                // Port dicek hanya saat layanan belum jalan - kalau sudah jalan,
                // yang memegang port itu justru kita sendiri.
                foreach (var u in PortCheck.Conflicts(_e.Active, false)) pesan.Add(u.Describe());
            }
            if (!SslTool.Exists)
                pesan.Add("HTTPS belum aktif: sertifikat belum ada, jadi port "
                          + (_e.Active != null ? _e.Active.HttpsPort.ToString() : "443")
                          + " tidak dibuka. Pakai http:// (bukan https://), atau tekan "
                          + "\"Buat sertifikat SSL\" di bawah.");
            if (_e.Settings.ManageHosts && !HostsFile.IsAdmin())
                pesan.Add("Phoron tidak jalan sebagai Administrator, jadi berkas hosts tidak bisa disunting. "
                          + "Nama situs .test belum tentu bisa dibuka.");

            TxtPeringatan.Text = string.Join(Environment.NewLine, pesan);
            PanelPeringatan.Visibility = pesan.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ Aksi

        void CmbProfil_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            BtnSwitch.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        }

        async void BtnSwitch_Click(object sender, RoutedEventArgs e)
        {
            var p = CmbProfil.SelectedItem as Profile;
            if (p == null) return;
            BtnSwitch.IsEnabled = false;
            try
            {
                var warnings = await _e.SwitchAsync(p);
                AppState.RaiseChanged();
                RefreshState();
                var main = Window.GetWindow(this) as MainWindow;
                if (main != null) main.RefreshStatus();
                AppState.ShowWarnings(warnings);
            }
            finally { BtnSwitch.IsEnabled = true; }
        }

        void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            AppState.ShowWarnings(_e.Apply());
            RefreshState();
        }

        void BtnWww_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open(SiteScanner.DocumentRoot(_e.Active));
        }

        void BtnLocalhost_Click(object sender, RoutedEventArgs e) { Shell.Open(_e.RootUrl()); }

        void BtnEtc_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.Etc); }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            Shell.OpenTerminal(_e.Settings.Terminal, SiteScanner.DocumentRoot(_e.Active), _e.ToolEnv());
        }

        /// <summary>
        /// Menulis phpinfo.php ke akar www lalu membukanya. Cara tercepat
        /// memastikan versi yang benar-benar dilayani Apache sama dengan yang
        /// tertulis di profil - bukan sekadar yang dikira Phoron.
        /// </summary>
        void BtnPhpInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = Path.Combine(SiteScanner.DocumentRoot(_e.Active), "phoron-phpinfo.php");
                File.WriteAllText(file, "<?php phpinfo();", new UTF8Encoding(false));
                Shell.Open(_e.RootUrl() + "phoron-phpinfo.php");
            }
            catch (Exception ex) { AppState.Warn("Gagal membuat phpinfo: " + ex.Message); }
        }

        void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var apache = _e.Apache;
            if (apache == null) { AppState.Warn("Profil ini belum menunjuk Apache."); return; }
            _e.Apply();
            var httpd = Path.Combine(apache.Path, "bin", "httpd.exe");
            var conf = Path.Combine(Paths.EtcApache, "httpd.conf");
            var res = Shell.Run(httpd, "-f \"" + conf + "\" -d \"" + apache.Path + "\" -t",
                                apache.Path, 30000, ServiceManager.EnvFor(_e.Php));
            AppState.Info(string.IsNullOrWhiteSpace(res.All) ? "Konfigurasi OK." : res.All,
                          res.Ok ? "Konfigurasi OK" : "Konfigurasi bermasalah");
        }

        void BtnSsl_Click(object sender, RoutedEventArgs e)
        {
            var apache = _e.Apache;
            if (apache == null) { AppState.Warn("Butuh paket Apache (openssl.exe ada di dalamnya)."); return; }
            var err = SslTool.Generate(apache, _e.Active != null ? _e.Active.SiteSuffix : "test");
            if (err != null) { AppState.Warn(err); return; }
            _e.Apply();
            _e.Say("Sertifikat SSL dibuat di etc\\ssl.");

            if (AppState.Ask("Sertifikat dibuat. Pasang ke Trusted Root Windows supaya browser "
                             + "tidak memberi peringatan?"))
            {
                var t = SslTool.Trust();
                if (t != null && t.Contains("Administrator"))
                {
                    Program.RestartAsAdmin("Memasang sertifikat ke Trusted Root butuh hak Administrator.");
                    return;
                }
                AppState.Info(t ?? "Sertifikat terpasang. Tutup dan buka ulang browser.");
            }
        }
    }
}
