using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Phoron.Core;

namespace Phoron.App.Pages
{
    /// <summary>
    /// Pengelola basis data: basis data, tabel, isi tabel yang bisa DISUNTING,
    /// struktur berikut relasinya, dan SQL bebas.
    ///
    /// SELURUH PEKERJAANNYA LEWAT PROSES LUAR - mysql.exe milik profil aktif -
    /// dan setiap pemanggilan MEMBLOKIR utas pemanggilnya sampai selesai. Karena
    /// itu tidak satu pun panggilan MySqlKlien, MySqlSkema, atau MySqlSunting
    /// boleh terjadi di utas layar: kueri yang makan tiga detik akan membekukan
    /// seluruh jendela Phoron selama tiga detik, termasuk tombol berhenti Apache.
    /// </summary>
    public partial class DatabasePage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        MySqlKlien.Sambungan _sambungan;
        MySqlSkema.InfoServer _server;

        // Daftar LENGKAP, sebelum disaring kotak cari. Menyaring dengan membuang
        // isi daftar berarti kata cari yang dihapus tidak bisa mengembalikannya.
        List<MySqlSkema.InfoDb> _semuaDb = new List<MySqlSkema.InfoDb>();
        List<MySqlSkema.InfoTabel> _semuaTabel = new List<MySqlSkema.InfoTabel>();

        string _dbTerpilih;
        string _tabelTerpilih;

        // Bentuk tabel yang sedang dibuka. Dibaca sekali per tabel, lalu dipakai
        // oleh penjelajahan, penyuntingan, dan rangka INSERT.
        List<string> _kolomTabel = new List<string>();
        List<string> _kunci = new List<string>();
        List<MySqlSkema.InfoKolom> _kolomInfo = new List<MySqlSkema.InfoKolom>();
        bool _bentukTerbaca;
        bool _strukturTerbaca;

        MySqlSunting.Halaman _halaman;
        int _offset;
        int _perHalaman = 100;
        long _total = -1;
        string _urutKolom;
        bool _menurun;
        string _saring = "";

        readonly List<string> _riwayat = new List<string>();

        bool _sibuk;
        bool _mengisi;

        /// <summary>Baris daftar relasi - Tujuan dirakit supaya kolomnya terbaca sekali lihat.</summary>
        public class BarisRelasi
        {
            public string Kolom { get; set; }
            public string Tujuan { get; set; }
            public string SaatHapus { get; set; }
            public string SaatUbah { get; set; }
        }

        public DatabasePage()
        {
            InitializeComponent();
            _mengisi = true;
            TxtInfo.Text = Lang.T("Basis data MySQL profil yang sedang aktif. "
                                  + "Klik ganda sebuah tabel untuk melihat isinya.");
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

            _server = server;
            TxtServer.Text = server.Ringkas;
            KartuServer.Visibility = server.Ada ? Visibility.Visible : Visibility.Collapsed;

            // Tombol HeidiSQL hanya muncul kalau HeidiSQL memang ada di komputer
            // ini. Tombol yang selalu terlihat lalu mengeluh "tidak ditemukan"
            // saat ditekan cuma memindahkan kekecewaan ke belakang.
            var heidi = KlienLuar.CariHeidi(_e.Settings);
            BtnHeidi.Visibility = heidi.Ada ? Visibility.Visible : Visibility.Collapsed;
            BtnHeidi.ToolTip = heidi.Ada ? heidi.Jalur : null;

            var sebelumnya = _dbTerpilih;
            _semuaDb = daftar;

            if (_semuaDb.Count == 0)
            {
                _dbTerpilih = null;
                _semuaTabel.Clear();
                SaringDb();
                SaringTabel();
                BersihkanKanan();
                AturTombol();
                Status(Lang.T("Server ini belum punya basis data."));
                return;
            }

            // Pilihan sebelumnya dipertahankan: menyegarkan daftar setelah
            // menjalankan sesuatu tidak boleh melemparkan orang kembali ke awal.
            //
            // Tabelnya dimuat lewat pemanggilan LANGSUNG, bukan menyandarkan diri
            // pada peristiwa pemilihan: menyetel SelectedItem ke butir yang memang
            // sudah terpilih tidak menyalakan peristiwa apa pun.
            _dbTerpilih = sebelumnya != null && _semuaDb.Any(d => d.Nama == sebelumnya)
                ? sebelumnya : _semuaDb[0].Nama;
            SaringDb();
            await MuatTabelAsync();
        }

        void PilihDb(string nama)
        {
            foreach (MySqlSkema.InfoDb d in DaftarDb.Items)
                if (d.Nama == nama) { DaftarDb.SelectedItem = d; return; }
        }

        // ---------------------------------------------------------------- Saringan

        void TxtCariDb_Ubah(object sender, TextChangedEventArgs e) { if (!_mengisi) SaringDb(); }
        void TxtCariTabel_Ubah(object sender, TextChangedEventArgs e) { if (!_mengisi) SaringTabel(); }

