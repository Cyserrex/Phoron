using System;
using System.Security.Cryptography;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Menyandikan nilai rahasia sebelum ditulis ke phoron.ini.
    ///
    /// phoron.ini adalah berkas teks biasa yang dibuka orang, ikut dalam
    /// backup, dan kadang dibawa antar komputer. Token GitHub yang tersimpan
    /// apa adanya di sana berarti siapa pun yang sempat melihat berkas itu
    /// memegang token tersebut.
    ///
    /// DPAPI dipakai dengan lingkup PENGGUNA: hasilnya hanya bisa dibuka oleh
    /// akun Windows yang sama di komputer yang sama. Berkas phoron.ini yang
    /// disalin ke komputer lain tetap terbaca seluruhnya, kecuali barisnya -
    /// dan itu memang yang diinginkan.
    ///
    /// Ini BUKAN brankas. Program lain yang berjalan sebagai pengguna yang sama
    /// tetap bisa membukanya. Tujuannya menutup jalan yang paling sering
    /// terjadi - mata yang tidak sengaja membaca, dan berkas yang ikut tersalin -
    /// bukan menahan penyerang yang sudah berada di dalam akun.
    /// </summary>
    public static class Rahasia
    {
        // Entropi tambahan, supaya blob ini tidak bisa dibuka oleh aplikasi lain
        // yang kebetulan memanggil DPAPI dengan parameter baku.
        static readonly byte[] Garam = Encoding.UTF8.GetBytes("Phoron.Rahasia.v1");

        /// <summary>Menyandikan teks jadi base64; kosong tetap kosong.</summary>
        public static string Sandi(string teks)
        {
            if (string.IsNullOrEmpty(teks)) return "";
            try
            {
                var buf = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(teks), Garam, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(buf);
            }
            catch
            {
                // Lebih baik tidak menyimpan sama sekali daripada menyimpan
                // token apa adanya karena penyandiannya gagal.
                return "";
            }
        }

        /// <summary>Membuka hasil Sandi(). Mengembalikan "" bila gagal - misalnya berkasnya dari komputer lain.</summary>
        public static string Buka(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return "";
            try
            {
                var buf = ProtectedData.Unprotect(
                    Convert.FromBase64String(base64), Garam, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(buf);
            }
            catch { return ""; }
        }

        /// <summary>
        /// Bentuk aman untuk ditampilkan atau dicatat di log: "ghp_1234...wxyz".
        /// Token tidak boleh pernah muncul utuh di layar maupun di berkas log.
        /// </summary>
        public static string Samar(string teks)
        {
            if (string.IsNullOrEmpty(teks)) return "";
            if (teks.Length <= 12) return new string('*', teks.Length);
            return teks.Substring(0, 8) + new string('.', 3) + teks.Substring(teks.Length - 4);
        }
    }
}
