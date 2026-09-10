using System;
using System.Collections.Generic;

namespace Phoron.Core
{
    public static class AppInfo
    {
        public const string Name = "Phoron";
        public const string Version = "1.0.0";
    }

    public enum BinKind { Php, Apache, Nginx, MySql }

    public enum ServiceKind { Web, Db }

    public enum ServiceState { Berhenti, Menyalakan, Jalan, Mematikan, Gagal }

    /// <summary>Satu folder versi hasil pindaian, mis. php-8.3.12-Win32-vs16-x64.</summary>
    public class BinPackage
    {
        public BinKind Kind;
        /// <summary>Nama folder apa adanya - jadi kunci di berkas profil.</summary>
        public string Id;
        public string Path;
        /// <summary>Nomor versi hasil urai nama folder, mis. "8.3.12".</summary>
        public string Version = "";
        /// <summary>Toolset kompilator: VC11, VC15, VS16, ... Kosong bila tidak tertera.</summary>
        public string Compiler = "";
        /// <summary>x64 atau x86.</summary>
        public string Arch = "";
        /// <summary>PHP saja: true bila build Thread Safe (punya php*apache2_4.dll).</summary>
        public bool ThreadSafe;
        /// <summary>Exe utama (php.exe / httpd.exe / mysqld.exe / nginx.exe).</summary>
        public string MainExe;
        /// <summary>PHP saja: DLL modul Apache, kosong bila build NTS.</summary>
        public string ApacheModuleDll;
        /// <summary>Dari root bin mana paket ini ditemukan - dipakai UI untuk menandai bin pinjaman.</summary>
        public string SourceRoot = "";

        public Version Parsed
        {
            get
            {
                System.Version v;
                return System.Version.TryParse(Normalize(Version), out v) ? v : new Version(0, 0, 0);
            }
        }

        static string Normalize(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            var parts = v.Split('.');
            while (parts.Length < 3) { Array.Resize(ref parts, parts.Length + 1); parts[parts.Length - 1] = "0"; }
            return string.Join(".", parts);
        }

        /// <summary>Label pendek untuk daftar: "8.3.12 · VS16 · x64 · TS".</summary>
        public string Label
        {
            get
            {
                var bits = new List<string> { Version };
                if (!string.IsNullOrEmpty(Compiler)) bits.Add(Compiler.ToUpperInvariant());
                if (!string.IsNullOrEmpty(Arch)) bits.Add(Arch);
                if (Kind == BinKind.Php) bits.Add(ThreadSafe ? "TS" : "NTS");
                return string.Join(" · ", bits);
            }
        }

        public override string ToString() { return Id; }
    }

    /// <summary>
    /// Template setting yang bisa di-switch: kombinasi versi PHP + web server +
    /// database + port. Disimpan satu berkas .ini per profil di folder profiles\.
    /// </summary>
    public class Profile
    {
        /// <summary>
        /// Properti, bukan field, karena inilah yang ditampilkan ComboBox dan
        /// ListBox lewat DisplayMemberPath - binding WPF tidak bisa membaca field
        /// dan gagalnya SENYAP: daftarnya tampil kosong tanpa satu pun kesalahan.
        /// </summary>
        public string Name { get; set; }
        public string FileName;                 // nama berkas tanpa .ini
        public string PhpId = "";
        public string ApacheId = "";
        public string NginxId = "";
        public string MySqlId = "";
        /// <summary>"apache" atau "nginx".</summary>
        public string WebServer = "apache";
        public int HttpPort = 80;
        public int HttpsPort = 443;
        public int MySqlPort = 3306;
        public string DocumentRoot = "";        // kosong = pakai <root>\www
        public string SiteSuffix = "test";
        /// <summary>Ekstensi PHP yang dinyalakan profil ini (nama tanpa awalan php_).</summary>
        public List<string> PhpExtensions = new List<string>();
        /// <summary>Penimpaan php.ini: kunci -> nilai (memory_limit, dst).</summary>
        public Dictionary<string, string> PhpIniOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Notes = "";

        public Profile() { Name = "Baru"; }

        public override string ToString() { return Name; }

        public Profile Clone()
        {
            return new Profile
            {
                Name = Name,
                FileName = FileName,
                PhpId = PhpId,
                ApacheId = ApacheId,
                NginxId = NginxId,
                MySqlId = MySqlId,
                WebServer = WebServer,
                HttpPort = HttpPort,
                HttpsPort = HttpsPort,
                MySqlPort = MySqlPort,
                DocumentRoot = DocumentRoot,
                SiteSuffix = SiteSuffix,
                PhpExtensions = new List<string>(PhpExtensions),
                PhpIniOverrides = new Dictionary<string, string>(PhpIniOverrides, StringComparer.OrdinalIgnoreCase),
                Notes = Notes,
            };
        }
    }

    /// <summary>Satu situs (subfolder di www) yang dapat vhost otomatis.</summary>
    public class Site
    {
        public string Folder;
        public string Path;
        public string HostName;
        public bool InHosts;
        public bool HasVhost;
        /// <summary>Subfolder public/ atau web/ dipakai sebagai DocumentRoot bila ada (Laravel, Symfony).</summary>
        public string DocRoot;
    }
}
