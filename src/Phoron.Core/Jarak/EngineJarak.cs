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
        public Engine(KlienDaemon klien)
        {
            if (klien == null) throw new ArgumentNullException("klien");
            _klien = klien;
            var hasil = Core.Settings.Muat();
            Settings = hasil.Setelan;
            foreach (var k in hasil.Keluhan)
                MasalahMuat.Add("phoron.ini: " + k);
            // phoron.ini TIDAK ditulis di sini walau belum ada: daemon yang
            // membuatnya, dan daemon satu-satunya penulisnya.
            Settings.PenyimpanJarak = SimpanSetelanJarak;
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

        void SesudahSambungUlang()
        {
            // Daemon yang baru dihidupkan lagi tidak tahu apa yang dilihat UI;
            // ambil ulang seluruh keadaannya.
            ThreadPool.QueueUserWorkItem(delegate { try { Sinkronkan(); } catch { } });
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
            r.Warnings.AddRange(peringatan);
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
                    _nodeJarak.TerapkanStatus(data);
                    break;
                case "node_keluaran":
                    _nodeJarak.TerapkanKeluaran(data);
                    break;
                case "perlu_muat_ulang":
                    if (AksiSendiriBaruSaja()) break;
                    Jadwalkan(MuatUlangDariDaemon);
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
            try { Settings.SalinDari(Core.Settings.Load()); } catch { }
            Reload();
            var h = BerubahDariLuar;
            if (h != null) h();
        }

        // Kabar perlu_muat_ulang yang menyusul perintah UI ini sendiri (simpan
        // setelan, muat ulang, ganti profil) TIDAK memuat ulang lagi: Reload
        // mengganti objek profil, dan halaman Profil yang sedang menyunting
        // akan menunjuk objek yatim - alasan yang sama kenapa halaman itu
        // sengaja tidak memanggil Reload sesudah menyimpan.
        int _aksiBerjalan;
        long _aksiSelesaiTick;

        void MulaiAksi() { Interlocked.Increment(ref _aksiBerjalan); }

        void SelesaiAksi()
        {
            Interlocked.Exchange(ref _aksiSelesaiTick, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _aksiBerjalan);
        }

        bool AksiSendiriBaruSaja()
        {
            if (Volatile.Read(ref _aksiBerjalan) > 0) return true;
            var selesai = new DateTime(Interlocked.Read(ref _aksiSelesaiTick), DateTimeKind.Utc);
            return (DateTime.UtcNow - selesai).TotalMilliseconds < 1500;
        }

        // --------------------------------------------------------- Kirim aksi

        Task _muatUlangTertunda;

        void BerkasPenggunaBerubah()
        {
            if (!_klien.Aktif) return;
            // Langsung dikirim, dan aksi berikutnya menunggu jawabannya: daemon
            // harus sudah membaca profil baru sebelum diminta menerapkannya.
            MulaiAksi();
            var t = _klien.KirimAsync("muat_ulang");
            _muatUlangTertunda = t;
            t.ContinueWith(x => { SelesaiAksi(); var _ = x.Exception; }, TaskScheduler.Default);
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
        async Task<List<string>> AksiJarakAsync(string perintah, object data, TimeSpan batas)
        {
            await TungguMuatUlangAsync();
            MulaiAksi();
            try
            {
                var j = await _klien.KirimAsync(perintah, data, batas);
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
                SelesaiAksi();
                await SegarkanKeadaanAsync();
            }
        }

        Jawaban AksiJarak(string perintah, object data, TimeSpan batas, out string galat)
        {
            TungguMuatUlang();
            MulaiAksi();
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
            finally { SelesaiAksi(); }
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
            }, BatasLama);
            // Daemon sudah menulis profil_aktif ke phoron.ini; salinan di memori
            // disamakan tanpa menyimpan lagi.
            Active = profile;
            Settings.ActiveProfile = nama;
            RefreshSites();
            var hasil = HasilTerapan(peringatan);
            LastBuild = hasil;
            return hasil.Warnings;
        }

        string JalankanNodeJarak(NodeApp app, int portSekali)
        {
            string galat;
            var j = AksiJarak("jalankan_node", new Dictionary<string, object>
            {
                { "path", app.Path ?? "" },
                { "port_sekali", Math.Max(0, Math.Min(65535, portSekali)) },
            }, TimeSpan.FromSeconds(60), out galat);
            if (galat != null) return galat;
            var url = j.Hasil as string;
            _nodeJarak.TerapkanStatus(new Dictionary<string, object>
            {
                { "path", app.Path ?? "" },
                { "berjalan", true },
                { "url", url },
            });
            return null;
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

        void SimpanSetelanJarak(Settings s)
        {
            MulaiAksi();
            try
            {
                _klien.Kirim("simpan_setelan", new Dictionary<string, object> { { "setelan", SetelanJson.KeJson(s) } },
                             TimeSpan.FromSeconds(15)).Pastikan();
            }
            catch (GalatDaemon g) { throw new IOException(g.Message, g); }
            finally { SelesaiAksi(); }
        }
    }
}
