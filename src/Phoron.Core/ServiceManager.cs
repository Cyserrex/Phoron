using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Phoron.Core
{
    /// <summary>
    /// Menyalakan dan mematikan Apache/Nginx, php-cgi, dan MySQL sebagai proses
    /// biasa - bukan sebagai Windows Service. Alasannya sama dengan Laragon:
    /// service butuh hak admin untuk dipasang, terus ada setelah aplikasi ditutup,
    /// dan tidak bisa punya dua versi berbeda yang gampang ditukar.
    /// </summary>
    public class ServiceManager
    {
        /// <summary>
        /// Menjaga tiga rujukan proses di bawah, itu saja. TIDAK pernah dipegang
        /// saat melakukan I/O, saat menunggu await, atau saat mengangkat
        /// peristiwa.
        ///
        /// Aturan terakhir bukan soal kerapian. MainWindow menyambungkan
        /// StateChanged lewat Dispatcher yang memblokir, jadi kunci yang masih
        /// dipegang saat peristiwa diangkat dari utas kolam - sementara utas
        /// layar menunggu kunci yang sama di StopAll - adalah kebuntuan yang
        /// sempurna.
        ///
        /// Dulu medan ini ada tapi hanya dipakai di SATU dari tiga belas tempat
        /// yang menyentuh ketiga rujukan itu.
        /// </summary>
        readonly object _lock = new object();
        Process _web, _db, _fcgi;

        // volatile: ditulis dari kelanjutan async di utas kolam, dibaca dari utas
        // layar setiap kali RefreshStatus jalan.
        volatile ServiceState _webState;
        volatile ServiceState _dbState;

        public ServiceState WebState { get { return _webState; } }
        public ServiceState DbState { get { return _dbState; } }

        // ------------------------------------------------- Rujukan proses
        // Satu-satunya tempat _web/_db/_fcgi boleh disentuh. Sebagian publik
        // supaya perlombaannya bisa diuji sungguhan, bukan diandaikan.

        public void PasangWeb(Process p) { lock (_lock) _web = p; }
        void PasangDb(Process p) { lock (_lock) _db = p; }
        void PasangFcgi(Process p) { lock (_lock) _fcgi = p; }

        Process AmbilWeb() { lock (_lock) return _web; }
        Process AmbilDb() { lock (_lock) return _db; }

        public Process AmbilLepasWeb() { lock (_lock) { var p = _web; _web = null; return p; } }
        Process AmbilLepasDb() { lock (_lock) { var p = _db; _db = null; return p; } }
        Process AmbilLepasFcgi() { lock (_lock) { var p = _fcgi; _fcgi = null; return p; } }

        /// <summary>
        /// Uji-lalu-kosongkan dalam SATU langkah. Inilah inti perbaikannya.
        ///
        /// Dulu ProsesMati - yang jalan di utas kolam lewat Process.Exited -
        /// membandingkan rujukannya lalu mengosongkannya sebagai dua langkah
        /// terpisah, sementara StopWebAsync di utas layar melakukan hal yang
        /// sama. Keduanya bisa melihat rujukan yang sama lalu sama-sama
        /// bertindak: penghentian yang DISENGAJA pengguna ikut dilaporkan
        /// sebagai "Apache berhenti sendiri", dan statusnya berubah jadi Gagal
        /// sesudah StopWebAsync menyetelnya ke Berhenti.
        /// </summary>
        public bool LepasWebJika(Process p)
        {
            lock (_lock) { if (!ReferenceEquals(_web, p)) return false; _web = null; return true; }
        }

        public bool LepasDbJika(Process p)
        {
            lock (_lock) { if (!ReferenceEquals(_db, p)) return false; _db = null; return true; }
        }

        public bool LepasFcgiJika(Process p)
        {
            lock (_lock) { if (!ReferenceEquals(_fcgi, p)) return false; _fcgi = null; return true; }
        }

        /// <summary>
        /// Teruskan SELURUH keluaran layanan, bukan hanya barisnya yang
        /// mencurigakan. Mati secara baku: mysqld mencetak ratusan baris tiap
        /// kali menyala, dan semuanya ikut tertulis ke phoron.log.
        /// </summary>
        public bool LogRinci;

        /// <summary>
        /// Baris yang tetap diteruskan walau log rinci mati. Tanpa penyaring
        /// ini, mematikan log berarti kegagalan start juga ikut hilang - dan
        /// itu justru satu-satunya saat keluarannya dibaca orang.
        /// </summary>
        static bool Penting(string baris)
        {
            if (string.IsNullOrEmpty(baris)) return false;
            var l = baris.ToLowerInvariant();
            return l.Contains("error") || l.Contains("fatal") || l.Contains("warning")
                || l.Contains("cannot") || l.Contains("failed") || l.Contains("denied");
        }

        public event Action<ServiceKind, ServiceState> StateChanged;
        public event Action<string> Log;

        public int WebPid { get { var p = AmbilWeb(); return p != null && !p.HasExited ? p.Id : 0; } }
        public int DbPid { get { var p = AmbilDb(); return p != null && !p.HasExited ? p.Id : 0; } }

        void SetState(ServiceKind kind, ServiceState state)
        {
            if (kind == ServiceKind.Web) _webState = state; else _dbState = state;
            var h = StateChanged;
            if (h != null) h(kind, state);
        }

        void Say(string text)
        {
            var h = Log;
            if (h != null) h(text);
        }

        // ------------------------------------------------------ Giliran start/stop
        //
        // DUA PERMINTAAN YANG BERTABRAKAN. Dulu start hanya menolak bila layanannya
        // sudah "Jalan" - bukan bila ia MASIH menyala. Akibatnya yang sungguh
        // terjadi:
        //
        //   - "Nyalakan semua" di baki ditekan dua kali selagi InnoDB memulihkan
        //     diri: dua mysqld, rujukan yang pertama tertimpa, dan mysqld yang
        //     benar-benar memegang 3306 tidak bisa dihentikan lagi sampai Phoron
        //     ditutup.
        //   - "Matikan semua" di tengah start web: Stop tidak menemukan apa pun
        //     untuk dimatikan, start melanjutkan, dan Apache tetap hidup di port
        //     80 walau orangnya meminta semuanya mati.
        //   - Stop di tengah jeda pemeriksaan: httpd yang BARU SAJA dibunuh Stop
        //     dilaporkan "Apache berhenti seketika", lengkap dengan saran VC++.
        //
        // Sekarang start yang masih berjalan dipakai ulang oleh permintaan start
        // berikutnya, dan setiap Stop menaikkan nomor giliran. Start memeriksa
        // nomor itu sesudah setiap await; kalau sudah berganti, ia membereskan
        // apa pun yang sempat ia lahirkan lalu mundur diam-diam - Stop yang sudah
        // melaporkan hasilnya.

        Task<bool> _mulaiWeb, _mulaiDb;
        int _giliranWeb, _giliranDb;

        int Giliran(ServiceKind jenis)
        {
            return jenis == ServiceKind.Web
                ? System.Threading.Volatile.Read(ref _giliranWeb)
                : System.Threading.Volatile.Read(ref _giliranDb);
        }

        bool Batal(ServiceKind jenis, int giliran) { return Giliran(jenis) != giliran; }

        void NaikkanGiliran(ServiceKind jenis)
        {
            if (jenis == ServiceKind.Web) System.Threading.Interlocked.Increment(ref _giliranWeb);
            else System.Threading.Interlocked.Increment(ref _giliranDb);
        }

        Task<bool> Antre(ServiceKind jenis, Func<int, Task<bool>> kerja)
        {
            TaskCompletionSource<bool> tcs;
            int giliran;
            lock (_lock)
            {
                var berjalan = jenis == ServiceKind.Web ? _mulaiWeb : _mulaiDb;
                if (berjalan != null && !berjalan.IsCompleted) return berjalan;
                tcs = new TaskCompletionSource<bool>();
                if (jenis == ServiceKind.Web) _mulaiWeb = tcs.Task; else _mulaiDb = tcs.Task;
                giliran = Giliran(jenis);
            }
            // Dijalankan DI LUAR kunci: badan start langsung mengangkat
            // StateChanged, dan peristiwa tidak boleh diangkat sambil memegang
            // _lock - lihat catatan di sana.
            var _ = Kerjakan(jenis, tcs, kerja, giliran);
            return tcs.Task;
        }

        /// <summary>
        /// Menjalankan badan start dan MENJAMIN statusnya tidak tertinggal di
        /// "Menyalakan". Galat yang lolos - exe yang dikarantina antivirus
        /// sesudah dipindai, folder data yang tidak bisa dibuat - dulu membuat
        /// tombol daya mati selamanya sampai Phoron dibuka ulang.
        /// </summary>
        async Task Kerjakan(ServiceKind jenis, TaskCompletionSource<bool> tcs,
                            Func<int, Task<bool>> kerja, int giliran)
        {
            try { tcs.TrySetResult(await kerja(giliran)); }
            catch (Exception ex)
            {
                Say((jenis == ServiceKind.Web ? "Web server" : "MySQL")
                    + " tidak bisa dinyalakan: " + ex.Message);
                if (!Batal(jenis, giliran)) SetState(jenis, ServiceState.Gagal);
                tcs.TrySetResult(false);
            }
        }

        // ------------------------------------------------------------- Web server

        public Task<bool> StartWebAsync(Profile profile, BinPackage web, BinPackage php,
                                        ConfigWriter.Result cfg)
        {
            return Antre(ServiceKind.Web, g => MulaiWebAsync(profile, web, php, cfg, g));
        }

        async Task<bool> MulaiWebAsync(Profile profile, BinPackage web, BinPackage php,
                                       ConfigWriter.Result cfg, int g)
        {
            if (WebState == ServiceState.Jalan) return true;
            if (web == null) { Say("Profil belum menunjuk web server."); SetState(ServiceKind.Web, ServiceState.Gagal); return false; }

            SetState(ServiceKind.Web, ServiceState.Menyalakan);
            var port = PortCheck.Check(profile.HttpPort);
            if (port.InUse)
            {
                // Berhenti di sini dengan sebab yang jelas jauh lebih berguna
                // daripada membiarkan Apache mati dengan kode 1 tanpa keterangan.
                Say(port.Describe() + " Matikan dulu aplikasi itu atau ganti port profil.");
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }

            if (cfg != null && cfg.SslEnabled)
            {
                // Apache yang gagal mengikat SATU port menolak start seluruhnya.
                // Tanpa pemeriksaan ini, port 443 yang dipakai aplikasi lain
                // membuat situs http ikut mati tanpa sebab yang kelihatan.
                var portSsl = PortCheck.Check(profile.HttpsPort);
                if (portSsl.InUse)
                {
                    Say(portSsl.Describe() + " HTTPS memakai port itu, dan Apache menolak "
                        + "start kalau salah satu portnya terpakai.");
                    SetState(ServiceKind.Web, ServiceState.Gagal);
                    return false;
                }
            }

            if (profile.WebServer == "nginx") return await StartNginxAsync(web, php, cfg, g);
            return await StartApacheAsync(profile, web, php, cfg, g);
        }

        /// <summary>
        /// Web server yang baru saja dilahirkan ternyata sudah tidak diminta
        /// lagi - Stop datang di tengah jalan. Bereskan proses itu sendiri, sebab
        /// Stop mungkin datang SEBELUM ia sempat dipasang dan tidak menemukannya.
        /// </summary>
        void BuangWebYatim(Process p)
        {
            if (p != null && LepasWebJika(p))
                try { if (!p.HasExited) Shell.KillTree(p.Id); } catch { }
            StopFastCgi();
        }

        /// <summary>
        /// Beberapa baris terakhir apache-error.log. Apache menulis sebab
        /// kematiannya ke sana, dan menyuruh orang membukanya sendiri hanya
        /// menunda jawaban yang sudah kita pegang.
        /// </summary>
        static string EkorLogApache(int baris = 6)
        {
            try
            {
                var f = Path.Combine(Paths.Logs, "apache-error.log");
                if (!File.Exists(f)) return "";
                var semua = File.ReadAllLines(f);
                var ambil = new List<string>();
                for (int i = Math.Max(0, semua.Length - baris); i < semua.Length; i++)
                    if (semua[i].Trim().Length > 0) ambil.Add(semua[i]);
                return string.Join(Environment.NewLine, ambil.ToArray());
            }
            catch { return ""; }
        }

        /// <summary>
        /// Menambahkan sebab yang paling sering di balik kegagalan Apache, kalau
        /// keluarannya memang cocok. Kalimat Windows "The specified module could
        /// not be found" menunjuk berkas PHP, padahal yang hilang biasanya
        /// Visual C++ Redistributable milik Microsoft.
        /// </summary>
        static string PetunjukGagal(BinPackage apache, BinPackage php, string keluaran)
        {
            var kurang = RuntimeVc.PeriksaSemua(php, apache);
            if (kurang.Count == 0) return "";
            return Environment.NewLine + Environment.NewLine + "Kemungkinan sebabnya: "
                   + string.Join(Environment.NewLine, kurang.ToArray());
        }

        async Task<bool> StartApacheAsync(Profile profile, BinPackage apache, BinPackage php,
                                          ConfigWriter.Result cfg, int g)
        {
            var httpd = Path.Combine(apache.Path, "bin", "httpd.exe");
            var conf = cfg.HttpdConf ?? Path.Combine(Paths.EtcApache, "httpd.conf");
            var args = "-f \"" + conf + "\" -d \"" + apache.Path + "\"";

            // Uji konfigurasi dulu. httpd yang gagal karena salah konfigurasi
            // mencetak sebabnya lalu keluar seketika; tanpa uji ini pengguna cuma
            // melihat "Gagal" tanpa satu baris pun keterangan.
            var test = await Task.Run(() => Shell.Run(httpd, args + " -t", apache.Path, 30000, EnvFor(php, cfg.PhpIniDir)));
            if (Batal(ServiceKind.Web, g)) return false;
            if (!test.Ok)
            {
                Say("Konfigurasi Apache ditolak:" + Environment.NewLine + test.All
                    + PetunjukGagal(apache, php, test.All));
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }

            if (cfg.PhpFastCgi && php != null)
            {
                var fcgi = await StartFastCgiAsync(php, cfg.FastCgiPort, cfg.PhpIniDir);
                if (Batal(ServiceKind.Web, g)) { StopFastCgi(); return false; }
                if (!fcgi) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            }

            var p = Spawn(httpd, args, apache.Path, EnvFor(php, cfg.PhpIniDir), "apache");
            if (p == null) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            PasangWeb(p);
            if (Batal(ServiceKind.Web, g)) { BuangWebYatim(p); return false; }

            // httpd yang sehat tidak keluar. Kalau ia sudah mati dalam dua detik,
            // yang gagal adalah bind port atau modul, bukan konfigurasinya.
            await Task.Delay(1200);
            // Stop yang datang selama jeda ini sudah membunuh httpd-nya. Tanpa
            // pemeriksaan ini, kematian yang DISENGAJA itu dilaporkan sebagai
            // "Apache berhenti seketika", lengkap dengan saran memasang VC++.
            if (Batal(ServiceKind.Web, g)) { BuangWebYatim(p); return false; }
            if (p.HasExited)
            {
                var ekor = EkorLogApache();
                Say("Apache berhenti seketika (kode " + p.ExitCode + ")."
                    + (ekor.Length > 0 ? Environment.NewLine + ekor : "")
                    + PetunjukGagal(apache, php, ekor));
                LepasWebJika(p);
                StopFastCgi();
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            Say("Apache " + apache.Version + " jalan di port " + profile.HttpPort + " (PID " + p.Id + ").");
            return Selesaikan(ServiceKind.Web, g, p);
        }

        /// <summary>
        /// Langkah terakhir start: nyatakan Jalan. Diperiksa SEKALI LAGI sesudahnya,
        /// sebab Stop dari utas lain bisa menyelinap tepat di antara pemeriksaan
        /// terakhir dan penyetelan status - dan lampu hijau untuk layanan yang
        /// sudah diminta mati adalah persis kebohongan yang sedang ditutup ini.
        /// </summary>
        bool Selesaikan(ServiceKind jenis, int g, Process p)
        {
            SetState(jenis, ServiceState.Jalan);
            if (!Batal(jenis, g)) return true;
            if (jenis == ServiceKind.Web) BuangWebYatim(p);
            else if (p != null && LepasDbJika(p)) try { if (!p.HasExited) Shell.KillTree(p.Id); } catch { }
            SetState(jenis, ServiceState.Berhenti);
            return false;
        }

        async Task<bool> StartNginxAsync(BinPackage nginx, BinPackage php, ConfigWriter.Result cfg, int g)
        {
            var exe = Path.Combine(nginx.Path, "nginx.exe");
            var conf = cfg.NginxConf ?? Path.Combine(Paths.EtcNginx, "nginx.conf");
            var args = "-p \"" + nginx.Path + "\" -c \"" + conf + "\"";

            var test = await Task.Run(() => Shell.Run(exe, args + " -t", nginx.Path, 30000, EnvFor(php, cfg.PhpIniDir)));
            if (Batal(ServiceKind.Web, g)) return false;
            if (!test.Ok)
            {
                Say("Konfigurasi Nginx ditolak:\n" + test.All);
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            if (php != null)
            {
                var fcgi = await StartFastCgiAsync(php, cfg.FastCgiPort, cfg.PhpIniDir);
                if (Batal(ServiceKind.Web, g)) { StopFastCgi(); return false; }
                if (!fcgi) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            }
            var p = Spawn(exe, args, nginx.Path, EnvFor(php, cfg.PhpIniDir), "nginx");
            if (p == null) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            PasangWeb(p);
            if (Batal(ServiceKind.Web, g)) { BuangWebYatim(p); return false; }

            // Penjaga yang sama seperti Apache, yang dulu TIDAK ada di sini:
            // nginx yang mati seketika - port terpakai, jalur log tidak bisa
            // dibuat - tetap dilaporkan "jalan", dan orang mencari-cari sebab
            // halamannya tidak terbuka padahal Phoron bilang semuanya beres.
            await Task.Delay(1000);
            if (Batal(ServiceKind.Web, g)) { BuangWebYatim(p); return false; }
            if (p.HasExited)
            {
                Say("Nginx berhenti seketika (kode " + p.ExitCode + "). "
                    + "Lihat logs\\nginx-error.log.");
                LepasWebJika(p);
                StopFastCgi();
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            Say("Nginx " + nginx.Version + " jalan (PID " + p.Id + ").");
            return Selesaikan(ServiceKind.Web, g, p);
        }

        public async Task StopWebAsync()
        {
            // PALING AWAL, sebelum apa pun: start yang sedang berjalan harus
            // melihat dirinya dibatalkan sebelum ia sempat melahirkan httpd yang
            // tidak akan ditemukan Stop ini.
            NaikkanGiliran(ServiceKind.Web);
            SetState(ServiceKind.Web, ServiceState.Mematikan);
            // Rujukannya dilepas SEBELUM dimatikan: pengawas Exited memakai
            // rujukan itu untuk membedakan "dimatikan pengguna" dari "mati
            // sendiri", dan kalau urutannya terbalik penghentian yang disengaja
            // ikut dilaporkan sebagai kegagalan.
            var proc = AmbilLepasWeb();
            await Task.Run(() =>
            {
                if (proc == null) return;
                try { if (!proc.HasExited) Shell.KillTree(proc.Id); } catch { }
            });
            StopFastCgi();
            Say("Web server dimatikan.");
            SetState(ServiceKind.Web, ServiceState.Berhenti);
        }

        // ----------------------------------------------------------------- PHP CGI

        async Task<bool> StartFastCgiAsync(BinPackage php, int port, string folderPhpIni)
        {
            var exe = Path.Combine(php.Path, "php-cgi.exe");
            if (!File.Exists(exe)) { Say("php-cgi.exe tidak ada di " + php.Id + "."); return false; }
            StopFastCgi();

            var env = EnvFor(php, folderPhpIni);
            // php-cgi memutar ulang dirinya setelah sekian permintaan; nilai bawaan
            // 500 membuat pekerja mati di tengah pengembangan dan Apache membalas 503.
            env["PHP_FCGI_MAX_REQUESTS"] = "0";
            // Tanpa ini hanya ADA SATU responder, dan permintaan .php dilayani
            // satu per satu: halaman dengan beberapa panggilan AJAX terasa
            // tersendat tanpa sebab yang kelihatan di mana pun.
            //
            // Diukur di mesin pengembang, sepuluh permintaan serentak ke berkas
            // yang tidur satu detik: 10,1 detik dengan satu responder, 3,0 detik
            // dengan empat pekerja.
            //
            // Kebiasaan umum menyebut variabel ini tidak berlaku di Windows
            // karena pekerjanya lahir lewat fork(). Itu keliru, dan sudah
            // dibuktikan: php-cgi.exe 8.3 memang melahirkan empat proses anak,
            // dan keempatnya benar-benar melayani bersamaan.
            //
            // Anak-anak itu ikut mati bersama induknya - StopFastCgi memakai
            // KillTree, bukan Kill.
            env["PHP_FCGI_CHILDREN"] = "4";
            var p = Spawn(exe, "-b 127.0.0.1:" + port, php.Path, env, "php-cgi");
            if (p == null) return false;
            PasangFcgi(p);
            await Task.Delay(600);
            if (p.HasExited)
            {
                Say("php-cgi berhenti seketika - port " + port + " mungkin dipakai proses lain.");
                LepasFcgiJika(p);
                return false;
            }
            Say("php-cgi (FastCGI) jalan di 127.0.0.1:" + port + ".");
            return true;
        }

        void StopFastCgi()
        {
            var proc = AmbilLepasFcgi();   // dilepas dulu, lihat catatan di StopWebAsync
            if (proc == null) return;
            try { if (!proc.HasExited) Shell.KillTree(proc.Id); } catch { }
        }

        // ------------------------------------------------------------------ MySQL

        // Paket dan port mysqld yang sedang dijalankan. StopAll dipanggil saat
        // aplikasi ditutup dan tidak menerima profil apa pun, padahal cadangan
        // penghentian rapinya - mysqladmin - butuh keduanya.
        BinPackage _dbPaket;
        int _dbPort;

        public Task<bool> StartDbAsync(Profile profile, BinPackage mysql)
        {
            return Antre(ServiceKind.Db, g => MulaiDbAsync(profile, mysql, g));
        }

        async Task<bool> MulaiDbAsync(Profile profile, BinPackage mysql, int g)
        {
            if (DbState == ServiceState.Jalan) return true;
            if (mysql == null) { Say("Profil belum menunjuk versi MySQL."); return false; }
            SetState(ServiceKind.Db, ServiceState.Menyalakan);

            var port = PortCheck.Check(profile.MySqlPort);
            if (port.InUse)
            {
                Say(port.Describe() + " Matikan dulu, atau ubah port MySQL profil ini.");
                SetState(ServiceKind.Db, ServiceState.Gagal);
                return false;
            }

            var myIni = Path.Combine(Paths.EtcMysql, "my.ini");
            var dataDir = ConfigWriter.MySqlDataDir(mysql);
            if (!IsInitialized(dataDir))
            {
                Say("Folder data " + mysql.Id + " belum ada - menyiapkan basis data awal...");
                var init = await Task.Run(() => Initialize(mysql, myIni, dataDir));
                if (Batal(ServiceKind.Db, g)) return false;
                if (!init) { SetState(ServiceKind.Db, ServiceState.Gagal); return false; }
                Say("Basis data siap. Pengguna: root, tanpa kata sandi.");
            }

            var mysqld = Path.Combine(mysql.Path, "bin", "mysqld.exe");
            var p = Spawn(mysqld, "--defaults-file=\"" + myIni + "\" --console", mysql.Path, null, "mysql");
            if (p == null) { SetState(ServiceKind.Db, ServiceState.Gagal); return false; }
            PasangDb(p);
            _dbPaket = mysql;
            _dbPort = profile.MySqlPort;

            // mysqld butuh waktu memulihkan InnoDB; port-nya dijadikan tanda siap
            // karena log-nya berbeda-beda antarversi.
            for (int i = 0; i < 40; i++)
            {
                // Stop datang selagi InnoDB masih memulihkan diri. Kalau Stop itu
                // datang sebelum mysqld ini sempat dipasang, ia tidak menemukannya
                // - jadi yang membereskannya start ini sendiri.
                if (Batal(ServiceKind.Db, g))
                {
                    if (LepasDbJika(p)) try { if (!p.HasExited) Shell.KillTree(p.Id); } catch { }
                    return false;
                }
                // Mati sendiri di tengah start. ProsesMati sudah melaporkannya dan
                // menyetel Gagal; menambahkan "gagal siap dalam 20 detik" lalu
                // "MySQL dimatikan" hanya menulis dua kebohongan di bawah laporan
                // yang benar.
                if (p.HasExited) return false;
                if (!PortCheck.IsFree(profile.MySqlPort))
                {
                    Say("MySQL " + mysql.Version + " jalan di port " + profile.MySqlPort + " (PID " + p.Id + ").");
                    return Selesaikan(ServiceKind.Db, g, p);
                }
                await Task.Delay(500);
            }
            Say("MySQL gagal siap dalam 20 detik. Lihat logs\\mysql-error.log.");
            await StopDbAsync();
            SetState(ServiceKind.Db, ServiceState.Gagal);
            return false;
        }

        public async Task StopDbAsync()
        {
            NaikkanGiliran(ServiceKind.Db);
            SetState(ServiceKind.Db, ServiceState.Mematikan);
            var proc = AmbilLepasDb();
            if (proc != null)
            {
                await Task.Run(() =>
                {
                    try { if (!proc.HasExited) Shell.KillTree(proc.Id); } catch { }
                });
            }
            Say("MySQL dimatikan.");
            SetState(ServiceKind.Db, ServiceState.Berhenti);
        }

        /// <summary>
        /// Matikan MySQL dengan rapi - InnoDB tidak suka dibunuh, dan tabel MyISAM
        /// bisa rusak karenanya.
        ///
        /// Rujukannya dilepas LEBIH DULU, sebelum mysqld diminta berhenti. Dulu
        /// urutannya terbalik: mysqld keluar dengan tertib, pengawas Exited masih
        /// menemukan rujukannya, dan setiap penghentian rapi dilaporkan "MySQL
        /// berhenti sendiri (kode 0)" dengan lampu merah - di log pengguna, 42
        /// kali.
        /// </summary>
        public async Task StopDbGracefullyAsync(Profile profile, BinPackage mysql)
        {
            NaikkanGiliran(ServiceKind.Db);
            var proc = AmbilLepasDb();
            if (proc == null) { SetState(ServiceKind.Db, ServiceState.Berhenti); return; }
            SetState(ServiceKind.Db, ServiceState.Mematikan);
            Say("Meminta MySQL berhenti dengan rapi...");
            var port = profile != null ? profile.MySqlPort : _dbPort;
            var rapi = await Task.Run(() => MatikanMysqlRapi(proc, mysql ?? _dbPaket, port));
            if (!rapi) Say("MySQL tidak mau berhenti dengan rapi - dimatikan paksa.");
            Say("MySQL dimatikan.");
            SetState(ServiceKind.Db, ServiceState.Berhenti);
        }

        /// <summary>
        /// Minta mysqld berhenti dengan tertib, tunggu, lalu paksa bila perlu.
        /// Mengembalikan true bila ia berhenti sendiri tanpa dipaksa.
        ///
        /// Jalan pertama: event bernama "MySQLShutdown&lt;PID&gt;" yang dibuat
        /// mysqld di Windows - jalan yang sama yang dipakai saat layanan Windows-
        /// nya dihentikan. Tidak butuh kata sandi apa pun. Dibuktikan pada MySQL
        /// 5.7.38: berhenti dalam 1,1 detik, log mencatat "Shutdown complete".
        ///
        /// Cadangannya mysqladmin, untuk build yang tidak membuat event itu.
        /// mysqladmin dijalankan sebagai root TANPA sandi, jadi begitu root diberi
        /// sandi cadangan ini gagal - karena itulah ia bukan jalan pertama.
        /// </summary>
        /// <summary>
        /// Setel event "MySQLShutdown&lt;PID&gt;" milik mysqld. Mengembalikan
        /// false bila event itu tidak ada (build yang tidak membuatnya, atau
        /// mysqld milik pengguna lain yang tidak bisa dibuka).
        /// </summary>
        public static bool MintaMysqlBerhenti(int pid)
        {
            try
            {
                System.Threading.EventWaitHandle ev;
                if (!System.Threading.EventWaitHandle.TryOpenExisting("MySQLShutdown" + pid, out ev)) return false;
                using (ev) return ev.Set();
            }
            catch { return false; }
        }

        bool MatikanMysqlRapi(Process proc, BinPackage mysql, int port)
        {
            try
            {
                if (proc.HasExited) return true;
                bool diminta = MintaMysqlBerhenti(proc.Id);

                if (!diminta && mysql != null && port > 0)
                {
                    var admin = Path.Combine(mysql.Path, "bin", "mysqladmin.exe");
                    if (File.Exists(admin))
                        Shell.Run(admin, "--protocol=tcp --port=" + port + " -u root shutdown",
                                  mysql.Path, 15000);
                }
                // InnoDB yang sedang menulis buffer pool-nya bisa perlu beberapa
                // detik; dua belas detik jauh di atas yang pernah terukur.
                if (proc.WaitForExit(12000)) return true;
                Shell.KillTree(proc.Id);
                return false;
            }
            catch
            {
                try { if (!proc.HasExited) Shell.KillTree(proc.Id); } catch { }
                return false;
            }
        }

        static bool IsInitialized(string dataDir)
        {
            return Directory.Exists(Path.Combine(dataDir, "mysql"));
        }

        bool Initialize(BinPackage mysql, string myIni, string dataDir)
        {
            Directory.CreateDirectory(dataDir);
            var bin = Path.Combine(mysql.Path, "bin");
            var installDb = Path.Combine(bin, "mysql_install_db.exe");
            Shell.RunResult res;
            if (File.Exists(installDb))
            {
                // MariaDB. mysqld --initialize tidak ada di sana.
                res = Shell.Run(installDb,
                    "--datadir=\"" + dataDir + "\" --service= --password=", bin, 300000);
            }
            else
            {
                res = Shell.Run(Path.Combine(bin, "mysqld.exe"),
                    "--defaults-file=\"" + myIni + "\" --initialize-insecure --console", bin, 300000);
            }
            if (!res.Ok && !IsInitialized(dataDir))
            {
                Say("Gagal menyiapkan folder data:\n" + res.All);
                return false;
            }
            return true;
        }

        // --------------------------------------------------------------- Utilitas

        Process Spawn(string exe, string args, string workDir, IDictionary<string, string> env, string tag)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                DataReceivedEventHandler baca = (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    if (LogRinci || Penting(e.Data)) Say("[" + tag + "] " + e.Data);
                };
                p.OutputDataReceived += baca;
                p.ErrorDataReceived += baca;
                // Proses bisa mati sendiri setelah dilaporkan "jalan": httpd yang
                // kehabisan port saat vhost baru ditambahkan, mysqld yang gagal
                // memulihkan InnoDB, php-cgi yang ditutup paksa. Tanpa pengawas
                // ini, lampu indikator tetap hijau padahal tidak ada lagi yang
                // mendengarkan - dan pengguna mencari-cari sebabnya di browser.
                p.Exited += (s, e) => ProsesMati(p, tag);
                p.Start();
                // Diikat SEGERA setelah start: begitu Phoron berakhir dengan cara
                // apa pun, Windows ikut menutup proses ini. Tanpa itu, httpd dan
                // mysqld jadi yatim dan tetap memegang port 80 serta 3306.
                ProcessJob.Ikat(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return p;
            }
            catch (Exception ex)
            {
                Say("Tidak bisa menjalankan " + Path.GetFileName(exe) + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Dipanggil saat sebuah proses anak berakhir. Hanya berarti kalau proses
        /// itu MASIH yang sedang dipegang: penghentian yang disengaja lebih dulu
        /// melepas rujukannya, jadi perbandingan ini yang membedakan "dimatikan
        /// pengguna" dari "mati sendiri".
        /// </summary>
        void ProsesMati(Process p, string tag)
        {
            int kode;
            try { kode = p.ExitCode; } catch { kode = -1; }

            // Uji-lalu-kosongkan, bukan bandingkan lalu kosongkan. Persis satu
            // pihak yang menang antara ini dan StopWebAsync, jadi penghentian
            // yang disengaja tidak lagi ikut dilaporkan sebagai kegagalan.
            // Say dan SetState sengaja DI LUAR kunci - lihat catatan di _lock.
            if (LepasWebJika(p))
            {
                StopFastCgi();
                Say(tag + " berhenti sendiri (kode " + kode + "). Lihat logs\\apache-error.log.");
                SetState(ServiceKind.Web, ServiceState.Gagal);
            }
            else if (LepasDbJika(p))
            {
                Say("MySQL berhenti sendiri (kode " + kode + "). Lihat logs\\mysql-error.log.");
                SetState(ServiceKind.Db, ServiceState.Gagal);
            }
            else if (LepasFcgiJika(p))
            {
                // Rujukannya dikosongkan lebih dulu, baru keadaannya diperiksa:
                // kalau urutannya terbalik, php-cgi yang mati saat web server
                // sudah berhenti akan tertinggal sebagai rujukan basi.
                if (WebState == ServiceState.Jalan)
                    Say("php-cgi berhenti sendiri (kode " + kode + ") - halaman PHP akan membalas 503.");
            }
        }

        /// <summary>PATH anak diberi folder PHP di depan supaya exec() dari skrip memakai versi profil ini.</summary>
        public static IDictionary<string, string> EnvFor(BinPackage php, string folderPhpIni = null)
        {
            var env = new Dictionary<string, string>();
            if (php != null)
            {
                env["PATH"] = php.Path + ";" + Environment.GetEnvironmentVariable("PATH");
                // Ke folder php.ini yang BENAR-BENAR ditulis - lihat
                // ConfigWriter.FolderPhpIni.
                env["PHPRC"] = folderPhpIni ?? ConfigWriter.FolderPhpIni(php, false);
            }
            return env;
        }

        /// <summary>Bunuh semua proses yang masih dipegang - dipanggil saat aplikasi ditutup.</summary>
        public void StopAll()
        {
            // Dipotret di dalam kunci, dibunuh di LUAR kunci. KillTree lambat
            // dan memicu callback Exited di utas kolam yang ikut meminta kunci
            // yang sama; memegangnya selama pembunuhan berarti menahan mereka
            // semua di belakang utas layar tanpa alasan.
            //
            // Start yang masih berjalan ikut dibatalkan, supaya ia tidak
            // melahirkan httpd atau mysqld baru sesudah semuanya dibereskan.
            NaikkanGiliran(ServiceKind.Web);
            NaikkanGiliran(ServiceKind.Db);
            Process w, f, d;
            lock (_lock)
            {
                w = _web; f = _fcgi; d = _db;
                _web = _fcgi = _db = null;
            }
            // Web server dan php-cgi aman dibunuh: tidak ada yang mereka tulis
            // setengah jalan.
            foreach (var p in new[] { w, f }.Where(x => x != null))
            {
                try { if (!p.HasExited) Shell.KillTree(p.Id); } catch { }
            }
            // MySQL TIDAK. Dulu ia ikut dibunuh dengan taskkill /F setiap kali
            // Phoron ditutup, diperbarui, atau Windows dimatikan - sementara
            // lognya berkata "mematikan layanan dulu". Tiap start sesudahnya
            // diawali pemulihan InnoDB, dan tabel MyISAM bisa rusak.
            if (d != null)
            {
                if (MatikanMysqlRapi(d, _dbPaket, _dbPort)) Say("MySQL dimatikan dengan rapi.");
                else Say("MySQL tidak mau berhenti dengan rapi - dimatikan paksa.");
            }
            // Lewat SetState, bukan medannya langsung: tanpa peristiwa ini lampu
            // di layar tetap hijau untuk layanan yang sudah mati.
            SetState(ServiceKind.Web, ServiceState.Berhenti);
            SetState(ServiceKind.Db, ServiceState.Berhenti);
        }
    }
}
