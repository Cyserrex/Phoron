using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class ExtensionsPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        readonly List<CheckBox> _kotak = new List<CheckBox>();

        public ExtensionsPage()
        {
            InitializeComponent();
            Isi();
        }

        void Isi()
        {
            var php = _e.Php;
            var p = _e.Active;
            if (php == null || p == null)
            {
                TxtInfo.Text = "Profil aktif belum menunjuk versi PHP.";
                return;
            }
            TxtInfo.Text = "Ekstensi yang tersedia di " + php.Id + ". Centangan disimpan di profil \""
                         + p.Name + "\", jadi tiap profil bisa punya daftar sendiri.";

            var aktif = new HashSet<string>(p.PhpExtensions, StringComparer.OrdinalIgnoreCase);
            _kotak.Clear();
            foreach (var ext in ConfigWriter.AvailableExtensions(php))
            {
                var cb = new CheckBox
                {
                    // Teks dibungkus TextBlock, bukan diberikan sebagai string:
                    // ContentPresenter memperlakukan garis bawah sebagai penanda
                    // tombol akses, jadi "pdo_mysql" akan tampil "pdomysql".
                    Content = new TextBlock { Text = ext },
                    Tag = ext,
                    IsChecked = aktif.Contains(ext),
                    Width = 165,
                    Margin = new Thickness(0, 2, 8, 2),
                };
                _kotak.Add(cb);
            }
            DaftarExt.ItemsSource = _kotak;

            TxtMemory.Text = Nilai(p, "memory_limit");
            TxtUpload.Text = Nilai(p, "upload_max_filesize");
            TxtPost.Text = Nilai(p, "post_max_size");
            TxtExec.Text = Nilai(p, "max_execution_time");
            TxtTz.Text = Nilai(p, "date.timezone");
            var de = Nilai(p, "display_errors");
            ChkErrors.IsChecked = de.Length == 0 || de.Equals("On", StringComparison.OrdinalIgnoreCase);
            // Tanpa penimpaan di profil, yang berlaku adalah nilai dari php.ini
            // dasar - jadi kotaknya mencerminkan berkas itu, bukan menebak Off.
            var sot = Nilai(p, "short_open_tag");
            ChkShortTag.IsChecked = sot.Length > 0
                ? sot.Equals("On", StringComparison.OrdinalIgnoreCase)
                : PhpIniAktif("short_open_tag");
        }

        static string Nilai(Profile p, string key)
        {
            string v;
            return p.PhpIniOverrides.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>Nilai direktif menurut php.ini yang sedang berlaku, ditanyakan ke php.exe sendiri.</summary>
        bool PhpIniAktif(string key)
        {
            var php = _e.Php;
            if (php == null) return false;
            try
            {
                var res = Shell.Run(Path.Combine(php.Path, "php.exe"),
                    "-r \"echo ini_get('" + key + "') ? 1 : 0;\"", php.Path, 15000,
                    ServiceManager.EnvFor(php));
                return res.StdOut.Trim() == "1";
            }
            catch { return false; }
        }

        void TxtCari_Changed(object sender, TextChangedEventArgs e)
        {
            var q = (TxtCari.Text ?? "").Trim();
            foreach (var cb in _kotak)
                cb.Visibility = q.Length == 0
                    || (cb.Tag ?? "").ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Centang ulang mengikuti php.ini yang berlaku sebelum Phoron ikut
        /// campur. Pengambilalihan otomatis hanya terjadi pada profil yang belum
        /// punya daftar; profil yang sudah terlanjur berisi sebagian ekstensi
        /// butuh jalan sadar seperti ini.
        /// </summary>
        void BtnAmbil_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Php;
            if (php == null) { AppState.Warn("Profil belum menunjuk PHP."); return; }
            var dasar = ConfigWriter.EkstensiDariPhpIniDasar(php);
            if (dasar.Count == 0)
            {
                AppState.Info("php.ini asli di " + php.Id + " tidak mengaktifkan ekstensi apa pun.");
                return;
            }
            if (!AppState.Ask("php.ini asli mengaktifkan " + dasar.Count + " ekstensi:\n\n"
                              + string.Join(", ", dasar)
                              + "\n\nGanti centangan sekarang dengan daftar itu?")) return;

            var set = new HashSet<string>(dasar, StringComparer.OrdinalIgnoreCase);
            foreach (var cb in _kotak) cb.IsChecked = set.Contains((cb.Tag ?? "").ToString());

            var hilang = dasar.Where(d => !_kotak.Any(c =>
                string.Equals((c.Tag ?? "").ToString(), d, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hilang.Count > 0)
                AppState.Warn("Tidak ada DLL-nya di " + php.Id + ", jadi dilewati: "
                              + string.Join(", ", hilang));
            AppState.Info("Centangan disesuaikan. Tekan \"Simpan ke profil\" untuk menerapkannya.");
        }

        void BtnSimpan_Click(object sender, RoutedEventArgs e)
        {
            var p = _e.Active;
            if (p == null) return;
            p.PhpExtensions = _kotak.Where(c => c.IsChecked == true)
                                    .Select(c => (c.Tag ?? "").ToString()).ToList();

            Set(p, "memory_limit", TxtMemory.Text);
            Set(p, "upload_max_filesize", TxtUpload.Text);
            Set(p, "post_max_size", TxtPost.Text);
            Set(p, "max_execution_time", TxtExec.Text);
            Set(p, "date.timezone", TxtTz.Text);
            p.PhpIniOverrides["display_errors"] = ChkErrors.IsChecked == true ? "On" : "Off";
            p.PhpIniOverrides["short_open_tag"] = ChkShortTag.IsChecked == true ? "On" : "Off";

            ProfileStore.Save(p);
            AppState.ShowWarnings(_e.Apply());
            _e.Say("Ekstensi dan setelan php.ini profil \"" + p.Name + "\" disimpan.");

            if (_e.Services.WebState == ServiceState.Jalan
                && AppState.Ask("php.ini sudah ditulis ulang, tapi Apache membacanya hanya saat start. "
                                + "Nyalakan ulang web server sekarang?"))
            {
                var main = Window.GetWindow(this) as MainWindow;
                Restart(main);
            }
        }

        async void Restart(MainWindow main)
        {
            await _e.StopWebAsync();
            await _e.StartWebAsync();
            if (main != null) main.RefreshStatus();
        }

        static void Set(Profile p, string key, string value)
        {
            value = (value ?? "").Trim();
            // Kolom kosong berarti "pakai bawaan php.ini", bukan "setel ke kosong" -
            // menulis nilai kosong ke php.ini justru mematikan direktifnya.
            if (value.Length == 0) p.PhpIniOverrides.Remove(key);
            else p.PhpIniOverrides[key] = value;
        }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            if (_e.Php == null) return;
            // Jalurnya diambil dari hasil penulisan terakhir, bukan disusun ulang:
            // php.ini bisa berada di etc\ atau di dalam folder PHP tergantung
            // setelan, dan menebaknya di sini berarti tombol ini membuka berkas
            // yang bukan yang sedang dipakai.
            if (_e.LastBuild == null || _e.LastBuild.PhpIniDir == null) _e.Apply();
            var dir = _e.LastBuild != null ? _e.LastBuild.PhpIniDir : null;
            if (dir == null) { AppState.Warn("php.ini belum pernah ditulis."); return; }
            Shell.Open(Path.Combine(dir, "php.ini"));
        }

        void BtnUji_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Php;
            if (php == null) { AppState.Warn("Profil belum menunjuk PHP."); return; }
            _e.Apply();
            var res = Shell.Run(Path.Combine(php.Path, "php.exe"), "-m", php.Path, 30000,
                                ServiceManager.EnvFor(php));
            AppState.Info(res.All, "Modul yang benar-benar dimuat");
        }
    }
}
