using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Phoron.Core;

namespace Phoron.App.Pages
{
    /// <summary>
    /// Halaman basis data.
    ///
    /// DULU HALAMAN INI PUNYA PENJELAJAH SENDIRI - daftar tabel, isi tabel yang
    /// bisa disunting, struktur, kotak SQL. Semuanya bekerja, tapi semuanya juga
    /// sudah ada di HeidiSQL dalam bentuk yang jauh lebih lengkap, dan ditulis
    /// oleh orang yang memang mengerjakan itu saja selama belasan tahun.
    /// Memelihara dua jalan untuk hal yang sama berarti salah satunya pasti
    /// tertinggal - dan yang tertinggal itulah yang akan dipakai orang tanpa
    /// tahu ia kalah lengkap.
    ///
    /// YANG TERSISA DI SINI hanyalah yang memang tugas Phoron:
    ///   - menyambungkan HeidiSQL ke server milik PROFIL YANG SEDANG AKTIF,
    ///     tanpa orang perlu mengetik host, port, dan pengguna tiap kali
    ///     berganti profil;
    ///   - melihat basis data apa saja yang ada, dan membuat/menghapusnya;
    ///   - impor dan ekspor .sql yang sadar profil;
    ///   - menuliskan keterangan sambungan apa adanya, supaya alat lain bisa
    ///     memakainya tanpa menebak.
    ///
    /// Pemanggilan mysql.exe MEMBLOKIR utas pemanggilnya, jadi tidak satu pun
    /// boleh terjadi di utas layar.
    /// </summary>
    public partial class DatabasePage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        MySqlKlien.Sambungan _sambungan;
        List<MySqlSkema.InfoDb> _semuaDb = new List<MySqlSkema.InfoDb>();
        string _dbTerpilih;

        bool _sibuk;
        bool _mengisi;

        public DatabasePage()
        {
            InitializeComponent();
            _mengisi = true;
            TxtInfo.Text = Lang.T("Basis data MySQL profil yang sedang aktif. "
                                  + "Menjelajah dan menyunting isinya lewat HeidiSQL.");
            _mengisi = false;
            AturTombol();
            Loaded += (s, e) => MuatAsync();
        }

        // ------------------------------------------------------------- Penghalang

        string Halangan(out string rinci, out bool bisaNyalakan, out bool bisaKeProfil)
        {
            rinci = "";
            bisaNyalakan = false;
            bisaKeProfil = false;

            if (_e.Active == null)
            {
                rinci = Lang.T("Pilih atau buat sebuah profil lebih dulu.");
                bisaKeProfil = true;
                return Lang.T("Belum ada profil yang aktif.");
            }
            if (!_e.Active.PakaiMySql)
            {
                rinci = Lang.T("Profil ini disetel tanpa basis data. Pilih sebuah versi MySQL "
                               + "atau MariaDB di halaman Profil, lalu kembali ke sini.");
                bisaKeProfil = true;
                return Lang.T("Profil aktif tidak memakai basis data.");
            }
            if (_e.MySql == null)
            {
                rinci = Lang.T("Versi yang disebut profil ini tidak ada di folder bin. "
                               + "Pasang versinya di halaman Versi, atau pilih versi lain di Profil.");
                bisaKeProfil = true;
                return Lang.T("Versi basis data profil ini tidak ditemukan.");
            }
            if (_sambungan == null)
            {
                rinci = Lang.T("Paket ini tidak punya mysql.exe di folder bin-nya, "
                               + "jadi Phoron tidak punya cara bicara dengannya.");
                return Lang.T("Klien mysql tidak ada di paket ini.");
            }
            if (_e.Services.DbState != ServiceState.Jalan)
            {
                rinci = Lang.T("Basis data hanya bisa dijelajahi selagi servernya menyala.");
                bisaNyalakan = true;
                return Lang.T("MySQL sedang tidak jalan.");
            }
            return null;
        }

