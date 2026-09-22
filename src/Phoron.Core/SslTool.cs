using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Membuat sertifikat self-signed untuk *.test supaya situs lokal bisa
    /// diakses lewat https tanpa peringatan (setelah dipasang ke Trusted Root).
    /// </summary>
    public static class SslTool
    {
        public static string CrtPath { get { return Path.Combine(Paths.EtcSsl, "phoron.crt"); } }
        public static string KeyPath { get { return Path.Combine(Paths.EtcSsl, "phoron.key"); } }

        public static bool Exists { get { return File.Exists(CrtPath) && File.Exists(KeyPath); } }

        /// <summary>openssl.exe ikut dalam paket Apache; tidak perlu unduhan terpisah.</summary>
        public static string FindOpenSsl(BinPackage apache)
        {
            if (apache == null) return null;
            var p = Path.Combine(apache.Path, "bin", "openssl.exe");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// Buat sertifikat wildcard untuk akhiran situs (mis. *.test) plus localhost.
        /// Mengembalikan pesan kesalahan, atau null bila berhasil.
        /// </summary>
        public static string Generate(BinPackage apache, string siteSuffix)
        {
            var openssl = FindOpenSsl(apache);
            if (openssl == null) return "openssl.exe tidak ditemukan di paket Apache yang dipilih.";

            var suffix = string.IsNullOrWhiteSpace(siteSuffix) ? "test" : siteSuffix.Trim().TrimStart('.');
            Directory.CreateDirectory(Paths.EtcSsl);

            // Ekstensi ditulis lewat berkas konfigurasi, bukan -addext: opsi itu
            // baru ada di OpenSSL 1.1.1, sedangkan paket Apache lama membawa 1.0.2.
            // Tanpa subjectAltName, browser modern menolak sertifikatnya mentah-mentah.
            var cnf = Path.Combine(Paths.Tmp, "phoron-ssl.cnf");
            var sb = new StringBuilder();
            sb.AppendLine("[req]");
            sb.AppendLine("default_bits = 2048");
            sb.AppendLine("prompt = no");
            sb.AppendLine("default_md = sha256");
            sb.AppendLine("distinguished_name = dn");
            sb.AppendLine("x509_extensions = ext");
            sb.AppendLine();
            sb.AppendLine("[dn]");
            sb.AppendLine("C = ID");
            sb.AppendLine("O = Phoron Local Development");
            sb.AppendLine("CN = *." + suffix);
            sb.AppendLine();
            sb.AppendLine("[ext]");
            sb.AppendLine("basicConstraints = critical, CA:TRUE");
            sb.AppendLine("keyUsage = critical, keyCertSign, cRLSign, digitalSignature, keyEncipherment");
            sb.AppendLine("subjectAltName = @alt");
            sb.AppendLine();
            sb.AppendLine("[alt]");
            sb.AppendLine("DNS.1 = *." + suffix);
            sb.AppendLine("DNS.2 = " + suffix);
            sb.AppendLine("DNS.3 = localhost");
            sb.AppendLine("IP.1 = 127.0.0.1");
            File.WriteAllText(cnf, sb.ToString(), new UTF8Encoding(false));

            var args = "req -x509 -nodes -days 3650 -newkey rsa:2048"
                     + " -keyout \"" + KeyPath + "\""
                     + " -out \"" + CrtPath + "\""
                     + " -config \"" + cnf + "\"";
            var env = new System.Collections.Generic.Dictionary<string, string>
            {
                // Tanpa OPENSSL_CONF, openssl bawaan Apache mencari berkas config di
                // jalur build-nya (c:\...\ssl) dan gagal dengan pesan yang membingungkan.
                { "OPENSSL_CONF", cnf },
            };
            var res = Shell.Run(openssl, args, Path.GetDirectoryName(openssl), 120000, env);
            // Sertifikatnya berganti, jadi jawaban "sudah dipercaya" yang
            // tersimpan menjawab pertanyaan tentang sertifikat yang sudah tidak
            // ada lagi.
            LupakanKepercayaan();
            if (!Exists) return "openssl gagal: " + res.All;
            return null;
        }

        /// <summary>
        /// Sudah terpasang di Trusted Root Windows? Dibandingkan lewat sidik jari,
        /// bukan nama subjek: sertifikat lama dengan subjek sama tapi kunci berbeda
        /// tetap membuat browser memperingatkan, dan itu justru keadaan yang paling
        /// membingungkan - "kan sudah saya pasang".
        /// </summary>
        public static bool IsTrusted
        {
            get
            {
                if (!Exists) return false;
                lock (_kunciPercaya)
                {
                    if (_adaJawaban && DateTime.UtcNow - _percayaWaktu < UmurPercaya)
                        return _percaya;
                    _percaya = HitungKepercayaan();
                    _adaJawaban = true;
                    _percayaWaktu = DateTime.UtcNow;
                    return _percaya;
                }
            }
        }

        // Jawabannya disinggahkan. Menelusuri gudang Trusted Root berarti
        // membaca RATUSAN sertifikat, dua gudang sekaligus - dan halaman Beranda
        // menanyakannya beberapa kali dalam satu penyegaran.
        //
        // Umurnya pendek, dan singgahannya dibuang tegas begitu Phoron sendiri
        // memasang sertifikatnya. Yang tidak boleh terjadi: orang menekan
        // "percayai sertifikat", Windows menerimanya, lalu layar tetap berkata
        // belum dipercaya.
        static readonly object _kunciPercaya = new object();
        static bool _percaya;
        static bool _adaJawaban;
        static DateTime _percayaWaktu = DateTime.MinValue;
        static readonly TimeSpan UmurPercaya = TimeSpan.FromSeconds(20);

        /// <summary>Lupakan jawaban <see cref="IsTrusted"/> yang tersimpan.</summary>
        public static void LupakanKepercayaan()
        {
            lock (_kunciPercaya) { _adaJawaban = false; }
        }

        static bool HitungKepercayaan()
        {
            string sidik;
            try
            {
                // Dibuang sesudah dipakai: X509Certificate2 memegang sumber daya
                // tak terkelola, dan properti ini dibaca berkali-kali per menit.
                using (var punya = new X509Certificate2(CrtPath)) sidik = punya.Thumbprint;
            }
            catch { return false; }

            foreach (var lokasi in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                try
                {
                    var store = new X509Store(StoreName.Root, lokasi);
                    store.Open(OpenFlags.ReadOnly);
                    try
                    {
                        foreach (var c in store.Certificates)
                        {
                            bool sama = string.Equals(c.Thumbprint, sidik,
                                                      StringComparison.OrdinalIgnoreCase);
                            c.Dispose();
                            if (sama) return true;
                        }
                    }
                    finally { store.Close(); }
                }
                catch { }
            }
            return false;
        }

        /// <summary>Pasang sertifikat ke Trusted Root Windows. Butuh hak admin.</summary>
        public static string Trust()
        {
            if (!Exists) return "Sertifikat belum dibuat.";
            if (!HostsFile.IsAdmin()) return "Perlu menjalankan Phoron sebagai Administrator.";
            var res = Shell.Run("certutil.exe", "-addstore -f Root \"" + CrtPath + "\"", Paths.Root, 60000);
            // Jawaban lama tidak berlaku lagi - gudangnya baru saja berubah.
            LupakanKepercayaan();
            return res.Ok ? null : "certutil gagal: " + res.All;
        }
    }
}
