using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class SettingsPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public SettingsPage()
        {
            InitializeComponent();
            Isi();
        }

        /// <summary>Menahan handler saat kotak diisi program, supaya tidak dianggap klik pengguna.</summary>
        bool _mengisi;

        void Isi()
        {
            _mengisi = true;
            var s = _e.Settings;
            SwWindows.IsChecked = Autostart.Aktif;
            var asing = Autostart.EntriAsing();
            TxtWindows.Text = asing == null
                ? "Phoron mulai langsung mengecil ke baki sistem, tanpa memunculkan jendela."
                : "Ada entri autostart milik salinan Phoron lain: " + asing
                  + ". Menyalakan sakelar ini akan menggantinya dengan yang ini.";
            SwAutoStart.IsChecked = s.AutoStartServices;
            SwTray.IsChecked = s.MinimizeToTray;
            SwVhost.IsChecked = s.AutoVhost;
            SwHosts.IsChecked = s.ManageHosts;
            SwPhpIni.IsChecked = s.PhpIniKeFolderPhp;
            TxtRoots.Text = string.Join(Environment.NewLine, s.BinRoots);
            foreach (ComboBoxItem item in CmbTerminal.Items)
                if ((item.Tag ?? "").ToString() == s.Terminal) CmbTerminal.SelectedItem = item;
            if (CmbTerminal.SelectedItem == null) CmbTerminal.SelectedIndex = 0;

            bool admin = HostsFile.IsAdmin();
            TxtAdmin.Text = admin
                ? "Phoron berjalan sebagai Administrator. Berkas hosts dan pemasangan sertifikat bisa dilakukan."
                : "Phoron berjalan tanpa hak Administrator. Menyunting berkas hosts dan memasang sertifikat "
                  + "ke Trusted Root tidak akan berhasil.";
            BtnAdmin.IsEnabled = !admin;
            TxtRoot.Text = Paths.Root;
            TxtVersi.Text = "Phoron " + AppInfo.Version;
            _mengisi = false;
        }

        /// <summary>
        /// Autostart ditulis SEKETIKA, tidak menunggu tombol Simpan: tempatnya di
        /// registri Windows, bukan di phoron.ini, jadi menggabungkannya dengan
        /// tombol Simpan hanya membuat dua sumber kebenaran yang bisa berbeda.
        /// </summary>
        void SwWindows_Ubah(object sender, RoutedEventArgs e)
        {
            if (_mengisi) return;
            var mau = SwWindows.IsChecked == true;
            var err = Autostart.Set(mau);
            if (err != null)
            {
                AppState.Warn(err);
                _mengisi = true;
                SwWindows.IsChecked = Autostart.Aktif;
                _mengisi = false;
                return;
            }
            _e.Say(mau
                ? "Phoron akan ikut menyala saat Windows dinyalakan."
                : "Phoron tidak lagi menyala otomatis saat Windows dinyalakan.");
            Isi();
        }

        void BtnSimpan_Click(object sender, RoutedEventArgs e)
        {
            var s = _e.Settings;
            s.AutoStartServices = SwAutoStart.IsChecked == true;
            s.MinimizeToTray = SwTray.IsChecked == true;
            s.AutoVhost = SwVhost.IsChecked == true;
            s.ManageHosts = SwHosts.IsChecked == true;
            s.PhpIniKeFolderPhp = SwPhpIni.IsChecked == true;
            var item = CmbTerminal.SelectedItem as ComboBoxItem;
            s.Terminal = item != null ? (item.Tag ?? "cmd").ToString() : "cmd";
            s.BinRoots = (TxtRoots.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
            if (s.BinRoots.Count == 0) s.BinRoots = Settings.DefaultBinRoots();
            s.Save();

            _e.Reload();
            AppState.RaiseChanged();
            AppState.ShowWarnings(_e.Apply());
            Isi();
            _e.Say("Pengaturan disimpan.");
        }

        void BtnAdmin_Click(object sender, RoutedEventArgs e)
        {
            Program.RestartAsAdmin("Beberapa fungsi Phoron perlu hak Administrator.");
        }

        void BtnRoot_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.Root); }
        void BtnIni_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.SettingsFile); }

        void BtnBersihHosts_Click(object sender, RoutedEventArgs e)
        {
            if (!AppState.Ask("Hapus semua baris yang ditambahkan Phoron dari berkas hosts? "
                              + "Baris milik aplikasi lain tidak disentuh.")) return;
            try
            {
                HostsFile.Clear();
                AppState.Info("Blok Phoron dibuang dari berkas hosts.");
            }
            catch (UnauthorizedAccessException)
            {
                Program.RestartAsAdmin("Menyunting berkas hosts butuh hak Administrator.");
            }
            catch (Exception ex) { AppState.Warn("Gagal: " + ex.Message); }
        }
    }
}
