using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Membaca BENTUK basis data - keterangan server, ukuran, kolom, indeks,
    /// dan halaman isi tabel. Dipisah dari <see cref="MySqlKlien"/> yang hanya
    /// mengurus cara bicara dengan mysql.exe dan cara membaca keluarannya.
    ///
    /// Seluruh isinya memakai MySqlKlien.Jalankan, jadi aturan yang sama tetap
    /// berlaku: memblokir utas pemanggilnya, dan tidak boleh dipanggil dari utas
    /// layar.
    /// </summary>
    public static class MySqlSkema
    {
        // ---------------------------------------------------------------- Server

        public sealed class InfoServer
        {
            public string Versi = "";
            public string Pengguna = "";
            public string Komentar = "";   // "MySQL Community Server" dan sejenisnya
            public int Port;
            public bool Ada { get { return Versi.Length > 0; } }

            /// <summary>Satu baris untuk bilah status.</summary>
            public string Ringkas
            {
                get
                {
                    var sb = new StringBuilder();
                    sb.Append(Komentar.IndexOf("MariaDB", StringComparison.OrdinalIgnoreCase) >= 0
                              || Versi.IndexOf("MariaDB", StringComparison.OrdinalIgnoreCase) >= 0
                              ? "MariaDB " : "MySQL ");
                    sb.Append(Versi);
                    sb.Append("  ·  port ").Append(Port);
                    if (Pengguna.Length > 0) sb.Append("  ·  ").Append(Pengguna);
                    return sb.ToString();
                }
            }
        }

        public static InfoServer Server(MySqlKlien.Sambungan s, out string galat)
        {
            galat = null;
            var info = new InfoServer { Port = s != null ? s.Port : 0 };
            var h = MySqlKlien.Jalankan(s,
                "SELECT VERSION(), CURRENT_USER(), @@version_comment");
            if (!h.Ok) { galat = h.Galat; return info; }
            if (h.Tabel.Baris.Count == 0 || h.Tabel.Baris[0].Length < 3) return info;
            info.Versi = h.Tabel.Baris[0][0] ?? "";
            info.Pengguna = h.Tabel.Baris[0][1] ?? "";
            info.Komentar = h.Tabel.Baris[0][2] ?? "";
            return info;
        }

        // ----------------------------------------------------------- Basis data

        public sealed class InfoDb
        {
            public string Nama { get; set; }
            public long Bytes { get; set; }
            public int Tabel { get; set; }
            public string Ukuran { get { return Ukur(Bytes); } }

            /// <summary>Baris kedua di daftar: berapa tabel dan seberapa besar.</summary>
            public string Ringkas
            {
                get
                {
                    return Tabel == 0
                        ? Lang.T("kosong")
                        : string.Format(CultureInfo.CurrentCulture,
                                        Lang.T("{0} tabel - {1}"), Tabel, Ukuran);
                }
            }
        }

        /// <summary>
        /// Daftar basis data berikut ukuran dan jumlah tabelnya, dalam SATU
        /// kueri. Menanyakannya satu per satu berarti satu proses mysql.exe per
        /// basis data, dan di server berisi belasan skema itu terasa jelas.
        ///
        /// LEFT JOIN, bukan JOIN biasa: basis data yang masih kosong harus tetap
        /// muncul di daftar. Kalau tidak, basis data yang baru saja dibuat orang
        /// langsung hilang dari layar dan tampak seperti gagal dibuat.
        /// </summary>
        public static List<InfoDb> DaftarBasisData(MySqlKlien.Sambungan s, out string galat)
        {
            galat = null;
            var hasil = new List<InfoDb>();
            var h = MySqlKlien.Jalankan(s,
                "SELECT s.SCHEMA_NAME, "
                + "IFNULL(SUM(t.DATA_LENGTH + t.INDEX_LENGTH), 0), "
                + "COUNT(t.TABLE_NAME) "
                + "FROM information_schema.SCHEMATA s "
                + "LEFT JOIN information_schema.TABLES t "
                + "  ON t.TABLE_SCHEMA = s.SCHEMA_NAME "
                + "WHERE s.SCHEMA_NAME NOT IN "
                + "  ('information_schema','performance_schema','mysql','sys') "
                + "GROUP BY s.SCHEMA_NAME ORDER BY s.SCHEMA_NAME");
            if (!h.Ok) { galat = h.Galat; return hasil; }

            foreach (var b in h.Tabel.Baris)
            {
                if (b.Length < 3 || string.IsNullOrEmpty(b[0])) continue;
                hasil.Add(new InfoDb
                {
                    Nama = b[0],
                    Bytes = Angka(b[1]),
                    Tabel = (int)Angka(b[2]),
                });
            }
            return hasil;
        }

        // ---------------------------------------------------------------- Tabel

        public sealed class InfoTabel
        {
            public string Nama { get; set; }
            public bool Tilikan { get; set; }
            public long Baris { get; set; }       // perkiraan; -1 bila tidak diketahui
            public long Bytes { get; set; }
            public string Mesin { get; set; }
            public string Kolasi { get; set; }

            public string Jenis { get { return Tilikan ? Lang.T("tilikan") : Lang.T("tabel"); } }
            public string Ukuran { get { return Tilikan ? "-" : Ukur(Bytes); } }

            /// <summary>
            /// Jumlah baris untuk layar. Diberi tanda ~ karena angka InnoDB di
            /// information_schema memang taksiran dari sampel halaman indeks,
            /// dan bisa meleset jauh - menampilkannya seolah tepat membuat orang
            /// mengira datanya hilang.
            /// </summary>
            public string BarisTampil
            {
                get
                {
                    if (Tilikan || Baris < 0) return "-";
                    return "~" + Baris.ToString("N0", CultureInfo.CurrentCulture);
                }
            }
        }

        public static List<InfoTabel> DaftarTabel(MySqlKlien.Sambungan s, string db, out string galat)
        {
            galat = null;
            var daftar = new List<InfoTabel>();
            if (string.IsNullOrEmpty(db)) return daftar;

            var h = MySqlKlien.Jalankan(s,
                "SELECT TABLE_NAME, TABLE_TYPE, IFNULL(TABLE_ROWS,-1), IFNULL(ENGINE,''), "
                + "IFNULL(DATA_LENGTH + INDEX_LENGTH, 0), IFNULL(TABLE_COLLATION,'') "
                + "FROM information_schema.TABLES WHERE TABLE_SCHEMA = " + MySqlKlien.KutipTeks(db)
                + " ORDER BY TABLE_NAME");
            if (!h.Ok) { galat = h.Galat; return daftar; }

            foreach (var b in h.Tabel.Baris)
            {
                if (b.Length < 6 || string.IsNullOrEmpty(b[0])) continue;
                daftar.Add(new InfoTabel
                {
                    Nama = b[0],
                    Tilikan = string.Equals(b[1], "VIEW", StringComparison.OrdinalIgnoreCase),
                    Baris = Angka(b[2], -1),
                    Mesin = b[3] ?? "",
                    Bytes = Angka(b[4]),
                    Kolasi = b[5] ?? "",
                });
            }
            return daftar;
        }

        // -------------------------------------------------------------- Struktur

        public sealed class InfoKolom
        {
            public string Nama { get; set; }
            public string Jenis { get; set; }
            public string Kosong { get; set; }    // "ya" / "tidak"
            public string Kunci { get; set; }
            public string Bawaan { get; set; }
            public string Ekstra { get; set; }
            public string Kolasi { get; set; }
            public string Komentar { get; set; }
        }

        /// <summary>
        /// Kolom sebuah tabel. Memakai SHOW FULL COLUMNS, bukan information_schema:
        /// bentuknya sama untuk tabel maupun tilikan, dan jenis kolomnya sudah
        /// lengkap dengan panjang serta tak-bertanda - "int(10) unsigned", bukan
        /// "int" yang harus dirakit ulang dari empat kolom terpisah.
        /// </summary>
        public static List<InfoKolom> Struktur(MySqlKlien.Sambungan s, string db, string tabel,
                                               out string galat)
        {
            galat = null;
            var hasil = new List<InfoKolom>();
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel)) return hasil;

            var h = MySqlKlien.Jalankan(s,
                "SHOW FULL COLUMNS FROM " + MySqlKlien.Kutip(tabel), db);
            if (!h.Ok) { galat = h.Galat; return hasil; }

            var k = Peta(h.Tabel);
            foreach (var b in h.Tabel.Baris)
            {
                hasil.Add(new InfoKolom
                {
                    Nama = Sel(b, k, "Field"),
                    Jenis = Sel(b, k, "Type"),
                    Kosong = string.Equals(Sel(b, k, "Null"), "YES", StringComparison.OrdinalIgnoreCase)
                             ? Lang.T("ya") : Lang.T("tidak"),
                    Kunci = Sel(b, k, "Key"),
                    Bawaan = Sel(b, k, "Default"),
                    Ekstra = Sel(b, k, "Extra"),
                    Kolasi = Sel(b, k, "Collation"),
                    Komentar = Sel(b, k, "Comment"),
                });
            }
            return hasil;
        }

        public sealed class InfoIndeks
        {
            public string Nama { get; set; }
            public string Kolom { get; set; }
            public string Sifat { get; set; }
            public string Jenis { get; set; }
        }

        /// <summary>
        /// Indeks sebuah tabel. SHOW INDEX mengembalikan SATU BARIS PER KOLOM,
        /// jadi indeks gabungan tampil terpecah-pecah dan urutannya - yang justru
        /// menentukan apakah indeksnya terpakai - tidak terlihat. Di sini
        /// barisnya disatukan kembali per nama indeks, mengikuti Seq_in_index.
        /// </summary>
        public static List<InfoIndeks> Indeks(MySqlKlien.Sambungan s, string db, string tabel,
                                              out string galat)
        {
            galat = null;
            var hasil = new List<InfoIndeks>();
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel)) return hasil;

            var h = MySqlKlien.Jalankan(s, "SHOW INDEX FROM " + MySqlKlien.Kutip(tabel), db);
            if (!h.Ok) { galat = h.Galat; return hasil; }

            var k = Peta(h.Tabel);
            var urutan = new List<string>();
            var kolom = new Dictionary<string, List<KeyValuePair<int, string>>>(StringComparer.Ordinal);
            var unik = new Dictionary<string, bool>(StringComparer.Ordinal);
            var jenis = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var b in h.Tabel.Baris)
            {
                var nama = Sel(b, k, "Key_name");
                if (nama.Length == 0) continue;
                if (!kolom.ContainsKey(nama))
                {
                    urutan.Add(nama);
                    kolom[nama] = new List<KeyValuePair<int, string>>();
                    unik[nama] = Sel(b, k, "Non_unique") == "0";
                    jenis[nama] = Sel(b, k, "Index_type");
                }
                kolom[nama].Add(new KeyValuePair<int, string>(
                    (int)Angka(Sel(b, k, "Seq_in_index")), Sel(b, k, "Column_name")));
            }

            foreach (var nama in urutan)
            {
                hasil.Add(new InfoIndeks
                {
                    Nama = nama,
                    Kolom = string.Join(", ", kolom[nama]
                        .OrderBy(x => x.Key).Select(x => x.Value).ToArray()),
                    Sifat = nama == "PRIMARY" ? Lang.T("kunci utama")
                          : unik[nama] ? Lang.T("unik") : Lang.T("biasa"),
                    Jenis = jenis[nama],
                });
            }
            return hasil;
        }

        /// <summary>Perintah CREATE TABLE lengkap, seperti yang disimpan server.</summary>
        public static string BuatTabelSql(MySqlKlien.Sambungan s, string db, string tabel,
                                          out string galat)
        {
            galat = null;
            var h = MySqlKlien.Jalankan(s, "SHOW CREATE TABLE " + MySqlKlien.Kutip(tabel), db);
            if (!h.Ok) { galat = h.Galat; return ""; }
            if (h.Tabel.Baris.Count == 0) return "";
            var b = h.Tabel.Baris[0];
            return b.Length >= 2 ? (b[1] ?? "") : "";
        }

        // --------------------------------------------------------------- Halaman

        public sealed class Halaman
        {
            public MySqlKlien.Tabel Tabel;
            public long Total = -1;      // -1 bila belum/tidak dihitung
            public int Offset;
            public string Galat;
            public long Ms;
            public bool Ok { get { return Galat == null; } }
        }

        /// <summary>
        /// Satu halaman isi tabel, berikut jumlah baris SEBENARNYA.
        ///
        /// Dulu isi tabel diambil dengan LIMIT 500 lalu diberi catatan "yang
        /// ditampilkan 500 pertama". Itu jalan buntu: tidak ada cara melihat
        /// baris ke-501, dan pada tabel kerja yang berisi puluhan ribu baris
        /// itu berarti sebagian besar datanya tidak pernah bisa dilihat sama
        /// sekali. Sekarang berhalaman.
        ///
        /// COUNT(*) dijalankan sekali, hanya saat halaman pertama diminta -
        /// pada InnoDB ia memindai indeks dan tidak murah, jadi mengulangnya di
        /// tiap ganti halaman membuat penelusuran terasa berat tanpa guna.
        /// </summary>
        public static Halaman Isi(MySqlKlien.Sambungan s, string db, string tabel,
                                  int offset, int batas, string urutKolom, bool menurun,
                                  bool hitungTotal)
        {
            var hal = new Halaman { Offset = offset };
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel))
            {
                hal.Galat = Lang.T("Belum ada tabel yang dipilih.");
                return hal;
            }

            var sql = new StringBuilder();
            sql.Append("SELECT * FROM ").Append(MySqlKlien.Kutip(tabel));
            if (!string.IsNullOrEmpty(urutKolom))
                sql.Append(" ORDER BY ").Append(MySqlKlien.Kutip(urutKolom))
                   .Append(menurun ? " DESC" : " ASC");
            sql.Append(" LIMIT ").Append(batas).Append(" OFFSET ").Append(offset);

            var h = MySqlKlien.Jalankan(s, sql.ToString(), db);
            hal.Ms = h.Ms;
            if (!h.Ok) { hal.Galat = h.Galat; return hal; }
            hal.Tabel = h.Tabel;

            if (hitungTotal)
            {
                var c = MySqlKlien.Jalankan(s,
                    "SELECT COUNT(*) FROM " + MySqlKlien.Kutip(tabel), db);
                hal.Ms += c.Ms;
                if (c.Ok && c.Tabel.Baris.Count > 0 && c.Tabel.Baris[0].Length > 0)
                    hal.Total = Angka(c.Tabel.Baris[0][0], -1);
            }
            return hal;
        }

        // ----------------------------------------------------------------- Bantu

        /// <summary>Nama kolom -> nomornya, supaya sel diambil menurut NAMA.</summary>
        static Dictionary<string, int> Peta(MySqlKlien.Tabel t)
        {
            var p = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < t.Kolom.Count; i++)
                if (!p.ContainsKey(t.Kolom[i])) p[t.Kolom[i]] = i;
            return p;
        }

        /// <summary>
        /// Sel menurut nama kolom. Diambil menurut NAMA, bukan nomor, karena
        /// susunan kolom SHOW COLUMNS dan SHOW INDEX berbeda antarversi MySQL
        /// dan MariaDB - nomor yang dipatok mati akan menaruh isi kolom yang
        /// keliru di bawah judul yang benar, dan itu tidak terlihat salah.
        /// </summary>
        static string Sel(string[] baris, Dictionary<string, int> peta, string nama)
        {
            int i;
            if (!peta.TryGetValue(nama, out i) || i >= baris.Length) return "";
            return baris[i] ?? "";
        }

        static long Angka(string teks, long bila = 0)
        {
            long n;
            return long.TryParse((teks ?? "").Trim(), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out n) ? n : bila;
        }

        public static string Ukur(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= 1024 * 1024)
                return (bytes / 1024.0 / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
            return Math.Max(1, bytes / 1024).ToString("N0", CultureInfo.CurrentCulture) + " KB";
        }
    }
}
