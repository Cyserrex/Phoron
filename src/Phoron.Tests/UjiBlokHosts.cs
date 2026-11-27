using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Blok Phoron di berkas hosts setelah penandanya disunting tangan.
        ///
        /// Dulu End dicari dari atas berkas, bukan sesudah Begin. Begitu satu
        /// penanda terhapus, entri lama jadi yatim dan setiap Sync menambah blok
        /// baru di bawahnya - berkasnya memanjang tanpa akhir, dan situs yang
        /// sudah dihapus tidak pernah keluar dari hosts.
        /// </summary>
        static void UjiBlokHosts(string sandbox)
        {
            Bagian("Blok hosts yang penandanya rusak");

            var hostsLama = Paths.HostsFile;
            var hosts = Path.Combine(sandbox, "hosts-blok");
            Paths.HostsFile = hosts;
            try
            {
                var B = HostsFile.Begin;
                var E = HostsFile.End;

                // 1. Begin dihapus tangan: entri lama + End yatim.
                File.WriteAllLines(hosts, new[]
                {
                    "127.0.0.1 localhost", "10.0.0.1 kantor.internal",
                    "", "127.0.0.1\tlama.test", "::1\tlama.test", E,
                });
                HostsFile.Sync(new[] { "baru.test" });
                var isi = File.ReadAllLines(hosts);
                Ok("Begin hilang: entri yatim ikut dibersihkan", !isi.Any(l => l.Contains("lama.test")),
                   string.Join(" | ", isi));
                Ok("Begin hilang: tepat satu blok sesudahnya",
                   isi.Count(l => l.Trim() == B) == 1 && isi.Count(l => l.Trim() == E) == 1,
                   string.Join(" | ", isi));
                Ok("Begin hilang: baris pengguna utuh",
                   isi.Contains("127.0.0.1 localhost") && isi.Contains("10.0.0.1 kantor.internal"));
                var sekali = File.ReadAllText(hosts);
                HostsFile.Sync(new[] { "baru.test" });
                Ok("Sync berikutnya tidak memanjangkan berkas", File.ReadAllText(hosts) == sekali);

                // 2. End dihapus tangan: baris pengguna SESUDAH entri harus selamat.
                File.WriteAllLines(hosts, new[]
                {
                    "127.0.0.1 localhost", B, "127.0.0.1\tlama.test", "::1\tlama.test",
                    "# catatan pengguna", "10.0.0.2 printer.kantor",
                });
                HostsFile.Sync(new[] { "baru.test" });
                isi = File.ReadAllLines(hosts);
                Ok("End hilang: entri lama dibersihkan", !isi.Any(l => l.Contains("lama.test")),
                   string.Join(" | ", isi));
                Ok("End hilang: baris pengguna sesudahnya utuh",
                   isi.Contains("# catatan pengguna") && isi.Contains("10.0.0.2 printer.kantor"),
                   string.Join(" | ", isi));
                Ok("End hilang: tepat satu blok sesudahnya", isi.Count(l => l.Trim() == B) == 1);

                // 3. Dua blok (sisa versi lama) dilebur jadi satu.
                File.WriteAllLines(hosts, new[]
                {
                    "127.0.0.1 localhost",
                    B, "127.0.0.1\ta.test", E,
                    "10.0.0.3 nas.rumah",
                    B, "127.0.0.1\tb.test", E,
                });
                HostsFile.Sync(new[] { "c.test" });
                isi = File.ReadAllLines(hosts);
                Ok("Dua blok dilebur jadi satu",
                   isi.Count(l => l.Trim() == B) == 1 && !isi.Any(l => l.Contains("a.test") || l.Contains("b.test")),
                   string.Join(" | ", isi));
                Ok("Baris pengguna di antara dua blok utuh", isi.Contains("10.0.0.3 nas.rumah"));

                // 4. Baris berspasi sesudah Begin yatim bukan tulisan Phoron.
                var rentang = HostsFile.RentangBlok(new[] { B, "127.0.0.1\tx.test", "127.0.0.1 milikku.test" });
                Ok("Baris berspasi sesudah Begin yatim tidak dianggap milik Phoron",
                   rentang.Count == 1 && rentang[0].Key == 0 && rentang[0].Value == 1,
                   string.Join(",", rentang.Select(r => r.Key + "-" + r.Value)));
                Ok("Berkas tanpa penanda tidak punya blok",
                   HostsFile.RentangBlok(new[] { "127.0.0.1\tlocalhost", "# x" }).Count == 0);
            }
            finally
            {
                Paths.HostsFile = hostsLama;
            }

            // ------------------------------------------- Mutex & UAC batal
            var prog = BacaSumber("Phoron.App", "Program.cs");
            if (prog != null)
            {
                var restart = Potong(prog, "public static bool RestartAsAdmin", "\n        }\n");
                Ok("UAC yang dibatalkan mengambil lagi mutex instans tunggal",
                   Regex.IsMatch(restart, @"catch \(Exception ex\)\s*\{[^}]*AmbilMutexLagi\(\);"),
                   "tanpa mutex, klik ikon berikutnya menyalakan Phoron kedua");
            }
        }
    }
}
