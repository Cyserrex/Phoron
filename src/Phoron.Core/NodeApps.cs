using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Phoron.Core
{
    /// <summary>
    /// Satu proyek Node/TypeScript yang didaftarkan pengguna - Next.js, Astro,
    /// Vite, apa pun yang dijalankan lewat skrip di package.json.
    /// </summary>
    public class NodeApp
    {
        /// <summary>Folder proyek. Jadi kunci; boleh di mana saja, tidak harus di www.</summary>
        public string Path { get; set; }
        public string Name { get; set; }
        /// <summary>Nama skrip di package.json, mis. "dev".</summary>
        public string Script { get; set; }
        /// <summary>npm | pnpm | yarn | bun</summary>
        public string Manager { get; set; }
        /// <summary>Nama folder paket Node yang dipakai; kosong = ikut PATH sistem.</summary>
        public string NodeId { get; set; }

        public NodeApp()
        {
            Script = "dev";
            Manager = "npm";
            NodeId = "";
            Name = "";
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Name;
                return string.IsNullOrEmpty(Path) ? "(tanpa nama)" : System.IO.Path.GetFileName(Path.TrimEnd('\\'));
            }
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>Baca/tulis daftar proyek Node di &lt;root&gt;\apps.ini.</summary>
    public static class NodeAppStore
    {
        static string FilePath { get { return System.IO.Path.Combine(Paths.Root, "apps.ini"); } }

        public static List<NodeApp> LoadAll()
        {
            var list = new List<NodeApp>();
            var ini = Ini.Load(FilePath);
            foreach (var section in ini.Sections)
            {
                if (string.IsNullOrWhiteSpace(section)) continue;
                list.Add(new NodeApp
                {
                    Path = section,
                    Name = ini.Get(section, "nama", ""),
                    Script = ini.Get(section, "skrip", "dev"),
                    Manager = ini.Get(section, "manager", "npm"),
                    NodeId = ini.Get(section, "node", ""),
                });
            }
            return list;
        }

        public static void SaveAll(IEnumerable<NodeApp> apps)
        {
            var ini = new Ini();
            foreach (var a in apps)
            {
                if (string.IsNullOrWhiteSpace(a.Path)) continue;
                var s = a.Path.TrimEnd('\\');
                ini.Set(s, "nama", a.Name ?? "");
                ini.Set(s, "skrip", a.Script ?? "dev");
                ini.Set(s, "manager", a.Manager ?? "npm");
                ini.Set(s, "node", a.NodeId ?? "");
            }
            ini.Save(FilePath,
                "Proyek Node/TypeScript yang terdaftar di Phoron.\n" +
                "Nama seksi = folder proyeknya. Boleh disunting tangan.");
        }
    }

    /// <summary>Membaca package.json tanpa pustaka JSON - hanya bagian yang benar-benar dipakai.</summary>
    public static class PackageJson
    {
        /// <summary>
        /// Nama-nama skrip di package.json, menurut urutan tulisnya.
        ///
        /// Sengaja tidak memakai pengurai JSON penuh: Core tidak menarik satu pun
        /// dependensi, dan yang dibutuhkan di sini hanya kunci di dalam objek
        /// "scripts". Nilainya diabaikan, jadi tanda kutip di dalam perintah
        /// (mis. "astro check && astro build") tidak perlu diurai sama sekali.
        /// </summary>
        public static List<string> Scripts(string folder)
        {
            var hasil = new List<string>();
            var file = Path.Combine(folder ?? "", "package.json");
            if (!File.Exists(file)) return hasil;

            string teks;
            try { teks = File.ReadAllText(file); } catch { return hasil; }

            var mulai = teks.IndexOf("\"scripts\"", StringComparison.Ordinal);
            if (mulai < 0) return hasil;
            var buka = teks.IndexOf('{', mulai);
            if (buka < 0) return hasil;

            // Cari kurung tutup pasangannya dengan menghitung kedalaman, sambil
            // melewati isi string - nilai skrip bisa memuat { atau } sendiri.
            int dalam = 0, i = buka;
            bool diString = false;
            for (; i < teks.Length; i++)
            {
                var c = teks[i];
                if (diString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') diString = false;
                    continue;
                }
                if (c == '"') { diString = true; continue; }
                if (c == '{') dalam++;
                else if (c == '}') { dalam--; if (dalam == 0) break; }
            }
            if (i >= teks.Length) return hasil;

            var isi = teks.Substring(buka + 1, i - buka - 1);
            foreach (Match m in Regex.Matches(isi, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:"))
            {
                var nama = m.Groups[1].Value;
                if (nama.Length > 0 && !hasil.Contains(nama)) hasil.Add(nama);
            }
            return hasil;
        }

        /// <summary>Pengelola paket menurut berkas kunci yang ada di folder proyek.</summary>
        public static string DetectManager(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "npm";
            if (File.Exists(Path.Combine(folder, "pnpm-lock.yaml"))) return "pnpm";
            if (File.Exists(Path.Combine(folder, "yarn.lock"))) return "yarn";
            if (File.Exists(Path.Combine(folder, "bun.lockb"))
                || File.Exists(Path.Combine(folder, "bun.lock"))) return "bun";
            return "npm";
        }

        /// <summary>Kerangka yang terdeteksi, sekadar keterangan di layar.</summary>
        public static string DetectFramework(string folder)
        {
            var file = Path.Combine(folder ?? "", "package.json");
            if (!File.Exists(file)) return "";
            string teks;
            try { teks = File.ReadAllText(file); } catch { return ""; }
            foreach (var k in new[] { "next", "astro", "nuxt", "vite", "@sveltejs/kit",
                                      "@angular/core", "react-scripts", "express", "nest" })
                if (Regex.IsMatch(teks, "\"" + Regex.Escape(k) + "\"\\s*:")) return k;
            return "";
        }

        public static bool LooksLikeNodeProject(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "package.json"));
        }

        public static bool HasNodeModules(string folder)
        {
            return !string.IsNullOrEmpty(folder) && Directory.Exists(Path.Combine(folder, "node_modules"));
        }
    }
}
