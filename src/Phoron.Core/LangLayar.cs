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
            yield return BarisBerandaD();
        }

        static string[][] BarisUmum()
        {
            return new[]
            {
                new[] { "Versi {0}", "Version {0}", "Versi {0}", "Versi {0}" },

                // Kelompok di halaman Pengaturan
                new[] { "Mulai dan baki sistem", "Startup and system tray", "Miwiti lan baki sistem", "Mulai wan baki sistem" },
                new[] { "Situs dan berkas hosts", "Sites and the hosts file", "Situs lan berkas hosts", "Situs wan barakas hosts" },
                new[] { "Log dan terminal", "Logs and terminal", "Log lan terminal", "Log wan terminal" },

                // Tabel Situs
                new[] { "Nama .test", ".test name", "Jeneng .test", "Ngaran .test" },
                new[] { "Document root: {0}", "Document root: {0}", "Document root: {0}", "Document root: {0}" },
                new[] { "belum di hosts", "not in hosts yet", "durung ana ing hosts", "balum di hosts" },
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
