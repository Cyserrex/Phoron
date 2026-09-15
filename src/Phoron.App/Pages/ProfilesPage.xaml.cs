using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class ProfilesPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        Profile _current;
        bool _loading;

        /// <summary>Profil aktif sudah berubah tapi layanan masih memakai yang lama.</summary>
        bool _perluRestart;

        /// <summary>Baris ComboBox versi; entri kosong dipakai untuk "tidak dipakai".</summary>
        class Row
        {
            public BinPackage Pkg;
            public string Text { get; set; }
            public override string ToString() { return Text; }
        }

        public ProfilesPage()
        {
            InitializeComponent();
            IsiComboVersi();
            IsiDaftar(_e.Active);
        }

        void IsiComboVersi()
        {
            CmbPhp.ItemsSource = Rows(BinKind.Php);
            CmbApache.ItemsSource = Rows(BinKind.Apache);
            CmbNginx.ItemsSource = Rows(BinKind.Nginx);
            CmbMysql.ItemsSource = Rows(BinKind.MySql);
        }

        List<Row> Rows(BinKind kind)
        {
            var list = new List<Row> { new Row { Pkg = null, Text = "(tidak dipakai)" } };
            list.AddRange(_e.Of(kind).Select(p => new Row { Pkg = p, Text = LabelPaket(p) }));
            return list;
        }

        /// <summary>
        /// Baris untuk sebuah paket versi.
        ///
        /// Nama folder ditaruh di depan karena itulah yang tersimpan di berkas
        /// profil - memudahkan mencocokkan saat menyunting manual. Nomor versi
        /// dan toolset tidak diulang lagi kalau sudah ada di nama folder itu;
        /// mengulangnya membuat tiap baris panjang dan mirip satu sama lain.
        ///
        /// Folder bin asal SELALU ikut ditulis. Dua folder bin bisa memuat nama
        /// folder yang sama persis - lazim terjadi saat bin Phoron dan bin
        /// Laragon dipakai bersama - dan tanpa penanda ini kedua barisnya tidak
        /// bisa dibedakan sama sekali.
        /// </summary>
        static string LabelPaket(BinPackage p)
        {
            var bagian = new List<string> { p.Id };

            var tambahan = new List<string>();
            if (!string.IsNullOrEmpty(p.Compiler)
                && p.Id.IndexOf(p.Compiler, StringComparison.OrdinalIgnoreCase) < 0)
                tambahan.Add(p.Compiler);
            // Arsitektur ditulis bermacam-macam di nama folder: x64, win64,
            // winx64, Win32. Mencari "x64" harfiah membuat httpd-...-win64-...
            // tetap diberi embel-embel "x64" yang mengulang isi namanya sendiri.
            if (!string.IsNullOrEmpty(p.Arch) && !ArsitekturTersirat(p.Id, p.Arch))
                tambahan.Add(p.Arch);
            if (p.Kind == BinKind.Php) tambahan.Add(p.ThreadSafe ? "TS" : "NTS");
            if (tambahan.Count > 0) bagian.Add(string.Join(" · ", tambahan));

            if (!string.IsNullOrEmpty(p.SourceRoot)) bagian.Add(p.SourceRoot);
            return string.Join("   —   ", bagian);
        }

        static bool ArsitekturTersirat(string id, string arch)
        {
            var l = (id ?? "").ToLowerInvariant();
            if (string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase))
                return l.Contains("x64") || l.Contains("win64") || l.Contains("amd64");
            if (string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase))
                return l.Contains("x86") || l.Contains("win32");
            return false;
        }

        void IsiDaftar(Profile pilih)
        {
            _loading = true;
            Daftar.ItemsSource = null;
            Daftar.ItemsSource = _e.Profiles;
            _loading = false;
            Daftar.SelectedItem = _e.Profiles.FirstOrDefault(p => pilih != null && p.FileName == pilih.FileName)
                                  ?? _e.Profiles.FirstOrDefault();
        }

        void Daftar_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _current = Daftar.SelectedItem as Profile;
            TampilkanProfil(_current);
        }

        void TampilkanProfil(Profile p)
        {
            Editor.IsEnabled = p != null;
            if (p == null) return;
            _loading = true;
            TxtNama.Text = p.Name;
            Pilih(CmbWeb, p.WebServer);
            PilihPkg(CmbPhp, p.PhpId);
            PilihPkg(CmbApache, p.ApacheId);
            PilihPkg(CmbNginx, p.NginxId);
            PilihPkg(CmbMysql, p.MySqlId);
            TxtPortHttp.Text = p.HttpPort.ToString();
            TxtPortHttps.Text = p.HttpsPort.ToString();
            TxtPortMysql.Text = p.MySqlPort.ToString();
            TxtRingkasRoot.Text = RingkasRoot(p);
            TxtSuffix.Text = p.SiteSuffix ?? "test";
            TxtCatatan.Text = p.Notes ?? "";
            _loading = false;
            AturTampilanWeb();
        }

        /// <summary>
        /// Ringkasan folder proyek untuk halaman Profil. Menyebut jumlah DAN
        /// tempat menyuntingnya - ringkasan yang tidak memberi tahu ke mana
        /// harus pergi hanya memindahkan kebingungan, bukan menghilangkannya.
        /// </summary>
        static string RingkasRoot(Profile p)
        {
            var n = p.ProjectRoots.Count;
            var isi = n == 0
                ? "Belum diisi - memakai folder www bawaan Phoron."
                : string.Join(", ", p.ProjectRoots.ToArray());
            return isi + "  (diatur di halaman Situs)";
        }

        static void Pilih(ComboBox box, string tag)
        {
            foreach (ComboBoxItem item in box.Items)
                if ((item.Tag ?? "").ToString() == tag) { box.SelectedItem = item; return; }
            box.SelectedIndex = 0;
        }

        static void PilihPkg(ComboBox box, string id)
        {
            var rows = box.ItemsSource as List<Row>;
            if (rows == null) return;
            box.SelectedItem = rows.FirstOrDefault(r => r.Pkg != null
                                   && string.Equals(r.Pkg.Id, id, StringComparison.OrdinalIgnoreCase))
                               ?? rows[0];
        }

        static string IdDari(ComboBox box)
        {
            var row = box.SelectedItem as Row;
            return row != null && row.Pkg != null ? row.Pkg.Id : "";
        }

        void CmbWeb_Changed(object sender, SelectionChangedEventArgs e) { AturTampilanWeb(); Simpan(); }

        /// <summary>Sembunyikan pilihan yang tidak relevan - profil Nginx tidak memakai Apache dan sebaliknya.</summary>
        void AturTampilanWeb()
        {
            var item = CmbWeb.SelectedItem as ComboBoxItem;
            bool nginx = item != null && (item.Tag ?? "").ToString() == "nginx";
            LblNginx.Visibility = CmbNginx.Visibility = nginx ? Visibility.Visible : Visibility.Collapsed;
            LblApache.Visibility = CmbApache.Visibility = nginx ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ Aksi

        /// <summary>
        /// Pindahkan isi layar ke profil. TIDAK memunculkan dialog: fungsi ini
        /// berjalan setiap kali kendali berubah, dan dialog di tengah ketikan
        /// jauh lebih mengganggu daripada tombol simpan yang tadinya ada.
        /// Keberatan dikembalikan sebagai teks untuk ditempel di layar.
        /// Kolom yang nilainya tidak masuk akal TIDAK ditulis - nilai lamanya
        /// dipertahankan, bukan ditimpa dengan sesuatu yang rusak.
        /// </summary>
        List<string> Kumpulkan(Profile p)
        {
            var masalah = new List<string>();

            if (string.IsNullOrWhiteSpace(TxtNama.Text)) masalah.Add("Nama profil belum diisi.");
            else p.Name = TxtNama.Text.Trim();

            p.HttpPort = Port(TxtPortHttp.Text, p.HttpPort, "HTTP", masalah);
            p.HttpsPort = Port(TxtPortHttps.Text, p.HttpsPort, "HTTPS", masalah);
            p.MySqlPort = Port(TxtPortMysql.Text, p.MySqlPort, "MySQL", masalah);

            var item = CmbWeb.SelectedItem as ComboBoxItem;
            p.WebServer = item != null ? (item.Tag ?? "apache").ToString() : "apache";
            p.PhpId = IdDari(CmbPhp);
            p.ApacheId = IdDari(CmbApache);
            p.NginxId = IdDari(CmbNginx);
            p.MySqlId = IdDari(CmbMysql);
            p.SiteSuffix = string.IsNullOrWhiteSpace(TxtSuffix.Text) ? "test" : TxtSuffix.Text.Trim();
            p.Notes = TxtCatatan.Text;

            var hilang = p.ProjectRoots.Where(r => !System.IO.Directory.Exists(r)).ToList();
            if (hilang.Count > 0)
                masalah.Add("Folder ini belum ada: " + string.Join(", ", hilang));

            var php = _e.Find(BinKind.Php, p.PhpId);
            var apache = _e.Find(BinKind.Apache, p.ApacheId);
            if (p.WebServer == "apache" && php != null && apache != null)
            {
                // Arsitektur lebih dulu, dan nadanya lebih keras: mod_php DIMUAT
                // KE DALAM httpd.exe, jadi beda arsitektur TIDAK PERNAH jalan -
                // bukan "biasanya gagal". Lazim terjadi begitu XAMPP (x86) dan
                // Laragon (x64) dipakai berdampingan, misalnya PHP 5 dari XAMPP
                // dipasangkan dengan Apache milik Laragon.
                if (!ProfileStore.ArsitekturSepadan(php.Arch, apache.Arch))
                    masalah.Add("PHP " + php.Version + " berarsitektur " + php.Arch
                        + " sedangkan Apache " + apache.Version + " berarsitektur " + apache.Arch
                        + ". Kombinasi ini TIDAK AKAN pernah jalan - Apache memuat modul PHP ke dalam "
                        + "dirinya sendiri, jadi keduanya harus sama. Tekan \"Sarankan otomatis\".");
                else if (!string.IsNullOrEmpty(php.Compiler) && !string.IsNullOrEmpty(apache.Compiler)
                    && !string.Equals(php.Compiler, apache.Compiler, StringComparison.OrdinalIgnoreCase))
                    // Bukan penghalang - ada kombinasi yang tetap jalan - tapi ini
                    // penyebab paling umum Apache mati seketika tanpa pesan.
                    masalah.Add("PHP dibangun dengan " + php.Compiler + " sedangkan Apache dengan "
                        + apache.Compiler + ". Kombinasi beda toolset biasanya membuat Apache gagal start.");
            }

            // Runtime Visual C++ tidak ikut dalam arsip PHP maupun Apache. Kalau
            // belum terpasang, Apache gagal start dengan pesan yang menunjuk
            // berkas PHP - jadi disebut di sini, sebelum orang mencoba lalu
            // menghabiskan waktu mencurigai versi PHP-nya.
            foreach (var m in RuntimeVc.PeriksaSemua(php, apache)) masalah.Add(m);
            return masalah;
        }

        static int Port(string teks, int lama, string nama, List<string> masalah)
        {
            int n;
            if (int.TryParse((teks ?? "").Trim(), out n) && n >= 1 && n <= 65535) return n;
            masalah.Add("Port " + nama + " tidak masuk akal, jadi tetap " + lama + ".");
            return lama;
        }

        /// <summary>
        /// Mengisikan Apache dan MySQL yang cocok untuk PHP yang sedang dipilih.
        ///
        /// Pertanyaan yang dijawab tombol ini: "versi mana yang cocok dengan
        /// mana". Jawabannya tidak sepele begitu ada lebih dari satu pengelola -
        /// PHP 5 dari XAMPP (x86) berdampingan dengan Apache milik Laragon (x64),
        /// dan pilihan yang salah membuat Apache mati seketika tanpa pesan.
        /// </summary>
        void BtnSaran_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var phpRow = CmbPhp.SelectedItem as Row;
            var php = phpRow != null ? phpRow.Pkg : null;
            if (php == null)
            {
                AppState.Warn("Pilih dulu versi PHP-nya; sisanya disesuaikan dengan itu.");
                return;
            }

            var apache = ProfileStore.PickApache(php, _e.Of(BinKind.Apache));
            var mysql = ProfileStore.PickMySql(_e.Of(BinKind.MySql));

            var rincian = new List<string>();
            if (apache != null)
            {
                PilihPkg(CmbApache, apache.Id);
                rincian.Add("Apache " + apache.Version + " (" + apache.Arch
                            + (apache.Compiler.Length > 0 ? " " + apache.Compiler : "") + ") dari " + apache.SourceRoot);
            }
            else rincian.Add("Tidak ada Apache yang cocok dengan PHP " + php.Arch + " di komputer ini.");

            if (mysql != null)
            {
                PilihPkg(CmbMysql, mysql.Id);
                rincian.Add("MySQL " + mysql.Version + " dari " + mysql.SourceRoot);
            }

            Simpan();
            AppState.Info("Disesuaikan dengan PHP " + php.Version + " (" + php.Arch + ") dari "
                          + php.SourceRoot + ":" + Environment.NewLine + Environment.NewLine
                          + string.Join(Environment.NewLine, rincian.ToArray()));
        }

        void Kendali_Ubah(object sender, SelectionChangedEventArgs e) { Simpan(); }

        void Teks_Lepas(object sender, RoutedEventArgs e) { Simpan(); }

        /// <summary>
        /// Tulis profil ke berkasnya. Dipanggil sendiri setiap kali ada
        /// perubahan; tidak ada tombol simpan lagi.
        /// </summary>
        Profile Simpan()
        {
            if (_loading || _current == null) return null;

            var masalah = Kumpulkan(_current);
            ProfileStore.Save(_current);

            // _e.Reload() sengaja TIDAK dipanggil: ia membaca ulang seluruh
            // profil dari disk dan mengganti objeknya dengan yang baru, sehingga
            // _current jadi menunjuk objek yatim dan suntingan berikutnya masuk
            // ke tempat yang salah. Nama di daftar cukup digambar ulang.
            Daftar.Items.Refresh();
            AppState.RaiseChanged();

            // Profil yang sedang dipakai layanan: berkasnya sudah berubah, tapi
            // Apache/MySQL masih berjalan dengan yang lama.
            if (_e.Active != null && _e.Active.FileName == _current.FileName
                && _e.Services.WebState == ServiceState.Jalan)
                _perluRestart = true;

            TxtStatusSimpan.Text = "Tersimpan " + DateTime.Now.ToString("HH:mm:ss")
                + " ke profiles\\" + _current.FileName + ".ini";
            TampilkanMasalah(masalah);
            SegarkanBilah();
            return _current;
        }

        void TampilkanMasalah(List<string> masalah)
        {
            PanelMasalah.Visibility = masalah.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (masalah.Count > 0) TxtMasalah.Text = string.Join(Environment.NewLine, masalah);
        }

        void SegarkanBilah()
        {
            bool jalan = _e.Services.WebState == ServiceState.Jalan;
            PanelRestart.Visibility = _perluRestart && jalan ? Visibility.Visible : Visibility.Collapsed;
            if (PanelRestart.Visibility == Visibility.Visible)
                TxtRestart.Text = "Profil yang sedang dipakai sudah berubah, tapi web server membaca "
                    + "konfigurasinya HANYA saat start - jadi perubahan ini belum berlaku di browser.";
        }

        async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            Simpan();
            // Peringatannya sudah masuk Aktivitas lewat Engine.Apply, berwarna
            // dan tinggal di sana. Kotak dialog di atasnya cuma menggandakan
            // hal yang sama sambil menghalangi jalan.
            _e.Apply();
            var main = Window.GetWindow(this) as MainWindow;
            await _e.StopWebAsync();
            await _e.StartWebAsync();
            if (main != null) main.RefreshStatus();
            _perluRestart = false;
            SegarkanBilah();
        }

        async void BtnSwitch_Click(object sender, RoutedEventArgs e)
        {
            var p = Simpan();
            if (p == null) return;
            var target = _e.Profiles.FirstOrDefault(x => x.FileName == p.FileName);
            if (target == null) return;
            var warnings = await _e.SwitchAsync(target);
            AppState.RaiseChanged();
            var main = Window.GetWindow(this) as MainWindow;
            if (main != null) main.RefreshStatus();
            _perluRestart = false;
            SegarkanBilah();
            AppState.Info("Sekarang memakai profil \"" + target.Name + "\".");
        }

        void BtnBaru_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Of(BinKind.Php).FirstOrDefault();
            var p = new Profile { Name = "Profil baru" };
            if (php != null)
            {
                p.PhpId = php.Id;
                var apache = ProfileStore.PickApache(php, _e.Of(BinKind.Apache));
                if (apache != null) p.ApacheId = apache.Id;
                // Sama seperti profil bawaan: lahir dengan daftar ekstensi yang
                // masuk akal, bukan kosong.
                p.PhpExtensions = ConfigWriter.EkstensiDisarankan(php);
            }
            var db = _e.Of(BinKind.MySql).FirstOrDefault();
            if (db != null) p.MySqlId = db.Id;
            p.FileName = ProfileStore.UniqueFileName(p.Name);
            ProfileStore.Save(p);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(p);
        }

        void BtnDuplikat_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var copy = _current.Clone();
            copy.Name = _current.Name + " (salinan)";
            copy.FileName = ProfileStore.UniqueFileName(copy.Name);
            ProfileStore.Save(copy);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(copy);
        }

        void BtnHapus_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            if (_e.Profiles.Count <= 1) { AppState.Warn("Sisakan minimal satu profil."); return; }
            if (!AppState.Ask("Hapus profil \"" + _current.Name + "\"?")) return;
            ProfileStore.Delete(_current);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(_e.Active);
        }

    }
}
