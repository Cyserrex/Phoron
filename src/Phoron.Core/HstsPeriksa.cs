using System;
using System.Net;
using System.Threading;

namespace Phoron.Core
{
    /// <summary>
    /// Mendeteksi header Strict-Transport-Security yang dikirim server
    /// pengembangan di localhost.
    ///
    /// Phoron TIDAK bisa memperbaiki keadaan ini sendiri: headernya milik
    /// proyek pengguna, dan catatan yang terlanjur tersimpan ada di dalam
    /// browser, bukan di berkas mana pun yang boleh disentuh Phoron. Yang bisa
    /// - dan perlu - dilakukan Phoron hanyalah MENJELASKANNYA, sebab akibatnya
    /// muncul di tempat yang sama sekali lain dari sebabnya: satu proyek Node
    /// yang dijalankan dengan HTTPS membuat situs PHP di port 80 tidak bisa
    /// dibuka, bahkan lama sesudah server Node itu dimatikan.
    ///
    /// Sebabnya: HSTS melekat pada NAMA HOST dan mengabaikan nomor port. Satu
    /// respons dari https://localhost:3000 mengunci seluruh "localhost" - port
    /// 80 sekalipun - ke HTTPS selama max-age yang diminta.
    /// </summary>
    public static class HstsPeriksa
    {
        public const string NamaHeader = "Strict-Transport-Security";

        /// <summary>
        /// Apakah alamat ini bisa membuat browser menyimpan catatan HSTS.
        ///
        /// Dua syaratnya sering dilupakan. Pertama, header HSTS lewat HTTP
        /// polos DIABAIKAN browser - hanya respons HTTPS yang dicatat. Kedua,
        /// browser tidak menyimpan HSTS untuk alamat IP telanjang, jadi
        /// https://127.0.0.1:3000 tidak menulari apa pun; yang berbahaya justru
        /// nama "localhost" yang terlihat paling tidak berbahaya.
        /// </summary>
        public static bool Berlaku(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;
            if (u.HostNameType != UriHostNameType.Dns) return false;
            var host = u.Host;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Nilai header HSTS yang dikirim alamat ini, atau null bila tidak ada.
        /// Tidak pernah melempar: ini pemeriksaan penyedap, dan kegagalannya
        /// tidak boleh mengganggu proyek yang baru saja berhasil dijalankan.
        /// </summary>
        public static string BacaHeader(string url)
        {
            if (!Berlaku(url)) return null;
            // Server pengembangan mencetak alamatnya beberapa saat SEBELUM ia
            // benar-benar menerima sambungan, jadi percobaan pertama yang gagal
            // belum berarti apa-apa.
            for (int coba = 0; coba < 3; coba++)
            {
                var nilai = SekaliJalan(url);
                if (nilai != null) return nilai;
                if (coba < 2) Thread.Sleep(700);
            }
            return null;
        }

        static string SekaliJalan(string url)
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)3072; }
                catch { }

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                // Pengalihan tidak diikuti: yang ditanyakan adalah header dari
                // alamat INI, bukan dari tempat ia menyuruh pergi.
                req.AllowAutoRedirect = false;
                req.UserAgent = "Phoron";
                // Proxy DIMATIKAN, bukan dibiarkan bawaan. Di mesin berproxy,
                // bawaan .NET akan mencari konfigurasi proxy lewat WPAD lalu
                // meneruskan permintaan ini ke sana - padahal alamat yang dituju
                // ada di mesin ini sendiri. Hasilnya jawaban dari proxy, bukan
                // dari server pengembangan yang sedang ditanyai.
                req.Proxy = null;

                // Sertifikat diabaikan PER-PERMINTAAN, bukan lewat
                // ServicePointManager.ServerCertificateValidationCallback yang
                // berlaku untuk seluruh proses. Yang kedua akan ikut mematikan
                // pemeriksaan sertifikat pada pengunduh dan pemeriksa pembaruan
                // - membuka lubang sungguhan demi kenyamanan sesaat.
                req.ServerCertificateValidationCallback = (a, b, c, d) => true;

                using (var res = (HttpWebResponse)req.GetResponse())
                    return res.Headers[NamaHeader];
            }
            catch (WebException ex)
            {
                // 404 dan 405 tetap membawa header responsnya, dan header itulah
                // yang dicari - server yang menolak HEAD pun sudah cukup
                // menjawab pertanyaan kita.
                var res = ex.Response as HttpWebResponse;
                if (res == null) return null;
                using (res) return res.Headers[NamaHeader];
            }
            catch { return null; }
        }

        /// <summary>
        /// Kalimat peringatan untuk catatan aktivitas, atau null bila alamat ini
        /// tidak bermasalah. <paramref name="nama"/> adalah nama proyeknya.
        /// </summary>
        public static string Keluhan(string nama, string url, string nilaiHeader)
        {
            if (string.IsNullOrEmpty(nilaiHeader)) return null;
            var hari = HariDariMaxAge(nilaiHeader);
            return Lang.T("PERINGATAN HSTS: {0} mengirim header Strict-Transport-Security lewat HTTPS di localhost.", nama)
                 + " " + Lang.T("Browser akan memaksa SEMUA alamat localhost ke HTTPS - termasuk situs PHP di port 80 - selama {0} hari, bahkan sesudah server ini dimatikan.", hari)
                 + " " + Lang.T("Matikan header itu saat pengembangan, lalu buang catatannya lewat Riwayat > klik kanan situs > Lupakan Situs Ini.");
        }

        /// <summary>max-age dalam hari, dibulatkan ke bawah; 0 bila tidak terbaca.</summary>
        public static long HariDariMaxAge(string nilaiHeader)
        {
            if (string.IsNullOrEmpty(nilaiHeader)) return 0;
            foreach (var bagian in nilaiHeader.Split(';'))
            {
                var b = bagian.Trim();
                if (!b.StartsWith("max-age", StringComparison.OrdinalIgnoreCase)) continue;
                var sama = b.IndexOf('=');
                if (sama < 0) continue;
                long detik;
                if (long.TryParse(b.Substring(sama + 1).Trim().Trim('"'), out detik) && detik > 0)
                    return detik / 86400;
            }
            return 0;
        }
    }
}
