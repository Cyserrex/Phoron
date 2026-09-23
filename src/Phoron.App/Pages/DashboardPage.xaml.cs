using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Threading;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class DashboardPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        bool _loading;

        public DashboardPage()
        {
            InitializeComponent();
            _e.Log += OnLog;
            Unloaded += (s, ev) => _e.Log -= OnLog;
            // Riwayat yang sudah ada digambar seketika. Halaman ini dibuat ulang
            // tiap kali navigasi berpindah, jadi tanpa ini panel Aktivitas selalu
            // tampak kosong sepulang dari tab lain.
            GambarLog();
            LoadProfiles();
            RefreshState();
        }

        int _logTertunda;

        /// <summary>
        /// Barisnya sudah dicatat Engine sebelum pendengar ini dipanggil; di
        /// sini tinggal menggambar ulang. Kotak ini untuk melihat sekilas -
        /// riwayat lengkapnya ada di halaman Log.
        ///
        /// DUA HAL YANG DULU SALAH DI SATU BARIS INI.
        ///
        /// Pertama, Invoke MEMBLOKIR pemanggilnya, dan pemanggilnya adalah utas
        /// yang membaca keluaran httpd dan mysqld. Selama panel ini menggambar,
        /// utas itu berhenti membaca - dan kalau utas layar sedang sibuk, ia
        /// berhenti lebih lama lagi. BeginInvoke tidak menunggu siapa pun.
        ///
        /// Kedua, tiap baris memicu satu penggambaran ulang penuh. mysqld
        /// mencetak belasan baris beruntun tiap kali menyala, masing-masing
        /// menggambar ulang dua ratus paragraf yang hampir seluruhnya sama -
        /// dan hanya gambar yang terakhir yang sempat dilihat mata. Sekarang
        /// permintaan yang datang selagi satu penggambaran masih mengantre
        /// ikut menumpang padanya, jadi satu semburan cukup sekali gambar.
        /// </summary>
        void OnLog(string text)
        {
            // Sudah ada yang mengantre - biarkan ia yang menggambar.
            if (System.Threading.Interlocked.Exchange(ref _logTertunda, 1) == 1) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    // Dilepas SEBELUM menggambar: baris yang datang di tengah
                    // penggambaran harus bisa memesan giliran berikutnya, bukan
                    // hilang diam-diam.
                    System.Threading.Interlocked.Exchange(ref _logTertunda, 0);
                    GambarLog();
                }));
        }

        /// <summary>
        /// Menggambar ulang seluruh panel Aktivitas dengan warna per baris.
        ///
        /// Digambar ulang seluruhnya, bukan ditambah satu paragraf: antriannya
        /// dibatasi 200 baris, jadi yang tertua harus ikut hilang dari layar -
        /// dan menyelaraskan dokumen dengan antrian jauh lebih mudah dipercaya
        /// daripada menambah di bawah sambil membuang di atas.
        /// </summary>
        void GambarLog()
        {
            // FlowDocument yang dibuat lewat kode TIDAK mewarisi font dari
            // RichTextBox-nya, dan perataan bawaannya Justify - itu yang bikin
            // hurufnya membesar dan barisnya melar merenggang. Ketiganya
            // disetel tegas supaya panel ini tetap terlihat seperti keluaran
            // terminal, persis seperti sebelum diwarnai.
            var dok = new FlowDocument
            {
                PagePadding = new Thickness(4, 2, 4, 2),
                FontFamily = TxtLog.FontFamily,
                FontSize = TxtLog.FontSize,
                TextAlignment = TextAlignment.Left,
            };
            foreach (var baris in _e.Riwayat())
            {
                var par = new Paragraph { Margin = new Thickness(0) };

                // Jam dipisah dan diredupkan: ia berulang di setiap baris, jadi
                // menuntut perhatian yang sama dengan isinya justru mengaburkan
                // mana yang penting. Waktunya diambil dari saat baris itu DICATAT,
                // bukan saat digambar - kalau tidak, seluruh riwayat akan tampak
                // terjadi bersamaan setiap kali halaman ini dibuka.
                var jam = baris.Waktu.ToString("HH:mm:ss") + "  ";
                var isi = baris.Teks;

                if (jam.Length > 0)
                    par.Inlines.Add(new Run(jam) { Foreground = KuasLog.Jam });

                var run = new Run(isi);
                // Kuasnya diambil dari daftar yang sudah beku, bukan dirakit
                // ulang per baris - lihat KuasLog.
                var jenis = LogWarna.Golongkan(isi);
                var kuas = KuasLog.Untuk(jenis);
                if (kuas != null)
                {
                    run.Foreground = kuas;
                    if (KuasLog.Tebal(jenis)) run.FontWeight = FontWeights.SemiBold;
                }
                par.Inlines.Add(run);
                dok.Blocks.Add(par);
            }
            TxtLog.Document = dok;
            GulungKeBawah();
        }

        /// <summary>
        /// Selalu perlihatkan baris terbaru.
        ///
        /// Memanggil ScrollToEnd sekali saja tidak cukup. GambarLog()
        /// pertama kali jalan dari konstruktor, saat kotak ini belum ditata
        /// sama sekali - belum ada tinggi, belum ada yang bisa digulung - jadi
        /// panggilan itu tidak berbuat apa-apa dan panel diam di baris TERTUA.
        /// Justru itulah keadaan yang paling sering terlihat: halaman ini
        /// dibuat ulang setiap kali navigasi berpindah, jadi tiap kali kembali
        /// ke Beranda seluruh riwayat tampil dari awal, bukan dari ujungnya.
        ///
        /// Karena itu gulungannya diulang sekali lagi SETELAH tata letak
        /// selesai. Yang pertama melayani hal yang lazim - satu baris baru
        /// masuk ke panel yang sudah terbuka; yang kedua melayani halaman yang
        /// baru saja dibuat.
        /// </summary>
        void GulungKeBawah()
        {
            TxtLog.ScrollToEnd();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                                   new Action(() => TxtLog.ScrollToEnd()));
        }

        void LoadProfiles()
        {
            _loading = true;
            CmbProfil.ItemsSource = _e.Profiles;
            CmbProfil.DisplayMemberPath = "Name";
            CmbProfil.SelectedItem = _e.Active;
            _loading = false;
        }

        public void RefreshState()
        {
            var php = _e.Php;
            var web = _e.WebPackage;
            var db = _e.MySql;
            var p = _e.Active;

            TxtPhp.Text = php != null ? php.Version : "-";
            TxtPhpSub.Text = php != null ? php.Label : "belum dipilih di profil";
            TxtWebKind.Text = p != null && p.WebServer == "nginx" ? "Nginx" : "Apache";
            TxtWeb.Text = web != null ? web.Version : "-";
            // Status HTTPS ikut ditulis: tanpa sertifikat, port 443 tidak dibuka
            // sama sekali, dan browser hanya menjawab "tidak dapat tersambung"
            // tanpa menyebutkan sebabnya di mana pun.
            var https = SslTool.Exists
                ? " · https " + (p != null ? p.HttpsPort.ToString() : "?")
                : " · https mati";
            TxtWebSub.Text = web != null
                ? web.Label + " · port " + (p != null ? p.HttpPort.ToString() : "?") + https
                : "belum dipilih di profil";
            TxtDb.Text = db != null ? db.Version : "-";
            TxtDbSub.Text = db != null
                ? db.Label + " · port " + (p != null ? p.MySqlPort.ToString() : "?")
                : "belum dipilih di profil";

            TitikKartu();

            BtnSsl.Content = !SslTool.Exists ? "Buat sertifikat SSL"
                           : !SslTool.IsTrusted ? "Percayai sertifikat SSL"
                           : "Buat ulang sertifikat SSL";
            ShowConflicts();
        }

        /// <summary>
        /// Titik penanda di pojok tiap kartu: hijau saat layanannya benar-benar
        /// melayani, kelabu saat berhenti atau memang tidak dipakai, merah saat
        /// gagal.
        ///
        /// Kartu PHP mengikuti keadaan WEB SERVER, dan itu disengaja. PHP tidak
        /// punya proses sendiri yang bisa "jalan": ia dimuat ke dalam httpd
        /// (mod_php) atau dilayani php-cgi yang hanya hidup selama web server
        /// hidup. Titik hijau di kartu PHP karena itu berarti satu hal yang
        /// benar - halaman PHP sedang dilayani - bukan "ada proses php.exe".
        ///
        /// Warnanya sama persis dengan titik di panel kiri; keduanya memakai
        /// WarnaLayanan supaya tidak pernah berbeda untuk keadaan yang sama.
        /// </summary>
        void TitikKartu()
        {
            var p = _e.Active;
            var web = _e.Services.WebState;
            var db = _e.Services.DbState;
            var namaWeb = p != null && p.WebServer == "nginx" ? "Nginx" : "Apache";

            var pakaiWeb = p == null || p.PakaiWeb;
            var pakaiDb = p == null || p.PakaiMySql;
            var adaPhp = _e.Php != null;

            // Kelabu untuk yang tidak dipakai: titik merah pada layanan yang
            // memang sengaja tidak dipilih membaca seperti kerusakan.
            var keadaanWeb = pakaiWeb ? web : ServiceState.Berhenti;
            var keadaanDb = pakaiDb ? db : ServiceState.Berhenti;
            var keadaanPhp = adaPhp && pakaiWeb ? web : ServiceState.Berhenti;

            DotKartuWeb.Fill = WarnaLayanan.Titik(keadaanWeb);
            DotKartuDb.Fill = WarnaLayanan.Titik(keadaanDb);
            DotKartuPhp.Fill = WarnaLayanan.Titik(keadaanPhp);

            // Keterangan saat disentuh, supaya artinya tidak hanya dibawa warna.
            DotKartuWeb.ToolTip = namaWeb + " " + Lang.T(pakaiWeb
                ? web.ToString().ToLowerInvariant() : "tidak dipakai");
            DotKartuDb.ToolTip = "MySQL " + Lang.T(pakaiDb
                ? db.ToString().ToLowerInvariant() : "tidak dipakai");
            DotKartuPhp.ToolTip = !adaPhp
                ? "PHP " + Lang.T("tidak dipakai")
                : "PHP " + Lang.T(keadaanPhp == ServiceState.Jalan ? "dilayani" : "belum dilayani");
        }

        int _nomorPeringatan;

        /// <summary>
        /// Panel peringatan kuning.
        ///
        /// Bagian yang lambat - memeriksa port, yang menjalankan netstat.exe bila
        /// ada yang terpakai, dan mencari sisa proses - dikerjakan di utas latar.
        /// Dulu semuanya di utas layar, pada SETIAP perubahan status layanan, dan
        /// jendela membeku sesaat tiap kali lampu berganti warna.
        ///
        /// Hanya hasil permintaan TERAKHIR yang digambar. Menyalakan layanan
        /// memicu beberapa perubahan status beruntun; tanpa nomor ini jawaban
        /// yang lebih lama bisa tiba belakangan dan menimpa yang benar.
        /// </summary>
        async void ShowConflicts()
        {
            var nomor = ++_nomorPeringatan;
            var aktif = _e.Active;
            // Port hanya diperiksa untuk layanan yang BELUM jalan. Yang sudah
            // jalan memegang portnya sendiri - dulu MySQL yang jalan selagi web
            // server belum menghasilkan keluhan "port 3306 dipakai mysqld" tentang
            // mysqld milik Phoron sendiri.
            bool periksaWeb = _e.Services.WebState != ServiceState.Jalan;
            bool periksaDb = _e.Services.DbState != ServiceState.Jalan;
            List<string> bentrok;
            List<PortCheck.Usage> sisa;
            try
            {
                var latar = await System.Threading.Tasks.Task.Run(() => new
                {
                    Bentrok = aktif == null ? new List<string>()
                        : PortCheck.Conflicts(aktif, false, periksaWeb, periksaDb)
                                   .Select(u => u.Describe()).ToList(),
                    Sisa = _e.SisaProses(),
                });
                bentrok = latar.Bentrok;
                sisa = latar.Sisa;
            }
            catch { bentrok = new List<string>(); sisa = new List<PortCheck.Usage>(); }
            if (nomor != _nomorPeringatan) return;   // sudah ada permintaan yang lebih baru

            var pesan = new List<string>();
            if (aktif == null) pesan.Add("Belum ada profil. Buat satu di halaman Profil.");
            else pesan.AddRange(bentrok);

            // Konfigurasi sudah berubah di cakram, tapi server yang sedang jalan
            // masih memakai yang lama. Dulu tidak ada tanda apa pun: sakelar
            // Virtual Host, folder proyek, situs baru - semuanya "tersimpan",
            // dan Apache tetap melayani keadaan sebelumnya. Situs yang baru saja
            // diumumkan siap di toko.test dijawab vhost bawaan.
            bool restartWeb = _e.PerluRestartWeb, restartDb = _e.PerluRestartDb;
            string barisRestart = null, barisHosts = null, barisPembaruan = null, barisSisa = null;
            var barisAdmin = new List<string>();
            if (restartWeb || restartDb)
                pesan.Add(barisRestart = restartWeb && restartDb
                    ? "Konfigurasi web server dan MySQL sudah berubah, tapi keduanya masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku."
                    : restartWeb
                        ? "Konfigurasi web server sudah berubah, tapi yang sedang jalan masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku."
                        : "Konfigurasi MySQL sudah berubah, tapi yang sedang jalan masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku.");
            // Sebagian pesan di panel ini bisa ditindak tanpa hak apa pun, sebagian
            // lagi memang mentok tanpa Administrator. Hanya yang kedua yang boleh
            // memunculkan tombol naik hak akses.
            bool perluAdmin = false;
            if (!SslTool.Exists)
                pesan.Add("HTTPS belum aktif: sertifikat belum ada, jadi port "
                          + (_e.Active != null ? _e.Active.HttpsPort.ToString() : "443")
                          + " tidak dibuka. Pakai http:// (bukan https://), atau tekan "
                          + "\"Buat sertifikat SSL\" di bawah.");
            else if (!SslTool.IsTrusted)
            {
                // Sertifikat yang ada tapi belum tepercaya adalah keadaan paling
                // menjebak: https menjawab, lalu browser menuduh situsnya palsu.
                // Pada host ber-HSTS (mis. localhost yang pernah dipasangi header
                // itu), tombol "tambah pengecualian" pun tidak ditawarkan.
                pesan.Add("Sertifikat HTTPS sudah ada tapi belum tepercaya, jadi browser "
                          + "akan memperingatkan - dan pada host ber-HSTS tidak ada tombol "
                          + "pengecualian sama sekali. Tekan \"Percayai sertifikat SSL\" di bawah.");
                barisAdmin.Add(pesan[pesan.Count - 1]);
                perluAdmin = true;   // memasang ke Trusted Root butuh Administrator
            }
            // Versi yang dicatat profil tapi tidak ada di komputer ini. Phoron
            // sudah memakai penggantinya, dan itu HARUS terlihat - diam-diam
            // menjalankan versi lain dari yang tertulis di profil adalah cara
            // tercepat membuat orang tidak percaya pada tampilan versinya.
            foreach (var p in _e.Penyesuaian()) pesan.Add(p);

            var baru = _e.Pembaruan;
            if (baru != null && baru.Galat == null && baru.LebihBaru)
                pesan.Add(barisPembaruan = "Phoron " + baru.Versi + " sudah rilis; yang terpasang "
                          + AppInfo.Version + ".");
            // Autostart Windows memakai kunci Run, dan Windows SELALU menjalankan
            // entri Run tanpa hak admin. Jadi keadaan ini normal, bukan kerusakan,
            // dan kalimatnya harus mengatakan begitu - lalu menunjukkan bahwa
            // cukup sekali izin, tidak selamanya.
            var belumDaftar = BelumDaftar();
            if (_e.Settings.ManageHosts && !HostsFile.IsAdmin() && belumDaftar.Count > 0)
            {
                pesan.Add(barisHosts = "Phoron jalan tanpa hak Administrator - itu wajar, Windows selalu begitu "
                          + "untuk aplikasi yang menyala sendiri saat boot. Akibatnya "
                          + belumDaftar.Count + " nama situs .test belum terdaftar di berkas hosts. "
                          + "Alamat http://localhost/proyek/ tetap jalan normal. "
                          + "Daftarkan sekali saja, sesudah itu tidak perlu Administrator lagi.");
                barisAdmin.Add(barisHosts);
                perluAdmin = true;
            }
            // Tidak ada cabang "else" di sini dengan sengaja. Kalau seluruh nama
            // sudah terdaftar, jalan tanpa Administrator TIDAK merugikan apa pun:
            // entri hosts menetap, .test tetap kebuka, localhost tidak pernah
            // terpengaruh. Memasang panel kuning untuk keadaan yang tidak bisa -
            // dan tidak perlu - ditindak cuma melatih orang mengabaikan panelnya.

            // Sisa proses dari salinan Phoron sebelumnya: port terpakai, tapi
            // yang memegangnya justru httpd/mysqld - bukan aplikasi asing.
            // Menyebutkannya tanpa menyediakan tombolnya hanya memaksa orang
            // membuka Task Manager dan menebak PID mana yang boleh dimatikan.
            if (sisa.Count > 0)
                pesan.Add(barisSisa = "Proses itu BISA JADI sisa Phoron yang sebelumnya berakhir tanpa sempat "
                    + "membersihkan diri - tapi bisa juga milik Laragon atau XAMPP yang memang "
                    + "sedang Anda pakai. Jalur berkasnya ditampilkan sebelum dihentikan.");

            // Catatan yang sudah ditutup (X) tidak ditampilkan lagi selama masih
            // sama; tombol aksinya ikut tersembunyi bersama catatannya.
            var tampil = CatatanDitutup.Saring("beranda", pesan);
            _peringatanTampil = tampil;
            Func<string, bool> terlihat = b => b != null && tampil.Contains(b);
            TxtPeringatan.Text = string.Join(Environment.NewLine, tampil);
            PanelPeringatan.Visibility = tampil.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnRestartKonfig.Visibility = terlihat(barisRestart) ? Visibility.Visible : Visibility.Collapsed;
            BtnAdmin.Visibility = perluAdmin && !HostsFile.IsAdmin() && barisAdmin.Any(terlihat)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnDaftarHosts.Visibility = (barisHosts != null ? terlihat(barisHosts) : belumDaftar.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnPembaruan.Visibility = terlihat(barisPembaruan) ? Visibility.Visible : Visibility.Collapsed;
            BtnBebaskan.Visibility = terlihat(barisSisa) ? Visibility.Visible : Visibility.Collapsed;
        }

        List<string> _peringatanTampil = new List<string>();

        void BtnTutupPeringatan_Click(object sender, RoutedEventArgs e)
        {
            CatatanDitutup.Tutup("beranda", _peringatanTampil);
            PanelPeringatan.Visibility = Visibility.Collapsed;
        }

        async void BtnRestartKonfig_Click(object sender, RoutedEventArgs e)
        {
            BtnRestartKonfig.IsEnabled = false;
            try
            {
                // Basis data lebih dulu - alasannya sama dengan tombol serupa di
                // halaman Profil: aplikasi PHP menyambung ke MySQL saat halamannya
                // dibuka.
                if (_e.PerluRestartDb) { await _e.StopDbAsync(); await _e.StartDbAsync(); }
                if (_e.PerluRestartWeb) { await _e.StopWebAsync(); await _e.StartWebAsync(); }
            }
            finally { BtnRestartKonfig.IsEnabled = true; }
            var main = Window.GetWindow(this) as MainWindow;
            if (main != null) main.RefreshStatus();
            RefreshState();
        }

        void BtnPembaruan_Click(object sender, RoutedEventArgs e)
        {
            UpdateDialog.Tawarkan(_e, _e.Pembaruan);
        }

        async void BtnBebaskan_Click(object sender, RoutedEventArgs e)
        {
            var sisa = _e.SisaProses();
            if (sisa.Count == 0) { RefreshState(); return; }
            // Jalur berkasnya ikut ditampilkan. Phoron tidak punya cara
            // membuktikan bahwa proses ini miliknya - ia memindai folder bin
            // Laragon dan XAMPP juga, jadi httpd yang sama persis bisa saja
            // dijalankan Laragon sendiri. Yang bisa diperbuat adalah menyodorkan
            // keterangan secukupnya supaya orang memutuskan, bukan mengaku-aku.
            if (!AppState.Ask("Hentikan proses berikut?" + Environment.NewLine + Environment.NewLine
                              + string.Join(Environment.NewLine, sisa.Select(u =>
                                    "  " + u.ProcessName + " (PID " + u.Pid + ") di port " + u.Port
                                    + (string.IsNullOrEmpty(u.Jalur)
                                       ? "" : Environment.NewLine + "      " + u.Jalur)))
                              + Environment.NewLine + Environment.NewLine
                              + "Periksa jalurnya dulu: kalau ia berada di folder Laragon atau "
                              + "XAMPP, kemungkinan besar itu milik aplikasi lain yang sedang "
                              + "berjalan." + Environment.NewLine + Environment.NewLine
                              + "MySQL diminta berhenti dengan rapi lebih dulu.")) return;

            BtnBebaskan.IsEnabled = false;
            try
            {
                var gagal = await _e.HentikanSisaAsync();
                RefreshState();
                var main = Window.GetWindow(this) as MainWindow;
                if (main != null) main.RefreshStatus();
                if (gagal.Count > 0) AppState.Warn(string.Join(Environment.NewLine + Environment.NewLine, gagal));
                else AppState.Info("Proses yang tertinggal sudah dihentikan; portnya bebas.");
            }
            finally { BtnBebaskan.IsEnabled = true; }
        }

        // RefreshState ikut setiap perubahan status layanan, sedangkan
        // BelumTerdaftar() memindai folder proyek SELURUH profil - puluhan
        // pembacaan direktori. Jawabannya hampir tidak pernah berubah, jadi
        // dihitung sekali per halaman dan hanya dibatalkan setelah pendaftaran.
        List<string> _belumDaftar;

        List<string> BelumDaftar()
        {
            if (_belumDaftar != null) return _belumDaftar;
            if (!_e.Settings.ManageHosts || HostsFile.IsAdmin())
                return _belumDaftar = new List<string>();
            try { _belumDaftar = HostsTool.BelumTerdaftar(); }
            catch { _belumDaftar = new List<string>(); }
            return _belumDaftar;
        }

        void BtnDaftarHosts_Click(object sender, RoutedEventArgs e)
        {
            DaftarHosts.Jalankan(Window.GetWindow(this));
            _belumDaftar = null;   // hasilnya berubah; hitung ulang sekali
            RefreshState();        // peringatan dan tombolnya hilang kalau sudah beres
        }

        void BtnAdmin_Click(object sender, RoutedEventArgs e)
        {
            Program.RestartAsAdmin("Menyunting berkas hosts dan memasang sertifikat ke Trusted Root "
                                   + "hanya bisa dilakukan dengan hak Administrator.");
        }

        // ------------------------------------------------------------------ Aksi

        void CmbProfil_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            BtnSwitchRun.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        }

        void BtnSwitch_Click(object sender, RoutedEventArgs e) { Pindah(false); }

        void BtnSwitchRun_Click(object sender, RoutedEventArgs e) { Pindah(true); }

        /// <summary>
        /// Pindah ke profil terpilih. Dengan <paramref name="lalimJalankan"/>,
        /// layanan ikut dinyalakan setelahnya - tanpa itu SwitchAsync hanya
        /// mengembalikan keadaan seperti semula (yang tadinya mati tetap mati).
        /// </summary>
        async void Pindah(bool lalimJalankan)
        {
            var p = CmbProfil.SelectedItem as Profile;
            if (p == null) return;
            BtnSwitch.IsEnabled = BtnSwitchRun.IsEnabled = false;
            try
            {
                var warnings = await _e.SwitchAsync(p);
                AppState.RaiseChanged();
                var main = Window.GetWindow(this) as MainWindow;

                if (lalimJalankan)
                {
                    // Peringatan ditampilkan SETELAH layanan dicoba dinyalakan:
                    // kotak pesan modal di tengah proses akan menahan start
                    // sampai pengguna menekan OK.
                    await _e.StartAllAsync();
                }

                // Profil lain bisa menunjuk folder proyek yang lain pula, jadi
                // daftar nama yang belum terdaftar ikut berubah.
                _belumDaftar = null;
                RefreshState();
                if (main != null) main.RefreshStatus();
            }
            finally { BtnSwitch.IsEnabled = BtnSwitchRun.IsEnabled = true; }
        }

        void BtnWww_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open(SiteScanner.DocumentRoot(_e.Active));
        }

        void BtnLocalhost_Click(object sender, RoutedEventArgs e) { Shell.Open(_e.RootUrl()); }

        /// <summary>
        /// Beranda Phoron ada di /phoron, bukan di akar. Akar itu milik folder
        /// proyek pengguna - kalau folder itu sudah punya index.php sendiri,
        /// halaman itulah yang muncul di localhost, dan memang seharusnya begitu.
        /// </summary>
        void BtnBeranda_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open(_e.RootUrl().TrimEnd('/') + Beranda.Alias + "/");
        }

        void BtnEtc_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.Etc); }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            Shell.OpenTerminal(_e.Settings.Terminal, SiteScanner.DocumentRoot(_e.Active), _e.ToolEnv());
        }

        /// <summary>
        /// Menulis phpinfo.php ke akar www lalu membukanya. Cara tercepat
        /// memastikan versi yang benar-benar dilayani Apache sama dengan yang
        /// tertulis di profil - bukan sekadar yang dikira Phoron.
        /// </summary>
        void BtnPhpInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = Path.Combine(SiteScanner.DocumentRoot(_e.Active), "phoron-phpinfo.php");
                File.WriteAllText(file, "<?php phpinfo();", new UTF8Encoding(false));
                Shell.Open(_e.RootUrl() + "phoron-phpinfo.php");
            }
            catch (Exception ex) { AppState.Warn("Gagal membuat phpinfo: " + ex.Message); }
        }

        void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var apache = _e.Apache;
            if (apache == null) { AppState.Warn("Profil ini belum menunjuk Apache."); return; }
            _e.Apply();
            var httpd = Path.Combine(apache.Path, "bin", "httpd.exe");
            var conf = Path.Combine(Paths.EtcApache, "httpd.conf");
            var res = Shell.Run(httpd, "-f \"" + conf + "\" -d \"" + apache.Path + "\" -t",
                                apache.Path, 30000, ServiceManager.EnvFor(_e.Php, ConfigWriter.FolderPhpIni(_e.Php, _e.Settings.PhpIniKeFolderPhp)));
            AppState.Info(string.IsNullOrWhiteSpace(res.All) ? "Konfigurasi OK." : res.All,
                          res.Ok ? "Konfigurasi OK" : "Konfigurasi bermasalah");
        }

        void BtnSsl_Click(object sender, RoutedEventArgs e)
        {
            // Sertifikat yang sudah ada TIDAK dibuat ulang begitu saja: membuat
            // ulang berarti sertifikat yang sudah dipercayai orang jadi tidak
            // cocok lagi, dan peringatan browser justru kembali muncul.
            if (SslTool.Exists && !SslTool.IsTrusted) { PercayaiSertifikat(); return; }
            if (SslTool.Exists
                && !AppState.Ask("Sertifikat sudah ada dan sudah tepercaya. Buat ulang? "
                                 + "Sertifikat lama akan berhenti berlaku dan harus dipercayai lagi."))
                return;

            var apache = _e.Apache;
            if (apache == null) { AppState.Warn("Butuh paket Apache (openssl.exe ada di dalamnya)."); return; }
            var err = SslTool.Generate(apache, _e.Active != null ? _e.Active.SiteSuffix : "test");
            if (err != null) { AppState.Warn(err); return; }
            _e.Apply();
            _e.Say("Sertifikat SSL dibuat di etc\\ssl.");
            RefreshState();
            PercayaiSertifikat();
        }

        void PercayaiSertifikat()
        {
            if (!AppState.Ask("Pasang sertifikat ke Trusted Root Windows supaya browser tidak "
                              + "memberi peringatan?")) return;

            var t = SslTool.Trust();
            if (t != null && t.Contains("Administrator"))
            {
                Program.RestartAsAdmin("Memasang sertifikat ke Trusted Root butuh hak Administrator.");
                return;
            }
            if (t != null) { AppState.Warn(t); return; }

            RefreshState();
            // Firefox punya daftar sertifikat sendiri dan tidak selalu ikut
            // Windows, jadi "sudah terpasang" saja bukan jawaban lengkap bagi
            // pengguna Firefox - dan diam soal ini membuat orang mengira
            // pemasangannya gagal.
            AppState.Info("Sertifikat terpasang di Trusted Root Windows. Tutup dan buka ulang browser.\n\n"
                          + "Chrome dan Edge langsung ikut. Firefox memakai daftar sertifikatnya "
                          + "sendiri: buka about:config, setel security.enterprise_roots.enabled "
                          + "jadi true, lalu jalankan ulang Firefox.\n\n"
                          + "Kalau sebuah nama (mis. localhost) pernah dipasangi HSTS, Firefox "
                          + "menolak menampilkan tombol pengecualian sampai sertifikatnya benar-benar "
                          + "tepercaya - atau entri HSTS-nya dibuang lewat Riwayat > klik kanan situs "
                          + "> Lupakan Situs Ini.");
        }
    }
}
