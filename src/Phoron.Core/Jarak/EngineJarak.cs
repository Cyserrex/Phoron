using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Phoron.Core.Jarak;

namespace Phoron.Core
{
    /// <summary>
    /// Engine dalam mode jarak: UI sebagai klien phorond.
    ///
    /// Pembagiannya mengikuti protokol v2. UI tetap MEMBACA berkas sendiri
    /// (setelan, profil, paket, situs, apps.ini) karena isinya sama persis
    /// dengan yang dibaca daemon. Yang dikirim lewat pipe hanya AKSI -
    /// menulis config, menyalakan, mematikan, menjalankan Node - dan penulisan
    /// phoron.ini. Keadaan layanan dan Node datang sebagai kabar.
    /// </summary>
    public partial class Engine
    {
        KlienDaemon _klien;
        LayananJarak _layananJarak;
        NodeJarak _nodeJarak;
        volatile bool _perluRestartWebJarak, _perluRestartDbJarak;

        /// <summary>UI ini klien phorond; layanan tidak berjalan di proses ini.</summary>
        public bool ModeJarak { get { return _klien != null; } }

        public KlienDaemon Klien { get { return _klien; } }

        /// <summary>Hak Administrator milik DAEMON (yang menulis hosts dan sertifikat).</summary>
        public bool AdminDaemon { get; private set; }

        /// <summary>
        /// Hak Administrator milik proses yang benar-benar menulis hosts dan
        /// sertifikat: daemon dalam mode jarak, proses ini dalam mode lama.
        /// Halaman memakai ini, bukan HostsFile.IsAdmin(): dalam mode daemon
        /// hak UI tidak menentukan apa pun.
        /// </summary>
        public bool HakAdmin { get { return ModeJarak ? AdminDaemon : HostsFile.IsAdmin(); } }

        /// <summary>Keluhan muat dari daemon (Keadaan.masalah_muat), sudah diterjemahkan.</summary>
        public List<string> MasalahDaemon { get { return _masalahDaemon; } }
        volatile List<string> _masalahDaemon = new List<string>();

        /// <summary>
        /// Menjalankan pekerjaan di utas layar. Kabar datang di utas pembaca
        /// pipe, sedangkan memuat ulang profil mengganti daftar yang sedang
        /// digambar layar. Null = jalankan di tempat (uji).
        /// </summary>
        public Action<Action> PenjadwalUi;

        /// <summary>Berkas dimuat ulang karena kabar perlu_muat_ulang (perubahan dari luar UI ini).</summary>
        public event Action BerubahDariLuar;

        /// <summary>Daemon berpamitan - UI ikut keluar.</summary>
        public event Action Pamit;

        static readonly TimeSpan BatasLama = TimeSpan.FromMinutes(10);
        static readonly TimeSpan BatasSedang = TimeSpan.FromSeconds(90);

        /// <summary>Muat berkas sendiri, aksi lewat <paramref name="klien"/>.</summary>
        public Engine(KlienDaemon klien) : this(klien, null) { }

        /// <param name="penjadwalUi">
        /// Penjadwal ke utas layar, dipasang SEBELUM langganan kabar. Kabar bisa
        /// tiba begitu klien terhubung; tanpa penjadwal sejak awal, muat ulang
        /// dari kabar pertama berjalan di utas pembaca pipe dan menyentuh
        /// kontrol WPF dari sana - Phoron mati saat dibuka (ShowConflicts).
        /// </param>
        public Engine(KlienDaemon klien, Action<Action> penjadwalUi)
        {
            if (klien == null) throw new ArgumentNullException("klien");
            _klien = klien;
            PenjadwalUi = penjadwalUi;
            var hasil = Core.Settings.Muat();
            Settings = hasil.Setelan;
            foreach (var k in hasil.Keluhan)
                MasalahMuat.Add("phoron.ini: " + k);
            // phoron.ini TIDAK ditulis di sini walau belum ada: daemon yang
            // membuatnya, dan daemon satu-satunya penulisnya.
            Settings.PenyimpanJarak = SimpanSetelanJarak;
            PotretSetelan();
            Packages = new List<BinPackage>();
            Profiles = new List<Profile>();
            Sites = new List<Site>();
            NodeAppsList = new List<NodeApp>();
            Services = _layananJarak = new LayananJarak(klien);
            Node = _nodeJarak = new NodeJarak(klien);

            klien.Kabar += TerimaKabar;
            klien.Tersambung += SesudahSambungUlang;
            BerkasPengguna.Berubah += BerkasPenggunaBerubah;
            if (klien.Aktif) Sinkronkan();
        }

