using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>Baca/tulis profil di folder profiles\ (satu berkas .ini per profil).</summary>
    public static class ProfileStore
    {
        public static List<Profile> LoadAll()
        {
            var list = new List<Profile>();
            foreach (var f in Directory.GetFiles(Paths.Profiles, "*.ini").OrderBy(x => x))
            {
                try { list.Add(Load(f)); } catch { /* profil rusak dilewati, bukan alasan gagal start */ }
            }
            return list;
        }

        public static Profile Load(string path)
        {
            var ini = Ini.Load(path);
            var p = new Profile
            {
                FileName = Path.GetFileNameWithoutExtension(path),
                Name = ini.Get("profil", "nama", Path.GetFileNameWithoutExtension(path)),
                PhpId = ini.Get("profil", "php", ""),
                ApacheId = ini.Get("profil", "apache", ""),
                NginxId = ini.Get("profil", "nginx", ""),
                MySqlId = ini.Get("profil", "mysql", ""),
                WebServer = ini.Get("profil", "web_server", "apache"),
                HttpPort = ini.GetInt("profil", "port_http", 80),
                HttpsPort = ini.GetInt("profil", "port_https", 443),
                MySqlPort = ini.GetInt("profil", "port_mysql", 3306),
                SiteSuffix = ini.Get("profil", "akhiran_situs", "test"),
                Notes = ini.Get("profil", "catatan", ""),
            };
            // folder_proyek menggantikan document_root sejak dukungan banyak
            // folder. Berkas profil lama tetap dibaca lewat kunci lamanya -
            // menghapusnya diam-diam berarti proyek pengguna hilang dari daftar
            // tanpa penjelasan apa pun.
            var roots = ini.Get("profil", "folder_proyek", null)
                       ?? ini.Get("profil", "document_root", "");
            p.ProjectRoots = SplitRoots(roots);
            var ext = ini.Get("php", "ekstensi", "");
            p.PhpExtensions = ext.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim().ToLowerInvariant())
                                 .Where(x => x.Length > 0).Distinct().ToList();
            foreach (var kv in ini.Items("php.ini")) p.PhpIniOverrides[kv.Key] = kv.Value;
            return p;
        }

        public static void Save(Profile p)
        {
            if (string.IsNullOrWhiteSpace(p.FileName)) p.FileName = Slug(p.Name);
            var path = Path.Combine(Paths.Profiles, p.FileName + ".ini");
            var ini = new Ini();
            ini.Set("profil", "nama", p.Name);
            ini.Set("profil", "web_server", p.WebServer);
            ini.Set("profil", "php", p.PhpId);
            ini.Set("profil", "apache", p.ApacheId);
            ini.Set("profil", "nginx", p.NginxId);
            ini.Set("profil", "mysql", p.MySqlId);
            ini.Set("profil", "port_http", p.HttpPort.ToString());
            ini.Set("profil", "port_https", p.HttpsPort.ToString());
            ini.Set("profil", "port_mysql", p.MySqlPort.ToString());
            ini.Set("profil", "folder_proyek", string.Join(";", p.ProjectRoots));
            ini.Set("profil", "akhiran_situs", p.SiteSuffix ?? "test");
            ini.Set("profil", "catatan", (p.Notes ?? "").Replace("\r", " ").Replace("\n", " "));
            ini.Set("php", "ekstensi", string.Join(",", p.PhpExtensions));
            foreach (var kv in p.PhpIniOverrides) ini.Set("php.ini", kv.Key, kv.Value);
            ini.Save(path, "Profil Phoron - boleh disunting tangan.\nNama folder versi harus persis seperti di folder bin.");
        }

        public static void Delete(Profile p)
        {
            if (p == null || string.IsNullOrEmpty(p.FileName)) return;
            var path = Path.Combine(Paths.Profiles, p.FileName + ".ini");
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Pemisahnya titik koma, sama seperti bin_roots di phoron.ini. Jalur
        /// Windows tidak pernah memuat titik koma, jadi tidak ada yang perlu
        /// di-escape - dan satu baris tetap enak disunting tangan.
        /// </summary>
        public static List<string> SplitRoots(string value)
        {
            return (value ?? "")
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimEnd('\\'))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string Slug(string name)
        {
            var chars = (name ?? "profil").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
            var s = new string(chars).Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Length == 0 ? "profil" : s;
        }

        /// <summary>Nama berkas yang belum dipakai, supaya profil baru tidak menimpa yang lama.</summary>
        public static string UniqueFileName(string name)
        {
            var slug = Slug(name);
            var candidate = slug;
            int n = 2;
            while (File.Exists(Path.Combine(Paths.Profiles, candidate + ".ini")))
                candidate = slug + "-" + (n++);
            return candidate;
        }

        /// <summary>
        /// Pada jalan pertama belum ada profil apa pun. Daripada menyodorkan layar
        /// kosong, dibuatkan satu profil per versi PHP yang terpindai, dipasangkan
        /// dengan Apache yang toolset-nya cocok.
        /// </summary>
        public static List<Profile> Seed(List<BinPackage> packages)
        {
            var made = new List<Profile>();
            var phps = packages.Where(p => p.Kind == BinKind.Php).OrderByDescending(p => p.Parsed).ToList();
            var apaches = packages.Where(p => p.Kind == BinKind.Apache).ToList();
            var mysql = packages.FirstOrDefault(p => p.Kind == BinKind.MySql);
            foreach (var php in phps)
            {
                var apache = PickApache(php, apaches);
                var p = new Profile
                {
                    Name = "PHP " + php.Version + (apache != null ? " + Apache " + apache.Version : ""),
                    PhpId = php.Id,
                    ApacheId = apache != null ? apache.Id : "",
                    MySqlId = mysql != null ? mysql.Id : "",
                    WebServer = "apache",
                };
                p.FileName = UniqueFileName(p.Name);
                Save(p);
                made.Add(p);
            }
            return made;
        }

        /// <summary>
        /// Apache dan PHP harus dibangun dengan toolset yang sama - modul PHP VC11
        /// tidak akan dimuat oleh httpd VS16, dan gagalnya berupa crash saat start,
        /// bukan pesan yang jelas. Karena itu kecocokan toolset didahulukan.
        /// </summary>
        public static BinPackage PickApache(BinPackage php, IEnumerable<BinPackage> apaches)
        {
            var list = (apaches ?? Enumerable.Empty<BinPackage>()).ToList();
            if (php == null || list.Count == 0) return list.OrderByDescending(a => a.Parsed).FirstOrDefault();
            var same = list.Where(a => !string.IsNullOrEmpty(a.Compiler)
                                    && string.Equals(a.Compiler, php.Compiler, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(a => a.Parsed).FirstOrDefault();
            return same ?? list.OrderByDescending(a => a.Parsed).FirstOrDefault();
        }
    }
}
