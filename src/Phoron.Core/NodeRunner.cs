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
    public class NodeRunner
    {
        class Berjalan
        {
            public Process Proc;
            public string Url;
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
        public string Start(NodeApp app, BinPackage node)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return "Proyek belum punya folder.";
            if (!Directory.Exists(app.Path)) return "Folder tidak ada: " + app.Path;
            if (!PackageJson.LooksLikeNodeProject(app.Path))
                return "Tidak ada package.json di " + app.Path + ".";
            if (IsRunning(app.Path)) return Lang.T("Proyek ini sudah jalan.");

            var manager = string.IsNullOrWhiteSpace(app.Manager) ? "npm" : app.Manager.Trim();
            var skrip = string.IsNullOrWhiteSpace(app.Script) ? "dev" : app.Script.Trim();

            // Dijalankan lewat cmd.exe: npm/pnpm/yarn di Windows berupa berkas
            // .cmd, dan CreateProcess tidak bisa menjalankan berkas batch
            // langsung. "run" dilewati untuk yarn, yang memang tidak memakainya.
            var perintah = manager == "yarn"
                ? manager + " " + skrip
                : manager + " run " + skrip;

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

            Process p;
            try
            {
                p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var entri = new Berjalan { Proc = p };
                lock (_kunci) _jalan[app.Path] = entri;

                DataReceivedEventHandler baca = (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
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
            var h = Output;
            if (h != null) h(folder, baris);
        }

        void Ubah(string folder, bool jalan)
        {
            var h = StateChanged;
            if (h != null) h(folder, jalan);
        }
    }
}
