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
    /// </summary>
    public static class HostsFile
    {
        public const string Begin = "# === Phoron mulai ===";
        public const string End = "# === Phoron selesai ===";

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

        static string[] ReadAll()
        {
            try { return File.ReadAllLines(Paths.HostsFile); }
            catch { return new string[0]; }
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

            var lines = ReadAll().ToList();
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
            WriteAll(lines);
        }

        static void WriteAll(List<string> lines)
        {
            var path = Paths.HostsFile;
            var attrs = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;
            // Berkas hosts sering ber-atribut ReadOnly/Hidden; harus dilepas dulu
            // lalu dikembalikan, kalau tidak antivirus menganggapnya aneh.
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            if (File.Exists(path)) File.SetAttributes(path, attrs);
        }

        public static void Clear() { Sync(new string[0]); }
    }
}
