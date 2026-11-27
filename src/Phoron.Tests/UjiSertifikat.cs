using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Sertifikat HTTPS: CA lokal terpisah dari sertifikat situs.
        ///
        /// Dulu Phoron membuat SATU sertifikat self-signed ber-CA:TRUE dan
        /// Apache menyajikannya langsung. Chrome menerimanya setelah dipercayai,
        /// Firefox tidak pernah (MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY) -
        /// dan "buat ulang" hanya menambah satu akar lagi yang tetap ditolak.
        /// Memakai openssl.exe sungguhan dari paket Apache yang ada di mesin ini,
        /// sebab yang perlu dipastikan adalah OpenSSL 1.0.2 bawaan Apache lama
        /// memang menerima berkas konfigurasi yang ditulis Phoron.
        /// </summary>
        static void UjiSertifikat()
        {
            Bagian("Sertifikat HTTPS: CA lokal + sertifikat situs");

            var apache = BinScanner.ScanAll(Settings.DefaultBinRoots()
                    .Concat(new[] { @"C:\laragon\bin" }).Distinct())
                .FirstOrDefault(p => p.Kind == BinKind.Apache && SslTool.FindOpenSsl(p) != null);
            if (apache == null)
            {
                Console.WriteLine("  (dilewati: tidak ada paket Apache yang membawa openssl.exe)");
                return;
            }

            try { Directory.Delete(Paths.EtcSsl, true); } catch { }
            Ok("Belum ada sertifikat di awal", !SslTool.Exists);

            var versiAwal = ConfigWriter.VersiKonfigWeb;
            var err = SslTool.Generate(apache, "test");
            Ok("Generate berhasil dengan openssl " + apache.Id, err == null, err ?? "");
            if (err != null) return;
            Ok("CA, sertifikat situs, dan kuncinya ada",
               SslTool.Exists && File.Exists(SslTool.CaKeyPath));
            Ok("Sertifikat baru menandai web server perlu dinyalakan ulang",
               ConfigWriter.VersiKonfigWeb != versiAwal);

            using (var ca = new X509Certificate2(SslTool.CaCrtPath))
            using (var situs = new X509Certificate2(SslTool.CrtPath))
            {
                var bcCa = ca.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                var bcSitus = situs.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                Ok("CA bertanda CA:TRUE", bcCa != null && bcCa.CertificateAuthority);
                Ok("Sertifikat situs BUKAN CA (syarat Firefox)",
                   bcSitus != null && !bcSitus.CertificateAuthority);

                var eku = situs.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
                Ok("Sertifikat situs untuk autentikasi server",
                   eku != null && eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                                     .Any(o => o.Value == "1.3.6.1.5.5.7.3.1"));

                var san = situs.Extensions.Cast<X509Extension>()
                               .FirstOrDefault(x => x.Oid.Value == "2.5.29.17");
                var sanTeks = san == null ? "" : san.Format(false);
                Ok("SAN memuat *.test dan localhost",
                   sanTeks.Contains("*.test") && sanTeks.Contains("localhost"), sanTeks);

                Ok("Sertifikat situs diterbitkan oleh CA Phoron", situs.Issuer == ca.Subject,
                   situs.Issuer + " vs " + ca.Subject);

                // Rantai dibangun dengan CA sebagai simpanan tambahan - tanpa
                // menyentuh Trusted Root mesin ini. Akar tak dikenal diizinkan;
                // yang diperiksa hanyalah bahwa rantainya berakhir di CA ini.
                using (var rantai = new X509Chain())
                {
                    rantai.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    rantai.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    rantai.ChainPolicy.ExtraStore.Add(ca);
                    bool dibangun = rantai.Build(situs);
                    var akar = rantai.ChainElements.Count > 0
                        ? rantai.ChainElements[rantai.ChainElements.Count - 1].Certificate : null;
                    Ok("Rantai sertifikat situs berakhir di CA Phoron",
                       dibangun && akar != null && akar.Thumbprint == ca.Thumbprint);
                }

                // CA tanpa batasan nama bisa menerbitkan sertifikat google.com
                // yang dipercaya seluruh mesin, dan kuncinya tergeletak di disk.
                var nc = ca.Extensions.Cast<X509Extension>().FirstOrDefault(x => x.Oid.Value == "2.5.29.30");
                Ok("CA punya batasan nama (nameConstraints)", nc != null && nc.Critical,
                   "CA tanpa batasan bisa memalsukan sertifikat situs mana pun");
                Ok("Batasan nama mencakup akhiran situs", SslTool.CaMencakup("test"),
                   nc == null ? "" : nc.Format(false));
                Ok("Batasan nama tidak mencakup akhiran lain", !SslTool.CaMencakup("com"),
                   nc == null ? "" : nc.Format(false));
            }

            UjiPalsuDitolak(apache);

            Ok("Batasan dicocokkan per nama utuh",
               SslTool.BatasanMemuat("DNS Name=testing, DNS Name=localhost", "localhost")
               && !SslTool.BatasanMemuat("DNS Name=testing", "test"));

            // Membuat ulang: sertifikat situs berganti, CA TETAP - itulah yang
            // membuat kepercayaan yang sudah dipasang tidak hilang.
            string sidikCa, seriLama;
            using (var ca = new X509Certificate2(SslTool.CaCrtPath)) sidikCa = ca.Thumbprint;
            using (var situs = new X509Certificate2(SslTool.CrtPath)) seriLama = situs.SerialNumber;
            err = SslTool.Generate(apache, "test");
            Ok("Generate ulang berhasil", err == null, err ?? "");
            using (var ca = new X509Certificate2(SslTool.CaCrtPath))
                Ok("Generate ulang memakai CA yang sama", ca.Thumbprint == sidikCa);
            using (var situs = new X509Certificate2(SslTool.CrtPath))
                Ok("Generate ulang menghasilkan sertifikat situs baru", situs.SerialNumber != seriLama);
            Ok("Tidak ada berkas sementara yang tertinggal",
               !File.Exists(SslTool.CrtPath + ".baru") && !File.Exists(SslTool.KeyPath + ".baru"));

            // Akhiran yang tidak dicakup CA: CA dibuat ulang, kunci lama hilang.
            Ok("Akhiran baru di luar batasan perlu sertifikat baru", SslTool.PerluDibuat("lokal"));
            err = SslTool.Generate(apache, "lokal", new[] { "test" });
            Ok("Generate untuk akhiran baru berhasil", err == null, err ?? "");
            using (var ca = new X509Certificate2(SslTool.CaCrtPath))
                Ok("Akhiran di luar batasan membuat CA baru", ca.Thumbprint != sidikCa);
            Ok("CA baru mencakup akhiran baru dan akhiran profil lain",
               SslTool.CaMencakup("lokal") && SslTool.CaMencakup("test"));
            Ok("Tidak perlu dibuat ulang lagi", !SslTool.PerluDibuat("lokal") && !SslTool.PerluDibuat("test"));

            // Instalasi lama: phoron.crt model lama tanpa CA. Harus dianggap
            // belum ada, supaya Engine membuatkan pasangan baru.
            File.Delete(SslTool.CaCrtPath);
            Ok("Sertifikat model lama (tanpa CA) dianggap belum ada", !SslTool.Exists);
        }

        /// <summary>
        /// Bukti bahwa batasan nama sungguh ditegakkan, bukan hanya tertulis:
        /// kunci CA dipakai menandatangani sertifikat google.com, dan Windows
        /// harus menolaknya karena melanggar batasan.
        /// </summary>
        static void UjiPalsuDitolak(BinPackage apache)
        {
            var openssl = SslTool.FindOpenSsl(apache);
            var kerja = Path.Combine(Paths.Tmp, "uji-palsu");
            Directory.CreateDirectory(kerja);
            var cnf = Path.Combine(kerja, "palsu.cnf");
            File.WriteAllText(cnf, "[req]\nprompt = no\ndistinguished_name = dn\n[dn]\nCN = google.com\n"
                                 + "[e]\nbasicConstraints = critical, CA:FALSE\nextendedKeyUsage = serverAuth\n"
                                 + "subjectAltName = DNS:google.com\n");
            var key = Path.Combine(kerja, "palsu.key");
            var csr = Path.Combine(kerja, "palsu.csr");
            var crt = Path.Combine(kerja, "palsu.crt");
            var dir = Path.GetDirectoryName(openssl);
            Shell.Run(openssl, "req -new -nodes -newkey rsa:2048 -keyout \"" + key + "\" -out \"" + csr
                               + "\" -config \"" + cnf + "\"", dir, 60000);
            var r = Shell.Run(openssl, "x509 -req -days 1 -in \"" + csr + "\" -CA \"" + SslTool.CaCrtPath
                                       + "\" -CAkey \"" + SslTool.CaKeyPath + "\" -set_serial 7 -out \"" + crt
                                       + "\" -extfile \"" + cnf + "\" -extensions e", dir, 60000);
            if (!File.Exists(crt)) { Ok("Sertifikat palsu bisa dibuat untuk diuji", false, r.All); return; }

            // OpenSSL, bukan X509Chain: CryptoAPI berhenti di UntrustedRoot untuk
            // akar yang belum dipasang dan tidak pernah memeriksa batasan namanya,
            // jadi uji lewat X509Chain lulus apa pun isinya. OpenSSL menegakkan
            // nameConstraints milik trust anchor, seperti Chrome dan Firefox.
            var asli = Shell.Run(openssl, "verify -CAfile \"" + SslTool.CaCrtPath + "\" \"" + SslTool.CrtPath + "\"",
                                 dir, 60000);
            Ok("Sertifikat situs lolos batasan nama (openssl verify)",
               asli.Ok && asli.All.Contains(": OK"), asli.All);
            var palsu = Shell.Run(openssl, "verify -CAfile \"" + SslTool.CaCrtPath + "\" \"" + crt + "\"", dir, 60000);
            Ok("Sertifikat google.com dari CA Phoron ditolak (openssl verify)",
               !palsu.Ok && palsu.All.IndexOf("permitted subtree", StringComparison.OrdinalIgnoreCase) >= 0,
               palsu.All);
            try { Directory.Delete(kerja, true); } catch { }
        }
    }
}
