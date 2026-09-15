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

        /// <summary>Dari mana setelan ini berasal - menentukan apakah aman ditimpa.</summary>
        public enum Sumber { Baru, Terbaca, Rusak, TidakTerbaca }

        public class HasilMuat
        {
            public Settings Setelan;
            public Sumber Asal;
            public List<string> Keluhan = new List<string>();
        }

        /// <summary>Asal setelan ini; dibaca Save() sebelum menimpa berkasnya.</summary>
        public Sumber AsalMuat = Sumber.Baru;

        public static Settings Load() { return Muat().Setelan; }

        /// <summary>
        /// Muat setelan sambil melaporkan keadaan berkasnya.
        ///
        /// Membedakan "berkasnya tidak ada" dari "berkasnya ada tapi cacat"
        /// adalah inti persoalannya. Keduanya dulu menghasilkan Settings yang
        /// tampak sah dengan seluruh nilai bawaan, dan penyimpanan berikutnya -
        /// yang terjadi sendiri saat mengecek pembaruan atau berganti profil -
        /// menuliskan bawaan itu kembali ke berkas. Sisa setelan yang sebenarnya
        /// masih selamat ikut terhapus, tanpa pernah ditawarkan kepada siapa pun.
        /// </summary>
        public static HasilMuat Muat()
        {
            var hasil = new HasilMuat();
            var baca = Ini.Baca(Paths.SettingsFile);
            hasil.Keluhan.AddRange(baca.Keluhan);
            hasil.Asal = !baca.Ada ? Sumber.Baru
                       : baca.GagalBaca ? Sumber.TidakTerbaca
                       : baca.Rusak ? Sumber.Rusak
                       : Sumber.Terbaca;

            var s = MuatDari(baca.Isi);
            s.AsalMuat = hasil.Asal;
            hasil.Setelan = s;
            return hasil;
        }

        static Settings MuatDari(Ini ini)
        {
            var s = new Settings();
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

        /// <summary>
        /// Save() adalah baca-ubah-tulis, dan dipanggil dari beberapa tempat -
        /// pengecekan pembaruan yang berjalan sendiri, pergantian profil,
        /// halaman Pengaturan. Tanpa kunci, dua di antaranya yang berpapasan
        /// membuat salah satu suntingan hilang.
        /// </summary>
        static readonly object _kunciSimpan = new object();

        /// <summary>Terisi bila berkas yang cacat terpaksa dikarantina saat menyimpan.</summary>
        public static string KeluhanTerakhir = "";

        public void Save()
        {
            lock (_kunciSimpan) { SimpanInti(); }
        }

        void SimpanInti()
        {
            // Berkas yang cacat tidak ditimpa begitu saja: disingkirkan dulu,
            // supaya sisa setelan di dalamnya masih bisa dilihat orang.
            //
            // Menolak menulis sama sekali justru lebih buruk - CekTerakhir tidak
            // akan pernah tersimpan, jadi Phoron menanyai GitHub tiap kali start
            // selamanya sambil mengomel tentang berkas yang tidak bisa dilewati
            // siapa pun.
            if (AsalMuat == Sumber.Rusak || AsalMuat == Sumber.TidakTerbaca)
            {
                var salinan = AtomicFile.Karantina(Paths.SettingsFile, "rusak");
                KeluhanTerakhir = salinan != null
                    ? "phoron.ini tidak terbaca utuh; salinannya disimpan sebagai "
                      + Path.GetFileName(salinan) + " sebelum ditulis ulang."
                    : "phoron.ini tidak terbaca utuh dan tidak bisa dikarantina; "
                      + "setelan ditulis ulang dari awal.";
                AsalMuat = Sumber.Baru;   // sekali saja, bukan tiap penyimpanan
            }

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
            // Pernah ada di sini sampai 1.20.x. Dibuang AKTIF, bukan sekadar
            // berhenti ditulis: Save() memuat ulang berkas yang ada lalu
            // menimpanya, jadi tanpa baris ini token lama tetap tertinggal di
            // phoron.ini milik orang yang pernah mengisinya.
            ini.Remove("umum", "token_github");
            ini.Set("umum", "cek_terakhir", CekTerakhir == DateTime.MinValue
                ? "" : CekTerakhir.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("umum", "terminal", Terminal ?? "cmd");
            ini.Set("umum", "editor", Editor ?? "");
            ini.Save(Paths.SettingsFile,
                "Setelan Phoron. Berkas ini juga jadi penanda akar instalasi -\njangan dipindah dari folder Phoron.");
        }
    }
}