        /// <summary>Lepas semua langganan (uji, atau UI yang keluar).</summary>
        public void Lepas()
        {
            if (_klien == null) return;
            _klien.Kabar -= TerimaKabar;
            _klien.Tersambung -= SesudahSambungUlang;
            BerkasPengguna.Berubah -= BerkasPenggunaBerubah;
        }

        /// <summary>Banyaknya jabat tangan sesudah Engine dibuat (sambung ulang).</summary>
        int _jumlahSambung;

        void SesudahSambungUlang()
        {
            Interlocked.Increment(ref _jumlahSambung);
            // Daemon yang baru dihidupkan lagi tidak tahu apa yang dilihat UI;
            // ambil ulang seluruh keadaannya. Yang tertunda selama pipe putus -
            // profil yang disimpan, setelan yang gagal terkirim - dikirim lagi.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (Interlocked.Exchange(ref _muatUlangSaatSambung, 0) == 1) BerkasPenggunaBerubah();
                    if (_setelanTertunda) Settings.Save();
                    Sinkronkan();
                }
                catch { }
            });
        }

        /// <summary>Keadaan lengkap dan riwayat log dari daemon.</summary>
        void Sinkronkan()
        {
            // Hasilnya diterapkan DI UTAS PEMBACA, berurutan dengan kabar: kabar
            // yang tiba sesudah jawaban ini lebih baru dan tidak boleh tertimpa.
            Tunggu(KirimKeadaanAsync());
            Tunggu(_klien.KirimAsync("riwayat_log", null, TimeSpan.FromSeconds(15),
                                     j => { if (j.Ok) TerapkanRiwayat(j.Hasil); }));
        }

        static void Tunggu(Task t)
        {
            try { t.Wait(TimeSpan.FromSeconds(20)); }
            catch (AggregateException) { }
        }

        Task<Jawaban> KirimKeadaanAsync()
        {
            return _klien.KirimAsync("keadaan", null, TimeSpan.FromSeconds(15), j => { if (j.Ok) TerapkanKeadaan(j.Hasil); });
        }

        int _keadaanTertunda;

        /// <summary>Minta keadaan di latar, paling banyak satu permintaan sekaligus.</summary>
        void SegarkanKeadaanLatar()
        {
            if (Interlocked.Exchange(ref _keadaanTertunda, 1) == 1) return;
            KirimKeadaanAsync().ContinueWith(t =>
            {
                Interlocked.Exchange(ref _keadaanTertunda, 0);
                var _ = t.Exception;
            }, TaskScheduler.Default);
        }

        async Task SegarkanKeadaanAsync()
        {
            try { await KirimKeadaanAsync(); } catch (GalatDaemon) { }
        }

        void TerapkanKeadaan(object hasil)
        {
            var d = Protokol.Objek(hasil);
            if (d == null) return;
            _layananJarak.Terapkan(Protokol.Ambil(d, "status"));
            _nodeJarak.TerapkanDaftar(Protokol.Ambil(d, "node"));
            _perluRestartWebJarak = Protokol.Benar(d, "perlu_restart_web");
            _perluRestartDbJarak = Protokol.Benar(d, "perlu_restart_db");
            AdminDaemon = Protokol.Benar(d, "admin");
            _masalahDaemon = Protokol.DaftarPesan(Protokol.Ambil(d, "masalah_muat")).Select(p => p.Teks()).ToList();
            LastBuild = HasilTerapan(Protokol.DaftarPesan(Protokol.Ambil(d, "peringatan_terapkan")).Select(p => p.Teks()));
        }

        /// <summary>
        /// LastBuild versi jarak: yang dibaca UI hanya peringatannya dan folder
        /// php.ini. Jalur config lain milik daemon.
        /// </summary>
        ConfigWriter.Result HasilTerapan(IEnumerable<string> peringatan)
        {
            var r = new ConfigWriter.Result();
            try
            {
                var php = Php;
                if (php != null) r.PhpIniDir = ConfigWriter.FolderPhpIni(php, Settings.PhpIniKeFolderPhp);
            }
            catch { }
            // Keluhan muat ikut tampil seperti Apply() mode lama: keluhan UI
            // sendiri (berkas yang ia baca) digabung dengan keluhan daemon.
            // Keduanya membaca berkas yang sama, jadi kalimat yang sama cukup
            // muncul sekali.
            string[] lokal;
            try { lokal = MasalahMuat.ToArray(); } catch { lokal = new string[0]; }
            var dilihat = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in peringatan.Concat(lokal).Concat(_masalahDaemon))
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (dilihat.Add(p.Trim())) r.Warnings.Add(p);
            }
            return r;
        }

        void TerapkanRiwayat(object hasil)
        {
            var baris = Protokol.Larik(hasil).Select(BarisLogDari).Where(b => b != null).ToList();
            lock (_riwayat)
            {
                _riwayat.Clear();
                foreach (var b in baris.Skip(Math.Max(0, baris.Count - RiwayatMaks))) _riwayat.Enqueue(b);
            }
        }

        static BarisLog BarisLogDari(object o)
        {
            var d = Protokol.Objek(o);
            if (d == null) return null;
            var p = Pesan.Dari(Protokol.Ambil(d, "pesan"));
            DateTime waktu;
            if (!DateTime.TryParse(Protokol.Teks(d, "waktu") ?? "", CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out waktu))
                waktu = DateTime.Now;
            else if (waktu.Kind != DateTimeKind.Local) waktu = waktu.ToLocalTime();
            return new BarisLog { Waktu = waktu, Teks = p != null ? p.Teks() : "" };
        }

        // ------------------------------------------------------------- Kabar

        void TerimaKabar(string jenis, object data)
        {
            switch (jenis)
            {
                case "status":
                    _layananJarak.Terapkan(data);
                    // Tanda "perlu dinyalakan ulang" hanya ada di Keadaan.
                    SegarkanKeadaanLatar();
                    break;
                case "log":
                    var b = BarisLogDari(data);
                    if (b != null) SayLokal(b);
                    break;
                case "node":
                    _nodeJarak.TerapkanKabar(data);
                    break;
                case "node_keluaran":
                    _nodeJarak.TerapkanKeluaran(data);
                    break;
                case "perlu_muat_ulang":
                    KabarPerluMuatUlang();
                    break;
                case "pamit":
                    var h = Pamit;
                    if (h != null) h();
                    break;
            }
        }

        void Jadwalkan(Action kerja)
        {
            var p = PenjadwalUi;
            if (p != null) p(kerja);
            else kerja();
        }

        /// <summary>
        /// Setelan, profil, atau paket berubah di luar UI ini (menu baki, klien
        /// lain). Setelan disalin KE objek yang sama - halaman memegang
        /// rujukannya.
        /// </summary>
        public void MuatUlangDariDaemon()
        {
            try
            {
                Settings.SalinDari(Core.Settings.Load());
                // Salinan baru inilah pembanding Save berikutnya. Perubahan
                // lokal yang belum terkirim ikut tertimpa SalinDari, jadi tidak
                // ada lagi yang tertunda.
                PotretSetelan();
                _setelanTertunda = false;
            }
            catch { }
            Reload();
            var h = BerubahDariLuar;
            if (h != null) h();
        }

        // Kabar perlu_muat_ulang yang menyusul perintah UI ini sendiri (ubah
        // setelan, muat ulang, ganti profil) - "gema" - TIDAK memuat ulang
        // lagi: Reload mengganti objek profil, dan halaman Profil yang sedang
        // menyunting akan menunjuk objek yatim - alasan yang sama kenapa
        // halaman itu sengaja tidak memanggil Reload sesudah menyimpan.
        //
        // Tetapi kabar yang tiba selama aksi itu berjalan bisa juga datang
        // dari luar (menu baki, klien lain), dan aksinya boleh berjalan
        // sampai sepuluh menit. Jadi kabar itu DITAHAN, bukan dibuang: begitu
        // semua aksi penyebab selesai, tiap aksi yang berhasil menebus satu
        // kabar (gemanya); sisanya berarti ada perubahan dari luar dan UI
        // memuat ulang. Gema yang belum tiba saat aksinya selesai ditunggu
        // sebentar (BatasGema). Aksi yang bukan penyebab (menyalakan layanan,
        // terapkan) tidak menahan apa pun: kabarnya langsung dimuat.
        readonly object _kunciMuat = new object();
        int _penyebabBerjalan;
        int _kabarTertahan;
        int _gemaDitunggu;
        long _gemaSampaiTick;
        static readonly TimeSpan BatasGema = TimeSpan.FromSeconds(2);

        void MulaiPenyebab()
        {
            lock (_kunciMuat)
            {
                // Gema kumpulan aksi sebelumnya yang tidak pernah datang sudah
                // basi; tanpa ini ia akan menebus kabar dari luar berikutnya.
                if (_penyebabBerjalan == 0 && DateTime.UtcNow.Ticks >= _gemaSampaiTick) _gemaDitunggu = 0;
                _penyebabBerjalan++;
            }
        }

        /// <summary><paramref name="berhasil"/>: daemon menjalankannya, jadi gemanya akan (atau sudah) datang.</summary>
        void SelesaiPenyebab(bool berhasil)
        {
            bool muat = false;
            lock (_kunciMuat)
            {
                _penyebabBerjalan = Math.Max(0, _penyebabBerjalan - 1);
                if (berhasil) _gemaDitunggu++;
                if (_penyebabBerjalan == 0)
                {
                    int tebus = Math.Min(_gemaDitunggu, _kabarTertahan);
                    _gemaDitunggu -= tebus;
                    _kabarTertahan -= tebus;
                    muat = _kabarTertahan > 0;
                    _kabarTertahan = 0;
                    _gemaSampaiTick = (DateTime.UtcNow + BatasGema).Ticks;
                }
            }
            if (muat) Jadwalkan(MuatUlangDariDaemon);
        }

        void KabarPerluMuatUlang()
        {
            lock (_kunciMuat)
            {
                if (_penyebabBerjalan > 0) { _kabarTertahan++; return; }
                if (_gemaDitunggu > 0)
                {
                    if (DateTime.UtcNow.Ticks < _gemaSampaiTick) { _gemaDitunggu--; return; }
                    _gemaDitunggu = 0;
                }
            }
            Jadwalkan(MuatUlangDariDaemon);
        }

        // --------------------------------------------------------- Kirim aksi

        Task _muatUlangTertunda;

        /// <summary>1 = profil/apps.ini disimpan saat pipe putus; muat_ulang dikirim begitu tersambung lagi.</summary>
        int _muatUlangSaatSambung;

        void BerkasPenggunaBerubah()
        {
            if (!_klien.Aktif)
            {
                // Daemon yang hanya kehilangan sambungan (tidak restart) masih
                // memegang profil lama; ia harus diberi tahu begitu tersambung.
                Interlocked.Exchange(ref _muatUlangSaatSambung, 1);
                return;
            }
            // Langsung dikirim, dan aksi berikutnya menunggu jawabannya: daemon
            // harus sudah membaca profil baru sebelum diminta menerapkannya.
            MulaiPenyebab();
            var t = _klien.KirimAsync("muat_ulang");
            _muatUlangTertunda = t;
            t.ContinueWith(x =>
            {
                bool ok = x.Status == TaskStatus.RanToCompletion && x.Result.Ok;
                if (!ok) Interlocked.Exchange(ref _muatUlangSaatSambung, 1);
                SelesaiPenyebab(ok);
                var _ = x.Exception;
            }, TaskScheduler.Default);
        }

        void TungguMuatUlang()
        {
            var t = _muatUlangTertunda;
            if (t == null) return;
            try { t.Wait(TimeSpan.FromSeconds(30)); } catch { }
        }

        async Task TungguMuatUlangAsync()
        {
            var t = _muatUlangTertunda;
            if (t == null) return;
            try { await t; } catch { }
        }

        /// <summary>
        /// Kirim satu aksi, tunggu, dan kembalikan peringatannya (sudah
        /// diterjemahkan). Galat tidak dilempar: ia dicatat dan ikut sebagai
        /// peringatan, sama seperti layanan yang gagal di mode lama berakhir
        /// sebagai keadaan Gagal, bukan pengecualian di tangan tombol.
        /// </summary>
        /// <param name="penyebab">Daemon menyiarkan perlu_muat_ulang sesudah perintah ini (gema).</param>
        async Task<List<string>> AksiJarakAsync(string perintah, object data, TimeSpan batas, bool penyebab = false)
        {
            await TungguMuatUlangAsync();
            if (penyebab) MulaiPenyebab();
            bool ok = false;
            try
            {
                var j = await _klien.KirimAsync(perintah, data, batas);
                ok = j.Ok;
                var hasil = j.Peringatan.Select(p => p.Teks()).ToList();
                if (!j.Ok)
                {
                    var galat = j.PesanGalat();
                    SayLokal(galat);
                    hasil.Add(galat);
                }
                return hasil;
            }
            catch (GalatDaemon g)
            {
                SayLokal(g.Message);
                return new List<string> { g.Message };
            }
            finally
            {
                if (penyebab) SelesaiPenyebab(ok);
                await SegarkanKeadaanAsync();
            }
        }

        Jawaban AksiJarak(string perintah, object data, TimeSpan batas, out string galat)
        {
            TungguMuatUlang();
            try
            {
                var j = _klien.Kirim(perintah, data, batas);
                galat = j.Ok ? null : j.PesanGalat();
                return j;
            }
            catch (GalatDaemon g)
            {
                galat = g.Message;
                return null;
            }
        }

        async Task<bool> NyalakanJarakAsync(string perintah, ServiceKind jenis)
        {
            await AksiJarakAsync(perintah, null, BatasLama);
            var s = jenis == ServiceKind.Web ? Services.WebState : Services.DbState;
            return s == ServiceState.Jalan;
        }

        List<string> TerapkanJarak()
        {
            string galat;
            var j = AksiJarak("terapkan", null, BatasSedang, out galat);
            var peringatan = j != null ? j.Peringatan.Select(p => p.Teks()).ToList() : new List<string>();
            if (galat != null) { SayLokal(galat); peringatan.Add(galat); }
            // Kolom vhost/hosts di halaman Situs dibaca dari berkas yang baru
            // saja ditulis daemon.
            RefreshSites();
            // Disimpan di variabel sendiri: jawaban keadaan di latar bisa
            // mengganti LastBuild di utas pembaca sebelum baris return.
            var hasil = HasilTerapan(peringatan);
            LastBuild = hasil;
            SegarkanKeadaanLatar();
            return hasil.Warnings;
        }

        async Task<List<string>> GantiProfilJarakAsync(Profile profile, bool restartIfRunning)
        {
            var nama = profile != null ? profile.FileName ?? "" : "";
            var peringatan = await AksiJarakAsync("ganti_profil", new Dictionary<string, object>
            {
                { "file_name", nama },
                { "restart", restartIfRunning },
            }, BatasLama, true);
            // Daemon sudah menulis profil_aktif ke phoron.ini; salinan di memori
            // disamakan tanpa menyimpan lagi - termasuk potretnya, supaya Save
            // berikutnya tidak mengirim ulang profil ini.
            Active = profile;
            Settings.ActiveProfile = nama;
            SamakanPotret("active_profile");
            RefreshSites();
            var hasil = HasilTerapan(peringatan);
            LastBuild = hasil;
            return hasil.Warnings;
        }

        string JalankanNodeJarak(NodeApp app, int portSekali)
        {
            TungguMuatUlang();
            var path = app.Path ?? "";
            // Status "berjalan" dari jawaban diterapkan DI UTAS PEMBACA, urut
            // dengan kabar - dan hanya bila belum ada kabar node untuk proyek
            // ini sejak permintaannya dikirim. Proses yang langsung mati
            // dikabarkan {berjalan:false} lebih dulu; jawaban yang menyusul
            // tidak boleh menghidupkannya lagi di layar.
            long versi = _nodeJarak.VersiKabar(path);
            Task<Jawaban> t;
            try
            {
                t = _klien.KirimAsync("jalankan_node", new Dictionary<string, object>
                {
                    { "path", path },
                    { "port_sekali", Math.Max(0, Math.Min(65535, portSekali)) },
                }, TimeSpan.FromSeconds(60), j =>
                {
                    if (!j.Ok) return;
                    _nodeJarak.TerapkanStatusBilaTetap(new Dictionary<string, object>
                    {
                        { "path", path },
                        { "berjalan", true },
                        { "url", j.Hasil as string },
                    }, versi);
                });
                var jawab = t.GetAwaiter().GetResult();
                return jawab.Ok ? null : jawab.PesanGalat();
            }
            catch (GalatDaemon g) { return g.Message; }
        }

        List<PortCheck.Usage> SisaProsesJarak()
        {
            string galat;
            var j = AksiJarak("sisa_proses", null, BatasSedang, out galat);
            var hasil = new List<PortCheck.Usage>();
            if (j == null || !j.Ok) return hasil;
            foreach (var o in Protokol.Larik(j.Hasil))
            {
                var d = Protokol.Objek(o);
                if (d == null) continue;
                hasil.Add(new PortCheck.Usage
                {
                    Port = (int)Protokol.Angka(d, "port"),
                    InUse = Protokol.Benar(d, "in_use"),
                    Pid = (int)Protokol.Angka(d, "pid"),
                    ProcessName = Protokol.Teks(d, "process_name") ?? "",
                    Jalur = Protokol.Teks(d, "jalur") ?? "",
                });
            }
            return hasil;
        }

        void CatatSitusJarak(string path)
        {
            // Daemon mencatatnya sendiri; daftar lokal yang sama tidak perlu
            // ikut terkirim di Save berikutnya.
            SamakanPotret("situs_terakhir");
            _klien.KirimAsync("catat_situs_dibuka", new Dictionary<string, object> { { "path", path } })
                  .ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Kirim baris log ke daemon. False bila tidak tersambung - pemanggil mencatat sendiri.</summary>
        bool CatatJarak(string text)
        {
            if (!_klien.Aktif) return false;
            _klien.KirimAsync("catat", new Dictionary<string, object>
            {
                { "pesan", new Pesan(text ?? "").KeJson() },
            }).ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }

        void SayLokal(string teks) { SayLokal(new BarisLog { Waktu = DateTime.Now, Teks = teks ?? "" }); }

        /// <summary>Masukkan ke riwayat dan kabarkan, tanpa menulis phoron.log (milik daemon).</summary>
        void SayLokal(BarisLog b)
        {
            lock (_riwayat)
            {
                _riwayat.Enqueue(b);
                while (_riwayat.Count > RiwayatMaks) _riwayat.Dequeue();
            }
            var h = Log;
            if (h != null) h(b.Teks);
        }

        // ----------------------------------------------------------- Setelan

        // Potret = bentuk JSON tiap medan setelan sebagaimana terakhir dimuat
        // dari, atau terkirim ke, daemon. Save hanya mengirim medan yang
        // berbeda dari potret (ubah_setelan), jadi salinan UI yang basi tidak
        // menimpa perubahan dari menu baki atau klien lain.
        readonly object _kunciPotret = new object();
        Dictionary<string, string> _potret = new Dictionary<string, string>();
        /// <summary>Ada perubahan setelan yang belum sampai ke daemon; dikirim lagi saat tersambung.</summary>
        volatile bool _setelanTertunda;
        /// <summary>Isi potret untuk medan yang pengirimannya gagal: tidak pernah sama dengan nilai mana pun.</summary>
        const string BelumTerkirim = "\u0001belum-terkirim";

        static Dictionary<string, string> JsonMedan(Settings s)
        {
            return SetelanJson.KeJson(s).ToDictionary(kv => kv.Key, kv => Protokol.Tulis(kv.Value));
        }

        void PotretSetelan()
        {
            var baru = JsonMedan(Settings);
            lock (_kunciPotret) _potret = baru;
        }

        /// <summary>Samakan potret satu medan dengan nilai lokalnya (daemon sudah tahu nilai itu).</summary>
        void SamakanPotret(string medan)
        {
            string json;
            if (!JsonMedan(Settings).TryGetValue(medan, out json)) return;
            lock (_kunciPotret) _potret[medan] = json;
        }

        /// <summary>
        /// Settings.Save mode jarak: kirim HANYA medan yang berubah sejak potret
        /// lewat ubah_setelan, tanpa menunggu jawabannya - utas layar tidak
        /// boleh tertahan oleh pipe. Tidak pernah melempar. False bila jelas
        /// tidak terkirim (pipe putus); perubahan lokal tetap berlaku dan
        /// dikirim lagi saat tersambung. Kegagalan yang baru ketahuan sesudah
        /// jawaban datang dicatat di log.
        /// </summary>
        bool SimpanSetelanJarak(Settings s)
        {
            Dictionary<string, object> perubahan;
            Dictionary<string, string> terkirim;
            try
            {
                var nilai = SetelanJson.KeJson(s);
                lock (_kunciPotret)
                {
                    perubahan = new Dictionary<string, object>();
                    terkirim = new Dictionary<string, string>();
                    foreach (var kv in nilai)
                    {
                        // Milik daemon, bukan setelan biasa: profil aktif diganti
                        // lewat ganti_profil, situs terakhir lewat
                        // catat_situs_dibuka. Daemon MENOLAK ubah_setelan yang
                        // menyebut keduanya - dan menolak seluruh perubahan itu,
                        // termasuk medan lain yang sah di dalamnya.
                        if (kv.Key == "active_profile" || kv.Key == "situs_terakhir") continue;
                        var json = Protokol.Tulis(kv.Value);
                        string dulu;
                        if (_potret.TryGetValue(kv.Key, out dulu) && dulu == json) continue;
                        perubahan[kv.Key] = kv.Value;
                        terkirim[kv.Key] = json;
                    }
                    if (perubahan.Count == 0) { _setelanTertunda = false; return true; }
                    if (!_klien.Aktif)
                    {
                        _setelanTertunda = true;
                        perubahan = null;
                    }
                    else
                    {
                        // Dianggap terkirim sekarang: Save kedua yang menyusul
                        // sebelum jawaban datang hanya mengirim yang baru lagi.
                        foreach (var kv in terkirim) _potret[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Say(Lang.T("Setelan gagal dikirim ke daemon Phoron: {0}", ex.Message));
                return false;
            }

            if (perubahan == null)
            {
                Say(Lang.T("Setelan belum tersimpan: tidak tersambung ke daemon Phoron. Perubahan dikirim lagi begitu tersambung."));
                return false;
            }

            MulaiPenyebab();
            Task<Jawaban> t;
            try
            {
                t = _klien.KirimAsync("ubah_setelan", new Dictionary<string, object> { { "perubahan", perubahan } },
                                      TimeSpan.FromSeconds(15));
            }
            catch (Exception ex) { t = Task.FromException<Jawaban>(ex); }
            t.ContinueWith(x =>
            {
                string galat = null;
                if (x.IsFaulted)
                {
                    var g = x.Exception.InnerException;
                    galat = g != null ? g.Message : x.Exception.Message;
                }
                else if (x.IsCanceled) galat = Lang.T("Tidak tersambung ke daemon Phoron.");
                else if (!x.Result.Ok) galat = x.Result.PesanGalat();

                if (galat != null)
                {
                    lock (_kunciPotret)
                    {
                        // Hanya medan yang potretnya masih nilai kiriman ini:
                        // yang sudah diubah lagi sesudahnya ikut kiriman baru.
                        foreach (var kv in terkirim)
                        {
                            string kini;
                            if (_potret.TryGetValue(kv.Key, out kini) && kini == kv.Value)
                                _potret[kv.Key] = BelumTerkirim;
                        }
                    }
                    _setelanTertunda = true;
                    Say(Lang.T("Setelan gagal dikirim ke daemon Phoron: {0}", galat));
                }
                SelesaiPenyebab(galat == null);
            }, TaskScheduler.Default);
            return true;
        }

        // ------------------------------------------------- Hak Administrator

        /// <summary>
        /// Minta daemon menjalankan ulang dirinya dengan hak Administrator
        /// (dialog UAC muncul dari daemon). UI ini sendiri TIDAK dinaikkan
        /// haknya: hosts dan sertifikat ditulis daemon. Daemon lama berpamitan
        /// lalu keluar; pamit itu bukan alasan UI keluar - klien menyambung ke
        /// daemon baru, lalu keadaannya disegarkan.
        ///
        /// Null bila berhasil; selain itu pesan untuk orang (mis. UAC dibatalkan).
        /// </summary>
        public async Task<string> MulaiUlangAdminAsync()
        {
            if (!ModeJarak) return Lang.T("Daemon Phoron belum berjalan sebagai Administrator.");
            int sambungAwal = Volatile.Read(ref _jumlahSambung);
            // Lama: dialog UAC menunggu orangnya menjawab.
            _klien.HarapkanMulaiUlang(BatasJawabUac + BatasSambungAdmin);
            Jawaban j = null;
            try { j = await _klien.KirimAsync("mulai_ulang_admin", null, BatasJawabUac); }
            catch (GalatDaemon g)
            {
                // Terputus bisa berarti daemon lama keluar tepat sesudah
                // menjawab: tunggu daemon barunya. Galat lain berarti gagal.
                if (g.Jenis != "terputus")
                {
                    _klien.BatalMulaiUlang();
                    Say(g.Message);
                    return g.Message;
                }
            }
            if (j != null && !j.Ok)
            {
                _klien.BatalMulaiUlang();
                var pesan = j.PesanGalat();
                Say(pesan);
                return pesan;
            }

            _klien.HarapkanMulaiUlang(BatasSambungAdmin);
            var sampai = DateTime.UtcNow + BatasSambungAdmin;
            while (DateTime.UtcNow < sampai)
            {
                if (Volatile.Read(ref _jumlahSambung) != sambungAwal && _klien.Aktif)
                {
                    await SegarkanKeadaanAsync();
                    if (AdminDaemon) break;
                }
                await Task.Delay(200);
            }
            _klien.BatalMulaiUlang();
            if (AdminDaemon) return null;
            var belum = Lang.T("Daemon Phoron belum berjalan sebagai Administrator.");
            Say(belum);
            return belum;
        }

        static readonly TimeSpan BatasJawabUac = TimeSpan.FromMinutes(5);
        /// <summary>Lama menunggu daemon ber-Administrator tersambung sesudah jawaban ok.</summary>
        public TimeSpan BatasSambungAdmin = TimeSpan.FromSeconds(30);

        // ------------------------------------------ hosts dan sertifikat

        /// <summary>
        /// Hasil aksi hosts/sertifikat. Dalam mode daemon penulisnya DAEMON:
        /// dialah yang dinaikkan ke Administrator, jadi kalau jendela menulis
        /// sendiri, tombolnya tetap gagal sesudah "Jalankan sebagai Administrator".
        /// </summary>
        public sealed class HasilAksi
        {
            /// <summary>Null = berhasil; selain itu pesan untuk orang.</summary>
            public string Galat;
            /// <summary>Gagal karena hak akses: halaman menawarkan menaikkan hak.</summary>
            public bool PerluAdmin;
            public object Hasil;
            public bool Ok { get { return Galat == null; } }
        }

        HasilAksi AksiSistem(string perintah, object data, Action lokal)
        {
            if (ModeJarak)
            {
                string galat;
                var j = AksiJarak(perintah, data, BatasSedang, out galat);
                return new HasilAksi
                {
                    Galat = galat,
                    PerluAdmin = j != null && j.JenisGalat == "perlu_admin",
                    Hasil = j != null ? j.Hasil : null,
                };
            }
            try { lokal(); return new HasilAksi(); }
            catch (UnauthorizedAccessException)
            {
                return new HasilAksi { Galat = Lang.T("Menyunting berkas hosts butuh hak Administrator."), PerluAdmin = true };
            }
            catch (Exception ex) { return new HasilAksi { Galat = ex.Message }; }
        }

        /// <summary>
        /// Kembalikan hosts dari cadangan. Ke daemon yang dikirim hanya NAMA
        /// cadangannya; daemon mencarinya di folder cadangan sendiri.
        /// </summary>
        public HasilAksi PulihkanHosts(HostsFile.Cadangan c)
        {
            return AksiSistem("pulihkan_hosts", new Dictionary<string, object> { { "nama", c.Nama } },
                              () => HostsFile.Pulihkan(c.Path));
        }

        public HasilAksi BersihkanHosts()
        {
            return AksiSistem("bersihkan_hosts", null, HostsFile.Clear);
        }

        /// <summary>Daftarkan nama situs semua profil lewat daemon (hanya mode daemon).</summary>
        public HasilAksi DaftarkanHostsJarak()
        {
            if (!ModeJarak) return new HasilAksi { Galat = Lang.T("Daemon Phoron tidak berjalan.") };
            return AksiSistem("daftarkan_hosts", null, null);
        }

        /// <summary>Buat (ulang) sertifikat situs. Config ditulis ulang oleh pemanggil lewat Apply.</summary>
        public HasilAksi BuatSertifikat()
        {
            if (ModeJarak) return AksiSistem("buat_sertifikat", null, null);
            var apache = Apache;
            if (apache == null) return new HasilAksi { Galat = Lang.T("Butuh paket Apache (openssl.exe ada di dalamnya).") };
            var err = SslTool.Generate(apache, Active != null ? Active.SiteSuffix : "test",
                                       Profiles.Select(x => x.SiteSuffix));
            if (err != null) return new HasilAksi { Galat = err };
            Say("Sertifikat SSL dibuat di etc\\ssl.");
            return new HasilAksi();
        }

        public HasilAksi PercayaiSertifikat()
        {
            HasilAksi h;
            if (ModeJarak) h = AksiSistem("percayai_sertifikat", null, null);
            else
            {
                var t = SslTool.Trust();
                h = new HasilAksi { Galat = t, PerluAdmin = t != null && SslTool.Exists && !HostsFile.IsAdmin() };
            }
            // Gudang Trusted Root berubah di proses lain: jawaban singgahan
            // "belum dipercaya" di proses ini sudah basi.
            SslTool.LupakanKepercayaan();
            return h;
        }
    }
}
