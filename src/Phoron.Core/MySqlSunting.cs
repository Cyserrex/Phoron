using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Membaca dan MENGUBAH isi tabel.
    ///
    /// MASALAH YANG HARUS DISELESAIKAN LEBIH DULU: mode --batch menulis NULL
    /// sebagai kata "NULL", sama persis dengan nilai teks yang isinya kata itu.
    /// Untuk sekadar melihat, kerancuan itu bisa diterima dan memang dibiarkan
    /// apa adanya di tab SQL. Untuk MENYUNTING, ia berbahaya: sel yang
    /// sesungguhnya kosong akan tersimpan kembali sebagai teks "NULL", dan
    /// perubahan itu tidak akan terlihat salah oleh siapa pun sampai ada
    /// laporan yang hasilnya ganjil berbulan-bulan kemudian.
    ///
    /// CARA MENYELESAIKANNYA. Untuk tab Jelajah, Phoron tahu daftar kolomnya
    /// lebih dulu, jadi kueri tidak perlu memakai SELECT *: tiap kolom diminta
    /// bersama satu kolom pendamping berisi hasil "col IS NULL". Pendampingnya
    /// nilai 1 atau 0, tidak pernah rancu, dan tidak ikut ditampilkan. Dengan
    /// begitu kekosongan jadi fakta yang dibaca, bukan tebakan.
    ///
    /// Pendamping dipilih ketimbang penanda yang ditempelkan ke nilainya sendiri
    /// (misalnya CONCAT('\0', col)): penempelan memaksa setiap kolom jadi teks,
    /// dan itu merusak kolom biner serta mengubah bentuk tanggal dan desimal.
    /// </summary>
    public static class MySqlSunting
    {
        /// <summary>Awalan nama kolom pendamping. Sengaja tidak mungkin jadi nama kolom sungguhan.</summary>
        const string Pendamping = "__phoron_null_";

        // ------------------------------------------------------------ Kunci baris

        /// <summary>
        /// Kolom kunci utama sebuah tabel, urut sesuai urutannya di dalam kunci.
        /// Kosong berarti tabel ini tidak punya kunci utama.
        /// </summary>
        public static List<string> KunciUtama(MySqlKlien.Sambungan s, string db, string tabel,
                                              out string galat)
        {
            galat = null;
            var hasil = new List<string>();
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel)) return hasil;

            var h = MySqlKlien.Jalankan(s,
                "SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE "
                + "WHERE TABLE_SCHEMA = " + MySqlKlien.KutipTeks(db)
                + " AND TABLE_NAME = " + MySqlKlien.KutipTeks(tabel)
                + " AND CONSTRAINT_NAME = 'PRIMARY' ORDER BY ORDINAL_POSITION");
            if (!h.Ok) { galat = h.Galat; return hasil; }

            foreach (var b in h.Tabel.Baris)
                if (b.Length > 0 && !string.IsNullOrEmpty(b[0])) hasil.Add(b[0]);
            return hasil;
        }

        // -------------------------------------------------------------- Kunci asing

        public sealed class InfoRelasi
        {
            public string Kolom { get; set; }
            public string KeTabel { get; set; }
            public string KeKolom { get; set; }
            public string SaatHapus { get; set; }
            public string SaatUbah { get; set; }
            public string Nama { get; set; }
        }

        /// <summary>
        /// Kunci asing tabel ini - ke mana kolomnya menunjuk, dan apa yang
        /// terjadi pada baris ini kalau baris tujuannya dihapus atau diubah.
        /// Dua hal terakhir itulah yang menjelaskan kenapa sebuah DELETE
        /// menyeret baris lain yang sama sekali tidak disebut.
        /// </summary>
        public static List<InfoRelasi> Relasi(MySqlKlien.Sambungan s, string db, string tabel,
                                              out string galat)
        {
            galat = null;
            var hasil = new List<InfoRelasi>();
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel)) return hasil;

            var h = MySqlKlien.Jalankan(s,
                "SELECT k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME, "
                + "IFNULL(r.DELETE_RULE,''), IFNULL(r.UPDATE_RULE,''), k.CONSTRAINT_NAME "
                + "FROM information_schema.KEY_COLUMN_USAGE k "
                + "LEFT JOIN information_schema.REFERENTIAL_CONSTRAINTS r "
                + "  ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA "
                + " AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME "
                + "WHERE k.TABLE_SCHEMA = " + MySqlKlien.KutipTeks(db)
                + " AND k.TABLE_NAME = " + MySqlKlien.KutipTeks(tabel)
                + " AND k.REFERENCED_TABLE_NAME IS NOT NULL "
                + "ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION");
            if (!h.Ok) { galat = h.Galat; return hasil; }

            foreach (var b in h.Tabel.Baris)
            {
                if (b.Length < 6) continue;
                hasil.Add(new InfoRelasi
                {
                    Kolom = b[0], KeTabel = b[1], KeKolom = b[2],
                    SaatHapus = b[3], SaatUbah = b[4], Nama = b[5],
                });
            }
            return hasil;
        }

        // ------------------------------------------------------------ Baca halaman

        /// <summary>
        /// Satu sel: isinya, dan apakah ia benar-benar kosong.
        ///
        /// PROPERTI, BUKAN MEDAN. WPF hanya mengikat ke properti, dan pengikatan
        /// ke medan gagal TANPA SUARA: kisinya tetap terbentuk, kolomnya tetap
        /// ada, selnya kosong semua, dan tidak ada satu pun galat. Jebakan ini
        /// sudah pernah menjerat proyek ini sebelumnya.
        /// </summary>
        public sealed class Sel
        {
            public string Teks { get; set; }
            public bool Kosong { get; set; }

            /// <summary>
            /// Yang dibaca layar. Sel yang benar-benar kosong ditulis NULL dan
            /// oleh layar dimiringkan; sel yang isinya kata "NULL" tampil tegak.
            /// Bedanya ada di gaya, bukan di tulisannya - sebab tulisan yang sama
            /// memang bisa berasal dari dua hal yang berbeda.
            /// </summary>
            public string Papar { get { return Kosong ? "NULL" : (Teks ?? ""); } }

            public override string ToString() { return Papar; }
        }

        public sealed class Halaman
        {
            public List<string> Kolom = new List<string>();
            public List<Sel[]> Baris = new List<Sel[]>();
            public long Total = -1;
            public int Offset;
            public string Galat;
            public long Ms;
            public bool Ok { get { return Galat == null; } }
        }

        /// <summary>
        /// Satu halaman isi tabel, dengan kekosongan yang PASTI.
        ///
        /// COUNT(*) dijalankan hanya saat diminta - pada InnoDB ia memindai
        /// indeks dan tidak murah, jadi mengulangnya di tiap ganti halaman
        /// membuat penelusuran terasa berat tanpa guna.
        /// </summary>
        public static Halaman Isi(MySqlKlien.Sambungan s, string db, string tabel,
                                  List<string> kolom, int offset, int batas,
                                  string urutKolom, bool menurun, string saring,
                                  bool hitungTotal)
        {
            var hal = new Halaman { Offset = offset };
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(tabel))
            {
                hal.Galat = Lang.T("Belum ada tabel yang dipilih.");
                return hal;
            }
            if (kolom == null || kolom.Count == 0)
            {
                hal.Galat = Lang.T("Daftar kolom tabel ini tidak terbaca.");
                return hal;
            }

            var pilih = new StringBuilder();
            for (int i = 0; i < kolom.Count; i++)
            {
                if (i > 0) pilih.Append(", ");
                pilih.Append(MySqlKlien.Kutip(kolom[i]));
                pilih.Append(", ").Append(MySqlKlien.Kutip(kolom[i])).Append(" IS NULL AS ")
                     .Append(MySqlKlien.Kutip(Pendamping + i));
            }

            var sql = new StringBuilder();
            sql.Append("SELECT ").Append(pilih).Append(" FROM ").Append(MySqlKlien.Kutip(tabel));
            var where = (saring ?? "").Trim();
            if (where.Length > 0) sql.Append(" WHERE ").Append(where);
            if (!string.IsNullOrEmpty(urutKolom))
                sql.Append(" ORDER BY ").Append(MySqlKlien.Kutip(urutKolom))
                   .Append(menurun ? " DESC" : " ASC");
            sql.Append(" LIMIT ").Append(batas).Append(" OFFSET ").Append(offset);

            var h = MySqlKlien.Jalankan(s, sql.ToString(), db);
            hal.Ms = h.Ms;
            if (!h.Ok) { hal.Galat = h.Galat; return hal; }

            hal.Kolom = new List<string>(kolom);
            foreach (var b in h.Tabel.Baris)
            {
                var baris = new Sel[kolom.Count];
                for (int i = 0; i < kolom.Count; i++)
                {
                    var iNilai = i * 2;
                    var iKosong = i * 2 + 1;
                    var kosong = iKosong < b.Length && b[iKosong] == "1";
                    baris[i] = new Sel
                    {
                        Kosong = kosong,
                        Teks = kosong ? null : (iNilai < b.Length ? b[iNilai] : null),
                    };
                }
                hal.Baris.Add(baris);
            }

            if (hitungTotal)
            {
                var c = MySqlKlien.Jalankan(s,
                    "SELECT COUNT(*) FROM " + MySqlKlien.Kutip(tabel)
                    + (where.Length > 0 ? " WHERE " + where : ""), db);
                hal.Ms += c.Ms;
                if (c.Ok && c.Tabel.Baris.Count > 0 && c.Tabel.Baris[0].Length > 0)
                {
                    long n;
                    if (long.TryParse((c.Tabel.Baris[0][0] ?? "").Trim(), out n)) hal.Total = n;
                }
            }
            return hal;
        }

        // ---------------------------------------------------------------- Mengubah

        /// <summary>
        /// Syarat WHERE yang menunjuk TEPAT satu baris, disusun dari kunci utama.
        /// Mengembalikan null bila ada nilai kunci yang kosong - baris seperti itu
        /// tidak bisa ditunjuk dengan pasti, dan menebaknya berarti mengubah baris
        /// yang salah.
        /// </summary>
        static string Syarat(List<string> kunci, Dictionary<string, Sel> nilai)
        {
            var bagian = new List<string>();
            foreach (var k in kunci)
            {
                Sel sel;
                if (!nilai.TryGetValue(k, out sel) || sel == null || sel.Kosong) return null;
                bagian.Add(MySqlKlien.Kutip(k) + " = " + MySqlKlien.KutipTeks(sel.Teks));
            }
            return bagian.Count > 0 ? string.Join(" AND ", bagian.ToArray()) : null;
        }

        public sealed class HasilUbah
        {
            public string Galat;
            public long Terpengaruh = -1;
            public string Sql = "";
            public bool Ok { get { return Galat == null; } }
        }

        /// <summary>
        /// Ubah satu sel.
        ///
        /// WHERE-nya disusun dari kunci utama dan DIBATASI LIMIT 1. Batas itu
        /// bukan hiasan: kalau kunci utamanya ternyata tidak seunik yang dikira -
        /// tabel yang diimpor dengan kunci rusak memang ada - tanpa batas itu satu
        /// suntingan sel bisa menimpa ribuan baris sekaligus, tanpa peringatan.
        /// </summary>
        public static HasilUbah UbahSel(MySqlKlien.Sambungan s, string db, string tabel,
                                        List<string> kunci, Dictionary<string, Sel> barisAsli,
                                        string kolom, string nilaiBaru, bool jadikanKosong)
        {
            var hasil = new HasilUbah();
            if (kunci == null || kunci.Count == 0)
            {
                hasil.Galat = Lang.T("Tabel ini tidak punya kunci utama, jadi barisnya tidak bisa "
                                     + "ditunjuk dengan pasti. Suntingan lewat tab SQL masih bisa.");
                return hasil;
            }
            var syarat = Syarat(kunci, barisAsli);
            if (syarat == null)
            {
                hasil.Galat = Lang.T("Nilai kunci utama baris ini kosong, jadi barisnya tidak bisa "
                                     + "ditunjuk dengan pasti.");
                return hasil;
            }

            var sql = "UPDATE " + MySqlKlien.Kutip(tabel) + " SET " + MySqlKlien.Kutip(kolom)
                      + " = " + (jadikanKosong ? "NULL" : MySqlKlien.KutipTeks(nilaiBaru))
                      + " WHERE " + syarat + " LIMIT 1";
            hasil.Sql = sql;

            var h = MySqlKlien.Jalankan(s, sql, db);
            if (!h.Ok) { hasil.Galat = h.Galat; return hasil; }
            hasil.Terpengaruh = h.Terpengaruh;
            return hasil;
        }

        /// <summary>Hapus satu baris, ditunjuk lewat kunci utamanya.</summary>
        public static HasilUbah HapusBaris(MySqlKlien.Sambungan s, string db, string tabel,
                                           List<string> kunci, Dictionary<string, Sel> barisAsli)
        {
            var hasil = new HasilUbah();
            if (kunci == null || kunci.Count == 0)
            {
                hasil.Galat = Lang.T("Tabel ini tidak punya kunci utama, jadi barisnya tidak bisa "
                                     + "ditunjuk dengan pasti. Suntingan lewat tab SQL masih bisa.");
                return hasil;
            }
            var syarat = Syarat(kunci, barisAsli);
            if (syarat == null)
            {
                hasil.Galat = Lang.T("Nilai kunci utama baris ini kosong, jadi barisnya tidak bisa "
                                     + "ditunjuk dengan pasti.");
                return hasil;
            }

            var sql = "DELETE FROM " + MySqlKlien.Kutip(tabel) + " WHERE " + syarat + " LIMIT 1";
            hasil.Sql = sql;

            var h = MySqlKlien.Jalankan(s, sql, db);
            if (!h.Ok) { hasil.Galat = h.Galat; return hasil; }
            hasil.Terpengaruh = h.Terpengaruh;
            return hasil;
        }

        /// <summary>
        /// Rangka INSERT untuk tabel ini, siap disunting di kotak SQL.
        ///
        /// Menyediakan rangka, bukan kotak isian berkolom-kolom: nilai bawaan,
        /// auto_increment, dan kolom yang boleh kosong punya aturan sendiri-sendiri
        /// yang lebih jelas terbaca sebagai SQL daripada sebagai deretan kotak
        /// kosong yang tidak menjelaskan apa-apa.
        /// </summary>
        public static string RangkaInsert(string tabel, List<MySqlSkema.InfoKolom> kolom)
        {
            if (kolom == null || kolom.Count == 0) return "";
            var isi = kolom.Where(k => (k.Ekstra ?? "").IndexOf("auto_increment",
                                        StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (isi.Count == 0) isi = kolom;

            var sb = new StringBuilder();
            sb.Append("INSERT INTO ").Append(MySqlKlien.Kutip(tabel)).AppendLine(" (");
            sb.Append("  ").AppendLine(string.Join(", ",
                isi.Select(k => MySqlKlien.Kutip(k.Nama)).ToArray()));
            sb.AppendLine(") VALUES (");
            sb.Append("  ").AppendLine(string.Join(", ",
                isi.Select(k => k.Kosong == Lang.T("ya") ? "NULL" : "''").ToArray()));
            sb.Append(");");
            return sb.ToString();
        }

        /// <summary>Satu baris sebagai perintah INSERT - untuk disalin ke tempat lain.</summary>
        public static string BarisJadiInsert(string tabel, List<string> kolom, Sel[] baris)
        {
            if (kolom == null || baris == null) return "";
            var sb = new StringBuilder();
            sb.Append("INSERT INTO ").Append(MySqlKlien.Kutip(tabel)).Append(" (");
            sb.Append(string.Join(", ", kolom.Select(MySqlKlien.Kutip).ToArray()));
            sb.Append(") VALUES (");
            var nilai = new List<string>();
            for (int i = 0; i < kolom.Count; i++)
                nilai.Add(i < baris.Length && baris[i] != null && !baris[i].Kosong
                          ? MySqlKlien.KutipTeks(baris[i].Teks) : "NULL");
            sb.Append(string.Join(", ", nilai.ToArray()));
            sb.Append(");");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ CSV

        /// <summary>
        /// Baris-baris jadi CSV. Sel kosong ditulis KOSONG tanpa tanda kutip,
        /// sedangkan teks kosong ditulis sebagai sepasang kutip - itulah satu-
        /// satunya cara CSV membedakan keduanya, dan pembedaan itu baru mungkin
        /// karena kekosongan dibaca sebagai fakta, bukan ditebak dari tulisannya.
        /// </summary>
        public static string Csv(List<string> kolom, IEnumerable<Sel[]> baris, char pemisah = ',')
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(pemisah.ToString(),
                kolom.Select(k => Kutip(k, pemisah)).ToArray()));
            foreach (var b in baris)
            {
                var sel = new List<string>();
                for (int i = 0; i < kolom.Count; i++)
                {
                    if (i >= b.Length || b[i] == null || b[i].Kosong) { sel.Add(""); continue; }
                    sel.Add(Kutip(b[i].Teks, pemisah));
                }
                sb.AppendLine(string.Join(pemisah.ToString(), sel.ToArray()));
            }
            return sb.ToString();
        }

        static string Kutip(string nilai, char pemisah)
        {
            var t = nilai ?? "";
            bool perlu = t.Length == 0 || t.IndexOf(pemisah) >= 0 || t.IndexOf('"') >= 0
                         || t.IndexOf('\n') >= 0 || t.IndexOf('\r') >= 0
                         || t != t.Trim();
            return perlu ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
        }
    }
}
