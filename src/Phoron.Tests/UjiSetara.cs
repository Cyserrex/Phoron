using System;
using System.IO;
using System.Text;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Pembangkit bahan uji setara untuk port Core ke Rust (daemon\).
    ///
    /// Menulis berkas MASUKAN (phoron.ini dan profil dalam berbagai bentuk,
    /// termasuk yang disunting tangan dan yang cacat ringan), lalu membiarkan
    /// Core C# memuat dan menyimpannya ulang sebagai KELUARAN. Uji di
    /// daemon\inti\tests\setara.rs melakukan hal yang sama dengan Core Rust dan
    /// menuntut hasil yang identik byte demi byte.
    ///
    /// Dibangkitkan saat uji, tidak disimpan di repo: sandi tersandi DPAPI
    /// terikat pada pengguna Windows yang membuatnya.
    /// Jalankan dengan: daemon\uji_setara.ps1
    /// </summary>
    internal static class UjiSetara
    {
        public static int Jalankan(string akar)
        {
            if (Directory.Exists(akar)) Directory.Delete(akar, true);
            var masuk = Path.Combine(akar, "masuk");
            var keluar = Path.Combine(akar, "csharp");
            var dulu = Paths.Root;
            try
            {
                Profil(masuk, keluar);
                Setelan(masuk, keluar, akar);
            }
            finally { Paths.Root = dulu; }
            Console.WriteLine("bahan uji setara ditulis ke " + akar);
            return 0;
        }

        static void Tulis(string path, string isi, bool bom = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, isi, new UTF8Encoding(bom));
        }

        static void Profil(string masuk, string keluar)
        {
            var dir = Path.Combine(masuk, "profiles");
            // Buatan Phoron sendiri.
            Tulis(Path.Combine(dir, "php-8-1-10-apache-2-4-54.ini"),
                "; Profil Phoron - boleh disunting tangan.\r\n; Nama folder versi harus persis seperti di folder bin.\r\n"
                + "[profil]\r\nnama=PHP 8.1.10 + Apache 2.4.54\r\nweb_server=apache\r\nphp=php-8.1.10-Win32-vs16-x64\r\n"
                + "apache=httpd-2.4.54-win64-VS16\r\nnginx=\r\nmysql=mysql-8.0.30-winx64\r\nport_http=80\r\nport_https=443\r\n"
                + "port_mysql=3306\r\nfolder_proyek=\r\nakhiran_situs=test\r\ncatatan=\r\n\r\n[php]\r\n"
                + "ekstensi=curl,fileinfo,gd,intl,mbstring,exif,mysqli,openssl,pdo_mysql,xsl\r\n\r\n");
            // Disunting tangan: LF, spasi, kunci lama document_root, besar-kecil campur.
            Tulis(Path.Combine(dir, "lama.ini"),
                "[Profil]\nNama = Proyek Lama  \ndocument_root=D:\\www\\;d:\\WWW;E:\\situs\\\n"
                + "PORT_HTTP= 8080\nport_mysql=tiga\n[php]\nekstensi= MbString , ,mbstring,GD\n"
                + "[php.ini]\nmemory_limit=512M\nMemory_Limit=1G\nmax_execution_time = 300\n"
                + "[php_situs]\nD:\\www\\ci2=php-5.6.40-Win32-VC11-x64\nD:\\www\\kosong=\n");
            // BOM, aksara non-ASCII, nginx, catatan.
            Tulis(Path.Combine(dir, "nginx-ünik.ini"),
                "[profil]\r\nnama=Nginx ✓ Ünik\r\nweb_server=nginx\r\nnginx=nginx-1.27.0\r\ncatatan=satu dua\r\n"
                + "akhiran_situs=local\r\n[lain]\r\nkunci_asing=dibuang\r\n", true);
            // Cacat ringan: baris nyasar dan NUL di ujung.
            Tulis(Path.Combine(dir, "cacat.ini"),
                "[profil]\r\nnama=Cacat\r\nngawur\r\n[tidak tertutup\r\nphp=php-7\r\n\0\0\0");

            Paths.Root = keluar;
            Directory.CreateDirectory(Paths.Profiles);
            foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(Paths.Profiles, Path.GetFileName(f)));
            foreach (var p in ProfileStore.LoadAll()) ProfileStore.Save(p);

            // Profil baru tanpa nama berkas: nama berkasnya dari Slug.
            var baru = new Profile
            {
                Name = "Kopi Pagi ✓ 8.3",
                PhpId = "php-8.3.12",
                Notes = "baris\r\nkedua",
            };
            baru.ProjectRoots.Add(@"D:\proyek");
            baru.PhpIniOverrides["upload_max_filesize"] = "64M";
            ProfileStore.Save(baru);
        }

        static void Setelan(string masuk, string keluar, string akar)
        {
            // a: dibuat dari nol oleh C#, dengan sandi tersandi DPAPI.
            Paths.Root = Path.Combine(masuk, "setelan-a");
            Directory.CreateDirectory(Paths.Root);
            var s = new Settings
            {
                BinRoots = { @"C:\Phoron\bin", @"D:\laragon\bin" },
                ActiveProfile = "php-8-1-10-apache-2-4-54",
                SitusTerakhir = { @"D:\www\blog", @"D:\www\toko" },
                AutoStartServices = true,
                MinimizeToTray = false,
                AutoVhost = true,
                Opcache = false,
                ManageHosts = false,
                PhpIniKeFolderPhp = true,
                LogRinci = true,
                BerandaDiAkar = false,
                Tema = "gelap",
                JendelaLebar = 900,
                JendelaTinggi = 600,
                Bahasa = "en",
                CekPembaruan = false,
                CekTerakhir = new DateTime(2026, 11, 27, 7, 50, 29, DateTimeKind.Utc).AddTicks(1234567),
                Terminal = "wt",
                Editor = @"C:\Program Files\Microsoft VS Code\Code.exe",
                DbPengguna = "dev",
                HeidiSql = @"C:\laragon\bin\heidisql\heidisql.exe",
                DbSandi = "s3kr!t é",
            };
            s.Save();
            Salin(masuk, keluar, "setelan-a");

            // b: disunting tangan, dengan kunci asing dan token lama.
            Tulis(Path.Combine(masuk, "setelan-b", "phoron.ini"),
                "; komentar orang\n[umum]\nbin_roots=C:\\Phoron\\bin ; D:\\laragon\\bin;\nbahasa=jv\n"
                + "token_github=ghp_lama\nkunci_masa_depan=1\nsitus_terakhir=a|b|c|d|e|f|g\n"
                + "[basisdata]\npengguna=\n[plugin]\nx=y\n");
            Salin(masuk, keluar, "setelan-b");

            // c: nilai ngawur jatuh ke bawaan.
            Tulis(Path.Combine(masuk, "setelan-c", "phoron.ini"),
                "[umum]\r\nbin_roots=C:\\x\r\nbahasa=xx\r\ncek_terakhir=kemarin\r\njendela_lebar=-5\r\n"
                + "auto_start=YES\r\nopcache=nanti\r\n[basisdata]\r\nsandi=bukan-base64!\r\n");
            Salin(masuk, keluar, "setelan-c");

            // Harapan nilai yang tidak terlihat di berkas keluaran.
            var h = new Ini();
            h.Set("setelan-a", "sandi", "s3kr!t é");
            h.Save(Path.Combine(akar, "harapan.ini"));
        }

        /// <summary>Salin masukan ke folder keluaran, lalu muat-dan-simpan dengan Core C#.</summary>
        static void Salin(string masuk, string keluar, string kasus)
        {
            Paths.Root = Path.Combine(keluar, kasus);
            Directory.CreateDirectory(Paths.Root);
            File.Copy(Path.Combine(masuk, kasus, "phoron.ini"), Paths.SettingsFile);
            Settings.Load().Save();
        }
    }
}
