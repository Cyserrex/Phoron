using System.Windows.Media;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Warna titik penanda keadaan layanan.
    ///
    /// Ditaruh di satu tempat karena dipakai dua layar sekaligus - panel kiri
    /// dan kartu di Beranda - dan titik yang warnanya berbeda untuk keadaan
    /// yang sama justru membuat orang ragu mana yang benar.
    /// </summary>
    internal static class WarnaLayanan
    {
        public static Brush Titik(ServiceState s)
        {
            switch (s)
            {
                case ServiceState.Jalan: return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                case ServiceState.Gagal: return new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x4A));
                case ServiceState.Menyalakan:
                case ServiceState.Mematikan: return new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x20));
                default: return new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
            }
        }

        /// <summary>
        /// Keadaan dalam kata, untuk ditulis di sebelah titiknya - supaya
        /// artinya tidak hanya dibawa warna. Kuncinya kata kecil yang sudah ada
        /// di kamus ("jalan", "gagal", ...); huruf pertamanya dibesarkan di sini.
        /// </summary>
        public static string Teks(ServiceState s)
        {
            switch (s)
            {
                case ServiceState.Jalan: return Besar(Lang.T("jalan"));
                case ServiceState.Menyalakan: return Besar(Lang.T("menyalakan")) + "...";
                case ServiceState.Mematikan: return Besar(Lang.T("mematikan")) + "...";
                case ServiceState.Gagal: return Besar(Lang.T("gagal"));
                default: return Besar(Lang.T("berhenti"));
            }
        }

        public static string Besar(string t)
        {
            return string.IsNullOrEmpty(t) ? t : char.ToUpper(t[0]) + t.Substring(1);
        }
    }
}
