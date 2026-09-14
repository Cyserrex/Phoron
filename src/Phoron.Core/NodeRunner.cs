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

        readonly Dictionary<string, Berjalan> _jalan =
            new Dictionary<string, Berjalan>(StringComparer.OrdinalIgnoreCase);

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
            if (folder == null || !_jalan.TryGetValue(folder, out b)) return false;
            try { return b.Proc != null && !b.Proc.HasExited; } catch { return false; }
        }

        public string UrlOf(string folder)
        {
            Berjalan b;
            return folder != null && _jalan.TryGetValue(folder, out b) ? b.Url : null;
        }

        public IEnumerable<string> RunningFolders { get { return new List<string>(_jalan.Keys); } }

        /// <summary>Jalankan skrip. Mengembalikan pesan kesalahan, atau null bila berhasil dimulai.</summary>
        public string Start(NodeApp app, BinPackage node)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return "Proyek belum punya folder.";
            if (!Directory.Exists(app.Path)) return "Folder tidak ada: " + app.Path;
            if (!PackageJson.LooksLikeNodeProject(app.Path))
                return "Tidak ada package.json di " + app.Path + ".";
            if (IsRunning(app.Path)) return "Proyek ini sudah jalan.";

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
                _jalan[app.Path] = entri;

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
                    Berjalan ada;
                    if (_jalan.TryGetValue(app.Path, out ada) && ReferenceEquals(ada.Proc, p))
                        _jalan.Remove(app.Path);
                    int kode;
                    try { kode = p.ExitCode; } catch { kode = -1; }
                    Lapor(app.Path, "-- proses berakhir (kode " + kode + ") --");
                    Ubah(app.Path, false);
                };

                p.Start();
                ProcessJob.Ikat(p);   // lihat ProcessJob: anak tidak boleh hidup lebih lama dari Phoron
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _jalan.Remove(app.Path);
                return "Tidak bisa menjalankan: " + ex.Message;
            }

            Lapor(app.Path, "> " + perintah + "   (di " + app.Path + ")");
            Ubah(app.Path, true);
            return null;
        }

        public void Stop(string folder)
        {
            Berjalan b;
            if (folder == null || !_jalan.TryGetValue(folder, out b)) return;
            _jalan.Remove(folder);   // dilepas dulu supaya Exited tidak melapor dua kali
            try
            {
                // Pohon proses, bukan cmd.exe-nya saja: cmd hanya pembungkus,
                // dan node.exe yang sebenarnya memegang port ada di bawahnya.
                if (b.Proc != null && !b.Proc.HasExited) Shell.KillTree(b.Proc.Id);
            }
            catch { }
            Lapor(folder, "-- dihentikan --");
            Ubah(folder, false);
        }

        public void StopAll()
        {
            foreach (var f in new List<string>(_jalan.Keys)) Stop(f);
        }

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
