using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Phoron.Core
{
    /// <summary>
    /// Memeriksa Oracle Instant Client yang akan ditemukan PHP di komputer ini.
    ///
    /// Ekstensi oci8 dan pdo_oci tidak berdiri sendiri: keduanya memanggil
    /// oci.dll milik Instant Client, dan Instant Client itu TIDAK ikut dalam
    /// paket PHP - pengguna memasangnya sendiri. Jadi DLL ekstensinya boleh ada
    /// dan arsitekturnya boleh benar, tapi pemuatannya tetap gagal.
    ///
    /// Yang membuatnya sulit: pesan Windows-nya sama sekali tidak menyebut
    /// Oracle. Instant Client 32-bit dengan PHP 64-bit berbunyi
    /// "%1 is not a valid Win32 application", dan tanpa Instant Client sama
    /// sekali berbunyi "The specified module could not be found" - keduanya
    /// menunjuk berkas php_oci8_*.dll yang sebenarnya baik-baik saja.
    ///
    /// Dua kalimat itu dibuktikan langsung: PHP 5.6 x64 dengan PATH berisi
    /// HANYA client 32-bit menghasilkan kalimat pertama; PATH tanpa client
    /// sama sekali menghasilkan kalimat kedua.
    /// </summary>
    public static class Oracle
    {
        /// <summary>Nama ekstensi yang bergantung pada Instant Client.</summary>
        public static readonly string[] Ekstensi =
            { "oci8", "oci8_11g", "oci8_12c", "oci8_19", "pdo_oci" };

        public class Hasil
        {
            /// <summary>oci.dll pertama yang akan ditemukan PHP, atau null.</summary>
            public string JalurDll;
            public string Arsitektur = "";
            public string ArsitekturPhp = "";
            public bool Ada { get { return JalurDll != null; } }
            /// <summary>Benar bila oci8 punya peluang termuat.</summary>
            public bool Layak;
            /// <summary>Penjelasan siap tampil; kosong bila tidak ada masalah.</summary>
            public string Pesan = "";
        }

        /// <summary>
        /// Folder Instant Client yang harus ditaruh PALING DEPAN di PATH untuk
        /// PHP ini dan daftar ekstensinya - atau null bila tidak ada yang perlu
        /// diatur.
        ///
        /// Periksa() hanya menjamin arsitekturnya sepadan, dan itu cukup selama
        /// satu PHP memakai satu client. Kolam PHP per situs mengubahnya: di
        /// mesin pengembang PATH berisi Instant Client 12.1 LEBIH DULU daripada
        /// 19.24. PHP 5.6 (oci8_11g) puas dengan 12.1; php_oci8_19.dll milik
        /// PHP 8.3 butuh client 19 ke atas. Tanpa pengaturan ini, kolam 8.3 akan
        /// memuat client yang terlalu tua. Dibuktikan: dengan folder 19.24 di
        /// depan, oci_client_version() di php-cgi 8.3 menjawab 19.24.0.0.0.
        /// </summary>
        public static string FolderUntuk(BinPackage php, IEnumerable<string> ekstensi, string path = null)
        {
            if (php == null || ekstensi == null) return null;
            int perlu = 0;
            foreach (var e in ekstensi)
            {
                var n = (e ?? "").ToLowerInvariant();
                if (n == "oci8_19") perlu = Math.Max(perlu, 19);
                else if (n == "oci8_12c") perlu = Math.Max(perlu, 12);
                else if (n == "oci8_11g") perlu = Math.Max(perlu, 11);
            }
            if (perlu == 0) return null;

            var archPhp = string.IsNullOrEmpty(php.Arch) ? BinProbe.Arsitektur(php.MainExe) : php.Arch;
            var daftarPath = path ?? Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var d in daftarPath.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string calon;
                try { calon = Path.Combine(d.Trim(), "oci.dll"); }
                catch { continue; }
                if (!File.Exists(calon)) continue;
                if (!ProfileStore.ArsitekturSepadan(BinProbe.Arsitektur(calon), archPhp)) continue;
                int mayor;
                try { mayor = System.Diagnostics.FileVersionInfo.GetVersionInfo(calon).FileMajorPart; }
                catch { continue; }
                // Client yang lebih baru boleh; yang lebih tua dari yang dibutuhkan
                // ekstensinya tidak.
                if (mayor >= perlu) return d.Trim();
            }
            return null;
        }

        public static bool AdalahEkstensiOracle(string nama)
        {
            return Ekstensi.Any(x => string.Equals(x, nama, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Urutan pencarian mengikuti aturan Windows untuk DLL yang diimpor:
        /// folder exe lebih dulu, baru PATH.
        ///
        /// SELURUH PATH ditelusuri, bukan berhenti di oci.dll pertama. Sudah
        /// dibuktikan dengan php.exe sungguhan: PATH berisi client 32-bit di
        /// posisi PALING DEPAN diikuti client 64-bit tetap memuat oci8 dengan
        /// mulus - Windows melewati yang tidak sepadan dan meneruskan pencarian.
        /// Berhenti di yang pertama akan membuat Phoron menuduh keadaan yang
        /// sebenarnya sehat, lalu membuang oci8 dari php.ini tanpa alasan.
        /// </summary>
        public static Hasil Periksa(BinPackage php, string path = null)
        {
            var h = new Hasil();
            if (php == null) { h.Layak = true; return h; }
            h.ArsitekturPhp = string.IsNullOrEmpty(php.Arch) ? BinProbe.Arsitektur(php.MainExe) : php.Arch;

            var folder = new List<string> { php.Path };
            var daftarPath = path ?? Environment.GetEnvironmentVariable("PATH") ?? "";
            folder.AddRange(daftarPath.Split(';'));

            string pertama = null, pertamaArch = "";
            foreach (var d in folder)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string calon;
                try { calon = Path.Combine(d.Trim(), "oci.dll"); }
                catch { continue; }
                if (!File.Exists(calon)) continue;

                var arch = BinProbe.Arsitektur(calon);
                if (pertama == null) { pertama = calon; pertamaArch = arch; }

                if (ProfileStore.ArsitekturSepadan(arch, h.ArsitekturPhp))
                {
                    // Ketemu yang sepadan; itulah yang akan dipakai Windows.
                    h.JalurDll = calon;
                    h.Arsitektur = arch;
                    h.Layak = true;
                    return h;
                }
            }

            h.JalurDll = pertama;
            h.Arsitektur = pertamaArch;
            h.Layak = false;
            h.Pesan = h.Ada
                ? "Oracle Instant Client yang ada di komputer ini (" + h.JalurDll + ") berarsitektur "
                  + h.Arsitektur + ", sedangkan PHP " + php.Version + " berarsitektur "
                  + h.ArsitekturPhp + ". Keduanya HARUS sama, dan tidak ada client "
                  + h.ArsitekturPhp + " lain di PATH. Pasang Instant Client " + h.ArsitekturPhp
                  + " lalu tambahkan foldernya ke PATH Windows, atau pakai PHP " + h.Arsitektur + "."
                : "Oracle Instant Client tidak ditemukan di PATH, jadi oci8 dan pdo_oci tidak akan "
                  + "bisa dimuat. Pasang Instant Client "
                  + (h.ArsitekturPhp.Length > 0 ? h.ArsitekturPhp : "yang sesuai arsitektur PHP")
                  + ", lalu tambahkan foldernya ke PATH Windows.";
            return h;
        }
    }
}
