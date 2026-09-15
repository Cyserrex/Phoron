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
            SwLogRinci.IsChecked = s.LogRinci;
            SwBerandaAkar.IsChecked = s.BerandaDiAkar;
            foreach (ComboBoxItem it in CmbTema.Items)
                if ((it.Tag ?? "").ToString() == s.Tema) CmbTema.SelectedItem = it;
            if (CmbTema.SelectedItem == null) CmbTema.SelectedIndex = 0;
            if (CmbBahasa.Items.Count == 0)
                foreach (var kode in Lang.Semua)
                    CmbBahasa.Items.Add(new ComboBoxItem { Content = Lang.NamaBahasa(kode), Tag = kode });
            foreach (ComboBoxItem it in CmbBahasa.Items)
                if ((it.Tag ?? "").ToString() == s.Bahasa) CmbBahasa.SelectedItem = it;
            if (CmbBahasa.SelectedItem == null) CmbBahasa.SelectedIndex = 0;
            SwCekPembaruan.IsChecked = s.CekPembaruan;
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
            SegarkanKeteranganPembaruan();
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
            // HANYA keterangannya yang disegarkan, bukan Isi() seluruh halaman.
            // Isi() membaca ulang SEMUA kotak dari setelan tersimpan, sehingga
            // sakelar lain yang baru saja diubah pengguna ikut dikembalikan ke
            // nilai lamanya - persis gejala "sakelar kembali off sendiri".
            SegarkanKeteranganWindows();
        }

        void SegarkanKeteranganWindows()
        {
            var asing = Autostart.EntriAsing();
            TxtWindows.Text = asing == null
                ? "Phoron mulai langsung mengecil ke baki sistem, tanpa memunculkan jendela."
                : "Ada entri autostart milik salinan Phoron lain: " + asing
                  + ". Menyalakan sakelar ini akan menggantinya dengan yang ini.";
        }

        /// <summary>
        /// Semua sakelar dan pilihan disimpan SEKETIKA. Sebelumnya sebagian
        /// menunggu tombol "Simpan pengaturan" di ujung bawah halaman yang
        /// panjang, sementara sakelar autostart Windows menyimpan sendiri -
        /// dua perilaku berbeda di satu halaman, dan yang menunggu tombol itu
        /// tampak seperti tidak berfungsi.
        /// </summary>
        void Sw_Ubah(object sender, RoutedEventArgs e) { SimpanSeketika(); }

        void CmbTema_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var it = CmbTema.SelectedItem as ComboBoxItem;
            _e.Settings.Tema = it != null ? (it.Tag ?? "sistem").ToString() : "sistem";
            _e.Settings.Save();
            MainWindow.TerapkanTema(_e.Settings.Tema);
        }

        void CmbBahasa_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var it = CmbBahasa.SelectedItem as ComboBoxItem;
            var kode = it != null ? (it.Tag ?? Lang.Indonesia).ToString() : Lang.Indonesia;
            if (kode == _e.Settings.Bahasa) return;
            _e.Settings.Bahasa = kode;
            _e.Settings.Save();
            Lang.Pakai(kode);
            // Halaman digambar ulang seluruhnya; penerjemahnya bekerja saat XAML
            // dimuat, jadi teks yang sudah terlanjur tampil tidak berubah sendiri.
            var utama = Window.GetWindow(this) as MainWindow;
            if (utama != null) utama.TerapkanBahasa();
        }

        void CmbTerminal_Ubah(object sender, SelectionChangedEventArgs e) { SimpanSeketika(); }

        void SimpanSeketika(bool pindaiUlang = false)
        {
            if (_mengisi) return;
            var s = _e.Settings;
            var vhostLama = s.AutoVhost;
            var hostsLama = s.ManageHosts;
            var logLama = s.LogRinci;
            var berandaLama = s.BerandaDiAkar;
            var phpIniLama = s.PhpIniKeFolderPhp;
            var rootsLama = string.Join(";", s.BinRoots);

            s.AutoStartServices = SwAutoStart.IsChecked == true;
            s.MinimizeToTray = SwTray.IsChecked == true;
            s.AutoVhost = SwVhost.IsChecked == true;
            s.ManageHosts = SwHosts.IsChecked == true;
            s.LogRinci = SwLogRinci.IsChecked == true;
            s.BerandaDiAkar = SwBerandaAkar.IsChecked == true;
            s.CekPembaruan = SwCekPembaruan.IsChecked == true;
            s.PhpIniKeFolderPhp = SwPhpIni.IsChecked == true;
            var item = CmbTerminal.SelectedItem as ComboBoxItem;
            s.Terminal = item != null ? (item.Tag ?? "cmd").ToString() : "cmd";
            s.Save();

            // Pemindaian ulang dan penulisan konfigurasi hanya dijalankan kalau
            // setelan yang MEMPENGARUHINYA benar-benar berubah. Menjalankannya
            // di tiap ketukan sakelar membuat halaman ini terasa berat tanpa
            // alasan.
            bool rootsBerubah = string.Join(";", s.BinRoots) != rootsLama;
            if (pindaiUlang && rootsBerubah)
            {
                _e.Reload();
                AppState.RaiseChanged();
            }
            if (rootsBerubah || s.AutoVhost != vhostLama || s.ManageHosts != hostsLama
                || s.LogRinci != logLama || s.PhpIniKeFolderPhp != phpIniLama
                || s.BerandaDiAkar != berandaLama)
            {
                // Peringatannya cukup masuk log; kotak pesan modal tiap kali
                // sakelar disentuh justru mengganggu.
                _e.Apply();
            }
        }

        void SegarkanKeteranganPembaruan()
        {
            var h = _e.Pembaruan;
            if (h == null)
                TxtPembaruan.Text = "Versi terpasang " + AppInfo.Version + ". Belum dicek.";
            else if (h.Galat != null)
                TxtPembaruan.Text = "Versi terpasang " + AppInfo.Version + ". " + h.Galat;
            else if (h.LebihBaru)
                TxtPembaruan.Text = "Phoron " + h.Versi + " sudah rilis; yang terpasang "
                                    + AppInfo.Version + ".";
            else
                TxtPembaruan.Text = "Phoron " + AppInfo.Version + " sudah versi terbaru.";
        }

        async void BtnCek_Click(object sender, RoutedEventArgs e)
        {
            BtnCek.IsEnabled = false;
            TxtPembaruan.Text = "Menghubungi GitHub...";
            try
            {
                // Dipaksa: tombol ini ditekan justru ketika orang ingin tahu
                // SEKARANG, jadi jeda enam jam tidak berlaku di sini.
                var hasil = await _e.CekPembaruanAsync(true);
                SegarkanKeteranganPembaruan();
                if (hasil != null && hasil.Galat == null && hasil.LebihBaru)
                    UpdateDialog.Tawarkan(_e, hasil);
                else if (hasil != null && hasil.Galat != null) AppState.Warn(hasil.Galat);
                else AppState.Info("Phoron " + AppInfo.Version + " sudah versi terbaru.");
            }
            finally { BtnCek.IsEnabled = true; }
        }

        void BtnRilis_Click(object sender, RoutedEventArgs e) { Shell.Open(Updater.HalamanRilis); }

        void BtnAdmin_Click(object sender, RoutedEventArgs e)
        {
            Program.RestartAsAdmin("Beberapa fungsi Phoron perlu hak Administrator.");
        }

        void BtnRoot_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.Root); }
        void BtnIni_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.SettingsFile); }

        void BtnDaftarHosts_Click(object sender, RoutedEventArgs e)
        {
            DaftarHosts.Jalankan(Window.GetWindow(this));
        }

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
