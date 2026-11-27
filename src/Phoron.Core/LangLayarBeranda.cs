namespace Phoron.Core
{
    public static partial class Lang
    {
        /// <summary>
        /// Teks layar Beranda, jendela utama, dan kotak pesan galat di App.
        /// Baris: { Indonesia, Inggris, Jawa, Banjar }.
        /// </summary>
        static string[][] BarisBeranda()
        {
            return new[]
            {
                // Kartu layanan
                new[] { "https mati", "https off", "https mati", "https pajah" },

                // Panel peringatan
                new[] { "Belum ada profil. Buat satu di halaman Profil.",
                        "No profile yet. Create one on the Profiles page.",
                        "Durung ana profil. Gawe siji ing kaca Profil.",
                        "Balum ada profil. Ulah sabuting di laman Profil." },
                new[] { "Konfigurasi web server dan MySQL sudah berubah, tapi keduanya masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku.",
                        "The web server and MySQL configuration has changed, but both are still using the old one. Restart them for the change to take effect.",
                        "Setelan web server lan MySQL wis owah, nanging loro-lorone isih nganggo sing lawas. Uripna maneh supaya owahane kanggo.",
                        "Setelan web server wan MySQL sudah baubah, tagal duduanya masih mamakai nang lawas. Hidupakan pulang supaya parubahannya balaku." },
                new[] { "Konfigurasi web server sudah berubah, tapi yang sedang jalan masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku.",
                        "The web server configuration has changed, but the running server still uses the old one. Restart it for the change to take effect.",
                        "Setelan web server wis owah, nanging sing lagi mlaku isih nganggo sing lawas. Uripna maneh supaya owahane kanggo.",
                        "Setelan web server sudah baubah, tagal nang lagi bajalan masih mamakai nang lawas. Hidupakan pulang supaya parubahannya balaku." },
                new[] { "Konfigurasi MySQL sudah berubah, tapi yang sedang jalan masih memakai yang lama. Nyalakan ulang supaya perubahannya berlaku.",
                        "The MySQL configuration has changed, but the running server still uses the old one. Restart it for the change to take effect.",
                        "Setelan MySQL wis owah, nanging sing lagi mlaku isih nganggo sing lawas. Uripna maneh supaya owahane kanggo.",
                        "Setelan MySQL sudah baubah, tagal nang lagi bajalan masih mamakai nang lawas. Hidupakan pulang supaya parubahannya balaku." },
                new[] { "HTTPS belum aktif: sertifikat belum ada, jadi port {0} tidak dibuka. Pakai http:// (bukan https://), atau tekan \"Buat sertifikat SSL\" di bawah.",
                        "HTTPS is not active: there is no certificate yet, so port {0} is not opened. Use http:// (not https://), or press \"Create SSL certificate\" below.",
                        "HTTPS durung aktif: sertifikat durung ana, dadi port {0} ora dibukak. Nganggo http:// (dudu https://), utawa pencet \"Gawe sertifikat SSL\" ing ngisor.",
                        "HTTPS balum aktip: sertifikat balum ada, jadi port {0} kada dibuka. Pakai http:// (lain https://), atawa tikan \"Ulah sertifikat SSL\" di bawah." },
                new[] { "Sertifikat HTTPS sudah ada tapi belum tepercaya, jadi browser akan memperingatkan - dan pada host ber-HSTS tidak ada tombol pengecualian sama sekali. Tekan \"Percayai sertifikat SSL\" di bawah.",
                        "The HTTPS certificate exists but is not trusted yet, so browsers will warn - and on HSTS hosts there is no exception button at all. Press \"Trust SSL certificate\" below.",
                        "Sertifikat HTTPS wis ana nanging durung dipercaya, dadi browser bakal ngelingake - lan ing host sing nganggo HSTS ora ana tombol pengecualian babar pisan. Pencet \"Percaya sertifikat SSL\" ing ngisor.",
                        "Sertifikat HTTPS sudah ada tagal balum dipicayai, jadi browser akan mambari paringatan - wan di host nang bapakai HSTS kadada tombol pangecualian sama sakali. Tikan \"Picayai sertifikat SSL\" di bawah." },
                new[] { "Phoron jalan tanpa hak Administrator - itu wajar, Windows selalu begitu untuk aplikasi yang menyala sendiri saat boot. Akibatnya {0} nama situs .test belum terdaftar di berkas hosts. Alamat http://localhost/proyek/ tetap jalan normal. Daftarkan sekali saja, sesudah itu tidak perlu Administrator lagi.",
                        "Phoron is running without Administrator rights - that is normal, Windows always does this for apps that start on their own at boot. As a result, {0} .test site names are not registered in the hosts file yet. http://localhost/proyek/ still works normally. Register them once; after that Administrator is no longer needed.",
                        "Phoron mlaku tanpa hak Administrator - kuwi lumrah, Windows tansah ngono kanggo aplikasi sing urip dhewe nalika boot. Akibate {0} jeneng situs .test durung kadaftar ing berkas hosts. Alamat http://localhost/proyek/ tetep mlaku normal. Daftarake sepisan wae, sawise kuwi ora perlu Administrator maneh.",
                        "Phoron bajalan kada baisi hak Administrator - itu wajar, Windows salawasnya kaya itu gasan aplikasi nang hidup surang wayah boot. Akibatnya {0} ngaran situs .test balum tadaptar di barakas hosts. Alamat http://localhost/proyek/ tatap bajalan biasa. Daptarakan sakali haja, imbah itu kada paralu Administrator lagi." },
                new[] { "Proses itu BISA JADI sisa Phoron yang sebelumnya berakhir tanpa sempat membersihkan diri - tapi bisa juga milik Laragon atau XAMPP yang memang sedang Anda pakai. Jalur berkasnya ditampilkan sebelum dihentikan.",
                        "Those processes MAY BE leftovers from an earlier Phoron that exited without cleaning up - but they could also belong to a Laragon or XAMPP you are actually using. Their file paths are shown before anything is stopped.",
                        "Proses kuwi BISA WAE turahan Phoron sadurunge sing rampung tanpa sempat ngresiki awake - nanging bisa uga duweke Laragon utawa XAMPP sing pancen lagi sampeyan anggo. Dalan berkase ditampilake sadurunge dipateni.",
                        "Proses itu KAWA JADI sisa Phoron nang sabalumnya baranti kada sempat mambarasihi diri - tagal kawa jua punya Laragon atawa XAMPP nang mamang lagi Pian pakai. Jalur barakasnya ditampaiakan sabalum dipajahakan." },

                // Menghentikan sisa proses
                new[] { "Hentikan proses berikut?",
                        "Stop the following processes?",
                        "Patenana proses ing ngisor iki?",
                        "Pajahakan proses nang di bawah ini?" },
                new[] { "{0} (PID {1}) di port {2}",
                        "{0} (PID {1}) on port {2}",
                        "{0} (PID {1}) ing port {2}",
                        "{0} (PID {1}) di port {2}" },
                new[] { "Periksa jalurnya dulu: kalau ia berada di folder Laragon atau XAMPP, kemungkinan besar itu milik aplikasi lain yang sedang berjalan.",
                        "Check the path first: if it is inside a Laragon or XAMPP folder, it most likely belongs to another application that is running.",
                        "Priksa dalane dhisik: yen ana ing folder Laragon utawa XAMPP, kemungkinan gedhe kuwi duweke aplikasi liya sing lagi mlaku.",
                        "Pariksa jalurnya dahulu: amun inya ada di folder Laragon atawa XAMPP, kamungkinan ganal itu punya aplikasi lain nang lagi bajalan." },
                new[] { "MySQL diminta berhenti dengan rapi lebih dulu.",
                        "MySQL is asked to shut down cleanly first.",
                        "MySQL dijaluk mandheg kanthi rapi luwih dhisik.",
                        "MySQL dimintai baranti sacara rapi labih dahulu." },
                new[] { "Proses yang tertinggal sudah dihentikan; portnya bebas.",
                        "The leftover processes have been stopped; the ports are free.",
                        "Proses sing keri wis dipateni; porte wis bebas.",
                        "Proses nang tatinggal sudah dipajahakan; portnya bebas." },

                // Hak Administrator
                new[] { "Menyunting berkas hosts dan memasang sertifikat ke Trusted Root hanya bisa dilakukan dengan hak Administrator.",
                        "Editing the hosts file and installing the certificate into Trusted Root can only be done with Administrator rights.",
                        "Nyunting berkas hosts lan masang sertifikat menyang Trusted Root mung bisa dilakoni nganggo hak Administrator.",
                        "Manyunting barakas hosts wan mamasang sertifikat ka Trusted Root hanya kawa dilakuakan lawan hak Administrator." },
                new[] { "Memasang sertifikat ke Trusted Root butuh hak Administrator.",
                        "Installing the certificate into Trusted Root requires Administrator rights.",
                        "Masang sertifikat menyang Trusted Root butuh hak Administrator.",
                        "Mamasang sertifikat ka Trusted Root paralu hak Administrator." },

                // Pintasan
                new[] { "Gagal membuat phpinfo: {0}",
                        "Failed to create phpinfo: {0}",
                        "Gagal nggawe phpinfo: {0}",
                        "Gagal maulah phpinfo: {0}" },
                new[] { "Profil ini belum menunjuk Apache.",
                        "This profile has no Apache selected.",
                        "Profil iki durung milih Apache.",
                        "Profil ngini balum mamilih Apache." },
                new[] { "Konfigurasi OK.", "Configuration OK.", "Konfigurasi OK.", "Konfigurasi OK." },
                new[] { "Konfigurasi OK", "Configuration OK", "Konfigurasi OK", "Konfigurasi OK" },
                new[] { "Konfigurasi bermasalah",
                        "Configuration has problems",
                        "Konfigurasi ana masalah",
                        "Konfigurasi ada masalah" },
                new[] { "Sertifikat sudah ada dan CA-nya sudah tepercaya. Buat ulang sertifikat situs? CA yang sama dipakai lagi, jadi tidak perlu dipercayai ulang.",
                        "The certificate already exists and its CA is trusted. Recreate the site certificate? The same CA is reused, so it does not need to be trusted again.",
                        "Sertifikat wis ana lan CA-ne wis dipercaya. Gawe maneh sertifikat situs? CA sing padha dianggo maneh, dadi ora perlu dipercaya maneh.",
                        "Sertifikat sudah ada wan CA-nya sudah dipicayai. Ulah pulang sertifikat situs? CA nang sama dipakai pulang, jadi kada paralu dipicayai pulang." },
                new[] { "Butuh paket Apache (openssl.exe ada di dalamnya).",
                        "Requires the Apache package (openssl.exe is inside it).",
                        "Butuh paket Apache (openssl.exe ana ing njerone).",
                        "Paralu pakat Apache (openssl.exe ada di dalamnya)." },
                new[] { "Pasang CA Phoron ke Trusted Root Windows supaya browser tidak memberi peringatan?",
                        "Install the Phoron CA into Windows Trusted Root so browsers stop showing warnings?",
                        "Pasang CA Phoron menyang Trusted Root Windows supaya browser ora menehi pengetan?",
                        "Pasang CA Phoron ka Trusted Root Windows supaya browser kada mambari paringatan?" },
                new[] { "CA Phoron terpasang di Trusted Root Windows. Tutup dan buka ulang browser.\n\nChrome dan Edge langsung ikut. Firefox memakai daftar sertifikatnya sendiri: buka about:config, setel security.enterprise_roots.enabled jadi true, lalu jalankan ulang Firefox.\n\nKalau sebuah nama (mis. localhost) pernah dipasangi HSTS, Firefox menolak menampilkan tombol pengecualian sampai sertifikatnya benar-benar tepercaya - atau entri HSTS-nya dibuang lewat Riwayat > klik kanan situs > Lupakan Situs Ini.",
                        "The Phoron CA is installed in Windows Trusted Root. Close and reopen your browser.\n\nChrome and Edge pick it up right away. Firefox uses its own certificate list: open about:config, set security.enterprise_roots.enabled to true, then restart Firefox.\n\nIf a name (e.g. localhost) has ever received HSTS, Firefox refuses to show the exception button until the certificate is truly trusted - or until its HSTS entry is removed via History > right-click the site > Forget About This Site.",
                        "CA Phoron wis kepasang ing Trusted Root Windows. Tutup banjur bukak maneh browser.\n\nChrome lan Edge langsung melu. Firefox nganggo daftar sertifikate dhewe: bukak about:config, setel security.enterprise_roots.enabled dadi true, banjur uripna maneh Firefox.\n\nYen sawijining jeneng (upamane localhost) tau dipasangi HSTS, Firefox ora gelem nampilake tombol pengecualian nganti sertifikate tenan-tenan dipercaya - utawa entri HSTS-e dibuwang liwat Riwayat > klik tengen situse > Lalekna Situs Iki.",
                        "CA Phoron sudah tapasang di Trusted Root Windows. Tutup wan buka pulang browser.\n\nChrome wan Edge langsung umpat. Firefox mamakai daptar sertifikatnya surang: buka about:config, setel security.enterprise_roots.enabled jadi true, hanyar jalanakan pulang Firefox.\n\nAmun sabuting ngaran (umpamanya localhost) suah dipasangi HSTS, Firefox kada hakun manampaiakan tombol pangecualian sampai sertifikatnya bujur-bujur dipicayai - atawa entri HSTS-nya dibuang lewat Riwayat > klik kanan situs > Lupakan Situs Ini." },

                // Jendela utama
                new[] { "Tutup Phoron sepenuhnya dan matikan semua layanan",
                        "Close Phoron completely and stop all services",
                        "Tutup Phoron babar pisan lan patenana kabeh layanan",
                        "Tutup Phoron sapanuhnya wan pajahakan samunyaan layanan" },
                new[] { "Masih ada layanan yang berjalan. Semuanya akan dimatikan.\n\nKeluar dari Phoron sekarang?",
                        "Some services are still running. They will all be stopped.\n\nExit Phoron now?",
                        "Isih ana layanan sing mlaku. Kabeh bakal dipateni.\n\nMetu saka Phoron saiki?",
                        "Masih ada layanan nang bajalan. Samunyaan akan dipajahakan.\n\nKaluar matan Phoron wayahini?" },

                // Kotak pesan galat (App)
                new[] { "Phoron berkali-kali menemui galat dan akan ditutup supaya tidak menimbulkan kerusakan lain.",
                        "Phoron has hit errors repeatedly and will close so it does not cause further damage.",
                        "Phoron bola-bali nemu galat lan bakal ditutup supaya ora nuwuhake karusakan liyane.",
                        "Phoron bakali-kali tatamu kasalahan wan akan ditutup supaya kada maulah karusakan lain." },
                new[] { "Ada yang tidak beres di dalam Phoron.",
                        "Something went wrong inside Phoron.",
                        "Ana sing ora beres ing njero Phoron.",
                        "Ada nang kada baik di dalam Phoron." },
                new[] { "Layanan yang sedang berjalan tidak terganggu. Lanjutkan memakai Phoron?",
                        "Running services are not affected. Keep using Phoron?",
                        "Layanan sing lagi mlaku ora keganggu. Terusake nganggo Phoron?",
                        "Layanan nang lagi bajalan kada taganggu. Lanjutakan mamakai Phoron?" },
                new[] { "Phoron harus ditutup karena galat yang tidak bisa dipulihkan.",
                        "Phoron must close because of an unrecoverable error.",
                        "Phoron kudu ditutup amarga galat sing ora bisa dipulihake.",
                        "Phoron musti ditutup gara-gara kasalahan nang kada kawa dipulihakan." },
                new[] { "(galat tanpa keterangan)",
                        "(error with no details)",
                        "(galat tanpa katrangan)",
                        "(kasalahan kada baisi katarangan)" },
            };
        }
    }
}
