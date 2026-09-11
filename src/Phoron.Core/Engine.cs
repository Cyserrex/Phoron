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
    public class Engine
    {
        public Settings Settings { get; private set; }
        public List<BinPackage> Packages { get; private set; }
        public List<Profile> Profiles { get; private set; }
        public Profile Active { get; private set; }
        public ServiceManager Services { get; private set; }
        /// <summary>Proyek Node/TypeScript yang terdaftar, dan pengendali prosesnya.</summary>
        public NodeRunner Node { get; private set; }
        public List<NodeApp> NodeAppsList { get; private set; }
        public ConfigWriter.Result LastBuild { get; private set; }
        public List<Site> Sites { get; private set; }

        public event Action<string> Log;

        public Engine()
        {
            Settings = Settings.Load();
            // Ditulis sejak jalan pertama, bukan menunggu pengguna menyimpan
            // sesuatu: berkas ini juga jadi penanda akar instalasi, dan tanpanya
            // exe yang dipindah ke subfolder akan menebak akar yang salah.
            if (!File.Exists(Paths.SettingsFile)) Settings.Save();
            Packages = new List<BinPackage>();
            Profiles = new List<Profile>();
            Sites = new List<Site>();
            Services = new ServiceManager();
            Services.Log += Say;
            Services.LogRinci = Settings.LogRinci;
            Node = new NodeRunner();
            NodeAppsList = new List<NodeApp>();
        }

        public void Say(string text)
        {
            var h = Log;
            if (h != null) h(text);
            try
            {
                File.AppendAllText(Path.Combine(Paths.Logs, "phoron.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        // ------------------------------------------------------------- Pemuatan

        /// <summary>Pindai ulang versi dan muat profil. Aman dipanggil berkali-kali.</summary>
        public void Reload()
        {
            Packages = BinScanner.ScanAll(Settings.BinRoots);
            NodeAppsList = NodeAppStore.LoadAll();
            Profiles = ProfileStore.LoadAll();
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
        }

        // ------------------------------------------------------------- Resolusi

        public BinPackage Find(BinKind kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Packages.FirstOrDefault(p => p.Kind == kind
                && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<BinPackage> Of(BinKind kind) { return Packages.Where(p => p.Kind == kind); }

        public BinPackage Php { get { return Active == null ? null : Find(BinKind.Php, Active.PhpId); } }
        public BinPackage Apache { get { return Active == null ? null : Find(BinKind.Apache, Active.ApacheId); } }
        public BinPackage Nginx { get { return Active == null ? null : Find(BinKind.Nginx, Active.NginxId); } }
        public BinPackage MySql { get { return Active == null ? null : Find(BinKind.MySql, Active.MySqlId); } }
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
            bool webWasRunning = Services.WebState == ServiceState.Jalan;
            bool dbWasRunning = Services.DbState == ServiceState.Jalan;
            if (webWasRunning || dbWasRunning)
            {
                Say("Mematikan layanan sebelum ganti profil...");
                if (dbWasRunning) await Services.StopDbGracefullyAsync(Active ?? profile, MySql);
                if (webWasRunning) await Services.StopWebAsync();
            }

            Active = profile;
            Settings.ActiveProfile = profile != null ? profile.FileName : "";
            Settings.Save();
            var warnings = Apply();

            if (restartIfRunning && (webWasRunning || dbWasRunning))
            {
                if (dbWasRunning) await StartDbAsync();
                if (webWasRunning) await StartWebAsync();
            }
            return warnings;
        }

        /// <summary>Tulis ulang konfigurasi profil aktif tanpa menyentuh layanan.</summary>
        public List<string> Apply()
        {
            if (Active == null) return new List<string> { "Belum ada profil." };
            PastikanHalamanSambutan();
            var awal = PastikanSertifikat();
            RefreshSites();
            Services.LogRinci = Settings.LogRinci;
            LastBuild = ConfigWriter.Build(Active, Php, Apache, MySql, Nginx,
                                           Settings.AutoVhost ? Sites : new List<Site>(),
                                           Settings.PhpIniKeFolderPhp, Settings.LogRinci);
            // Masalah folder proyek disampaikan bersama peringatan konfigurasi -
            // kalau tidak, satu folder yang salah ketik hanya berwujud situs yang
            // hilang dari daftar tanpa sebab yang terlihat.
            LastBuild.Warnings.AddRange(SiteWarnings);
            LastBuild.Warnings.AddRange(awal);
            LastBuild.Warnings.AddRange(PaketKembar());

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
            // Dipindai ulang SETELAH vhost ditulis. Pemindaian di atas terjadi
            // sebelum berkasnya ada, jadi kolom "vhost" di halaman Situs akan
            // menunjukkan "-" untuk semua situs padahal berkasnya baru saja dibuat.
            RefreshSites();
            if (Settings.ManageHosts) SyncHosts(LastBuild.Warnings);
            foreach (var w in LastBuild.Warnings) Say("Peringatan: " + w);
            Say("Konfigurasi profil \"" + Active.Name + "\" ditulis ulang.");
            return LastBuild.Warnings;
        }

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
                var cocok = Packages.Where(p => p.Kind == d.Jenis
                    && string.Equals(p.Id, d.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (cocok.Count < 2) continue;
                pesan.Add("Nama folder \"" + d.Id + "\" ada di lebih dari satu folder bin ("
                          + string.Join(", ", cocok.Select(c => c.SourceRoot))
                          + "). Yang dipakai adalah " + cocok[0].Path
                          + "; ganti nama salah satunya supaya tidak ambigu.");
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
            if (SslTool.Exists) return pesan;
            var apache = Apache;
            if (apache == null || Active.WebServer == "nginx") return pesan;
            if (SslTool.FindOpenSsl(apache) == null) return pesan;   // paket tanpa openssl: diam saja

            var err = SslTool.Generate(apache, Active.SiteSuffix);
            if (err != null) pesan.Add("HTTPS tidak aktif - " + err);
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
                var names = Sites.Select(s => s.HostName).ToList();
                names.Add("localhost");
                HostsFile.Sync(names.Where(n => n != "localhost").ToList());
                RefreshSites();
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("Berkas hosts tidak bisa ditulis - jalankan Phoron sebagai Administrator "
                             + "atau matikan opsi 'kelola hosts'.");
            }
            catch (Exception ex) { warnings.Add("Gagal menyunting hosts: " + ex.Message); }
        }

        // -------------------------------------------------------------- Layanan

        public Task<bool> StartWebAsync()
        {
            if (Active == null) return Task.FromResult(false);
            if (LastBuild == null) Apply();
            return Services.StartWebAsync(Active, WebPackage, Php, LastBuild);
        }

        public Task StopWebAsync() { return Services.StopWebAsync(); }

        public Task<bool> StartDbAsync()
        {
            if (Active == null) return Task.FromResult(false);
            if (LastBuild == null) Apply();
            return Services.StartDbAsync(Active, MySql);
        }

        public Task StopDbAsync() { return Services.StopDbGracefullyAsync(Active, MySql); }

        public async Task StartAllAsync()
        {
            await StartDbAsync();
            await StartWebAsync();
        }

        public async Task StopAllAsync()
        {
            await StopWebAsync();
            await StopDbAsync();
        }

        // ------------------------------------------------------------- Kemudahan

        /// <summary>Variabel lingkungan untuk terminal/komposer: PHP dan MySQL profil aktif di depan PATH.</summary>
        public IDictionary<string, string> ToolEnv()
        {
            var parts = new List<string>();
            if (Php != null) parts.Add(Php.Path);
            if (MySql != null) parts.Add(Path.Combine(MySql.Path, "bin"));
            var composer = Path.Combine(Paths.Bin, "composer");
            if (Directory.Exists(composer)) parts.Add(composer);
            var env = new Dictionary<string, string>
            {
                { "PATH", string.Join(";", parts) + ";" + Environment.GetEnvironmentVariable("PATH") },
            };
            if (Php != null) env["PHPRC"] = Path.Combine(Paths.Etc, "php", Php.Id);
            return env;
        }

        public string SiteUrl(Site site)
        {
            if (Active == null) return "http://localhost/";
            var port = Active.HttpPort == 80 ? "" : ":" + Active.HttpPort;
            return "http://" + site.HostName + port + "/";
        }

        public string RootUrl()
        {
            if (Active == null) return "http://localhost/";
            return "http://localhost" + (Active.HttpPort == 80 ? "" : ":" + Active.HttpPort) + "/";
        }
    }
}
