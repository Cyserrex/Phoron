using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Bahan uji setara fitur "proses anak dan log" (daemon\inti\src\log_file.rs
    /// dan node_runner.rs).
    ///
    /// Log: tiap skenario punya keadaan awal folder (masuk\skenario) dan daftar
    /// operasi (log-ops.txt). LogFile C# menjalankannya ke csharp\skenario;
    /// Rust harus menghasilkan nama dan isi berkas yang identik.
    ///
    /// Keluaran Node: contoh baris dari Next.js, Vite, Astro dan kasus tepi
    /// (keluaran.txt) beserta hasil C# per baris (keluaran-csharp.txt):
    /// PortDariKeluhan dan URL yang dibaca UrlRx.
    ///
    /// Jalankan dengan: daemon\uji_setara_modul.ps1 proses
    /// </summary>
    internal static class UjiSetara_proses
    {
        public static int Jalankan(string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            Log(folder);
            Keluaran(folder);
            Console.WriteLine("bahan uji setara proses ditulis ke " + folder);
            return 0;
        }

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static void Tulis(string path, string isi)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, isi, Utf8);
        }

        // ---- Log berputar ----------------------------------------------------

        /// <summary>
        /// Satu operasi per baris: skenario TAB berkas TAB jenis TAB data.
        /// jenis t = tambah teks (\n, \r, \t, \\ di-escape); u = tambah huruf
        /// berulang ("x:1200000"); p = putar.
        /// </summary>
        static void Log(string folder)
        {
            var masuk = Path.Combine(folder, "log", "masuk");
            var ops = new List<string>();
            Action<string, string, string, string> op = (s, b, j, d) => ops.Add(s + "\t" + b + "\t" + j + "\t" + d);

            // s1: folder kosong, berbagai bentuk baris; putar berkas kecil dan berkas yang tidak ada.
            Directory.CreateDirectory(Path.Combine(masuk, "s1"));
            Tulis(Path.Combine(masuk, "s1", "kecil.log"), "isi lama\r\n");
            op("s1", "phoron.log", "t", "satu");
            op("s1", "phoron.log", "t", "Ünik ✓ ç 日本");
            op("s1", "phoron.log", "t", "");
            op("s1", "phoron.log", "t", "a\\nb\\tc");
            op("s1", "phoron.log", "t", "akhir\\r");
            op("s1", "kecil.log", "p", "");
            op("s1", "tidak-ada.log", "p", "");
            op("s1", "kecil.log", "t", "sesudah putar");

            // s2: tepat di bawah batas; lubang di .2; .3 lama harus dibuang.
            Directory.CreateDirectory(Path.Combine(masuk, "s2"));
            File.WriteAllBytes(Path.Combine(masuk, "s2", "phoron.log"), Huruf('a', (int)LogFile.BatasBytes - 10));
            Tulis(Path.Combine(masuk, "s2", "phoron.log.1"), "satu\r\n");
            Tulis(Path.Combine(masuk, "s2", "phoron.log.3"), "tiga\r\n");
            op("s2", "phoron.log", "t", "baris pertama");
            op("s2", "phoron.log", "t", "baris kedua");
            op("s2", "phoron.log", "t", "baris ketiga");

            // s3: baris besar memaksa beberapa putaran; yang tertua terbuang.
            Directory.CreateDirectory(Path.Combine(masuk, "s3"));
            foreach (var c in "bcdefg")
                op("s3", "apache-error.log", "u", c + ":1100000");
            op("s3", "apache-error.log", "t", "penutup");

            // s4: tepat di batas: baris pertama sudah memutar.
            Directory.CreateDirectory(Path.Combine(masuk, "s4"));
            File.WriteAllBytes(Path.Combine(masuk, "s4", "mysql-error.log"), Huruf('m', (int)LogFile.BatasBytes));
            op("s4", "mysql-error.log", "t", "baru");
            op("s4", "mysql-error.log", "p", "");
            op("s4", "mysql-error.log", "p", "");

            File.WriteAllLines(Path.Combine(folder, "log", "log-ops.txt"), ops, Utf8);

            var keluar = Path.Combine(folder, "log", "csharp");
            foreach (var d in Directory.GetDirectories(masuk))
            {
                var tuju = Path.Combine(keluar, Path.GetFileName(d));
                Directory.CreateDirectory(tuju);
                foreach (var f in Directory.GetFiles(d)) File.Copy(f, Path.Combine(tuju, Path.GetFileName(f)));
            }
            foreach (var baris in ops)
            {
                var b = baris.Split('\t');
                var path = Path.Combine(keluar, b[0], b[1]);
                switch (b[2])
                {
                    case "t": LogFile.Tambah(path, Urai(b[3])); break;
                    case "u":
                        var x = b[3].Split(':');
                        LogFile.Tambah(path, new string(x[0][0], int.Parse(x[1])));
                        break;
                    case "p": LogFile.Putar(path); break;
                }
            }
        }

        static byte[] Huruf(char c, int n)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)c;
            return b;
        }

        static string Urai(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] == 'n' ? '\n' : s[i] == 'r' ? '\r' : s[i] == 't' ? '\t' : s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ---- Keluaran Node ---------------------------------------------------

        static readonly string[] Contoh =
        {
            // Next.js
            "  ▲ Next.js 15.0.3",
            "  - Local:        http://localhost:3000",
            "  - Network:      http://192.168.1.5:3000",
            " ✓ Ready in 1834ms",
            " ⚠ Port 3000 is in use, trying 3001 instead.",
            "Error: listen EADDRINUSE: address already in use :::3000",
            "Error: listen EADDRINUSE: address already in use 127.0.0.1:3000",
            "  code: 'EADDRINUSE',",
            "  port: 3000",
            "   - Local:        http://localhost:3001",
            // Vite
            "  VITE v5.4.2  ready in 312 ms",
            "  ➜  Local:   http://localhost:5173/",
            "  ➜  Network: use --host to expose",
            "Port 5173 is in use, trying another one...",
            "error when starting dev server:",
            "Error: Port 5173 is already in use",
            "  ➜  Local:   http://[::1]:5174/",
            // Astro
            " astro  v4.15.0 ready in 210 ms",
            "┃ Local    http://localhost:4321/",
            "┃ Network  use --host to expose",
            "[ERROR] [vite] Port 4321 is already in use",
            "  ┃ Local    http://127.0.0.1:4322/docs/",
            // Kasus tepi
            "",
            "Siap di http://127.0.0.1:8080/api.",
            "di http://[::1]:3000, ya",
            "HTTPS://LOCALHOST:443/x?y=1",
            "http://localhost:abc",
            "http://localhostx",
            "https://example.com lalu http://localhost:1/",
            "httpx://localhost lalu http://localhost:9",
            "http://localhost:3000/,.",
            "listen EADDRINUSE :::99999999",
            "EADDRINUSE :3000ms",
            "EADDRINUSE :3000.",
            "port 123456789012 is in use",
            "port 99999999999 is in use at :3005",
            "PORT\t4321  IS ALREADY   IN USE",
            "address already in use :::5173 dan :8080",
            "x:1234 EADDRINUSE :22",
            "x:1234 EADDRINUSE",
            "EADDRINUSE lalu port 3000 is in use",
            "port 3000 is in use; EADDRINUSE :4000",
            "Address Already In Use 0.0.0.0:80",
            "port 3000 is used",
            "port3000 is in use :12",
            " port 3000 is in use",
        };

        static void Keluaran(string folder)
        {
            var dir = Path.Combine(folder, "node");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "keluaran.txt"), string.Join("\n", Contoh), Utf8);

            // UrlRx privat: dibaca lewat refleksi supaya yang diuji memang pola yang dipakai.
            var rx = (Regex)typeof(NodeRunner).GetField("UrlRx", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var hasil = new List<string>();
            foreach (var b in Contoh)
            {
                var m = rx.Match(b);
                hasil.Add(NodeRunner.PortDariKeluhan(b) + "\t" + (m.Success ? m.Value.TrimEnd('.', ',') : ""));
            }
            File.WriteAllText(Path.Combine(dir, "keluaran-csharp.txt"), string.Join("\n", hasil), Utf8);

            var kerangka = new[] { "vite", "astro", "sveltekit", "remix", "next", "nuxt", "", "express", "VITE" };
            var k = new List<string>();
            foreach (var x in kerangka) k.Add(x + "\t" + (NodeRunner.ButuhArgumenPort(x) ? "1" : "0"));
            File.WriteAllText(Path.Combine(dir, "kerangka-csharp.txt"), string.Join("\n", k), Utf8);
        }
    }
}
