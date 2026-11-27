using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using Phoron.Core;
using Phoron.Core.Jarak;

namespace Phoron.Tests
{
    /// <summary>
    /// Daemon tiruan: pipe server di proses uji yang berbicara protokol v2
    /// dengan nama pipe uji, jadi phorond (dan Phoron) yang sedang dipakai di
    /// komputer ini tidak tersentuh sama sekali.
    /// </summary>
    sealed class DaemonPalsu : IDisposable
    {
        public readonly string Nama = "phoron-uji-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public int ProtokolDaemon = Protokol.Versi;
        /// <summary>Akar yang disebut di halo; null = tidak disebut (seperti daemon 1.42.0).</summary>
        public string AkarDaemon;

        /// <summary>Perintah yang tidak pernah dijawab (uji batas waktu).</summary>
        public readonly HashSet<string> Diam = new HashSet<string>();

        /// <summary>(perintah, data) -> isi jawaban selain id; null = {"status":"ok"}.</summary>
        public Func<string, IDictionary<string, object>, Dictionary<string, object>> Jawab;

        readonly List<KeyValuePair<string, IDictionary<string, object>>> _diterima =
            new List<KeyValuePair<string, IDictionary<string, object>>>();
        readonly List<Sambungan> _klien = new List<Sambungan>();
        readonly Thread _penerima;
        volatile bool _berhenti;

        sealed class Sambungan
        {
            public NamedPipeServerStream Pipe;
            public StreamWriter Penulis;
            public readonly object Kunci = new object();
        }

        public DaemonPalsu()
        {
            _penerima = new Thread(TerimaTerus) { IsBackground = true, Name = "DaemonPalsu" };
            _penerima.Start();
        }

        public List<KeyValuePair<string, IDictionary<string, object>>> Diterima
        {
            get { lock (_diterima) return new List<KeyValuePair<string, IDictionary<string, object>>>(_diterima); }
        }

        public int Hitung(string perintah) { return Diterima.Count(x => x.Key == perintah); }

        public IDictionary<string, object> Terakhir(string perintah)
        {
            var d = Diterima.LastOrDefault(x => x.Key == perintah);
            return d.Key == null ? null : (d.Value ?? new Dictionary<string, object>());
        }

        void TerimaTerus()
        {
            while (!_berhenti)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(Nama, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    pipe.WaitForConnection();
                }
                catch { if (_berhenti) return; Thread.Sleep(50); continue; }
                if (_berhenti) { pipe.Dispose(); return; }
                var s = new Sambungan { Pipe = pipe, Penulis = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" } };
                lock (_klien) _klien.Add(s);
                new Thread(() => Layani(s)) { IsBackground = true }.Start();
            }
        }

        void Layani(Sambungan s)
        {
            try
            {
                var r = new StreamReader(s.Pipe, new UTF8Encoding(false));
                string baris;
                while ((baris = r.ReadLine()) != null)
                {
                    var d = Protokol.Objek(Protokol.Urai(baris));
                    var perintah = Protokol.Teks(d, "perintah");
                    var data = Protokol.Objek(Protokol.Ambil(d, "data"));
                    lock (_diterima) _diterima.Add(new KeyValuePair<string, IDictionary<string, object>>(perintah, data));
                    if (Diam.Contains(perintah)) continue;

                    Dictionary<string, object> isi;
                    if (perintah == "halo")
                    {
                        long ui = Protokol.Angka(data, "protokol");
                        isi = ui != ProtokolDaemon
                            ? Galat(new Dictionary<string, object> { { "jenis", "protokol_beda" }, { "daemon", ProtokolDaemon }, { "ui", ui } })
                            : new Dictionary<string, object>
                              {
                                  { "status", "ok" },
                                  { "hasil", new Dictionary<string, object> { { "versi_daemon", "uji-0.1" }, { "protokol", ProtokolDaemon } } },
                              };
                        if (AkarDaemon != null && ui == ProtokolDaemon)
                            ((Dictionary<string, object>)isi["hasil"])["akar"] = AkarDaemon;
                    }
                    else isi = (Jawab != null ? Jawab(perintah, data) : null) ?? new Dictionary<string, object> { { "status", "ok" } };
                    isi["id"] = Protokol.Ambil(d, "id");
                    Tulis(s, isi);
                }
            }
            catch { }
            finally
            {
                lock (_klien) _klien.Remove(s);
                try { s.Pipe.Dispose(); } catch { }
            }
        }

        public static Dictionary<string, object> Galat(Dictionary<string, object> galat)
        {
            return new Dictionary<string, object> { { "status", "galat" }, { "galat", galat } };
        }

        static void Tulis(Sambungan s, object pesan)
        {
            var baris = Protokol.Tulis(pesan);
            lock (s.Kunci) s.Penulis.WriteLine(baris);
        }

        /// <summary>Kirim kabar ke semua klien.</summary>
        public void Kabar(string jenis, object data = null)
        {
            var k = new Dictionary<string, object> { { "kabar", jenis } };
            if (data != null) k["data"] = data;
            List<Sambungan> semua;
            lock (_klien) semua = new List<Sambungan>(_klien);
            foreach (var s in semua) { try { Tulis(s, k); } catch { } }
        }

        public int JumlahKlien { get { lock (_klien) return _klien.Count; } }

        /// <summary>Putus semua sambungan (seperti daemon yang crash lalu hidup lagi).</summary>
        public void PutuskanSemua()
        {
            List<Sambungan> semua;
            lock (_klien) semua = new List<Sambungan>(_klien);
            foreach (var s in semua) { try { s.Pipe.Dispose(); } catch { } }
        }

