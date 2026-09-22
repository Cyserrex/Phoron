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
using Microsoft.Win32;
using Phoron.Core;

namespace Phoron.App.Pages
{
    /// <summary>
    /// Penjelajah basis data: basis data, tabel, isi tabel berhalaman, struktur,
    /// dan SQL bebas.
    ///
    /// SELURUH PEKERJAANNYA LEWAT PROSES LUAR - mysql.exe milik profil aktif -
    /// dan setiap pemanggilan MEMBLOKIR utas pemanggilnya sampai proses itu
    /// selesai. Karena itu tidak satu pun panggilan MySqlKlien atau MySqlSkema
    /// boleh terjadi di utas layar: kueri yang makan tiga detik akan membekukan
    /// seluruh jendela Phoron selama tiga detik, termasuk tombol berhenti untuk
    /// Apache.
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
        bool _strukturTerbaca;      // struktur dimuat saat tabnya dibuka, bukan lebih awal

        int _offset;
        int _perHalaman = 100;
        long _total = -1;
        string _urutKolom;
        bool _menurun;

        readonly List<string> _riwayat = new List<string>();

        bool _sibuk;
        bool _mengisi;

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

        /// <summary>
        /// Apa yang menghalangi halaman ini bekerja, atau null bila tidak ada.
        /// Tiap sebab punya kalimatnya sendiri - "tidak bisa menyambung" yang
        /// sama untuk lima sebab berbeda tidak menolong siapa pun.
        /// </summary>
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
            // Tabelnya dimuat lewat pemanggilan LANGSUNG, bukan dengan menyandarkan
            // diri pada peristiwa pemilihan: menyetel SelectedItem ke butir yang
            // memang sudah terpilih tidak menyalakan peristiwa apa pun, jadi
            // daftar tabelnya tidak akan pernah terisi saat menyegarkan.
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
            // menyalakan DaftarDb_Pilih seolah orang baru saja mengklik basis data
            // itu - dan penangan itu membuang tabel yang sedang dipilih lalu
            // memuat ulang daftarnya. Akibatnya mengetik satu huruf di kotak cari
            // memuat ulang seluruh daftar tabel, dan pilihan di kanan lenyap.
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
            _tabelTerpilih = t != null ? t.Nama : null;
            _strukturTerbaca = false;
            AturTombol();
        }

        /// <summary>
        /// Jalan pintas ke tab Struktur. Ada karena klik ganda membuka isi, dan
        /// tanpa tombol ini satu-satunya cara melihat bentuk tabel adalah
        /// menemukan sendiri bahwa tabnya berpindah mengikuti tabel terpilih.
        /// </summary>
        void BtnStruktur_Click(object sender, RoutedEventArgs e)
        {
            if (_tabelTerpilih == null) { Status(Lang.T("Pilih tabelnya dulu.")); return; }
            Tab.SelectedIndex = 1;
        }

        void DaftarTabel_KlikGanda(object sender, MouseButtonEventArgs e)
        {
            var t = DaftarTabel.SelectedItem as MySqlSkema.InfoTabel;
            if (t == null) return;
            _tabelTerpilih = t.Nama;
            Tab.SelectedIndex = 0;
            MulaiJelajah();
        }

        async void Tab_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || e.OriginalSource != Tab) return;
            // Struktur dibaca saat tabnya dibuka, bukan tiap kali tabel dipilih:
            // dua kueri tambahan di tiap klik daftar membuat penelusuran terasa
            // tersendat, dan sebagian besar klik memang cuma ingin melihat isi.
            if (Tab.SelectedIndex == 1 && _tabelTerpilih != null && !_strukturTerbaca)
                await MuatStrukturAsync();
        }

        // ------------------------------------------------------------- Jelajah

        void MulaiJelajah()
        {
            _offset = 0;
            _total = -1;
            _urutKolom = null;
            _menurun = false;
            JelajahAsync(true);
        }

        async void JelajahAsync(bool hitungTotal)
        {
            if (_sibuk || _tabelTerpilih == null) return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            int offset = _offset, batas = _perHalaman;
            string urut = _urutKolom;
            bool turun = _menurun;
            MySqlSkema.Halaman hal = null;
            await Task.Run(() => hal = MySqlSkema.Isi(s, db, tabel, offset, batas,
                                                      urut, turun, hitungTotal));
            Sibuk(false);

            LblJelajah.Text = tabel + (urut != null
                ? "  ·  " + Lang.T("urut") + " " + urut + (turun ? " ↓" : " ↑") : "");

            if (!hal.Ok)
            {
                IsiKisi(Kisi, null);
                TxtKisiHampa.Text = hal.Galat;
                TxtKisiHampa.Visibility = Visibility.Visible;
                Status(hal.Galat, true);
                return;
            }

            if (hitungTotal) _total = hal.Total;
            IsiKisi(Kisi, hal.Tabel);
            TxtKisiHampa.Text = Lang.T("Tabel ini tidak punya baris.");
            TxtKisiHampa.Visibility = hal.Tabel.Baris.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

            AturBilahHalaman(hal.Tabel.Baris.Count);
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0} baris terbaca dalam {1} ms."), hal.Tabel.Baris.Count, hal.Ms));
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

        void BtnAwal_Click(object sender, RoutedEventArgs e)
        {
            if (_offset == 0) return;
            _offset = 0;
            JelajahAsync(false);
        }

        void BtnMundur_Click(object sender, RoutedEventArgs e)
        {
            if (_offset == 0) return;
            _offset = Math.Max(0, _offset - _perHalaman);
            JelajahAsync(false);
        }

        void BtnMaju_Click(object sender, RoutedEventArgs e)
        {
            _offset += _perHalaman;
            JelajahAsync(false);
        }

        void CmbPerHalaman_Ubah(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            var item = CmbPerHalaman.SelectedItem as ComboBoxItem;
            int n;
            if (item == null || !int.TryParse((item.Content ?? "").ToString(), out n)) return;
            _perHalaman = n;
            _offset = 0;
            if (_tabelTerpilih != null) JelajahAsync(false);
        }

        /// <summary>
        /// Pengurutan dikerjakan SERVER, bukan kisi.
        ///
        /// Kisi hanya memegang satu halaman, jadi mengurutkannya sendiri berarti
        /// mengurutkan seratus baris yang kebetulan sedang terlihat - dan itu
        /// menampilkan "nilai terbesar" yang bukan nilai terbesar di tabelnya.
        /// Salah yang tampak benar, dan itu jenis kesalahan yang paling mahal.
        /// </summary>
        void Kisi_Urut(object sender, DataGridSortingEventArgs e)
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
            JelajahAsync(false);
        }

        // ------------------------------------------------------------- Struktur

        async Task MuatStrukturAsync()
        {
            if (_tabelTerpilih == null) return;

            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            var tabel = _tabelTerpilih;
            string g1 = null, g2 = null;
            List<MySqlSkema.InfoKolom> kolom = null;
            List<MySqlSkema.InfoIndeks> indeks = null;
            await Task.Run(() =>
            {
                kolom = MySqlSkema.Struktur(s, db, tabel, out g1);
                indeks = MySqlSkema.Indeks(s, db, tabel, out g2);
            });
            Sibuk(false);

            KisiKolom.ItemsSource = kolom;
            KisiIndeks.ItemsSource = indeks;
            _strukturTerbaca = true;

            if (g1 != null) { Status(g1, true); return; }
            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0}: {1} kolom, {2} indeks."), tabel, kolom.Count, indeks.Count));
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
            var ringkas = new List<string>();
            MySqlKlien.Hasil terakhirBerbaris = null;
            string galat = null;
            long totalMs = 0;
            bool skemaBerubah = false;

            await Task.Run(() =>
            {
                foreach (var p in perintah)
                {
                    var h = MySqlKlien.Jalankan(s, p, db, 0);
                    totalMs += h.Ms;
                    if (!h.Ok) { galat = h.Galat; break; }
                    if (h.Tabel != null) terakhirBerbaris = h;
                    else if (h.Terpengaruh >= 0)
                        ringkas.Add(string.Format(CultureInfo.CurrentCulture,
                            Lang.T("{0} baris terpengaruh"), h.Terpengaruh));
                    if (MengubahSkema(p)) skemaBerubah = true;
                }
            });
            Sibuk(false);

            IngatRiwayat(teks);

            if (galat != null)
            {
                IsiKisi(KisiSql, null);
                TxtSqlHampa.Text = galat;
                TxtSqlHampa.Visibility = Visibility.Visible;
                Status(galat, true);
                return;
            }

            IsiKisi(KisiSql, terakhirBerbaris != null ? terakhirBerbaris.Tabel : null);
            TxtSqlHampa.Text = terakhirBerbaris == null
                ? Lang.T("Perintah ini tidak mengembalikan baris.")
                : Lang.T("Hasilnya kosong.");
            TxtSqlHampa.Visibility = terakhirBerbaris == null
                                     || terakhirBerbaris.Tabel.Baris.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

            var pesan = new List<string>();
            if (terakhirBerbaris != null)
                pesan.Add(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} baris"), terakhirBerbaris.Tabel.Baris.Count));
            pesan.AddRange(ringkas);
            pesan.Add(totalMs + " ms");
            Status(string.Join(" · ", pesan.ToArray()));

            // Daftar tabel ikut disegarkan hanya bila memang ada yang mengubahnya.
            // Menyegarkannya setelah SETIAP kueri berarti dua pemanggilan proses
            // tambahan untuk tiap SELECT, dan itu terasa di setiap klik.
            if (skemaBerubah) { await MuatTabelAsync(); _strukturTerbaca = false; }
        }

        static bool MengubahSkema(string sql)
        {
            var kata = new string((sql ?? "").TrimStart().TakeWhile(char.IsLetter).ToArray())
                       .ToUpperInvariant();
            return kata == "CREATE" || kata == "DROP" || kata == "ALTER"
                   || kata == "RENAME" || kata == "TRUNCATE";
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
                .Select(x => x.Replace("\r", " ").Replace("\n", " "))
                .Select(x => x.Length > 60 ? x.Substring(0, 60) + "..." : x)
                .ToList();
            CmbRiwayat.SelectedIndex = -1;
            _mengisi = false;
            LblSql.Text = string.Format(CultureInfo.CurrentCulture,
                Lang.T("SQL untuk \"{0}\""), _dbTerpilih ?? Lang.T("(tanpa basis data)"));
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
            IsiKisi(KisiSql, null);
            TxtSqlHampa.Visibility = Visibility.Collapsed;
            Status("");
        }

        void BtnSalin_Click(object sender, RoutedEventArgs e)
        {
            var kisi = Tab.SelectedIndex == 0 ? Kisi : KisiSql;
            var baris = kisi.ItemsSource as IEnumerable<string[]>;
            if (baris == null) { Status(Lang.T("Belum ada hasil untuk disalin.")); return; }

            var sb = new StringBuilder();
            // Judulnya dibalikkan ke nama aslinya: yang disalin orang dipakai
            // lagi di tempat lain, dan "nama__pelanggan" bukan nama kolom mana pun.
            sb.AppendLine(string.Join("\t", kisi.Columns
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

        // -------------------------------------------------------- Tampilan kisi

        /// <summary>
        /// Bangun kolom kisi dari hasil kueri. Kolomnya dibuat di kode karena
        /// bentuk hasil baru diketahui saat kueri selesai - tidak ada bentuk
        /// tetap yang bisa ditulis di XAML.
        /// </summary>
        void IsiKisi(DataGrid kisi, MySqlKlien.Tabel t)
        {
            kisi.ItemsSource = null;
            kisi.Columns.Clear();
            if (t == null) return;

            for (int i = 0; i < t.Kolom.Count; i++)
            {
                // Nilainya ditampilkan APA ADANYA. Sempat ada niat memiringkan
                // NULL supaya beda dari teks biasa, tapi klien MySQL menulis kata
                // yang sama untuk nilai kosong dan untuk teks berisi kata "NULL" -
                // diperiksa langsung ke MySQL 5.7 di mesin ini. Gaya yang
                // membedakan keduanya berarti menebak, dan tebakan yang meleset di
                // sini memberi tahu orang bahwa kolomnya kosong padahal berisi.
                //
                // GARIS BAWAH DI NAMA KOLOM DIGANDAKAN, dan ini sudah dua kali
                // salah sebelum benar. Judul kisi memang MEMAKAN satu garis bawah
                // sebagai penanda tombol akses: nama_pelanggan tampil jadi
                // namapelanggan. Sempat disimpulkan sebaliknya karena yang dibaca
                // properti Text milik TextBlock di dalam judulnya - properti itu
                // memang tidak berubah; pemakanannya terjadi saat digambar. Yang
                // memutuskan akhirnya gambar hasil render, bukan pembacaan
                // properti. Nama berkolom garis bawah adalah kebiasaan paling
                // umum di basis data, jadi ini terlihat di hampir tiap tabel.
                kisi.Columns.Add(new DataGridTextColumn
                {
                    Header = t.Kolom[i].Replace("_", "__"),
                    Binding = new Binding("[" + i + "]"),
                    MaxWidth = 420,
                });
            }
            kisi.ItemsSource = t.Baris;
        }

        void BersihkanKanan()
        {
            IsiKisi(Kisi, null);
            KisiKolom.ItemsSource = null;
            KisiIndeks.ItemsSource = null;
            _strukturTerbaca = false;
            _total = -1;
            _offset = 0;
            _urutKolom = null;
            LblJelajah.Text = "";
            TxtHalaman.Text = "";
            TxtKisiHampa.Text = Lang.T("Pilih sebuah tabel, atau klik gandanya.");
            TxtKisiHampa.Visibility = Visibility.Visible;
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
            if (Tab.SelectedIndex == 0) MulaiJelajah();
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
            BtnHapusDb.IsEnabled = adaDb;
            // Impor dan ekspor bekerja pada BASIS DATA, bukan pada tabel.
            BtnImpor.IsEnabled = adaDb;
            BtnEkspor.IsEnabled = adaDb;
            BtnKosongkan.IsEnabled = adaTabel;
            BtnHapusTabel.IsEnabled = adaTabel;
            BtnStruktur.IsEnabled = adaTabel;
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
            TxtStatus.Text = teks ?? "";
            TxtStatus.SetResourceReference(ForegroundProperty,
                galat ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
            TxtStatus.ToolTip = string.IsNullOrEmpty(teks) ? null : teks;
        }
    }
}
