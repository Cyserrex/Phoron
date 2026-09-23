using System;
using System.Collections.Generic;

namespace Phoron.Core
{
    public static class AppInfo
    {
        public const string Name = "Phoron";
        public const string Version = "1.35.0";

        // set_version.ps1 hanya menyentuh baris Version di atas, jadi keterangan
        // di bawah ini aman dari penulisan ulang saat menaikkan nomor rilis.
        public const string Pemilik = "Cyserrex";
        public const string Repo = "https://github.com/Cyserrex/Phoron";
        public const string HalamanRilis = "https://github.com/Cyserrex/Phoron/releases";
        public const string TahunMulai = "2026";

        /// <summary>Baris hak cipta. Tahunnya jadi rentang begitu tahun berjalan melewati tahun rilis pertama.</summary>
        public static string HakCipta
        {
            get
            {
                var kini = DateTime.Now.Year.ToString();
                var tahun = kini == TahunMulai ? TahunMulai : TahunMulai + "-" + kini;
                return "Hak cipta © " + tahun + " " + Pemilik;
            }
        }
    }

    /// <summary>Satu baris riwayat log berikut waktunya.</summary>
    public class BarisLog
    {
        public DateTime Waktu;
        public string Teks = "";
    }

    public enum BinKind { Php, Apache, Nginx, MySql, Node }

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
        /// <summary>
        /// Folder-folder yang dipindai untuk mencari situs. Kosong = pakai
        /// &lt;root&gt;\www. Yang pertama jadi akar utama: itulah yang dilayani
        /// http://localhost dan yang dibuka tombol "Buka www".
        /// </summary>
        public List<string> ProjectRoots = new List<string>();
        /// <summary>
        /// Id web server yang benar-benar dipakai profil ini, sesuai pilihan
        /// Apache atau Nginx-nya.
        /// </summary>
        /// <summary>
        /// Penanda tampilan: profil inikah yang sedang aktif.
        ///
        /// Tidak pernah ikut tersimpan ke berkas profil - ProfileStore menulis
        /// kunci yang disebutnya satu per satu - dan tidak dibaca siapa pun
        /// selain daftar di halaman Profil, yang menyetelnya sendiri tiap kali
        /// daftar itu diisi ulang. Profil tidak tahu apa-apa soal Engine, jadi
        /// ia tidak bisa menyimpulkannya sendiri.
        /// </summary>
        /// PROPERTI, bukan medan: WPF hanya mengikat ke properti, dan
        /// pengikatan ke medan gagal TANPA SUARA - centangnya lalu memakai
        /// nilai bawaan Visibility, yaitu tampak, sehingga muncul di semua
        /// baris sekaligus. Itu persis yang terjadi pada percobaan pertama.
        public bool Aktif { get; set; }

        public string WebId { get { return WebServer == "nginx" ? NginxId : ApacheId; } }

        /// <summary>
        /// Apakah profil ini memang memakai layanan tersebut.
        ///
        /// Kosong berarti "(tidak dipakai)", dan itu pilihan yang disengaja -
        /// ada entrinya di ComboBox halaman Profil. Membedakannya dari "disebut
        /// tapi tidak ketemu" penting di banyak tempat: yang pertama tidak
        /// boleh dilaporkan sebagai kegagalan, tidak perlu diperiksa portnya,
        /// dan tidak pantas ditandai merah di layar.
        /// </summary>
        public bool PakaiWeb { get { return !string.IsNullOrEmpty(WebId); } }
        public bool PakaiMySql { get { return !string.IsNullOrEmpty(MySqlId); } }

        public string SiteSuffix = "test";
        /// <summary>Ekstensi PHP yang dinyalakan profil ini (nama tanpa awalan php_).</summary>
        public List<string> PhpExtensions = new List<string>();
        /// <summary>Penimpaan php.ini: kunci -> nilai (memory_limit, dst).</summary>
        public Dictionary<string, string> PhpIniOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Versi PHP khusus untuk situs tertentu: jalur folder situs -> Id paket
        /// PHP. Situs yang tidak disebut di sini ikut PHP profil.
        ///
        /// Kuncinya jalur lengkap, bukan nama folder: dua folder proyek boleh
        /// berisi situs bernama sama, dan keduanya bisa butuh versi berbeda.
        /// Situs yang disebut di sini dilayani php-cgi versinya lewat FastCGI,
        /// berdampingan dengan PHP profil - CodeIgniter 2 di PHP 5.6 dan Laravel
        /// di PHP 8.3, dalam satu Apache yang sama, tanpa berganti profil.
        /// </summary>
        public Dictionary<string, string> PhpPerSitus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                ProjectRoots = new List<string>(ProjectRoots),
                SiteSuffix = SiteSuffix,
                PhpExtensions = new List<string>(PhpExtensions),
                PhpIniOverrides = new Dictionary<string, string>(PhpIniOverrides, StringComparer.OrdinalIgnoreCase),
                PhpPerSitus = new Dictionary<string, string>(PhpPerSitus, StringComparer.OrdinalIgnoreCase),
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
        /// <summary>Folder proyek asal situs ini - berguna saat ada lebih dari satu.</summary>
        public string Root;
        /// <summary>Subfolder public/ atau web/ dipakai sebagai DocumentRoot bila ada (Laravel, Symfony).</summary>
        public string DocRoot;
    }
}
