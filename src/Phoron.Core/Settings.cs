using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>Setelan global aplikasi, tersimpan di &lt;root&gt;\phoron.ini.</summary>
    public class Settings
    {
        /// <summary>
        /// Folder-folder yang dipindai untuk mencari versi. Default berisi bin milik
        /// Phoron sendiri DAN bin Laragon, supaya pemasangan baru langsung punya
        /// pilihan versi tanpa mengunduh apa pun.
        /// </summary>
        public List<string> BinRoots = new List<string>();
        public string ActiveProfile = "";
        public bool AutoStartServices;      // nyalakan Apache+MySQL saat aplikasi dibuka
        public bool MinimizeToTray = true;
        public bool AutoVhost = true;       // buat vhost otomatis untuk tiap folder di www
        public bool ManageHosts = true;     // sinkronkan berkas hosts (butuh admin)
        /// <summary>
        /// Tulis php.ini langsung ke folder PHP, bukan ke etc\php\&lt;versi&gt;\.
        /// Baku mati: folder PHP sering dipinjam dari Laragon, dan menimpanya
        /// berarti dua pengelola berebut satu berkas yang sama.
        /// </summary>
        public bool PhpIniKeFolderPhp;
        public string Terminal = "cmd";     // cmd | powershell | wt
        public string Editor = "";          // kosong = notepad

        public static Settings Load()
        {
            var s = new Settings();
            var ini = Ini.Load(Paths.SettingsFile);
            var roots = ini.Get("umum", "bin_roots", "");
            s.BinRoots = roots.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (s.BinRoots.Count == 0) s.BinRoots = DefaultBinRoots();
            s.ActiveProfile = ini.Get("umum", "profil_aktif", "");
            s.AutoStartServices = ini.GetBool("umum", "auto_start", false);
            s.MinimizeToTray = ini.GetBool("umum", "minimize_ke_tray", true);
            s.AutoVhost = ini.GetBool("umum", "auto_vhost", true);
            s.ManageHosts = ini.GetBool("umum", "kelola_hosts", true);
            s.PhpIniKeFolderPhp = ini.GetBool("umum", "php_ini_ke_folder_php", false);
            s.Terminal = ini.Get("umum", "terminal", "cmd");
            s.Editor = ini.Get("umum", "editor", "");
            return s;
        }

        public static List<string> DefaultBinRoots()
        {
            var list = new List<string> { Paths.Bin };
            // Laragon dicari di lokasi bakunya; kalau ada, versinya ikut terpakai.
            foreach (var drive in new[] { "C", "D", "E" })
            {
                var p = drive + @":\laragon\bin";
                if (Directory.Exists(p)) list.Add(p);
            }
            return list;
        }

        public void Save()
        {
            var ini = Ini.Load(Paths.SettingsFile);
            ini.Set("umum", "bin_roots", string.Join(";", BinRoots));
            ini.Set("umum", "profil_aktif", ActiveProfile ?? "");
            ini.Set("umum", "auto_start", AutoStartServices ? "1" : "0");
            ini.Set("umum", "minimize_ke_tray", MinimizeToTray ? "1" : "0");
            ini.Set("umum", "auto_vhost", AutoVhost ? "1" : "0");
            ini.Set("umum", "kelola_hosts", ManageHosts ? "1" : "0");
            ini.Set("umum", "php_ini_ke_folder_php", PhpIniKeFolderPhp ? "1" : "0");
            ini.Set("umum", "terminal", Terminal ?? "cmd");
            ini.Set("umum", "editor", Editor ?? "");
            ini.Save(Paths.SettingsFile,
                "Setelan Phoron. Berkas ini juga jadi penanda akar instalasi -\njangan dipindah dari folder Phoron.");
        }
    }
}