        void SaringDb()
        {
            var cari = (TxtCariDb.Text ?? "").Trim();
            var tampil = cari.Length == 0 ? _semuaDb
                : _semuaDb.Where(d => d.Nama.IndexOf(cari, StringComparison.OrdinalIgnoreCase) >= 0)
                          .ToList();

            // Sorotan dikembalikan DI DALAM penjaga _mengisi. Di luar penjaga, ia
            // menyalakan DaftarDb_Pilih seolah orang baru mengklik basis data itu -
            // dan penangan itu membuang tabel terpilih lalu memuat ulang daftarnya.
            var terpilih = _dbTerpilih;
            _mengisi = true;
            DaftarDb.ItemsSource = tampil;
            if (terpilih != null) PilihDb(terpilih);
            _mengisi = false;

            LblDb.Text = _semuaDb.Count == 0
                ? Lang.T("Basis data")
                : string.Format(CultureInfo.CurrentCulture, Lang.T("Basis data ({0})"), tampil.Count);
            TxtDbHampa.Text = _semuaDb.Count == 0
                ? Lang.T("Belum ada basis data di server ini.\nTekan Buat untuk membuatnya.")
                : Lang.T("Tidak ada yang cocok dengan pencarian.");
            TxtDbHampa.Visibility = tampil.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SaringTabel()
        {
            var cari = (TxtCariTabel.Text ?? "").Trim();
            var tampil = cari.Length == 0 ? _semuaTabel
                : _semuaTabel.Where(t => t.Nama.IndexOf(cari, StringComparison.OrdinalIgnoreCase) >= 0)
                             .ToList();

            var terpilih = _tabelTerpilih;
            _mengisi = true;
            DaftarTabel.ItemsSource = tampil;
            foreach (MySqlSkema.InfoTabel t in DaftarTabel.Items)
                if (t.Nama == terpilih) { DaftarTabel.SelectedItem = t; break; }
            _mengisi = false;

            LblTabel.Text = _dbTerpilih == null
                ? Lang.T("Tabel")
                : string.Format(CultureInfo.CurrentCulture, Lang.T("Tabel ({0})"), tampil.Count);
            TxtTabelHampa.Text = _dbTerpilih == null
                ? Lang.T("Pilih sebuah basis data di sebelah kiri.")
                : _semuaTabel.Count == 0
                    ? Lang.T("Basis data ini belum punya tabel.\nBuat lewat tab SQL, atau impor berkas .sql.")
                    : Lang.T("Tidak ada yang cocok dengan pencarian.");
            TxtTabelHampa.Visibility = tampil.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------- Pemilihan

        async void DaftarDb_Pilih(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var pilih = DaftarDb.SelectedItem as MySqlSkema.InfoDb;
            _dbTerpilih = pilih != null ? pilih.Nama : null;
            _tabelTerpilih = null;
            BersihkanKanan();
            await MuatTabelAsync();
        }

        async Task MuatTabelAsync()
        {
            if (_dbTerpilih == null) { _semuaTabel.Clear(); SaringTabel(); AturTombol(); return; }

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            string galat = null;
            List<MySqlSkema.InfoTabel> daftar = null;
            await Task.Run(() => daftar = MySqlSkema.DaftarTabel(s, db, out galat));
            Sibuk(false);

            if (galat != null) { _semuaTabel.Clear(); SaringTabel(); Status(galat, true); return; }

            _semuaTabel = daftar;
            SaringTabel();
            AturTombol();

            var isi = _semuaDb.FirstOrDefault(d => d.Nama == db);
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0}: {1} tabel, {2}."), db, daftar.Count,
                isi != null ? isi.Ukuran : MySqlSkema.Ukur(daftar.Sum(t => t.Bytes))));
        }

        void DaftarTabel_Pilih(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var t = DaftarTabel.SelectedItem as MySqlSkema.InfoTabel;
            var nama = t != null ? t.Nama : null;
            if (nama == _tabelTerpilih) return;
            _tabelTerpilih = nama;
            _bentukTerbaca = false;
            _strukturTerbaca = false;
            AturTombol();
        }

        void DaftarTabel_KlikGanda(object sender, MouseButtonEventArgs e)
        {
            var t = DaftarTabel.SelectedItem as MySqlSkema.InfoTabel;
            if (t == null) return;
            _tabelTerpilih = t.Nama;
            Tab.SelectedIndex = 0;
            MulaiJelajahAsync();
        }

        void BtnStruktur_Click(object sender, RoutedEventArgs e)
        {
            if (_tabelTerpilih == null) { Status(Lang.T("Pilih tabelnya dulu.")); return; }
            Tab.SelectedIndex = 1;
        }

        async void Tab_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || e.OriginalSource != Tab) return;
            // Dibaca saat tabnya dibuka, bukan tiap kali tabel dipilih: kueri
            // tambahan di tiap klik daftar membuat penelusuran terasa tersendat,
            // dan sebagian besar klik memang cuma ingin melihat isi.
            if (Tab.SelectedIndex == 1 && _tabelTerpilih != null && !_strukturTerbaca)
                await MuatStrukturAsync();
        }

        // --------------------------------------------------------- Bentuk tabel

        /// <summary>
        /// Daftar kolom dan kunci utama tabel yang sedang dibuka.
        ///
        /// Kolomnya HARUS diketahui sebelum isinya diambil: kueri penjelajahan
        /// tidak memakai SELECT *, melainkan menyebut tiap kolom bersama satu
        /// kolom pendamping "IS NULL" - itulah satu-satunya cara membedakan sel
        /// yang benar-benar kosong dari teks yang isinya kata "NULL", dan tanpa
        /// pembedaan itu penyuntingan tidak bisa dipercaya.
        /// </summary>
        async Task<bool> PastikanBentukAsync()
        {
            if (_bentukTerbaca) return _kolomTabel.Count > 0;
            if (_tabelTerpilih == null) return false;

            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            string g1 = null, g2 = null;
            List<MySqlSkema.InfoKolom> kolom = null;
            List<string> kunci = null;
            await Task.Run(() =>
            {
                kolom = MySqlSkema.Struktur(s, db, tabel, out g1);
                kunci = MySqlSunting.KunciUtama(s, db, tabel, out g2);
            });

            if (g1 != null) { Status(g1, true); return false; }
            _kolomInfo = kolom;
            _kolomTabel = kolom.Select(k => k.Nama).ToList();
            _kunci = kunci ?? new List<string>();
            _bentukTerbaca = true;

            var tilikan = _semuaTabel.FirstOrDefault(t => t.Nama == tabel);
            bool bisaSunting = _kunci.Count > 0 && (tilikan == null || !tilikan.Tilikan);
            Kisi.IsReadOnly = !bisaSunting;
            LblSunting.Text = bisaSunting
                ? string.Format(CultureInfo.CurrentCulture,
                                Lang.T("kunci: {0} - klik ganda sel untuk menyunting"),
                                string.Join(", ", _kunci.ToArray()))
                : Lang.T("tanpa kunci utama - hanya bisa dibaca");
            return _kolomTabel.Count > 0;
        }

        // ------------------------------------------------------------- Jelajah

        async void MulaiJelajahAsync()
        {
            _offset = 0;
            _total = -1;
            _urutKolom = null;
            _menurun = false;
            await JelajahAsync(true);
        }

        void TxtSaring_Tombol(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; TerapkanSaringAsync(); }
        }

        void BtnSaring_Click(object sender, RoutedEventArgs e) { TerapkanSaringAsync(); }

        async void TerapkanSaringAsync()
        {
            _saring = (TxtSaring.Text ?? "").Trim();
            _offset = 0;
            _total = -1;
            await JelajahAsync(true);
        }

        async Task JelajahAsync(bool hitungTotal)
        {
            if (_sibuk || _tabelTerpilih == null) return;

            Sibuk(true);
            try
            {
                if (!await PastikanBentukAsync()) return;

                var s = _sambungan;
                var db = _dbTerpilih;
                var tabel = _tabelTerpilih;
                var kolom = _kolomTabel;
                int offset = _offset, batas = _perHalaman;
                string urut = _urutKolom, saring = _saring;
                bool turun = _menurun;
                MySqlSunting.Halaman hal = null;
                await Task.Run(() => hal = MySqlSunting.Isi(s, db, tabel, kolom, offset, batas,
                                                            urut, turun, saring, hitungTotal));

                LblJelajah.Text = tabel + (urut != null
                    ? "  ·  " + Lang.T("urut") + " " + urut + (turun ? " ↓" : " ↑") : "");

                if (!hal.Ok)
                {
                    _halaman = null;
                    IsiKisi(null);
                    TxtKisiHampa.Text = hal.Galat;
                    TxtKisiHampa.Visibility = Visibility.Visible;
                    Status(hal.Galat, true);
                    return;
                }

                if (hitungTotal) _total = hal.Total;
                _halaman = hal;
                IsiKisi(hal);
                TxtKisiHampa.Text = saring.Length > 0
                    ? Lang.T("Tidak ada baris yang cocok dengan saringan ini.")
                    : Lang.T("Tabel ini tidak punya baris.");
                TxtKisiHampa.Visibility = hal.Baris.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;

                AturBilahHalaman(hal.Baris.Count);
                AturTombol();
                Status(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} baris terbaca dalam {1} ms."), hal.Baris.Count, hal.Ms));
            }
            finally { Sibuk(false); }
        }

        void AturBilahHalaman(int jumlahTampil)
        {
            var dari = jumlahTampil == 0 ? 0 : _offset + 1;
            var sampai = _offset + jumlahTampil;
            TxtHalaman.Text = _total >= 0
                ? string.Format(CultureInfo.CurrentCulture, Lang.T("{0}-{1} dari {2}"),
                                dari, sampai, _total.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, Lang.T("{0}-{1}"), dari, sampai);

            BtnAwal.IsEnabled = _offset > 0;
            BtnMundur.IsEnabled = _offset > 0;
            // Maju dimatikan begitu halaman ini tidak penuh: halaman terakhir
            // selalu kurang dari batasnya, dan itu tanda yang cukup - tanpa perlu
            // mengandalkan COUNT(*) yang bisa saja tidak dihitung.
            BtnMaju.IsEnabled = jumlahTampil >= _perHalaman;
        }

        async void BtnAwal_Click(object sender, RoutedEventArgs e)
        {
            if (_offset == 0) return;
            _offset = 0;
            await JelajahAsync(false);
        }

        async void BtnMundur_Click(object sender, RoutedEventArgs e)
        {
            if (_offset == 0) return;
            _offset = Math.Max(0, _offset - _perHalaman);
            await JelajahAsync(false);
        }

        async void BtnMaju_Click(object sender, RoutedEventArgs e)
        {
            _offset += _perHalaman;
            await JelajahAsync(false);
        }

        async void CmbPerHalaman_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            var item = CmbPerHalaman.SelectedItem as ComboBoxItem;
            int n;
            if (item == null || !int.TryParse((item.Content ?? "").ToString(), out n)) return;
            _perHalaman = n;
            _offset = 0;
            if (_tabelTerpilih != null) await JelajahAsync(false);
        }

        /// <summary>
        /// Pengurutan dikerjakan SERVER, bukan kisi.
        ///
        /// Kisi hanya memegang satu halaman, jadi mengurutkannya sendiri berarti
        /// mengurutkan seratus baris yang kebetulan sedang terlihat - dan itu
        /// menampilkan "nilai terbesar" yang bukan nilai terbesar di tabelnya.
        /// Salah yang tampak benar, dan itu jenis kesalahan yang paling mahal.
        /// </summary>
        async void Kisi_Urut(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (_tabelTerpilih == null) return;

            // Nama aslinya, bukan judul yang garis bawahnya digandakan - yang
            // dikirim ke ORDER BY harus nama kolom yang sungguhan ada.
            var kolom = (e.Column.Header as string ?? "").Replace("__", "_");
            if (kolom.Length == 0) return;

            if (_urutKolom == kolom) _menurun = !_menurun;
            else { _urutKolom = kolom; _menurun = false; }

            foreach (var c in Kisi.Columns)
                c.SortDirection = (c.Header as string ?? "").Replace("__", "_") == _urutKolom
                    ? (_menurun ? System.ComponentModel.ListSortDirection.Descending
                                : System.ComponentModel.ListSortDirection.Ascending)
                    : (System.ComponentModel.ListSortDirection?)null;

            _offset = 0;
            await JelajahAsync(false);
        }

        // -------------------------------------------------------- Tampilan kisi

        /// <summary>
        /// Bangun kolom kisi dari halaman yang baru dibaca. Kolomnya dibuat di
        /// kode karena bentuk hasil baru diketahui saat kueri selesai.
        /// </summary>
        void IsiKisi(MySqlSunting.Halaman hal)
        {
            Kisi.ItemsSource = null;
            Kisi.Columns.Clear();
            if (hal == null) return;

            for (int i = 0; i < hal.Kolom.Count; i++)
            {
                var info = _kolomInfo.FirstOrDefault(k => k.Nama == hal.Kolom[i]);

                // Kolom bertemplat, bukan kolom teks biasa. Yang DITAMPILKAN dan
                // yang DISUNTING memang berbeda: tampilan memakai Papar, yang
                // menuliskan NULL untuk sel kosong; suntingan memakai Teks, yang
                // untuk sel kosong bernilai null sehingga kotaknya mulai kosong -
                // kalau tidak, membuka lalu menutup sel kosong akan menyimpan
                // teks "NULL" ke dalamnya.
                //
                // Pemiringan tidak bisa ditempuh lewat gaya pada kolom teks: nilai
                // lokal dari pengikatan kolom selalu menang atas setter di dalam
                // Style, jadi pemicunya tidak akan pernah kelihatan.
                var gaya = new Style(typeof(TextBlock));
                var pemicu = new DataTrigger
                {
                    Binding = new Binding("[" + i + "].Kosong"),
                    Value = true,
                };
                pemicu.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
                pemicu.Setters.Add(new Setter(TextBlock.OpacityProperty, 0.5));
                gaya.Triggers.Add(pemicu);
                gaya.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(8, 3, 8, 3)));
                gaya.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,
                                            TextTrimming.CharacterEllipsis));

                var tampil = new FrameworkElementFactory(typeof(TextBlock));
                tampil.SetBinding(TextBlock.TextProperty, new Binding("[" + i + "].Papar"));
                tampil.SetValue(TextBlock.StyleProperty, gaya);

                var sunting = new FrameworkElementFactory(typeof(TextBox));
                sunting.SetBinding(TextBox.TextProperty,
                    new Binding("[" + i + "].Teks")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    });
                sunting.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
                sunting.SetValue(TextBox.PaddingProperty, new Thickness(6, 1, 6, 1));

                Kisi.Columns.Add(new DataGridTemplateColumn
                {
                    // Garis bawah di nama kolom DIGANDAKAN: judul kisi memakan satu
                    // garis bawah sebagai penanda tombol akses, jadi nama_pelanggan
                    // akan tampil namapelanggan. Diputuskan dari gambar hasil render,
                    // bukan dari pembacaan properti Text - properti itu tidak berubah;
                    // pemakanannya terjadi saat digambar.
                    Header = hal.Kolom[i].Replace("_", "__"),
                    CellTemplate = new DataTemplate { VisualTree = tampil },
                    CellEditingTemplate = new DataTemplate { VisualTree = sunting },
                    MaxWidth = 420,
                    CanUserSort = true,
                    SortMemberPath = "[" + i + "].Teks",
                    // Jenis kolomnya tidak muat di judul, tapi selalu dibutuhkan saat
                    // menyunting - jadi ia tinggal di tooltip, bukan hilang sama sekali.
                    HeaderStyle = JudulBerTooltip(info),
                });
            }
            Kisi.ItemsSource = hal.Baris;
        }

        Style JudulBerTooltip(MySqlSkema.InfoKolom info)
        {
            var gaya = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                                 (Style)FindResource("JudulKisi"));
            if (info != null)
            {
                var tip = info.Jenis;
                if (!string.IsNullOrEmpty(info.Kunci)) tip += "  ·  " + info.Kunci;
                if (!string.IsNullOrEmpty(info.Ekstra)) tip += "  ·  " + info.Ekstra;
                gaya.Setters.Add(new Setter(ToolTipProperty, tip));
            }
            return gaya;
        }

        void BersihkanKanan()
        {
            _halaman = null;
            _kolomTabel = new List<string>();
            _kolomInfo = new List<MySqlSkema.InfoKolom>();
            _kunci = new List<string>();
            _bentukTerbaca = false;
            _strukturTerbaca = false;
            IsiKisi(null);
            KisiKolom.ItemsSource = null;
            KisiIndeks.ItemsSource = null;
            KisiRelasi.ItemsSource = null;
            _total = -1;
            _offset = 0;
            _urutKolom = null;
            _saring = "";
            _mengisi = true;
            TxtSaring.Text = "";
            _mengisi = false;
            LblJelajah.Text = "";
            LblSunting.Text = "";
            TxtHalaman.Text = "";
            TxtKisiHampa.Text = Lang.T("Pilih sebuah tabel, atau klik gandanya.");
            TxtKisiHampa.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------ Menyunting

        // Keadaan baris SEBELUM disunting. Kotak suntingnya terikat dua arah, jadi
        // begitu orang mengetik, nilai di dalam objeknya sudah berubah - padahal
        // WHERE untuk UPDATE harus memakai nilai LAMA. Tanpa potret ini, menyunting
        // kolom yang kebetulan bagian kunci utama akan mencari baris memakai nilai
        // yang baru saja diketik, dan tidak menemukan apa pun.
        Dictionary<string, MySqlSunting.Sel> _barisSebelum;
        string _selSebelum;
        bool _selKosongSebelum;

        void Kisi_MulaiSunting(object sender, DataGridBeginningEditEventArgs e)
        {
            var baris = e.Row.Item as MySqlSunting.Sel[];
            if (baris == null || _halaman == null) { _barisSebelum = null; return; }

            _barisSebelum = new Dictionary<string, MySqlSunting.Sel>(StringComparer.Ordinal);
            for (int k = 0; k < _halaman.Kolom.Count && k < baris.Length; k++)
                _barisSebelum[_halaman.Kolom[k]] = new MySqlSunting.Sel
                {
                    Teks = baris[k].Teks,
                    Kosong = baris[k].Kosong,
                };

            int i = Kisi.Columns.IndexOf(e.Column);
            _selSebelum = i >= 0 && i < baris.Length ? baris[i].Teks : null;
            _selKosongSebelum = i >= 0 && i < baris.Length && baris[i].Kosong;
        }

        /// <summary>
        /// Simpan satu sel yang baru disunting.
        ///
        /// Dijalankan dari peristiwa selesai-sunting, bukan dari tombol simpan
        /// tersendiri: kisi yang sudah berubah di layar tapi belum tersimpan
        /// adalah kebohongan yang menunggu waktu, dan tombol simpan yang lupa
        /// ditekan tidak memberi tanda apa pun.
        /// </summary>
        async void Kisi_SelesaiSunting(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (_halaman == null || _tabelTerpilih == null || _barisSebelum == null) return;

            var baris = e.Row.Item as MySqlSunting.Sel[];
            if (baris == null) return;

            int i = Kisi.Columns.IndexOf(e.Column);
            if (i < 0 || i >= _halaman.Kolom.Count) return;

            var kolom = _halaman.Kolom[i];
            var baru = baris[i].Teks;
            if (!_selKosongSebelum && string.Equals(_selSebelum, baru, StringComparison.Ordinal))
                return;

            var asli = _barisSebelum;
            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            var kunci = _kunci;
            MySqlSunting.HasilUbah hasil = null;
            await Task.Run(() => hasil = MySqlSunting.UbahSel(s, db, tabel, kunci, asli,
                                                              kolom, baru, false));
            Sibuk(false);

            if (!hasil.Ok)
            {
                // Nilai di layar dikembalikan ke keadaan sebelumnya. Membiarkan
                // nilai yang gagal tersimpan tetap terpampang membuat orang mengira
                // perubahannya sudah masuk.
                baris[i].Teks = _selSebelum;
                baris[i].Kosong = _selKosongSebelum;
                Kisi.Items.Refresh();
                AppState.Warn(hasil.Galat, Lang.T("Simpan perubahan"));
                Status(hasil.Galat, true);
                return;
            }

            baris[i].Kosong = false;
            Kisi.Items.Refresh();
            _e.Say(hasil.Sql);
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0} tersimpan - {1} baris terpengaruh."), kolom, hasil.Terpengaruh));
        }

        /// <summary>Nilai baris menurut nama kolom, untuk menyusun WHERE dari kunci utama.</summary>
        Dictionary<string, MySqlSunting.Sel> PetaBaris(MySqlSunting.Sel[] baris)
        {
            var peta = new Dictionary<string, MySqlSunting.Sel>(StringComparer.Ordinal);
            if (_halaman == null) return peta;
            for (int i = 0; i < _halaman.Kolom.Count && i < baris.Length; i++)
                peta[_halaman.Kolom[i]] = baris[i];
            return peta;
        }

        async void BtnNull_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk || _halaman == null) return;
            var sel = Kisi.CurrentCell;
            var baris = sel.Item as MySqlSunting.Sel[];
            if (baris == null || sel.Column == null)
            {
                Status(Lang.T("Pilih sebuah sel dulu."));
                return;
            }
            int i = Kisi.Columns.IndexOf(sel.Column);
            if (i < 0 || i >= _halaman.Kolom.Count) return;

            var kolom = _halaman.Kolom[i];
            var info = _kolomInfo.FirstOrDefault(k => k.Nama == kolom);
            if (info != null && info.Kosong == Lang.T("tidak"))
            {
                // Ditolak di sini, bukan dibiarkan ditolak server: pesan MySQL untuk
                // hal ini menyebut nomor kolom, bukan namanya.
                Status(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Kolom \"{0}\" tidak boleh kosong."), kolom), true);
                return;
            }

            var asli = PetaBaris(baris);
            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            var kunci = _kunci;
            MySqlSunting.HasilUbah hasil = null;
            await Task.Run(() => hasil = MySqlSunting.UbahSel(s, db, tabel, kunci, asli,
                                                              kolom, null, true));
            Sibuk(false);

            if (!hasil.Ok) { AppState.Warn(hasil.Galat, Lang.T("Jadikan NULL")); return; }
            baris[i].Kosong = true;
            baris[i].Teks = null;
            Kisi.Items.Refresh();
            _e.Say(hasil.Sql);
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("\"{0}\" dijadikan NULL."), kolom));
        }

        async void BtnHapusBaris_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk || _halaman == null) return;
            var baris = Kisi.CurrentCell.Item as MySqlSunting.Sel[];
            if (baris == null) { Status(Lang.T("Pilih barisnya dulu.")); return; }

            var asli = PetaBaris(baris);
            var sebut = string.Join(", ", _kunci
                .Where(k => asli.ContainsKey(k))
                .Select(k => k + " = " + asli[k]).ToArray());

            if (!AppState.Ask(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Hapus baris {0} dari \"{1}\"? Tidak bisa dikembalikan."),
                    sebut.Length > 0 ? sebut : "?", _tabelTerpilih),
                    Lang.T("Hapus baris")))
                return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            var kunci = _kunci;
            MySqlSunting.HasilUbah hasil = null;
            await Task.Run(() => hasil = MySqlSunting.HapusBaris(s, db, tabel, kunci, asli));
            Sibuk(false);

            if (!hasil.Ok) { AppState.Warn(hasil.Galat, Lang.T("Hapus baris")); return; }
            _e.Say(hasil.Sql);
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0} baris dihapus."), hasil.Terpengaruh));
            await JelajahAsync(true);
        }

        async void BtnTambahBaris_Click(object sender, RoutedEventArgs e)
        {
            if (_tabelTerpilih == null) { Status(Lang.T("Pilih tabelnya dulu.")); return; }
            Sibuk(true);
            try { if (!await PastikanBentukAsync()) return; }
            finally { Sibuk(false); }

            // Rangka SQL, bukan deretan kotak isian: nilai bawaan, auto_increment,
            // dan kolom yang boleh kosong punya aturan sendiri-sendiri yang lebih
            // jelas terbaca sebagai SQL.
            TxtSql.Text = MySqlSunting.RangkaInsert(_tabelTerpilih, _kolomInfo);
            Tab.SelectedIndex = 2;
            TxtSql.Focus();
            Status(Lang.T("Rangka INSERT dimuat ke kotak SQL. Isi nilainya, lalu jalankan."));
        }

        void BtnSalinInsert_Click(object sender, RoutedEventArgs e)
        {
            if (_halaman == null) { Status(Lang.T("Belum ada hasil untuk disalin.")); return; }
            var baris = Kisi.CurrentCell.Item as MySqlSunting.Sel[];
            if (baris == null) { Status(Lang.T("Pilih barisnya dulu.")); return; }
            SalinKePapan(MySqlSunting.BarisJadiInsert(_tabelTerpilih, _halaman.Kolom, baris));
        }

        void BtnCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_halaman == null || _halaman.Baris.Count == 0)
            {
                Status(Lang.T("Belum ada hasil untuk disimpan."));
                return;
            }
            var dlg = new SaveFileDialog
            {
                Title = Lang.T("Ekspor CSV"),
                FileName = _tabelTerpilih + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv",
                Filter = Lang.T("Berkas CSV") + " (*.csv)|*.csv|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                DefaultExt = ".csv",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                // BOM UTF-8 sengaja ditulis: tanpa itu Excel membuka berkasnya
                // dengan halaman kode mesin, dan setiap aksara beraksen jadi rusak.
                File.WriteAllText(dlg.FileName,
                    MySqlSunting.Csv(_halaman.Kolom, _halaman.Baris),
                    new UTF8Encoding(true));
                var pesan = string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} baris disimpan ke {1}."), _halaman.Baris.Count, dlg.FileName);
                _e.Say(pesan);
                Status(pesan);
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        // ------------------------------------------------------------- Struktur

        async Task MuatStrukturAsync()
        {
            if (_tabelTerpilih == null) return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            string g1 = null, g2 = null, g3 = null;
            List<MySqlSkema.InfoKolom> kolom = null;
            List<MySqlSkema.InfoIndeks> indeks = null;
            List<MySqlSunting.InfoRelasi> relasi = null;
            await Task.Run(() =>
            {
                kolom = MySqlSkema.Struktur(s, db, tabel, out g1);
                indeks = MySqlSkema.Indeks(s, db, tabel, out g2);
                relasi = MySqlSunting.Relasi(s, db, tabel, out g3);
            });
            Sibuk(false);

            KisiKolom.ItemsSource = kolom;
            KisiIndeks.ItemsSource = indeks;
            KisiRelasi.ItemsSource = relasi.Select(r => new BarisRelasi
            {
                Kolom = r.Kolom,
                Tujuan = r.KeTabel + "." + r.KeKolom,
                SaatHapus = r.SaatHapus,
                SaatUbah = r.SaatUbah,
            }).ToList();
            TxtRelasiHampa.Visibility = relasi.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _strukturTerbaca = true;

            if (g1 != null) { Status(g1, true); return; }
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0}: {1} kolom, {2} indeks, {3} relasi."),
                tabel, kolom.Count, indeks.Count, relasi.Count));
        }

        async void BtnCreateTable_Click(object sender, RoutedEventArgs e)
        {
            if (_tabelTerpilih == null) { Status(Lang.T("Pilih tabelnya dulu.")); return; }

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            string galat = null, sql = null;
            await Task.Run(() => sql = MySqlSkema.BuatTabelSql(s, db, tabel, out galat));
            Sibuk(false);

            if (galat != null) { Status(galat, true); return; }

            // Ditaruh di kotak SQL, bukan di kotak pesan tersendiri: dari sana ia
            // bisa disunting dan dijalankan lagi - misalnya untuk membuat salinan
            // tabel dengan nama lain - dan itulah yang biasanya ingin dilakukan.
            TxtSql.Text = sql;
            Tab.SelectedIndex = 2;
            TxtSql.Focus();
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("CREATE TABLE untuk \"{0}\" dimuat ke kotak SQL."), tabel));
        }

        void BtnSalinStruktur_Click(object sender, RoutedEventArgs e)
        {
            var kolom = KisiKolom.ItemsSource as IEnumerable<MySqlSkema.InfoKolom>;
            if (kolom == null) { Status(Lang.T("Belum ada struktur untuk disalin.")); return; }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", new[]
            {
                Lang.T("Nama"), Lang.T("Jenis"), Lang.T("Boleh kosong"),
                Lang.T("Kunci"), Lang.T("Bawaan"), Lang.T("Ekstra"),
            }));
            foreach (var k in kolom)
                sb.AppendLine(string.Join("\t", new[]
                    { k.Nama, k.Jenis, k.Kosong, k.Kunci, k.Bawaan, k.Ekstra }));
            SalinKePapan(sb.ToString());
        }

        // ------------------------------------------------------------------ SQL

        void BtnJalankan_Click(object sender, RoutedEventArgs e) { JalankanAsync(); }

        void TxtSql_Tombol(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                JalankanAsync();
            }
        }

        async void JalankanAsync()
        {
            if (_sibuk) return;
            var teks = (TxtSql.Text ?? "").Trim();
            if (teks.Length == 0) return;

            // Dipecah di sini, bukan dikirim sekaligus. Keluaran --batch dari
            // beberapa perintah menempel tanpa penanda apa pun, jadi baris judul
            // kolom perintah kedua akan terbaca sebagai data perintah pertama.
            var perintah = MySqlKlien.Pisah(teks);
            if (perintah.Count == 0) return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var catatan = new List<string>();
            MySqlKlien.Hasil terakhirBerbaris = null;
            string galat = null;
            long totalMs = 0;
            bool skemaBerubah = false;
            int selesai = 0;

            await Task.Run(() =>
            {
                for (int i = 0; i < perintah.Count; i++)
                {
                    var p = perintah[i];
                    var h = MySqlKlien.Jalankan(s, p, db, 0);
                    totalMs += h.Ms;
                    var kepala = "[" + (i + 1) + "] " + Singkat(p);
                    if (!h.Ok)
                    {
                        catatan.Add(kepala + "  ->  " + Lang.T("GAGAL") + ": " + Ratakan(h.Galat));
                        galat = h.Galat;
                        break;
                    }
                    selesai++;
                    catatan.Add(kepala + "  ->  " + (h.Tabel != null
                        ? string.Format(CultureInfo.CurrentCulture, Lang.T("{0} baris"),
                                        h.Tabel.Baris.Count)
                        : h.Terpengaruh >= 0
                            ? string.Format(CultureInfo.CurrentCulture,
                                            Lang.T("{0} baris terpengaruh"), h.Terpengaruh)
                            : Lang.T("selesai")) + "  (" + h.Ms + " ms)");
                    if (h.Tabel != null) terakhirBerbaris = h;
                    if (MengubahSkema(p)) skemaBerubah = true;
                }
            });
            Sibuk(false);

            IngatRiwayat(teks);

            // Catatan per perintah hanya muncul saat memang ada lebih dari satu.
            // Untuk satu perintah, bilah status di bawah sudah mengatakan semuanya.
            bool perluLog = perintah.Count > 1;
            TxtLogSql.Visibility = perluLog ? Visibility.Visible : Visibility.Collapsed;
            if (perluLog) TxtLogSql.Text = string.Join(Environment.NewLine, catatan.ToArray());

            IsiKisiSql(terakhirBerbaris != null ? terakhirBerbaris.Tabel : null);

            if (galat != null)
            {
                TxtSqlHampa.Text = galat;
                TxtSqlHampa.Visibility = Visibility.Visible;
                Status(perintah.Count > 1
                    ? string.Format(CultureInfo.CurrentCulture,
                        Lang.T("Berhenti di perintah ke-{0}. {1} sebelumnya sudah jalan."),
                        selesai + 1, selesai)
                    : galat, true);
                if (skemaBerubah) { await MuatTabelAsync(); _bentukTerbaca = false; }
                return;
            }

            TxtSqlHampa.Text = terakhirBerbaris == null
                ? Lang.T("Perintah ini tidak mengembalikan baris.")
                : Lang.T("Hasilnya kosong.");
            TxtSqlHampa.Visibility = terakhirBerbaris == null
                                     || terakhirBerbaris.Tabel.Baris.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

            var pesan = new List<string>();
            if (perintah.Count > 1)
                pesan.Add(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} perintah"), perintah.Count));
            if (terakhirBerbaris != null)
                pesan.Add(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} baris"), terakhirBerbaris.Tabel.Baris.Count));
            pesan.Add(totalMs + " ms");
            Status(string.Join(" · ", pesan.ToArray()));

            // Daftar tabel ikut disegarkan hanya bila memang ada yang mengubahnya.
            // Menyegarkannya setelah SETIAP kueri berarti dua pemanggilan proses
            // tambahan untuk tiap SELECT, dan itu terasa di setiap klik.
            if (skemaBerubah)
            {
                await MuatTabelAsync();
                _bentukTerbaca = false;
                _strukturTerbaca = false;
            }
        }

        static string Singkat(string sql)
        {
            var t = Ratakan(sql);
            return t.Length > 48 ? t.Substring(0, 48) + "..." : t;
        }

        static string Ratakan(string teks)
        {
            return string.Join(" ", (teks ?? "").Split(
                new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray());
        }

        static bool MengubahSkema(string sql)
        {
            var kata = new string((sql ?? "").TrimStart().TakeWhile(char.IsLetter).ToArray())
                       .ToUpperInvariant();
            return kata == "CREATE" || kata == "DROP" || kata == "ALTER"
                   || kata == "RENAME" || kata == "TRUNCATE";
        }

        /// <summary>Hasil tab SQL. Di sini NULL TIDAK dibedakan - lihat catatan di MySqlKlien.</summary>
        void IsiKisiSql(MySqlKlien.Tabel t)
        {
            KisiSql.ItemsSource = null;
            KisiSql.Columns.Clear();
            if (t == null) return;

            for (int i = 0; i < t.Kolom.Count; i++)
                KisiSql.Columns.Add(new DataGridTextColumn
                {
                    Header = t.Kolom[i].Replace("_", "__"),
                    Binding = new Binding("[" + i + "]"),
                    MaxWidth = 420,
                });
            KisiSql.ItemsSource = t.Baris;
        }

        /// <summary>
        /// Kueri yang pernah dijalankan, supaya tidak perlu diketik ulang.
        /// Hanya di dalam ingatan: menyimpannya ke cakram berarti menyimpan
        /// potongan data orang - kueri sering memuat nama, alamat, dan nomor
        /// yang ikut tertulis apa adanya di dalam WHERE.
        /// </summary>
        void IngatRiwayat(string sql)
        {
            _riwayat.RemoveAll(x => x == sql);
            _riwayat.Insert(0, sql);
            while (_riwayat.Count > 20) _riwayat.RemoveAt(_riwayat.Count - 1);

            _mengisi = true;
            CmbRiwayat.ItemsSource = _riwayat
                .Select(x => Singkat(x)).ToList();
            CmbRiwayat.SelectedIndex = -1;
            _mengisi = false;
        }

        void CmbRiwayat_Pilih(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var i = CmbRiwayat.SelectedIndex;
            if (i >= 0 && i < _riwayat.Count) { TxtSql.Text = _riwayat[i]; TxtSql.Focus(); }
        }

        void BtnBersih_Click(object sender, RoutedEventArgs e)
        {
            TxtSql.Clear();
            IsiKisiSql(null);
            TxtSqlHampa.Visibility = Visibility.Collapsed;
            TxtLogSql.Visibility = Visibility.Collapsed;
            Status("");
        }

        void BtnBukaSql_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = Lang.T("Buka berkas SQL"),
                Filter = Lang.T("Berkas SQL") + " (*.sql)|*.sql|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                var info = new FileInfo(dlg.FileName);
                // Berkas dump bisa ratusan megabyte. Memuatnya ke kotak teks akan
                // menggantung jendela berpuluh detik lalu tetap tidak terbaca -
                // untuk itulah ada tombol Impor, yang menyalurkannya ke mysql.exe
                // tanpa pernah melewati layar.
                if (info.Length > 2 * 1024 * 1024)
                {
                    AppState.Warn(string.Format(CultureInfo.CurrentCulture,
                        Lang.T("Berkas ini {0}, terlalu besar untuk kotak SQL. "
                               + "Pakai tombol Impor .sql untuk menjalankannya."),
                        MySqlSkema.Ukur(info.Length)), Lang.T("Buka berkas SQL"));
                    return;
                }
                TxtSql.Text = File.ReadAllText(dlg.FileName);
                Tab.SelectedIndex = 2;
                Status(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} dimuat ke kotak SQL."), Path.GetFileName(dlg.FileName)));
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        void BtnSimpanSql_Click(object sender, RoutedEventArgs e)
        {
            var teks = TxtSql.Text ?? "";
            if (teks.Trim().Length == 0) { Status(Lang.T("Kotak SQL masih kosong.")); return; }

            var dlg = new SaveFileDialog
            {
                Title = Lang.T("Simpan berkas SQL"),
                FileName = (_dbTerpilih ?? "kueri") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sql",
                Filter = Lang.T("Berkas SQL") + " (*.sql)|*.sql|" + Lang.T("Semua berkas") + " (*.*)|*.*",
                DefaultExt = ".sql",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                File.WriteAllText(dlg.FileName, teks, new UTF8Encoding(false));
                Status(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Disimpan ke {0}."), dlg.FileName));
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        void BtnSalin_Click(object sender, RoutedEventArgs e)
        {
            if (Tab.SelectedIndex == 0)
            {
                if (_halaman == null) { Status(Lang.T("Belum ada hasil untuk disalin.")); return; }
                SalinKePapan(MySqlSunting.Csv(_halaman.Kolom, _halaman.Baris, '\t'));
                return;
            }

            var baris = KisiSql.ItemsSource as IEnumerable<string[]>;
            if (baris == null) { Status(Lang.T("Belum ada hasil untuk disalin.")); return; }

            var sb = new StringBuilder();
            // Judulnya dibalikkan ke nama aslinya: yang disalin orang dipakai lagi
            // di tempat lain, dan "nama__pelanggan" bukan nama kolom mana pun.
            sb.AppendLine(string.Join("\t", KisiSql.Columns
                .Select(c => (c.Header ?? "").ToString().Replace("__", "_")).ToArray()));
            foreach (var b in baris)
                sb.AppendLine(string.Join("\t", b.Select(x => x ?? "").ToArray()));
            SalinKePapan(sb.ToString());
        }

        void SalinKePapan(string teks)
        {
            try
            {
                Clipboard.SetText(teks);
                Status(Lang.T("Disalin ke papan klip."));
            }
            catch (Exception ex) { Status(ex.Message, true); }
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

        async void BtnKosongkan_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk || _tabelTerpilih == null) return;
            var tabel = _tabelTerpilih;

            if (!AppState.Ask(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Buang SEMUA baris dari \"{0}\"? Tabelnya tetap ada, tapi isinya "
                           + "hilang dan tidak bisa dikembalikan."), tabel),
                    Lang.T("Kosongkan tabel")))
                return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            MySqlKlien.Hasil h = null;
            await Task.Run(() => h = MySqlKlien.Jalankan(s,
                "TRUNCATE TABLE " + MySqlKlien.Kutip(tabel), db));
            Sibuk(false);

            if (!h.Ok) { AppState.Warn(h.Galat, Lang.T("Kosongkan tabel")); return; }
            _e.Say(string.Format(CultureInfo.CurrentCulture,
                Lang.T("Tabel \"{0}\" dikosongkan."), tabel));
            await MuatTabelAsync();
            if (Tab.SelectedIndex == 0) await JelajahAsync(true);
        }

        async void BtnHapusTabel_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk || _tabelTerpilih == null) return;
            var tabel = _tabelTerpilih;

            var ketik = InputDialog.Tanya(Window.GetWindow(this),
                Lang.T("Hapus tabel"),
                string.Format(CultureInfo.CurrentCulture,
                    Lang.T("Tabel \"{0}\" berikut seluruh isinya akan hilang dan tidak bisa "
                           + "dikembalikan. Ketik nama tabelnya untuk melanjutkan:"), tabel), "");
            if (ketik == null) return;
            if (ketik.Trim() != tabel)
            {
                AppState.Warn(Lang.T("Nama yang diketik tidak sama. Tidak ada yang dihapus."),
                              Lang.T("Hapus tabel"));
                return;
            }

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            MySqlKlien.Hasil h = null;
            await Task.Run(() => h = MySqlKlien.Jalankan(s,
                "DROP TABLE " + MySqlKlien.Kutip(tabel), db));
            Sibuk(false);

            if (!h.Ok) { AppState.Warn(h.Galat, Lang.T("Hapus tabel")); return; }
            _e.Say(string.Format(CultureInfo.CurrentCulture,
                Lang.T("Tabel \"{0}\" dihapus."), tabel));
            _tabelTerpilih = null;
            BersihkanKanan();
            await MuatTabelAsync();
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
            _bentukTerbaca = false;
            await MuatTabelAsync();
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

        // ---------------------------------------------------------- Klien luar

        /// <summary>
        /// Buka basis data profil ini di HeidiSQL - cara yang dipakai Laragon.
        ///
        /// Tab bawaan dan HeidiSQL menjawab kebutuhan yang berbeda, dan itu bukan
        /// pengulangan: tab bawaan untuk pekerjaan yang harus cepat tanpa
        /// berpindah jendela, HeidiSQL untuk yang memang lebih berat. Phoron
        /// tidak ikut mengirimkan HeidiSQL; ia dipakai kalau sudah ada.
        /// </summary>
        void BtnHeidi_Click(object sender, RoutedEventArgs e)
        {
            var galat = KlienLuar.BukaHeidi(_e);
            if (galat != null) { AppState.Warn(galat, "HeidiSQL"); Status(galat, true); return; }

            var pesan = Lang.T("HeidiSQL dibuka untuk profil ini.");
            // Sandi sengaja tidak ikut di baris perintah - lihat KlienLuar. Kalau
            // memang ada sandinya, orang perlu tahu kenapa HeidiSQL menanyakannya.
            if (!string.IsNullOrEmpty(_e.Settings.DbSandi))
                pesan += " " + Lang.T("Sandinya tidak ikut dikirim, jadi HeidiSQL akan menanyakannya.");
            _e.Say(pesan);
            Status(pesan);
        }

        // ----------------------------------------------------------------- Bantu

        /// <summary>
        /// Tombol yang tidak ada sasarannya dimatikan, bukan dibiarkan hidup lalu
        /// mengeluh saat ditekan. Tombol mati sudah menjelaskan sendiri bahwa
        /// sesuatu perlu dipilih lebih dulu.
        /// </summary>
        void AturTombol()
        {
            bool adaDb = _dbTerpilih != null;
            bool adaTabel = _tabelTerpilih != null;
            bool adaBaris = _halaman != null && _halaman.Baris.Count > 0;
            bool bisaUbah = adaBaris && _kunci.Count > 0;

            BtnHapusDb.IsEnabled = adaDb;
            // Impor dan ekspor bekerja pada BASIS DATA, bukan pada tabel.
            BtnImpor.IsEnabled = adaDb;
            BtnEkspor.IsEnabled = adaDb;

            BtnKosongkan.IsEnabled = adaTabel;
            BtnHapusTabel.IsEnabled = adaTabel;
            BtnStruktur.IsEnabled = adaTabel;

            BtnTambahBaris.IsEnabled = adaTabel;
            BtnHapusBaris.IsEnabled = bisaUbah;
            BtnNull.IsEnabled = bisaUbah;
            BtnSalinInsert.IsEnabled = adaBaris;
            BtnCsv.IsEnabled = adaBaris;
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
            TxtStatus.Text = Ratakan(teks);
            TxtStatus.SetResourceReference(ForegroundProperty,
                galat ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
            TxtStatus.ToolTip = string.IsNullOrEmpty(teks) ? null : teks;
        }
    }
}
