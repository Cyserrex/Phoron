using System;
using System.Collections.Generic;
using System.IO;

namespace Phoron.Core
{
    /// <summary>
    /// Semua lokasi folder Phoron dihitung dari satu akar. Akar itu BUKAN folder
    /// exe: saat pengembangan exe ada di src\Phoron.App\bin\Debug\net48, sedangkan
    /// www/etc/data tetap di C:\Claude\Phoron. Menaiki folder induk sampai ketemu
    /// penanda membuat exe hasil build dan exe hasil rilis melihat data yang sama.
    /// </summary>
    public static class Paths
    {
        static string _root;

        /// <summary>Akar instalasi Phoron, mis. C:\Claude\Phoron.</summary>
        public static string Root
        {
            get { return _root ?? (_root = FindRoot()); }
            set { _root = value; }
        }

        static string FindRoot()
        {
            var env = Environment.GetEnvironmentVariable("PHORON_HOME");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                // Penanda: berkas phoron.ini, atau pasangan folder www+etc.
                if (File.Exists(Path.Combine(dir.FullName, "phoron.ini"))) return dir.FullName;
                if (Directory.Exists(Path.Combine(dir.FullName, "www"))
                    && Directory.Exists(Path.Combine(dir.FullName, "etc"))) return dir.FullName;
                dir = dir.Parent;
            }
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }

        public static string Bin { get { return Sub("bin"); } }
        public static string Www { get { return Sub("www"); } }
        public static string Etc { get { return Sub("etc"); } }
        public static string Data { get { return Sub("data"); } }
        public static string Tmp { get { return Sub("tmp"); } }
        public static string Logs { get { return Sub("logs"); } }
        public static string Profiles { get { return Sub("profiles"); } }
        public static string EtcApache { get { return Sub(Path.Combine("etc", "apache2")); } }
        public static string SitesEnabled { get { return Sub(Path.Combine("etc", "apache2", "sites-enabled")); } }
        public static string EtcNginx { get { return Sub(Path.Combine("etc", "nginx")); } }
        public static string EtcSsl { get { return Sub(Path.Combine("etc", "ssl")); } }
        public static string EtcMysql { get { return Sub(Path.Combine("etc", "mysql")); } }
        public static string SettingsFile { get { return Path.Combine(Root, "phoron.ini"); } }

        // Folder yang sudah dipastikan ada.
        //
        // KENAPA DISINGGAHKAN. Sub() dipanggil dari properti yang dibaca sangat
        // sering: Engine.Say menyentuh Paths.Logs untuk SETIAP baris log, dan
        // SslTool menyentuh Paths.EtcSsl beberapa kali tiap penyegaran. Tanpa
        // singgahan, sekadar MEMBACA sebuah jalur berarti satu panggilan
        // CreateDirectory yang menemui cakram.
        //
        // Diikat pada akarnya, sebab Paths.Root bisa diganti - harness uji
        // menggantinya di tiap sandbox - dan singgahan yang tidak ikut berganti
        // akan menunjuk folder milik pemasangan yang lain.
        static string _akarSinggahan;
        static readonly Dictionary<string, string> _sub =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Folder di bawah akar; dibuat kalau belum ada supaya pemanggil tidak perlu mengecek.</summary>
        static string Sub(string name)
        {
            lock (_sub)
            {
                if (!string.Equals(_akarSinggahan, Root, StringComparison.OrdinalIgnoreCase))
                {
                    _sub.Clear();
                    _akarSinggahan = Root;
                }
                string siap;
                if (_sub.TryGetValue(name, out siap)) return siap;

                var p = Path.Combine(Root, name);
                try { Directory.CreateDirectory(p); } catch { }
                // Hanya disinggahkan kalau foldernya memang jadi ada. Kalau
                // pembuatannya gagal - cakram penuh, hak akses, folder yang
                // dihapus orang - panggilan berikutnya harus mencoba lagi,
                // bukan mewarisi kegagalan itu selamanya.
                if (Directory.Exists(p)) _sub[name] = p;
                return p;
            }
        }

        /// <summary>Apache dan MySQL hanya menerima garis miring maju di berkas konfigurasinya.</summary>
        public static string Fwd(string path)
        {
            return path == null ? null : path.Replace('\\', '/');
        }

        static string _hostsFile;

        /// <summary>
        /// Berkas hosts Windows.
        ///
        /// Bisa disetel - sama seperti <see cref="Root"/> - supaya uji bisa
        /// mengarahkannya ke berkas sementara. Tanpa itu tidak satu pun perilaku
        /// penulisan hosts bisa dibuktikan tanpa menyentuh berkas sistem mesin
        /// yang sedang dipakai, dan justru bagian itulah yang paling berbahaya
        /// kalau salah.
        /// </summary>
        public static string HostsFile
        {
            get
            {
                return _hostsFile ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");
            }
            set { _hostsFile = value; }
        }
    }
}
