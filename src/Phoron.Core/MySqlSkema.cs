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

        // Daftar tabel, struktur kolom, indeks, CREATE TABLE, dan pengambilan
        // halaman isi dulu ada di sini. Semuanya dibuang bersama penjelajah
        // bawaan: HeidiSQL sudah melakukan semuanya dengan jauh lebih lengkap,
        // dan memelihara dua jalan untuk hal yang sama berarti salah satunya
        // pasti tertinggal.

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
