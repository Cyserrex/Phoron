using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Phoron.Core
{
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
    /// Memakai API rilis, bukan mengikis halaman HTML: tata letak halaman
    /// berubah sewaktu-waktu tanpa pemberitahuan, sedangkan bentuk JSON-nya
    /// stabil. Penguraiannya tetap seadanya - Core tidak menarik satu pun
    /// dependensi, dan yang dibutuhkan hanya tiga medan.
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

        public static async Task<HasilCek> CekAsync()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(ApiTerbaru);
                // GitHub menolak permintaan tanpa User-Agent dengan 403.
                req.UserAgent = "Phoron/" + AppInfo.Version;
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
        public static async Task<string> UnduhInstallerAsync(HasilCek hasil, IProgress<int> kemajuan,
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
                var req = (HttpWebRequest)WebRequest.Create(hasil.UrlInstaller);
                req.UserAgent = "Phoron/" + AppInfo.Version;
                req.AllowAutoRedirect = true;
                req.Timeout = 60000;
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var masuk = resp.GetResponseStream())
                using (var keluar = File.Create(tujuan))
                {
                    long total = resp.ContentLength, sudah = 0;
                    var buf = new byte[81920];
                    int n;
                    while ((n = await masuk.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        await keluar.WriteAsync(buf, 0, n);
                        sudah += n;
                        if (kemajuan != null && total > 0)
                            kemajuan.Report((int)Math.Min(100, sudah * 100 / total));
                    }
                }
                return tujuan;
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
