using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>Menerjemahkan isi folder www menjadi daftar situs beserta status vhost/hosts.</summary>
    public static class SiteScanner
    {
        /// <summary>Subfolder yang lazim dipakai framework sebagai akar web.</summary>
        static readonly string[] PublicDirs = { "public", "public_html", "web", "html" };

        public static List<Site> Scan(Profile profile)
        {
            var root = DocumentRoot(profile);
            var suffix = string.IsNullOrWhiteSpace(profile == null ? null : profile.SiteSuffix)
                ? "test" : profile.SiteSuffix.Trim().TrimStart('.');
            var hosts = HostsFile.AllNames();
            var list = new List<Site>();
            if (!Directory.Exists(root)) return list;

            foreach (var dir in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.StartsWith("_")) continue;
                var host = SafeHost(name) + "." + suffix;
                var site = new Site
                {
                    Folder = name,
                    Path = dir,
                    HostName = host,
                    DocRoot = FindDocRoot(dir),
                    InHosts = hosts.Contains(host),
                };
                site.HasVhost = File.Exists(VhostPath(site));
                list.Add(site);
            }
            return list;
        }

        public static string VhostPath(Site site)
        {
            return Path.Combine(Paths.SitesEnabled, "auto." + site.HostName + ".conf");
        }

        /// <summary>Nama folder bisa mengandung spasi atau garis bawah; nama host tidak boleh.</summary>
        public static string SafeHost(string folder)
        {
            var chars = folder.Select(c => char.IsLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '-').ToArray();
            var s = new string(chars).Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Length == 0 ? "situs" : s;
        }

        static string FindDocRoot(string dir)
        {
            foreach (var p in PublicDirs)
            {
                var candidate = Path.Combine(dir, p);
                // index.php harus benar-benar ada: banyak proyek punya folder
                // "public" berisi aset saja, dan menjadikannya DocumentRoot
                // membuat situsnya 404 seluruhnya.
                if (Directory.Exists(candidate)
                    && (File.Exists(Path.Combine(candidate, "index.php"))
                        || File.Exists(Path.Combine(candidate, "index.html"))))
                    return candidate;
            }
            return dir;
        }

        public static string DocumentRoot(Profile profile)
        {
            if (profile != null && !string.IsNullOrWhiteSpace(profile.DocumentRoot))
                return profile.DocumentRoot;
            return Paths.Www;
        }
    }
}
