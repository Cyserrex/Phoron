using System;
using System.IO;
using System.Linq;
using System.Text;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Nama host ASCII, encoding berkas hosts yang dipertahankan, dan
        /// pengenalan port bentrok proyek Node.
        /// </summary>
        static void UjiHostDanPort(string sandbox)
        {
            Bagian("Nama host, encoding hosts, port Node");

            // ------------------------------------------- Nama host ASCII
            Ok("Huruf beraksen diturunkan ke huruf dasar", SiteScanner.SafeHost("kafé") == "kafe", SiteScanner.SafeHost("kafé"));
            Ok("Spasi dan aksen jadi nama host yang sah", SiteScanner.SafeHost("Résumé App") == "resume-app",
               SiteScanner.SafeHost("Résumé App"));
            Ok("Aksara non-Latin tidak lolos ke nama host", SiteScanner.SafeHost("日本") == "situs", SiteScanner.SafeHost("日本"));
            Ok("Nama biasa tidak berubah", SiteScanner.SafeHost("sotk_bsc") == "sotk-bsc");

            // ------------------------------------------- Encoding hosts
            // Akar tersendiri: folder cadangan harus kosong supaya hosts-asli.bak
            // yang diperiksa benar-benar dibuat dari berkas ANSI ini.
            var hostsLama = Paths.HostsFile;
            var akarLama = Paths.Root;
            Paths.Root = Path.Combine(sandbox, "hosts-ansi-akar");
            Directory.CreateDirectory(Paths.Root);
            var hosts = Path.Combine(Paths.Root, "hosts");
            Paths.HostsFile = hosts;
            try
            {
                var ansi = Encoding.Default;
                var asli = ansi.GetBytes("# komentar café\r\n127.0.0.1 localhost\r\n");
                File.WriteAllBytes(hosts, asli);
                Ok("Berkas ANSI dikenali sebagai bukan UTF-8",
                   !(HostsFile.DeteksiEncoding(asli) is UTF8Encoding) || ansi is UTF8Encoding);

                HostsFile.Sync(new[] { "toko.test" });
                var sesudah = File.ReadAllBytes(hosts);
                var teks = ansi.GetString(sesudah);
                Ok("Komentar ber-aksen tetap utuh sesudah ditulis ulang", teks.Contains("# komentar café"),
                   teks.Split('\n')[0]);
                Ok("Tidak ada karakter pengganti U+FFFD", !teks.Contains('\uFFFD'));
                Ok("Blok Phoron tertulis", teks.Contains("toko.test"));

                var bak = Path.Combine(HostsFile.FolderCadangan, "hosts-asli.bak");
                Ok("hosts-asli.bak salinan byte demi byte dari aslinya",
                   File.Exists(bak) && File.ReadAllBytes(bak).SequenceEqual(asli));
            }
            finally { Paths.HostsFile = hostsLama; Paths.Root = akarLama; }

            // ------------------------------------------- Port Node
            Ok("Keluhan Next.js dikenali beserta portnya",
               NodeRunner.PortDariKeluhan("⚠ Port 3000 is in use, trying 3001 instead.") == 3000);
            Ok("EADDRINUSE dikenali beserta portnya",
               NodeRunner.PortDariKeluhan("Error: listen EADDRINUSE: address already in use :::5173") == 5173);
            Ok("EADDRINUSE tanpa angka tetap terhitung bentrok",
               NodeRunner.PortDariKeluhan("Error: EADDRINUSE") == -1);
            Ok("Baris biasa bukan keluhan port", NodeRunner.PortDariKeluhan("✓ Ready in 2.1s") == 0);
            Ok("Vite dan Astro butuh --port", NodeRunner.ButuhArgumenPort("vite") && NodeRunner.ButuhArgumenPort("Astro"));
            Ok("Next.js membaca PORT", !NodeRunner.ButuhArgumenPort("next"));

            var appsLama = Paths.Root;
            try
            {
                Paths.Root = Path.Combine(sandbox, "apps-port");
                Directory.CreateDirectory(Paths.Root);
                NodeAppStore.SaveAll(new[] { new NodeApp { Path = @"C:\proyek-a", Port = 3005 }, new NodeApp { Path = @"C:\proyek-b" } });
                var muat = NodeAppStore.LoadAll();
                Ok("Port tetap proyek tersimpan dan terbaca lagi",
                   muat.Count == 2 && muat[0].Port == 3005 && muat[1].Port == 0,
                   string.Join(",", muat.Select(x => x.Port)));
            }
            finally { Paths.Root = appsLama; }
        }
    }
}
