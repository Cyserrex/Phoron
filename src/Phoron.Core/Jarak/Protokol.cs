using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace Phoron.Core.Jarak
{
    /// <summary>
    /// Bentuk pesan protokol pipe v2 di sisi C#. Sumber kebenarannya
    /// daemon/inti/src/protokol.rs; ringkasannya daemon/PROTOKOL.md.
    ///
    /// Sengaja memakai JavaScriptSerializer dan kamus biasa, bukan kelas DTO
    /// berpasangan satu-satu: rakitannya bagian dari .NET Framework (tanpa
    /// NuGet), dan bentuk pesannya kecil - menulis kelas untuk tiap perintah
    /// hanya menambah tempat yang harus diubah bila protokolnya bergeser.
    /// </summary>
    public static class Protokol
    {
        /// <summary>Harus sama dengan VERSI_PROTOKOL di protokol.rs.</summary>
        public const int Versi = 2;

        /// <summary>Nama pipe bawaan, tanpa awalan \\.\pipe\.</summary>
        public const string NamaPipeBawaan = "phoron";

        /// <summary>Batas satu baris, sama dengan BATAS_BARIS di daemon.</summary>
        public const int BatasBaris = 4 * 1024 * 1024;

        [ThreadStatic] static JavaScriptSerializer _json;

        /// <summary>Satu serializer per utas: JavaScriptSerializer tidak dijamin aman dipakai bersama.</summary>
        public static JavaScriptSerializer Json
        {
            get
            {
                if (_json == null)
                    _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
                return _json;
            }
        }

        public static string Tulis(object nilai) { return Json.Serialize(nilai); }

        /// <summary>Urai satu baris; objek JSON jadi IDictionary&lt;string, object&gt;, larik jadi object[].</summary>
        public static object Urai(string baris) { return Json.DeserializeObject(baris); }

        // ------------------------------------------------------ Pembaca nilai

        public static IDictionary<string, object> Objek(object o) { return o as IDictionary<string, object>; }

        public static object Ambil(IDictionary<string, object> d, string kunci)
        {
            object v;
            return d != null && d.TryGetValue(kunci, out v) ? v : null;
        }

        public static string Teks(IDictionary<string, object> d, string kunci)
        {
            var v = Ambil(d, kunci);
            return v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool Benar(IDictionary<string, object> d, string kunci)
        {
            var v = Ambil(d, kunci);
            return v is bool && (bool)v;
        }

        public static long Angka(IDictionary<string, object> d, string kunci, long bawaan = 0)
        {
            var v = Ambil(d, kunci);
            if (v == null || v is bool || v is string) return bawaan;
            try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return bawaan; }
        }

        public static IEnumerable<object> Larik(object o)
        {
            if (o == null || o is string) return Enumerable.Empty<object>();
            var e = o as IEnumerable;
            return e == null ? Enumerable.Empty<object>() : e.Cast<object>();
        }

        // ------------------------------------------------------------ Bentuk

        public static ServiceState Keadaan(string s)
        {
            ServiceState k;
            return Enum.TryParse(s ?? "", false, out k) ? k : ServiceState.Berhenti;
        }

        public static List<Pesan> DaftarPesan(object o)
        {
            return Larik(o).Select(x => Pesan.Dari(x)).Where(x => x != null).ToList();
        }
    }

    /// <summary>
    /// Teks untuk orang yang belum diterjemahkan: kunci templat Indonesia
    /// persis seperti argumen Lang.T, ditambah argumennya.
    /// </summary>
    public sealed class Pesan
    {
        public string Kunci = "";
        public List<string> Args = new List<string>();

        public Pesan() { }
        public Pesan(string kunci, params string[] args)
        {
            Kunci = kunci ?? "";
            if (args != null) Args.AddRange(args);
        }

        public static Pesan Dari(object o)
        {
            var d = Protokol.Objek(o);
            if (d == null) return null;
            var p = new Pesan { Kunci = Protokol.Teks(d, "kunci") ?? "" };
            foreach (var a in Protokol.Larik(Protokol.Ambil(d, "args")))
                p.Args.Add(a == null ? "" : Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture));
            return p;
        }

        public Dictionary<string, object> KeJson()
        {
            var d = new Dictionary<string, object> { { "kunci", Kunci ?? "" } };
            if (Args.Count > 0) d["args"] = Args.ToArray();
            return d;
        }

        /// <summary>Terjemahan untuk layar: Lang.T(kunci, args).</summary>
        public string Teks()
        {
            if (Args.Count == 0) return Lang.T(Kunci);
            try { return Lang.T(Kunci, Args.Cast<object>().ToArray()); }
            catch (FormatException)
            {
                // Templat dan argumennya tidak sepadan (daemon lebih baru dari
                // kamus ini). Kuncinya tetap ditampilkan, bukan dibuang.
                return Kunci + " " + string.Join(" ", Args);
            }
        }

        public override string ToString() { return Teks(); }
    }

    /// <summary>Jawaban daemon atas satu permintaan.</summary>
    public sealed class Jawaban
    {
        public long Id;
        public bool Ok;
        /// <summary>Isi "hasil": kamus, larik, teks, angka, atau null.</summary>
        public object Hasil;
        public List<Pesan> Peringatan = new List<Pesan>();
        /// <summary>belum_ada, protokol_beda, tidak_terbaca, gagal, perlu_admin; null bila Ok.</summary>
        public string JenisGalat;
        public IDictionary<string, object> Galat;

        public static Jawaban Dari(IDictionary<string, object> d)
        {
            var j = new Jawaban { Id = Protokol.Angka(d, "id") };
            j.Ok = Protokol.Teks(d, "status") == "ok";
            j.Hasil = Protokol.Ambil(d, "hasil");
            j.Peringatan = Protokol.DaftarPesan(Protokol.Ambil(d, "peringatan"));
            if (!j.Ok)
            {
                j.Galat = Protokol.Objek(Protokol.Ambil(d, "galat"));
                j.JenisGalat = Protokol.Teks(j.Galat, "jenis") ?? "gagal";
            }
            return j;
        }

        /// <summary>Galat sebagai teks untuk orang, sudah diterjemahkan.</summary>
        public string PesanGalat()
        {
            if (Ok) return null;
            switch (JenisGalat)
            {
                case "gagal":
                case "perlu_admin":
                    var p = Pesan.Dari(Protokol.Ambil(Galat, "pesan"));
                    return p != null ? p.Teks() : Lang.T("Daemon Phoron menolak perintah ini.");
                case "belum_ada":
                    return Lang.T("Daemon Phoron belum bisa menjalankan perintah {0}.",
                                  Protokol.Teks(Galat, "perintah") ?? "?");
                case "protokol_beda":
                    return Lang.T("Versi Phoron dan daemonnya tidak sepadan. Phoron perlu dipasang ulang.");
                default:
                    return Lang.T("Daemon Phoron tidak memahami perintah ini: {0}",
                                  Protokol.Teks(Galat, "sebab") ?? JenisGalat);
            }
        }

        /// <summary>Lempar GalatDaemon bila jawaban ini galat; selain itu kembalikan dirinya.</summary>
        public Jawaban Pastikan()
        {
            if (!Ok) throw new GalatDaemon(JenisGalat, PesanGalat());
            return this;
        }
    }

    /// <summary>Kegagalan berbicara dengan daemon, dengan pesan yang siap ditampilkan.</summary>
    public class GalatDaemon : Exception
    {
        /// <summary>
        /// Jenis galat dari daemon (gagal, belum_ada, protokol_beda,
        /// tidak_terbaca), atau milik klien: terputus, batas_waktu, tidak_ada.
        /// </summary>
        public readonly string Jenis;

        public GalatDaemon(string jenis, string pesan) : base(pesan) { Jenis = jenis; }
        public GalatDaemon(string jenis, string pesan, Exception dalam) : base(pesan, dalam) { Jenis = jenis; }
    }
}
