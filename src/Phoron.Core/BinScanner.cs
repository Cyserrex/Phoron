using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Phoron.Core
{
    /// <summary>
    /// Menemukan versi PHP/Apache/Nginx/MySQL yang terpasang dengan memindai
    /// folder bin. Sengaja berbasis nama folder + keberadaan exe, bukan menjalankan
    /// "php -v": memindai sepuluh folder dengan memanggil exe tiap kali makan
    /// beberapa detik, sedangkan daftar versi dipakai di layar pembuka.
    /// </summary>
    public static class BinScanner
    {
        static readonly Regex VersionRx = new Regex(@"(\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);
        static readonly Regex CompilerRx = new Regex(@"\b(vc\d{1,2}|vs\d{2})\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex MajorOnlyRx = new Regex(@"v(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Pindai semua root; hasilnya sudah diurutkan versi menurun.</summary>
        public static List<BinPackage> ScanAll(IEnumerable<string> roots)
        {
            var found = new List<BinPackage>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (var pkg in ScanRoot(root))
                {
                    // Folder yang sama muncul dua kali kalau pengguna menambah root
                    // yang beririsan; yang pertama menang.
                    if (!seen.Add(pkg.Path)) continue;
                    found.Add(pkg);
                }
            }
            return found
                .OrderBy(p => p.Kind)
                .ThenByDescending(p => p.Parsed)
                .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Sedalam apa penelusuran turun dari folder bin. Cukup untuk tata letak
        /// terdalam yang lazim, yaitu WAMP: &lt;root&gt;\bin\php\php8.1.0.
        /// </summary>
        const int KedalamanMaks = 3;

        /// <summary>
        /// Folder yang tidak mungkin berisi paket, tapi bisa sangat besar. Tanpa
        /// daftar ini, menambahkan folder bin yang salah sedikit saja - misalnya
        /// akar sebuah instalasi - membuat pemindaian merayapi htdocs dan
        /// node_modules milik pengguna.
        /// </summary>
        static readonly HashSet<string> Lewati = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ext", "conf", "logs", "log", "data", "tmp", "temp", "cache", "backup",
            "www", "htdocs", "public_html", "cgi-bin", "node_modules", ".git", ".svn",
            "include", "includes", "lib", "libs", "share", "man", "doc", "docs",
            "icons", "error", "modules", "sbin", "sessions", "uploads", "vendor",
        };

        static IEnumerable<BinPackage> ScanRoot(string root)
        {
            // Tata letak yang harus tertangani sekaligus:
            //   <root>\php-8.3.12-...            folder versi langsung di akar
            //   <root>\php\php-8.3.12-...        gaya Laragon
            //   <root>\php                       gaya XAMPP (tanpa folder versi)
            //   <root>\bin\php\php8.1.0          gaya WAMP
            // Daripada menambah pola satu per satu tiap kali ketemu pengelola
            // baru, folder ditelusuri sampai kedalaman terbatas dan yang
            // menentukan adalah ADA TIDAKNYA exe - lihat Identify.
            return Telusuri(root, root, 0);
        }

        static IEnumerable<BinPackage> Telusuri(string dir, string root, int dalam)
        {
            foreach (var sub in SafeDirs(dir))
            {
                if (Lewati.Contains(Path.GetFileName(sub))) continue;

                var pkg = Identify(sub, root);
                // Sebuah paket tidak ditelusuri lebih dalam: isinya berkas
                // miliknya sendiri, dan "bin" di dalam Apache bukan folder bin
                // dalam arti Phoron.
                if (pkg != null) { yield return pkg; continue; }

                if (dalam < KedalamanMaks)
                    foreach (var p in Telusuri(sub, root, dalam + 1))
                        yield return p;
            }
        }

        static IEnumerable<string> SafeDirs(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>Kenali satu folder sebagai paket, atau null bila bukan.</summary>
        public static BinPackage Identify(string dir, string sourceRoot = "")
        {
            if (!Directory.Exists(dir)) return null;
            var name = Path.GetFileName(dir.TrimEnd('\\'));
            var lower = name.ToLowerInvariant();

            string exe;
            if (File.Exists(exe = Path.Combine(dir, "php.exe")))
                return Php(dir, name, sourceRoot, exe);

            if (File.Exists(exe = Path.Combine(dir, "bin", "httpd.exe")))
                return Common(BinKind.Apache, dir, name, sourceRoot, exe);

            if (File.Exists(exe = Path.Combine(dir, "nginx.exe")))
                return Common(BinKind.Nginx, dir, name, sourceRoot, exe);

            if (File.Exists(exe = Path.Combine(dir, "bin", "mysqld.exe")))
                return Common(BinKind.MySql, dir, name, sourceRoot, exe);

            if (File.Exists(exe = Path.Combine(dir, "node.exe")))
                return Common(BinKind.Node, dir, name, sourceRoot, exe);

            // Nama folder yang menjanjikan tapi exe-nya tidak ada = instalasi rusak
            // atau baru setengah diekstrak; jangan ditawarkan.
            if (lower.StartsWith("php-") || lower.StartsWith("httpd-")
                || lower.StartsWith("mysql-") || lower.StartsWith("mariadb-")
                || lower.StartsWith("nginx-")) return null;
            return null;
        }

        static BinPackage Php(string dir, string name, string root, string exe)
        {
            var p = Common(BinKind.Php, dir, name, root, exe);
            // Build Thread Safe adalah satu-satunya yang punya modul Apache. Dicek
            // dari berkasnya, bukan dari nama folder: banyak arsip PHP dinamai
            // seragam padahal isinya NTS.
            var dll = Directory.GetFiles(dir, "php*apache2_4.dll").FirstOrDefault();
            p.ApacheModuleDll = dll;
            p.ThreadSafe = dll != null || !name.ToLowerInvariant().Contains("nts");
            return p;
        }

        static BinPackage Common(BinKind kind, string dir, string name, string root, string exe)
        {
            var pkg = new BinPackage
            {
                Kind = kind,
                Id = name,
                Path = dir,
                MainExe = exe,
                SourceRoot = root,
            };
            var m = VersionRx.Match(name);
            // Folder Node kerap dinamai hanya dengan nomor mayor ("node-v18"),
            // tanpa titik sama sekali - tanpa cadangan ini versinya kosong dan
            // urutan daftarnya jadi acak.
            if (!m.Success) m = MajorOnlyRx.Match(name);
            pkg.Version = m.Success ? m.Groups[1].Value : "";
            var c = CompilerRx.Match(name);
            pkg.Compiler = c.Success ? c.Groups[1].Value.ToUpperInvariant() : "";
            var lower = name.ToLowerInvariant();
            pkg.Arch = (lower.Contains("x64") || lower.Contains("win64") || lower.Contains("winx64"))
                ? "x64"
                : (lower.Contains("x86") || lower.Contains("win32") ? "x86" : "");

            // Tata letak seperti XAMPP menamai foldernya cuma "php" / "apache" /
            // "mysql", jadi namanya tidak menyebut apa-apa. Barulah di situ
            // binernya sendiri ditanya - lihat BinProbe untuk alasannya.
            if (pkg.Arch.Length == 0) pkg.Arch = BinProbe.Arsitektur(exe);
            if (pkg.Version.Length == 0) pkg.Version = BinProbe.Versi(kind, exe);
            return pkg;
        }

        /// <summary>
        /// Nama modul Apache untuk sebuah PHP: php5_module / php7_module / php_module.
        /// PHP 8 memakai "php_module" tanpa angka - salah menulisnya bikin Apache
        /// menolak start dengan pesan yang tidak menyebut sebabnya.
        /// </summary>
        public static string ApacheModuleName(BinPackage php)
        {
            if (php == null) return "php_module";
            var major = php.Parsed.Major;
            if (major == 5) return "php5_module";
            if (major == 7) return "php7_module";
            return "php_module";
        }
    }
}
