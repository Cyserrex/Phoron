using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Phoron.Core
{
    /// <summary>
    /// Mencari tahu apakah sebuah port sudah dipakai, dan oleh siapa. Ini bukan
    /// kemewahan: keluhan paling sering pada perkakas semacam ini adalah "Apache
    /// tidak mau nyala" padahal penyebabnya Laragon/XAMPP/IIS masih memegang port 80,
    /// dan pesan bawaan Apache tidak menyebut proses mana yang bersalah.
    /// </summary>
    public static class PortCheck
    {
        public class Usage
        {
            public int Port;
            public bool InUse;
            public int Pid;
            public string ProcessName = "";

            /// <summary>
            /// Jalur exe pemegang port. Inilah yang membedakan httpd milik
            /// Phoron dari httpd milik Laragon - dan tanpa itu, tawaran
            /// "hentikan proses yang tertinggal" meminta orang memutuskan
            /// tanpa satu pun keterangan.
            /// </summary>
            public string Jalur = "";
            public string Describe()
            {
                if (!InUse) return "Port " + Port + " bebas.";
                var who = string.IsNullOrEmpty(ProcessName) ? "proses tak dikenal" : ProcessName;
                return "Port " + Port + " sedang dipakai " + who + (Pid > 0 ? " (PID " + Pid + ")" : "") + ".";
            }
        }

        public static bool IsFree(int port)
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                return !props.GetActiveTcpListeners().Any(e => e.Port == port);
            }
            catch
            {
                // Bila API tidak bisa dipakai, coba ikat langsung - lebih lambat
                // tapi tidak pernah salah positif.
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, port);
                    l.Start();
                    l.Stop();
                    return true;
                }
                catch { return false; }
            }
        }

        public static Usage Check(int port)
        {
            var u = new Usage { Port = port, InUse = !IsFree(port) };
            if (!u.InUse) return u;
            u.Pid = FindPid(port);
            if (u.Pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(u.Pid);
                    u.ProcessName = proc.ProcessName;
                    // Bisa gagal untuk proses milik pengguna lain atau yang
                    // berhak lebih tinggi; namanya saja sudah cukup berguna.
                    try { u.Jalur = proc.MainModule.FileName; }
                    catch { }
                }
                catch { }
            }
            return u;
        }

        /// <summary>
        /// PID pemilik port diambil dari netstat -ano. .NET Framework tidak
        /// mengekspos pemetaan port-ke-PID, dan satu-satunya alternatifnya adalah
        /// P/Invoke GetExtendedTcpTable - jauh lebih banyak kode untuk informasi
        /// yang hanya dipakai di pesan kesalahan.
        /// </summary>
        static int FindPid(int port)
        {
            try
            {
                var res = Shell.Run("netstat.exe", "-ano -p tcp", Paths.Root, 10000);
                foreach (var line in res.StdOut.Split('\n'))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    if (!parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    var local = parts[1];
                    var colon = local.LastIndexOf(':');
                    if (colon < 0) continue;
                    int p;
                    if (!int.TryParse(local.Substring(colon + 1), out p) || p != port) continue;
                    int pid;
                    if (int.TryParse(parts[4], out pid)) return pid;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>Periksa semua port sebuah profil sekaligus; hanya yang bermasalah yang dikembalikan.</summary>
        public static List<Usage> Conflicts(Profile p, bool https)
        {
            // Port layanan yang TIDAK dipakai profil ini tidak diperiksa sama
            // sekali. Kalau tidak, profil tanpa MySQL akan mengeluh port 3306
            // dipegang orang lain - padahal ia memang tidak berniat memakainya,
            // dan yang memegangnya sering justru Laragon atau XAMPP milik orang
            // itu sendiri yang sedang dipakai.
            var ports = new List<int>();
            if (p.PakaiWeb)
            {
                ports.Add(p.HttpPort);
                if (https) ports.Add(p.HttpsPort);
            }
            if (p.PakaiMySql) ports.Add(p.MySqlPort);
            return ports.Distinct().Select(Check).Where(u => u.InUse).ToList();
        }
    }
}
