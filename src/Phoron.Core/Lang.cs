using System;
using System.Collections.Generic;

namespace Phoron.Core
{
    /// <summary>
    /// Terjemahan antarmuka.
    ///
    /// Kuncinya adalah teks Indonesia itu sendiri, bukan kode seperti
    /// "nav.beranda". Dua alasan: bahasa asal aplikasi ini memang Indonesia,
    /// jadi tidak perlu kamus sama sekali untuk bahasa itu; dan teks yang belum
    /// diterjemahkan jatuh kembali ke Indonesia yang benar, bukan ke kode mentah
    /// yang tidak berarti apa-apa bagi pengguna.
    ///
    /// Bahasa Banjar dan Jawa sengaja tidak memaksakan padanan untuk istilah
    /// teknis yang memang dipakai apa adanya sehari-hari (port, profil, log).
    /// Menerjemahkannya justru membuat layar lebih sulit dibaca, bukan lebih
    /// ramah.
    /// </summary>
    public static class Lang
    {
        public const string Indonesia = "id";
        public const string Inggris = "en";
        public const string Jawa = "jv";
        public const string Banjar = "bjn";

        /// <summary>Kode bahasa yang sedang dipakai.</summary>
        public static string Kode = Indonesia;

        /// <summary>Diangkat setiap kali bahasa berganti, supaya layar bisa menggambar ulang.</summary>
        public static event Action Berubah;

        public static void Pakai(string kode)
        {
            var baru = Sah(kode) ? kode : Indonesia;
            if (baru == Kode) return;
            Kode = baru;
            var h = Berubah;
            if (h != null) h();
        }

        public static bool Sah(string kode)
        {
            return kode == Indonesia || kode == Inggris || kode == Jawa || kode == Banjar;
        }

        public static string NamaBahasa(string kode)
        {
            switch (kode)
            {
                case Inggris: return "English";
                case Jawa: return "Basa Jawa";
                case Banjar: return "Bahasa Banjar";
                default: return "Bahasa Indonesia";
            }
        }

        public static string[] Semua { get { return new[] { Indonesia, Inggris, Jawa, Banjar }; } }

        /// <summary>Terjemahkan satu teks. Yang tidak ada padanannya tetap tampil dalam bahasa Indonesia.</summary>
        public static string T(string teks)
        {
            if (string.IsNullOrEmpty(teks) || Kode == Indonesia) return teks;
            Dictionary<string, string> kamus;
            if (!Kamus.TryGetValue(Kode, out kamus)) return teks;
            string hasil;
            return kamus.TryGetValue(teks, out hasil) ? hasil : teks;
        }

        /// <summary>Terjemahkan lalu sisipkan nilai, mis. T("Port {0} bebas.", 80).</summary>
        public static string T(string teks, params object[] isi)
        {
            return string.Format(T(teks), isi);
        }

        static readonly Dictionary<string, Dictionary<string, string>> Kamus =
            new Dictionary<string, Dictionary<string, string>>
            {
                { Inggris, Inggris_() },
                { Jawa, Jawa_() },
                { Banjar, Banjar_() },
            };

