using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class VersionsPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public class Baris
        {
            public string Jenis { get; set; }
            public string Versi { get; set; }
            public string Toolset { get; set; }
            public string Arsitektur { get; set; }
            public string Folder { get; set; }
            public string Sumber { get; set; }
            public BinPackage Pkg;
        }

        /// <summary>Menahan penangan saat kotak diisi program, bukan oleh pengguna.</summary>
        bool _mengisi;

        public VersionsPage()
        {
            InitializeComponent();
            IsiRoots();
            IsiDaftar();
            IsiPaket();
        }

        void IsiRoots()
        {
            _mengisi = true;
            TxtRoots.Text = string.Join(Environment.NewLine, _e.Settings.BinRoots);
            _mengisi = false;
        }

        void TxtRoots_Lepas(object sender, RoutedEventArgs e)
        {
            if (_mengisi) return;
            var s = _e.Settings;
            var sebelum = string.Join(";", s.BinRoots);
            s.BinRoots = (TxtRoots.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
            // Daftar kosong berarti "kembali ke bawaan", bukan "jangan pindai apa
            // pun" - tanpa ini Phoron kehilangan seluruh versinya begitu kotaknya
            // tak sengaja dikosongkan.
            if (s.BinRoots.Count == 0) s.BinRoots = Settings.DefaultBinRoots();
            s.Save();

            if (string.Join(";", s.BinRoots) == sebelum) return;
            _e.Reload();
            AppState.RaiseChanged();
            IsiRoots();
            IsiDaftar();
        }

        void IsiDaftar()
        {
            Daftar.ItemsSource = _e.Packages.Select(p => new Baris
            {
                Jenis = p.Kind.ToString(),
                Versi = p.Version,
                Toolset = p.Compiler + (p.Kind == BinKind.Php ? (p.ThreadSafe ? " TS" : " NTS") : ""),
                Arsitektur = p.Arch,
                Folder = p.Id,
                Sumber = p.SourceRoot,
                Pkg = p,
            }).ToList();
        }

        void BtnPindai_Click(object sender, RoutedEventArgs e)
        {
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar();
            _e.Say("Pindai ulang selesai: " + _e.Packages.Count + " paket ditemukan.");
        }

        void BtnTambahRoot_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Pilih folder bin tambahan (mis. C:\\xampp atau bin milik Laragon)";
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                if (_e.Settings.BinRoots.Any(r => string.Equals(r, dlg.SelectedPath, StringComparison.OrdinalIgnoreCase)))
                { AppState.Info("Folder itu sudah terdaftar."); return; }
                _e.Settings.BinRoots.Add(dlg.SelectedPath);
                _e.Settings.Save();
                _e.Reload();
                AppState.RaiseChanged();
                IsiRoots();
                IsiDaftar();
            }
        }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            var b = Daftar.SelectedItem as Baris;
            Shell.Open(b != null ? b.Pkg.Path : Paths.Bin);
        }

        void BtnKatalog_Click(object sender, RoutedEventArgs e)
        {
            Downloader.Catalog();   // membuat berkasnya bila belum ada
            Shell.Open(Path.Combine(Paths.Etc, "catalog.ini"));
        }

        // -------------------------------------------------------------- Unduhan

        async void IsiPaket()
        {
            var item = CmbSumber.SelectedItem as ComboBoxItem;
            var tag = item != null ? (item.Tag ?? "").ToString() : "php";
            CmbPaket.ItemsSource = null;
            if (tag == "katalog")
            {
                CmbPaket.ItemsSource = Downloader.Catalog();
                CmbPaket.DisplayMemberPath = "Name";
                CmbPaket.SelectedIndex = 0;
                return;
            }

            TxtStatus.Text = "Mengambil daftar versi PHP...";
            try
            {
                var list = await Downloader.ListPhpAsync();
                CmbPaket.ItemsSource = list;
                CmbPaket.DisplayMemberPath = "Name";
                CmbPaket.SelectedIndex = 0;
                TxtStatus.Text = list.Count + " arsip PHP tersedia di windows.php.net.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message + " (perlu koneksi internet)";
            }
        }

        void CmbSumber_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            IsiPaket();
        }

        async void BtnPasang_Click(object sender, RoutedEventArgs e)
        {
            var pkg = CmbPaket.SelectedItem as RemotePackage;
            if (pkg == null) { AppState.Warn("Pilih dulu paket yang mau dipasang."); return; }

            BtnPasang.IsEnabled = false;
            Bar.Visibility = Visibility.Visible;
            Bar.Value = 0;
            TxtStatus.Text = "Mengunduh " + pkg.Name + "...";
            var progress = new Progress<int>(v => Bar.Value = v);
            try
            {
                var err = await Downloader.InstallAsync(pkg, progress, CancellationToken.None);
                if (err != null) { TxtStatus.Text = err; AppState.Warn(err); return; }
                TxtStatus.Text = pkg.Name + " terpasang.";
                _e.Say("Paket " + pkg.Name + " dipasang ke folder bin Phoron.");
                _e.Reload();
                AppState.RaiseChanged();
                IsiRoots();
                IsiDaftar();
            }
            finally
            {
                BtnPasang.IsEnabled = true;
                Bar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
