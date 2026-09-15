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
        /// <summary>
        /// Catat log rinci: log akses Apache dan seluruh keluaran layanan.
        /// Baku mati. mysqld sendiri mencetak ratusan baris tiap kali menyala,
        /// dan log akses tumbuh terus sepanjang hari - keduanya jarang dibaca
        /// saat pengembangan. Log GALAT (Apache, MySQL, PHP) tetap menyala:
        /// itulah yang dibutuhkan ketika ada yang rusak.
        /// </summary>
        public bool LogRinci;
        /// <summary>
        /// Layani beranda Phoron di http://localhost/ (akar), bukan hanya di
        /// /phoron/. Baku menyala - itulah yang orang harapkan dari perkakas
        /// semacam ini. Hanya alamat akar PERSIS yang dialihkan; subfolder
        /// seperti /simpdam/ tidak tersentuh, dan index.php milik folder proyek
        /// tetap bisa dibuka di /index.php.
        /// </summary>
        public bool BerandaDiAkar = true;
        /// <summary>"sistem" (ikut Windows), "terang", atau "gelap".</summary>
        public string Tema = "sistem";
        /// <summary>Kode bahasa antarmuka: id, en, jv, bjn.</summary>
        public string Bahasa = Lang.Indonesia;
        /// <summary>Cek rilis baru di GitHub saat aplikasi dibuka. Baku menyala.</summary>
        public bool CekPembaruan = true;
        /// <summary>
        /// Kapan terakhir kali GitHub ditanya. API tanpa token dibatasi 60
        /// permintaan per jam per alamat IP; menanyakannya tiap kali jendela
        /// dibuka akan menghabiskan jatah itu tanpa guna.
        /// </summary>
        public DateTime CekTerakhir = DateTime.MinValue;
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
            s.LogRinci = ini.GetBool("umum", "log_rinci", false);
            s.BerandaDiAkar = ini.GetBool("umum", "beranda_di_akar", true);
            s.Tema = ini.Get("umum", "tema", "sistem");
            s.Bahasa = ini.Get("umum", "bahasa", Lang.Indonesia);
            if (!Lang.Sah(s.Bahasa)) s.Bahasa = Lang.Indonesia;
            s.CekPembaruan = ini.GetBool("umum", "cek_pembaruan", true);
            DateTime kapan;
            s.CekTerakhir = DateTime.TryParse(ini.Get("umum", "cek_terakhir", ""),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out kapan)
                ? kapan : DateTime.MinValue;
            s.Terminal = ini.Get("umum", "terminal", "cmd");
            s.Editor = ini.Get("umum", "editor", "");
            return s;
        }

        public static List<string> DefaultBinRoots()
        {
            // Folder bin Phoron sendiri selalu pertama - itulah yang menang
            // saat ada nama folder kembar di beberapa tempat.
            var list = new List<string> { Paths.Bin };
            // Pengelola lain yang benar-benar terpasang di komputer INI ikut
            // dipakai, jadi Phoron langsung punya daftar versi tanpa disuruh.
            // Dulu hanya Laragon yang dicari; XAMPP dan WAMP terlewat walau
            // pemindainya sudah mengenali tata letak keduanya.
            list.AddRange(Deteksi.FolderBinTerpasang());
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
            ini.Set("umum", "log_rinci", LogRinci ? "1" : "0");
            ini.Set("umum", "beranda_di_akar", BerandaDiAkar ? "1" : "0");
            ini.Set("umum", "tema", Tema ?? "sistem");
            ini.Set("umum", "bahasa", Bahasa ?? Lang.Indonesia);
            ini.Set("umum", "cek_pembaruan", CekPembaruan ? "1" : "0");
            ini.Set("umum", "cek_terakhir", CekTerakhir == DateTime.MinValue
                ? "" : CekTerakhir.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("umum", "terminal", Terminal ?? "cmd");
            ini.Set("umum", "editor", Editor ?? "");
            ini.Save(Paths.SettingsFile,
                "Setelan Phoron. Berkas ini juga jadi penanda akar instalasi -\njangan dipindah dari folder Phoron.");
        }
    }
}