        static Dictionary<string, string> Inggris_()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Teks layar yang tersisa
                { "Menyambung...",
                  "Connecting..." },
                { "Batalkan",
                  "Cancel" },
                { "Apache berhenti",
                  "Apache stopped" },
                { "MySQL berhenti",
                  "MySQL stopped" },
                { "Centang ekstensi yang dipakai profil aktif.",
                  "Tick the extensions the active profile uses." },
                { "Cari ekstensi...",
                  "Search extensions..." },
                { "Satu folder per baris; boleh lebih dari satu. Yang paling atas jadi akar utama (dilayani http://localhost). Kosongkan untuk memakai www bawaan.",
                  "One folder per line; more than one is fine. The topmost one becomes the main root (served at http://localhost). Leave it empty to use the built-in www." },
                { "Phoron mulai langsung mengecil ke baki sistem, tanpa memunculkan jendela.",
                  "Phoron starts straight into the system tray, without showing a window." },
                { "Hanya alamat akar yang dialihkan. Subfolder seperti /simpdam/ tidak tersentuh, dan index.php milik folder proyek tetap bisa dibuka di /index.php. Matikan kalau akar folder proyek Anda memang aplikasi sendiri.",
                  "Only the root address is redirected. Subfolders such as /simpdam/ are untouched, and the project folder's own index.php is still reachable at /index.php. Turn this off if your project root really is an application of its own." },
                { "Baku mati agar Phoron ringan: log akses Apache dan seluruh keluaran layanan tidak ditulis (mysqld sendiri mencetak ratusan baris tiap kali menyala). Log GALAT Apache, MySQL, dan PHP tetap menyala — itulah yang menjelaskan kalau ada yang rusak.",
                  "Off by default to keep Phoron light: Apache access logs and all service output are not written (mysqld alone prints hundreds of lines on every start). Apache, MySQL and PHP ERROR logs stay on — those are what explain a breakage." },
                { @"Baku: php.ini ditulis ke etc\php\<versi>\ dan folder PHP tidak disentuh. Nyalakan kalau Anda ingin php.exe dari editor atau Composer di luar Phoron ikut memakai setelan yang sama. Hati-hati bila versi PHP-nya dipinjam dari Laragon - berkasnya dipakai bersama. php.ini asli dicadangkan sekali ke php.ini.sebelum-phoron.",
                  @"Default: php.ini is written to etc\php\<version>\ and the PHP folder is left alone. Turn this on if you want php.exe from your editor or from Composer outside Phoron to use the same settings. Be careful when the PHP version is borrowed from Laragon - the file is shared. The original php.ini is backed up once to php.ini.sebelum-phoron." },
                { "Satu folder per baris. Folder bin Laragon boleh ikut - Phoron tidak pernah menulis ke dalamnya.",
                  "One folder per line. Laragon's bin folder may be included - Phoron never writes into it." },
                { "Pengecekan dilewati kalau baru dilakukan dalam 6 jam terakhir - API GitHub tanpa token dibatasi 60 permintaan per jam.",
                  "The check is skipped if one already ran within the last 6 hours - GitHub's API without a token is capped at 60 requests per hour." },
                { "Tiap subfolder di www otomatis dapat alamat sendiri.",
                  "Every subfolder in www automatically gets its own address." },
                { "Semua folder versi yang ditemukan di folder bin yang terdaftar. Folder bin Laragon ikut dipindai bila ada.",
                  "Every version folder found in the registered bin folders. Laragon's bin folder is scanned too when it is there." },
                { @"Katalog unduhan (etc\catalog.ini)",
                  @"Download catalogue (etc\catalog.ini)" },
                { "Katalog lokal",
                  "Local catalogue" },
                { "Paket diunduh ke folder bin milik Phoron, tidak menyentuh folder Laragon.",
                  "Packages are downloaded into Phoron's own bin folder, never touching Laragon's." },
                // Navigasi dan status
                { "Beranda", "Home" },
                { "Profil", "Profiles" },
                { "Versi", "Versions" },
                { "Situs", "Sites" },
                { "Node / TS", "Node / TS" },
                { "Ekstensi PHP", "PHP extensions" },
                { "Log", "Logs" },
                { "Pengaturan", "Settings" },
                { "Nyalakan semua", "Start all" },
                { "Matikan semua", "Stop all" },
                { "Keluar", "Quit" },
                { "(belum ada profil)", "(no profile yet)" },
                { "berhenti", "stopped" },
                { "jalan", "running" },
                { "menyalakan", "starting" },
                { "mematikan", "stopping" },
                { "gagal", "failed" },

                // Beranda
                { "Pilih profil, lalu nyalakan. Ganti profil berarti ganti versi PHP, Apache, dan MySQL sekaligus.",
                  "Pick a profile, then start. Switching profiles changes PHP, Apache and MySQL together." },
                { "Profil aktif", "Active profile" },
                { "Switch & Jalankan", "Switch & Run" },
                { "Switch", "Switch" },
                { "Perhatian", "Heads up" },
                { "Pintasan", "Shortcuts" },
                { "Aktivitas", "Activity" },
                { "Buka www", "Open www" },
                { "Buka localhost", "Open localhost" },
                { "Beranda Phoron", "Phoron home" },
                { "Terminal", "Terminal" },
                { "Uji konfigurasi Apache", "Test Apache config" },
                { "Buat sertifikat SSL", "Create SSL certificate" },
                { "Percayai sertifikat SSL", "Trust SSL certificate" },
                { "Buat ulang sertifikat SSL", "Recreate SSL certificate" },
                { "Folder etc", "etc folder" },
                { "Unduh pembaruan", "Download update" },
                { "Jalankan ulang sebagai Administrator", "Restart as Administrator" },
                { "Hentikan proses yang tertinggal", "Stop leftover processes" },
                { "belum dipilih di profil", "not set in this profile" },

                // Profil
                { "Satu profil = satu kombinasi versi + port. Simpan sebanyak yang perlu, lalu tinggal switch.",
                  "One profile = one set of versions and ports. Save as many as you need, then just switch." },
                { "Profil baru", "New profile" },
                { "Duplikat", "Duplicate" },
                { "Hapus", "Delete" },
                { "Nama profil", "Profile name" },
                { "Web server", "Web server" },
                { "Versi PHP", "PHP version" },
                { "Versi Apache", "Apache version" },
                { "Versi Nginx", "Nginx version" },
                { "Versi MySQL / MariaDB", "MySQL / MariaDB version" },
                { "Port HTTP", "HTTP port" },
                { "Port HTTPS", "HTTPS port" },
                { "Port MySQL", "MySQL port" },
                { "Folder proyek", "Project folders" },
                { "Tambah folder...", "Add folder..." },
                { "Akhiran nama situs", "Site name suffix" },
                { "Catatan", "Notes" },
                { "Simpan", "Save" },
                { "Simpan lalu switch ke profil ini", "Save and switch to this profile" },