        public void Dispose()
        {
            _berhenti = true;
            PutuskanSemua();
            // WaitForConnection tidak bisa dibatalkan; satu sambungan kosong
            // membangunkannya supaya utasnya selesai.
            try
            {
                using (var c = new NamedPipeClientStream(".", Nama, PipeDirection.InOut))
                    c.Connect(200);
            }
            catch { }
        }
    }

    public static partial class Program
    {
        static bool TungguSampai(Func<bool> syarat, int ms = 3000)
        {
            var sampai = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < sampai)
            {
                if (syarat()) return true;
                Thread.Sleep(20);
            }
            return syarat();
        }

        /// <summary>Syarat yang harus TETAP benar sesudah jeda singkat (sesuatu yang tidak boleh terjadi).</summary>
        static bool SetelahJeda(Func<bool> syarat, int ms = 300)
        {
            Thread.Sleep(ms);
            return syarat();
        }

        /// <summary>
        /// UI sebagai klien phorond (tahap E): KlienDaemon dan Engine mode jarak
        /// diuji terhadap daemon tiruan.
        /// </summary>
        static void UjiKlienDaemon()
        {
            Bagian("Klien daemon (pipe protokol v2)");

            Ok("Tanpa phorond.exe di sebelah exe, mode lama yang dipakai",
               Environment.GetEnvironmentVariable("PHORON_DAEMON") != null || !ModeDaemon.Aktif);
            ModeDaemon.Paksa = true;
            Ok("Mode daemon bisa dipaksa", ModeDaemon.Aktif);
            Ok("Autostart mode daemon menunjuk phorond.exe",
               Autostart.ExeTujuan.EndsWith(ModeDaemon.NamaExe, StringComparison.OrdinalIgnoreCase), Autostart.ExeTujuan);
            ModeDaemon.Paksa = false;
            Ok("Autostart mode lama tetap menunjuk exe ini", Autostart.ExeTujuan == Autostart.ExePath);
            ModeDaemon.Paksa = null;

            // Pengendali: dalam mode daemon UI tidak memegang baki, sinyal
            // keluar, maupun layanan.
            var pg = BacaSumber("Phoron.App", "Pengendali.cs");
            if (pg != null)
            {
                var mulai = Potong(pg, "public void Mulai(bool tampilkan)", "async Task MulaiAsync()");
                Ok("Mode daemon tanpa ikon baki dan sinyal keluar di UI",
                   System.Text.RegularExpressions.Regex.IsMatch(mulai, @"if \(!ModeDaemon\)\s*\{\s*SetupTray\(\);[\s\S]*PasangSinyalKeluar\(\);\s*\}"));
                Ok("Mode daemon: menutup jendela = UI keluar",
                   Potong(pg, "void JendelaTertutup(", "void JadwalkanLepasMemori(").Contains("if (ModeDaemon) { KeluarUi(); return; }"));
                Ok("Mode daemon: Keluar meminta daemon berhenti",
                   pg.Contains("KirimAsync(\"keluar\"") && pg.Contains("_engine.Pamit +="));
                var beres = Potong(pg, "void Bereskan()", "// ----");
                int iMode = beres.IndexOf("if (ModeDaemon)"), iLepas = beres.IndexOf("Klien.Dispose()"),
                    iLain = beres.IndexOf("else"), iStop = beres.IndexOf("_engine.Services.StopAll()");
                Ok("Mode daemon: membereskan UI tidak mematikan layanan",
                   iMode >= 0 && iMode < iLepas && iLepas < iLain && iLain < iStop);
                // Event Phoron.KeluarSekarang milik daemon: satu-satunya tempat
                // UI membuatnya adalah PasangSinyalKeluar, yang hanya dipanggil
                // di mode lama. UI mode daemon ikut keluar lewat kabar pamit.
                int sebut = System.Text.RegularExpressions.Regex.Matches(pg, "\"Phoron\\.KeluarSekarang\"").Count;
                int panggil = System.Text.RegularExpressions.Regex.Matches(pg, @"PasangSinyalKeluar\(\);").Count;
                Ok("Mode daemon: UI tidak membuat atau menunggu event Phoron.KeluarSekarang",
                   sebut == 1 && panggil == 1 && Potong(pg, "void PasangSinyalKeluar()", "void PasangSinyalTampil()").Contains("\"Phoron.KeluarSekarang\""),
                   "sebut " + sebut + ", panggil " + panggil);
                Ok("Mode daemon: kabar pamit membuat UI keluar",
                   pg.Contains("_engine.Pamit += () => d.BeginInvoke(new Action(KeluarUi));"));
            }
            foreach (var hal in new[] { "DashboardPage.xaml.cs", "SettingsPage.xaml.cs", "AboutPage.xaml.cs" })
            {
                var isi = BacaSumber("Phoron.App", "Pages", hal);
                if (isi == null) continue;
                Ok("Hak Administrator di " + hal + " dari Engine.HakAdmin, bukan proses UI",
                   !isi.Contains("HostsFile.IsAdmin()") && isi.Contains("_e.HakAdmin"));
                // Daemon yang dinaikkan ke Administrator, jadi daemon pula yang
                // menulis: jendela yang menulis sendiri tetap gagal sesudahnya.
                Ok("Hosts dan sertifikat di " + hal + " lewat Engine, bukan ditulis jendela",
                   !System.Text.RegularExpressions.Regex.IsMatch(isi, @"HostsFile\.(Pulihkan|Clear|Sync)\(|SslTool\.(Trust|Generate)\("));
            }
            var prog = BacaSumber("Phoron.App", "Program.cs");
            if (prog != null)
            {
                var restart = Potong(prog, "public static bool RestartAsAdmin", "\n        }\n");
                int iJarak = restart.IndexOf("mesin.ModeJarak"), iRunas = restart.IndexOf("Verb = \"runas\"");
                Ok("RestartAsAdmin mode daemon menaikkan daemon, bukan UI",
                   iJarak >= 0 && iJarak < iRunas && restart.Contains("NaikkanDaemon(mesin); return false;")
                   && Potong(prog, "static async void NaikkanDaemon", "\n        }\n").Contains("MulaiUlangAdminAsync()"));
            }

            // --------------------------------------------------- Setelan JSON
            var waktu = new DateTime(2026, 11, 27, 7, 50, 29, DateTimeKind.Utc).AddTicks(1234567);
            var saat = Protokol.Objek(SetelanJson.Saat(waktu));
            Ok("CekTerakhir jadi {detik, tick}",
               Protokol.Angka(saat, "detik") == 1795765829 && Protokol.Angka(saat, "tick") == 1234567,
               Protokol.Tulis(saat));
            Ok("Saat bolak-balik utuh", SetelanJson.DariSaat(saat) == waktu);
            Ok("Belum pernah dicek = null", SetelanJson.Saat(DateTime.MinValue) == null);
            var json = SetelanJson.KeJson(new Settings { Bahasa = "en", AutoVhost = true });
            var kunciRust = new[] { "bin_roots", "situs_terakhir", "active_profile", "auto_start_services",
                "minimize_to_tray", "auto_vhost", "opcache", "akses_jaringan", "manage_hosts", "php_ini_ke_folder_php", "log_rinci",
                "beranda_di_akar", "tema", "jendela_lebar", "jendela_tinggi", "bahasa", "cek_pembaruan",
                "cek_terakhir", "terminal", "editor", "db_pengguna", "heidi_sql", "db_sandi" };
            Ok("Semua medan Settings Rust dikirim, tidak lebih",
               kunciRust.All(json.ContainsKey) && json.Count == kunciRust.Length,
               string.Join(",", json.Keys));
            Ok("Nilai setelan terpetakan", (string)json["bahasa"] == "en" && (bool)json["auto_vhost"]);

            Ok("Riwayat node: ekor daemon yang sama dengan kabar awal digabung sekali",
               NodeJarak.Tumpang(new[] { "a", "b", "c" }, new[] { "b", "c", "d" }) == 2
               && NodeJarak.Tumpang(new[] { "a" }, new[] { "b" }) == 0
               && NodeJarak.Tumpang(new string[0], new[] { "b" }) == 0);

            var pesan = new Pesan("Menghentikan {0}", "httpd");
            Ok("Pesan diterjemahkan lewat Lang.T(kunci, args)", pesan.Teks() == "Menghentikan httpd", pesan.Teks());
            Ok("Pesan tanpa argumen tidak diformat",
               new Pesan("kurung {kurawal} apa adanya").Teks() == "kurung {kurawal} apa adanya");

            // ------------------------------------------------- Daemon tidak ada
            using (var tanpa = new KlienDaemon("phoron-uji-tidak-ada-" + Guid.NewGuid().ToString("N").Substring(0, 6))
                   { BatasTungguDaemon = TimeSpan.FromMilliseconds(300) })
            {
                string jenis = null;
                var mulai = DateTime.UtcNow;
                try { tanpa.Sambung(); }
                catch (GalatDaemon g) { jenis = g.Jenis; }
                Ok("Pipe tidak ada = galat tidak_ada, tidak menggantung",
                   jenis == "tidak_ada" && (DateTime.UtcNow - mulai).TotalSeconds < 5, jenis ?? "(tidak melempar)");
            }

            // ------------------------------------------------------ Protokol beda
            using (var lama = new DaemonPalsu { ProtokolDaemon = 1 })
            using (var k = new KlienDaemon(lama.Nama))
            {
                GalatDaemon galat = null;
                try { k.Sambung(); } catch (GalatDaemon g) { galat = g; }
                Ok("Protokol beda ditolak dengan pesan pasang ulang",
                   galat != null && galat.Jenis == "protokol_beda" && galat.Message.Contains("dipasang ulang"),
                   galat != null ? galat.Message : "(tidak melempar)");
                Thread.Sleep(300);
                Ok("Protokol beda tidak disambung ulang terus-menerus", lama.Hitung("halo") == 1, "halo " + lama.Hitung("halo"));
            }

            // ------------------------------------------------------ Akar beda
            // Nama pipe sama untuk semua salinan Phoron: jendela dari folder lain
            // tidak boleh mengendalikan daemon milik C:\Phoron.
            using (var lain = new DaemonPalsu { AkarDaemon = @"C:\Phoron" })
            using (var k = new KlienDaemon(lain.Nama) { AkarHarapan = @"C:\Claude\phoron\dist" })
            {
                GalatDaemon galat = null;
                try { k.Sambung(); } catch (GalatDaemon g) { galat = g; }
                Ok("Daemon berakar lain ditolak dengan pesan yang menyebut kedua folder",
                   galat != null && galat.Jenis == "akar_beda" && galat.Message.Contains(@"C:\Phoron")
                   && galat.Message.Contains(@"C:\Claude\phoron\dist") && !k.Aktif,
                   galat != null ? galat.Message : "(tidak melempar)");
                Thread.Sleep(300);
                Ok("Akar beda tidak disambung ulang terus-menerus", lain.Hitung("halo") == 1, "halo " + lain.Hitung("halo"));
            }
            using (var sama = new DaemonPalsu { AkarDaemon = @"c:\phoron\" })
            using (var k = new KlienDaemon(sama.Nama) { AkarHarapan = @"C:\Phoron" })
            {
                k.Sambung();
                Ok("Akar sama walau beda huruf besar dan garis miring di ujung", k.Aktif && k.AkarDaemon == @"c:\phoron\");
            }
            using (var lama = new DaemonPalsu())
            using (var k = new KlienDaemon(lama.Nama) { AkarHarapan = @"C:\Phoron" })
            {
                k.Sambung();
                Ok("Daemon 1.42.0 tanpa akar tetap diterima", k.Aktif && k.AkarDaemon == "");
            }

            UjiKlienDasar();
            UjiEngineJarak();
        }

        static void UjiKlienDasar()
        {
            using (var d = new DaemonPalsu())
            using (var k = new KlienDaemon(d.Nama))
            {
                d.Jawab = (perintah, data) => perintah == "keadaan"
                    ? new Dictionary<string, object> { { "status", "ok" }, { "hasil", new Dictionary<string, object> { { "admin", true } } } }
                    : null;

                int tersambung = 0, terputus = 0;
                k.Tersambung += () => Interlocked.Increment(ref tersambung);
                k.Terputus += () => Interlocked.Increment(ref terputus);
                var kabar = new List<string>();
                k.Kabar += (jenis, data) => { lock (kabar) kabar.Add(jenis + ":" + Protokol.Teks(Protokol.Objek(data), "x")); };

                k.Sambung();
                var halo = d.Terakhir("halo");
                Ok("Halo dikirim lebih dulu dengan protokol 2",
                   d.Diterima.Count > 0 && d.Diterima[0].Key == "halo" && Protokol.Angka(halo, "protokol") == 2);
                Ok("Versi daemon terbaca dari halo", k.VersiDaemon == "uji-0.1", k.VersiDaemon);

                var j = k.Kirim("keadaan");
                Ok("Jawaban dicocokkan lewat id", j.Ok && Protokol.Benar(Protokol.Objek(j.Hasil), "admin"));

                d.Kabar("log", new Dictionary<string, object> { { "x", "satu" } });
                Ok("Kabar tanpa id dinaikkan sebagai event",
                   TungguSampai(() => { lock (kabar) return kabar.Contains("log:satu"); }));

                d.Diam.Add("riwayat_node");
                string jenisGalat = null;
                try { k.Kirim("riwayat_node", null, TimeSpan.FromMilliseconds(300)); }
                catch (GalatDaemon g) { jenisGalat = g.Jenis; }
                Ok("Permintaan tak terjawab berakhir dengan batas waktu", jenisGalat == "batas_waktu", jenisGalat ?? "-");
                Ok("Pipe tetap hidup sesudah batas waktu", k.Kirim("keadaan").Ok);

                d.Jawab = (perintah, data) => perintah == "terapkan"
                    ? DaemonPalsu.Galat(new Dictionary<string, object>
                      {
                          { "jenis", "gagal" },
                          { "pesan", new Pesan("Menghentikan {0}", "nginx").KeJson() },
                      })
                    : null;
                var gagal = k.Kirim("terapkan");
                Ok("Galat daemon diterjemahkan", !gagal.Ok && gagal.PesanGalat() == "Menghentikan nginx", gagal.PesanGalat());

                // Daemon putus (crash); klien menyambung ulang sendiri.
                d.PutuskanSemua();
                Ok("Putus terdeteksi", TungguSampai(() => terputus >= 1));
                Ok("Tersambung ulang sendiri", TungguSampai(() => tersambung >= 2 && k.Aktif, 5000),
                   "tersambung " + tersambung);
                Ok("Sesudah sambung ulang halo dikirim lagi", d.Hitung("halo") == 2, "halo " + d.Hitung("halo"));
                Ok("Perintah jalan lagi sesudah sambung ulang", k.Kirim("keadaan").Ok);

                // Pamit: daemon sengaja berhenti, jangan disambung ulang.
                d.Kabar("pamit");
                Ok("Kabar pamit sampai", TungguSampai(() => { lock (kabar) return kabar.Any(x => x.StartsWith("pamit")); }));
                d.PutuskanSemua();
                Thread.Sleep(600);
                Ok("Sesudah pamit tidak disambung ulang", d.Hitung("halo") == 2, "halo " + d.Hitung("halo"));
            }
        }

        static void UjiEngineJarak()
        {
            Bagian("Engine mode jarak");
            var dulu = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(), "phoron-jarak-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(akar);
            Paths.Root = akar;
            var daemon = new DaemonPalsu();
            var klien = new KlienDaemon(daemon.Nama);
            Engine e = null;
            try
            {
                string web = "Jalan", db = "Berhenti";
                bool perluRestart = true;
                object peringatanTerakhir = new object[] { new Pesan("Peringatan dari daemon").KeJson() };
                bool admin = false, batalUac = false, tolakUbah = false, nodeMati = false;
                string namaCadangan = null;
                int tundaNyalakan = 0, tundaGanti = 0;
                object masalahDaemon = new object[0];
                daemon.Jawab = (perintah, data) =>
                {
                    switch (perintah)
                    {
                        case "nyalakan_semua":
                            if (tundaNyalakan > 0) Thread.Sleep(tundaNyalakan);
                            return null;
                        case "ganti_profil":
                            if (tundaGanti > 0) Thread.Sleep(tundaGanti);
                            return null;
                        case "ubah_setelan":
                            return tolakUbah
                                ? DaemonPalsu.Galat(new Dictionary<string, object>
                                  { { "jenis", "gagal" }, { "pesan", new Pesan("Setelan ditolak uji").KeJson() } })
                                : null;
                        case "pulihkan_hosts":
                        case "percayai_sertifikat":
                            if (perintah == "pulihkan_hosts")
                                namaCadangan = Protokol.Teks(Protokol.Objek(data), "nama");
                            return admin
                                ? null
                                : DaemonPalsu.Galat(new Dictionary<string, object>
                                  { { "jenis", "perlu_admin" }, { "pesan", new Pesan("Menyunting berkas hosts butuh hak Administrator.").KeJson() } });
                        case "mulai_ulang_admin":
                            if (batalUac)
                                return DaemonPalsu.Galat(new Dictionary<string, object>
                                  { { "jenis", "gagal" }, { "pesan", new Pesan("Dibatalkan di dialog UAC.").KeJson() } });
                            // Seperti phorond: jawab ok, pamit, keluar; penggantinya
                            // ber-Administrator dan menerima sambungan baru.
                            new Thread(() =>
                            {
                                Thread.Sleep(150);
                                daemon.Kabar("pamit");
                                Thread.Sleep(50);
                                admin = true;
                                daemon.PutuskanSemua();
                            }) { IsBackground = true }.Start();
                            return null;
                        case "keadaan":
                            return new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "hasil", new Dictionary<string, object>
                                    {
                                        { "status", new Dictionary<string, object> { { "web", web }, { "db", db } } },
                                        { "profil_aktif", "" },
                                        { "node", new object[0] },
                                        { "perlu_restart_web", perluRestart },
                                        { "perlu_restart_db", false },
                                        { "peringatan_terapkan", peringatanTerakhir },
                                        { "masalah_muat", masalahDaemon },
                                        { "admin", admin },
                                    } },
                            };
                        case "riwayat_log":
                            return new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "hasil", new object[]
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "waktu", "2026-11-27T07:50:29.0000000Z" },
                                            { "pesan", new Pesan("Baris lama {0}", "daemon").KeJson() },
                                        },
                                    } },
                            };
                        case "terapkan":
                            peringatanTerakhir = new object[] { new Pesan("Peringatan uji {0}", "satu").KeJson() };
                            return new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "peringatan", new object[] { new Pesan("Peringatan uji {0}", "satu").KeJson() } },
                            };
                        case "jalankan_node":
                            // Proses yang langsung mati: kabarnya mendahului jawaban.
                            if (nodeMati)
                                daemon.Kabar("node", new Dictionary<string, object>
                                    { { "path", Protokol.Teks(data, "path") }, { "berjalan", false } });
                            return new Dictionary<string, object> { { "status", "ok" }, { "hasil", "http://localhost:5173" } };
                        case "sisa_proses":
                            return new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "hasil", new object[]
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "port", 80 }, { "in_use", true }, { "pid", 4321 },
                                            { "process_name", "httpd" }, { "jalur", @"C:\lama\httpd.exe" },
                                        },
                                    } },
                            };
                    }
                    return null;
                };
                klien.Sambung();
                e = new Engine(klien);

                Ok("Engine jarak tahu dirinya jarak", e.ModeJarak && e.Services is LayananJarak && e.Node is NodeJarak);
                Ok("Keadaan awal diambil dari daemon",
                   e.Services.WebState == ServiceState.Jalan && e.Services.DbState == ServiceState.Berhenti,
                   e.Services.WebState + "/" + e.Services.DbState);
                Ok("PerluRestartWeb dari Keadaan", e.PerluRestartWeb && !e.PerluRestartDb);
                Ok("Riwayat log diambil dari daemon",
                   e.Riwayat().Any(b => b.Teks == "Baris lama daemon"),
                   string.Join(" | ", e.Riwayat().Select(b => b.Teks)));
                Ok("phoron.ini tidak ditulis UI saat mulai", !File.Exists(Paths.SettingsFile));

                // Kabar status -> WebState + StateChanged
                var berubah = new List<string>();
                e.Services.StateChanged += (kind, state) => { lock (berubah) berubah.Add(kind + "=" + state); };
                web = "Berhenti"; db = "Jalan";
                daemon.Kabar("status", new Dictionary<string, object> { { "web", "Berhenti" }, { "db", "Jalan" } });
                Ok("Kabar status mengubah WebState dan menaikkan StateChanged",
                   TungguSampai(() => { lock (berubah) return berubah.Contains("Web=Berhenti") && berubah.Contains("Db=Jalan"); })
                   && e.Services.WebState == ServiceState.Berhenti,
                   string.Join(",", berubah));

                // Kabar log -> event Log
                var log = new List<string>();
                e.Log += t => { lock (log) log.Add(t); };
                daemon.Kabar("log", new Dictionary<string, object>
                {
                    { "waktu", "2026-11-27T08:00:00.0000000Z" },
                    { "pesan", new Pesan("Menghentikan {0}", "mysqld").KeJson() },
                });
                Ok("Kabar log jadi event Log yang sudah diterjemahkan",
                   TungguSampai(() => { lock (log) return log.Contains("Menghentikan mysqld"); }));
                Ok("Kabar log masuk riwayat", e.Riwayat().Any(b => b.Teks == "Menghentikan mysqld"));

                e.Say("halo dari UI");
                Ok("Say dikirim sebagai perintah catat",
                   TungguSampai(() => Protokol.Teks(Protokol.Objek(Protokol.Ambil(daemon.Terakhir("catat"), "pesan")), "kunci") == "halo dari UI"));
                Ok("Say tidak menulis phoron.log di UI", !File.Exists(Path.Combine(Paths.Logs, "phoron.log")));

                // Profil ditulis UI -> muat_ulang
                var profil = new Profile { Name = "Uji Jarak", FileName = "uji-jarak" };
                ProfileStore.Save(profil);
                Ok("Menyimpan profil mengirim muat_ulang", TungguSampai(() => daemon.Hitung("muat_ulang") >= 1));
                e.Reload();
                var aktif = e.Profiles.FirstOrDefault(p => p.FileName == "uji-jarak");
                Ok("Profil dibaca UI sendiri", aktif != null);

                var ganti = e.SwitchAsync(aktif, false).Result;
                var dataGanti = daemon.Terakhir("ganti_profil");
                Ok("SwitchAsync dikirim sebagai ganti_profil",
                   dataGanti != null && Protokol.Teks(dataGanti, "file_name") == "uji-jarak"
                   && Protokol.Ambil(dataGanti, "restart") is bool && !(bool)Protokol.Ambil(dataGanti, "restart"));
                Ok("Profil aktif ikut berganti di UI", ReferenceEquals(e.Active, aktif) && e.Settings.ActiveProfile == "uji-jarak");

                var peringatan = e.Apply();
                Ok("Apply dikirim sebagai terapkan", daemon.Hitung("terapkan") == 1);
                Ok("Peringatan terapkan diterjemahkan dan jadi LastBuild",
                   peringatan.Contains("Peringatan uji satu") && e.LastBuild.Warnings.Contains("Peringatan uji satu"),
                   string.Join(" | ", peringatan));

                e.StartWebAsync().Wait();
                Ok("StartWebAsync = nyalakan_web", daemon.Hitung("nyalakan_web") == 1);
                e.StartDbAsync().Wait();
                e.StopWebAsync().Wait();
                e.StopDbAsync().Wait();
                Ok("Start/Stop Db dan Stop Web jadi perintah masing-masing",
                   daemon.Hitung("nyalakan_db") == 1 && daemon.Hitung("matikan_web") == 1 && daemon.Hitung("matikan_db") == 1);
                e.StartAllAsync().Wait();
                e.StopAllAsync().Wait();
                Ok("Start/StopAll jadi nyalakan_semua dan matikan_semua",
                   daemon.Hitung("nyalakan_semua") == 1 && daemon.Hitung("matikan_semua") == 1
                   && daemon.Hitung("nyalakan_web") == 1);

                var sisa = e.SisaProses();
                Ok("SisaProses dari daemon", sisa.Count == 1 && sisa[0].Pid == 4321 && sisa[0].Jalur == @"C:\lama\httpd.exe");
                e.HentikanSisaAsync().Wait();
                Ok("HentikanSisaAsync = hentikan_sisa", daemon.Hitung("hentikan_sisa") == 1);

                e.CatatSitusDibuka(@"C:\www\toko");
                Ok("CatatSitusDibuka dikirim ke daemon",
                   TungguSampai(() => Protokol.Teks(daemon.Terakhir("catat_situs_dibuka"), "path") == @"C:\www\toko"));
                Ok("Situs terakhir langsung tampil di UI", e.Settings.SitusTerakhir.FirstOrDefault() == @"C:\www\toko");

                // Settings.Save -> ubah_setelan berisi medan yang berubah saja.
                // Profil aktif (ganti_profil) dan situs terakhir (catat_situs_dibuka)
                // sudah diketahui daemon, jadi tidak ikut.
                e.Settings.Bahasa = "en";
                e.Settings.JendelaLebar = 1234;
                var mulaiSimpan = DateTime.UtcNow;
                bool tersimpan = e.Settings.Save();
                var lamaSimpan = (DateTime.UtcNow - mulaiSimpan).TotalMilliseconds;
                Ok("Settings.Save mengirim ubah_setelan", TungguSampai(() => daemon.Hitung("ubah_setelan") == 1));
                var ubah = Protokol.Objek(Protokol.Ambil(daemon.Terakhir("ubah_setelan"), "perubahan"));
                Ok("ubah_setelan hanya berisi medan yang berubah, dengan nama medan Rust",
                   tersimpan && ubah != null && ubah.Count == 2 && Protokol.Teks(ubah, "bahasa") == "en"
                   && Protokol.Angka(ubah, "jendela_lebar") == 1234,
                   ubah != null ? string.Join(",", ubah.Keys) : "(kosong)");
                Ok("Settings.Save tidak menunggu jawaban daemon", lamaSimpan < 500, lamaSimpan + " ms");
                Ok("Settings.Save tidak lagi mengirim simpan_setelan utuh", daemon.Hitung("simpan_setelan") == 0);
                bool tanpaUbah = e.Settings.Save();
                Thread.Sleep(200);
                Ok("Save tanpa perubahan tidak mengirim apa pun", tanpaUbah && daemon.Hitung("ubah_setelan") == 1);
                Ok("Settings.Save tidak menulis phoron.ini sendiri", !File.Exists(Paths.SettingsFile));

                // Ditolak daemon: tidak melempar, dicatat, dan medannya ikut lagi
                // di Save berikutnya.
                tolakUbah = true;
                e.Settings.Tema = "gelap";
                bool melempar = false;
                try { e.Settings.Save(); } catch { melempar = true; }
                Ok("Save yang ditolak daemon tidak melempar ke pemanggil", !melempar);
                Ok("Penolakan simpan dicatat di log",
                   TungguSampai(() => daemon.Diterima.Any(x => x.Key == "catat"
                       && (Protokol.Teks(Protokol.Objek(Protokol.Ambil(x.Value, "pesan")), "kunci") ?? "").Contains("Setelan ditolak uji"))));
                tolakUbah = false;
                e.Settings.Save();
                Ok("Medan yang gagal terkirim dikirim lagi di Save berikutnya",
                   TungguSampai(() => daemon.Hitung("ubah_setelan") == 3)
                   && Protokol.Teks(Protokol.Objek(Protokol.Ambil(daemon.Terakhir("ubah_setelan"), "perubahan")), "tema") == "gelap");
                Ok("Perubahan lokal tetap berlaku", e.Settings.Tema == "gelap");

                // Node
                var app = new NodeApp { Path = @"C:\proyek\vite", Name = "vite", Script = "dev", Manager = "npm" };
                var err = e.JalankanNode(app, 5174);
                var dataNode = daemon.Terakhir("jalankan_node");
                Ok("JalankanNode = jalankan_node dengan port sekali",
                   err == null && Protokol.Teks(dataNode, "path") == app.Path && Protokol.Angka(dataNode, "port_sekali") == 5174, err ?? "");
                Ok("URL dari jawaban jalankan_node langsung terlihat",
                   e.Node.IsRunning(app.Path) && e.Node.UrlOf(app.Path) == "http://localhost:5173");

                string urlKabar = null; bool? jalanKabar = null; string keluaran = null;
                e.Node.UrlFound += (f, u) => urlKabar = u;
                e.Node.StateChanged += (f, j) => jalanKabar = j;
                e.Node.Output += (f, b) => keluaran = b;
                daemon.Kabar("node_keluaran", new Dictionary<string, object> { { "path", app.Path }, { "baris", "ready in 300 ms" } });
                Ok("node_keluaran jadi Output dan riwayat",
                   TungguSampai(() => keluaran == "ready in 300 ms") && e.Node.Riwayat(app.Path).Contains("ready in 300 ms"));
                daemon.Kabar("node", new Dictionary<string, object> { { "path", app.Path }, { "berjalan", true }, { "url", "http://localhost:5174" } });
                Ok("Kabar node dengan URL baru menaikkan UrlFound", TungguSampai(() => urlKabar == "http://localhost:5174"));
                daemon.Kabar("node", new Dictionary<string, object> { { "path", app.Path }, { "berjalan", false }, { "port_bentrok", 5174 } });
                Ok("Kabar node berhenti menaikkan StateChanged",
                   TungguSampai(() => jalanKabar == false) && !e.Node.IsRunning(app.Path) && e.Node.PortBentrok(app.Path) == 5174);
                e.Node.Stop(app.Path);
                e.Node.StopAll();
                Ok("Node.Stop dan StopAll jadi perintah",
                   TungguSampai(() => daemon.Hitung("hentikan_node") == 1 && daemon.Hitung("hentikan_semua_node") == 1));

                daemon.Kabar("node_keluaran", new Dictionary<string, object>
                {
                    { "path", app.Path }, { "baris", "-- dihentikan --" },
                    { "pesan", new Pesan("Proyek {0} dihentikan uji.", "vite").KeJson() },
                });
                Ok("node_keluaran dengan pesan ditampilkan lewat Lang.T(kunci, args)",
                   TungguSampai(() => keluaran == "Proyek vite dihentikan uji.")
                   && e.Node.Riwayat(app.Path).Contains("Proyek vite dihentikan uji."), keluaran ?? "");

                var mati = new NodeApp { Path = @"C:\proyek\mati", Name = "mati", Script = "dev", Manager = "npm" };
                nodeMati = true;
                var errMati = e.JalankanNode(mati, 0);
                nodeMati = false;
                Ok("Kabar node berhenti yang mendahului jawaban jalankan_node tidak tertimpa",
                   errMati == null && !e.Node.IsRunning(mati.Path), errMati ?? "");

                // masalah_muat dari daemon digabung dengan keluhan muat UI, tanpa dobel.
                masalahDaemon = new object[]
                {
                    new Pesan("phoron.ini: baris {0} tidak terbaca", "3").KeJson(),
                    new Pesan("Profil {0} dilewati uji", "rusak.ini").KeJson(),
                };
                e.MasalahMuat.Add("phoron.ini: baris 3 tidak terbaca");
                daemon.Kabar("status", new Dictionary<string, object> { { "web", web }, { "db", db } });
                Ok("masalah_muat dari Keadaan terbaca", TungguSampai(() => e.MasalahDaemon.Count == 2));
                var periksa = e.Apply();
                Ok("masalah_muat ikut ditampilkan, digabung tanpa dobel",
                   periksa.Count(x => x == "phoron.ini: baris 3 tidak terbaca") == 1
                   && periksa.Contains("Profil rusak.ini dilewati uji") && periksa.Contains("Peringatan uji satu"),
                   string.Join(" | ", periksa));
                masalahDaemon = new object[0];
                e.MasalahMuat.Remove("phoron.ini: baris 3 tidak terbaca");

                // ------------------------------------------- perlu_muat_ulang
                int dariLuar = 0;
                e.BerubahDariLuar += () => Interlocked.Increment(ref dariLuar);
                Thread.Sleep(2200);   // gema aksi-aksi di atas kedaluwarsa

                // Gema aksi sendiri (ganti_profil) tidak memuat ulang.
                e.SwitchAsync(aktif, false).Wait();
                daemon.Kabar("perlu_muat_ulang");
                Thread.Sleep(300);
                Ok("Gema perlu_muat_ulang dari aksi sendiri tidak memuat ulang", dariLuar == 0, "dimuat " + dariLuar);

                // Kabar dari luar SELAMA aksi penyebab berjalan: ditahan, lalu
                // dimuat begitu aksinya selesai - tidak dibuang.
                Thread.Sleep(2200);
                File.WriteAllText(Paths.SettingsFile, "[umum]\r\nbahasa=jv\r\nprofil_aktif=uji-jarak\r\n");
                tundaGanti = 800;
                var tg = e.SwitchAsync(aktif, false);
                Thread.Sleep(250);
                daemon.Kabar("perlu_muat_ulang");   // dari menu baki
                Thread.Sleep(250);
                bool ditahan = dariLuar == 0 && !tg.IsCompleted;
                tg.Wait();
                tundaGanti = 0;
                daemon.Kabar("perlu_muat_ulang");   // gema ganti_profil
                Ok("Kabar dari luar selama aksi penyebab ditahan", ditahan);
                Ok("Kabar yang ditahan dimuat ulang sesudah aksinya selesai", TungguSampai(() => dariLuar == 1), "dimuat " + dariLuar);
                Ok("Setelan dimuat ulang ke objek yang sama", e.Settings.Bahasa == "jv", e.Settings.Bahasa);
                Thread.Sleep(300);
                Ok("Kabar dari luar dan gema dimuat sekali saja", dariLuar == 1, "dimuat " + dariLuar);

                // Aksi yang bukan penyebab (nyalakan_semua bisa sepuluh menit):
                // kabarnya langsung dimuat, tidak menunggu aksinya.
                Thread.Sleep(2200);
                File.WriteAllText(Paths.SettingsFile, "[umum]\r\nbahasa=bjn\r\nprofil_aktif=uji-jarak\r\n");
                tundaNyalakan = 1500;
                var tn = e.StartAllAsync();
                Thread.Sleep(200);
                daemon.Kabar("perlu_muat_ulang");
                Ok("perlu_muat_ulang selama aksi bukan penyebab langsung dimuat",
                   TungguSampai(() => dariLuar == 2, 1000) && !tn.IsCompleted && e.Settings.Bahasa == "bjn",
                   "dimuat " + dariLuar + ", selesai " + tn.IsCompleted);
                tn.Wait();
                tundaNyalakan = 0;
                Ok("Potret setelan ikut dimuat ulang: Save sesudahnya tidak mengirim apa pun",
                   e.Settings.Save() && SetelahJeda(() => daemon.Hitung("ubah_setelan") == 3));

                // ---------------------------------------- Hak Administrator
                Ok("HakAdmin mode jarak = hak daemon", !e.HakAdmin && !e.AdminDaemon);
                // Hosts dan sertifikat ditulis daemon; jendela tidak menyentuhnya.
                var cad = new HostsFile.Cadangan { Path = @"C:\di\mana\saja\hosts-20260101-000000.bak" };
                var hp = e.PulihkanHosts(cad);
                Ok("Pulihkan hosts lewat daemon: hanya NAMA cadangan yang dikirim",
                   namaCadangan == "hosts-20260101-000000.bak" && daemon.Hitung("pulihkan_hosts") == 1, namaCadangan ?? "(null)");
                Ok("Daemon tanpa Administrator: PerluAdmin, bukan galat biasa",
                   !hp.Ok && hp.PerluAdmin && hp.Galat == "Menyunting berkas hosts butuh hak Administrator.", hp.Galat ?? "(null)");
                var hs = e.PercayaiSertifikat();
                Ok("Percayai sertifikat lewat daemon", daemon.Hitung("percayai_sertifikat") == 1 && hs.PerluAdmin);
                batalUac = true;
                var hasilBatal = e.MulaiUlangAdminAsync().Result;
                batalUac = false;
                Ok("UAC dibatalkan: pesannya dari galat daemon", hasilBatal == "Dibatalkan di dialog UAC." && !e.AdminDaemon,
                   hasilBatal ?? "(null)");
                bool ikutKeluar = false;
                Action saatPamit = () => ikutKeluar = true;
                e.Pamit += saatPamit;
                int haloSebelum = daemon.Hitung("halo");
                e.BatasSambungAdmin = TimeSpan.FromSeconds(10);
                var hasilNaik = e.MulaiUlangAdminAsync().Result;
                Ok("mulai_ulang_admin: daemon baru tersambung dan keadaannya disegarkan",
                   hasilNaik == null && daemon.Hitung("mulai_ulang_admin") == 2 && e.AdminDaemon && e.HakAdmin
                   && daemon.Hitung("halo") > haloSebelum && klien.Aktif, hasilNaik ?? "");
                Ok("Pamit daemon lama saat mulai ulang admin tidak membuat UI keluar", !ikutKeluar);
                hp = e.PulihkanHosts(cad);
                Ok("Sesudah daemon ber-Administrator, pulihkan hosts berhasil", hp.Ok && !hp.PerluAdmin, hp.Galat ?? "");
                e.Pamit -= saatPamit;

                bool pamit = false;
                e.Pamit += () => pamit = true;
                daemon.Kabar("pamit");
                Ok("Kabar pamit sampai ke Engine", TungguSampai(() => pamit));

                // Save saat pipe putus: false, tidak melempar, tidak menahan
                // utas pemanggil, dan perubahan lokalnya tetap.
                using (var d2 = new DaemonPalsu())
                using (var k2 = new KlienDaemon(d2.Nama))
                {
                    k2.Sambung();
                    var e2 = new Engine(k2);
                    try
                    {
                        d2.Dispose();
                        Ok("Daemon uji kedua putus", TungguSampai(() => !k2.Aktif));
                        e2.Settings.Tema = "terang";
                        bool hasilPutus = true, lemparPutus = false;
                        var t0 = DateTime.UtcNow;
                        try { hasilPutus = e2.Settings.Save(); } catch { lemparPutus = true; }
                        var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
                        Ok("Save saat pipe putus mengembalikan false tanpa melempar dan tanpa menahan",
                           !hasilPutus && !lemparPutus && ms < 500, ms + " ms");
                        Ok("Perubahan lokal tetap berlaku walau belum terkirim", e2.Settings.Tema == "terang");
                        Ok("Kegagalan simpan dicatat di riwayat",
                           e2.Riwayat().Any(b => b.Teks.Contains("Setelan belum tersimpan")));
                    }
                    finally { e2.Lepas(); }
                }

                // Mode lama tidak berubah.
                var lama = new Engine();
                Ok("Engine tanpa klien tetap memegang layanan sendiri",
                   !lama.ModeJarak && lama.Services is ServiceManager && lama.Node is NodeRunner);
            }
            catch (Exception ex) { Ok("Engine jarak tanpa pengecualian", false, ex.ToString()); }
            finally
            {
                if (e != null) e.Lepas();
                klien.Dispose();
                daemon.Dispose();
                Paths.Root = dulu;
                try { Directory.Delete(akar, true); } catch { }
            }
        }
    }
}
