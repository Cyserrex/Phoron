namespace Phoron.Core
{
    public static partial class Lang
    {
        /// <summary>
        /// Halaman Versi, halaman Basis data, dan alur pembaruan (kotak tawaran
        /// pembaruan serta jendela unduhannya).
        /// Baris: { Indonesia, Inggris, Jawa, Banjar }.
        /// </summary>
        static string[][] BarisVersi()
        {
            return new[]
            {
                // Halaman Versi - tabel versi terpasang
                new[] { "Toolset", "Toolset", "Toolset", "Toolset" },
                new[] { "Arsitektur", "Architecture", "Arsitektur", "Arsitektur" },

                // Halaman Versi - folder bin
                new[] { "Pilih folder bin tambahan (mis. C:\\xampp atau bin milik Laragon)",
                        "Choose an extra bin folder (e.g. C:\\xampp or Laragon's bin)",
                        "Pilih folder bin tambahan (upamane C:\\xampp utawa bin duweke Laragon)",
                        "Pilih folder bin tambahan (umpamanya C:\\xampp atawa bin punya Laragon)" },
                new[] { "Tidak ada Laragon, XAMPP, atau WAMP yang terpasang di tempat bakunya. Kalau punya, tambahkan foldernya sendiri lewat \"Tambah folder bin\".",
                        "No Laragon, XAMPP or WAMP is installed in its default location. If you have one, add its folder yourself with \"Add bin folder\".",
                        "Ora ana Laragon, XAMPP, utawa WAMP sing kapasang ing panggonan bawaane. Yen duwe, tambahna foldere dhewe liwat \"Tambah folder bin\".",
                        "Kadada Laragon, XAMPP, atawa WAMP nang tapasang di wadah bakunya. Amun baisi, tambahakan foldernya surang lewat \"Tambahi folder bin\"." },
                new[] { "Semua yang terdeteksi sudah terdaftar: {0}",
                        "Everything detected is already on the list: {0}",
                        "Kabeh sing kadeteksi wis kadaftar: {0}",
                        "Samunyaan nang tadeteksi sudah tadaptar: {0}" },
                new[] { "Ditemukan folder bin ini di komputer Anda:\n\n{0}\n\nTambahkan ke daftar yang dipindai? Phoron tidak pernah menulis ke dalam folder itu.",
                        "Found these bin folders on your computer:\n\n{0}\n\nAdd them to the scanned list? Phoron never writes into those folders.",
                        "Folder bin iki ketemu ing komputer sampeyan:\n\n{0}\n\nTambahake menyang daftar sing dipindhai? Phoron ora tau nulis ing njero folder kuwi.",
                        "Tatamu folder bin ini di komputer Pian:\n\n{0}\n\nTambahakan ka daptar nang digagai? Phoron kada suah manulis ka dalam folder itu." },
                new[] { "{0} folder ditambahkan. Sekarang ada {1} paket terdeteksi.",
                        "{0} folder(s) added. {1} packages are now detected.",
                        "{0} folder ditambahake. Saiki ana {1} paket kadeteksi.",
                        "{0} folder ditambahakan. Wayahini ada {1} pakit tadeteksi." },

                // Halaman Versi - unduhan paket
                new[] { "Mengambil daftar versi PHP...",
                        "Fetching the list of PHP versions...",
                        "Njupuk daftar versi PHP...",
                        "Maambil daptar versi PHP..." },
                new[] { "{0} arsip PHP tersedia di windows.php.net.",
                        "{0} PHP archives available on windows.php.net.",
                        "{0} arsip PHP cumepak ing windows.php.net.",
                        "{0} arsip PHP tasadia di windows.php.net." },
                new[] { "{0} (perlu koneksi internet)",
                        "{0} (internet connection required)",
                        "{0} (butuh sambungan internet)",
                        "{0} (paralu koneksi internet)" },
                new[] { "Pilih dulu paket yang mau dipasang.",
                        "Choose a package to install first.",
                        "Pilih dhisik paket sing arep dipasang.",
                        "Pilih bahulu pakit nang handak dipasang." },
                new[] { "Mengunduh {0}...", "Downloading {0}...", "Ngundhuh {0}...", "Mahunduh {0}..." },
                new[] { "{0} terpasang.", "{0} installed.", "{0} wis kapasang.", "{0} tapasang." },

                // Tawaran pembaruan
                new[] { "Belum ada hasil pengecekan.",
                        "No check result yet.",
                        "Durung ana asil pamriksan.",
                        "Balum ada kulihan pamariksaan." },
                new[] { "Phoron {0} sudah rilis.\nYang terpasang sekarang: {1}.",
                        "Phoron {0} is out.\nCurrently installed: {1}.",
                        "Phoron {0} wis dirilis.\nSing kapasang saiki: {1}.",
                        "Phoron {0} sudah dirilis.\nNang tapasang wayahini: {1}." },
                // Label dalam kurung siku menyebut tombol kotak pesan Windows, yang
                // bahasanya mengikuti Windows, bukan Phoron.
                new[] { "Unduh installer-nya sekarang?\n\n[Ya] unduh lalu jalankan pemasangnya\n[Tidak] buka halaman rilis di browser\n[Batal] tidak sekarang",
                        "Download the installer now?\n\n[Yes] download it and run the installer\n[No] open the release page in the browser\n[Cancel] not now",
                        "Undhuh installer-e saiki?\n\n[Ya] undhuh banjur bukak pemasange\n[Tidak] bukak kaca rilis ing browser\n[Batal] ora saiki",
                        "Hunduh installer-nya wayahini?\n\n[Ya] hunduh hanyar jalanakan pamasangnya\n[Kada] buka laman rilis di browser\n[Batal] kada wayahini" },
                new[] { "Pembaruan tersedia", "Update available", "Nganyari cumepak", "Pahanyaran tasadia" },
                new[] { "Rilis itu tidak menyertakan berkas installer. Halaman rilisnya akan dibuka di browser.",
                        "That release has no installer file. Its release page will open in the browser.",
                        "Rilis kuwi ora nggawa berkas installer. Kaca rilise bakal dibukak ing browser.",
                        "Rilis itu kada manyartakan barakas installer. Laman rilisnya handak dibuka di browser." },
                new[] { "Installer sudah diunduh.\n\nJalankan sekarang?\n\nPhoron akan ditutup lebih dulu supaya Apache dan MySQL berhenti dengan rapi. Proyek, basis data, dan profil Anda tidak disentuh.",
                        "The installer has been downloaded.\n\nRun it now?\n\nPhoron will close first so Apache and MySQL stop cleanly. Your projects, databases and profiles are not touched.",
                        "Installer wis diundhuh.\n\nBukak saiki?\n\nPhoron bakal ditutup dhisik supaya Apache lan MySQL mandheg kanthi rapi. Proyek, basis data, lan profil sampeyan ora disenggol.",
                        "Installer sudah dihunduh.\n\nJalanakan wayahini?\n\nPhoron handak ditutup bahulu supaya Apache wan MySQL pajah lawan rapi. Proyek, basis data, wan profil Pian kada disantuh." },
                new[] { "Tidak bisa menjalankan installer: {0}\n\nBerkasnya ada di {1}.",
                        "Could not run the installer: {0}\n\nThe file is at {1}.",
                        "Ora bisa mbukak installer: {0}\n\nBerkase ana ing {1}.",
                        "Kada kawa manjalanakan installer: {0}\n\nBarakasnya ada di {1}." },

                // Jendela unduhan pembaruan
                new[] { "Mengunduh pembaruan", "Downloading update", "Ngundhuh nganyari", "Mahunduh pahanyaran" },
                new[] { "{0} dari {1}", "{0} of {1}", "{0} saka {1}", "{0} matan {1}" },
                new[] { "{0} terunduh", "{0} downloaded", "{0} wis diundhuh", "{0} tahunduh" },
                new[] { "Membatalkan...", "Cancelling...", "Mbatalake...", "Mamasahakan..." },
            };
        }
    }
}
