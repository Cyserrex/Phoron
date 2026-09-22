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
    /// Penjelajah basis data. Menampilkan basis data, tabel, isi tabel, dan
    /// menerima SQL bebas.
    ///
    /// SELURUH PEKERJAANNYA LEWAT PROSES LUAR - mysql.exe milik profil aktif -
    /// dan itu berarti setiap pemanggilan MEMBLOKIR utas pemanggilnya sampai
    /// proses itu selesai. Karena itu tidak satu pun panggilan MySqlKlien di
    /// halaman ini boleh terjadi di utas layar: kueri yang makan tiga detik
    /// akan membekukan seluruh jendela Phoron selama tiga detik, termasuk
    /// tombol berhenti untuk Apache.
    /// </summary>
    public partial class DatabasePage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        /// <summary>Berapa baris yang ditarik ke layar untuk sekali lihat.</summary>
        const int BatasBaris = 500;

        MySqlKlien.Sambungan _sambungan;
        string _dbTerpilih;
        bool _sibuk;
        bool _mengisi;

        /// <summary>Baris daftar tabel. Properti, bukan medan - WPF hanya mengikat ke properti.</summary>
        public class BarisTabel
        {
            public string Nama { get; set; }
            public string Jenis { get; set; }
            public long Baris { get; set; }
            public string BarisTampil
            {
                get
                {
                    return Baris < 0 ? "-" : Baris.ToString("N0", CultureInfo.CurrentCulture);
                }
            }
        }

        public DatabasePage()
        {
            InitializeComponent();
            TxtInfo.Text = Lang.T("Menjelajahi basis data MySQL profil yang sedang aktif. "
                                  + "Klik ganda sebuah tabel untuk melihat isinya.");
            Loaded += (s, e) => MuatAsync();
        }

        // ------------------------------------------------------------- Penghalang

        /// <summary>
        /// Apa yang menghalangi halaman ini bekerja, atau null bila tidak ada.
        /// Dipisah dari tampilannya supaya tiap sebab punya kalimat sendiri -
        /// "tidak bisa menyambung" yang sama untuk lima sebab berbeda tidak
        /// menolong siapa pun.
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
            string galat = null;
            List<string> daftar = null;
            await Task.Run(() => { daftar = MySqlKlien.DaftarBasisData(s, out galat); });
            Sibuk(false);

            if (galat != null)
            {
                // Sambungan yang ditolak bukan keadaan "kosong" - kalau daftarnya
                // sekadar dikosongkan, layar ini berbohong bahwa tidak ada basis
                // data sama sekali.
                TampilkanHalangan(Lang.T("Tidak bisa menyambung ke MySQL."),
                                  galat + Environment.NewLine + Environment.NewLine
                                  + Lang.T("Pengguna dan sandi bisa diubah di Pengaturan."),
                                  false, false);
                return;
            }

            var sebelumnya = _dbTerpilih;
            _mengisi = true;
            DaftarDb.ItemsSource = daftar;
            _mengisi = false;

            if (daftar.Count == 0)
            {
                DaftarTabel.ItemsSource = null;
                LblTabel.Text = Lang.T("Tabel");
                Status(Lang.T("Belum ada basis data. Tekan Buat untuk membuatnya."));
                return;
            }
            // Pilihan sebelumnya dipertahankan: menyegarkan daftar setelah
            // menjalankan sesuatu tidak boleh melemparkan orang kembali ke awal.
            DaftarDb.SelectedItem = sebelumnya != null && daftar.Contains(sebelumnya)
                ? sebelumnya : daftar[0];
        }

        async void DaftarDb_Pilih(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            _dbTerpilih = DaftarDb.SelectedItem as string;
            await MuatTabelAsync();
        }

        async Task MuatTabelAsync()
        {
            if (_dbTerpilih == null) { DaftarTabel.ItemsSource = null; return; }

            LblTabel.Text = Lang.T("Tabel") + " - " + _dbTerpilih;
            Sibuk(true);
            var s = _sambungan;
            var db = _dbTerpilih;
            string galat = null;
            List<MySqlKlien.InfoTabel> daftar = null;
            await Task.Run(() => { daftar = MySqlKlien.DaftarTabel(s, db, out galat); });
            Sibuk(false);

            if (galat != null) { DaftarTabel.ItemsSource = null; Status(galat, true); return; }

            DaftarTabel.ItemsSource = daftar.Select(t => new BarisTabel
            {
                Nama = t.Nama,
                Jenis = t.Jenis,
                Baris = t.Baris,
            }).ToList();

            Status(string.Format(CultureInfo.CurrentCulture,
                Lang.T("{0}: {1} tabel."), _dbTerpilih, daftar.Count));
        }

        void DaftarTabel_KlikGanda(object sender, MouseButtonEventArgs e)
        {
            var baris = DaftarTabel.SelectedItem as BarisTabel;
            if (baris == null) return;
            TxtSql.Text = "SELECT * FROM " + MySqlKlien.Kutip(baris.Nama)
                          + " LIMIT " + BatasBaris + ";";
            JalankanAsync();
        }

        // -------------------------------------------------------------- Menjalankan

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
                    var h = MySqlKlien.Jalankan(s, p, db, BatasBaris);
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

            if (galat != null)
            {
                TampilkanTabel(null);
                Status(galat, true);
                return;
            }

            TampilkanTabel(terakhirBerbaris != null ? terakhirBerbaris.Tabel : null);

            var pesan = new List<string>();
            if (terakhirBerbaris != null)
            {
                pesan.Add(string.Format(CultureInfo.CurrentCulture,
                    Lang.T("{0} baris"), terakhirBerbaris.Tabel.Baris.Count));
                if (terakhirBerbaris.Dipotong)
                    pesan.Add(string.Format(CultureInfo.CurrentCulture,
                        Lang.T("ditampilkan {0} pertama saja"), BatasBaris));
            }
            pesan.AddRange(ringkas);
            pesan.Add(totalMs + " ms");
            Status(string.Join(" - ", pesan.ToArray()));

            // Daftar tabel ikut disegarkan hanya bila memang ada yang mengubahnya.
            // Menyegarkannya setelah SETIAP kueri berarti satu pemanggilan proses
            // tambahan untuk tiap SELECT, dan itu terasa di setiap klik.
            if (skemaBerubah) await MuatTabelAsync();
        }

        static bool MengubahSkema(string sql)
        {
            var kata = new string((sql ?? "").TrimStart().TakeWhile(char.IsLetter).ToArray())
                       .ToUpperInvariant();
            return kata == "CREATE" || kata == "DROP" || kata == "ALTER"
                   || kata == "RENAME" || kata == "TRUNCATE";
        }

        // ------------------------------------------------------------- Tampilan hasil

        /// <summary>
        /// Bangun kolom kisi dari hasil kueri. Kolomnya dibuat di kode karena
        /// bentuk hasil baru diketahui saat kueri selesai - tidak ada bentuk
        /// tetap yang bisa ditulis di XAML.
        /// </summary>
        void TampilkanTabel(MySqlKlien.Tabel t)
        {
            Kisi.ItemsSource = null;
            Kisi.Columns.Clear();
            if (t == null) return;

            for (int i = 0; i < t.Kolom.Count; i++)
            {
                // Nilainya ditampilkan APA ADANYA. Sempat ada niat memiringkan
                // NULL supaya beda dari teks biasa, tapi klien MySQL menulis kata
                // yang sama untuk nilai kosong dan untuk teks berisi kata "NULL" -
                // diperiksa langsung ke MySQL 5.7 di mesin ini. Gaya yang
                // membedakan keduanya berarti menebak, dan tebakan yang meleset di
                // sini memberi tahu orang bahwa kolomnya kosong padahal berisi.
                Kisi.Columns.Add(new DataGridTextColumn
                {
                    // Nama kolom dipasang APA ADANYA. Sempat digandakan garis
                    // bawahnya ("nama_pelanggan" -> "nama__pelanggan") karena
                    // dikira judul kisi membacanya sebagai penanda tombol akses.
                    // Diperiksa ke pohon visual: judulnya dirender lewat TextBlock
                    // biasa, bukan AccessText, jadi penggandaan itu justru TAMPIL -
                    // dan nama berkolom garis bawah adalah kebiasaan paling umum
                    // di basis data, jadi cacatnya akan terlihat di hampir tiap tabel.
                    Header = t.Kolom[i],
                    Binding = new Binding("[" + i + "]"),
                    MaxWidth = 400,
                });
            }
            Kisi.ItemsSource = t.Baris;
        }

        void BtnBersih_Click(object sender, RoutedEventArgs e)
        {
            TxtSql.Clear();
            TampilkanTabel(null);
            Status("");
        }

        void BtnSalin_Click(object sender, RoutedEventArgs e)
        {
            var baris = Kisi.ItemsSource as IEnumerable<string[]>;
            if (baris == null) { Status(Lang.T("Belum ada hasil untuk disalin.")); return; }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", Kisi.Columns
                .Select(c => (c.Header ?? "").ToString()).ToArray()));
            foreach (var b in baris)
                sb.AppendLine(string.Join("\t", b.Select(x => x ?? "").ToArray()));
            try
            {
                Clipboard.SetText(sb.ToString());
                Status(Lang.T("Hasil disalin ke papan klip."));
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        // ------------------------------------------------------- Buat dan hapus

        async void BtnBuatDb_Click(object sender, RoutedEventArgs e)
        {
            if (_sibuk) return;
            var nama = InputDialog.Tanya(Window.GetWindow(this),
                Lang.T("Buat basis data"),
                Lang.T("Nama basis data baru:"), "");
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
            var nama = DaftarDb.SelectedItem as string;
            if (nama == null) return;

            // Nama basis datanya disebut di pertanyaan, dan jawabannya bukan Ya
            // melainkan mengetik namanya. DROP DATABASE tidak bisa dibatalkan dan
            // tidak menyisakan apa pun - konfirmasi yang bisa dilewati dengan satu
            // klik refleks terlalu murah untuk perbuatan yang semahal itu.
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

            var ukuran = 0L;
            try { ukuran = new FileInfo(tujuan).Length; } catch { }
            var pesan = string.Format(CultureInfo.CurrentCulture,
                Lang.T("\"{0}\" diekspor ke {1} ({2})."), db, tujuan, Ukuran(ukuran));
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

        static string Ukuran(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= 1024 * 1024)
                return (bytes / 1024.0 / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
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

        void Sibuk(bool sibuk)
        {
            _sibuk = sibuk;
            PanelIsi.IsEnabled = !sibuk;
            Mouse.OverrideCursor = sibuk ? Cursors.Wait : null;
        }

        void Status(string teks, bool galat = false)
        {
            TxtStatus.Text = teks ?? "";
            TxtStatus.SetResourceReference(ForegroundProperty,
                galat ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
        }
    }
}
