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
        /// Argumen yang dikirim ke HeidiSQL.
        ///
        /// Yang paling penting dijaga: SANDI TIDAK PERNAH IKUT. Di Windows, baris
        /// perintah sebuah proses bisa dibaca proses lain di sesi yang sama -
        /// Task Manager pun menampilkannya. Phoron sudah menolak menaruh sandi di
        /// baris perintah mysql.exe dan memakai berkas setelan sementara sebagai
        /// gantinya; menaruhnya di sini berarti membatalkan keputusan itu lewat
        /// pintu belakang, dan tidak akan ada yang menyadarinya.
        /// </summary>
        static void UjiArgumenHeidi()
        {
            Bagian("Argumen HeidiSQL");

            var arg = KlienLuar.ArgumenHeidi("PHP 8.3 + Apache", 3307, "root");
            Ok("Alamat selalu 127.0.0.1", arg.Contains("--host=\"127.0.0.1\""), arg);
            Ok("Port profil ikut dikirim", arg.Contains("--port=3307"), arg);
            Ok("Pengguna ikut dikirim", arg.Contains("--user=\"root\""), arg);

            // Nama sesi menyebut profilnya: orang yang punya beberapa profil perlu
            // tahu server mana yang sedang dibukanya.
            Ok("Nama sesi menyebut profilnya",
               arg.Contains("--description=\"Phoron: PHP 8.3 + Apache\""), arg);

            Ok("Sandi TIDAK pernah ikut",
               arg.IndexOf("--password", StringComparison.OrdinalIgnoreCase) < 0, arg);

            // Tanda kutip di dalam nama profil akan menutup argumennya lebih awal,
            // dan sisanya dibaca HeidiSQL sebagai argumen tersendiri.
            var nakal = KlienLuar.ArgumenHeidi("pro\"fil", 3306, "ro\"ot");
            Ok("Tanda kutip di dalam nama dibuang, bukan diteruskan",
               nakal.Split('"').Length % 2 == 1, nakal);

            var tanpaNama = KlienLuar.ArgumenHeidi("", 3306, null);
            Ok("Tanpa nama profil tetap punya nama sesi",
               tanpaNama.Contains("--description=\"Phoron\""), tanpaNama);
            Ok("Pengguna kosong jatuh ke root",
               tanpaNama.Contains("--user=\"root\""), tanpaNama);
        }

        /// <summary>
        /// Penyertaan HeidiSQL ke installer tidak boleh diam-diam rusak.
        ///
        /// Skrip pengambil menyiapkan folder, dan setup.iss merujuk folder itu.
        /// Kalau salah satunya berubah sendiri, installer TETAP terbangun - hanya
        /// isinya yang kurang, dan itu baru ketahuan setelah ada yang memasangnya.
        /// </summary>
        static void UjiPaketHeidi()
        {
            Bagian("Penyertaan HeidiSQL");

            var akar = AkarRepo();
            var skrip = Path.Combine(akar, "installer", "ambil_heidisql.ps1");
            var iss = Path.Combine(akar, "installer", "setup.iss");
            Ok("Skrip pengambil ada", File.Exists(skrip), skrip);
            Ok("setup.iss ada", File.Exists(iss), iss);
            if (!File.Exists(skrip) || !File.Exists(iss)) return;

            var isiSkrip = File.ReadAllText(skrip);
            var isiIss = File.ReadAllText(iss);

            // Versi dipatok, bukan "yang terbaru": isi installer tidak boleh
            // berubah tanpa ada yang memutuskannya.
            Ok("Versi HeidiSQL dipatok",
               System.Text.RegularExpressions.Regex.IsMatch(isiSkrip, @"\$Versi\s*=\s*'[\d.]+'"),
               "tidak ada $Versi tetap");

            // Sidik diperiksa: berkas yang tertukar di tengah jalan harus
            // menggagalkan build, bukan ikut terbungkus diam-diam.
            Ok("Sidik SHA-256 dipatok",
               System.Text.RegularExpressions.Regex.IsMatch(isiSkrip, @"\$Sidik\s*=\s*'[a-f0-9]{64}'"),
               "tidak ada $Sidik 64 aksara");
            Ok("Sidik benar-benar dibandingkan",
               isiSkrip.Contains("-ne $Sidik") && isiSkrip.Contains("throw"),
               "sidik dipatok tapi tidak diperiksa");

            // Kewajiban GPL: teks lisensi dan alamat sumbernya ikut terpasang.
            Ok("Keterangan sumber ikut ditulis", isiSkrip.Contains("HeidiSQL-SUMBER.txt"));
            Ok("Alamat kode sumber disebut", isiSkrip.Contains("github.com/HeidiSQL/HeidiSQL"));

            Ok("setup.iss memasang folder heidisql",
               isiIss.Contains("heidisql\\*") && isiIss.Contains("bin\\heidisql"));
            Ok("Komponen heidisql terdaftar", isiIss.Contains("Name: \"heidisql\";"));

            // Barang bawaan, bukan pekerjaan pengguna - harus ikut bersih saat
            // Phoron dicopot.
            var barisFiles = isiIss.Replace("\r\n", "\n").Split('\n')
                .FirstOrDefault(b => b.Contains("heidisql\\*"));
            Ok("Folder HeidiSQL tidak ditandai jangan-pernah-dicopot",
               barisFiles != null && !barisFiles.Contains("uninsneveruninstall"),
               barisFiles ?? "");
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
