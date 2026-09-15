using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class SitesPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public class Baris
        {
            public string Alamat { get; set; }
            public string Folder { get; set; }
            public string Root { get; set; }
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
            var roots = SiteScanner.Roots(_e.Active);
            TxtInfo.Text = roots.Count == 1
                ? "Tiap subfolder di " + roots[0] + " otomatis dapat alamat sendiri. "
                  + "Klik ganda untuk membuka di browser."
                : "Memindai " + roots.Count + " folder proyek: " + string.Join(", ", roots)
                  + ". Klik ganda untuk membuka di browser.";

            LblFolderProyek.Text = _e.Active != null
                ? "Folder proyek profil \"" + _e.Active.Name + "\""
                : "Folder proyek";
            _mengisi = true;
            TxtDocRoot.Text = _e.Active != null
                ? string.Join(Environment.NewLine, _e.Active.ProjectRoots)
                : "";
            _mengisi = false;

            CmbRootBaru.ItemsSource = roots;
            CmbRootBaru.SelectedIndex = 0;
            CmbRootBaru.Visibility = roots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            Daftar.ItemsSource = _e.Sites.Select(s => new Baris
            {
                Alamat = _e.SiteUrl(s),
                Folder = s.Folder,
                Root = s.Root,
                // Ditampilkan relatif terhadap folder situsnya sendiri; yang perlu
                // terlihat di kolom ini cuma apakah Phoron memilih subfolder public/.
                DocRoot = s.DocRoot != null && s.Path != null
                          && s.DocRoot.Length > s.Path.Length
                          && s.DocRoot.StartsWith(s.Path, StringComparison.OrdinalIgnoreCase)
                    ? s.DocRoot.Substring(s.Path.Length).TrimStart('\\')
                    : "(akar folder)",
                Hosts = s.InHosts ? "ada" : "-",
                Vhost = s.HasVhost ? "ada" : "-",
                Situs = s,
            }).ToList();

            TampilkanInfo();
        }

        /// <summary>
        /// Keterangan tentang daftar situs, ditempel di halaman - bukan
        /// dimunculkan sebagai kotak dialog yang menghalangi.
        /// </summary>
        void TampilkanInfo()
        {
            var pesan = new List<string>(_e.SiteWarnings ?? new List<string>());
            PanelInfo.Visibility = pesan.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (pesan.Count == 0) return;
            LblInfo.Text = pesan.Count == 1
                ? "1 catatan tentang daftar situs"
                : pesan.Count + " catatan tentang daftar situs";
            TxtInfoSitus.Text = string.Join(Environment.NewLine + Environment.NewLine, pesan.ToArray());
        }

        Site Terpilih()
        {
            var b = Daftar.SelectedItem as Baris;
            return b != null ? b.Situs : null;
        }

        /// <summary>Menahan penangan saat kotak diisi program, bukan oleh pengguna.</summary>
        bool _mengisi;

        /// <summary>
        /// Folder proyek adalah setelan PROFIL, bukan setelan global - jadi yang
        /// disunting di sini adalah profil yang sedang aktif, dan labelnya
        /// menyebut namanya supaya itu tidak jadi kejutan.
        /// </summary>
        void TxtDocRoot_Lepas(object sender, RoutedEventArgs e)
        {
            if (_mengisi || _e.Active == null) return;
            var sebelum = string.Join(";", _e.Active.ProjectRoots);
            _e.Active.ProjectRoots = (TxtDocRoot.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimEnd('\\')).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.Join(";", _e.Active.ProjectRoots) == sebelum) return;

            ProfileStore.Save(_e.Active);
            // Daftar situs di bawah langsung ikut berubah - itulah gunanya kotak
            // ini berada di halaman yang sama.
            foreach (var w in _e.Apply()) _e.Say(w);
            AppState.RaiseChanged();
            Isi();

            var hilang = _e.Active.ProjectRoots
                .Where(r => !System.IO.Directory.Exists(r)).ToList();
            TxtStatusRoot.Text = hilang.Count > 0
                ? "Tersimpan, tapi folder ini belum ada: " + string.Join(", ", hilang.ToArray())
                : "Tersimpan " + DateTime.Now.ToString("HH:mm:ss") + ".";
        }

        void BtnPilihFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_e.Active == null) return;
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Pilih folder proyek untuk ditambahkan ke profil ini";
                dlg.SelectedPath = Paths.Www;
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                // Ditambahkan sebagai baris baru, bukan menimpa: tombol ini ada
                // justru untuk menyusun daftar berisi beberapa folder.
                var ada = (TxtDocRoot.Text ?? "").TrimEnd();
                if (ada.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Any(x => string.Equals(x.Trim().TrimEnd('\\'),
                                               dlg.SelectedPath.TrimEnd('\\'),
                                               StringComparison.OrdinalIgnoreCase)))
                {
                    AppState.Info("Folder itu sudah ada di daftar.");
                    return;
                }
                TxtDocRoot.Text = ada.Length == 0
                    ? dlg.SelectedPath
                    : ada + Environment.NewLine + dlg.SelectedPath;
                TxtDocRoot_Lepas(sender, e);
            }
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
            TampilkanInfo();
        }

        void BtnHosts_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.HostsFile); }

        void BtnBuat_Click(object sender, RoutedEventArgs e)
        {
            var nama = InputDialog.Tanya(Window.GetWindow(this), "Buat situs",
                "Nama folder proyek baru. Phoron membuat foldernya berikut index.php "
                + "contoh, lalu mendaftarkan alamatnya.",
                "Akan dibuat di " + (CmbRootBaru.SelectedItem as string
                                     ?? SiteScanner.DocumentRoot(_e.Active)));
            if (nama == null) return;
            if (nama.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { AppState.Warn("Nama folder mengandung karakter yang tidak boleh dipakai."); return; }

            var root = CmbRootBaru.SelectedItem as string ?? SiteScanner.DocumentRoot(_e.Active);
            var dir = Path.Combine(root, nama);
            if (Directory.Exists(dir)) { AppState.Warn("Folder itu sudah ada."); return; }
            try
            {
                Directory.CreateDirectory(dir);
                var host = SiteScanner.SafeHost(nama) + "." + SiteScanner.Suffix(_e.Active);
                File.WriteAllText(Path.Combine(dir, "index.php"),
                    "<?php\n"
                    + "// Dibuat oleh Phoron.\n"
                    + "echo '<h1>" + nama + "</h1>';\n"
                    + "echo '<p>PHP ' . PHP_VERSION . ' lewat ' . php_sapi_name() . '</p>';\n",
                    new UTF8Encoding(false));
                _e.Apply();
                Isi();
                _e.Say("Situs " + host + " dibuat.");
                AppState.Info("Situs siap di " + host + ".");
            }
            catch (Exception ex) { AppState.Warn("Gagal membuat situs: " + ex.Message); }
        }
    }
}
