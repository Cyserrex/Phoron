using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Phoron.Core
{
    /// <summary>
    /// Kemajuan unduhan. Byte yang sudah turun ikut dilaporkan, bukan hanya
    /// persen: berkas 5 MB yang macet di 40% terlihat sama dengan yang sedang
    /// berjalan lancar kalau yang ditampilkan hanya angka persen.
    /// </summary>
    public class KemajuanUnduh
    {
        public long Sudah;
        public long Total;
        public int Persen { get { return Total > 0 ? (int)Math.Min(100, Sudah * 100 / Total) : 0; } }

        public static string Ukuran(long b)
        {
            if (b <= 0) return "?";
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            return (b / 1048576.0).ToString("0.#") + " MB";
        }

        public override string ToString()
        {
            return Total > 0
                ? Ukuran(Sudah) + " dari " + Ukuran(Total)
                : Ukuran(Sudah) + " terunduh";
        }
    }

    /// <summary>Hasil pengecekan rilis terbaru di GitHub.</summary>
    public class HasilCek
    {
        /// <summary>Versi rilis terbaru, mis. "1.8.0". Kosong bila gagal membaca.</summary>
        public string Versi = "";
        /// <summary>Alamat berkas installer (*-Setup.exe) pada rilis itu.</summary>
        public string UrlInstaller = "";
        /// <summary>Halaman rilisnya, untuk dibuka di browser.</summary>
        public string UrlHalaman = "";
        /// <summary>Catatan rilis apa adanya; bisa panjang.</summary>
        public string Catatan = "";
        /// <summary>Pesan kesalahan bila pengecekan gagal; null bila berhasil.</summary>
        public string Galat;

        /// <summary>Apakah versi ini lebih baru daripada yang sedang berjalan.</summary>
        public bool LebihBaru { get { return Updater.LebihBaru(Versi, AppInfo.Version); } }
    }

    /// <summary>
    /// Mengecek rilis terbaru Phoron di GitHub.
    ///
    /// Sumber utamanya umpan Atom rilis, yang dilayani github.com dan TIDAK
    /// tunduk pada batas 60 permintaan per jam milik api.github.com. Karena itu
    /// pengecekan rutin tidak pernah kehabisan jatah, dan tidak seorang pun
    /// perlu menyiapkan token - kotak token yang sempat ada di Pengaturan
    /// dibuang lagi begitu jalur ini terbukti.
    ///
    /// API dipakai hanya sebagai cadangan bila umpan itu gagal. Keduanya diurai
    /// seadanya dengan regex: Core tidak menarik satu pun dependensi, dan yang
    /// dibutuhkan cuma beberapa medan.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "Cyserrex/Phoron";
        public const string HalamanRilis = "https://github.com/" + Repo + "/releases";
        const string ApiTerbaru = "https://api.github.com/repos/" + Repo + "/releases/latest";

        static Updater()
        {
            // .NET Framework 4.8 pada sebagian mesin masih default ke protokol
            // lama; tanpa ini permintaan ke GitHub gagal dengan "koneksi ditutup"
            // yang tidak menyebut sebab sebenarnya.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; }
            catch { }
        }

        /// <summary>
        /// Bandingkan dua nomor versi. Perbandingan angka per bagian, bukan
        /// teks: "1.10.0" lebih baru daripada "1.9.0", padahal secara abjad
        /// justru sebaliknya.
        /// </summary>
        public static bool LebihBaru(string calon, string sekarang)
        {
            Version a, b;
            if (!Version.TryParse(Bersihkan(calon), out a)) return false;
            if (!Version.TryParse(Bersihkan(sekarang), out b)) return false;
            return a > b;
        }

        static string Bersihkan(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            // Nomor versi rakitan membawa ekor "+<sha>"; System.Version menolaknya.
            var plus = v.IndexOf('+');
            if (plus > 0) v = v.Substring(0, plus);
            var m = Regex.Match(v, @"^\d+(\.\d+){0,3}");
            return m.Success ? m.Value : "";
        }

        /// <summary>
        /// Urai jawaban JSON dari API rilis GitHub. Dipisah dari pengambilan
        /// datanya supaya bisa diuji tanpa menyentuh jaringan sama sekali.
        /// </summary>
        public static HasilCek Urai(string json)
        {
            var h = new HasilCek();
            if (string.IsNullOrWhiteSpace(json)) { h.Galat = "Jawaban kosong dari GitHub."; return h; }

            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success) { h.Galat = "Nomor versi tidak ditemukan di jawaban GitHub."; return h; }
            h.Versi = Bersihkan(tag.Groups[1].Value);

            var halaman = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+/releases/tag/[^\"]+)\"");
            h.UrlHalaman = halaman.Success ? halaman.Groups[1].Value : HalamanRilis;

            // Aset yang dicari khusus installer. Rilis juga memuat Phoron.exe dan
            // .exe.config; mengambil aset pertama begitu saja akan mengunduh
            // berkas yang salah.
            var aset = Regex.Match(json,
                "\"browser_download_url\"\\s*:\\s*\"([^\"]*-Setup\\.exe)\"", RegexOptions.IgnoreCase);
            h.UrlInstaller = aset.Success ? aset.Groups[1].Value : "";

            var body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (body.Success)
                h.Catatan = body.Groups[1].Value
                    .Replace("\\r\\n", "\n").Replace("\\n", "\n")
                    .Replace("\\\"", "\"").Replace("\\\\", "\\").Trim();
            return h;
        }

        /// <summary>
        /// Menyiapkan permintaan ke GitHub, lengkap dengan token bila ada.
        ///
        /// Tanpa token, GitHub membatasi 60 permintaan per JAM per alamat IP -
        /// bukan per aplikasi. Di kantor yang keluar lewat satu IP, jatah itu
        /// bisa habis oleh alat lain sebelum Phoron sempat memakainya, dan
        /// gejalanya berupa "gagal menghubungi GitHub" yang menyesatkan.
        /// Dengan token, batasnya 5.000 per jam per AKUN.
        /// </summary>
        static HttpWebRequest Siapkan(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            // GitHub menolak permintaan tanpa User-Agent dengan 403.
            req.UserAgent = "Phoron/" + AppInfo.Version;
            return req;
        }

        static bool JatahHabis(HttpWebResponse resp)
        {
            if (resp == null) return false;
            var kode = (int)resp.StatusCode;
            if (kode != 403 && kode != 429) return false;
            // 403 juga dipakai GitHub untuk sebab lain; sisa jatah nol yang
            // memastikan.
            var sisa = resp.Headers["X-RateLimit-Remaining"];
            return sisa == null || sisa == "0";
        }

        /// <summary>
        /// Umpan Atom rilis. Dilayani github.com, BUKAN api.github.com - dan
        /// karena itu tidak tunduk pada batas 60 permintaan per jam. Sudah
        /// dibuktikan: jawabannya tidak memuat satu pun header X-RateLimit.
        /// </summary>
        const string UmpanAtom = "https://github.com/" + Repo + "/releases.atom";

        /// <summary>
        /// Membaca versi terbaru dari umpan Atom.
        ///
        /// Umpan ini tidak menyebutkan berkas aset, jadi alamat installernya
        /// disusun dari pola penamaan yang dipakai CI: satu-satunya hal yang
        /// harus tetap - dan kalau suatu saat berubah, jalur API di bawah masih
        /// jadi cadangannya.
        /// </summary>
        public static HasilCek UraiAtom(string xml)
        {
            var h = new HasilCek();
            if (string.IsNullOrWhiteSpace(xml)) { h.Galat = "Jawaban kosong dari GitHub."; return h; }

            // Entri PERTAMA adalah rilis terbaru; GitHub mengurutkannya begitu.
            var tag = Regex.Match(xml, "/releases/tag/([^\"<]+)");
            if (!tag.Success) { h.Galat = "Nomor versi tidak ditemukan di umpan rilis."; return h; }

            var namaTag = tag.Groups[1].Value.Trim();
            h.Versi = Bersihkan(namaTag);
            h.UrlHalaman = "https://github.com/" + Repo + "/releases/tag/" + namaTag;
            h.UrlInstaller = "https://github.com/" + Repo + "/releases/download/"
                             + namaTag + "/Phoron-" + h.Versi + "-Setup.exe";

            // Catatan rilis di umpan berupa HTML ter-escape. Yang dibutuhkan
            // cuma ringkasannya, jadi tag-nya dibuang seadanya - ini keterangan
            // untuk dibaca manusia, bukan data yang diolah lebih lanjut.
            var isi = Regex.Match(xml, "<content type=\"html\">(.*?)</content>", RegexOptions.Singleline);
            if (isi.Success)
            {
                var t = isi.Groups[1].Value
                    .Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&quot;", "\"").Replace("&amp;", "&");
                t = Regex.Replace(t, "<[^>]+>", " ");
                h.Catatan = Regex.Replace(t, "\\s+", " ").Trim();
            }
            return h;
        }

        /// <summary>
        /// Mengecek rilis terbaru lewat umpan Atom - tanpa menyentuh API sama
        /// sekali, jadi batas 60 permintaan per jam tidak berlaku dan token
        /// tidak dibutuhkan siapa pun.
        /// </summary>
        static async Task<HasilCek> CekLewatAtomAsync()
        {
            var req = Siapkan(UmpanAtom);
            req.Accept = "application/atom+xml";
            req.Timeout = 20000;
            using (var resp = await req.GetResponseAsync())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return UraiAtom(await r.ReadToEndAsync());
        }

        public static async Task<HasilCek> CekAsync()
        {
            // Umpan Atom lebih dulu: ia TIDAK tunduk pada batas 60 permintaan
            // per jam milik API, jadi pengecekan rutin tidak pernah kehabisan
            // jatah dan tidak seorang pun perlu menyiapkan token. API dipakai
            // hanya kalau umpan itu gagal - misalnya diblokir jaringan kantor.
            try
            {
                var lewatAtom = await CekLewatAtomAsync();
                if (lewatAtom.Galat == null && lewatAtom.Versi.Length > 0) return lewatAtom;
            }
            catch { /* jatuh ke API di bawah */ }

            try
            {
                var req = Siapkan(ApiTerbaru);
                req.Accept = "application/vnd.github+json";
                req.Timeout = 20000;
                using (var resp = await req.GetResponseAsync())
                using (var r = new StreamReader(resp.GetResponseStream()))
                    return Urai(await r.ReadToEndAsync());
            }
            catch (WebException ex)
            {
                var h = new HasilCek();
                var resp = ex.Response as HttpWebResponse;
                h.Galat = resp != null && (int)resp.StatusCode == 404
                    ? "Belum ada rilis di GitHub."
                    : JatahHabis(resp)
                        ? "Jatah permintaan API GitHub habis. Ini hanya terjadi kalau umpan "
                          + "rilis tidak bisa dihubungi sehingga Phoron jatuh ke API. Coba lagi "
                          + "beberapa saat lagi, atau periksa apakah jaringan memblokir github.com."
                        : "Tidak bisa menghubungi GitHub: " + ex.Message;
                return h;
            }
            catch (Exception ex)
            {
                return new HasilCek { Galat = "Gagal mengecek pembaruan: " + ex.Message };
            }
        }

        /// <summary>
        /// Unduh installer rilis terbaru ke folder tmp. Mengembalikan jalur
        /// berkasnya, atau null bila gagal (pesan kesalahan lewat out).
        /// </summary>
        public static async Task<string> UnduhInstallerAsync(HasilCek hasil,
                                                             IProgress<KemajuanUnduh> kemajuan,
                                                             CancellationToken batal,
                                                             Action<string> galat)
        {
            if (hasil == null || string.IsNullOrEmpty(hasil.UrlInstaller))
            {
                if (galat != null) galat("Rilis itu tidak menyertakan berkas installer.");
                return null;
            }
            var tujuan = Path.Combine(Paths.Tmp, "Phoron-" + hasil.Versi + "-Setup.exe");
            try
            {
                var req = Siapkan(hasil.UrlInstaller);
                req.AllowAutoRedirect = true;
                req.Timeout = 60000;
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var masuk = resp.GetResponseStream())
                using (var keluar = File.Create(tujuan))
                {
                    long total = resp.ContentLength, sudah = 0;
                    var buf = new byte[81920];
                    int n;
                    while ((n = await masuk.ReadAsync(buf, 0, buf.Length, batal)) > 0)
                    {
                        batal.ThrowIfCancellationRequested();
                        await keluar.WriteAsync(buf, 0, n, batal);
                        sudah += n;
                        if (kemajuan != null)
                            kemajuan.Report(new KemajuanUnduh { Sudah = sudah, Total = total });
                    }
                }
                return tujuan;
            }
            catch (OperationCanceledException)
            {
                // Berkas separuh jadi dibuang: kalau ditinggal, unduhan berikutnya
                // menemukan berkas bernama sama dan menganggapnya sudah lengkap.
                try { if (File.Exists(tujuan)) File.Delete(tujuan); } catch { }
                if (galat != null) galat("Unduhan dibatalkan.");
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tujuan)) File.Delete(tujuan); } catch { }
                if (galat != null) galat("Gagal mengunduh: " + ex.Message);
                return null;
            }
        }
    }
}
