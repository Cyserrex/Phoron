using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Phoron.Core
{
    /// <summary>Satu versi yang bisa diunduh.</summary>
    public class RemotePackage
    {
        public BinKind Kind;
        /// <summary>
        /// Nama folder tujuan. Properti, bukan field: ComboBox menampilkannya
        /// lewat DisplayMemberPath, dan binding WPF tidak bisa membaca field -
        /// gagalnya berupa daftar yang tampil kosong tanpa pesan apa pun.
        /// </summary>
        public string Name { get; set; }
        public string Url { get; set; }
        public string Version { get; set; }
        public string Note { get; set; }

        public RemotePackage() { Version = ""; Note = ""; }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Mengunduh dan mengekstrak versi baru ke folder bin milik Phoron.
    /// Daftar PHP diambil langsung dari windows.php.net; Apache dan MySQL dari
    /// katalog di etc\catalog.ini karena situs mereka tidak menyediakan indeks
    /// yang stabil untuk dibaca mesin.
    /// </summary>
    public static class Downloader
    {
        const string PhpBase = "https://windows.php.net/downloads/releases/";

        static Downloader()
        {
            // .NET Framework 4.8 masih default ke protokol lama pada beberapa mesin;
            // tanpa baris ini unduhan gagal dengan "koneksi ditutup" yang menyesatkan.
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; }
            catch { }
        }

        public static async Task<List<RemotePackage>> ListPhpAsync()
        {
            var list = new List<RemotePackage>();
            string html;
            try { html = await GetStringAsync(PhpBase); }
            catch (Exception ex) { throw new Exception("Tidak bisa membaca daftar PHP: " + ex.Message); }

            // Indeks berkasnya HTML biasa; nama arsip yang menarik selalu berbentuk
            // php-<versi>-Win32-<toolset>-x64.zip. Paket debug dan devel dilewati.
            var rx = new Regex("php-(\\d+\\.\\d+\\.\\d+)-(nts-)?Win32-(vc|vs)(\\d+)-(x64|x86)\\.zip",
                               RegexOptions.IgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in rx.Matches(html))
            {
                var name = m.Value;
                if (!seen.Add(name)) continue;
                list.Add(new RemotePackage
                {
                    Kind = BinKind.Php,
                    Name = Path.GetFileNameWithoutExtension(name),
                    Url = PhpBase + name,
                    Version = m.Groups[1].Value,
                    Note = string.IsNullOrEmpty(m.Groups[2].Value) ? "Thread Safe" : "Non Thread Safe",
                });
            }
            return list.OrderByDescending(p => new BinPackage { Version = p.Version }.Parsed)
                       .ThenBy(p => p.Name).ToList();
        }

        /// <summary>Katalog Apache/MySQL/Nginx yang bisa disunting pengguna di etc\catalog.ini.</summary>
        public static List<RemotePackage> Catalog()
        {
            var path = Path.Combine(Paths.Etc, "catalog.ini");
            if (!File.Exists(path)) WriteDefaultCatalog(path);
            var ini = Ini.Load(path);
            var list = new List<RemotePackage>();
            foreach (var section in new[] { "apache", "mysql", "nginx" })
            {
                var kind = section == "apache" ? BinKind.Apache
                         : section == "mysql" ? BinKind.MySql : BinKind.Nginx;
                foreach (var kv in ini.Items(section))
                {
                    var m = Regex.Match(kv.Key, "(\\d+\\.\\d+(?:\\.\\d+)?)");
                    list.Add(new RemotePackage
                    {
                        Kind = kind,
                        Name = kv.Key,
                        Url = kv.Value,
                        Version = m.Success ? m.Groups[1].Value : "",
                    });
                }
            }
            return list;
        }

        static void WriteDefaultCatalog(string path)
        {
            var ini = new Ini();
            // Apache Lounge menolak permintaan tanpa header User-Agent browser;
            // Downloader sudah memasangnya, jadi tautan ini tetap bisa dipakai.
            ini.Set("apache", "httpd-2.4.62-win64-VS17", "https://www.apachelounge.com/download/VS17/binaries/httpd-2.4.62-240904-win64-VS17.zip");
            ini.Set("apache", "httpd-2.4.58-win64-VS17", "https://www.apachelounge.com/download/VS17/binaries/httpd-2.4.58-win64-VS17.zip");
            ini.Set("mysql", "mysql-8.0.39-winx64", "https://dev.mysql.com/get/Downloads/MySQL-8.0/mysql-8.0.39-winx64.zip");
            ini.Set("mysql", "mysql-5.7.44-winx64", "https://dev.mysql.com/get/Downloads/MySQL-5.7/mysql-5.7.44-winx64.zip");
            ini.Set("nginx", "nginx-1.27.1", "https://nginx.org/download/nginx-1.27.1.zip");
            ini.Save(path,
                "Katalog unduhan Phoron.\nTambahkan baris sendiri: <nama folder>=<url zip>.\n" +
                "Tautan bisa mati sewaktu-waktu; ganti dengan yang baru bila perlu.");
        }

        /// <summary>Unduh lalu ekstrak ke bin\&lt;jenis&gt;\&lt;nama&gt;. Melaporkan kemajuan 0-100.</summary>
        public static async Task<string> InstallAsync(RemotePackage pkg, IProgress<int> progress,
                                                      CancellationToken token)
        {
            var kindDir = Path.Combine(Paths.Bin, KindFolder(pkg.Kind));
            Directory.CreateDirectory(kindDir);
            var target = Path.Combine(kindDir, pkg.Name);
            if (Directory.Exists(target)) return "Folder " + pkg.Name + " sudah ada.";

            var zip = Path.Combine(Paths.Tmp, pkg.Name + ".zip");
            try
            {
                await DownloadAsync(pkg.Url, zip, progress, token);
                if (progress != null) progress.Report(100);
                Extract(zip, target);
                return null;
            }
            catch (OperationCanceledException)
            {
                SafeDelete(zip);
                SafeDeleteDir(target);
                return "Dibatalkan.";
            }
            catch (Exception ex)
            {
                SafeDeleteDir(target);
                return "Gagal: " + ex.Message;
            }
            finally { SafeDelete(zip); }
        }

        public static string KindFolder(BinKind kind)
        {
            switch (kind)
            {
                case BinKind.Php: return "php";
                case BinKind.Apache: return "apache";
                case BinKind.Nginx: return "nginx";
                default: return "mysql";
            }
        }

        /// <summary>
        /// Ekstrak dengan membuang satu lapis folder pembungkus bila arsipnya
        /// hanya berisi satu folder (Apache membungkus semuanya dalam "Apache24",
        /// MySQL dalam "mysql-8.0.39-winx64"). Tanpa ini jalur exe jadi berlapis
        /// dan pemindai tidak mengenalinya.
        /// </summary>
        static void Extract(string zipPath, string target)
        {
            var staging = target + ".tmp";
            SafeDeleteDir(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);

            var dirs = Directory.GetDirectories(staging);
            var files = Directory.GetFiles(staging);
            var source = (dirs.Length == 1 && files.Length == 0) ? dirs[0] : staging;
            Directory.Move(source, target);
            SafeDeleteDir(staging);
        }

        static async Task DownloadAsync(string url, string dest, IProgress<int> progress,
                                        CancellationToken token)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Phoron/" + AppInfo.Version;
            req.AllowAutoRedirect = true;
            req.Timeout = 60000;
            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            using (var input = resp.GetResponseStream())
            using (var output = File.Create(dest))
            {
                long total = resp.ContentLength;
                long done = 0;
                var buf = new byte[81920];
                int n;
                while ((n = await input.ReadAsync(buf, 0, buf.Length, token)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    await output.WriteAsync(buf, 0, n, token);
                    done += n;
                    if (progress != null && total > 0)
                        progress.Report((int)Math.Min(99, done * 100 / total));
                }
            }
        }

        static async Task<string> GetStringAsync(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Phoron/" + AppInfo.Version;
            req.Timeout = 30000;
            using (var resp = await req.GetResponseAsync())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return await reader.ReadToEndAsync();
        }

        static void SafeDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        static void SafeDeleteDir(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }
    }
}
