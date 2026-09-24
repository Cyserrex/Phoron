using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Membuat sertifikat untuk *.test supaya situs lokal bisa diakses lewat
    /// https tanpa peringatan (setelah CA-nya dipasang ke Trusted Root).
    ///
    /// Dua sertifikat, bukan satu: CA lokal (phoron-ca.crt) yang dipercayai
    /// Windows, dan sertifikat situs (phoron.crt) yang ditandatangani CA itu dan
    /// dipakai Apache. Dulu keduanya satu sertifikat self-signed ber-CA:TRUE.
    /// Chrome dan Edge menerimanya, tapi Firefox TIDAK PERNAH - sekalipun sudah
    /// dipasang ke Trusted Root dan enterprise_roots aktif - karena mozilla::pkix
    /// menolak sertifikat CA yang dipakai sebagai sertifikat server
    /// (MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY). Dengan CA terpisah,
    /// sertifikat situs juga bisa dibuat ulang tanpa harus dipercayai lagi.
    /// </summary>
    public static class SslTool
    {
        public static string CrtPath { get { return Path.Combine(Paths.EtcSsl, "phoron.crt"); } }
        public static string KeyPath { get { return Path.Combine(Paths.EtcSsl, "phoron.key"); } }
        public static string CaCrtPath { get { return Path.Combine(Paths.EtcSsl, "phoron-ca.crt"); } }
        public static string CaKeyPath { get { return Path.Combine(Paths.EtcSsl, "phoron-ca.key"); } }

        /// <summary>
        /// CA ikut disyaratkan: instalasi lama hanya punya phoron.crt model
        /// lama, dan dengan begini Engine membuatkan pasangan baru pada Apply
        /// berikutnya alih-alih terus menyajikan sertifikat yang ditolak Firefox.
        /// </summary>
        public static bool Exists
        {
            get { return File.Exists(CrtPath) && File.Exists(KeyPath) && File.Exists(CaCrtPath); }
        }

        const string Organisasi = "Phoron Local Development";

        /// <summary>openssl.exe ikut dalam paket Apache; tidak perlu unduhan terpisah.</summary>
        public static string FindOpenSsl(BinPackage apache)
        {
            if (apache == null) return null;
            var p = Path.Combine(apache.Path, "bin", "openssl.exe");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// Buat sertifikat wildcard untuk akhiran situs (mis. *.test) plus
        /// localhost, ditandatangani CA lokal. CA yang sudah ada DIPAKAI ULANG,
        /// jadi kepercayaan yang sudah dipasang tetap berlaku. Mengembalikan
        /// pesan kesalahan, atau null bila berhasil.
        /// </summary>
        public static string Generate(BinPackage apache, string siteSuffix)
        {
            var openssl = FindOpenSsl(apache);
            if (openssl == null) return "openssl.exe tidak ditemukan di paket Apache yang dipilih.";

            var suffix = string.IsNullOrWhiteSpace(siteSuffix) ? "test" : siteSuffix.Trim().TrimStart('.');
            Directory.CreateDirectory(Paths.EtcSsl);
            Directory.CreateDirectory(Paths.Tmp);
            var kerja = Path.GetDirectoryName(openssl);

            // Ekstensi ditulis lewat berkas konfigurasi, bukan -addext: opsi itu
            // baru ada di OpenSSL 1.1.1, sedangkan paket Apache lama membawa 1.0.2.
            if (!File.Exists(CaCrtPath) || !File.Exists(CaKeyPath))
            {
                var caCnf = Path.Combine(Paths.Tmp, "phoron-ca.cnf");
                var ca = new StringBuilder();
                ca.AppendLine("[req]");
                ca.AppendLine("default_bits = 2048");
                ca.AppendLine("prompt = no");
                ca.AppendLine("default_md = sha256");
                ca.AppendLine("distinguished_name = dn");
                ca.AppendLine("x509_extensions = ca");
                ca.AppendLine();
                ca.AppendLine("[dn]");
                ca.AppendLine("C = ID");
                ca.AppendLine("O = " + Organisasi);
                ca.AppendLine("CN = Phoron Local CA");
                ca.AppendLine();
                ca.AppendLine("[ca]");
                // pathlen:0 - CA ini hanya boleh menandatangani sertifikat situs,
                // bukan CA lain. Kuncinya tergeletak di disk; batasi apa yang
                // bisa dilakukan dengannya.
                ca.AppendLine("basicConstraints = critical, CA:TRUE, pathlen:0");
                ca.AppendLine("keyUsage = critical, keyCertSign, cRLSign");
                ca.AppendLine("subjectKeyIdentifier = hash");
                File.WriteAllText(caCnf, ca.ToString(), new UTF8Encoding(false));

                var resCa = JalankanOpenSsl(openssl, kerja, caCnf,
                    "req -x509 -nodes -days 3650 -newkey rsa:2048"
                    + " -keyout \"" + CaKeyPath + "\" -out \"" + CaCrtPath + "\""
                    + " -config \"" + caCnf + "\"");
                // CA baru berarti jawaban "sudah dipercaya" yang tersimpan
                // menjawab pertanyaan tentang CA yang sudah tidak ada lagi.
                LupakanKepercayaan();
                if (!File.Exists(CaCrtPath) || !File.Exists(CaKeyPath))
                    return "openssl gagal membuat CA: " + resCa.All;
            }

            var cnf = Path.Combine(Paths.Tmp, "phoron-ssl.cnf");
            var csr = Path.Combine(Paths.Tmp, "phoron.csr");
            var sb = new StringBuilder();
            sb.AppendLine("[req]");
            sb.AppendLine("default_bits = 2048");
            sb.AppendLine("prompt = no");
            sb.AppendLine("default_md = sha256");
            sb.AppendLine("distinguished_name = dn");
            sb.AppendLine();
            sb.AppendLine("[dn]");
            sb.AppendLine("C = ID");
            sb.AppendLine("O = " + Organisasi);
            sb.AppendLine("CN = *." + suffix);
            sb.AppendLine();
            sb.AppendLine("[leaf]");
            sb.AppendLine("basicConstraints = critical, CA:FALSE");
            sb.AppendLine("keyUsage = critical, digitalSignature, keyEncipherment");
            sb.AppendLine("extendedKeyUsage = serverAuth");
            sb.AppendLine("subjectKeyIdentifier = hash");
            sb.AppendLine("authorityKeyIdentifier = keyid");
            // Tanpa subjectAltName, browser modern menolak sertifikatnya mentah-mentah.
            sb.AppendLine("subjectAltName = @alt");
            sb.AppendLine();
            sb.AppendLine("[alt]");
            sb.AppendLine("DNS.1 = *." + suffix);
            sb.AppendLine("DNS.2 = " + suffix);
            sb.AppendLine("DNS.3 = localhost");
            sb.AppendLine("IP.1 = 127.0.0.1");
            sb.AppendLine("IP.2 = ::1");
            File.WriteAllText(cnf, sb.ToString(), new UTF8Encoding(false));

            // Ditulis ke nama sementara lalu dipindah: Apache yang sedang jalan
            // tidak boleh sempat melihat pasangan crt/key yang tidak cocok.
            var crtBaru = CrtPath + ".baru";
            var keyBaru = KeyPath + ".baru";
            try { File.Delete(crtBaru); File.Delete(keyBaru); File.Delete(csr); } catch { }

            var res = JalankanOpenSsl(openssl, kerja, cnf,
                "req -new -nodes -newkey rsa:2048"
                + " -keyout \"" + keyBaru + "\" -out \"" + csr + "\""
                + " -config \"" + cnf + "\"");
            if (!File.Exists(keyBaru) || !File.Exists(csr)) return "openssl gagal: " + res.All;

            // Nomor seri acak, bukan -CAcreateserial yang mulai dari angka kecil
            // di berkas .srl: kalau CA dibuat ulang dengan nama yang sama, seri
            // yang berulang membuat Firefox menolak dengan
            // SEC_ERROR_REUSED_ISSUER_AND_SERIAL.
            var seri = "0x" + Guid.NewGuid().ToString("N").Substring(0, 16);
            res = JalankanOpenSsl(openssl, kerja, cnf,
                "x509 -req -sha256 -days 3650"
                + " -in \"" + csr + "\""
                + " -CA \"" + CaCrtPath + "\" -CAkey \"" + CaKeyPath + "\""
                + " -set_serial " + seri
                + " -extfile \"" + cnf + "\" -extensions leaf"
                + " -out \"" + crtBaru + "\"");
            try { File.Delete(csr); } catch { }
            if (!File.Exists(crtBaru)) return "openssl gagal: " + res.All;

            try
            {
                GantiBerkas(keyBaru, KeyPath);
                GantiBerkas(crtBaru, CrtPath);
            }
            catch (Exception ex) { return "Sertifikat tidak bisa disimpan: " + ex.Message; }
            ConfigWriter.TandaiWebBerubah();
            return null;
        }

        static Shell.RunResult JalankanOpenSsl(string openssl, string kerja, string cnf, string args)
        {
            var env = new System.Collections.Generic.Dictionary<string, string>
            {
                // Tanpa OPENSSL_CONF, openssl bawaan Apache mencari berkas config di
                // jalur build-nya (c:\...\ssl) dan gagal dengan pesan yang membingungkan.
                { "OPENSSL_CONF", cnf },
            };
            return Shell.Run(openssl, args, kerja, 120000, env);
        }

        static void GantiBerkas(string sumber, string tujuan)
        {
            if (File.Exists(tujuan)) File.Delete(tujuan);
            File.Move(sumber, tujuan);
        }

        /// <summary>
        /// CA-nya sudah terpasang di Trusted Root Windows? Dibandingkan lewat sidik jari,
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
                using (var punya = new X509Certificate2(CaCrtPath)) sidik = punya.Thumbprint;
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

        /// <summary>Pasang CA ke Trusted Root Windows. Butuh hak admin.</summary>
        public static string Trust()
        {
            if (!Exists) return "Sertifikat belum dibuat.";
            if (!HostsFile.IsAdmin()) return "Perlu menjalankan Phoron sebagai Administrator.";
            var res = Shell.Run("certutil.exe", "-addstore -f Root \"" + CaCrtPath + "\"", Paths.Root, 60000);
            // Jawaban lama tidak berlaku lagi - gudangnya baru saja berubah.
            LupakanKepercayaan();
            if (!res.Ok) return "certutil gagal: " + res.All;
            BuangAkarLama();
            return null;
        }

        /// <summary>
        /// Buang sertifikat Phoron lama dari Trusted Root: setiap "buat ulang"
        /// model lama meninggalkan satu akar yang kuncinya sudah tidak dipakai.
        /// Hanya yang diterbitkan Phoron sendiri (O=Phoron Local Development,
        /// self-signed) dan bukan CA yang sekarang. Gagal diam-diam: ini bersih-
        /// bersih, bukan syarat supaya HTTPS jalan.
        /// </summary>
        static void BuangAkarLama()
        {
            string sidik;
            try { using (var punya = new X509Certificate2(CaCrtPath)) sidik = punya.Thumbprint; }
            catch { return; }

            foreach (var lokasi in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                try
                {
                    var store = new X509Store(StoreName.Root, lokasi);
                    store.Open(OpenFlags.ReadWrite);
                    try
                    {
                        foreach (var c in store.Certificates)
                        {
                            bool milikPhoron = c.Subject == c.Issuer
                                && c.Subject.IndexOf("O=" + Organisasi, StringComparison.Ordinal) >= 0;
                            if (milikPhoron && !string.Equals(c.Thumbprint, sidik, StringComparison.OrdinalIgnoreCase))
                            {
                                try { store.Remove(c); } catch { }
                            }
                            c.Dispose();
                        }
                    }
                    finally { store.Close(); }
                }
                catch { }
            }
        }
    }
}
