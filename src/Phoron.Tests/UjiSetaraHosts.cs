using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Bahan uji setara fitur "berkas hosts dan HTTPS" (daemon\inti\tests\setara_hosts.rs).
    ///
    /// Tiap kasus: masuk\kasus\&lt;nama&gt;\ berisi berkas hosts awal (ANSI, UTF-8,
    /// BOM, penanda rusak, ...) dan daftar nama; Core C# menjalankan Sync di
    /// sandbox sendiri dan menulis hasilnya ke csharp\&lt;nama&gt;\. Rust harus
    /// menghasilkan berkas hosts dan cadangan asli yang identik byte demi byte.
    ///
    /// Bagian SSL hanya membaca paket Apache yang ada (openssl.exe) dan menulis
    /// sertifikat di sandbox - tidak pernah memasang apa pun ke Trusted Root.
    /// Berkas hosts sistem tidak pernah disentuh: Paths.HostsFile selalu
    /// diarahkan ke berkas sementara.
    /// </summary>
    internal static class UjiSetara_hosts
    {
        const string B = HostsFile.Begin;
        const string E = HostsFile.End;

        public static int Jalankan(string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var masuk = Path.Combine(folder, "masuk");
            var keluar = Path.Combine(folder, "csharp");
            Directory.CreateDirectory(masuk);
            Directory.CreateDirectory(keluar);
            var rootLama = Paths.Root;
            var hostsLama = Paths.HostsFile;
            try
            {
                Kasus(masuk);
                foreach (var d in Directory.GetDirectories(Path.Combine(masuk, "kasus")))
                    JalankanKasus(d, Path.Combine(keluar, Path.GetFileName(d)));
                Batasan(folder);
                Hsts(folder);
                Ssl(folder);
            }
            finally
            {
                Paths.HostsFile = hostsLama;
                Paths.Root = rootLama;
            }
            Console.WriteLine("bahan uji setara hosts ditulis ke " + folder);
            return 0;
        }

        static string Crlf(params string[] baris)
        {
            return string.Join("\r\n", baris) + "\r\n";
        }

        static void Buat(string masuk, string nama, byte[] hosts, string[] nama1,
                         string[] nama2 = null, bool pulihkan = false, bool readOnly = false)
        {
            var d = Path.Combine(masuk, "kasus", nama);
            Directory.CreateDirectory(d);
            if (hosts != null) File.WriteAllBytes(Path.Combine(d, "hosts"), hosts);
            File.WriteAllText(Path.Combine(d, "nama.txt"), string.Join("\n", nama1), new UTF8Encoding(false));
            if (nama2 != null)
                File.WriteAllText(Path.Combine(d, "nama2.txt"), string.Join("\n", nama2), new UTF8Encoding(false));
            if (pulihkan) File.WriteAllText(Path.Combine(d, "pulihkan"), "");
            if (readOnly) File.WriteAllText(Path.Combine(d, "readonly"), "");
        }

        static byte[] U8(string s) { return new UTF8Encoding(false).GetBytes(s); }

        static void Kasus(string masuk)
        {
            var ansi = Encoding.Default;
            Buat(masuk, "ansi", ansi.GetBytes(Crlf("# komentar café", "127.0.0.1 localhost")),
                 new[] { "toko.test", "blog.test" }, new[] { "toko.test" });
            Buat(masuk, "ansi-tepi", Enumerable.Concat(ansi.GetBytes("# tepi "),
                     new byte[] { 0x80, 0x81, 0x8D, 0x9D, 0xA0, 0xE9, 0xFF, 0x0D, 0x0A })
                     .Concat(ansi.GetBytes("10.0.0.9 nas\r\n")).ToArray(),
                 new[] { "a.test" });
            Buat(masuk, "utf8-lf", U8("# catatan ünïcode ✓\n10.0.0.1 kantor.internal\n"),
                 new[] { "a.test" }, new[] { "a.test", "b.test" });
            Buat(masuk, "utf8-bom", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(U8(Crlf("# berbom é", "127.0.0.1 localhost"))).ToArray(),
                 new[] { "x.test" });
            Buat(masuk, "bom-rusak", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(U8("# rusak ")).Concat(new byte[] { 0xFF, 0x41, 0x0D, 0x0A }).ToArray(),
                 new[] { "x.test" });
            Buat(masuk, "begin-hilang", U8(Crlf("127.0.0.1 localhost", "10.0.0.1 kantor.internal", "",
                     "127.0.0.1\tlama.test", "::1\tlama.test", E)),
                 new[] { "baru.test" }, new[] { "baru.test" });
            Buat(masuk, "end-hilang", U8(Crlf("127.0.0.1 localhost", B, "127.0.0.1\tlama.test", "::1\tlama.test",
                     "# catatan pengguna", "10.0.0.2 printer.kantor")),
                 new[] { "baru.test" });
            Buat(masuk, "dua-blok", U8(Crlf("127.0.0.1 localhost", B, "127.0.0.1\ta.test", E,
                     "10.0.0.3 nas.rumah", B, "127.0.0.1\tb.test", E)),
                 new[] { "c.test" });
            Buat(masuk, "begin-berspasi", U8(Crlf(B, "127.0.0.1\tx.test", "127.0.0.1 milikku.test", "  " + E + "  ")),
                 new[] { "y.test" });
            // Sudah dalam bentuk akhir, tapi ber-LF: tidak boleh ditulis ulang.
            Buat(masuk, "sudah-sama", U8("127.0.0.1 localhost\n\n" + B + "\n127.0.0.1\ta.test\n::1\ta.test\n" + E + "\n\n\n"),
                 new[] { "a.test" });
            Buat(masuk, "kosongkan", U8(Crlf("127.0.0.1 localhost", "", B, "127.0.0.1\ta.test", "::1\ta.test", E)),
                 new string[0]);
            Buat(masuk, "tidak-ada", null, new[] { "a.test" }, new[] { "a.test", "b.test" });
            Buat(masuk, "tidak-ada-kosong", null, new string[0]);
            Buat(masuk, "nama-campur", U8(Crlf("127.0.0.1 localhost")),
                 new[] { " Zeta.test", "alpha.test", "ALPHA.test", "", "   ", "beta.TEST", "_x.test", "10-a.test" });
            Buat(masuk, "ekor-kosong", U8("127.0.0.1 localhost\r\n \r\n\t\r\n\r\n"), new[] { "a.test" });
            Buat(masuk, "cr-saja", U8("127.0.0.1 localhost\r# cr\r10.0.0.1 x"), new[] { "a.test" });
            Buat(masuk, "readonly", U8(Crlf("127.0.0.1 localhost")), new[] { "a.test" }, null, false, true);
            Buat(masuk, "pulihkan", ansi.GetBytes(Crlf("# asli café", "127.0.0.1 localhost")),
                 new[] { "a.test" }, new[] { "b.test" }, true);
        }

        static string[] BacaNama(string f)
        {
            if (!File.Exists(f)) return null;
            var t = File.ReadAllText(f, Encoding.UTF8);
            return t.Length == 0 ? new string[0] : t.Split('\n');
        }

        static void JalankanKasus(string dMasuk, string dKeluar)
        {
            Directory.CreateDirectory(dKeluar);
            var akar = Path.Combine(dKeluar, "akar");
            Directory.CreateDirectory(akar);
            Paths.Root = akar;
            var hosts = Path.Combine(dKeluar, "hosts");
            Paths.HostsFile = hosts;
            var asal = Path.Combine(dMasuk, "hosts");
            if (File.Exists(asal)) File.Copy(asal, hosts);
            if (File.Exists(Path.Combine(dMasuk, "readonly")))
                File.SetAttributes(hosts, File.GetAttributes(hosts) | FileAttributes.ReadOnly);

            var hasil = new List<string>();
            hasil.Add("sync1=" + Coba(() => HostsFile.Sync(BacaNama(Path.Combine(dMasuk, "nama.txt")))));
            var n2 = BacaNama(Path.Combine(dMasuk, "nama2.txt"));
            if (n2 != null) hasil.Add("sync2=" + Coba(() => HostsFile.Sync(n2)));
            // Cadangan bertanggal bernama per detik: dua cadangan dalam satu
            // detik saling menimpa, jadi jumlahnya hanya dibandingkan di kasus
            // yang paling banyak membuat satu.
            if (File.Exists(Path.Combine(dMasuk, "pulihkan")))
                hasil.Add("pulihkan=" + Coba(() => HostsFile.Pulihkan(
                    Path.Combine(HostsFile.FolderCadangan, HostsFile.NamaAsli))));
            else
                hasil.Add("cadangan=" + HostsFile.DaftarCadangan().Count(c => !c.Asli));
            hasil.Add("asli=" + File.Exists(Path.Combine(HostsFile.FolderCadangan, HostsFile.NamaAsli)));
            hasil.Add("readonly=" + (File.Exists(hosts) && (File.GetAttributes(hosts) & FileAttributes.ReadOnly) != 0));
            hasil.Add("nama=" + string.Join(",", HostsFile.AllNames().Select(x => x.ToLowerInvariant())
                                                  .OrderBy(x => x, StringComparer.Ordinal)));
            File.WriteAllText(Path.Combine(dKeluar, "hasil.txt"), string.Join("\n", hasil), new UTF8Encoding(false));

            var asli = Path.Combine(HostsFile.FolderCadangan, HostsFile.NamaAsli);
            var salinan = Path.Combine(dKeluar, "hosts-asli.bak");
            if (File.Exists(asli)) File.Copy(asli, salinan);
            // File.Copy ikut membawa atribut ReadOnly dari hosts yang read-only.
            foreach (var f in new[] { hosts, salinan }.Concat(Directory.GetFiles(akar, "*", SearchOption.AllDirectories)))
                if (File.Exists(f)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(akar, true);
        }

        static string Coba(Action a)
        {
            try { a(); return "ok"; }
            catch (UnauthorizedAccessException) { return "tidak-berhak"; }
            catch (Exception) { return "galat"; }
        }

        static readonly string[][] MasukBatasan =
        {
            new[] { "test" },
            new[] { "" },
            new[] { " .Lokal. ", "test", "", "dev", "LOKAL" },
            new[] { "local", "local", "Local", "  app  " },
        };

        static void Batasan(string folder)
        {
            var masuk = MasukBatasan.Select(x => string.Join("|", x)).ToArray();
            var keluar = MasukBatasan.Select(x => SslTool.BatasanNama(x[0], x.Skip(1))).ToArray();
            File.WriteAllText(Path.Combine(folder, "masuk", "batasan.txt"), string.Join("\n", masuk), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "csharp", "batasan.txt"), string.Join("\n", keluar), new UTF8Encoding(false));
        }

        static readonly string[] MasukHsts =
        {
            "https://localhost:3000/", "HTTPS://LocalHost", "https://app.localhost:5173/x?y", "http://localhost:3000",
            "https://127.0.0.1:3000", "https://[::1]:3000", "https://contoh.test", "https://notlocalhost",
            "localhost:3000", "https://localhost:abc", "", "https://user@localhost:8443", "https://localhost./",
            "max-age=31536000; includeSubDomains", "includeSubDomains; MAX-AGE = \"172800\"", "max-age=0",
            "max-age=abc", "max-age=+86400", "preload;max-age=90000",
        };

        static void Hsts(string folder)
        {
            File.WriteAllText(Path.Combine(folder, "masuk", "hsts.txt"), string.Join("\n", MasukHsts), new UTF8Encoding(false));
            var keluar = MasukHsts.Select(x => (HstsPeriksa.Berlaku(x) ? "1" : "0") + "|" + HstsPeriksa.HariDariMaxAge(x));
            File.WriteAllText(Path.Combine(folder, "csharp", "hsts.txt"), string.Join("\n", keluar), new UTF8Encoding(false));
        }

        /// <summary>
        /// CA + sertifikat situs C# di sandbox, bila ada openssl.exe di paket
        /// Apache yang terpasang (hanya dibaca). Rust memeriksa CA buatan C#
        /// dan membandingkan berkas cnf-nya.
        /// </summary>
        static void Ssl(string folder)
        {
            var apache = BinScanner.ScanAll(Settings.DefaultBinRoots()
                    .Concat(new[] { @"C:\laragon\bin" }).Distinct())
                .FirstOrDefault(p => p.Kind == BinKind.Apache && SslTool.FindOpenSsl(p) != null);
            if (apache == null) return;
            File.WriteAllText(Path.Combine(folder, "masuk", "apache.txt"), apache.Path, new UTF8Encoding(false));
            var akar = Path.Combine(folder, "csharp", "ssl-akar");
            Directory.CreateDirectory(akar);
            Paths.Root = akar;
            var err = SslTool.Generate(apache, "test", new[] { "local" });
            File.WriteAllText(Path.Combine(folder, "csharp", "ssl.txt"),
                "generate=" + (err == null ? "ok" : "galat") + "\nmencakup-test=" + SslTool.CaMencakup("test")
                + "\nmencakup-local=" + SslTool.CaMencakup("local") + "\nmencakup-com=" + SslTool.CaMencakup("com"),
                new UTF8Encoding(false));
        }
    }
}
