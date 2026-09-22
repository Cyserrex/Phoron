using System.Collections.Generic;
using System.Windows.Media;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Kuas warna untuk baris log, dibuat sekali lalu dibekukan.
    ///
    /// Sebelumnya tiap baris membuat SolidColorBrush baru, dan warnanya diurai
    /// dari teks heksadesimal lewat ColorConverter - dua hal yang hasilnya
    /// selalu sama persis. Pada satu semburan keluaran mysqld, panel Aktivitas
    /// menggambar ulang dua ratus paragraf untuk setiap baris yang masuk;
    /// kalikan dengan penguraian warna per paragraf, dan yang sebenarnya
    /// dikerjakan mesin ini jauh lebih banyak daripada yang terlihat.
    ///
    /// Freeze() bukan hiasan: kuas yang beku boleh dipakai bersama di seluruh
    /// pohon tampilan tanpa disalin diam-diam, dan WPF berhenti memasang
    /// pelacak perubahan padanya.
    /// </summary>
    static class KuasLog
    {
        /// <summary>
        /// Warna jam di awal baris. Abu-abu nada tengah, bukan Opacity: Run
        /// memang tidak punya Opacity, dan nada ini terbaca baik di tema terang
        /// maupun gelap.
        /// </summary>
        public static readonly Brush Jam = Bekukan(Color.FromRgb(0x8A, 0x8A, 0x8A));

        static readonly Dictionary<JenisPesan, Brush> _peta = Rakit();

        static Dictionary<JenisPesan, Brush> Rakit()
        {
            var peta = new Dictionary<JenisPesan, Brush>();
            foreach (JenisPesan jenis in System.Enum.GetValues(typeof(JenisPesan)))
            {
                var heks = LogWarna.Heks(jenis);
                if (heks.Length == 0) continue;   // Biasa: ikut warna teks biasa
                peta[jenis] = Bekukan((Color)ColorConverter.ConvertFromString(heks));
            }
            return peta;
        }

        static Brush Bekukan(Color warna)
        {
            var kuas = new SolidColorBrush(warna);
            kuas.Freeze();
            return kuas;
        }

        /// <summary>Kuas untuk golongan ini, atau null bila barisnya memakai warna biasa.</summary>
        public static Brush Untuk(JenisPesan jenis)
        {
            Brush kuas;
            return _peta.TryGetValue(jenis, out kuas) ? kuas : null;
        }

        /// <summary>
        /// Baris galat ditebalkan juga. Warna saja tidak cukup bagi yang sulit
        /// membedakan merah dan hijau.
        /// </summary>
        public static bool Tebal(JenisPesan jenis) { return jenis == JenisPesan.Galat; }
    }
}
