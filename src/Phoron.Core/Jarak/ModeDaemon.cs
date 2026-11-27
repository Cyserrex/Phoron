using System;
using System.IO;

namespace Phoron.Core.Jarak
{
    /// <summary>
    /// Menentukan apakah UI ini klien phorond atau masih memegang layanan sendiri.
    ///
    /// Aturannya sengaja sederhana dan terlihat dari luar: ada phorond.exe di
    /// sebelah Phoron.exe = mode daemon. Tanpa berkas itu Phoron berperilaku
    /// persis seperti versi sebelumnya - pemasangan lama yang diperbarui hanya
    /// sebagian tidak tiba-tiba kehilangan Apache-nya.
    ///
    /// PHORON_DAEMON=1 memaksa mode daemon (uji, atau phorond di folder lain
    /// lewat PHORON_DAEMON_EXE); PHORON_DAEMON=0 memaksa mode lama.
    /// PHORON_PIPE mengganti nama pipe, supaya uji tidak menyentuh Phoron yang
    /// sedang dipakai.
    /// </summary>
    public static class ModeDaemon
    {
        public const string NamaExe = "phorond.exe";

        /// <summary>Penimpa untuk uji; null = baca berkas dan lingkungan.</summary>
        public static bool? Paksa;

        static string FolderExe
        {
            get
            {
                try
                {
                    var exe = System.Reflection.Assembly.GetEntryAssembly();
                    if (exe != null && !string.IsNullOrEmpty(exe.Location))
                        return Path.GetDirectoryName(exe.Location);
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>Jalur phorond.exe yang dipakai (ada atau tidak).</summary>
        public static string JalurDaemon
        {
            get
            {
                var lain = Environment.GetEnvironmentVariable("PHORON_DAEMON_EXE");
                if (!string.IsNullOrWhiteSpace(lain)) return lain.Trim();
                return Path.Combine(FolderExe ?? "", NamaExe);
            }
        }

        public static bool Aktif
        {
            get
            {
                if (Paksa.HasValue) return Paksa.Value;
                var env = (Environment.GetEnvironmentVariable("PHORON_DAEMON") ?? "").Trim();
                if (env == "1") return true;
                if (env == "0") return false;
                try { return File.Exists(Path.Combine(FolderExe ?? "", NamaExe)); }
                catch { return false; }
            }
        }

        /// <summary>Nama pipe (tanpa \\.\pipe\), dari PHORON_PIPE bila disetel.</summary>
        public static string NamaPipe
        {
            get
            {
                var n = (Environment.GetEnvironmentVariable("PHORON_PIPE") ?? "").Trim();
                return n.Length > 0 ? n : Protokol.NamaPipeBawaan;
            }
        }

        /// <summary>
        /// Akhiran nama objek antarproses UI (mutex instans tunggal, sinyal
        /// tampil) dari PHORON_INSTANS - untuk menjalankan UI uji di samping
        /// Phoron yang sedang dipakai.
        /// </summary>
        public static string AkhiranObjek
        {
            get
            {
                var n = (Environment.GetEnvironmentVariable("PHORON_INSTANS") ?? "").Trim();
                return n.Length > 0 ? "." + n : "";
            }
        }
    }

    /// <summary>
    /// Penanda bahwa UI baru menulis berkas milik pengguna (profil, apps.ini).
    /// Dalam mode daemon, Engine mendengarnya dan mengirim muat_ulang, supaya
    /// daemon tidak menerapkan profil versi lama.
    /// </summary>
    public static class BerkasPengguna
    {
        public static event Action Berubah;

        public static void Tandai()
        {
            var h = Berubah;
            if (h != null) { try { h(); } catch { } }
        }
    }
}
