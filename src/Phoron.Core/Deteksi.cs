using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>
    /// Menemukan pengelola stack lain yang terpasang di komputer INI.
    ///
    /// Berkas phoron.ini dan berkas profil sering berpindah komputer - disalin,
    /// ikut backup, atau dibawa bersama folder Phoron. Yang tercatat di dalamnya
    /// adalah keadaan komputer ASAL: folder bin yang ada di sana, nomor versi
    /// yang ada di sana. Di komputer lain semua itu belum tentu berlaku, dan
    /// Phoron harus menyesuaikan diri, bukan memaksakan catatan lama.
    /// </summary>
    public static class Deteksi
    {
        /// <summary>
        /// Tempat baku pengelola yang lazim. Jalur "bin" ditulis apa adanya
        /// sesuai tata letak masing-masing: Laragon dan WAMP menaruh paketnya di
        /// bawah bin, XAMPP dan MAMP langsung di akar instalasinya.
        /// </summary>
        static readonly string[] PolaJalur =
        {
            @"{0}:\laragon\bin",
            @"{0}:\xampp",
            @"{0}:\wamp64\bin",
            @"{0}:\wamp\bin",
            @"{0}:\MAMP\bin",
        };

        static readonly string[] Drive = { "C", "D", "E", "F" };

        /// <summary>Folder bin milik pengelola lain yang benar-benar ada di komputer ini.</summary>
        public static List<string> FolderBinTerpasang()
        {
            var hasil = new List<string>();
            foreach (var d in Drive)
                foreach (var pola in PolaJalur)
                {
                    var p = string.Format(pola, d);
                    if (Directory.Exists(p)
                        && !hasil.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                        hasil.Add(p);
                }
            return hasil;
        }

        /// <summary>
        /// Folder bin terpasang yang BELUM terdaftar - dipakai untuk menawarkan
        /// diri, bukan menambah diam-diam. Menambah folder bin mengubah daftar
        /// versi yang terlihat, dan itu harus jadi keputusan pengguna.
        /// </summary>
        public static List<string> BelumTerdaftar(IEnumerable<string> terdaftar)
        {
            var ada = new HashSet<string>(
                (terdaftar ?? Enumerable.Empty<string>()).Select(x => (x ?? "").TrimEnd('\\')),
                StringComparer.OrdinalIgnoreCase);
            return FolderBinTerpasang().Where(p => !ada.Contains(p.TrimEnd('\\'))).ToList();
        }
    }
}
