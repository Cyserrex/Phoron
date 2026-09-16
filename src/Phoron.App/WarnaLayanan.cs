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
    }
}
