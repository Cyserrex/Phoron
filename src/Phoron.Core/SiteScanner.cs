using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>Menerjemahkan isi folder proyek menjadi daftar situs beserta status vhost/hosts.</summary>
    public static class SiteScanner
    {
        /// <summary>Subfolder yang lazim dipakai framework sebagai akar web.</summary>
        static readonly string[] PublicDirs = { "public", "public_html", "web", "html" };

        public static List<Site> Scan(Profile profile)
        {
            return Scan(profile, null);
        }

        /// <summary>
        /// Pindai seluruh folder proyek profil. Peringatan (folder hilang, nama
        /// situs bentrok) ditambahkan ke <paramref name="warnings"/> bila diberikan.
        /// </summary>
        public static List<Site> Scan(Profile profile, List<string> warnings)
        {
            var suffix = Suffix(profile);
            var hosts = HostsFile.AllNames();
            var list = new List<Site>();
            // Nama host harus unik di seluruh folder proyek: dua vhost dengan
            // ServerName sama membuat Apache selalu melayani yang pertama, dan
            // folder kedua seolah-olah tidak pernah ada.
            var terpakai = new Dictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in Roots(profile))
            {
                if (!Directory.Exists(root))
                {
                    if (warnings != null) warnings.Add("Folder proyek tidak ada: " + root);
                    continue;
                }

                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception ex)
                {
                    if (warnings != null)
                        warnings.Add("Folder proyek tidak terbaca (" + root + "): " + ex.Message);
                    continue;
                }

                foreach (var dir in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(".") || name.StartsWith("_")) continue;

                    var dasar = SafeHost(name);
                    var host = dasar + "." + suffix;
                    if (terpakai.ContainsKey(host))
                    {
                        // Diberi angka, bukan dibuang: folder yang dilewati
                        // diam-diam adalah hal yang hampir mustahil ditebak
                        // sebabnya dari sisi pengguna.
                        var asli = host;
                        int n = 2;
                        while (terpakai.ContainsKey(dasar + "-" + n + "." + suffix)) n++;
                        host = dasar + "-" + n + "." + suffix;
                        if (warnings != null)
                            warnings.Add("Nama " + asli + " sudah dipakai " + terpakai[asli].Path
                                         + "; folder " + dir + " memakai " + host + " sebagai gantinya.");
                    }

                    var site = new Site
                    {
                        Folder = name,
                        Path = dir,
                        Root = root,
                        HostName = host,
                        DocRoot = FindDocRoot(dir),
                        InHosts = hosts.Contains(host),
                    };
                    site.HasVhost = File.Exists(VhostPath(site));
                    terpakai[host] = site;
                    list.Add(site);
                }
            }
            return list;
        }

        public static string Suffix(Profile profile)
        {
            var s = profile == null ? null : profile.SiteSuffix;
            return string.IsNullOrWhiteSpace(s) ? "test" : s.Trim().TrimStart('.');
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

        /// <summary>Semua folder proyek profil. Selalu berisi minimal satu entri.</summary>
        public static List<string> Roots(Profile profile)
        {
            var list = profile == null
                ? new List<string>()
                : profile.ProjectRoots.Where(r => !string.IsNullOrWhiteSpace(r))
                                      .Select(r => r.Trim()).ToList();
            if (list.Count == 0) list.Add(Paths.Www);
            return list;
        }

        /// <summary>
        /// Akar utama: yang dilayani http://localhost dan yang dibuka tombol
        /// "Buka www". Folder proyek tambahan hanya dijangkau lewat nama situsnya
        /// masing-masing - Apache tidak bisa menggabungkan beberapa folder di
        /// bawah satu DocumentRoot tanpa alias per folder.
        /// </summary>
        public static string DocumentRoot(Profile profile)
        {
            return Roots(profile)[0];
        }
    }
}
