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

        static IEnumerable<BinPackage> ScanRoot(string root)
        {
            // Dua bentuk tata letak didukung: <root>\php\php-8.3.12-... (gaya Laragon)
            // dan <root>\php-8.3.12-... (folder versi langsung di root).
            foreach (var dir in SafeDirs(root))
            {
                var pkg = Identify(dir, root);
                if (pkg != null) { yield return pkg; continue; }

                var name = Path.GetFileName(dir).ToLowerInvariant();
                if (name == "php" || name == "apache" || name == "nginx"
                    || name == "mysql" || name == "mariadb"
                    || name == "nodejs" || name == "node")
                {
                    foreach (var sub in SafeDirs(dir))
                    {
                        var p = Identify(sub, root);
                        if (p != null) yield return p;
                    }
                }
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
