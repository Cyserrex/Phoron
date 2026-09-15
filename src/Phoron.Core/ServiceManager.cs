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
        readonly object _lock = new object();
        Process _web, _db, _fcgi;

        public ServiceState WebState { get; private set; }
        public ServiceState DbState { get; private set; }

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

        public int WebPid { get { var p = _web; return p != null && !p.HasExited ? p.Id : 0; } }
        public int DbPid { get { var p = _db; return p != null && !p.HasExited ? p.Id : 0; } }

        void SetState(ServiceKind kind, ServiceState state)
        {
            if (kind == ServiceKind.Web) WebState = state; else DbState = state;
            var h = StateChanged;
            if (h != null) h(kind, state);
        }

        void Say(string text)
        {
            var h = Log;
            if (h != null) h(text);
        }

        // ------------------------------------------------------------- Web server

        public async Task<bool> StartWebAsync(Profile profile, BinPackage web, BinPackage php,
                                              ConfigWriter.Result cfg)
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

            if (profile.WebServer == "nginx") return await StartNginxAsync(web, php, cfg);
            return await StartApacheAsync(profile, web, php, cfg);
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
                                          ConfigWriter.Result cfg)
        {
            var httpd = Path.Combine(apache.Path, "bin", "httpd.exe");
            var conf = cfg.HttpdConf ?? Path.Combine(Paths.EtcApache, "httpd.conf");
            var args = "-f \"" + conf + "\" -d \"" + apache.Path + "\"";

            // Uji konfigurasi dulu. httpd yang gagal karena salah konfigurasi
            // mencetak sebabnya lalu keluar seketika; tanpa uji ini pengguna cuma
            // melihat "Gagal" tanpa satu baris pun keterangan.
            var test = await Task.Run(() => Shell.Run(httpd, args + " -t", apache.Path, 30000, EnvFor(php)));
            if (!test.Ok)
            {
                Say("Konfigurasi Apache ditolak:" + Environment.NewLine + test.All
                    + PetunjukGagal(apache, php, test.All));
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }

            if (cfg.PhpFastCgi && php != null && !await StartFastCgiAsync(php, cfg.FastCgiPort))
            {
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }

            var p = Spawn(httpd, args, apache.Path, EnvFor(php), "apache");
            if (p == null) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            _web = p;

            // httpd yang sehat tidak keluar. Kalau ia sudah mati dalam dua detik,
            // yang gagal adalah bind port atau modul, bukan konfigurasinya.
            await Task.Delay(1200);
            if (p.HasExited)
            {
                var ekor = EkorLogApache();
                Say("Apache berhenti seketika (kode " + p.ExitCode + ")."
                    + (ekor.Length > 0 ? Environment.NewLine + ekor : "")
                    + PetunjukGagal(apache, php, ekor));
                _web = null;
                StopFastCgi();
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            Say("Apache " + apache.Version + " jalan di port " + profile.HttpPort + " (PID " + p.Id + ").");
            SetState(ServiceKind.Web, ServiceState.Jalan);
            return true;
        }

        async Task<bool> StartNginxAsync(BinPackage nginx, BinPackage php, ConfigWriter.Result cfg)
        {
            var exe = Path.Combine(nginx.Path, "nginx.exe");
            var conf = cfg.NginxConf ?? Path.Combine(Paths.EtcNginx, "nginx.conf");
            var args = "-p \"" + nginx.Path + "\" -c \"" + conf + "\"";

            var test = await Task.Run(() => Shell.Run(exe, args + " -t", nginx.Path, 30000, EnvFor(php)));
            if (!test.Ok)
            {
                Say("Konfigurasi Nginx ditolak:\n" + test.All);
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            if (php != null && !await StartFastCgiAsync(php, cfg.FastCgiPort))
            {
                SetState(ServiceKind.Web, ServiceState.Gagal);
                return false;
            }
            var p = Spawn(exe, args, nginx.Path, EnvFor(php), "nginx");
            if (p == null) { SetState(ServiceKind.Web, ServiceState.Gagal); return false; }
            _web = p;
            await Task.Delay(1000);
            Say("Nginx " + nginx.Version + " jalan.");
            SetState(ServiceKind.Web, ServiceState.Jalan);
            return true;
        }

        public async Task StopWebAsync()
        {
            SetState(ServiceKind.Web, ServiceState.Mematikan);
            // Rujukannya dilepas SEBELUM dimatikan: pengawas Exited memakai
            // rujukan itu untuk membedakan "dimatikan pengguna" dari "mati
            // sendiri", dan kalau urutannya terbalik penghentian yang disengaja
            // ikut dilaporkan sebagai kegagalan.
            Process proc;
            lock (_lock) { proc = _web; _web = null; }
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

        async Task<bool> StartFastCgiAsync(BinPackage php, int port)
        {
            var exe = Path.Combine(php.Path, "php-cgi.exe");
            if (!File.Exists(exe)) { Say("php-cgi.exe tidak ada di " + php.Id + "."); return false; }
            StopFastCgi();

            var env = EnvFor(php);
            // php-cgi memutar ulang dirinya setelah sekian permintaan; nilai bawaan
            // 500 membuat pekerja mati di tengah pengembangan dan Apache membalas 503.
            env["PHP_FCGI_MAX_REQUESTS"] = "0";
            var p = Spawn(exe, "-b 127.0.0.1:" + port, php.Path, env, "php-cgi");
            if (p == null) return false;
            _fcgi = p;
            await Task.Delay(600);
            if (p.HasExited)
            {
                Say("php-cgi berhenti seketika - port " + port + " mungkin dipakai proses lain.");
                _fcgi = null;
                return false;
            }
            Say("php-cgi (FastCGI) jalan di 127.0.0.1:" + port + ".");
            return true;
        }

        void StopFastCgi()
        {
            var proc = _fcgi;
            _fcgi = null;   // dilepas dulu, lihat catatan di StopWebAsync
            if (proc == null) return;
            try { if (!proc.HasExited) Shell.KillTree(proc.Id); } catch { }
        }

        // ------------------------------------------------------------------ MySQL

        public async Task<bool> StartDbAsync(Profile profile, BinPackage mysql)
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
                if (!init) { SetState(ServiceKind.Db, ServiceState.Gagal); return false; }
                Say("Basis data siap. Pengguna: root, tanpa kata sandi.");
            }

            var mysqld = Path.Combine(mysql.Path, "bin", "mysqld.exe");
            var p = Spawn(mysqld, "--defaults-file=\"" + myIni + "\" --console", mysql.Path, null, "mysql");
            if (p == null) { SetState(ServiceKind.Db, ServiceState.Gagal); return false; }
            _db = p;

            // mysqld butuh waktu memulihkan InnoDB; port-nya dijadikan tanda siap
            // karena log-nya berbeda-beda antarversi.
            for (int i = 0; i < 40 && !p.HasExited; i++)
            {
                if (!PortCheck.IsFree(profile.MySqlPort))
                {
                    Say("MySQL " + mysql.Version + " jalan di port " + profile.MySqlPort + " (PID " + p.Id + ").");
                    SetState(ServiceKind.Db, ServiceState.Jalan);
                    return true;
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
            SetState(ServiceKind.Db, ServiceState.Mematikan);
            var proc = _db;
            _db = null;
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

        /// <summary>Matikan MySQL dengan rapi lewat mysqladmin sebelum jalan paksa - InnoDB tidak suka dibunuh.</summary>
        public async Task StopDbGracefullyAsync(Profile profile, BinPackage mysql)
        {
            if (_db == null) { SetState(ServiceKind.Db, ServiceState.Berhenti); return; }
            SetState(ServiceKind.Db, ServiceState.Mematikan);
            var admin = mysql != null ? Path.Combine(mysql.Path, "bin", "mysqladmin.exe") : null;
            if (admin != null && File.Exists(admin))
            {
                Say("Meminta MySQL berhenti dengan rapi...");
                await Task.Run(() => Shell.Run(admin,
                    "--protocol=tcp --port=" + profile.MySqlPort + " -u root shutdown",
                    mysql.Path, 20000));
                for (int i = 0; i < 30; i++)
                {
                    var d = _db;
                    if (d == null || d.HasExited) break;
                    await Task.Delay(400);
                }
            }
            await StopDbAsync();
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

            if (ReferenceEquals(_web, p))
            {
                _web = null;
                StopFastCgi();
                Say(tag + " berhenti sendiri (kode " + kode + "). Lihat logs\\apache-error.log.");
                SetState(ServiceKind.Web, ServiceState.Gagal);
            }
            else if (ReferenceEquals(_db, p))
            {
                _db = null;
                Say("MySQL berhenti sendiri (kode " + kode + "). Lihat logs\\mysql-error.log.");
                SetState(ServiceKind.Db, ServiceState.Gagal);
            }
            else if (ReferenceEquals(_fcgi, p) && WebState == ServiceState.Jalan)
            {
                _fcgi = null;
                Say("php-cgi berhenti sendiri (kode " + kode + ") - halaman PHP akan membalas 503.");
            }
        }

        /// <summary>PATH anak diberi folder PHP di depan supaya exec() dari skrip memakai versi profil ini.</summary>
        public static IDictionary<string, string> EnvFor(BinPackage php)
        {
            var env = new Dictionary<string, string>();
            if (php != null)
            {
                env["PATH"] = php.Path + ";" + Environment.GetEnvironmentVariable("PATH");
                env["PHPRC"] = Path.Combine(Paths.Etc, "php", php.Id);
            }
            return env;
        }

        /// <summary>Bunuh semua proses yang masih dipegang - dipanggil saat aplikasi ditutup.</summary>
        public void StopAll()
        {
            foreach (var p in new[] { _web, _fcgi, _db }.Where(x => x != null))
            {
                try { if (!p.HasExited) Shell.KillTree(p.Id); } catch { }
            }
            _web = _fcgi = _db = null;
            WebState = ServiceState.Berhenti;
            DbState = ServiceState.Berhenti;
        }
    }
}