        void TampilkanHalangan(string judul, string rinci, bool bisaNyalakan, bool bisaKeProfil)
        {
            TxtHalangan.Text = judul;
            TxtHalanganRinci.Text = rinci;
            BtnNyalakan.Visibility = bisaNyalakan ? Visibility.Visible : Visibility.Collapsed;
            BtnKeProfil.Visibility = bisaKeProfil ? Visibility.Visible : Visibility.Collapsed;
            PanelHalangan.Visibility = Visibility.Visible;
            PanelIsi.Visibility = Visibility.Collapsed;
            KartuServer.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ Muat

        async void MuatAsync()
        {
            _sambungan = MySqlKlien.Untuk(_e);

            string rinci; bool nyalakan, keProfil;
            var halangan = Halangan(out rinci, out nyalakan, out keProfil);
            if (halangan != null) { TampilkanHalangan(halangan, rinci, nyalakan, keProfil); return; }

            PanelHalangan.Visibility = Visibility.Collapsed;
            PanelIsi.Visibility = Visibility.Visible;
            IsiKartuSambungan();
            IsiKartuHeidi();

            Sibuk(true);
            var s = _sambungan;
            string galatServer = null, galatDb = null;
            MySqlSkema.InfoServer server = null;
            List<MySqlSkema.InfoDb> daftar = null;
            await Task.Run(() =>
            {
                server = MySqlSkema.Server(s, out galatServer);
                if (galatServer == null) daftar = MySqlSkema.DaftarBasisData(s, out galatDb);
            });
            Sibuk(false);

            var galat = galatServer ?? galatDb;
            if (galat != null)
            {
                // Sambungan yang ditolak bukan keadaan "kosong". Kalau daftarnya
                // sekadar dikosongkan, layar ini berbohong bahwa tidak ada basis
                // data sama sekali.
                TampilkanHalangan(Lang.T("Tidak bisa menyambung ke MySQL."),
                                  galat + Environment.NewLine + Environment.NewLine
                                  + Lang.T("Pengguna dan sandi bisa diubah di Pengaturan."),
                                  false, false);
                return;
            }

            TxtServer.Text = server.Ringkas;
            KartuServer.Visibility = server.Ada ? Visibility.Visible : Visibility.Collapsed;

            var sebelumnya = _dbTerpilih;
            _semuaDb = daftar;
            if (_semuaDb.Count > 0)
                _dbTerpilih = sebelumnya != null && _semuaDb.Any(d => d.Nama == sebelumnya)
                    ? sebelumnya : _semuaDb[0].Nama;
            else
                _dbTerpilih = null;

            SaringDb();
            AturTombol();
            Status(_semuaDb.Count == 0
                ? Lang.T("Server ini belum punya basis data.")
                : string.Format(CultureInfo.CurrentCulture, Lang.T("{0} basis data, {1}."),
                                _semuaDb.Count, MySqlSkema.Ukur(_semuaDb.Sum(d => d.Bytes))));
        }

        /// <summary>
        /// Keterangan sambungan, ditulis apa adanya. Bukan hiasan: orang yang
        /// memakai DBeaver, TablePlus, atau sedang menulis kode aplikasinya
        /// butuh keempat nilai ini, dan menebaknya adalah sumber kebingungan
        /// yang tidak perlu.
        /// </summary>
        void IsiKartuSambungan()
        {
            TxtAlamat.Text = "127.0.0.1";
            TxtPort.Text = _e.Active != null
                ? _e.Active.MySqlPort.ToString(CultureInfo.InvariantCulture) : "-";
            var pengguna = _e.Settings != null && !string.IsNullOrEmpty(_e.Settings.DbPengguna)
                ? _e.Settings.DbPengguna : "root";
            TxtPengguna.Text = pengguna;

            bool adaSandi = _e.Settings != null && !string.IsNullOrEmpty(_e.Settings.DbSandi);
            TxtSandi.Text = adaSandi
                ? Lang.T("disetel di Pengaturan - tidak ditampilkan di sini")
                : Lang.T("kosong");
            TxtSandi.Opacity = adaSandi ? 0.8 : 0.6;

            TxtPerintah.Text = "mysql -u " + pengguna
                + (_e.Active != null && _e.Active.MySqlPort != 3306
                   ? " -P " + _e.Active.MySqlPort : "");
        }

        void IsiKartuHeidi()
        {
            var heidi = KlienLuar.CariHeidi(_e.Settings);
            BtnHeidi.IsEnabled = heidi.Ada;
            BtnLisensi.IsEnabled = heidi.Ada;
            BtnCariHeidi.Visibility = heidi.Ada ? Visibility.Collapsed : Visibility.Visible;

            if (heidi.Ada)
            {
                TxtHeidiJalur.Text = heidi.Jalur;
                TxtHeidiJalur.ToolTip = heidi.Jalur;
                TxtHeidiKeterangan.Text = Lang.T(
                    "Jelajahi tabel, sunting isinya, jalankan SQL, dan sunting strukturnya. "
                    + "Phoron membukanya sudah tersambung ke server profil ini.");
            }
            else
            {
                TxtHeidiJalur.Text = "";
                TxtHeidiJalur.ToolTip = null;
                // Bisa terjadi kalau komponennya dilewatkan saat pemasangan, atau
                // foldernya dihapus belakangan.
                TxtHeidiKeterangan.Text = Lang.T(
                    "HeidiSQL tidak ditemukan. Biasanya ia ikut terpasang bersama Phoron; "
                    + "kalau dilewatkan saat pemasangan, pasang ulang Phoron atau sebutkan "
                    + "sendiri jalurnya di bawah.");
            }
        }

        // ---------------------------------------------------------------- Saringan

        void TxtCariDb_Ubah(object sender, TextChangedEventArgs e) { if (!_mengisi) SaringDb(); }

        void SaringDb()
        {
            var cari = (TxtCariDb.Text ?? "").Trim();
            var tampil = cari.Length == 0 ? _semuaDb
                : _semuaDb.Where(d => d.Nama.IndexOf(cari, StringComparison.OrdinalIgnoreCase) >= 0)
                          .ToList();

            // Sorotan dikembalikan DI DALAM penjaga _mengisi. Di luar penjaga, ia
            // menyalakan DaftarDb_Pilih seolah orang baru saja mengklik basis data
            // itu - dan tiap ketukan di kotak cari jadi pekerjaan tambahan.
            var terpilih = _dbTerpilih;
            _mengisi = true;
            DaftarDb.ItemsSource = tampil;
            if (terpilih != null)
                foreach (MySqlSkema.InfoDb d in DaftarDb.Items)
                    if (d.Nama == terpilih) { DaftarDb.SelectedItem = d; break; }
            _mengisi = false;

            LblDb.Text = _semuaDb.Count == 0
                ? Lang.T("Basis data")
                : string.Format(CultureInfo.CurrentCulture, Lang.T("Basis data ({0})"), tampil.Count);
            TxtDbHampa.Text = _semuaDb.Count == 0
                ? Lang.T("Belum ada basis data di server ini.\nTekan Buat untuk membuatnya.")
                : Lang.T("Tidak ada yang cocok dengan pencarian.");
            TxtDbHampa.Visibility = tampil.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void DaftarDb_Pilih(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var pilih = DaftarDb.SelectedItem as MySqlSkema.InfoDb;
            _dbTerpilih = pilih != null ? pilih.Nama : null;
            AturTombol();
        }

        /// <summary>Klik ganda sebuah basis data berarti "buka yang ini".</summary>
        void DaftarDb_KlikGanda(object sender, MouseButtonEventArgs e)
        {
            if (DaftarDb.SelectedItem is MySqlSkema.InfoDb) BukaHeidi();
        }

        // ---------------------------------------------------------------- HeidiSQL

        void BtnHeidi_Click(object sender, RoutedEventArgs e) { BukaHeidi(); }

        void BukaHeidi()
        {
            var galat = KlienLuar.BukaHeidi(_e);
            if (galat != null) { AppState.Warn(galat, "HeidiSQL"); Status(galat, true); return; }

            var pesan = Lang.T("HeidiSQL dibuka untuk profil ini.");
            // Sandi sengaja tidak ikut di baris perintah - lihat KlienLuar. Kalau
            // memang ada sandinya, orang perlu tahu kenapa HeidiSQL menanyakannya.
            if (_e.Settings != null && !string.IsNullOrEmpty(_e.Settings.DbSandi))
                pesan += " " + Lang.T("Sandinya tidak ikut dikirim, jadi HeidiSQL akan menanyakannya.");
            _e.Say(pesan);
            Status(pesan);
        }

        void BtnCariHeidi_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = Lang.T("Pilih heidisql.exe"),
                Filter = "heidisql.exe|heidisql.exe|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            _e.Settings.HeidiSql = dlg.FileName;
            _e.Settings.Save();
            IsiKartuHeidi();
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("HeidiSQL disetel ke {0}."), dlg.FileName));
        }

        /// <summary>
        /// Buka folder HeidiSQL. Di dalamnya ada gpl.txt dan HeidiSQL-SUMBER.txt -
        /// teks lisensinya dan alamat kode sumbernya. Keduanya memang kewajiban
        /// GPL, dan kewajiban yang disembunyikan di dalam folder tanpa ada yang
        /// menunjukkannya sama saja tidak dipenuhi.
        /// </summary>
        void BtnLisensi_Click(object sender, RoutedEventArgs e)
        {
            var heidi = KlienLuar.CariHeidi(_e.Settings);
            if (!heidi.Ada) { Status(Lang.T("HeidiSQL tidak ditemukan.")); return; }
            var folder = Path.GetDirectoryName(heidi.Jalur);
            if (folder != null) Shell.Open(folder);
        }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            Shell.OpenTerminal(_e.Settings.Terminal, SiteScanner.DocumentRoot(_e.Active),
                               _e.ToolEnv());
        }

        // ------------------------------------------------------- Buat dan hapus

        async void BtnBuatDb_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk) return;
            var nama = InputDialog.Tanya(Window.GetWindow(this),
                Lang.T("Buat basis data"), Lang.T("Nama basis data baru:"), "");
            if (string.IsNullOrWhiteSpace(nama)) return;
            nama = nama.Trim();

            Sibuk(true);
            var s = _sambungan;
            MySqlKlien.Hasil h = null;
            await Task.Run(() => h = MySqlKlien.Jalankan(s,
                "CREATE DATABASE " + MySqlKlien.Kutip(nama)
                + " CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci"));
            Sibuk(false);

            if (!h.Ok) { AppState.Warn(h.Galat, Lang.T("Buat basis data")); return; }
            _dbTerpilih = nama;
            MuatAsync();
        }

        async void BtnHapusDb_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk) return;
            var pilih = DaftarDb.SelectedItem as MySqlSkema.InfoDb;
            if (pilih == null) return;
            var nama = pilih.Nama;

            // Namanya disebut di pertanyaan, dan jawabannya bukan "Ya" melainkan
            // mengetik namanya. DROP DATABASE tidak bisa dibatalkan dan tidak
            // menyisakan apa pun - konfirmasi yang bisa dilewati dengan satu klik
            // refleks terlalu murah untuk perbuatan semahal itu.
            var ketik = InputDialog.Tanya(Window.GetWindow(this),
                Lang.T("Hapus basis data"),
                string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Seluruh tabel dan isi \"{0}\" akan hilang dan tidak bisa dikembalikan. "
                           + "Ketik nama basis datanya untuk melanjutkan:"), nama), "");
            if (ketik == null) return;
            if (ketik.Trim() != nama)
            {
                AppState.Warn(Lang.T("Nama yang diketik tidak sama. Tidak ada yang dihapus."),
                              Lang.T("Hapus basis data"));
                return;
            }

            Sibuk(true);
            var s = _sambungan;
            MySqlKlien.Hasil h = null;
            await Task.Run(() => h = MySqlKlien.Jalankan(s, "DROP DATABASE " + MySqlKlien.Kutip(nama)));
            Sibuk(false);

            if (!h.Ok) { AppState.Warn(h.Galat, Lang.T("Hapus basis data")); return; }
            _e.Say(string.Format(CultureInfo.CurrentCulture,
                Lang.T("Basis data \"{0}\" dihapus."), nama));
            if (_dbTerpilih == nama) _dbTerpilih = null;
            MuatAsync();
        }

        // ------------------------------------------------------- Impor dan ekspor

        async void BtnEkspor_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk) return;
            if (_dbTerpilih == null) { Status(Lang.T("Pilih basis datanya dulu.")); return; }

            var dlg = new SaveFileDialog
            {
                Title = Lang.T("Ekspor basis data"),
                FileName = _dbTerpilih + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sql",
                Filter = Lang.T("Berkas SQL") + " (*.sql)|*.sql|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                DefaultExt = ".sql",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            Sibuk(true);
            Status(Lang.T("Mengekspor..."));
            var s = _sambungan;
            var db = _dbTerpilih;
            var tujuan = dlg.FileName;
            string galat = null;
            await Task.Run(() => galat = MySqlKlien.Ekspor(s, db, tujuan));
            Sibuk(false);

            if (galat != null) { AppState.Warn(galat, Lang.T("Ekspor basis data")); Status(galat, true); return; }

            long ukuran = 0;
            try { ukuran = new FileInfo(tujuan).Length; } catch { }
            var pesan = string.Format(CultureInfo.CurrentCulture,
                Lang.T("\"{0}\" diekspor ke {1} ({2})."), db, tujuan, MySqlSkema.Ukur(ukuran));
            _e.Say(pesan);
            Status(pesan);
        }

        async void BtnImpor_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk) return;
            if (_dbTerpilih == null) { Status(Lang.T("Pilih basis datanya dulu.")); return; }

            var dlg = new OpenFileDialog
            {
                Title = Lang.T("Impor berkas SQL"),
                Filter = Lang.T("Berkas SQL") + " (*.sql)|*.sql|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            // Impor menimpa tabel yang namanya sama, jadi ini bukan perbuatan
            // yang hanya menambah - dan basis data tujuannya disebut namanya.
            if (!AppState.Ask(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Jalankan isi berkas ini ke dalam \"{0}\"? "
                           + "Tabel yang namanya sama bisa tertimpa."), _dbTerpilih),
                    Lang.T("Impor berkas SQL")))
                return;

            Sibuk(true);
            Status(Lang.T("Mengimpor..."));
            var s = _sambungan;
            var db = _dbTerpilih;
            var sumber = dlg.FileName;
            string galat = null;
            await Task.Run(() => galat = MySqlKlien.Impor(s, db, sumber));
            Sibuk(false);

            if (galat != null) { AppState.Warn(galat, Lang.T("Impor berkas SQL")); Status(galat, true); return; }

            var pesan = string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0} diimpor ke \"{1}\"."), Path.GetFileName(sumber), db);
            _e.Say(pesan);
            Status(pesan);
            MuatAsync();
        }

        // ------------------------------------------------------------ Penghalang

        async void BtnNyalakan_Click(object sender, RoutedEventArgs e)
        {
            BtnNyalakan.IsEnabled = false;
            TxtHalanganRinci.Text = Lang.T("Menyalakan MySQL...");
            try { await _e.StartDbAsync(); }
            catch (Exception ex) { AppState.Warn(ex.Message, Lang.T("Nyalakan MySQL")); }
            BtnNyalakan.IsEnabled = true;
            MuatAsync();
        }

        void BtnKeProfil_Click(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this) as MainWindow;
            if (w != null) w.GoTo("profil");
        }

        void BtnMuatUlang_Click(object sender, RoutedEventArgs e) { MuatAsync(); }

        // ----------------------------------------------------------------- Bantu

        void AturTombol()
        {
            bool adaDb = _dbTerpilih != null;
            BtnHapusDb.IsEnabled = adaDb;
            BtnImpor.IsEnabled = adaDb;
            BtnEkspor.IsEnabled = adaDb;
        }

        void Sibuk(bool sibuk)
        {
            _sibuk = sibuk;
            PanelIsi.IsEnabled = !sibuk;
            Putaran.Visibility = sibuk ? Visibility.Visible : Visibility.Collapsed;
            Mouse.OverrideCursor = sibuk ? Cursors.Wait : null;
        }

        void Status(string teks, bool galat = false)
        {
            var satu = string.Join(" ", (teks ?? "").Split(
                new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray());
            TxtStatus.Text = satu;
            TxtStatus.SetResourceReference(ForegroundProperty,
                galat ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
            TxtStatus.ToolTip = satu.Length == 0 ? null : teks;
        }
    }
}
