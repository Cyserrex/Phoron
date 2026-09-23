using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Menyisipkan nama situs ke berkas hosts Windows di dalam satu blok bertanda.
    /// Blok bertanda dipakai supaya Phoron bisa menghapus entri miliknya sendiri
    /// tanpa menyentuh baris buatan pengguna atau buatan Laragon.
    ///
    /// Berkas ini milik sistem, bukan milik Phoron. Baris di dalamnya bisa berisi
    /// alamat server kantor, penghalang iklan, atau apa pun yang dipasang orang
    /// bertahun-tahun lalu dan sudah lupa caranya. Karena itu ada tiga penjaga
    /// yang tidak boleh dilepas:
    ///
    ///   1. gagal MEMBACA berarti gagal menulis - lihat <see cref="Sync"/>;
    ///   2. tiap baris di luar blok Phoron yang tadinya ada harus masih ada
    ///      sesudahnya - lihat PastikanMasukAkal;
    ///   3. keadaan sebelumnya selalu dicadangkan lebih dulu.
    /// </summary>
    public static class HostsFile
    {
        public const string Begin = "# === Phoron mulai ===";
        public const string End = "# === Phoron selesai ===";

        /// <summary>Cadangan pertama: keadaan hosts sebelum Phoron pernah menyentuhnya.</summary>
        public const string NamaAsli = "hosts-asli.bak";

        /// <summary>Sebanyak ini cadangan bertanggal disimpan; yang asli tidak ikut dihitung.</summary>
        public const int CadanganMaks = 10;

        public static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static List<string> ReadManaged()
        {
            var result = new List<string>();
            foreach (var line in ReadAll())
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < parts.Length; i++) result.Add(parts[i]);
            }
            return result;
        }

        /// <summary>
        /// Pembacaan yang boleh gagal - dipakai pembaca yang sekadar menampilkan
        /// sesuatu. JANGAN dipakai sebagai dasar penulisan: larik kosong yang
        /// dikembalikannya tidak bisa dibedakan dari berkas hosts yang memang
        /// kosong, dan menulis dari situ berarti membuang seluruh isi berkas.
        /// </summary>
        static string[] ReadAll()
        {
            try { return File.ReadAllLines(Paths.HostsFile); }
            catch { return new string[0]; }
        }

        /// <summary>Pembacaan yang wajib berhasil; melempar bila gagal.</summary>
        static string[] BacaWajib()
        {
            if (!File.Exists(Paths.HostsFile)) return new string[0];
            return File.ReadAllLines(Paths.HostsFile);
        }

        /// <summary>Semua nama host yang ada di berkas hosts (blok siapa pun).</summary>
        public static HashSet<string> AllNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in ReadAll())
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < parts.Length; i++) set.Add(parts[i]);
            }
            return set;
        }

        /// <summary>
        /// Tulis ulang blok Phoron berisi persis daftar nama yang diberikan.
        /// Melempar UnauthorizedAccessException bila aplikasi tidak jalan sebagai admin.
        /// </summary>
        public static void Sync(IEnumerable<string> hostNames)
        {
            var names = (hostNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            // Titik paling berbahaya di seluruh program. Dulu di sini dipakai
            // pembacaan yang memaafkan: satu galat baca sesaat - kunci antivirus,
            // pembantu ber-admin yang berbarengan - mengembalikan larik kosong,
            // logika di bawah jadi tidak berbuat apa-apa, dan berkas hosts
            // ditulis ulang HANYA berisi blok Phoron. Seluruh baris milik
            // pengguna lenyap, tanpa satu pun galat terangkat.
            string[] awal;
            try { awal = BacaWajib(); }
            catch (Exception ex)
            {
                throw new IOException(
                    "Berkas hosts tidak bisa dibaca, jadi sengaja tidak ditulis ulang: " + ex.Message
                    + " Menulisnya dari bacaan yang gagal akan membuang seluruh baris milik Anda.", ex);
            }

            var lines = awal.ToList();
            int b = lines.FindIndex(l => l.Trim() == Begin);
            int e = lines.FindIndex(l => l.Trim() == End);
            if (b >= 0 && e > b) lines.RemoveRange(b, e - b + 1);
            else if (b >= 0) lines.RemoveAt(b);

            // Buang ekor baris kosong supaya blok tidak makin turun tiap sinkron.
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);

            if (names.Count > 0)
            {
                lines.Add("");
                lines.Add(Begin);
                foreach (var n in names)
                {
                    lines.Add("127.0.0.1\t" + n);
                    lines.Add("::1\t" + n);
                }
                lines.Add(End);
            }

            // Tidak ada yang berubah - jangan menyentuh berkasnya sama sekali.
            //
            // Tanpa ini, Phoron yang Virtual Host-nya mati dan hosts-nya bersih
            // tetap mencoba menulis ulang berkas yang isinya sama persis. Tanpa
            // hak Administrator percobaan itu ditolak, dan yang muncul di tiap
            // Apply adalah peringatan "berkas hosts tidak bisa ditulis" - tentang
            // penulisan yang sejak awal tidak dibutuhkan siapa pun.
            //
            // Ekor baris kosong diabaikan saat membandingkan: itu satu-satunya
            // yang dirapikan di atas, dan bukan alasan untuk menulis.
            if (SamaIsinya(lines, awal)) return;

            WriteAll(lines, awal);
        }

        static bool SamaIsinya(List<string> baru, string[] awal)
        {
            var lama = awal.ToList();
            while (lama.Count > 0 && lama[lama.Count - 1].Trim().Length == 0)
                lama.RemoveAt(lama.Count - 1);
            return lama.SequenceEqual(baru, StringComparer.Ordinal);
        }

        static void WriteAll(List<string> baru, string[] awal)
        {
            PastikanMasukAkal(awal, baru);
            Cadangkan(awal);
            TulisLangsung(baru);
        }

        static void TulisLangsung(IEnumerable<string> lines)
        {
            // AtomicFile yang mengurus atribut ReadOnly/Hidden milik berkas hosts,
            // sekaligus memastikan berkasnya tidak pernah setengah tertulis.
            AtomicFile.WriteAllLines(Paths.HostsFile, lines, new UTF8Encoding(false));
        }

        /// <summary>Baris bermakna di LUAR blok Phoron; inilah milik orang lain.</summary>
        static IEnumerable<string> DiLuarBlok(IEnumerable<string> lines)
        {
            var dalam = false;
            foreach (var l in lines ?? Enumerable.Empty<string>())
            {
                var t = (l ?? "").Trim();
                if (t == Begin) { dalam = true; continue; }
                if (t == End) { dalam = false; continue; }
                if (dalam || t.Length == 0) continue;
                yield return t;
            }
        }

        /// <summary>
        /// Jaminan menyeluruh: Sync hanya boleh menambah atau mengurangi blok
        /// miliknya sendiri. Satu baris orang lain hilang berarti batalkan,
        /// jangan tulis. Murah dihitung, dan menutup seluruh kelas kesalahan
        /// sekaligus - termasuk yang belum terpikirkan.
        /// </summary>
        static void PastikanMasukAkal(string[] awal, List<string> baru)
        {
            var sesudah = new HashSet<string>(DiLuarBlok(baru), StringComparer.Ordinal);
            var hilang = DiLuarBlok(awal).Where(x => !sesudah.Contains(x))
                                         .Distinct(StringComparer.Ordinal).ToList();
            if (hilang.Count == 0) return;
            throw new InvalidOperationException(
                "Penulisan berkas hosts dibatalkan: " + hilang.Count
                + " baris di luar blok Phoron akan ikut hilang, mis. \"" + hilang[0]
                + "\". Phoron hanya boleh mengubah blok miliknya sendiri.");
        }

        // ------------------------------------------------------------ Cadangan

        /// <summary>
        /// Cadangan disimpan di bawah folder data Phoron, BUKAN di sebelah berkas
        /// hosts. Berkas asing di System32\drivers\etc justru memancing antivirus,
        /// dan Phoron tanpa hak admin tidak akan bisa membersihkannya lagi.
        /// </summary>
        public static string FolderCadangan
        {
            get
            {
                var p = Path.Combine(Paths.Data, "hosts-backup");
                try { Directory.CreateDirectory(p); } catch { }
                return p;
            }
        }

        public class Cadangan
        {
            public string Path;
            public DateTime Waktu;
            public long Ukuran;
            /// <summary>Keadaan sebelum Phoron dipasang; tidak pernah dibuang.</summary>
            public bool Asli;
            public string Nama { get { return System.IO.Path.GetFileName(Path); } }
        }

        /// <summary>Cadangan yang ada, terbaru lebih dulu.</summary>
        public static List<Cadangan> DaftarCadangan()
        {
            var hasil = new List<Cadangan>();
            try
            {
                foreach (var f in Directory.GetFiles(FolderCadangan, "hosts-*.bak"))
                {
                    var fi = new FileInfo(f);
                    hasil.Add(new Cadangan
                    {
                        Path = f,
                        Waktu = fi.LastWriteTime,
                        Ukuran = fi.Length,
                        Asli = string.Equals(fi.Name, NamaAsli, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }
            catch { }
            return hasil.OrderByDescending(x => x.Waktu).ToList();
        }

        static void Cadangkan(string[] awal)
        {
            var folder = FolderCadangan;
            var asli = Path.Combine(folder, NamaAsli);
            try
            {
                if (!File.Exists(asli))
                {
                    // Sekali seumur hidup mesin ini, dan tidak pernah dirotasi.
                    // Inilah berkas yang benar-benar dicari orang saat kacau:
                    // keadaan sebelum Phoron ikut campur.
                    AtomicFile.WriteAllLines(asli, awal);
                    return;
                }

                // Hanya kalau isinya berbeda dari yang sudah tersimpan. Apply()
                // jalan tiap start dan tiap ganti profil; mencadangkan tanpa
                // syarat cuma menghasilkan belasan berkas kembar.
                var terbaru = DaftarCadangan().FirstOrDefault();
                if (terbaru != null && Sama(terbaru.Path, awal)) return;
                if (Sama(asli, awal)) return;

                AtomicFile.WriteAllLines(
                    Path.Combine(folder, "hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak"),
                    awal);
                Pangkas();
            }
            catch (Exception ex)
            {
                // Sengaja melempar, bukan menelan. Menulis berkas milik sistem
                // tanpa cadangan adalah persis keadaan yang seluruh berkas ini
                // ada untuk mencegahnya.
                throw new IOException(
                    "Cadangan berkas hosts gagal dibuat, jadi hosts tidak ditulis ulang: "
                    + ex.Message, ex);
            }
        }

        static void Pangkas()
        {
            var bertanggal = DaftarCadangan().Where(c => !c.Asli).ToList();
            for (int i = CadanganMaks; i < bertanggal.Count; i++)
                try { File.Delete(bertanggal[i].Path); } catch { }
        }

        static bool Sama(string path, string[] lines)
        {
            try { return File.ReadAllLines(path).SequenceEqual(lines, StringComparer.Ordinal); }
            catch { return false; }
        }

        /// <summary>
        /// Kembalikan berkas hosts ke salah satu cadangan. Keadaan sekarang ikut
        /// dicadangkan lebih dulu - memulihkan juga sebuah perubahan, dan orang
        /// harus bisa membatalkannya.
        /// </summary>
        public static void Pulihkan(string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
                throw new FileNotFoundException("Berkas cadangan tidak ditemukan.", backupPath ?? "");

            var isi = File.ReadAllLines(backupPath);
            string[] sekarang = null;
            try { sekarang = BacaWajib(); } catch { }
            if (sekarang != null && sekarang.Length > 0) Cadangkan(sekarang);

            // Tanpa PastikanMasukAkal: memulihkan memang boleh menghapus baris,
            // itulah gunanya.
            TulisLangsung(isi);
        }

        public static void Clear() { Sync(new string[0]); }
    }
}