                // Versi
                { "Versi terpasang", "Installed versions" },
                { "Jenis", "Kind" },
                { "Toolset", "Toolset" },
                { "Arsitektur", "Architecture" },
                { "Folder", "Folder" },
                { "Sumber", "Source" },
                { "Pindai ulang", "Rescan" },
                { "Tambah folder bin", "Add bin folder" },
                { "Buka folder versi", "Open version folder" },
                { "Pasang versi baru", "Install a new version" },
                { "Unduh & pasang", "Download & install" },

                // Situs
                { "Buat situs", "Create site" },
                { "Alamat", "Address" },
                { "Document root", "Document root" },
                { "hosts", "hosts" },
                { "vhost", "vhost" },
                { "Buka di browser", "Open in browser" },
                { "Buka folder", "Open folder" },
                { "Terminal di sini", "Terminal here" },
                { "Segarkan & sinkronkan", "Refresh & sync" },
                { "Buka berkas hosts", "Open hosts file" },

                // Node
                { "Node / TypeScript", "Node / TypeScript" },
                { "Jalankan Next.js, Astro, Vite, dan proyek Node lain dari folder mana pun.",
                  "Run Next.js, Astro, Vite and other Node projects from any folder." },
                { "Proyek", "Project" },
                { "Kerangka", "Framework" },
                { "Perintah", "Command" },
                { "Status", "Status" },
                { "Skrip", "Script" },
                { "Node", "Node" },
                { "Jalankan", "Run" },
                { "Hentikan", "Stop" },
                { "Tambah proyek...", "Add project..." },
                { "Buka alamat", "Open address" },
                { "Hapus dari daftar", "Remove from list" },
                { "Keluaran", "Output" },

                // Ekstensi
                { "Setelan php.ini yang sering diubah", "Commonly changed php.ini settings" },
                { "Simpan ke profil", "Save to profile" },
                { "Ambil dari php.ini asli", "Take from original php.ini" },
                { "Buka php.ini hasil", "Open generated php.ini" },
                { "Uji: php -m", "Test: php -m" },
                { "short_open_tag (perlu untuk banyak proyek lama)",
                  "short_open_tag (needed by many legacy projects)" },

                // Log
                { "Semua log Apache, MySQL, PHP, dan Phoron ada di folder logs.",
                  "All Apache, MySQL, PHP and Phoron logs live in the logs folder." },
                { "Muat ulang", "Reload" },
                { "Kosongkan", "Clear" },
                { "Ikuti", "Follow" },

