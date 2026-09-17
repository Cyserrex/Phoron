using System;

namespace Phoron.Core
{
    /// <summary>Golongan sebuah baris log, menentukan warnanya di layar.</summary>
    public enum JenisPesan
    {
        Biasa,
        Berhasil,
        Peringatan,
        Galat,
    }

    /// <summary>
    /// Menggolongkan baris log supaya yang penting bisa ditemukan sekilas.
    ///
    /// Panel Aktivitas kerap berisi ratusan baris yang seragam - mysqld sendiri
    /// mencetak belasan [Warning] tiap kali menyala - dan satu baris "Konfigurasi
    /// Nginx ditolak" tenggelam di antaranya. Yang dicari orang saat membuka
    /// panel itu hampir selalu baris yang gagal.
    ///
    /// Penggolongan ditaruh di Core, bukan di kode layar, supaya bisa diuji:
    /// aturan berbasis kata kunci mudah sekali salah tangkap, dan salah warna
    /// pada baris galat lebih buruk daripada tidak berwarna sama sekali.
    /// </summary>
    public static class LogWarna
    {
        // Diperiksa berurutan: galat lebih dulu, karena sebuah baris bisa memuat
        // kata dari dua golongan sekaligus ("Peringatan: ... gagal ditulis").
        static readonly string[] KataGalat =
        {
            "gagal", "ditolak", "galat", "error", "[emerg]", "[alert]", "[crit]",
            "failed", "meledak", "berhenti seketika", "cannot load", "not a valid",
            "tidak bisa", "tidak dapat", "unable to", "could not", "syntax error",
            "tidak ditemukan", "kadaluwarsa", "kasalahan",
            // Keluaran perkakas Node; hampir selalu berbahasa Inggris apa pun
            // bahasa yang dipilih di Phoron.
            "npm err!", "eaddrinuse", "cannot find module", "module not found",
        };

        static readonly string[] KataPeringatan =
        {
            "peringatan", "[warning]", "warning:", "deprecated", "[note]",
            "sudah dipakai", "lebih dari satu", "dilewati", "belum ada",
            "tidak akan", "hati-hati", "perlu hak administrator",
            "npm warn",
            // Ditulis eksplisit supaya baris peringatan HSTS berwarna sama di
            // keempat bahasa - kata "peringatan" hanya ada di yang Indonesia.
            "hsts",
        };

        static readonly string[] KataBerhasil =
        {
            "jalan di port", "berhasil", "ditulis ulang", "selesai", "sudah terdaftar",
            "dibuat di", "tersimpan", "terpasang", "dihentikan", "sudah versi terbaru",
            // Padanan "dihentikan" di bahasa lain, supaya baris yang sama tetap
            // berwarna sama saat bahasanya diganti.
            "stopped", "dipateni", "dipajahakan",
            // Keluaran server pengembangan Node saat sudah siap melayani.
            "ready in", "ready on", "compiled successfully",
        };

        public static JenisPesan Golongkan(string baris)
        {
            if (string.IsNullOrWhiteSpace(baris)) return JenisPesan.Biasa;
            var t = baris.ToLowerInvariant();

            if (Memuat(t, KataGalat)) return JenisPesan.Galat;
            if (Memuat(t, KataPeringatan)) return JenisPesan.Peringatan;
            if (Memuat(t, KataBerhasil)) return JenisPesan.Berhasil;
            return JenisPesan.Biasa;
        }

        static bool Memuat(string teks, string[] kata)
        {
            foreach (var k in kata)
                if (teks.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Warna per golongan, dipilih supaya terbaca di tema terang MAUPUN gelap.
        /// Merah tua yang bagus di atas putih berubah jadi lumpur di atas hitam,
        /// dan sebaliknya - jadi yang dipakai nada tengah.
        /// </summary>
        public static string Heks(JenisPesan jenis)
        {
            switch (jenis)
            {
                case JenisPesan.Galat: return "#F2544B";
                case JenisPesan.Peringatan: return "#D9950A";
                case JenisPesan.Berhasil: return "#3BA55D";
                default: return "";     // kosong = pakai warna teks bawaan tema
            }
        }
    }
}
