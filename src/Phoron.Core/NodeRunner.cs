using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Phoron.Core
{
    /// <summary>
    /// Menjalankan skrip package.json (next dev, astro dev, vite, ...) sebagai
    /// proses anak Phoron. Beberapa proyek boleh jalan sekaligus - itu memang
    /// lazim: satu frontend, satu API, satu situs dokumentasi.
    /// </summary>
    public class NodeRunner : IProsesNode
    {
        class Berjalan
        {
            public Process Proc;
            public string Url;
            /// <summary>Port yang dikeluhkan "sudah dipakai" di keluarannya; 0 bila tidak ada.</summary>
            public int Bentrok;
        }

        // Proyek yang terakhir BERHENTI karena portnya dipakai proses lain.
        // Dicatat hanya bila prosesnya benar-benar berakhir dengan galat:
        // next dev versi baru mencetak "Port 3000 is in use, trying 3001
        // instead" lalu tetap jalan, dan itu bukan kegagalan.
        readonly Dictionary<string, int> _bentrok =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static readonly Regex BentrokRx = new Regex(
            @"EADDRINUSE|address already in use|port\s+(\d+)\s+is\s+(?:already\s+)?in\s+use",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Port di ekor baris EADDRINUSE: "...in use :::5173" atau "127.0.0.1:3000".
        static readonly Regex PortEkorRx = new Regex(@":(\d{2,5})\b", RegexOptions.Compiled);

        /// <summary>Port yang membuat proyek ini gagal start terakhir kali; 0 bila tidak ada.</summary>
        public int PortBentrok(string folder)
        {
            int p;
            lock (_kunci) return folder != null && _bentrok.TryGetValue(folder, out p) ? p : 0;
        }

        /// <summary>Port dari baris keluaran yang mengeluhkan port terpakai; 0 bila bukan baris itu.</summary>
        public static int PortDariKeluhan(string baris)
        {
            if (string.IsNullOrEmpty(baris)) return 0;
            var m = BentrokRx.Match(baris);
            if (!m.Success) return 0;
            int p;
            if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out p)) return p;
            var ekor = PortEkorRx.Matches(baris.Substring(m.Index));
            if (ekor.Count > 0 && int.TryParse(ekor[ekor.Count - 1].Groups[1].Value, out p)) return p;
            // EADDRINUSE tanpa angka di baris yang sama: tetap bentrok, port tak diketahui.
            return -1;
        }

        /// <summary>
        /// Kerangka yang tidak membaca variabel PORT dan butuh "--port" di
        /// baris perintahnya. Next.js dan Nuxt membaca PORT.
        /// </summary>
        public static bool ButuhArgumenPort(string kerangka)
        {
            switch ((kerangka ?? "").ToLowerInvariant())
            {
                case "vite": case "astro": case "sveltekit": case "remix": return true;
                default: return false;
            }
        }

        /// <summary>
        /// Menjaga daftar proses yang sedang berjalan. Ia disentuh dari utas
        /// layar - tombol Jalankan dan Hentikan - dan dari panggilan balik
        /// Process.Exited yang datang di utas kolam.
        /// </summary>
        readonly object _kunci = new object();

        readonly Dictionary<string, Berjalan> _jalan =
            new Dictionary<string, Berjalan>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Uji-lalu-lepas dalam satu langkah: benar hanya bagi pihak yang
        /// pertama sampai. Inilah yang membedakan proses yang berakhir SENDIRI
        /// dari yang sengaja dihentikan lewat Stop().
        /// </summary>
        bool LepasJika(string folder, System.Diagnostics.Process p)
        {
            lock (_kunci)
            {
                Berjalan ada;
                if (folder == null || !_jalan.TryGetValue(folder, out ada)
                    || !ReferenceEquals(ada.Proc, p)) return false;
                _jalan.Remove(folder);
                return true;
            }
        }

        /// <summary>(folder, baris) - keluaran mentah dari proses.</summary>
        public event Action<string, string> Output;
        /// <summary>(folder, sedangJalan)</summary>
        public event Action<string, bool> StateChanged;
        /// <summary>(folder, url) saat alamat terdeteksi dari keluaran.</summary>
        public event Action<string, string> UrlFound;

        // Server pengembangan mencetak alamatnya sendiri saat siap. Membaca
        // alamat itu jauh lebih andal daripada menebak port: Next memakai 3000,
        // Astro 4321, Vite 5173, dan semuanya bergeser sendiri kalau portnya
        // terpakai.
        static readonly Regex UrlRx = new Regex(
            @"https?://(?:localhost|127\.0\.0\.1|\[::1\])(?::\d+)?(?:/\S*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool IsRunning(string folder)
        {
            Berjalan b;
            lock (_kunci)
                if (folder == null || !_jalan.TryGetValue(folder, out b)) return false;
            try { return b.Proc != null && !b.Proc.HasExited; } catch { return false; }
        }

        public string UrlOf(string folder)
        {
            Berjalan b;
            lock (_kunci)
                return folder != null && _jalan.TryGetValue(folder, out b) ? b.Url : null;
        }

        public IEnumerable<string> RunningFolders
        {
            get { lock (_kunci) return new List<string>(_jalan.Keys); }
        }

        /// <summary>Jalankan skrip. Mengembalikan pesan kesalahan, atau null bila berhasil dimulai.</summary>
        public string Start(NodeApp app, BinPackage node) { return Start(app, node, 0); }

        /// <summary>
        /// Jalankan skrip. <paramref name="portSekali"/> &gt; 0 memakai port itu untuk
        /// kali ini saja; selain itu port tetap proyeknya (0 = pilihan servernya).
        /// </summary>
        public string Start(NodeApp app, BinPackage node, int portSekali)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return Lang.T("Proyek belum punya folder.");
            if (!Directory.Exists(app.Path)) return Lang.T("Folder tidak ada: {0}", app.Path);
            if (!PackageJson.LooksLikeNodeProject(app.Path))
                return Lang.T("Tidak ada package.json di {0}.", app.Path);
            if (IsRunning(app.Path)) return Lang.T("Proyek ini sudah jalan.");
            lock (_kunci) _bentrok.Remove(app.Path);

            var manager = string.IsNullOrWhiteSpace(app.Manager) ? "npm" : app.Manager.Trim();
            var skrip = string.IsNullOrWhiteSpace(app.Script) ? "dev" : app.Script.Trim();
            var port = portSekali > 0 ? portSekali : app.Port;

            // Dijalankan lewat cmd.exe: npm/pnpm/yarn di Windows berupa berkas
            // .cmd, dan CreateProcess tidak bisa menjalankan berkas batch
            // langsung. "run" dilewati untuk yarn, yang memang tidak memakainya.
            var perintah = manager == "yarn"
                ? manager + " " + skrip
                : manager + " run " + skrip;
            // Port diteruskan dua cara: variabel PORT (Next.js, Nuxt) selalu,
            // dan "--port" untuk kerangka yang tidak membacanya. npm dan pnpm
            // butuh "--" supaya argumennya sampai ke skrip, bukan ke npm.
            if (port > 0)
            {
                string kerangka = null;
                try { kerangka = PackageJson.DetectFramework(app.Path); } catch { }
                if (ButuhArgumenPort(kerangka))
                    perintah += (manager == "yarn" ? " --port " : " -- --port ") + port;
            }

            var psi = new ProcessStartInfo("cmd.exe", "/c " + perintah)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = app.Path,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            var jalurNode = node != null ? node.Path : null;
            if (!string.IsNullOrEmpty(jalurNode))
                psi.EnvironmentVariables["PATH"] =
                    jalurNode + ";" + Environment.GetEnvironmentVariable("PATH");
            // Banyak perkakas mewarnai keluaran dengan kode ANSI yang jadi sampah
            // di kotak teks biasa.
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            if (port > 0) psi.EnvironmentVariables["PORT"] = port.ToString();

            Process p;
            try
            {
                p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var entri = new Berjalan { Proc = p };
                lock (_kunci) _jalan[app.Path] = entri;

                DataReceivedEventHandler baca = (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    var keluh = PortDariKeluhan(e.Data);
                    if (keluh != 0 && entri.Bentrok <= 0) entri.Bentrok = keluh;
                    Lapor(app.Path, e.Data);
                    if (entri.Url == null)
                    {
                        var m = UrlRx.Match(e.Data);
                        if (m.Success)
                        {
                            entri.Url = m.Value.TrimEnd('.', ',');
                            var h = UrlFound;
                            if (h != null) h(app.Path, entri.Url);
                        }
                    }
                };
                p.OutputDataReceived += baca;
                p.ErrorDataReceived += baca;
                p.Exited += (s, e) =>
                {
                    // Rujukannya masih kita berarti proses ini berakhir SENDIRI.
                    // Kalau sudah dilepas, penghentiannya disengaja lewat Stop()
                    // - dan Stop() sudah melapor "dihentikan". Dulu keduanya
                    // dilaporkan, jadi menekan tombol Hentikan menghasilkan dua
                    // baris, yang kedua menyebut kode keluar bukan-nol seolah
                    // ada yang gagal.
                    if (!LepasJika(app.Path, p)) return;

                    int kode;
                    try { kode = p.ExitCode; } catch { kode = -1; }
                    // Berakhir dengan galat sesudah mengeluhkan port: itulah
                    // sebabnya. Port tak dikenal (-1) dicatat sebagai port yang
                    // dipakai saat itu, atau 3000 sebagai tebakan terakhir.
                    if (kode != 0 && entri.Bentrok != 0)
                        lock (_kunci) _bentrok[app.Path] = entri.Bentrok > 0 ? entri.Bentrok : (port > 0 ? port : 3000);
                    Lapor(app.Path, kode == 0
                        ? Lang.T("-- proses berakhir (kode 0) --")
                        : Lang.T("-- proses berakhir dengan galat (kode {0}) --", kode));
                    Ubah(app.Path, false);
                };

                p.Start();
                ProcessJob.Ikat(p);   // lihat ProcessJob: anak tidak boleh hidup lebih lama dari Phoron
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                lock (_kunci) _jalan.Remove(app.Path);
                return Lang.T("Tidak bisa menjalankan: ") + ex.Message;
            }

            Lapor(app.Path, "> " + perintah + "   (di " + app.Path + ")");
            Ubah(app.Path, true);
            return null;
        }

        public void Stop(string folder)
        {
            Berjalan b;
            lock (_kunci)
            {
                if (folder == null || !_jalan.TryGetValue(folder, out b)) return;
                _jalan.Remove(folder);   // dilepas dulu supaya Exited tidak melapor dua kali
            }
            try
            {
                // Pohon proses, bukan cmd.exe-nya saja: cmd hanya pembungkus,
                // dan node.exe yang sebenarnya memegang port ada di bawahnya.
                if (b.Proc != null && !b.Proc.HasExited) Shell.KillTree(b.Proc.Id);
            }
            catch { }
            Lapor(folder, Lang.T("-- dihentikan --"));
            Ubah(folder, false);
        }

        public void StopAll()
        {
            foreach (var f in RunningFolders) Stop(f);
        }

        /// <summary>
        /// Sisipkan satu baris ke keluaran sebuah proyek, seolah-olah datang
        /// dari prosesnya. Dipakai Engine untuk peringatan yang ia temukan
        /// sendiri - lihat HstsPeriksa - supaya terbaca di tempat pengguna
        /// sedang menatap, bukan hanya di catatan aktivitas Beranda.
        /// </summary>
        public void Sisipkan(string folder, string baris) { Lapor(folder, baris); }

        void Lapor(string folder, string baris)
        {
            // Disimpan DI SINI, bukan di halaman Node. Dulu satu-satunya
            // penampungnya halaman itu sendiri: jalankan Vite, pindah ke Beranda
            // atau tutup ke baki, dan setiap baris di antaranya - termasuk galat
            // saat start - hilang begitu saja.
            if (folder != null)
            {
                lock (_kunci)
                {
                    Queue<string> q;
                    if (!_riwayat.TryGetValue(folder, out q)) _riwayat[folder] = q = new Queue<string>();
                    q.Enqueue(baris);
                    // Server pengembangan bisa mencetak ribuan baris; hanya ekor
                    // terakhirnya yang berguna dan hanya itu yang disimpan.
                    while (q.Count > BatasRiwayat) q.Dequeue();
                }
            }
            var h = Output;
            if (h != null) h(folder, baris);
        }

        /// <summary>Jumlah baris keluaran terakhir yang disimpan per proyek.</summary>
        public const int BatasRiwayat = 400;

        readonly Dictionary<string, Queue<string>> _riwayat =
            new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Salinan keluaran terakhir sebuah proyek, terlama lebih dulu.</summary>
        public List<string> Riwayat(string folder)
        {
            lock (_kunci)
            {
                Queue<string> q;
                return folder != null && _riwayat.TryGetValue(folder, out q)
                    ? new List<string>(q) : new List<string>();
            }
        }

        void Ubah(string folder, bool jalan)
        {
            var h = StateChanged;
            if (h != null) h(folder, jalan);
        }
    }
}