                // Pengaturan
                { "Setiap perubahan langsung tersimpan ke phoron.ini - tidak ada tombol simpan.",
                  "Every change is saved to phoron.ini immediately - there is no save button." },
                { "Tampilan", "Appearance" },
                { "Tema", "Theme" },
                { "Bahasa", "Language" },
                { "Ikut Windows", "Follow Windows" },
                { "Terang", "Light" },
                { "Gelap", "Dark" },
                { "Jalankan Phoron saat Windows dinyalakan", "Start Phoron when Windows starts" },
                { "Nyalakan layanan otomatis saat Phoron dibuka",
                  "Start services automatically when Phoron opens" },
                { "Tombol tutup mengecilkan ke baki sistem, bukan keluar",
                  "Close button minimises to the system tray instead of quitting" },
                { "Buat vhost otomatis untuk tiap folder di www",
                  "Create a vhost automatically for every folder in www" },
                { "Sinkronkan berkas hosts Windows (butuh Administrator)",
                  "Sync the Windows hosts file (needs Administrator)" },
                { "Tampilkan beranda Phoron di http://localhost/",
                  "Show the Phoron home page at http://localhost/" },
                { "Catat log rinci", "Write verbose logs" },
                { "Tulis php.ini ke dalam folder PHP", "Write php.ini into the PHP folder" },
                { "Folder bin yang dipindai", "Scanned bin folders" },
                { "Status hak akses", "Privilege status" },
                { "Folder Phoron", "Phoron folder" },
                { "Buka folder instalasi", "Open install folder" },
                { "Buka phoron.ini", "Open phoron.ini" },
                { "Buang blok hosts milik Phoron", "Remove Phoron's hosts block" },
                { "Pembaruan", "Updates" },
                { "Cek rilis baru di GitHub saat Phoron dibuka",
                  "Check GitHub for new releases when Phoron opens" },
                { "Cek pembaruan sekarang", "Check for updates now" },
                { "Buka halaman rilis", "Open releases page" },
            };
        }

        static Dictionary<string, string> Jawa_()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Teks layar yang tersisa
                { "Menyambung...",
                  "Nyambung..." },
                { "Batalkan",
                  "Batalake" },
                { "Apache berhenti",
                  "Apache mandheg" },
                { "MySQL berhenti",
                  "MySQL mandheg" },
                { "Centang ekstensi yang dipakai profil aktif.",
                  "Centhangi ekstensi sing dianggo profil aktif." },
                { "Cari ekstensi...",
                  "Golek ekstensi..." },
                { "Satu folder per baris; boleh lebih dari satu. Yang paling atas jadi akar utama (dilayani http://localhost). Kosongkan untuk memakai www bawaan.",
                  "Siji folder saben baris; oleh luwih saka siji. Sing paling ndhuwur dadi oyod utama (dilayani http://localhost). Kosongake kanggo nganggo www bawaan." },
                { "Phoron mulai langsung mengecil ke baki sistem, tanpa memunculkan jendela.",
                  "Phoron mlaku langsung ngalih menyang baki sistem, tanpa mbukak jendhela." },
                { "Hanya alamat akar yang dialihkan. Subfolder seperti /simpdam/ tidak tersentuh, dan index.php milik folder proyek tetap bisa dibuka di /index.php. Matikan kalau akar folder proyek Anda memang aplikasi sendiri.",
                  "Mung alamat oyod sing dialihake. Subfolder kaya /simpdam/ ora diganggu, lan index.php duweke folder proyek isih bisa dibukak ing /index.php. Patenana yen oyod folder proyek sampeyan pancen aplikasi dhewe." },
                { "Baku mati agar Phoron ringan: log akses Apache dan seluruh keluaran layanan tidak ditulis (mysqld sendiri mencetak ratusan baris tiap kali menyala). Log GALAT Apache, MySQL, dan PHP tetap menyala — itulah yang menjelaskan kalau ada yang rusak.",
                  "Bawaane mati supaya Phoron entheng: log akses Apache lan kabeh metune layanan ora ditulis (mysqld dhewe nyithak atusan baris saben urip). Log GALAT Apache, MySQL, lan PHP tetep urip — kuwi sing nerangake yen ana sing rusak." },
                { @"Baku: php.ini ditulis ke etc\php\<versi>\ dan folder PHP tidak disentuh. Nyalakan kalau Anda ingin php.exe dari editor atau Composer di luar Phoron ikut memakai setelan yang sama. Hati-hati bila versi PHP-nya dipinjam dari Laragon - berkasnya dipakai bersama. php.ini asli dicadangkan sekali ke php.ini.sebelum-phoron.",
                  @"Bawaane: php.ini ditulis ing etc\php\<versi>\ lan folder PHP ora disenggol. Uripna yen sampeyan pengin php.exe saka editor utawa Composer ing njaba Phoron melu nganggo setelan sing padha. Ati-ati yen versi PHP-ne nyilih saka Laragon - berkase dianggo bareng. php.ini asli dicadhangake sepisan menyang php.ini.sebelum-phoron." },
                { "Satu folder per baris. Folder bin Laragon boleh ikut - Phoron tidak pernah menulis ke dalamnya.",
                  "Siji folder saben baris. Folder bin Laragon oleh melu - Phoron ora tau nulis ing njerone." },
                { "Pengecekan dilewati kalau baru dilakukan dalam 6 jam terakhir - API GitHub tanpa token dibatasi 60 permintaan per jam.",
                  "Pamriksan dilewati yen lagi wae dilakoni sajrone 6 jam pungkasan - API GitHub tanpa token diwatesi 60 panjaluk saben jam." },
                { "Tiap subfolder di www otomatis dapat alamat sendiri.",
                  "Saben subfolder ing www otomatis oleh alamat dhewe." },
                { "Semua folder versi yang ditemukan di folder bin yang terdaftar. Folder bin Laragon ikut dipindai bila ada.",
                  "Kabeh folder versi sing ketemu ing folder bin sing kadaftar. Folder bin Laragon melu dipindhai yen ana." },
                { @"Katalog unduhan (etc\catalog.ini)",
                  @"Katalog undhuhan (etc\catalog.ini)" },
                { "Katalog lokal",
                  "Katalog lokal" },
                { "Paket diunduh ke folder bin milik Phoron, tidak menyentuh folder Laragon.",
                  "Paket diundhuh menyang folder bin duweke Phoron, ora nyenggol folder Laragon." },
                { "Beranda", "Ngarep" },
                { "Profil", "Profil" },
                { "Versi", "Versi" },
                { "Situs", "Situs" },
                { "Ekstensi PHP", "Ekstensi PHP" },
                { "Pengaturan", "Setelan" },
                { "Nyalakan semua", "Uripake kabeh" },
                { "Matikan semua", "Patenana kabeh" },
                { "Keluar", "Metu" },
                { "(belum ada profil)", "(durung ana profil)" },
                { "berhenti", "mandheg" },
                { "jalan", "mlaku" },
                { "menyalakan", "nguripake" },
                { "mematikan", "matheni" },
                { "gagal", "gagal" },

                { "Pilih profil, lalu nyalakan. Ganti profil berarti ganti versi PHP, Apache, dan MySQL sekaligus.",
                  "Pilih profil, banjur uripake. Ganti profil tegese ganti versi PHP, Apache, lan MySQL sepisanan." },
                { "Profil aktif", "Profil sing aktif" },
                { "Switch & Jalankan", "Ganti & Uripake" },
                { "Switch", "Ganti" },
                { "Perhatian", "Gatekna" },
                { "Pintasan", "Dalan cepet" },
                { "Aktivitas", "Kagiyatan" },
                { "Buka www", "Bukak www" },
                { "Buka localhost", "Bukak localhost" },
                { "Beranda Phoron", "Ngarep Phoron" },
                { "Uji konfigurasi Apache", "Coba setelan Apache" },
                { "Buat sertifikat SSL", "Gawe sertifikat SSL" },
                { "Percayai sertifikat SSL", "Percaya sertifikat SSL" },
                { "Buat ulang sertifikat SSL", "Gawe maneh sertifikat SSL" },
                { "Folder etc", "Folder etc" },
                { "Unduh pembaruan", "Undhuh nganyari" },
                { "Jalankan ulang sebagai Administrator", "Mbaleni minangka Administrator" },
                { "Hentikan proses yang tertinggal", "Patenana proses sing keri" },
                { "belum dipilih di profil", "durung dipilih ing profil" },

                { "Satu profil = satu kombinasi versi + port. Simpan sebanyak yang perlu, lalu tinggal switch.",
                  "Siji profil = siji kombinasi versi lan port. Simpen samubarang sing perlu, banjur kari ganti." },
                { "Profil baru", "Profil anyar" },
                { "Duplikat", "Tiron" },
                { "Hapus", "Busak" },
                { "Nama profil", "Jeneng profil" },
                { "Versi PHP", "Versi PHP" },
                { "Versi Apache", "Versi Apache" },
                { "Versi MySQL / MariaDB", "Versi MySQL / MariaDB" },
                { "Folder proyek", "Folder proyek" },
                { "Tambah folder...", "Tambah folder..." },
                { "Akhiran nama situs", "Wuntat jeneng situs" },
                { "Catatan", "Cathetan" },
                { "Simpan", "Simpen" },
                { "Simpan lalu switch ke profil ini", "Simpen banjur ganti menyang profil iki" },

                { "Versi terpasang", "Versi sing kepasang" },
                { "Jenis", "Jinis" },
                { "Folder", "Folder" },
                { "Sumber", "Sumber" },
                { "Pindai ulang", "Sawang maneh" },
                { "Tambah folder bin", "Tambah folder bin" },
                { "Buka folder versi", "Bukak folder versi" },
                { "Pasang versi baru", "Pasang versi anyar" },
                { "Unduh & pasang", "Undhuh & pasang" },

                { "Buat situs", "Gawe situs" },
                { "Alamat", "Alamat" },
                { "Buka di browser", "Bukak ing browser" },
                { "Buka folder", "Bukak folder" },
                { "Terminal di sini", "Terminal ing kene" },
                { "Segarkan & sinkronkan", "Seger & selarasake" },
                { "Buka berkas hosts", "Bukak berkas hosts" },

                { "Jalankan Next.js, Astro, Vite, dan proyek Node lain dari folder mana pun.",
                  "Uripake Next.js, Astro, Vite, lan proyek Node liyane saka folder ngendi wae." },
                { "Proyek", "Proyek" },
                { "Perintah", "Printah" },
                { "Status", "Kahanan" },
                { "Skrip", "Skrip" },
                { "Jalankan", "Uripake" },
                { "Hentikan", "Patenana" },
                { "Tambah proyek...", "Tambah proyek..." },
                { "Buka alamat", "Bukak alamat" },
                { "Hapus dari daftar", "Busak saka dhaptar" },
                { "Keluaran", "Wetu" },

                { "Setelan php.ini yang sering diubah", "Setelan php.ini sing kerep diowahi" },
                { "Simpan ke profil", "Simpen menyang profil" },
                { "Ambil dari php.ini asli", "Jupuk saka php.ini asli" },
                { "Buka php.ini hasil", "Bukak php.ini asil" },

                { "Semua log Apache, MySQL, PHP, dan Phoron ada di folder logs.",
                  "Kabeh log Apache, MySQL, PHP, lan Phoron ana ing folder logs." },
                { "Muat ulang", "Muat maneh" },
                { "Kosongkan", "Kosongake" },
                { "Ikuti", "Tutake" },

                { "Setiap perubahan langsung tersimpan ke phoron.ini - tidak ada tombol simpan.",
                  "Saben owahan langsung kesimpen ing phoron.ini - ora ana tombol simpen." },
                { "Tampilan", "Tampilan" },
                { "Tema", "Tema" },
                { "Bahasa", "Basa" },
                { "Ikut Windows", "Melu Windows" },
                { "Terang", "Padhang" },
                { "Gelap", "Peteng" },
                { "Jalankan Phoron saat Windows dinyalakan", "Uripake Phoron nalika Windows urip" },
                { "Nyalakan layanan otomatis saat Phoron dibuka",
                  "Uripake layanan otomatis nalika Phoron dibukak" },
                { "Tombol tutup mengecilkan ke baki sistem, bukan keluar",
                  "Tombol tutup ngecilake menyang baki sistem, dudu metu" },
                { "Buat vhost otomatis untuk tiap folder di www",
                  "Gawe vhost otomatis kanggo saben folder ing www" },
                { "Sinkronkan berkas hosts Windows (butuh Administrator)",
                  "Selarasake berkas hosts Windows (butuh Administrator)" },
                { "Tampilkan beranda Phoron di http://localhost/",
                  "Tampilake ngarep Phoron ing http://localhost/" },
                { "Catat log rinci", "Cathet log rinci" },
                { "Tulis php.ini ke dalam folder PHP", "Tulis php.ini menyang folder PHP" },
                { "Folder bin yang dipindai", "Folder bin sing disawang" },
                { "Status hak akses", "Kahanan hak akses" },
                { "Folder Phoron", "Folder Phoron" },
                { "Buka folder instalasi", "Bukak folder instalasi" },
                { "Buka phoron.ini", "Bukak phoron.ini" },
                { "Buang blok hosts milik Phoron", "Buwang blok hosts duweke Phoron" },
                { "Pembaruan", "Nganyari" },
                { "Cek rilis baru di GitHub saat Phoron dibuka",
                  "Priksa rilis anyar ing GitHub nalika Phoron dibukak" },
                { "Cek pembaruan sekarang", "Priksa nganyari saiki" },
                { "Buka halaman rilis", "Bukak kaca rilis" },
            };
        }

        static Dictionary<string, string> Banjar_()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Teks layar yang tersisa
                { "Menyambung...",
                  "Manyambung..." },
                { "Batalkan",
                  "Batalakan" },
                { "Apache berhenti",
                  "Apache baranti" },
                { "MySQL berhenti",
                  "MySQL baranti" },
                { "Centang ekstensi yang dipakai profil aktif.",
                  "Centang ekstensi nang dipakai profil aktif." },
                { "Cari ekstensi...",
                  "Gagai ekstensi..." },
                { "Satu folder per baris; boleh lebih dari satu. Yang paling atas jadi akar utama (dilayani http://localhost). Kosongkan untuk memakai www bawaan.",
                  "Sabuah folder sabaris; kawa labih matan sabuah. Nang paling atas jadi akar utama (dilayani http://localhost). Kosongakan hagan mamakai www bawaan." },
                { "Phoron mulai langsung mengecil ke baki sistem, tanpa memunculkan jendela.",
                  "Phoron bamula langsung mangacil ka baki sistem, kada mamunculakan jandila." },
                { "Hanya alamat akar yang dialihkan. Subfolder seperti /simpdam/ tidak tersentuh, dan index.php milik folder proyek tetap bisa dibuka di /index.php. Matikan kalau akar folder proyek Anda memang aplikasi sendiri.",
                  "Hanya alamat akar nang dialihakan. Subfolder kaya /simpdam/ kada tasantuh, wan index.php nang di folder proyek tatap kawa dibuka di /index.php. Padamakan amun akar folder proyek pian mamang aplikasi surang." },
                { "Baku mati agar Phoron ringan: log akses Apache dan seluruh keluaran layanan tidak ditulis (mysqld sendiri mencetak ratusan baris tiap kali menyala). Log GALAT Apache, MySQL, dan PHP tetap menyala — itulah yang menjelaskan kalau ada yang rusak.",
                  "Baku pajah supaya Phoron ringan: log akses Apache wan samunyaan kaluaran layanan kada ditulis (mysqld surang mancitak ratusan baris satiap kali hidup). Log GALAT Apache, MySQL, wan PHP tatap hidup — itu nang manjalasakan amun ada nang rusak." },
                { @"Baku: php.ini ditulis ke etc\php\<versi>\ dan folder PHP tidak disentuh. Nyalakan kalau Anda ingin php.exe dari editor atau Composer di luar Phoron ikut memakai setelan yang sama. Hati-hati bila versi PHP-nya dipinjam dari Laragon - berkasnya dipakai bersama. php.ini asli dicadangkan sekali ke php.ini.sebelum-phoron.",
                  @"Baku: php.ini ditulis ka etc\php\<versi>\ wan folder PHP kada disantuh. Hidupakan amun pian handak php.exe matan editor atawa Composer di luar Phoron umpat mamakai setelan nang sama. Hati-hati amun versi PHP-nya dipinjam matan Laragon - berkasnya dipakai basama. php.ini asli dicadangakan sakali ka php.ini.sebelum-phoron." },
                { "Satu folder per baris. Folder bin Laragon boleh ikut - Phoron tidak pernah menulis ke dalamnya.",
                  "Sabuah folder sabaris. Folder bin Laragon kawa umpat - Phoron kada suah manulis ka dalamnya." },
                { "Pengecekan dilewati kalau baru dilakukan dalam 6 jam terakhir - API GitHub tanpa token dibatasi 60 permintaan per jam.",
                  "Pangecekan dilaluakan amun hanyar haja dilakuakan dalam 6 jam tarakhir - API GitHub kada pakai token dibatasi 60 pamintaan sajam." },
                { "Tiap subfolder di www otomatis dapat alamat sendiri.",
                  "Satiap subfolder di www otomatis dapat alamat surang." },
                { "Semua folder versi yang ditemukan di folder bin yang terdaftar. Folder bin Laragon ikut dipindai bila ada.",
                  "Samunyaan folder versi nang tatamu di folder bin nang tadaftar. Folder bin Laragon umpat dipindai amun ada." },
                { @"Katalog unduhan (etc\catalog.ini)",
                  @"Katalog unduhan (etc\catalog.ini)" },
                { "Katalog lokal",
                  "Katalog lokal" },
                { "Paket diunduh ke folder bin milik Phoron, tidak menyentuh folder Laragon.",
                  "Pakat diunduh ka folder bin nang Phoron, kada manyantuh folder Laragon." },
                { "Beranda", "Halaman Muka" },
                { "Profil", "Profil" },
                { "Versi", "Versi" },
                { "Situs", "Situs" },
                { "Ekstensi PHP", "Ekstensi PHP" },
                { "Pengaturan", "Pangatur" },
                { "Nyalakan semua", "Hidupakan barataan" },
                { "Matikan semua", "Padamakan barataan" },
                { "Keluar", "Kaluar" },
                { "(belum ada profil)", "(halum ada profil)" },
                { "berhenti", "baranti" },
                { "jalan", "bajalan" },
                { "menyalakan", "manghidupakan" },
                { "mematikan", "mamadamakan" },
                { "gagal", "gagal" },

                { "Pilih profil, lalu nyalakan. Ganti profil berarti ganti versi PHP, Apache, dan MySQL sekaligus.",
                  "Pilih profil, hanyar hidupakan. Baganti profil artinya baganti versi PHP, Apache, wan MySQL sakalian." },
                { "Profil aktif", "Profil nang aktif" },
                { "Switch & Jalankan", "Ganti & Jalankan" },
                { "Switch", "Ganti" },
                { "Perhatian", "Paratikan" },
                { "Pintasan", "Jalan singkat" },
                { "Aktivitas", "Kagiatan" },
                { "Buka www", "Buka www" },
                { "Buka localhost", "Buka localhost" },
                { "Beranda Phoron", "Halaman Muka Phoron" },
                { "Uji konfigurasi Apache", "Uji pangatur Apache" },
                { "Buat sertifikat SSL", "Gawi sertifikat SSL" },
                { "Percayai sertifikat SSL", "Picaya sertifikat SSL" },
                { "Buat ulang sertifikat SSL", "Gawi pulang sertifikat SSL" },
                { "Folder etc", "Folder etc" },
                { "Unduh pembaruan", "Unduh pambaharuan" },
                { "Jalankan ulang sebagai Administrator", "Jalankan pulang sabagai Administrator" },
                { "Hentikan proses yang tertinggal", "Hantiakan proses nang tatinggal" },
                { "belum dipilih di profil", "halum dipilih di profil" },

                { "Satu profil = satu kombinasi versi + port. Simpan sebanyak yang perlu, lalu tinggal switch.",
                  "Sabuah profil = sabuah kombinasi versi wan port. Simpan sabanyak nang paralu, hanyar tinggal ganti." },
                { "Profil baru", "Profil hanyar" },
                { "Duplikat", "Salinan" },
                { "Hapus", "Hapus" },
                { "Nama profil", "Ngaran profil" },
                { "Versi PHP", "Versi PHP" },
                { "Versi Apache", "Versi Apache" },
                { "Versi MySQL / MariaDB", "Versi MySQL / MariaDB" },
                { "Folder proyek", "Folder proyek" },
                { "Tambah folder...", "Tambahi folder..." },
                { "Akhiran nama situs", "Ujung ngaran situs" },
                { "Catatan", "Catatan" },
                { "Simpan", "Simpan" },
                { "Simpan lalu switch ke profil ini", "Simpan hanyar ganti ka profil ini" },

                { "Versi terpasang", "Versi nang tapasang" },
                { "Jenis", "Jinis" },
                { "Folder", "Folder" },
                { "Sumber", "Sumber" },
                { "Pindai ulang", "Sarak pulang" },
                { "Tambah folder bin", "Tambahi folder bin" },
                { "Buka folder versi", "Buka folder versi" },
                { "Pasang versi baru", "Pasang versi hanyar" },
                { "Unduh & pasang", "Unduh & pasang" },

                { "Buat situs", "Gawi situs" },
                { "Alamat", "Alamat" },
                { "Buka di browser", "Buka di browser" },
                { "Buka folder", "Buka folder" },
                { "Terminal di sini", "Terminal di sini" },
                { "Segarkan & sinkronkan", "Sagarakan & salaraskan" },
                { "Buka berkas hosts", "Buka barakas hosts" },

                { "Jalankan Next.js, Astro, Vite, dan proyek Node lain dari folder mana pun.",
                  "Jalankan Next.js, Astro, Vite, wan proyek Node lainnya matan folder mana haja." },
                { "Proyek", "Proyek" },
                { "Perintah", "Parintah" },
                { "Status", "Kaadaan" },
                { "Skrip", "Skrip" },
                { "Jalankan", "Jalankan" },
                { "Hentikan", "Hantiakan" },
                { "Tambah proyek...", "Tambahi proyek..." },
                { "Buka alamat", "Buka alamat" },
                { "Hapus dari daftar", "Hapus matan daftar" },
                { "Keluaran", "Kaluaran" },

                { "Setelan php.ini yang sering diubah", "Setelan php.ini nang rancak diubah" },
                { "Simpan ke profil", "Simpan ka profil" },
                { "Ambil dari php.ini asli", "Ambil matan php.ini asli" },
                { "Buka php.ini hasil", "Buka php.ini hasil" },

                { "Semua log Apache, MySQL, PHP, dan Phoron ada di folder logs.",
                  "Samuaan log Apache, MySQL, PHP, wan Phoron ada di folder logs." },
                { "Muat ulang", "Muat pulang" },
                { "Kosongkan", "Kosongakan" },
                { "Ikuti", "Ikuti" },

                { "Setiap perubahan langsung tersimpan ke phoron.ini - tidak ada tombol simpan.",
                  "Satiap parubahan langsung tasimpan ka phoron.ini - kada ada tombol simpan." },
                { "Tampilan", "Tampilan" },
                { "Tema", "Tema" },
                { "Bahasa", "Bahasa" },
                { "Ikut Windows", "Umpat Windows" },
                { "Terang", "Tarang" },
                { "Gelap", "Kalam" },
                { "Jalankan Phoron saat Windows dinyalakan", "Jalankan Phoron wayah Windows dihidupakan" },
                { "Nyalakan layanan otomatis saat Phoron dibuka",
                  "Hidupakan layanan otomatis wayah Phoron dibuka" },
                { "Tombol tutup mengecilkan ke baki sistem, bukan keluar",
                  "Tombol tutup mangacilakan ka baki sistem, lain kaluar" },
                { "Buat vhost otomatis untuk tiap folder di www",
                  "Gawi vhost otomatis gasan satiap folder di www" },
                { "Sinkronkan berkas hosts Windows (butuh Administrator)",
                  "Salaraskan barakas hosts Windows (paralu Administrator)" },
                { "Tampilkan beranda Phoron di http://localhost/",
                  "Tampilakan halaman muka Phoron di http://localhost/" },
                { "Catat log rinci", "Catat log rinci" },
                { "Tulis php.ini ke dalam folder PHP", "Tulis php.ini ka dalam folder PHP" },
                { "Folder bin yang dipindai", "Folder bin nang disarak" },
                { "Status hak akses", "Kaadaan hak akses" },
                { "Folder Phoron", "Folder Phoron" },
                { "Buka folder instalasi", "Buka folder instalasi" },
                { "Buka phoron.ini", "Buka phoron.ini" },
                { "Buang blok hosts milik Phoron", "Buang blok hosts punya Phoron" },
                { "Pembaruan", "Pambaharuan" },
                { "Cek rilis baru di GitHub saat Phoron dibuka",
                  "Pariksa rilis hanyar di GitHub wayah Phoron dibuka" },
                { "Cek pembaruan sekarang", "Pariksa pambaharuan wayahini" },
                { "Buka halaman rilis", "Buka halaman rilis" },
            };
        }
    }
}
