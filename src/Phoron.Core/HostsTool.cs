using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>
    /// Mendaftarkan nama situs ke berkas hosts SEKALI SAJA, lalu dilupakan.
    ///
    /// Alasannya: autostart Phoron lewat kunci Run milik Windows selalu jalan
    /// tanpa hak Administrator - Windows sengaja begitu, kalau tidak setiap boot
    /// akan disambut kotak UAC. Akibatnya berkas hosts tidak bisa disunting dan
    /// nama .test tidak kebuka, padahal http://localhost/proyek/ baik-baik saja.
    ///
    /// Entri di berkas hosts itu MENETAP. Jadi tidak perlu Phoron berhak admin
    /// selamanya: cukup satu kali menulis, sesudah itu Phoron boleh jalan sebagai
    /// pengguna biasa dan nama .test tetap hidup.
    ///
    /// Yang didaftarkan adalah situs dari SELURUH profil, bukan cuma profil yang
    /// sedang aktif. Kalau hanya yang aktif, berganti profil berarti minta admin
    /// lagi - persis yang ingin dihindari.
    /// </summary>
    public static class HostsTool
    {
        /// <summary>Nama host semua situs di semua profil, tanpa localhost.</summary>
        public static List<string> SemuaNamaSitus()
        {
            var nama = new List<string>();
            foreach (var profil in ProfileStore.LoadAll())
            {
                List<Site> situs;
                try { situs = SiteScanner.Scan(profil); }
                catch { continue; }   // satu profil rusak tidak boleh membatalkan sisanya
                foreach (var s in situs)
                    if (!string.IsNullOrWhiteSpace(s.HostName)
                        && !string.Equals(s.HostName, "localhost", StringComparison.OrdinalIgnoreCase))
                        nama.Add(s.HostName.Trim());
            }
            return nama.Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Nama yang belum ada di berkas hosts - dipakai untuk memutuskan perlu tidaknya minta admin.</summary>
        public static List<string> BelumTerdaftar()
        {
            var ada = HostsFile.AllNames();
            return SemuaNamaSitus().Where(n => !ada.Contains(n)).ToList();
        }

        /// <summary>
        /// Tulis blok Phoron berisi seluruh nama situs. Harus dipanggil dari proses
        /// yang sudah berhak Administrator; kalau tidak, melempar
        /// UnauthorizedAccessException dari HostsFile.
        /// </summary>
        public static List<string> DaftarkanSemua()
        {
            var nama = SemuaNamaSitus();
            HostsFile.Sync(nama);
            return nama;
        }
    }
}
