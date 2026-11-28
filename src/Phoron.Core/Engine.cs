using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Phoron.Core
{
    /// <summary>
    /// Perekat seluruh bagian: setelan, hasil pindaian versi, profil, penulis
    /// konfigurasi, dan pengendali layanan. UI hanya berbicara dengan kelas ini
    /// supaya urutan "tulis konfigurasi dulu, baru nyalakan" tidak pernah terlewat.
    /// </summary>
    public partial class Engine
    {
        public Settings Settings { get; private set; }
        public List<BinPackage> Packages { get; private set; }
        public List<Profile> Profiles { get; private set; }
        public Profile Active { get; private set; }
        /// <summary>
        /// Keadaan Apache/MySQL. Dalam mode lama ServiceManager di proses ini;
        /// dalam mode daemon cerminan keadaan di phorond (Jarak.LayananJarak).
        /// </summary>
        public ILayanan Services { get; private set; }
        /// <summary>Proyek Node/TypeScript yang terdaftar, dan pengendali prosesnya.</summary>
        public IProsesNode Node { get; private set; }

        // Pengendali milik proses ini - hanya ada dalam mode lama.
        ServiceManager _svc;
        NodeRunner _node;
        public List<NodeApp> NodeAppsList { get; private set; }
        /// <summary>Hasil pengecekan rilis terakhir; null bila belum pernah dicek.</summary>
        public HasilCek Pembaruan { get; private set; }
        public ConfigWriter.Result LastBuild { get; private set; }
        public List<Site> Sites { get; private set; }

        public event Action<string> Log;

        /// <summary>
        /// Keluhan saat memuat setelan dan profil: berkas cacat, profil yang
        /// tidak terbaca. Ikut ditampilkan di panel Perhatian pada Beranda,
        /// sebab kalau tidak, satu-satunya tandanya adalah setelan yang
        /// diam-diam kembali ke bawaan.
        /// </summary>
        public readonly List<string> MasalahMuat = new List<string>();

        public Engine()
        {
            var hasil = Core.Settings.Muat();
            Settings = hasil.Setelan;
            foreach (var k in hasil.Keluhan)
                MasalahMuat.Add("phoron.ini: " + k);
            // Ditulis sejak jalan pertama, bukan menunggu pengguna menyimpan
            // sesuatu: berkas ini juga jadi penanda akar instalasi, dan tanpanya
            // exe yang dipindah ke subfolder akan menebak akar yang salah.
            if (!File.Exists(Paths.SettingsFile)) Settings.Save();
            Packages = new List<BinPackage>();
            Profiles = new List<Profile>();
            Sites = new List<Site>();
            Services = _svc = new ServiceManager();
            _svc.Log += Say;
            _svc.LogRinci = Settings.LogRinci;
            Node = _node = new NodeRunner();
            _node.UrlFound += PeriksaHsts;
            // Dulu dicatat oleh halaman Node, jadi hilang kalau halamannya tertutup.
            _node.UrlFound += (folder, url) =>
                Say(Lang.T("Proyek {0} siap di {1}", Path.GetFileName((folder ?? "").TrimEnd('\\')), url));
            NodeAppsList = new List<NodeApp>();
        }

        /// <summary>
        /// Begitu sebuah proyek Node mengumumkan alamatnya, periksa apakah ia
        /// mengirim HSTS untuk localhost - lihat <see cref="HstsPeriksa"/>.
        ///
        /// Dijalankan di utas latar: pemeriksaannya menunggu jawaban jaringan
        /// sampai beberapa detik, dan peristiwa ini datang dari utas pembaca
        /// keluaran proses. Menahannya di situ berarti menahan keluaran proyek
        /// yang baru saja dijalankan pengguna.
        /// </summary>
        void PeriksaHsts(string folder, string url)
        {
            if (!HstsPeriksa.Berlaku(url)) return;
            var nama = folder;
            try { nama = System.IO.Path.GetFileName((folder ?? "").TrimEnd('\\')); }
            catch { }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var keluhan = HstsPeriksa.Keluhan(nama, url, HstsPeriksa.BacaHeader(url));
                    if (keluhan == null) return;
                    // Dua tempat sekaligus, dan itu disengaja. Catatan aktivitas
                    // Beranda adalah yang tersimpan ke phoron.log dan bisa dibaca
                    // lagi berhari-hari kemudian - saat gejalanya baru muncul.
                    // Keluaran proyeknya adalah yang sedang ditatap pengguna
                    // detik itu juga.
                    Say(keluhan);
                    Node.Sisipkan(folder, keluhan);
                }
                catch { /* pemeriksaan penyedap; kegagalannya bukan urusan pengguna */ }
            });
        }

        /// <summary>Nama proses yang memang dijalankan Phoron - dipakai mengenali sisa yang tertinggal.</summary>
        static readonly string[] ProsesKita = { "httpd", "mysqld", "nginx", "php-cgi" };

        /// <summary>
        /// Port profil yang sedang dipegang proses jenis milik Phoron, padahal
        /// Phoron ini tidak merasa menjalankannya. Biasanya sisa dari salinan
        /// sebelumnya yang berakhir tanpa sempat membersihkan diri - misalnya
        /// ditutup paksa pemasang saat memperbarui versi.
        /// </summary>
        public List<PortCheck.Usage> SisaProses()
        {
            if (ModeJarak) return SisaProsesJarak();
            var hasil = new List<PortCheck.Usage>();
            if (Active == null) return hasil;
            if (Services.WebState == ServiceState.Jalan || Services.DbState == ServiceState.Jalan)
                return hasil;   // yang memegang port itu kita sendiri
            foreach (var u in PortCheck.Conflicts(Active, SslTool.Exists))
                if (ProsesKita.Contains((u.ProcessName ?? "").ToLowerInvariant()))
                    hasil.Add(u);
            return hasil;
        }

        /// <summary>
        /// Hentikan sisa proses itu. MySQL diminta berhenti dengan rapi lebih
        /// dulu lewat mysqladmin - dimatikan mendadak, InnoDB harus memulihkan
        /// diri saat start berikutnya, dan itu bisa memakan waktu lama.
        /// </summary>
        public async Task<List<string>> HentikanSisaAsync()
        {
            if (ModeJarak) return await AksiJarakAsync("hentikan_sisa", null, BatasLama);
            var gagal = new List<string>();
            foreach (var u in SisaProses())
            {
                var nama = (u.ProcessName ?? "").ToLowerInvariant();
                if (nama == "mysqld")
                {
                    Say("Meminta mysqld (PID " + u.Pid + ") berhenti dengan rapi...");
                    // Lewat event bernamanya lebih dulu - tanpa kata sandi, jadi
                    // tetap bekerja walau root sudah diberi sandi. mysqladmin
                    // hanya cadangan untuk build yang tidak membuat event itu.
                    if (ServiceManager.MintaMysqlBerhenti(u.Pid))
                    {
                        for (int i = 0; i < 30 && !PortCheck.IsFree(u.Port); i++) await Task.Delay(400);
                    }
                    else if (MySql != null)
                    {
                        var admin = Path.Combine(MySql.Path, "bin", "mysqladmin.exe");
                        if (File.Exists(admin))
                        {
                            await Task.Run(() => Shell.Run(admin,
                                "--protocol=tcp --port=" + u.Port + " -u root shutdown", MySql.Path, 20000));
                            await Task.Delay(1500);
                        }
                    }
                }
                if (!PortCheck.IsFree(u.Port))
                {
                    Say("Menghentikan " + u.ProcessName + " (PID " + u.Pid + ") yang memegang port " + u.Port + ".");
                    await Task.Run(() => Shell.KillTree(u.Pid));
                    await Task.Delay(700);
                }

                // Port yang MASIH terpakai setelah dibunuh hampir selalu berarti
                // satu hal: sisa proses itu dijalankan Phoron ber-hak
                // Administrator, dan proses biasa tidak boleh menghentikannya.
                // Melaporkan "selesai" di keadaan ini hanya membuat orang
                // bertanya-tanya kenapa portnya tetap terpakai.
                if (!PortCheck.IsFree(u.Port))
                    gagal.Add(u.ProcessName + " (PID " + u.Pid + ") di port " + u.Port
                              + " tidak bisa dihentikan"
                              + (HostsFile.IsAdmin()
                                 ? "." 
                                 : " - proses itu kemungkinan dijalankan Phoron ber-hak "
                                   + "Administrator. Jalankan ulang Phoron sebagai Administrator, "
                                   + "lalu coba lagi."));
            }
            return gagal;
        }

        /// <summary>
        /// Cek rilis terbaru di GitHub. Dengan <paramref name="paksa"/> false,
        /// pengecekan dilewati bila baru saja dilakukan - API GitHub tanpa token
        /// dibatasi 60 permintaan per jam per IP, dan menanyakannya tiap kali
        /// jendela dibuka menghabiskan jatah itu tanpa menghasilkan apa pun baru.
        /// </summary>
        public System.Threading.Tasks.Task<HasilCek> CekPembaruanAsync(bool paksa)
        {
            if (!paksa)
            {
                if (!Settings.CekPembaruan) return Task.FromResult<HasilCek>(null);
                if ((DateTime.UtcNow - Settings.CekTerakhir).TotalHours < 6) return Task.FromResult(Pembaruan);
            }
            // Pengecekan yang sedang berjalan dipakai bersama: tombol di Setelan
            // dan Tentang, ditambah cek saat mulai, tidak menembak GitHub dua kali.
            var jalan = _cekBerjalan;
            if (jalan != null && !jalan.IsCompleted) return jalan;
            return _cekBerjalan = CekSekarangAsync();
        }

        Task<HasilCek> _cekBerjalan;
        string _galatCekTerakhir;
        DateTime _galatCekWaktu;

        async Task<HasilCek> CekSekarangAsync()
        {
            var hasil = await Updater.CekAsync();
            Settings.CekTerakhir = DateTime.UtcNow;
            Settings.Save();
            Pembaruan = hasil;
            if (hasil.Galat != null)
            {
                // Galat yang sama berulang dalam semenit (orang menekan "Cek" lagi
                // karena jaringan sedang putus) cukup dicatat sekali di log.
                bool ulang = hasil.Galat == _galatCekTerakhir
                             && (DateTime.UtcNow - _galatCekWaktu).TotalSeconds < 60;
                _galatCekTerakhir = hasil.Galat;
                _galatCekWaktu = DateTime.UtcNow;
                if (!ulang) Say("Cek pembaruan: " + hasil.Galat);
            }
            else
            {
                _galatCekTerakhir = null;
                if (hasil.LebihBaru) Say("Pembaruan tersedia: Phoron " + hasil.Versi + ".");
                else Say("Phoron " + AppInfo.Version + " sudah versi terbaru.");
            }
            return hasil;
        }

        /// <summary>Sebanyak ini baris riwayat disimpan untuk ditampilkan di layar.</summary>
        const int RiwayatMaks = 200;

        readonly Queue<BarisLog> _riwayat = new Queue<BarisLog>();

        /// <summary>
        /// Salinan riwayat log terakhir.
        ///
        /// Riwayatnya dipegang di sini, bukan di halaman Beranda, karena halaman
        /// itu DIBUAT ULANG setiap kali navigasi berpindah - dan antrian yang
        /// tinggal di dalamnya ikut hilang bersamanya. Gejalanya: panel Aktivitas
        /// mendadak kosong sepulang dari tab lain, seolah tidak pernah terjadi
        /// apa-apa. Engine hidup selama aplikasi hidup, jadi di sinilah tempatnya.
        ///
        /// Dikembalikan sebagai salinan: Say() bisa dipanggil dari utas layanan,
        /// sementara yang membacanya utas layar.
        /// </summary>
        public List<BarisLog> Riwayat()
        {
            lock (_riwayat) return new List<BarisLog>(_riwayat);
        }

        public void Say(string text)
        {
            // Mode daemon: log Aktivitas milik daemon. Barisnya kembali sebagai
            // kabar log, dan baru di situ masuk riwayat dan dikabarkan.
            if (ModeJarak && CatatJarak(text)) return;
            // Dicatat SEBELUM pendengarnya dipanggil, supaya penggambar layar
            // yang membaca Riwayat() sudah melihat baris ini di dalamnya.
            lock (_riwayat)
            {
                _riwayat.Enqueue(new BarisLog { Waktu = DateTime.Now, Teks = text ?? "" });
                while (_riwayat.Count > RiwayatMaks) _riwayat.Dequeue();
            }

            var h = Log;
            if (h != null) h(text);
            if (ModeJarak) return;   // phoron.log ditulis daemon

            // Penulisannya diserahkan ke LogFile: ia berputar sebelum berkasnya
            // membesar tanpa batas, dan mengunci supaya baris tidak lenyap saat
            // utas layar dan utas layanan menulis berbarengan. Dulu di sini
            // File.AppendAllText telanjang di dalam try/catch kosong.
            LogFile.Tambah(Path.Combine(Paths.Logs, "phoron.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text);
        }

        // ------------------------------------------------------------- Pemuatan

        /// <summary>Pindai ulang versi dan muat profil. Aman dipanggil berkali-kali.</summary>
        public void Reload()
        {
            // Mode daemon: daftar milik daemon (lihat PaketDariDaemon); daemon
            // lama yang belum mengenal "paket" membuat UI memindai sendiri.
            Packages = (ModeJarak ? PaketDariDaemon() : null) ?? BinScanner.ScanAll(Settings.BinRoots);
            NodeAppsList = NodeAppStore.LoadAll();
            MasalahMuat.RemoveAll(x => x.StartsWith("Profil "));
            Profiles = ProfileStore.LoadAll(MasalahMuat);
            if (Profiles.Count == 0 && Packages.Any(p => p.Kind == BinKind.Php))
            {
                Say("Belum ada profil - membuatkan satu profil per versi PHP yang ditemukan.");
                Profiles = ProfileStore.Seed(Packages);
            }
            Active = Profiles.FirstOrDefault(p => p.FileName == Settings.ActiveProfile)
                     ?? ProfilBawaan();
            RefreshSites();
        }

        /// <summary>
        /// Pindai ulang folder bin dan situs: tombol Pindai ulang, dan sesudah
        /// paket diunduh ke bin. Di mode daemon daemon yang memindai ulang -
        /// Reload saja hanya meminta daftar yang sudah dipegangnya.
        /// </summary>
        public void PindaiUlang()
        {
            if (ModeJarak) PindaiUlangJarak();
            else Reload();
        }

        /// <summary>
        /// Tanpa profil aktif tersimpan, yang dipilih adalah profil dengan PHP
        /// paling baru - bukan yang pertama menurut abjad nama berkas, yang
        /// kebetulan selalu versi paling tua.
        /// </summary>
        Profile ProfilBawaan()
        {
            return Profiles
                .OrderByDescending(p =>
                {
                    var php = Find(BinKind.Php, p.PhpId);
                    return php != null ? php.Parsed : new Version(0, 0, 0);
                })
                .FirstOrDefault();
        }

        /// <summary>Peringatan dari pemindaian situs terakhir (folder hilang, nama bentrok).</summary>
        public List<string> SiteWarnings { get; private set; }

        public void RefreshSites()
        {
            SiteWarnings = new List<string>();
            Sites = Active != null ? SiteScanner.Scan(Active, SiteWarnings) : new List<Site>();

            // Catatan dari pemindai situs seluruhnya tentang NAMA host - nama
            // yang bentrok lalu diberi akhiran -2, dan seterusnya. Tanpa Virtual
            // Host, nama itu tidak dipakai siapa pun: alamatnya jalur localhost,
            // dan dua folder berbeda tidak pernah berebut apa pun. Menampilkan
            // keberatan tentang sesuatu yang tidak berlaku hanya melatih orang
            // mengabaikan panel catatan.
            if (!Settings.AutoVhost) SiteWarnings.Clear();
        }

        // ------------------------------------------------------------- Resolusi

        /// <summary>
        /// Paket yang ditunjuk sebuah nilai profil - nama folder, atau jalur
        /// lengkap untuk paket yang nama foldernya kembar (lihat
        /// BinPackage.NilaiSimpan).
        ///
        /// Jalur yang tidak ada di komputer ini - profil dibawa dari komputer
        /// lain - jatuh ke nama folder terakhirnya, sehingga penyesuaian antar
        /// perangkat tetap bekerja seperti dulu.
        /// </summary>
        public BinPackage Find(BinKind kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.IndexOf('\\') >= 0 || id.IndexOf('/') >= 0)
            {
                var tepat = Packages.FirstOrDefault(p => p.Kind == kind && SamaJalur(p.Path, id));
                if (tepat != null) return tepat;
                id = Path.GetFileName(id.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(id)) return null;
            }
            return Packages.FirstOrDefault(p => p.Kind == kind
                && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Dua nilai profil menunjuk paket yang sama (atau sama-sama kosong).</summary>
        public bool SamaPaket(BinKind kind, string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
            var pa = Find(kind, a);
            var pb = Find(kind, b);
            if (pa == null || pb == null) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            return SamaJalur(pa.Path, pb.Path);
        }

        public IEnumerable<BinPackage> Of(BinKind kind) { return Packages.Where(p => p.Kind == kind); }

        /// <summary>
        /// Paket yang dipakai untuk sebuah entri profil, DENGAN penyesuaian
        /// kalau yang tercatat tidak ada di komputer ini.
        ///
        /// Berkas profil menyimpan NAMA folder, dan nama itu adalah keadaan
        /// komputer tempat profil dibuat. Dibawa ke komputer lain, namanya kerap
        /// tidak cocok - versi PHP-nya beda, atau Laragon-nya tidak terpasang.
        /// Dulu hasilnya null, dan php.ini ditulis tanpa PHP sama sekali:
        /// Apache menyala, tapi berkas .php diunduh mentah alih-alih dijalankan,
        /// tanpa satu pun pesan yang menyebutkan sebabnya.
        ///
        /// Berkas profilnya TIDAK diubah. Kalau diubah, membawa profil kembali
        /// ke komputer asal akan menemukannya sudah tertimpa.
        /// </summary>
        public BinPackage Pakai(BinKind jenis, string id)
        {
            // Kosong berarti TIDAK DIPAKAI, dan itu pilihan yang disengaja -
            // ada entri "(tidak dipakai)" di ComboBox-nya. Menggantinya dengan
            // versi tertinggi yang kebetulan terpasang adalah kebalikan dari
            // yang diminta orang.
            //
            // Akibatnya nyata dan tidak terlihat sama sekali: profil tanpa
            // MySQL tetap menyalakan MySQL, dan yang dipilih adalah versi
            // tertinggi di seluruh folder bin yang dipindai - termasuk milik
            // XAMPP atau Laragon. Di mesin penulis, profil yang MySQL-nya
            // "(tidak dipakai)" menyalakan MariaDB 10.1.38 dari D:\xampp di
            // port 3306, dan panel Perhatian tidak berkata apa-apa.
            //
            // Penggantian di bawah memang untuk keadaan LAIN: id yang DISEBUT
            // profil tapi tidak ada di komputer ini - penyesuaian antar
            // perangkat. Penyesuaian() sudah memakai batas yang sama sejak
            // awal; hanya di sinilah keduanya tidak sepakat.
            if (string.IsNullOrEmpty(id)) return null;

            var tepat = Find(jenis, id);
            if (tepat != null) return tepat;

            var tersedia = Of(jenis).ToList();
            if (tersedia.Count == 0) return null;

            // Apache dipilih lewat pencocokan toolset, bukan sekadar versi
            // tertinggi: Apache VS16 dengan PHP VC11 mati seketika tanpa pesan.
            if (jenis == BinKind.Apache)
                return ProfileStore.PickApache(Pakai(BinKind.Php, Active != null ? Active.PhpId : null),
                                               tersedia);
            return tersedia.OrderByDescending(x => x.Parsed).FirstOrDefault();
        }

        public BinPackage Php { get { return Active == null ? null : Pakai(BinKind.Php, Active.PhpId); } }
        public BinPackage Apache { get { return Active == null ? null : Pakai(BinKind.Apache, Active.ApacheId); } }
        public BinPackage Nginx { get { return Active == null ? null : Pakai(BinKind.Nginx, Active.NginxId); } }
        public BinPackage MySql { get { return Active == null ? null : Pakai(BinKind.MySql, Active.MySqlId); } }

        /// <summary>
        /// Penyesuaian yang sedang berlaku, untuk dilaporkan ke layar. Diam-diam
        /// memakai versi lain lebih buruk daripada gagal terang-terangan.
        /// </summary>
        public List<string> Penyesuaian()
        {
            var pesan = new List<string>();
            if (Active == null) return pesan;

            Action<BinKind, string, string> periksa = (jenis, id, sebutan) =>
            {
                if (string.IsNullOrEmpty(id)) return;
                if (Find(jenis, id) != null) return;
                var ganti = Pakai(jenis, id);
                pesan.Add(ganti == null
                    ? sebutan + " \"" + id + "\" yang dicatat profil tidak ada di komputer ini, "
                      + "dan tidak ada gantinya. Pasang versinya di halaman Versi, atau pilih yang lain di Profil."
                    : sebutan + " \"" + id + "\" tidak ada di komputer ini; Phoron memakai \""
                      + ganti.Id + "\". Simpan di halaman Profil kalau ingin penggantian ini tetap.");
            };

            periksa(BinKind.Php, Active.PhpId, "Versi PHP");
            if (Active.WebServer == "nginx") periksa(BinKind.Nginx, Active.NginxId, "Versi Nginx");
            else periksa(BinKind.Apache, Active.ApacheId, "Versi Apache");
            periksa(BinKind.MySql, Active.MySqlId, "Versi MySQL");

            // Versi PHP per situs yang tidak ada di komputer ini. Pilihannya
            // TIDAK dihapus dari profil - dibawa pulang ke komputer asalnya,
            // situs itu harus kembali ke versinya sendiri.
            foreach (var kv in Active.PhpPerSitus)
                if (Find(BinKind.Php, kv.Value) == null)
                    pesan.Add("Situs " + Path.GetFileName(kv.Key.TrimEnd('\\')) + " memakai PHP \""
                              + kv.Value + "\", yang tidak ada di komputer ini; sementara ia "
                              + "dilayani PHP profil.");
            return pesan;
        }

        static bool SamaJalur(string a, string b)
        {
            return string.Equals((a ?? "").TrimEnd('\\'), (b ?? "").TrimEnd('\\'),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Paket PHP yang sungguh melayani situs ini: pilihannya sendiri bila ada
        /// dan terpasang, selain itu PHP profil.
        /// </summary>
        public BinPackage PhpUntuk(Site s)
        {
            if (s != null && Active != null)
                foreach (var kv in Active.PhpPerSitus)
                    if (SamaJalur(kv.Key, s.Path))
                    {
                        var p = Find(BinKind.Php, kv.Value);
                        if (p != null) return p;
                    }
            return Php;
        }

        /// <summary>
        /// Kolam php-cgi untuk situs yang memakai versi PHP sendiri - satu kolam
        /// per VERSI, bukan per situs.
        ///
        /// Daftar ekstensi kolam diambil dari profil yang memakai versi itu
        /// sebagai PHP utamanya, bukan dari profil aktif: oci8_11g milik PHP 5.6
        /// tidak ada di PHP 8.3, yang membawa oci8_19. Tanpa profil semacam itu,
        /// dipakai daftar yang disarankan untuk build tersebut.
        /// </summary>
        List<ConfigWriter.Kolam> SusunKolam()
        {
            var hasil = new List<ConfigWriter.Kolam>();
            if (Active == null || Active.PhpPerSitus.Count == 0) return hasil;
            var perVersi = new Dictionary<string, ConfigWriter.Kolam>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Active.PhpPerSitus)
            {
                // Folder yang sudah dihapus atau berada di luar folder proyek
                // profil ini: tidak ada yang perlu dilayani.
                var situs = Sites.FirstOrDefault(s => SamaJalur(s.Path, kv.Key));
                if (situs == null) continue;
                var php = Find(BinKind.Php, kv.Value);
                if (php == null) continue;   // dilaporkan Penyesuaian()
                // Sama dengan PHP profil: mod_php-nya sudah melayani. Dibandingkan
                // lewat JALUR - dua paket bisa bernama folder sama padahal berbeda.
                if (Php != null && SamaJalur(php.Path, Php.Path)) continue;

                ConfigWriter.Kolam k;
                if (!perVersi.TryGetValue(php.Path, out k))
                {
                    var sumber = Profiles.FirstOrDefault(p =>
                        p.PhpExtensions.Count > 0 && Find(BinKind.Php, p.PhpId) != null
                        && SamaJalur(Find(BinKind.Php, p.PhpId).Path, php.Path));
                    k = new ConfigWriter.Kolam
                    {
                        Php = php,
                        Ekstensi = sumber != null
                            ? new List<string>(sumber.PhpExtensions)
                            : ConfigWriter.EkstensiDisarankan(php),
                        Timpa = sumber != null
                            ? new Dictionary<string, string>(sumber.PhpIniOverrides, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    };
                    perVersi[php.Path] = k;
                    hasil.Add(k);
                }
                k.Situs.Add(situs);
            }
            return hasil;
        }
        public BinPackage WebPackage
        {
            get { return Active != null && Active.WebServer == "nginx" ? Nginx : Apache; }
        }

        // --------------------------------------------------------------- Profil

        /// <summary>
        /// Jadikan sebuah profil aktif: tulis ulang seluruh konfigurasi, segarkan
        /// vhost dan berkas hosts. Layanan yang sedang jalan dimatikan lebih dulu -
        /// menukar versi di bawah proses yang hidup hanya menghasilkan keadaan
        /// setengah jadi yang membingungkan.
        /// </summary>
        public async Task<List<string>> SwitchAsync(Profile profile, bool restartIfRunning = true)
        {
            // Satu ganti profil dalam satu waktu. Dua yang berjalan bersamaan
            // (dobel klik, atau Beranda dan Profil berbarengan) saling membaca
            // keadaan setengah jadi milik yang lain, dan stop milik yang kedua
            // membatalkan start milik yang pertama - hasilnya layanan mati.
            await _gantiProfil.WaitAsync();
            try
            {
                if (ModeJarak) return await GantiProfilJarakAsync(profile, restartIfRunning);
                // "Hidup" termasuk yang MASIH menyala. Dulu hanya Jalan yang
                // dimatikan: MySQL yang sedang memulihkan InnoDB dibiarkan,
                // my.ini ditulis ulang untuk profil baru, lalu start lamanya
                // selesai dan menyala hijau dengan versi dan port profil lama.
                bool webWasRunning = Hidup(Services.WebState);
                bool dbWasRunning = Hidup(Services.DbState);
                if (webWasRunning || dbWasRunning)
                {
                    Say("Mematikan layanan sebelum ganti profil...");
                    if (dbWasRunning) await _svc.StopDbGracefullyAsync(Active ?? profile, MySql);
                    if (webWasRunning) await _svc.StopWebAsync();
                }

                Active = profile;
                Settings.ActiveProfile = profile != null ? profile.FileName : "";
                Settings.Save();
                var warnings = Apply();

                if (restartIfRunning && (webWasRunning || dbWasRunning))
                {
                    // Sesudah stop milik ganti profil sendiri: Stop dari
                    // pengguna selagi MySQL menyala lagi membatalkan web juga.
                    int gw = _svc.GiliranKini(ServiceKind.Web), gd = _svc.GiliranKini(ServiceKind.Db);
                    if (dbWasRunning) await StartDbAsync(gd);
                    if (webWasRunning) await StartWebAsync(gw);
                }
                return warnings;
            }
            finally { _gantiProfil.Release(); }
        }

        readonly System.Threading.SemaphoreSlim _gantiProfil = new System.Threading.SemaphoreSlim(1, 1);

        /// <summary>Layanan yang sedang atau akan menyala - harus dihentikan sebelum ganti profil.</summary>
        public static bool Hidup(ServiceState s)
        {
            return s == ServiceState.Jalan || s == ServiceState.Menyalakan;
        }

        /// <summary>Tulis ulang konfigurasi profil aktif tanpa menyentuh layanan.</summary>
        public List<string> Apply()
        {
            if (Active == null) return new List<string> { "Belum ada profil." };
            if (ModeJarak) return TerapkanJarak();
            PastikanHalamanSambutan();
            var awal = PastikanSertifikat();
            RefreshSites();
            _svc.LogRinci = Settings.LogRinci;
            LastBuild = ConfigWriter.Build(Active, Php, Apache, MySql, Nginx,
                                           Settings.AutoVhost ? Sites : new List<Site>(),
                                           Settings.PhpIniKeFolderPhp, Settings.LogRinci,
                                           Settings.BerandaDiAkar, Settings.Opcache,
                                           SusunKolam(), Settings.AksesJaringan);
            // Masalah folder proyek disampaikan bersama peringatan konfigurasi -
            // kalau tidak, satu folder yang salah ketik hanya berwujud situs yang
            // hilang dari daftar tanpa sebab yang terlihat.
            LastBuild.Warnings.AddRange(MasalahMuat);
            if (!string.IsNullOrEmpty(Core.Settings.KeluhanTerakhir))
                LastBuild.Warnings.Add(Core.Settings.KeluhanTerakhir);
            LastBuild.Warnings.AddRange(SiteWarnings);
            LastBuild.Warnings.AddRange(awal);
            var kembar = PaketKembar();
            LastBuild.Warnings.AddRange(kembar);
            LastBuild.Warnings.AddRange(Penyesuaian());

            // Daftar ekstensi yang diambil alih dari php.ini dasar disimpan ke
            // profil, bukan dibiarkan tersirat: begitu tersimpan, daftarnya
            // terlihat dan bisa disunting di halaman Ekstensi PHP, dan tidak
            // berubah lagi kalau berkas dasarnya kelak ikut berubah.
            if (LastBuild.AdoptedExtensions != null && LastBuild.AdoptedExtensions.Count > 0)
            {
                Active.PhpExtensions = new List<string>(LastBuild.AdoptedExtensions);
                ProfileStore.Save(Active);
                Say("Profil \"" + Active.Name + "\" mengambil alih " + Active.PhpExtensions.Count
                    + " ekstensi dari php.ini yang sudah ada: "
                    + string.Join(", ", Active.PhpExtensions) + ".");
            }
            if (Settings.ManageHosts) SyncHosts(LastBuild.Warnings);
            // Dipindai ulang SETELAH vhost ditulis DAN sesudah berkas hosts
            // disinkronkan - sekali saja, di sini. Pemindaian sebelum Build
            // terjadi saat kedua hal itu belum terjadi, jadi kolom "vhost" dan
            // "hosts" di halaman Situs akan menunjukkan "-" untuk semua situs
            // padahal keduanya baru saja ditulis.
            //
            // Dulu ada tiga pemindaian di satu Apply: satu sebelum Build, satu
            // di sini, dan satu lagi di dalam SyncHosts - dua di antaranya
            // beruntun tanpa apa pun berubah di sela. Tiap pemindaian membaca
            // seluruh berkas hosts dan menyentuh belasan berkas per folder
            // proyek.
            RefreshSites();
            foreach (var w in LastBuild.Warnings)
            {
                // Paket kembar adalah keadaan komputer, bukan kejadian: ia tidak
                // berubah di antara dua Apply. Dulu dicatat ulang di SETIAP Apply
                // - di log pengguna dua kali tiap start, setiap hari - sampai
                // orang berhenti membaca panel peringatan sama sekali. Cukup
                // sekali per sesi; LastBuild.Warnings tetap memuatnya untuk layar.
                if (kembar.Contains(w) && !_kembarTercatat.Add(w)) continue;
                Say("Peringatan: " + w);
            }
            Say("Konfigurasi profil \"" + Active.Name + "\" ditulis ulang.");
            return LastBuild.Warnings;
        }

        /// <summary>Peringatan paket kembar yang sudah dicatat di sesi ini.</summary>
        readonly HashSet<string> _kembarTercatat = new HashSet<string>();

        /// <summary>
        /// Profil menyimpan versi sebagai NAMA FOLDER saja. Kalau dua folder bin
        /// memuat nama yang sama persis, nama itu tidak lagi menunjuk satu paket
        /// tertentu dan yang terpakai adalah yang pertama ditemukan - diam-diam,
        /// dan bisa berubah kalau urutan folder bin diubah. Karena itu keadaan
        /// ini disebutkan, bukan dibiarkan.
        /// </summary>
        List<string> PaketKembar()
        {
            var pesan = new List<string>();
            if (Active == null) return pesan;
            var dipakai = new[]
            {
                new { Jenis = BinKind.Php, Id = Active.PhpId },
                new { Jenis = BinKind.Apache, Id = Active.ApacheId },
                new { Jenis = BinKind.Nginx, Id = Active.NginxId },
                new { Jenis = BinKind.MySql, Id = Active.MySqlId },
            };
            foreach (var d in dipakai)
            {
                if (string.IsNullOrEmpty(d.Id)) continue;
                // Jalur lengkap menunjuk tepat satu paket - tidak ada yang ambigu.
                if (d.Id.IndexOf('\\') >= 0 || d.Id.IndexOf('/') >= 0) continue;
                var cocok = Packages.Where(p => p.Kind == d.Jenis
                    && string.Equals(p.Id, d.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (cocok.Count < 2) continue;
                pesan.Add("Nama folder \"" + d.Id + "\" ada di lebih dari satu folder bin ("
                          + string.Join(", ", cocok.Select(c => c.SourceRoot))
                          + "). Yang dipakai adalah " + cocok[0].Path
                          + "; pilih ulang di halaman Profil untuk mengunci salinan yang Anda maksud.");
            }
            return pesan;
        }

        /// <summary>
        /// Buat sertifikat self-signed sekali saja, kalau belum ada.
        ///
        /// Tanpa ini HTTPS mati total dan kegagalannya membingungkan: browser
        /// hanya bilang "tidak dapat tersambung" tanpa petunjuk apa pun, dan
        /// Firefox kerap menaikkan sendiri http menjadi https. Sertifikatnya
        /// belum tepercaya sampai dipasang ke Trusted Root lewat tombol di
        /// Beranda - tapi port 443 sudah terbuka dan situsnya bisa dibuka.
        /// </summary>
        List<string> PastikanSertifikat()
        {
            var pesan = new List<string>();
            if (!SslTool.PerluDibuat(Active.SiteSuffix)) return pesan;
            var apache = Apache;
            if (apache == null || Active.WebServer == "nginx") return pesan;
            if (SslTool.FindOpenSsl(apache) == null) return pesan;   // paket tanpa openssl: diam saja

            // CA lama tanpa batasan nama diganti di sini, sekali. Kepercayaannya
            // ikut hilang, jadi pengguna perlu menekan "Percayai" lagi.
            bool gantiCa = SslTool.Exists;
            var err = SslTool.Generate(apache, Active.SiteSuffix, Profiles.Select(x => x.SiteSuffix));
            if (err != null) pesan.Add("HTTPS tidak aktif - " + err);
            else if (gantiCa)
                pesan.Add("CA HTTPS Phoron dibuat ulang supaya hanya sah untuk akhiran situs Phoron (*."
                          + SiteScanner.Suffix(Active) + ") dan localhost. "
                          + "Percayai sertifikatnya lagi lewat Beranda.");
            else Say("Sertifikat HTTPS dibuat untuk *." + SiteScanner.Suffix(Active)
                     + " (pasang ke Trusted Root lewat Beranda agar browser tidak memperingatkan).");
            return pesan;
        }

        /// <summary>
        /// Halaman sambutan hanya ditulis kalau folder www benar-benar kosong.
        /// Kalau sudah ada isinya - termasuk kalau pengguna menghapus halaman ini -
        /// jangan pernah menaruh berkas di folder kerja orang lain.
        /// </summary>
        void PastikanHalamanSambutan()
        {
            try
            {
                var root = SiteScanner.DocumentRoot(Active);
                if (!Directory.Exists(root)) return;
                if (Directory.EnumerateFileSystemEntries(root).Any()) return;
                File.WriteAllText(Path.Combine(root, "index.php"), HalamanSambutan(),
                    new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>Publik supaya harness uji bisa memeriksanya dengan "php -l" sungguhan.</summary>
        public static string HalamanSambutan()
        {
            return
"<?php\n" +
"$folders = array_filter(scandir(__DIR__), function ($f) {\n" +
"    return $f[0] !== '.' && is_dir(__DIR__ . DIRECTORY_SEPARATOR . $f);\n" +
"});\n" +
"?>\n" +
"<!doctype html>\n" +
"<html lang=\"id\">\n" +
"<head>\n" +
"<meta charset=\"utf-8\">\n" +
"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
"<title>Phoron</title>\n" +
"<style>\n" +
"  :root { color-scheme: light dark; }\n" +
"  body { font: 15px/1.6 system-ui, 'Segoe UI', sans-serif; margin: 0;\n" +
"         display: grid; place-items: center; min-height: 100vh;\n" +
"         background: #f6f6f8; color: #1a1a1c; }\n" +
"  @media (prefers-color-scheme: dark) { body { background: #17171a; color: #ececf0; } }\n" +
"  .kotak { width: min(620px, 90vw); padding: 32px; border-radius: 14px;\n" +
"           background: #fff; box-shadow: 0 2px 24px rgba(0,0,0,.08); }\n" +
"  @media (prefers-color-scheme: dark) { .kotak { background: #222226; box-shadow: none; } }\n" +
"  h1 { margin: 0 0 4px; font-size: 28px; }\n" +
"  .sub { opacity: .65; margin: 0 0 22px; }\n" +
"  dl { display: grid; grid-template-columns: max-content 1fr; gap: 6px 18px; margin: 0 0 22px; }\n" +
"  dt { opacity: .6; } dd { margin: 0; font-variant-numeric: tabular-nums; }\n" +
"  ul { margin: 0; padding-left: 20px; }\n" +
"  a { color: #2563eb; }\n" +
"</style>\n" +
"</head>\n" +
"<body>\n" +
"<div class=\"kotak\">\n" +
"  <h1>Phoron</h1>\n" +
"  <p class=\"sub\">Server lokal berjalan.</p>\n" +
"  <dl>\n" +
"    <dt>PHP</dt><dd><?= PHP_VERSION ?> (<?= php_sapi_name() ?>)</dd>\n" +
// Sengaja isset(), bukan "??": operator itu baru ada di PHP 7, sedangkan
// halaman ini juga harus jalan di profil PHP 5.6.
"    <dt>Server</dt><dd><?= isset($_SERVER['SERVER_SOFTWARE']) ? $_SERVER['SERVER_SOFTWARE'] : '-' ?></dd>\n" +
"    <dt>Document root</dt><dd><?= htmlspecialchars(__DIR__) ?></dd>\n" +
"  </dl>\n" +
"  <?php if ($folders): ?>\n" +
"    <p><strong>Proyek di folder ini</strong></p>\n" +
"    <ul>\n" +
"      <?php foreach ($folders as $f): ?>\n" +
"        <li><a href=\"/<?= rawurlencode($f) ?>/\"><?= htmlspecialchars($f) ?></a></li>\n" +
"      <?php endforeach; ?>\n" +
"    </ul>\n" +
"  <?php else: ?>\n" +
"    <p>Belum ada proyek. Buat satu lewat halaman <em>Situs</em> di Phoron,\n" +
"       atau salin folder proyek ke sini.</p>\n" +
"  <?php endif; ?>\n" +
"</div>\n" +
"</body>\n" +
"</html>\n";
        }

        void SyncHosts(List<string> warnings)
        {
            try
            {
                // Tanpa Virtual Host, blok Phoron dikosongkan - bukan dibiarkan.
                // Nama yang tertinggal di berkas hosts tetap menunjuk 127.0.0.1
                // dan dijawab vhost bawaan, jadi alamatnya tampak hidup padahal
                // menampilkan proyek yang salah. Hanya blok milik Phoron yang
                // disentuh; baris orang lain dijaga HostsFile sendiri.
                var names = Settings.AutoVhost
                    ? Sites.Select(s => s.HostName)
                           .Where(n => !string.IsNullOrWhiteSpace(n) && n != "localhost").ToList()
                    : new List<string>();
                HostsFile.Sync(names);
                // Pemindaian ulangnya urusan Apply, yang memanggil ini: di sana
                // ia dilakukan sekali untuk vhost dan hosts sekaligus.
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("Berkas hosts tidak bisa ditulis - jalankan Phoron sebagai Administrator "
                             + "atau matikan \"Sinkronkan berkas hosts Windows\" di Pengaturan.");
            }
            catch (Exception ex) { warnings.Add("Gagal menyunting hosts: " + ex.Message); }
        }

        // -------------------------------------------------------------- Layanan

        public Task<bool> StartWebAsync() { return StartWebAsync(null); }

        /// <param name="awal">Giliran web saat aksi pengguna dimulai (lihat
        /// ServiceManager.GiliranKini); Stop sesudahnya membatalkan start ini.</param>
        Task<bool> StartWebAsync(int? awal)
        {
            if (ModeJarak) return NyalakanJarakAsync("nyalakan_web", ServiceKind.Web);
            if (Active == null) return Task.FromResult(false);
            // Tidak memakai web server adalah pilihan, bukan kegagalan. Dulu
            // jalur ini berakhir di ServiceManager yang menyetel keadaan jadi
            // Gagal - titik merah di layar dan baris "Profil belum menunjuk web
            // server" setiap kali tombol Nyalakan ditekan, untuk profil yang
            // memang sengaja hanya menjalankan MySQL.
            if (!Active.PakaiWeb)
            {
                Say("Profil \"" + Active.Name + "\" tidak memakai web server, jadi tidak ada yang dinyalakan.");
                return Task.FromResult(false);
            }
            // SELALU dibangun ulang, bukan hanya saat LastBuild masih kosong.
            // Bentuk lamanya - "bangun kalau belum pernah" - berarti konfigurasi
            // ditulis SEKALI seumur proses. Sesudah itu, profil yang disunting
            // tidak pernah sampai ke berkas yang benar-benar dibaca server, dan
            // yang menyala adalah keadaan berjam-jam yang lalu. Membangun ulang
            // memang makan waktu, tapi menyalakan layanan bukan hal yang
            // dilakukan tiap detik - dan layanan yang menyala dengan konfigurasi
            // yang bukan miliknya adalah kesalahan yang paling sulit dilacak.
            Apply();
            return CatatSaatNyala(ServiceKind.Web, ConfigWriter.VersiKonfigWeb,
                                  _svc.StartWebAsync(Active, WebPackage, Php, LastBuild, awal));
        }

        // Versi konfigurasi yang dibaca layanan saat ia terakhir menyala - lihat
        // ConfigWriter.VersiKonfigWeb. -1 berarti belum pernah menyala di sesi ini.
        int _versiWebNyala = -1, _versiDbNyala = -1;

        async Task<bool> CatatSaatNyala(ServiceKind jenis, int versi, Task<bool> start)
        {
            var ok = await start;
            if (ok)
            {
                if (jenis == ServiceKind.Web) _versiWebNyala = versi;
                else _versiDbNyala = versi;
            }
            return ok;
        }

        /// <summary>
        /// Web server sedang jalan, tapi konfigurasinya sudah berubah di cakram
        /// sejak ia menyala - jadi yang dilayaninya masih keadaan lama.
        /// </summary>
        public bool PerluRestartWeb
        {
            get
            {
                if (ModeJarak) return _perluRestartWebJarak;
                return Services.WebState == ServiceState.Jalan && _versiWebNyala >= 0
                       && ConfigWriter.VersiKonfigWeb != _versiWebNyala;
            }
        }

        /// <summary>Sama dengan <see cref="PerluRestartWeb"/>, untuk my.ini dan MySQL.</summary>
        public bool PerluRestartDb
        {
            get
            {
                if (ModeJarak) return _perluRestartDbJarak;
                return Services.DbState == ServiceState.Jalan && _versiDbNyala >= 0
                       && ConfigWriter.VersiKonfigDb != _versiDbNyala;
            }
        }

        public Task StopWebAsync() { return ModeJarak ? AksiJarakAsync("matikan_web", null, BatasLama) : _svc.StopWebAsync(); }

        public Task<bool> StartDbAsync() { return StartDbAsync(null); }

        Task<bool> StartDbAsync(int? awal)
        {
            if (ModeJarak) return NyalakanJarakAsync("nyalakan_db", ServiceKind.Db);
            if (Active == null) return Task.FromResult(false);
            if (!Active.PakaiMySql)
            {
                Say("Profil \"" + Active.Name + "\" tidak memakai MySQL, jadi tidak ada yang dinyalakan.");
                return Task.FromResult(false);
            }
            Apply();   // lihat alasannya di StartWebAsync
            return CatatSaatNyala(ServiceKind.Db, ConfigWriter.VersiKonfigDb,
                                  _svc.StartDbAsync(Active, MySql, awal));
        }

        public Task StopDbAsync() { return ModeJarak ? AksiJarakAsync("matikan_db", null, BatasLama) : _svc.StopDbGracefullyAsync(Active, MySql); }

        public async Task StartAllAsync()
        {
            if (ModeJarak) { await AksiJarakAsync("nyalakan_semua", null, BatasLama); return; }
            // Dipotret sebelum MySQL: "Matikan semua" selagi MySQL masih
            // menyala dulu membatalkan MySQL lalu tetap menyalakan Apache.
            int gw = _svc.GiliranKini(ServiceKind.Web), gd = _svc.GiliranKini(ServiceKind.Db);
            await StartDbAsync(gd);
            await StartWebAsync(gw);
        }

        public async Task StopAllAsync()
        {
            if (ModeJarak) { await AksiJarakAsync("matikan_semua", null, BatasLama); return; }
            await StopWebAsync();
            await StopDbAsync();
        }

        // ------------------------------------------------------------- Kemudahan

        /// <summary>Variabel lingkungan untuk terminal/komposer: PHP dan MySQL profil aktif di depan PATH.</summary>
        /// <summary>
        /// Lingkungan untuk terminal yang dibuka Phoron.
        ///
        /// Dengan situs: PHP yang melayani SITUS ITU yang ada di PATH, bukan PHP
        /// profil. Proyek Laravel yang butuh PHP 8.2 tetap bisa menjalankan
        /// php artisan dan composer walau profilnya PHP 5.6.
        /// </summary>
        public IDictionary<string, string> ToolEnv(Site situs = null)
        {
            var php = situs != null ? PhpUntuk(situs) : Php;
            bool milikProfil = php != null && Php != null && SamaJalur(php.Path, Php.Path);
            var parts = new List<string>();
            if (php != null) parts.Add(php.Path);
            if (MySql != null) parts.Add(Path.Combine(MySql.Path, "bin"));
            var composer = Path.Combine(Paths.Bin, "composer");
            if (Directory.Exists(composer)) parts.Add(composer);
            var env = new Dictionary<string, string>
            {
                { "PATH", string.Join(";", parts) + ";" + Environment.GetEnvironmentVariable("PATH") },
            };
            // php.ini kolam selalu di etc\php\<versi>, apa pun sakelar "tulis ke
            // folder PHP" - sakelar itu hanya berlaku untuk PHP profil.
            if (php != null)
                env["PHPRC"] = ConfigWriter.FolderPhpIni(php, milikProfil && Settings.PhpIniKeFolderPhp);
            return env;
        }

        /// <summary>
        /// Alamat sebuah situs, MENGIKUTI keadaan Virtual Host.
        ///
        /// Tanpa Virtual Host, nama seperti toko.test tidak dilayani vhost mana
        /// pun. Menampilkannya tetap membuat orang mengira alamat itu bekerja -
        /// dan selama namanya masih tertinggal di berkas hosts, ia memang
        /// "menjawab", tapi yang menjawab adalah vhost bawaan dengan isi proyek
        /// LAIN, atau galat 500. Alamat yang salah lebih menyesatkan daripada
        /// alamat yang jelas-jelas tidak ada.
        ///
        /// Yang benar-benar bekerja tanpa vhost adalah jalur di bawah akar
        /// utama. Situs dari folder proyek KEDUA dan seterusnya tidak terjangkau
        /// sama sekali - untuk itu dikembalikan kosong, dan layar menampilkannya
        /// apa adanya.
        /// </summary>
        public string SiteUrl(Site site)
        {
            if (Active == null || site == null) return "http://localhost/";
            var port = Active.HttpPort == 80 ? "" : ":" + Active.HttpPort;
            if (Settings.AutoVhost) return "http://" + site.HostName + port + "/";

            var utama = SiteScanner.DocumentRoot(Active);
            if (string.IsNullOrEmpty(utama) || string.IsNullOrEmpty(site.Path)) return "";
            var jalur = site.Path.TrimEnd('\\');
            var akar = utama.TrimEnd('\\');
            // Dibandingkan pada BATAS FOLDER, bukan sebagai teks biasa. Tanpa
            // pemisah di belakang, "proyek-lain" dikira berada di dalam
            // "proyek", dan alamatnya keluar sebagai http://localhost/-lain/...
            if (!string.Equals(jalur, akar, StringComparison.OrdinalIgnoreCase)
                && !jalur.StartsWith(akar + Path.DirectorySeparatorChar,
                                     StringComparison.OrdinalIgnoreCase)) return "";

            var relatif = jalur.Substring(akar.Length).Trim('\\').Replace('\\', '/');
            return "http://localhost" + port + "/" + (relatif.Length > 0 ? relatif + "/" : "");
        }

        // ---------------------------------------------------- Situs dan proyek

        /// <summary>
        /// Buka situs di browser dan ingat bahwa ia baru dibuka. Mengembalikan
        /// alamatnya, atau null bila situs ini tidak punya alamat tanpa Virtual
        /// Host (ia ada di folder proyek kedua).
        /// </summary>
        public string BukaSitus(Site site)
        {
            if (site == null) return null;
            var url = SiteUrl(site);
            if (string.IsNullOrEmpty(url)) return null;
            CatatSitusDibuka(site.Path);
            Shell.Open(url);
            return url;
        }

        /// <summary>Taruh situs ini paling depan di daftar terakhir dibuka.</summary>
        public void CatatSitusDibuka(string path)
        {
            if (string.IsNullOrEmpty(path) || Settings == null) return;
            var daftar = Settings.SitusTerakhir ?? new List<string>();
            daftar.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            daftar.Insert(0, path);
            while (daftar.Count > Core.Settings.BatasSitusTerakhir) daftar.RemoveAt(daftar.Count - 1);
            Settings.SitusTerakhir = daftar;
            // Mode daemon: daemon yang mencatat ke phoron.ini; salinan di atas
            // hanya supaya daftar di layar langsung benar.
            if (ModeJarak) { CatatSitusJarak(path); return; }
            try { Settings.Save(); } catch { /* daftar ini kenyamanan, bukan data */ }
        }

        /// <summary>
        /// Situs yang terakhir dibuka dan masih ada di profil ini, terbaru dulu.
        /// Folder yang sudah dihapus atau milik profil lain dilewati diam-diam.
        /// </summary>
        public List<Site> SitusTerakhirDaftar(int maks)
        {
            var hasil = new List<Site>();
            if (Settings == null || Settings.SitusTerakhir == null) return hasil;
            foreach (var p in Settings.SitusTerakhir)
            {
                var s = Sites.FirstOrDefault(x => string.Equals(x.Path, p, StringComparison.OrdinalIgnoreCase));
                if (s != null) hasil.Add(s);
                if (hasil.Count >= maks) break;
            }
            return hasil;
        }

        /// <summary>
        /// Jalankan proyek Node dengan Node pilihannya. Mengembalikan pesan galat,
        /// atau null bila prosesnya dimulai. Dipakai halaman Node, Beranda, dan
        /// menu baki - satu jalur, supaya ketiganya berperilaku sama.
        /// </summary>
        public string JalankanNode(NodeApp app) { return JalankanNode(app, 0); }

        /// <summary>Seperti <see cref="JalankanNode(NodeApp)"/>, dengan port untuk kali ini saja.</summary>
        public string JalankanNode(NodeApp app, int portSekali)
        {
            if (app == null) return Lang.T("Proyek belum dipilih.");
            if (ModeJarak) return JalankanNodeJarak(app, portSekali);
            var node = string.IsNullOrEmpty(app.NodeId) ? null : Find(BinKind.Node, app.NodeId);
            var err = _node.Start(app, node, portSekali);
            if (err == null) Say(Lang.T("Menjalankan {0} ({1} {2}).", app.DisplayName, app.Manager, app.Script)
                                 + (portSekali > 0 ? " · port " + portSekali : ""));
            return err;
        }

        /// <summary>
        /// Port bebas pertama mulai dari <paramref name="mulai"/>, melewati port
        /// web server dan MySQL profil ini. 0 bila 50 port berturut-turut terpakai.
        /// </summary>
        public int CariPortBebas(int mulai)
        {
            if (mulai <= 0) mulai = 3001;
            for (int p = mulai; p < mulai + 50 && p < 65536; p++)
            {
                if (Active != null && (p == Active.HttpPort || p == Active.HttpsPort || p == Active.MySqlPort)) continue;
                if (PortCheck.IsFree(p)) return p;
            }
            return 0;
        }

        /// <summary>Port yang ditawarkan untuk proyek yang gagal karena portnya terpakai; 0 bila tidak ada.</summary>
        public int PortTawaran(NodeApp app)
        {
            if (app == null) return 0;
            var bentrok = Node.PortBentrok(app.Path);
            return bentrok > 0 ? CariPortBebas(bentrok + 1) : 0;
        }

        public string RootUrl()
        {
            if (Active == null) return "http://localhost/";
            return "http://localhost" + (Active.HttpPort == 80 ? "" : ":" + Active.HttpPort) + "/";
        }
    }
}
