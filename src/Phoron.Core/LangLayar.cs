using System.Collections.Generic;

namespace Phoron.Core
{
    public static partial class Lang
    {
        /// <summary>
        /// Daftar kamus tambahan per layar. Setiap berkas LangLayar*.cs
        /// menyumbang satu metode yang mengembalikan baris
        /// { Indonesia, Inggris, Jawa, Banjar }, dan didaftarkan di sini.
        /// </summary>
        static IEnumerable<string[][]> KumpulanLayar()
        {
            yield return BarisUmum();
            yield return BarisBeranda();
            yield return BarisSitus();
            yield return BarisSetelan();
            yield return BarisVersi();
        }

        static string[][] BarisUmum()
        {
            return new[]
            {
                new[] { "Versi {0}", "Version {0}", "Versi {0}", "Versi {0}" },
                new[] { "Pindah ke profil ini, lalu langsung nyalakan Apache dan MySQL",
                        "Switch to this profile, then start Apache and MySQL right away",
                        "Pindah menyang profil iki, banjur langsung uripake Apache lan MySQL",
                        "Pindah ka profil ini, imbah itu langsung hidupakan Apache wan MySQL" },
                new[] { "Basis data yang namanya diawali tanda minus tidak bisa diekspor lewat mysqldump.",
                        "A database whose name starts with a minus sign cannot be exported with mysqldump.",
                        "Basis data sing jenenge diwiwiti tandha minus ora bisa diekspor lewat mysqldump.",
                        "Basis data nang ngarannya diawali tanda minus kada kawa diekspor lewat mysqldump." },
                new[] { "Pindah ke profil ini tanpa mengubah keadaan layanan",
                        "Switch to this profile without changing the state of the services",
                        "Pindah menyang profil iki tanpa ngowahi kahanan layanan",
                        "Pindah ka profil ini kada maubah kaadaan layanan" },
            };
        }
    }
}
