using System;
using System.Security.Cryptography;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Menyandi nilai kecil yang tidak pantas tergeletak sebagai teks biasa di
    /// phoron.ini - untuk saat ini hanya sandi basis data.
    ///
    /// KENAPA ADA SAMA SEKALI. phoron.ini adalah berkas teks di folder yang bisa
    /// dibaca siapa pun yang bisa membaca cakram: ia ikut tersalin saat folder
    /// Phoron di-zip, ikut terbawa ke cadangan, dan ikut terkirim kalau orang
    /// melampirkannya saat melaporkan masalah. Phoron sudah pernah membuang
    /// token GitHub dari sana karena alasan yang persis sama, dan membuangnya
    /// secara AKTIF - bukan sekadar berhenti menulisnya.
    ///
    /// DPAPI dipilih karena ia sudah ada di Windows, jadi tidak ada dependensi
    /// baru dan tidak ada kunci yang harus Phoron simpan sendiri - kunci yang
    /// disimpan di sebelah barang yang dikuncinya tidak mengunci apa pun.
    /// Cakupannya CurrentUser: hasilnya hanya bisa dibuka oleh akun Windows yang
    /// sama di mesin yang sama.
    ///
    /// YANG TIDAK DIJANJIKANNYA. Ini bukan perlindungan terhadap orang yang
    /// sudah bisa menjalankan program sebagai akun Anda - orang itu bisa memanggil
    /// Buka() persis seperti Phoron. Yang dicegahnya adalah sandi ikut terbawa
    /// keluar dari mesin ini di dalam berkas yang tersalin.
    /// </summary>
    public static class Rahasia
    {
        // Entropi tambahan: hasil sandi Phoron tidak bisa dibuka begitu saja oleh
        // program lain yang memanggil DPAPI dengan cakupan pengguna yang sama.
        static readonly byte[] Bumbu = Encoding.UTF8.GetBytes("Phoron/rahasia/v1");

        /// <summary>Sandi jadi teks base64. Kosong tetap kosong.</summary>
        public static string Tutup(string teks)
        {
            if (string.IsNullOrEmpty(teks)) return "";
            try
            {
                var data = Encoding.UTF8.GetBytes(teks);
                var acak = ProtectedData.Protect(data, Bumbu, DataProtectionScope.CurrentUser);
                Array.Clear(data, 0, data.Length);
                return Convert.ToBase64String(acak);
            }
            catch
            {
                // DPAPI bisa gagal pada profil pengguna yang tidak lengkap. Lebih
                // baik tidak menyimpan apa pun daripada menyimpannya terbuka.
                return "";
            }
        }

        /// <summary>
        /// Kembalikan teks aslinya. Mengembalikan kosong bila nilainya berasal
        /// dari akun atau mesin lain - itu keadaan yang wajar, misalnya setelah
        /// folder Phoron disalin, dan bukan kerusakan.
        /// </summary>
        public static string Buka(string tersandi)
        {
            if (string.IsNullOrEmpty(tersandi)) return "";
            try
            {
                var acak = Convert.FromBase64String(tersandi);
                var data = ProtectedData.Unprotect(acak, Bumbu, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }
}
