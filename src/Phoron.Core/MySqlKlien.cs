using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Bicara dengan MySQL lewat klien baris perintahnya sendiri, mysql.exe,
    /// yang sudah ikut di tiap paket MySQL/MariaDB yang dipasang Phoron.
    ///
    /// KENAPA BUKAN PUSTAKA PENGHUBUNG. Phoron.Core tidak punya satu pun
    /// dependensi pihak ketiga, dan itu disengaja: setiap pustaka yang masuk
    /// ikut jadi tanggungan keamanan dan ikut membengkakkan installer. Klien
    /// resminya sudah ada di cakram, sudah pasti cocok dengan versi servernya -
    /// termasuk MariaDB, yang protokolnya sudah tidak sepenuhnya sama dengan
    /// MySQL 8 - dan sudah dipakai LiveTest untuk membuktikan server menjawab.
    ///
    /// BENTUK KELUARAN YANG DIPAKAI adalah mode --batch: satu baris judul kolom,
    /// lalu satu baris per baris data, dipisah TAB. Ini bukan tebakan atas
    /// keluaran manusiawi; ini bentuk yang memang disediakan MySQL untuk dibaca
    /// mesin, dan ia MELOLOSKAN aksara yang bisa mengacaukan pembacaan: TAB jadi
    /// \t, ganti baris jadi \n, garis miring terbalik jadi \\. Diperiksa langsung
    /// ke MySQL 5.7 milik mesin ini: HEX() membuktikan nilai C:\temp benar-benar
    /// keluar sebagai C:\\temp, jadi pelolosan itu memang terjadi.
    ///
    /// SATU HAL YANG TIDAK BISA DIBEDAKAN, dan ini perlu diketahui: NULL ditulis
    /// sebagai kata NULL, sama persis dengan nilai teks yang isinya kata "NULL".
    /// Klien baris perintah memang tidak membedakan keduanya - \N hanya dipakai
    /// SELECT ... INTO OUTFILE, bukan mode --batch. Phoron TIDAK menebak mana
    /// yang mana: keduanya ditampilkan apa adanya, persis seperti yang akan
    /// dilihat orang kalau mengetik kueri itu sendiri di terminal. Menerkanya
    /// berarti sesekali memberi tahu orang bahwa datanya kosong padahal berisi.
    /// </summary>
    public static class MySqlKlien
    {
        /// <summary>Berapa lama sebuah kueri boleh berjalan sebelum dipotong.</summary>
        public const int BatasWaktuMs = 60000;

        // ------------------------------------------------------------- Sambungan

        public sealed class Sambungan
        {
            public string Klien;        // jalur lengkap mysql.exe
            public string Dump;         // jalur lengkap mysqldump.exe (boleh kosong)
            public string KerjaDi;
            public int Port = 3306;
            public string Pengguna = "root";
            public string Sandi = "";
        }

        /// <summary>
        /// Sambungan untuk profil yang sedang aktif, atau null bila profil ini
        /// memang tidak memakai basis data - atau paketnya tidak punya klien.
        /// </summary>
        public static Sambungan Untuk(Engine e)
        {
            if (e == null || e.Active == null || !e.Active.PakaiMySql) return null;
            var paket = e.MySql;
            if (paket == null) return null;
            var klien = Path.Combine(paket.Path, "bin", "mysql.exe");
            if (!File.Exists(klien)) return null;
            var dump = Path.Combine(paket.Path, "bin", "mysqldump.exe");
            return new Sambungan
            {
                Klien = klien,
                Dump = File.Exists(dump) ? dump : "",
                KerjaDi = paket.Path,
                Port = e.Active.MySqlPort,
                Pengguna = string.IsNullOrEmpty(e.Settings.DbPengguna) ? "root" : e.Settings.DbPengguna,
                Sandi = e.Settings.DbSandi ?? "",
            };
        }

        // ----------------------------------------------------------------- Hasil

        public sealed class Tabel
        {
            public List<string> Kolom = new List<string>();
            public List<string[]> Baris = new List<string[]>();
        }

        public sealed class Hasil
        {
            public Tabel Tabel;             // null bila perintahnya tidak mengembalikan baris
            public string Galat;            // null bila berhasil
            public long Terpengaruh = -1;   // jumlah baris yang berubah, -1 bila tak berlaku
            public long Ms;
            public bool Dipotong;           // baris yang ditampilkan dibatasi
            public bool Ok { get { return Galat == null; } }
        }

        // -------------------------------------------------------------- Menjalankan

        /// <summary>
        /// Jalankan SATU perintah SQL. Untuk teks yang memuat beberapa perintah,
        /// pakai <see cref="Pisah"/> lebih dulu - keluaran --batch dari beberapa
        /// perintah menempel jadi satu tanpa penanda, sehingga baris judul kolom
        /// perintah kedua tidak bisa dibedakan dari data.
        /// </summary>
        public static Hasil Jalankan(Sambungan s, string sql, string basisData = null,
                                     int batasBaris = 0)
        {
            var hasil = new Hasil();
            if (s == null) { hasil.Galat = Lang.T("Profil ini tidak memakai basis data."); return hasil; }
            if (string.IsNullOrWhiteSpace(sql)) { hasil.Tabel = new Tabel(); return hasil; }

            var berbaris = Berbaris(sql);
            var kirim = new StringBuilder();
            if (!string.IsNullOrEmpty(basisData))
                kirim.AppendLine("USE " + Kutip(basisData) + ";");
            kirim.AppendLine(sql.TrimEnd().TrimEnd(';') + ";");
            // Perintah yang tidak mengembalikan baris tidak melaporkan apa pun di
            // mode --batch. ROW_COUNT() ditanyakan di sambungan YANG SAMA - kalau
            // ditanyakan lewat proses terpisah, nilainya sudah kembali ke awal.
            if (!berbaris) kirim.AppendLine("SELECT ROW_COUNT();");

            var mulai = DateTime.UtcNow;
            var r = Panggil(s, kirim.ToString());
            hasil.Ms = (long)(DateTime.UtcNow - mulai).TotalMilliseconds;

            if (r.TimedOut)
            {
                hasil.Galat = Lang.T("Kueri dihentikan karena sudah lewat batas waktu.");
                return hasil;
            }
            if (!r.Ok)
            {
                hasil.Galat = RapikanGalat(r.StdErr, r.StdOut);
                return hasil;
            }

            var tabel = Urai(r.StdOut);
            if (!berbaris)
            {
                long n;
                if (tabel.Baris.Count == 1 && tabel.Baris[0].Length == 1
                    && long.TryParse(tabel.Baris[0][0], NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out n))
                    hasil.Terpengaruh = n;
                return hasil;
            }

            if (batasBaris > 0 && tabel.Baris.Count > batasBaris)
            {
                tabel.Baris.RemoveRange(batasBaris, tabel.Baris.Count - batasBaris);
                hasil.Dipotong = true;
            }
            hasil.Tabel = tabel;
            return hasil;
        }

        // Daftar basis data, daftar tabel, dan InfoTabel dulu ada di sini.
        // Dipindah ke MySqlSkema, yang juga membawa ukuran, mesin, dan kolasi -
        // meninggalkan dua jalan untuk hal yang sama berarti cepat atau lambat
        // ada layar yang memakai yang lebih miskin tanpa alasan.

        // ------------------------------------------------------------- Pemanggilan

        static Shell.RunResult Panggil(Sambungan s, string stdin)
        {
            // Sandi TIDAK PERNAH lewat baris perintah. Di Windows baris perintah
            // sebuah proses bisa dibaca proses lain mana pun di sesi yang sama -
            // Task Manager pun menampilkannya - jadi -pRAHASIA berarti sandinya
            // terpampang. Berkas setelan sementara ini hanya hidup selama satu
            // pemanggilan dan dihapus sesudahnya.
            string berkas = null;
            try
            {
                berkas = TulisBerkasSetelan(s);
                var arg = new StringBuilder();
                if (berkas != null) arg.Append("--defaults-file=\"").Append(berkas).Append("\" ");
                else arg.Append("--no-defaults ");
                arg.Append("--protocol=tcp --host=127.0.0.1 --port=").Append(s.Port).Append(' ');
                arg.Append("--user=\"").Append(s.Pengguna.Replace("\"", "")).Append("\" ");
                // --batch: keluaran berpemisah TAB yang meloloskan aksara khusus.
                // --column-names: baris judul selalu ada, juga saat hasilnya kosong.
                // --binary-as-hex dihindari: build lama tidak mengenalnya dan langsung menolak jalan.
                arg.Append("--batch --column-names --default-character-set=utf8mb4 ");
                arg.Append("--connect-timeout=10");
                return Shell.Run(s.Klien, arg.ToString(), s.KerjaDi, BatasWaktuMs, null, stdin);
            }
            finally
            {
                if (berkas != null) { try { File.Delete(berkas); } catch { } }
            }
        }

        static string TulisBerkasSetelan(Sambungan s)
        {
            if (string.IsNullOrEmpty(s.Sandi)) return null;
            try
            {
                Directory.CreateDirectory(Paths.Tmp);
                var berkas = Path.Combine(Paths.Tmp,
                    "sandi-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-"
                    + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cnf");
                var isi = "[client]" + Environment.NewLine
                        + "password=\"" + s.Sandi.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                        + Environment.NewLine;
                File.WriteAllText(berkas, isi, new UTF8Encoding(false));
                return berkas;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Uraian

        /// <summary>
        /// Ubah keluaran --batch jadi tabel. Baris pertama adalah judul kolom.
        /// </summary>
        public static Tabel Urai(string keluaran)
        {
            var t = new Tabel();
            if (string.IsNullOrEmpty(keluaran)) return t;

            var baris = keluaran.Replace("\r\n", "\n").Replace('\r', '\n')
                                .Split('\n')
                                .Where(x => x.Length > 0)
                                .ToList();
            if (baris.Count == 0) return t;

            t.Kolom = baris[0].Split('\t').Select(LepasLolos).ToList();
            for (int i = 1; i < baris.Count; i++)
            {
                var sel = baris[i].Split('\t');
                var isi = new string[t.Kolom.Count];
                for (int k = 0; k < isi.Length; k++)
                    isi[k] = k < sel.Length ? LepasLolos(sel[k]) : null;
                t.Baris.Add(isi);
            }
            return t;
        }

        /// <summary>
        /// Kembalikan aksara yang diloloskan mode --batch ke bentuk aslinya.
        ///
        /// TIDAK mengubah kata NULL jadi null: di mode ini MySQL memakai kata
        /// yang sama untuk nilai kosong dan untuk teks yang isinya kata itu.
        /// Lihat catatan di kepala berkas.
        /// </summary>
        public static string LepasLolos(string sel)
        {
            if (sel == null) return null;
            if (sel.IndexOf('\\') < 0) return sel;

            var sb = new StringBuilder(sel.Length);
            for (int i = 0; i < sel.Length; i++)
            {
                if (sel[i] != '\\' || i + 1 >= sel.Length) { sb.Append(sel[i]); continue; }
                var n = sel[++i];
                switch (n)
                {
                    case '0': sb.Append('\0'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------- Pengutipan

        /// <summary>Nama basis data/tabel sebagai pengenal. Backtick di dalamnya digandakan.</summary>
        public static string Kutip(string pengenal)
        {
            return "`" + (pengenal ?? "").Replace("`", "``") + "`";
        }

        /// <summary>Teks sebagai literal SQL, untuk dibandingkan dengan kolom.</summary>
        public static string KutipTeks(string teks)
        {
            return "'" + (teks ?? "").Replace("\\", "\\\\").Replace("'", "''") + "'";
        }

        // -------------------------------------------------------- Impor dan ekspor

        /// <summary>
        /// Tulis seluruh isi sebuah basis data ke berkas .sql lewat mysqldump.
        ///
        /// Keluarannya diminta langsung ke berkas lewat --result-file, BUKAN
        /// ditangkap dari keluaran baku lalu disimpan Phoron. Dump basis data
        /// kerja gampang mencapai ratusan megabyte, dan menampungnya sebagai
        /// string di memori lebih dulu adalah cara yang pasti gagal justru pada
        /// basis data yang paling perlu dicadangkan.
        /// </summary>
        public static string Ekspor(Sambungan s, string db, string berkasTujuan)
        {
            if (s == null) return Lang.T("Profil ini tidak memakai basis data.");
            if (string.IsNullOrEmpty(s.Dump))
                return Lang.T("mysqldump tidak ada di paket MySQL profil ini.");

            string setelan = null;
            try
            {
                setelan = TulisBerkasSetelan(s);
                var arg = new StringBuilder();
                if (setelan != null) arg.Append("--defaults-file=\"").Append(setelan).Append("\" ");
                else arg.Append("--no-defaults ");
                arg.Append("--protocol=tcp --host=127.0.0.1 --port=").Append(s.Port).Append(' ');
                arg.Append("--user=\"").Append(s.Pengguna.Replace("\"", "")).Append("\" ");
                arg.Append("--default-character-set=utf8mb4 ");
                // --single-transaction: cadangan yang konsisten TANPA mengunci tabel,
                // jadi situs yang sedang dibuka tidak ikut membeku selama ekspor.
                arg.Append("--single-transaction --quick --routines --events ");
                arg.Append("--result-file=\"").Append(berkasTujuan).Append("\" ");
                arg.Append(Kutip(db).Replace("`", ""));

                var r = Shell.Run(s.Dump, arg.ToString(), s.KerjaDi, 30 * 60 * 1000);
                if (r.TimedOut) return Lang.T("Ekspor dihentikan karena sudah lewat batas waktu.");
                return r.Ok ? null : RapikanGalat(r.StdErr, r.StdOut);
            }
            catch (Exception ex) { return ex.Message; }
            finally { if (setelan != null) { try { File.Delete(setelan); } catch { } } }
        }

        /// <summary>
        /// Jalankan isi sebuah berkas .sql ke dalam satu basis data.
        ///
        /// Berkasnya disalurkan sebagai aliran, bukan dibaca jadi teks: berkas
        /// impor justru cenderung yang paling besar. Perintah di dalamnya tidak
        /// dipecah Phoron - mysql.exe sendiri yang menguraikannya, termasuk
        /// DELIMITER dan definisi prosedur yang tidak dimengerti pemisah mana pun
        /// yang lebih sederhana.
        /// </summary>
        public static string Impor(Sambungan s, string db, string berkasSumber)
        {
            if (s == null) return Lang.T("Profil ini tidak memakai basis data.");
            if (!File.Exists(berkasSumber)) return Lang.T("Berkasnya tidak ada.");

            string setelan = null;
            try
            {
                setelan = TulisBerkasSetelan(s);
                var arg = new StringBuilder();
                if (setelan != null) arg.Append("--defaults-file=\"").Append(setelan).Append("\" ");
                else arg.Append("--no-defaults ");
                arg.Append("--protocol=tcp --host=127.0.0.1 --port=").Append(s.Port).Append(' ');
                arg.Append("--user=\"").Append(s.Pengguna.Replace("\"", "")).Append("\" ");
                arg.Append("--default-character-set=utf8mb4 ");
                if (!string.IsNullOrEmpty(db)) arg.Append("--database=\"").Append(db.Replace("\"", "")).Append("\" ");
                arg.Append("--connect-timeout=10");

                using (var aliran = File.OpenRead(berkasSumber))
                {
                    var r = Shell.Run(s.Klien, arg.ToString(), s.KerjaDi,
                                      30 * 60 * 1000, null, null, aliran);
                    if (r.TimedOut) return Lang.T("Impor dihentikan karena sudah lewat batas waktu.");
                    return r.Ok ? null : RapikanGalat(r.StdErr, r.StdOut);
                }
            }
            catch (Exception ex) { return ex.Message; }
            finally { if (setelan != null) { try { File.Delete(setelan); } catch { } } }
        }

        // Pemisah perintah SQL dulu ada di sini, dipakai kotak SQL bawaan.
        // Kotak itu sudah dibuang - menulis SQL sekarang di HeidiSQL, yang punya
        // penyunting lengkap berikut penyorotan sintaksnya. Berbaris tetap
        // tinggal: Jalankan memakainya untuk memutuskan apakah perlu menanyakan
        // ROW_COUNT().

        /// <summary>
        /// Apakah perintah ini mengembalikan baris? Ditentukan dari kata
        /// pertamanya, setelah komentar di depan dibuang.
        /// </summary>
        public static bool Berbaris(string sql)
        {
            var t = (sql ?? "").TrimStart();
            // Komentar di depan dibuang dulu, kalau tidak "/* catatan */ SELECT"
            // dikira perintah yang tidak mengembalikan baris.
            while (true)
            {
                if (t.StartsWith("/*"))
                {
                    var tutup = t.IndexOf("*/", StringComparison.Ordinal);
                    if (tutup < 0) return false;
                    t = t.Substring(tutup + 2).TrimStart();
                    continue;
                }
                if (t.StartsWith("--") || t.StartsWith("#"))
                {
                    var baris = t.IndexOf('\n');
                    if (baris < 0) return false;
                    t = t.Substring(baris + 1).TrimStart();
                    continue;
                }
                break;
            }
            if (t.StartsWith("(")) return true;   // (SELECT ...) UNION (SELECT ...)

            var kata = new string(t.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
            switch (kata)
            {
                case "SELECT": case "SHOW": case "DESCRIBE": case "DESC":
                case "EXPLAIN": case "WITH": case "CALL": case "ANALYZE":
                case "CHECK": case "CHECKSUM": case "OPTIMIZE": case "REPAIR":
                case "HELP": case "TABLE": case "VALUES":
                    return true;
                default:
                    return false;
            }
        }

        // ----------------------------------------------------------------- Galat

        /// <summary>
        /// Buang derau khas klien baris perintah dari pesan galat, supaya yang
        /// sampai ke layar cuma keberatan MySQL yang sesungguhnya.
        /// </summary>
        static string RapikanGalat(string stderr, string stdout)
        {
            var teks = (stderr ?? "").Trim();
            if (teks.Length == 0) teks = (stdout ?? "").Trim();
            if (teks.Length == 0) return Lang.T("Perintah gagal tanpa keterangan.");

            var baris = teks.Replace("\r\n", "\n").Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                // Peringatan ini muncul di hampir tiap pemanggilan berkas setelan
                // dan tidak ada hubungannya dengan kueri yang barusan dijalankan.
                .Where(x => x.IndexOf("Using a password on the command line",
                                      StringComparison.OrdinalIgnoreCase) < 0)
                .Select(x => x.StartsWith("ERROR ", StringComparison.Ordinal)
                             ? BuangNomorGalat(x) : x)
                .ToList();
            return baris.Count == 0 ? teks : string.Join(Environment.NewLine, baris);
        }

        static string BuangNomorGalat(string baris)
        {
            // "ERROR 1064 (42000) at line 2: You have an error..." -> isi setelah ":"
            var titik = baris.IndexOf(": ", StringComparison.Ordinal);
            return titik > 0 && titik + 2 < baris.Length ? baris.Substring(titik + 2) : baris;
        }
    }
}
