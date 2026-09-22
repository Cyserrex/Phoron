using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoron.Core
{
    /// <summary>Pembungkus tipis untuk menjalankan proses luar.</summary>
    public static class Shell
    {
        public class RunResult
        {
            public int ExitCode;
            public string StdOut = "";
            public string StdErr = "";
            public bool TimedOut;
            public string All { get { return (StdOut + "\n" + StdErr).Trim(); } }
            public bool Ok { get { return ExitCode == 0 && !TimedOut; } }
        }

        /// <summary>Jalankan dan tunggu sampai selesai. Dipakai untuk perintah singkat (uji konfigurasi, init data).</summary>
        /// <param name="stdin">
        /// Bila diisi, teks ini disuapkan ke masukan baku proses lalu masukannya
        /// ditutup. Inilah cara yang benar mengirim SQL ke mysql.exe: lewat
        /// -e "..." teksnya harus lolos dari dua lapis pengutipan (cmd dan
        /// klien), sehingga tanda kutip di dalam kueri merusak perintahnya -
        /// dan panjang baris perintah Windows dibatasi 32767 aksara, yang
        /// gampang terlampaui oleh satu INSERT saja.
        /// </param>
        /// <param name="stdinAliran">
        /// Sumber masukan baku yang disalin apa adanya, untuk isi yang terlalu
        /// besar untuk dipegang sebagai teks di memori - berkas .sql hasil dump
        /// mudah mencapai ratusan megabyte. Diabaikan bila stdin juga diisi.
        /// </param>
        public static RunResult Run(string exe, string args, string workDir = null,
                                    int timeoutMs = 120000, IDictionary<string, string> env = null,
                                    string stdin = null, Stream stdinAliran = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null || stdinAliran != null,
                CreateNoWindow = true,
                WorkingDirectory = workDir ?? Path.GetDirectoryName(exe) ?? Paths.Root,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            var result = new RunResult();
            var so = new StringBuilder();
            var se = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) se.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (stdin == null && stdinAliran != null)
                {
                    // Disalin sebagai byte, tanpa pernah jadi string: berkas dump
                    // yang besar akan meledakkan memori kalau dibaca sekaligus,
                    // dan isinya sudah UTF-8 sejak dari mysqldump.
                    try
                    {
                        stdinAliran.CopyTo(p.StandardInput.BaseStream);
                        p.StandardInput.BaseStream.Flush();
                        p.StandardInput.Close();
                    }
                    catch { }
                }
                if (stdin != null)
                {
                    // Ditulis sebagai byte UTF-8 ke aliran mentah, BUKAN lewat
                    // StandardInput.Write. Di .NET Framework penulis itu memakai
                    // halaman kode ANSI mesin - dan tidak bisa diganti, sebab
                    // ProcessStartInfo.StandardInputEncoding baru ada di .NET Core.
                    // Lewat jalur itu setiap aksara di luar ASCII sampai ke MySQL
                    // dalam keadaan rusak.
                    //
                    // Ditutup, bukan sekadar disiram: mysql.exe membaca sampai
                    // akhir masukan, jadi tanpa penutupan ia menunggu selamanya.
                    try
                    {
                        var bytes = new UTF8Encoding(false).GetBytes(stdin);
                        p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                        p.StandardInput.BaseStream.Flush();
                        p.StandardInput.Close();
                    }
                    catch { }
                }
                if (!p.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    KillTree(p.Id);
                }
                else
                {
                    // WaitForExit tanpa argumen setelah versi bertimeout: memastikan
                    // pembaca stdout/stderr asinkron sudah menguras seluruh keluaran.
                    p.WaitForExit();
                    result.ExitCode = p.ExitCode;
                }
            }
            result.StdOut = so.ToString();
            result.StdErr = se.ToString();
            return result;
        }

        /// <summary>
        /// Matikan proses beserta anak-anaknya. httpd dan mysqld memunculkan proses
        /// anak; membunuh induknya saja meninggalkan anak yang tetap memegang port,
        /// dan start berikutnya gagal dengan "address already in use".
        /// </summary>
        public static void KillTree(int pid)
        {
            try
            {
                Run("taskkill.exe", "/PID " + pid + " /T /F", Paths.Root, 15000);
            }
            catch { }
        }

        /// <summary>Buka berkas/folder/URL dengan aplikasi bawaan Windows.</summary>
        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { }
        }

        /// <summary>
        /// Buka terminal yang langsung menjalankan satu perintah, lalu tetap
        /// terbuka. Dipakai untuk perintah panjang yang keluarannya perlu
        /// dibaca utuh (npm install) - menjalankannya diam-diam di latar hanya
        /// membuat orang menunggu tanpa tahu sedang terjadi apa.
        /// </summary>
        public static void OpenTerminalWithCommand(string kind, string workDir,
                                                   IDictionary<string, string> env, string command)
        {
            string exe, args;
            switch ((kind ?? "cmd").ToLowerInvariant())
            {
                case "powershell":
                    exe = "powershell.exe";
                    args = "-NoExit -NoLogo -Command \"" + command.Replace("\"", "`\"") + "\"";
                    break;
                case "wt":
                    exe = "wt.exe";
                    args = "-d \"" + workDir + "\" cmd /k " + command;
                    break;
                default:
                    exe = "cmd.exe";
                    args = "/k " + command;
                    break;
            }
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Directory.Exists(workDir) ? workDir : Paths.Root,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            try { Process.Start(psi); }
            catch { if (exe != "cmd.exe") OpenTerminalWithCommand("cmd", workDir, env, command); }
        }

        /// <summary>Buka terminal dengan PATH yang sudah berisi PHP, MySQL, dan Composer profil aktif.</summary>
        public static void OpenTerminal(string kind, string workDir, IDictionary<string, string> env)
        {
            string exe, args;
            switch ((kind ?? "cmd").ToLowerInvariant())
            {
                case "powershell": exe = "powershell.exe"; args = "-NoExit -NoLogo"; break;
                case "wt": exe = "wt.exe"; args = "-d \"" + workDir + "\""; break;
                default: exe = "cmd.exe"; args = "/K title Phoron"; break;
            }
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Directory.Exists(workDir) ? workDir : Paths.Root,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            try { Process.Start(psi); }
            catch
            {
                // wt.exe belum tentu terpasang; jatuh ke cmd daripada gagal diam-diam.
                if (exe != "cmd.exe") OpenTerminal("cmd", workDir, env);
            }
        }
    }
}
