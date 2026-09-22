using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Uji untuk lapisan yang bicara dengan MySQL.
    ///
    /// Yang diuji di sini adalah bagian yang TIDAK butuh server menyala: memecah
    /// SQL, membaca keluaran --batch, mengutip nama, dan menyandi sandi. Bagian
    /// yang butuh server sungguhan ada di LiveTest - dan memang harus di sana,
    /// sebab satu-satunya cara membuktikan bentuk keluaran adalah menanyakannya
    /// ke MySQL, bukan mengulang keyakinan sendiri dalam bentuk uji.
    /// </summary>
    public static partial class Program
    {
        static void UjiSqlPisah()
        {
            Bagian("Memecah SQL");

            var a = MySqlKlien.Pisah("SELECT 1; SELECT 2");
            Ok("Dua perintah dipisah", a.Count == 2, string.Join(" | ", a.ToArray()));

            // Inilah sebab pemisah ini ditulis sama sekali. Memecah dengan
            // Split(';') merusak setiap SQL yang memuat titik koma di dalam teks -
            // dan alamat, jam, serta daftar berkoma sangat sering memuatnya.
            var b = MySqlKlien.Pisah("INSERT INTO t VALUES ('a;b'); SELECT 1");
            Ok("Titik koma di dalam kutipan bukan pemisah", b.Count == 2,
               b.Count + ": " + string.Join(" | ", b.ToArray()));
            Ok("Isi kutipan tetap utuh", b.Count == 2 && b[0].Contains("'a;b'"),
               b.Count > 0 ? b[0] : "");

            var c = MySqlKlien.Pisah("SELECT 'bukan '' penutup; masih di dalam'; SELECT 2");
            Ok("Kutip ganda di dalam kutipan tidak menutup", c.Count == 2,
               c.Count + ": " + string.Join(" | ", c.ToArray()));

            var d = MySqlKlien.Pisah("SELECT `kolom;aneh` FROM t; SELECT 2");
            Ok("Titik koma di dalam nama berkutip-balik bukan pemisah", d.Count == 2,
               d.Count + ": " + string.Join(" | ", d.ToArray()));

            var e = MySqlKlien.Pisah("SELECT 1; -- catatan; bukan perintah\nSELECT 2");
            Ok("Titik koma di dalam komentar baris bukan pemisah", e.Count == 2,
               e.Count + ": " + string.Join(" | ", e.ToArray()));

            var f = MySqlKlien.Pisah("SELECT 1; /* catatan; panjang */ SELECT 2");
            Ok("Titik koma di dalam komentar blok bukan pemisah", f.Count == 2,
               f.Count + ": " + string.Join(" | ", f.ToArray()));

            // "--" baru jadi komentar bila diikuti spasi. Tanpa aturan itu,
            // pengurangan bilangan negatif akan tertelan sebagai komentar.
            var g = MySqlKlien.Pisah("SELECT 5--3");
            Ok("Tanda minus ganda tanpa spasi bukan komentar",
               g.Count == 1 && g[0].Contains("5--3"),
               g.Count > 0 ? g[0] : "kosong");

            // Dua sisi dari aturan yang sama, dan pasangan inilah yang membuatnya
            // berarti. Di SQL, \\ adalah satu garis miring - kutipannya tertutup,
            // jadi titik koma sesudahnya memang memisah.
            var h1 = MySqlKlien.Pisah(@"SELECT 'c:\\'; SELECT 2");
            Ok("Garis miring ganda menutup kutipan, jadi titik koma memisah",
               h1.Count == 2, h1.Count + ": " + string.Join(" | ", h1.ToArray()));

            // Sedangkan \' adalah tanda kutip yang diloloskan - kutipannya BELUM
            // tertutup, jadi titik koma sesudahnya masih di dalam teks. Harapan
            // pertama saya di sini keliru, dan pemisahnya yang benar.
            var h2 = MySqlKlien.Pisah(@"SELECT 'c:\'; SELECT 2");
            Ok("Kutip yang diloloskan tidak menutup, jadi tidak ada pemisahan",
               h2.Count == 1, h2.Count + ": " + string.Join(" | ", h2.ToArray()));

            Ok("Titik koma beruntun tidak jadi perintah kosong",
               MySqlKlien.Pisah("SELECT 1;;; SELECT 2").Count == 2);
            Ok("Teks kosong tidak menghasilkan perintah",
               MySqlKlien.Pisah("   \n  ").Count == 0);
            Ok("Perintah tanpa titik koma di akhir tetap terbaca",
               MySqlKlien.Pisah("SELECT 1").Count == 1);
        }

        static void UjiSqlBerbaris()
        {
            Bagian("Perintah yang mengembalikan baris");

            Ok("SELECT mengembalikan baris", MySqlKlien.Berbaris("SELECT 1"));
            Ok("select huruf kecil tetap dikenali", MySqlKlien.Berbaris("select 1"));
            Ok("SHOW mengembalikan baris", MySqlKlien.Berbaris("SHOW TABLES"));
            Ok("WITH mengembalikan baris", MySqlKlien.Berbaris("WITH x AS (SELECT 1) SELECT * FROM x"));
            Ok("Kurung buka dikenali sebagai SELECT bergabung",
               MySqlKlien.Berbaris("(SELECT 1) UNION (SELECT 2)"));

            Ok("INSERT tidak mengembalikan baris", !MySqlKlien.Berbaris("INSERT INTO t VALUES (1)"));
            Ok("UPDATE tidak mengembalikan baris", !MySqlKlien.Berbaris("UPDATE t SET a=1"));
            Ok("CREATE tidak mengembalikan baris", !MySqlKlien.Berbaris("CREATE TABLE t (a INT)"));

            // Tanpa membuang komentar di depan, kueri yang diberi keterangan akan
            // disangka perintah yang tidak mengembalikan apa-apa - dan hasilnya
            // tidak akan pernah tampil di layar.
            Ok("Komentar blok di depan tidak menyamarkan SELECT",
               MySqlKlien.Berbaris("/* laporan harian */ SELECT 1"));
            Ok("Komentar baris di depan tidak menyamarkan SELECT",
               MySqlKlien.Berbaris("-- laporan harian\nSELECT 1"));
            Ok("Komentar berpagar di depan tidak menyamarkan SELECT",
               MySqlKlien.Berbaris("# laporan harian\nSELECT 1"));
            Ok("Spasi di depan tidak menyamarkan SELECT",
               MySqlKlien.Berbaris("\r\n   \t SELECT 1"));
        }

        static void UjiSqlUrai()
        {
            Bagian("Membaca keluaran --batch");

            // Bentuk ini BUKAN karangan: persis inilah yang dikeluarkan mysql.exe
            // 5.7 saat ditanya, diperiksa langsung sebelum penguraiannya ditulis.
            var t = MySqlKlien.Urai("a\tb\tc\n1\tdua\ttiga\n4\tlima\tenam\n");
            Ok("Judul kolom terbaca", t.Kolom.Count == 3 && t.Kolom[0] == "a" && t.Kolom[2] == "c",
               string.Join(",", t.Kolom.ToArray()));
            Ok("Dua baris data terbaca", t.Baris.Count == 2, t.Baris.Count.ToString());
            Ok("Sel terbaca pada posisinya", t.Baris[1][1] == "lima", t.Baris[1][1]);

            Ok("Keluaran kosong jadi tabel kosong",
               MySqlKlien.Urai("").Kolom.Count == 0);
            var hanyaJudul = MySqlKlien.Urai("a\tb\n");
            Ok("Hasil tanpa baris tetap punya kolom",
               hanyaJudul.Kolom.Count == 2 && hanyaJudul.Baris.Count == 0,
               hanyaJudul.Kolom.Count + "/" + hanyaJudul.Baris.Count);

            // CRLF: keluaran mysql.exe di Windows memang berakhiran CRLF - terlihat
            // saat keluarannya diperiksa dengan cat -A. Tanpa penanganan ini, tiap
            // sel terakhir akan membawa carriage return yang tak kasatmata.
            var crlf = MySqlKlien.Urai("a\tb\r\n1\t2\r\n");
            Ok("Akhiran baris CRLF tidak menempel ke sel terakhir",
               crlf.Baris.Count == 1 && crlf.Baris[0][1] == "2",
               crlf.Baris.Count > 0 ? "[" + crlf.Baris[0][1] + "]" : "kosong");

            // Baris yang selnya kurang tidak boleh melempar - keluaran terpotong
            // karena batas waktu akan tampak persis seperti ini.
            var pendek = MySqlKlien.Urai("a\tb\tc\n1\t2\n");
            Ok("Baris yang kurang selnya tidak meledak",
               pendek.Baris.Count == 1 && pendek.Baris[0].Length == 3 && pendek.Baris[0][2] == null);
        }

        static void UjiSqlLepasLolos()
        {
            Bagian("Melepas pelolosan aksara");

            Ok("TAB dikembalikan", MySqlKlien.LepasLolos("a\\tb") == "a\tb");
            Ok("Ganti baris dikembalikan", MySqlKlien.LepasLolos("a\\nb") == "a\nb");

            // Ini yang paling mudah salah. Jalur Windows C:\temp dikirim MySQL
            // sebagai C:\\temp - dibuktikan dengan HEX() ke server sungguhan.
            // Kalau penggandaan itu tidak dilepas lebih dulu, \t di dalam jalur
            // akan dibaca sebagai TAB dan jalurnya berubah jadi "C:" + tab + "emp".
            Ok("Garis miring terbalik ganda jadi satu, bukan aksara kendali",
               MySqlKlien.LepasLolos("C:\\\\temp") == "C:\\temp",
               MySqlKlien.LepasLolos("C:\\\\temp"));

            Ok("Teks tanpa pelolosan dibiarkan", MySqlKlien.LepasLolos("biasa saja") == "biasa saja");
            Ok("Teks kosong tetap kosong", MySqlKlien.LepasLolos("") == "");
            Ok("null tetap null", MySqlKlien.LepasLolos(null) == null);

            // Dulu \N diperlakukan sebagai NULL. Itu KELIRU: mysql.exe hanya
            // memakai \N pada SELECT ... INTO OUTFILE, tidak pada mode --batch.
            // Diperiksa ke MySQL 5.7, yang menulis kata NULL apa adanya.
            Ok("Garis miring N bukan penanda NULL, jadi tidak ditelan",
               MySqlKlien.LepasLolos("\\N") == "N",
               "[" + (MySqlKlien.LepasLolos("\\N") ?? "null") + "]");
        }

        static void UjiSqlKutip()
        {
            Bagian("Mengutip nama dan teks");

            Ok("Nama biasa dikutip balik", MySqlKlien.Kutip("pelanggan") == "`pelanggan`");

            // Nama basis data datang dari kotak ketik. Tanpa penggandaan ini,
            // nama berisi backtick bisa menutup kutipan lebih awal dan sisanya
            // ikut dijalankan sebagai SQL.
            Ok("Backtick di dalam nama digandakan",
               MySqlKlien.Kutip("aneh`nama") == "`aneh``nama`",
               MySqlKlien.Kutip("aneh`nama"));

            Ok("Kutip tunggal di dalam teks digandakan",
               MySqlKlien.KutipTeks("d'Angelo") == "'d''Angelo'",
               MySqlKlien.KutipTeks("d'Angelo"));
            Ok("Garis miring terbalik di dalam teks digandakan",
               MySqlKlien.KutipTeks("c:\\x") == "'c:\\\\x'",
               MySqlKlien.KutipTeks("c:\\x"));
        }

        static void UjiRahasia()
        {
            Bagian("Menyandi sandi");

            const string sandi = "rahasia-ku-123";
            var tertutup = Rahasia.Tutup(sandi);

            // Syarat yang sesungguhnya: sandi TIDAK boleh bisa dibaca dari
            // berkasnya. phoron.ini ikut tersalin ke mana-mana, dan Phoron sudah
            // pernah membuang token GitHub dari sana karena alasan yang sama.
            Ok("Hasilnya tidak memuat sandi aslinya",
               tertutup.IndexOf(sandi, StringComparison.Ordinal) < 0);
            Ok("Hasilnya bukan teks kosong", tertutup.Length > 0);
            Ok("Bisa dibuka kembali utuh", Rahasia.Buka(tertutup) == sandi,
               Rahasia.Buka(tertutup));

            Ok("Kosong tetap kosong", Rahasia.Tutup("") == "");
            Ok("null jadi kosong", Rahasia.Tutup(null) == "");

            // Nilai dari mesin lain, atau berkas yang rusak, adalah keadaan yang
            // WAJAR - misalnya setelah folder Phoron disalin ke komputer lain.
            // Itu tidak boleh membuat Phoron gagal start.
            Ok("Nilai yang tidak masuk akal jadi kosong, bukan melempar",
               Rahasia.Buka("ini-bukan-base64-yang-sah!!") == "");
            Ok("Base64 sah tapi bukan milik kita jadi kosong",
               Rahasia.Buka("aGFsbyBkdW5pYQ==") == "");

            Ok("Aksara di luar ASCII selamat",
               Rahasia.Buka(Rahasia.Tutup("sandi-ñ-日本-🔒")) == "sandi-ñ-日本-🔒");
        }

        /// <summary>
        /// Sandi basis data harus sampai ke phoron.ini dalam keadaan tersandi,
        /// dan kembali utuh saat dimuat. Diperiksa terhadap berkas sungguhan,
        /// bukan terhadap objek di memori: yang dikhawatirkan justru apa yang
        /// TERTULIS di cakram.
        /// </summary>
        static void UjiSetelanBasisData(string sandbox)
        {
            Bagian("Setelan basis data");

            var dir = Path.Combine(sandbox, "db-setelan");
            Directory.CreateDirectory(dir);
            var dulu = Paths.Root;
            try
            {
                Paths.Root = dir;

                var s = Settings.Load();
                Ok("Pengguna bawaan adalah root", s.DbPengguna == "root", s.DbPengguna);
                Ok("Sandi bawaan kosong", s.DbSandi == "", "[" + s.DbSandi + "]");

                s.DbPengguna = "pengembang";
                s.DbSandi = "sandi-rahasia-999";
                s.Save();

                var isi = File.ReadAllText(Paths.SettingsFile);
                Ok("Pengguna tertulis sebagai teks biasa",
                   isi.Contains("pengguna=pengembang"));
                Ok("Sandi TIDAK tertulis sebagai teks biasa",
                   isi.IndexOf("sandi-rahasia-999", StringComparison.Ordinal) < 0,
                   "sandi terbaca di phoron.ini");

                var lagi = Settings.Load();
                Ok("Pengguna terbaca kembali", lagi.DbPengguna == "pengembang", lagi.DbPengguna);
                Ok("Sandi terbaca kembali utuh", lagi.DbSandi == "sandi-rahasia-999", lagi.DbSandi);
            }
            finally
            {
                Paths.Root = dulu;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// my.ini harus mengikuti profilnya, bukan tertinggal di belakangnya.
        ///
        /// Ini menirukan persis keadaan yang ditemukan di mesin penulis: my.ini
        /// bertanggal enam hari lebih tua daripada berkas profilnya, dengan
        /// basedir yang masih menunjuk pemasangan MariaDB milik XAMPP padahal
        /// yang berjalan MySQL. Akibatnya server memuat katalog pesan galat
        /// milik MariaDB, nomor galatnya tidak cocok, dan SETIAP kesalahan SQL
        /// dijawab "Unknown error 1146" tanpa keterangan apa pun - persis pada
        /// saat orang paling butuh keterangan.
        /// </summary>
        static void UjiMyIniMengikutiProfil(string sandbox)
        {
            Bagian("my.ini mengikuti profil");

            var dir = Path.Combine(sandbox, "myini");
            var dulu = Paths.Root;
            try
            {
                Directory.CreateDirectory(dir);
                Paths.Root = dir;

                var bin = Path.Combine(dir, "binpalsu");
                BuatPaket(bin, Path.Combine("php", "php-8.3.12-Win32-vs16-x64"), "php.exe");
                BuatPaket(bin, Path.Combine("mysql", "mysql-5.7.38-winx64"),
                          Path.Combine("bin", "mysqld.exe"));
                BuatPaket(bin, Path.Combine("mysql", "mariadb-10.1.38-winx64"),
                          Path.Combine("bin", "mysqld.exe"));

                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine +
                    "bin_roots=" + bin + Environment.NewLine +
                    "profil_aktif=kerja" + Environment.NewLine +
                    "kelola_hosts=0" + Environment.NewLine +
                    "auto_vhost=0" + Environment.NewLine);

                Directory.CreateDirectory(Paths.Profiles);
                var berkasProfil = Path.Combine(Paths.Profiles, "kerja.ini");
                File.WriteAllText(berkasProfil,
                    "[profil]" + Environment.NewLine +
                    "nama=Kerja" + Environment.NewLine +
                    "php=php-8.3.12-Win32-vs16-x64" + Environment.NewLine +
                    "web_server=apache" + Environment.NewLine +
                    "apache=" + Environment.NewLine +
                    "mysql=mariadb-10.1.38-winx64" + Environment.NewLine +
                    "port_mysql=3306" + Environment.NewLine);

                var e = new Engine();
                e.Reload();
                e.Apply();

                var myIni = Path.Combine(Paths.EtcMysql, "my.ini");
                Ok("my.ini tertulis", File.Exists(myIni));
                var isi = File.ReadAllText(myIni);
                Ok("basedir menunjuk paket yang disebut profil",
                   isi.Contains("mariadb-10.1.38-winx64"), BarisIni(isi, "basedir"));

                // Versinya diganti, persis seperti orang mengubahnya di layar
                // Profil. Sesudah itu my.ini TIDAK BOLEH lagi menyebut yang lama:
                // mysqld dijalankan dari paket baru tapi membaca berkas ini, dan
                // basedir yang menunjuk paket lain adalah sumber "Unknown error".
                e.Active.MySqlId = "mysql-5.7.38-winx64";
                e.Apply();

                isi = File.ReadAllText(myIni);
                Ok("basedir ikut berubah saat versinya diganti",
                   isi.Contains("mysql-5.7.38-winx64"), BarisIni(isi, "basedir"));
                Ok("basedir tidak lagi menyebut paket yang lama",
                   !isi.Contains("mariadb-10.1.38-winx64"), BarisIni(isi, "basedir"));

                // datadir juga: tabel sistem MySQL 5.7 dan MariaDB 10.1 tidak
                // saling baca, jadi folder data yang tertukar berarti server
                // menolak start - atau, lebih buruk, start dengan data asing.
                Ok("datadir ikut paket, bukan tertinggal",
                   isi.Contains("mysql-5.7.38-winx64") && !isi.Contains("mariadb"),
                   BarisIni(isi, "datadir"));

                // Port juga datang dari profil, dan ini yang paling sering diubah.
                e.Active.MySqlPort = 3310;
                e.Apply();
                isi = File.ReadAllText(myIni);
                Ok("port ikut berubah", isi.Contains("port=3310"));
            }
            finally
            {
                Paths.Root = dulu;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// Menyunting hanya boleh terjadi kalau barisnya bisa ditunjuk DENGAN PASTI.
        ///
        /// Tanpa kunci utama, satu-satunya cara menunjuk baris adalah mencocokkan
        /// seluruh nilainya - dan pada tabel yang punya baris kembar, itu akan
        /// mengubah baris yang salah tanpa ada yang tahu. Menolak lebih jujur
        /// daripada menebak.
        /// </summary>
        static void UjiSuntingMenolakTanpaKunci()
        {
            Bagian("Menyunting tanpa kunci utama");

            var baris = new Dictionary<string, MySqlSunting.Sel>
            {
                { "a", new MySqlSunting.Sel { Teks = "1" } },
            };

            var u = MySqlSunting.UbahSel(null, "db", "t", new List<string>(), baris, "a", "2", false);
            Ok("Ubah tanpa kunci utama ditolak", !u.Ok && u.Galat.Length > 0, u.Galat);
            Ok("Ubah tanpa kunci utama tidak menyusun SQL apa pun", u.Sql == "", u.Sql);

            var d = MySqlSunting.HapusBaris(null, "db", "t", null, baris);
            Ok("Hapus tanpa kunci utama ditolak", !d.Ok && d.Galat.Length > 0, d.Galat);
            Ok("Hapus tanpa kunci utama tidak menyusun SQL apa pun", d.Sql == "", d.Sql);

            // Kunci yang nilainya kosong sama saja tidak menunjuk apa-apa.
            var kosong = new Dictionary<string, MySqlSunting.Sel>
            {
                { "id", new MySqlSunting.Sel { Kosong = true } },
            };
            var u2 = MySqlSunting.UbahSel(null, "db", "t", new List<string> { "id" }, kosong,
                                          "a", "2", false);
            Ok("Kunci yang nilainya kosong ditolak", !u2.Ok, u2.Galat);
        }

        static void UjiBarisJadiInsert()
        {
            Bagian("Baris jadi INSERT");

            var kolom = new List<string> { "id", "nama", "catatan" };
            var baris = new[]
            {
                new MySqlSunting.Sel { Teks = "7" },
                new MySqlSunting.Sel { Teks = "d'Angelo" },
                new MySqlSunting.Sel { Kosong = true },
            };

            var sql = MySqlSunting.BarisJadiInsert("orang", kolom, baris);
            Ok("Nama tabel dan kolom dikutip balik",
               sql.Contains("`orang`") && sql.Contains("`nama`"), sql);
            // Tanpa penggandaan ini, nilai berisi tanda kutip menutup literalnya
            // lebih awal dan sisanya ikut dijalankan sebagai SQL.
            Ok("Kutip di dalam nilai digandakan", sql.Contains("'d''Angelo'"), sql);
            // Sel yang kosong harus jadi NULL, bukan menjadi teks "NULL" -
            // pembedaan ini baru mungkin karena kekosongan dibaca sebagai fakta.
            Ok("Sel kosong jadi NULL tanpa kutip",
               sql.Contains(", NULL)") && !sql.Contains("'NULL'"), sql);
        }

        static void UjiCsvBasisData()
        {
            Bagian("CSV basis data");

            var kolom = new List<string> { "id", "nama", "catatan", "kosong" };
            var baris = new List<MySqlSunting.Sel[]>
            {
                new[]
                {
                    new MySqlSunting.Sel { Teks = "1" },
                    new MySqlSunting.Sel { Teks = "Budi, Santoso" },
                    new MySqlSunting.Sel { Kosong = true },
                    new MySqlSunting.Sel { Teks = "" },
                },
                new[]
                {
                    new MySqlSunting.Sel { Teks = "2" },
                    new MySqlSunting.Sel { Teks = "dia bilang \"halo\"" },
                    new MySqlSunting.Sel { Teks = "baris\nkedua" },
                    new MySqlSunting.Sel { Teks = "NULL" },
                },
            };

            var csv = MySqlSunting.Csv(kolom, baris);
            Ok("Judul kolom ikut tertulis", csv.StartsWith("id,nama,catatan,kosong"), csv.Substring(0, 30));
            Ok("Koma di dalam nilai membuatnya dikutip", csv.Contains("\"Budi, Santoso\""), csv);
            Ok("Kutip ganda di dalam nilai digandakan",
               csv.Contains("\"dia bilang \"\"halo\"\"\""), csv);
            Ok("Ganti baris di dalam nilai membuatnya dikutip",
               csv.Contains("\"baris\nkedua\""), csv);

            // Inilah satu-satunya cara CSV membedakan keduanya, dan pembedaan itu
            // baru ada artinya karena kekosongan dibaca dari kolom pendamping
            // "IS NULL", bukan dikira-kira dari tulisannya.
            Ok("Sel kosong ditulis tanpa apa pun", csv.Contains(",,\"\""), csv);
            Ok("Teks kosong ditulis sebagai sepasang kutip", csv.Contains("\"\""), csv);
            Ok("Teks berisi kata NULL tetap tertulis", csv.Contains("NULL"), csv);
        }

        static void UjiRangkaInsert()
        {
            Bagian("Rangka INSERT");

            var kolom = new List<MySqlSkema.InfoKolom>
            {
                new MySqlSkema.InfoKolom { Nama = "id", Jenis = "int", Ekstra = "auto_increment",
                                           Kosong = Lang.T("tidak") },
                new MySqlSkema.InfoKolom { Nama = "nama", Jenis = "varchar(50)", Ekstra = "",
                                           Kosong = Lang.T("tidak") },
                new MySqlSkema.InfoKolom { Nama = "catatan", Jenis = "text", Ekstra = "",
                                           Kosong = Lang.T("ya") },
            };

            var sql = MySqlSunting.RangkaInsert("orang", kolom);
            // Kolom auto_increment sengaja tidak disebut: menyebutnya memaksa orang
            // mengarang nilai untuk sesuatu yang justru tugas server mengisinya.
            Ok("Kolom auto_increment tidak ikut disebut", !sql.Contains("`id`"), sql);
            Ok("Kolom biasa ikut disebut", sql.Contains("`nama`") && sql.Contains("`catatan`"), sql);
            Ok("Kolom yang boleh kosong diberi NULL", sql.Contains("NULL"), sql);
            Ok("Perintahnya diakhiri titik koma", sql.TrimEnd().EndsWith(";"), sql);

            Ok("Tabel tanpa kolom tidak menghasilkan apa pun",
               MySqlSunting.RangkaInsert("t", new List<MySqlSkema.InfoKolom>()) == "");
        }

        /// <summary>
        /// Tombol yang MERUSAK tidak boleh berbagi label dengan tombol yang tidak.
        ///
        /// Ditemukan saat memandangi tangkapan layar tema gelap: tombol yang
        /// menjalankan TRUNCATE TABLE diberi label "Kosongkan", dan kata itu
        /// sudah dipakai halaman Log untuk membersihkan TAMPILAN - padanan
        /// Inggrisnya "Clear". Jadi dalam bahasa Inggris tombol yang membuang
        /// seluruh isi tabel dan tombol yang cuma menyapu layar bertuliskan sama
        /// persis, bersebelahan di layar yang sama.
        ///
        /// Ini jenis cacat yang tidak akan tertangkap uji terjemahan mana pun:
        /// kedua padanan benar, hanya tidak boleh bertemu.
        /// </summary>
        static void UjiLabelTidakBentrok()
        {
            Bagian("Label perbuatan merusak");

            // Kiri merusak, kanan tidak. Tidak boleh sama di bahasa mana pun.
            var pasangan = new[]
            {
                new[] { "Kosongkan tabel", "Kosongkan" },
                new[] { "Kosongkan tabel", "Bersihkan" },
                new[] { "Hapus tabel", "Bersihkan" },
                new[] { "Hapus basis data", "Bersihkan" },
            };

            var dulu = Lang.Kode;
            try
            {
                foreach (var kode in Lang.Semua)
                {
                    Lang.Pakai(kode);
                    foreach (var p in pasangan)
                    {
                        var rusak = Lang.T(p[0]);
                        var aman = Lang.T(p[1]);
                        Ok("[" + kode + "] \"" + p[0] + "\" beda dari \"" + p[1] + "\"",
                           !string.Equals(rusak, aman, StringComparison.OrdinalIgnoreCase),
                           rusak + " vs " + aman);
                    }
                }
            }
            finally { Lang.Pakai(dulu); }
        }

        /// <summary>
        /// Seluruh padanan Banjar diperiksa, bukan hanya yang muncul di XAML.
        ///
        /// Penjaga yang sudah ada memindai berkas XAML dan menerjemahkan tiap
        /// kunci yang ditemukannya. Itu meninggalkan LUBANG yang sudah terbukti:
        /// kalimat yang hanya dipanggil dari kode - pesan galat, kotak tanya,
        /// keterangan halaman - tidak pernah ikut diperiksa. Seluruh halaman
        /// Basis data ditulis lewat jalan itu, dan lima kalimat Banjarnya lolos
        /// membawa "halaman", "berkas", dan "hasil" di dalamnya.
        ///
        /// Yang dibaca di sini kamusnya sendiri, dari berkas sumbernya. Lewat
        /// Lang.T tidak bisa: Lang.T menuntut kuncinya lebih dulu, dan justru
        /// daftar kunci lengkap itulah yang tidak dipegang siapa pun.
        /// </summary>
        static void UjiKamusBanjar()
        {
            Bagian("Kamus Banjar");

            var padanan = PadananBanjar();
            Ok("Kamus Banjar terbaca dari sumbernya", padanan.Count > 100,
               padanan.Count + " pasangan");

            var bocor = new List<string>();
            foreach (var p in padanan)
            {
                // Padanan yang sama persis dengan kuncinya memang disengaja -
                // "Buka www" dalam bahasa Banjar ya "Buka www".
                if (p.Key == p.Value) continue;
                foreach (var w in KataTugasIndonesia)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            p.Value, "(?<![A-Za-z])" + w + "(?![A-Za-z])",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;
                    bocor.Add("\"" + w + "\" di: "
                              + p.Value.Substring(0, Math.Min(60, p.Value.Length)));
                    break;
                }
            }
            Ok("Tidak ada kata tugas Indonesia di seluruh kamus Banjar",
               bocor.Count == 0, string.Join(" | ", bocor.ToArray()));
        }

        /// <summary>Pasangan kunci-nilai bagian Banjar, dibaca dari Lang.cs.</summary>
        static List<KeyValuePair<string, string>> PadananBanjar()
        {
            var hasil = new List<KeyValuePair<string, string>>();
            var berkas = Path.Combine(AkarRepo(), "src", "Phoron.Core", "Lang.cs");
            if (!File.Exists(berkas)) return hasil;

            var isi = File.ReadAllText(berkas);
            var mulai = isi.IndexOf("static Dictionary<string, string> Banjar_()",
                                    StringComparison.Ordinal);
            if (mulai < 0) return hasil;

            // Satu literal string C#, termasuk bentuk @"..." dan yang memuat
            // tanda kutip yang diloloskan.
            const string lit = "@?\"((?:[^\"\\\\]|\\\\.)*)\"";
            var pola = "\\{\\s*" + lit + "\\s*,\\s*" + lit + "\\s*\\}";
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         isi.Substring(mulai), pola,
                         System.Text.RegularExpressions.RegexOptions.Singleline))
                hasil.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            return hasil;
        }

        /// <summary>Akar repo, dicari naik dari folder biner uji.</summary>
        static string AkarRepo()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
                dir = dir.Parent;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>Baris my.ini yang diawali kunci tertentu - dipakai sebagai keterangan saat uji gagal.</summary>
        static string BarisIni(string isi, string kunci)
        {
            foreach (var b in (isi ?? "").Split('\n'))
                if (b.Trim().StartsWith(kunci, StringComparison.OrdinalIgnoreCase)) return b.Trim();
            return "(tidak ada " + kunci + ")";
        }

        /// <summary>
        /// Sambungan hanya dibuat kalau memang ada yang bisa disambungi. Tiap
        /// penolakan di sini adalah satu pesan halangan yang berbeda di layar,
        /// jadi yang diuji adalah bahwa sebabnya benar-benar dibedakan.
        /// </summary>
        static void UjiSambunganBasisData(string sandbox)
        {
            Bagian("Sambungan basis data");

            var dir = Path.Combine(sandbox, "db-sambung");
            Directory.CreateDirectory(dir);
            var dulu = Paths.Root;
            try
            {
                Paths.Root = dir;
                var e = new Engine();

                Ok("Tanpa profil aktif tidak ada sambungan", MySqlKlien.Untuk(e) == null);
                Ok("Engine null tidak meledak", MySqlKlien.Untuk(null) == null);

                // Perintah apa pun tanpa sambungan harus menghasilkan GALAT yang
                // bisa dibaca, bukan pengecualian dan bukan hasil kosong yang
                // menyamar sebagai keberhasilan.
                var h = MySqlKlien.Jalankan(null, "SELECT 1");
                Ok("Kueri tanpa sambungan melaporkan galat", !h.Ok && h.Galat.Length > 0, h.Galat);
                Ok("Kueri tanpa sambungan tidak mengembalikan tabel kosong palsu",
                   h.Tabel == null);

                Ok("Ekspor tanpa sambungan melaporkan galat",
                   MySqlKlien.Ekspor(null, "apa", Path.Combine(dir, "x.sql")) != null);
                Ok("Impor berkas yang tidak ada melaporkan galat",
                   MySqlKlien.Impor(new MySqlKlien.Sambungan(), "apa",
                                    Path.Combine(dir, "tidak-ada.sql")) != null);
            }
            finally
            {
                Paths.Root = dulu;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
