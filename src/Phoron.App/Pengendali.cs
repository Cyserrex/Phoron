using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Phoron.App.Pages;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App
{
    /// <summary>
    /// Pemilik umur aplikasi: ikon baki, sinyal antarproses, pendengar akhir
    /// sesi Windows, dan jalur keluar. Semuanya dulu menumpang di MainWindow.
    ///
    /// KENAPA DIPISAH. Selama ikon baki dan sinyal itu milik jendela, jendelanya
    /// tidak boleh dibuang - "tutup ke baki" hanya bisa berarti Hide(). Jendela
    /// yang disembunyikan tetap memegang seluruh pohon visualnya, halaman yang
    /// terakhir dibuka, dan permukaan render-nya: sekitar 150 MB untuk sebuah
    /// ikon di baki. Sekarang jendela adalah tampilan sekali pakai: dibuat saat
    /// dibuka, dibuang saat ditutup, dan memori yang ia tinggalkan dikembalikan
    /// ke Windows.
    /// </summary>
    internal sealed class Pengendali
    {
        public static Pengendali Ini { get; private set; }

        readonly Engine _engine;
        Forms.NotifyIcon _tray;
        MainWindow _jendela;
        HwndSource _pendengarSesi;
        DispatcherTimer _pelepasMemori;

        /// <summary>Jalur keluar sudah dimulai; jendela yang tertutup sesudahnya bukan "ke baki".</summary>
        bool _keluar;
        bool _sudahBeres;

        // Letak jendela terakhir, supaya jendela yang dibuat ulang muncul di
        // tempat dan ukuran yang sama - dulu ini gratis karena jendelanya
        // hanya disembunyikan.
        Rect? _letak;
        WindowState _keadaan = WindowState.Normal;
        string _tab;

        /// <summary>Ada jendela Phoron yang sedang terbuka.</summary>
        bool AdaJendela { get { return _jendela != null; } }

        System.Threading.EventWaitHandle _sinyalKeluar;
        System.Threading.RegisteredWaitHandle _daftarSinyal;
        System.Threading.EventWaitHandle _sinyalTampil;
        System.Threading.RegisteredWaitHandle _daftarTampil;

        public Pengendali(Engine engine)
        {
            _engine = engine;
            Ini = this;
        }

        /// <summary>Jendela yang sedang terbuka, atau null bila Phoron hanya ada di baki.</summary>
        public MainWindow Jendela { get { return _jendela; } }

        public void Mulai(bool tampilkan)
        {
            AppState.Init(_engine);
            // Bahasa disetel SEBELUM halaman mana pun dibuat: penerjemahnya
            // bekerja saat XAML dimuat, jadi halaman yang terlanjur dibuat
            // dengan bahasa lama tidak akan berubah sendiri.
            Lang.Pakai(_engine.Settings.Bahasa);
            MainWindow.TerapkanTema(_engine.Settings.Tema);
            // Dilanggan di SINI, bukan di jendela: jendela yang melanggan
            // peristiwa Engine ikut dipegang Engine seumur hidup, dan tidak
            // pernah bisa dibuang.
            //
            // BeginInvoke, bukan Invoke: penyegaran hanya membaca keadaan lalu
            // menggambar ulang, jadi utas layanan tidak perlu menunggunya.
            _engine.Services.StateChanged += (kind, state) =>
                Application.Current.Dispatcher.BeginInvoke(new Action(SegarkanStatus));
            _engine.Reload();

            SetupTray();
            PasangPendengarSesi();
            PasangSinyalKeluar();
            PasangSinyalTampil();
            // Jalur keluar mana pun - termasuk Application.Shutdown() langsung
            // seperti di RestartAsAdmin - tetap mematikan layanan dan mencabut
            // ikon baki. Dulu Shutdown yang bukan dari tombol Keluar membiarkan
            // ikon hantu di baki sampai kursor melewatinya.
            Application.Current.Exit += (s, e) => { _keluar = true; Bereskan(); };

            if (tampilkan) Tampilkan();
            SegarkanStatus();

            // Bukan lewat event Loaded jendela: kalau Phoron dijalankan Windows
            // dengan --tray, jendelanya tidak pernah ada sehingga Loaded tidak
            // pernah menyala - dan konfigurasi tidak akan pernah ditulis.
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(async () => await MulaiAsync()));
        }

        async Task MulaiAsync()
        {
            try { await MulaiIntiAsync(); }
            catch (Exception ex)
            {
                // Kait dispatcher juga menangkap ini, tapi kegagalan saat MEMULAI
                // pantas menyebut langkahnya sendiri - "gagal menulis konfigurasi"
                // jauh lebih berguna daripada "ada yang tidak beres".
                var jalur = Crash.Tulis(ex, "saat memulai Phoron", _engine.Riwayat());
                _engine.Say("Gagal memulai: " + ex.Message);
                AppState.Warn(Lang.T("Phoron gagal menyiapkan konfigurasinya.") + Environment.NewLine
                              + Environment.NewLine + ex.Message
                              + (jalur != null ? Environment.NewLine + Environment.NewLine
                                                 + Lang.T("Rincian: {0}", jalur) : ""));
            }
            finally
            {
                // Start dengan --tray (autostart Windows) tidak pernah membuat
                // jendela, jadi jalur "jendela ditutup" yang biasanya memangkas
                // memori tidak pernah dilalui - terukur 78 MB untuk sebuah ikon
                // baki. Dipangkas SESUDAH semua langkah start selesai, termasuk
                // menyalakan layanan dan cek rilis, supaya tidak ada yang sedang
                // bekerja saat GC penuh berjalan. Kalau jendelanya keburu dibuka,
                // Tampilkan() membatalkan jadwal ini.
                if (!AdaJendela && !_keluar) JadwalkanLepasMemori();
            }
        }

        async Task MulaiIntiAsync()
        {
            _engine.Apply();
            SegarkanStatus();
            if (_engine.Settings.AutoStartServices)
            {
                _engine.Say("Auto-start aktif - menyalakan layanan...");
                await _engine.StartAllAsync();
            }

            // Pengecekan rilis dilakukan PALING AKHIR dan tanpa kotak pesan.
            // Menyalakan Apache dan MySQL jauh lebih mendesak daripada menanyakan
            // GitHub, dan jaringan yang lambat tidak boleh menahan keduanya.
            // Hasilnya muncul sebagai baris di panel Perhatian, bukan dialog yang
            // menghadang pekerjaan orang begitu jendela terbuka.
            try
            {
                var hasil = await _engine.CekPembaruanAsync(false);
                if (hasil != null && _jendela != null)
                {
                    var dash = _jendela.Host.Content as DashboardPage;
                    if (dash != null) dash.RefreshState();
                }
            }
            catch { /* tidak ada internet bukan alasan aplikasi gagal dibuka */ }
        }

        // --------------------------------------------------------------- Jendela

        /// <summary>Munculkan jendela; dibuat baru bila sedang tidak ada.</summary>
        public void Tampilkan() { Tampilkan(null); }

        /// <summary>
        /// Munculkan jendela di tab tertentu (null = tab terakhir).
        ///
        /// Satu jendela utama saja. Panel ringkas terpisah sempat dicoba di
        /// 1.41.0 dan dibuang: untuk mengatur apa pun orang tetap harus membuka
        /// jendela lengkap, jadi panel itu menambah satu langkah, bukan
        /// mengurangi. Yang dijaga sekarang ukuran jendela ini - lihat UkuranAwal.
        /// </summary>
        public void Tampilkan(string tab)
        {
            if (_keluar) return;
            if (_pelepasMemori != null) _pelepasMemori.Stop();
            if (!string.IsNullOrEmpty(tab)) _tab = tab;

            if (_jendela == null)
            {
                var w = new MainWindow(_engine, _tab);
                if (_letak.HasValue)
                {
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = _letak.Value.Left;
                    w.Top = _letak.Value.Top;
                    w.Width = _letak.Value.Width;
                    w.Height = _letak.Value.Height;
                    // Disetel sebelum Show: jendela yang dimaksimalkan tetap
                    // ingat ukuran normalnya dari Left/Top/Width/Height di atas.
                    w.WindowState = _keadaan;
                }
                else UkuranAwal(w);
                w.Closing += (s, e) =>
                {
                    if (e.Cancel) return;
                    _letak = w.RestoreBounds.IsEmpty ? (Rect?)null : w.RestoreBounds;
                    // Jendela yang ditutup saat sedang dikecilkan dibuka lagi
                    // dalam ukuran normal - dibuka dalam keadaan kecil sama
                    // dengan tidak dibuka.
                    _keadaan = w.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    _tab = w.TagAktif;
                    SimpanUkuran(_letak);
                };
                w.Closed += (s, e) => JendelaTertutup(w);
                _jendela = w;
                // WPF menjadikan jendela PERTAMA yang dibuat sebagai MainWindow
                // bila belum ada. Tanpa penetapan ini, sesudah jendela utama
                // dibuang, kotak dialog berikutnya - InputDialog, unduhan -
                // diam-diam menjadi "MainWindow" aplikasi.
                Application.Current.MainWindow = w;
                w.Show();
                SegarkanStatus();
            }
            else if (!string.IsNullOrEmpty(tab)) _jendela.GoTo(tab);
            _jendela.Majukan();
        }

        /// <summary>
        /// Ukuran pertama jendela lengkap: yang terakhir dipakai orang (phoron.ini),
        /// atau 960x640 - tapi tidak pernah lebih dari 70% lebar dan 82% tinggi
        /// layar. Dulu 1120x740 tetap, yang di layar 1366x768 nyaris memenuhi layar.
        /// </summary>
        void UkuranAwal(MainWindow w)
        {
            var area = SystemParameters.WorkArea;
            var s = _engine.Settings;
            double lebar = s.JendelaLebar > 0 ? s.JendelaLebar : Math.Min(960, area.Width * 0.70);
            double tinggi = s.JendelaTinggi > 0 ? s.JendelaTinggi : Math.Min(640, area.Height * 0.82);
            w.Width = Math.Max(w.MinWidth, Math.Min(lebar, area.Width));
            w.Height = Math.Max(w.MinHeight, Math.Min(tinggi, area.Height));
        }

        void SimpanUkuran(Rect? letak)
        {
            if (!letak.HasValue || _engine.Settings == null) return;
            var lebar = (int)Math.Round(letak.Value.Width);
            var tinggi = (int)Math.Round(letak.Value.Height);
            if (lebar == _engine.Settings.JendelaLebar && tinggi == _engine.Settings.JendelaTinggi) return;
            _engine.Settings.JendelaLebar = lebar;
            _engine.Settings.JendelaTinggi = tinggi;
            try { _engine.Settings.Save(); } catch { /* ukuran jendela kenyamanan, bukan data */ }
        }

        void JendelaTertutup(Window w)
        {
            if (_jendela == w) _jendela = null;
            if (Application.Current != null && Application.Current.MainWindow == w)
                Application.Current.MainWindow = null;

            // Ditunda: Closed juga menyala saat Application.Shutdown menutup
            // semua jendela. Pekerjaan yang ditunda tidak pernah dijalankan
            // dispatcher yang sedang dimatikan, jadi jalur keluar tidak memicu
            // Keluar() kedua di tengah dirinya sendiri.
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_keluar) return;
                if (AdaJendela) return;
                if (_engine.Settings.MinimizeToTray) JadwalkanLepasMemori();
                else Keluar();
            }));
        }

        /// <summary>
        /// Kembalikan memori yang ditinggalkan jendela ke Windows.
        ///
        /// Ditunda beberapa detik, bukan seketika: utas render WPF baru
        /// melepas permukaan gambarnya sesudah jendelanya benar-benar hancur,
        /// dan orang yang menutup lalu langsung membuka lagi tidak perlu
        /// membayar pengumpulan sampah penuh di tengahnya.
        /// </summary>
        void JadwalkanLepasMemori()
        {
            if (_pelepasMemori == null)
            {
                _pelepasMemori = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _pelepasMemori.Tick += (s, e) =>
                {
                    _pelepasMemori.Stop();
                    if (AdaJendela || _keluar) return;
                    LepasMemori();
                };
            }
            _pelepasMemori.Stop();
            _pelepasMemori.Start();
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessWorkingSetSize(IntPtr proses, IntPtr min, IntPtr maks);

        internal static void LepasMemori()
        {
            try
            {
                // Dua putaran: putaran pertama hanya MENANDAI objek yang punya
                // finalizer (HwndSource, kuas, bitmap); baru sesudah finalizer
                // mereka jalan, putaran kedua benar-benar membuangnya.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                // -1/-1: minta Windows memangkas working set proses ini. Halaman
                // yang dipangkas tetap ada di memori standby dan kembali tanpa
                // biaya kalau dibutuhkan lagi - ini bukan membuang, hanya
                // berhenti mengaku memakainya.
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { /* penghematan, bukan syarat */ }
        }

        // ---------------------------------------------------------------- Status

        /// <summary>Segarkan teks baki dan, bila ada, panel status jendela.</summary>
        public void SegarkanStatus()
        {
            // RefreshStatus jendela lengkap ikut menyegarkan baki.
            if (_jendela != null) _jendela.RefreshStatus();
            else SegarkanBaki();
        }

        /// <summary>Hanya bagian baki - dipanggil juga oleh MainWindow.RefreshStatus.</summary>
        public void SegarkanBaki()
        {
            if (_tray == null) return;
            var web = _engine.Services.WebState;
            var db = _engine.Services.DbState;
            bool anyRunning = web == ServiceState.Jalan || db == ServiceState.Jalan;
            bool busy = web == ServiceState.Menyalakan || web == ServiceState.Mematikan
                     || db == ServiceState.Menyalakan || db == ServiceState.Mematikan;
            _tray.Text = Lang.T(anyRunning ? "Phoron - berjalan" : "Phoron - berhenti");
            if (_itemDaya != null)
            {
                _itemDaya.Text = Lang.T(anyRunning ? "Matikan semua" : "Nyalakan semua");
                // Sama dengan tombol daya di jendela. Dulu hanya tombol itu
                // yang dinonaktifkan saat sibuk, dan item baki ini jadi pintu
                // belakang untuk start kedua di tengah start pertama.
                _itemDaya.Enabled = !busy;
            }
        }

        public async Task TogglePower()
        {
            bool anyRunning = _engine.Services.WebState == ServiceState.Jalan
                           || _engine.Services.DbState == ServiceState.Jalan;
            if (anyRunning) await _engine.StopAllAsync();
            else await _engine.StartAllAsync();
            SegarkanStatus();
        }

        // ------------------------------------------------------------------ Baki

        /// <summary>
        /// Ikon untuk baki sistem, atau null bila tidak bisa diambil.
        ///
        /// SetupTray dipanggil saat start, jadi satu galat di sini berarti
        /// Phoron tidak pernah muncul sama sekali - bukan sekadar muncul tanpa
        /// ikon. Dulu barisnya memanggil GetEntryAssembly().Location tanpa
        /// penjaga apa pun; nilainya null di setiap proses yang bukan exe biasa,
        /// dan ExtractAssociatedIcon sendiri bisa melempar untuk berkas yang
        /// sedang terkunci. Ikon yang tidak ada jauh lebih ringan akibatnya
        /// daripada aplikasi yang tidak menyala.
        /// </summary>
        static System.Drawing.Icon IkonAplikasi()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetEntryAssembly();
                if (exe == null || string.IsNullOrEmpty(exe.Location)) return null;
                return System.Drawing.Icon.ExtractAssociatedIcon(exe.Location);
            }
            catch { return null; }
        }

        Forms.ToolStripMenuItem _itemDaya;

        void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            // Dirakit ulang setiap kali dibuka: proyek Node, situs terakhir, dan
            // profil berubah terus, dan menu yang dirakit sekali di awal akan
            // menawarkan keadaan yang sudah lewat.
            menu.Opening += (s, e) => IsiMenuBaki(menu);
            IsiMenuBaki(menu);

            _tray = new Forms.NotifyIcon
            {
                Visible = true,
                Text = "Phoron",
                ContextMenuStrip = menu,
                // Ikon exe dipakai apa adanya supaya tidak ada berkas .ico lepas
                // yang harus ikut dikirim.
                Icon = IkonAplikasi(),
            };
            _tray.DoubleClick += (s, e) => Tampilkan();
        }

        /// <summary>
        /// Menu baki: keadaan PHP, proyek Node, situs PHP terakhir, ganti profil.
        ///
        /// Bagi yang PHP-nya sudah berjalan sendiri, satu-satunya pekerjaan rutin
        /// di Phoron adalah menjalankan proyek Node - dan itu cukup dari sini,
        /// tanpa membuka jendela sama sekali.
        /// </summary>
        void IsiMenuBaki(Forms.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var ringkas = RingkasanLayanan.Hitung(_engine);
            menu.Items.Add(new Forms.ToolStripMenuItem(ringkas.Judul) { Enabled = false, Image = Titik(ringkas.Keadaan) });
            _itemDaya = new Forms.ToolStripMenuItem(Lang.T("Nyalakan semua"), null, async (s, e) => await TogglePower());
            menu.Items.Add(_itemDaya);
            SegarkanBaki();

            var proyek = _engine.NodeAppsList;
            if (proyek != null && proyek.Count > 0)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(new Forms.ToolStripMenuItem(Lang.T("Proyek Node")) { Enabled = false });
                foreach (var a in proyek.Take(8))
                {
                    var app = a;
                    if (_engine.Node.IsRunning(app.Path))
                    {
                        var url = _engine.Node.UrlOf(app.Path);
                        var item = new Forms.ToolStripMenuItem(
                            app.DisplayName + (string.IsNullOrEmpty(url) ? "" : "   " + url),
                            Titik(ServiceState.Jalan));
                        if (!string.IsNullOrEmpty(url))
                            item.DropDownItems.Add(Lang.T("Buka di browser"), null, (s, e) => Shell.Open(url));
                        item.DropDownItems.Add(Lang.T("Hentikan"), null, (s, e) => _engine.Node.Stop(app.Path));
                        item.DropDownItems.Add(Lang.T("Buka folder"), null, (s, e) => Shell.Open(app.Path));
                        menu.Items.Add(item);
                    }
                    else
                    {
                        menu.Items.Add(Lang.T("Jalankan {0}", app.DisplayName), Titik(ServiceState.Berhenti), (s, e) =>
                        {
                            var err = _engine.JalankanNode(app);
                            if (err != null) AppState.Warn(err);
                        });
                    }
                }
            }

            var situs = _engine.SitusTerakhirDaftar(3);
            if (situs.Count > 0)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(new Forms.ToolStripMenuItem(Lang.T("Situs PHP terakhir")) { Enabled = false });
                foreach (var st in situs)
                {
                    var site = st;
                    menu.Items.Add(site.Folder, null, (s, e) => _engine.BukaSitus(site));
                }
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            if (_engine.Profiles != null && _engine.Profiles.Count > 1)
            {
                var ganti = new Forms.ToolStripMenuItem(Lang.T("Ganti profil"));
                foreach (var pr in _engine.Profiles)
                {
                    var profil = pr;
                    var item = new Forms.ToolStripMenuItem(profil.Name) { Checked = ReferenceEquals(profil, _engine.Active) };
                    item.Click += async (s, e) =>
                    {
                        if (ReferenceEquals(profil, _engine.Active)) return;
                        await _engine.SwitchAsync(profil);
                        AppState.RaiseChanged();
                        SegarkanStatus();
                    };
                    ganti.DropDownItems.Add(item);
                }
                menu.Items.Add(ganti);
            }
            menu.Items.Add(Lang.T("Buka folder proyek"), null,
                (s, e) => Shell.Open(SiteScanner.DocumentRoot(_engine.Active)));
            menu.Items.Add(Lang.T("Buka localhost"), null, (s, e) => Shell.Open(_engine.RootUrl()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Lang.T("Tampilkan Phoron"), null, (s, e) => Tampilkan());
            menu.Items.Add(Lang.T("Keluar"), null, (s, e) => Keluar());
        }

        static readonly System.Collections.Generic.Dictionary<ServiceState, System.Drawing.Bitmap> _titik =
            new System.Collections.Generic.Dictionary<ServiceState, System.Drawing.Bitmap>();

        /// <summary>Titik warna keadaan untuk menu baki; warnanya sama dengan WarnaLayanan.</summary>
        static System.Drawing.Bitmap Titik(ServiceState s)
        {
            System.Drawing.Bitmap bmp;
            if (_titik.TryGetValue(s, out bmp)) return bmp;
            var w = ((System.Windows.Media.SolidColorBrush)WarnaLayanan.Titik(s)).Color;
            bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var kuas = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(w.A, w.R, w.G, w.B)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(kuas, 4, 4, 8, 8);
            }
            _titik[s] = bmp;
            return bmp;
        }

        // ------------------------------------------------------------ Akhir sesi

        /// <summary>
        /// Mendengarkan berakhirnya sesi Windows lewat jendela tersembunyi milik
        /// pengendali sendiri - jendela utama tidak selalu ada.
        ///
        /// Restart Manager - yang dipakai pemasang Inno Setup untuk menutup
        /// aplikasi yang sedang berjalan - mengirim pesan yang sama ke jendela
        /// tingkat atas aplikasi, termasuk yang tersembunyi. Tanpa penanganan
        /// ini, pemasang menghentikan prosesnya paksa, dan Apache serta MySQL
        /// tidak pernah sempat dimatikan dengan rapi.
        ///
        /// Jendela ini harus jendela tingkat atas biasa, BUKAN message-only
        /// (HWND_MESSAGE): jendela message-only tidak menerima pesan siaran,
        /// termasuk WM_ENDSESSION.
        ///
        /// Yang ditangani hanya WM_ENDSESSION dengan wParam bukan nol - "sesi
        /// BENAR-BENAR berakhir". WM_QUERYENDSESSION sengaja dibiarkan: itu baru
        /// PERTANYAAN, dan aplikasi lain masih boleh menolaknya (Notepad dengan
        /// berkas yang belum disimpan, misalnya). Dulu layanan sudah dimatikan
        /// pada pertanyaan itu, jadi shutdown yang dibatalkan meninggalkan Apache
        /// dan MySQL mati dengan lampu tetap hijau.
        /// </summary>
        void PasangPendengarSesi()
        {
            try
            {
                const int WS_POPUP = unchecked((int)0x80000000);
                var p = new HwndSourceParameters("Phoron.Sesi")
                {
                    Width = 0,
                    Height = 0,
                    // Tanpa WS_VISIBLE: tidak tampil, tidak di taskbar, tidak di Alt+Tab.
                    WindowStyle = WS_POPUP,
                };
                _pendengarSesi = new HwndSource(p);
                _pendengarSesi.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    const int WM_ENDSESSION = 0x0016;
                    if (msg == WM_ENDSESSION && wParam != IntPtr.Zero && !_keluar)
                    {
                        // Ditandai sebagai keluar sungguhan supaya jendela yang
                        // tertutup sesudahnya tidak diperlakukan "ke baki".
                        _keluar = true;
                        _engine.Say("Diminta menutup diri oleh sistem (pemasang atau shutdown) - "
                                    + "mematikan layanan dulu.");
                        try { _engine.Services.StopAll(); _engine.Node.StopAll(); } catch { }
                    }
                    return IntPtr.Zero;
                });
            }
            catch { /* tanpa pendengar, Job Object tetap jadi jaring pengaman */ }
        }

        /// <summary>
        /// Event bernama yang bisa disetel pihak lain - khususnya pemasang
        /// pembaruan - untuk meminta Phoron menutup diri dengan rapi.
        ///
        /// Restart Manager saja tidak cukup: ia hanya menyasar aplikasi yang
        /// mengunci berkas yang akan ditimpa, dan permintaannya bisa tidak
        /// terjawab kalau versi yang sedang berjalan belum mengenal pesan itu.
        /// Sinyal ini memberi pemasang jalan yang lugas untuk meminta - bukan
        /// memaksa - Phoron berhenti, sehingga Apache dan MySQL sempat dimatikan
        /// dengan rapi sebelum berkasnya diganti.
        /// </summary>
        void PasangSinyalKeluar()
        {
            try
            {
                bool baru;
                _sinyalKeluar = ObjekAntarProses.BuatEvent(
                    "Phoron.KeluarSekarang",
                    System.Threading.EventResetMode.AutoReset, out baru);
                _daftarSinyal = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _sinyalKeluar,
                    (keadaan, kehabisanWaktu) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _engine.Say("Diminta menutup diri oleh pemasang - mematikan layanan dulu.");
                        Keluar();
                    })),
                    null, System.Threading.Timeout.Infinite, true);
            }
            catch { /* tanpa sinyal, pemasang masih punya jalur paksa */ }
        }

        /// <summary>
        /// Event bernama yang disetel salinan kedua Phoron - lazimnya karena
        /// pengguna menekan ikon Phoron di taskbar sementara Phoron hanya ada
        /// di baki sistem - untuk meminta jendelanya dimunculkan.
        /// </summary>
        void PasangSinyalTampil()
        {
            try
            {
                bool baru;
                _sinyalTampil = ObjekAntarProses.BuatEvent(
                    Program.NamaSinyalTampil,
                    System.Threading.EventResetMode.AutoReset, out baru);
                _daftarTampil = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _sinyalTampil,
                    (keadaan, kehabisanWaktu) => Application.Current.Dispatcher.BeginInvoke(new Action(Tampilkan)),
                    null, System.Threading.Timeout.Infinite, false);
            }
            catch { /* tanpa sinyal, perilakunya kembali seperti sebelumnya */ }
        }

        // ---------------------------------------------------------------- Keluar

        /// <summary>Tutup Phoron sepenuhnya: layanan dimatikan, ikon baki dicabut.</summary>
        public void Keluar()
        {
            if (_keluar && _sudahBeres) return;
            _keluar = true;
            if (_jendela != null) { try { _jendela.Close(); } catch { } }
            Bereskan();
            Application.Current.Shutdown();
        }

        /// <summary>Apakah masih ada layanan atau server Node yang hidup.</summary>
        public bool AdaYangJalan()
        {
            return _engine.Services.WebState == ServiceState.Jalan
                || _engine.Services.DbState == ServiceState.Jalan
                || _engine.Node.RunningFolders.Any();
        }

        void Bereskan()
        {
            if (_sudahBeres) return;
            _sudahBeres = true;
            // Layanan adalah proses anak; membiarkannya hidup setelah aplikasi
            // ditutup berarti port 80 tetap terpakai tanpa ada yang mengaku.
            try { _engine.Services.StopAll(); } catch { }
            // Server pengembangan Node juga proses anak; kalau ditinggal hidup,
            // port 3000/4321 tetap terpakai oleh proses tanpa jendela.
            try { _engine.Node.StopAll(); } catch { }
            if (_pelepasMemori != null) _pelepasMemori.Stop();
            if (_daftarSinyal != null) { try { _daftarSinyal.Unregister(null); } catch { } }
            if (_sinyalKeluar != null) { try { _sinyalKeluar.Close(); } catch { } }
            if (_daftarTampil != null) { try { _daftarTampil.Unregister(null); } catch { } }
            if (_sinyalTampil != null) { try { _sinyalTampil.Close(); } catch { } }
            if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
            if (_pendengarSesi != null) { try { _pendengarSesi.Dispose(); } catch { } }
        }
    }
}
