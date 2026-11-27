using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoron.Core.Jarak
{
    /// <summary>
    /// Settings C# dalam bentuk JSON milik struct Settings di Rust
    /// (daemon/inti/src/settings.rs). Nama kunci = nama medan Rust. Semua medan
    /// dikirim: struct Rust-nya tidak memakai nilai bawaan saat diurai.
    /// </summary>
    public static class SetelanJson
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static Dictionary<string, object> KeJson(Settings s)
        {
            return new Dictionary<string, object>
            {
                { "bin_roots", (s.BinRoots ?? new List<string>()).ToArray() },
                { "situs_terakhir", (s.SitusTerakhir ?? new List<string>()).ToArray() },
                { "active_profile", s.ActiveProfile ?? "" },
                { "auto_start_services", s.AutoStartServices },
                { "minimize_to_tray", s.MinimizeToTray },
                { "auto_vhost", s.AutoVhost },
                { "opcache", s.Opcache },
                { "akses_jaringan", s.AksesJaringan },
                { "manage_hosts", s.ManageHosts },
                { "php_ini_ke_folder_php", s.PhpIniKeFolderPhp },
                { "log_rinci", s.LogRinci },
                { "beranda_di_akar", s.BerandaDiAkar },
                { "tema", s.Tema ?? "sistem" },
                { "jendela_lebar", s.JendelaLebar },
                { "jendela_tinggi", s.JendelaTinggi },
                { "bahasa", s.Bahasa ?? Lang.Indonesia },
                { "cek_pembaruan", s.CekPembaruan },
                { "cek_terakhir", Saat(s.CekTerakhir) },
                { "terminal", s.Terminal ?? "cmd" },
                { "editor", s.Editor ?? "" },
                { "db_pengguna", s.DbPengguna ?? "" },
                { "heidi_sql", s.HeidiSql ?? "" },
                { "db_sandi", s.DbSandi ?? "" },
            };
        }

        /// <summary>DateTime sebagai waktu::Saat Rust ({detik, tick} sejak 1970 UTC); null bila belum pernah.</summary>
        public static object Saat(DateTime t)
        {
            if (t == DateTime.MinValue) return null;
            var utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime()
                    : DateTime.SpecifyKind(t, DateTimeKind.Utc);
            long ticks = utc.Ticks - Epoch.Ticks;
            long detik = ticks >= 0 ? ticks / TimeSpan.TicksPerSecond
                                    : -((-ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);
            long sisa = ticks - detik * TimeSpan.TicksPerSecond;
            return new Dictionary<string, object> { { "detik", detik }, { "tick", sisa } };
        }

        /// <summary>Kebalikan <see cref="Saat(DateTime)"/>; DateTime.MinValue bila kosong.</summary>
        public static DateTime DariSaat(object o)
        {
            var d = Protokol.Objek(o);
            if (d == null) return DateTime.MinValue;
            long detik = Protokol.Angka(d, "detik"), tick = Protokol.Angka(d, "tick");
            try { return new DateTime(Epoch.Ticks + detik * TimeSpan.TicksPerSecond + tick, DateTimeKind.Utc); }
            catch { return DateTime.MinValue; }
        }
    }
}
