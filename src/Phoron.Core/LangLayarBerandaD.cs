namespace Phoron.Core
{
    public static partial class Lang
    {
        /// <summary>Beranda versi D, rail navigasi, dan menu baki: { id, en, jv, bjn }.</summary>
        static string[][] BarisBerandaD()
        {
            return new[]
            {
                // Baris PHP
                new[] { "PHP siap", "PHP ready", "PHP siap", "PHP siap" },
                new[] { "Belum ada profil", "No profile yet", "Durung ana profil", "Balum ada profil" },
                new[] { "Profil ini tidak memakai layanan", "This profile uses no services", "Profil iki ora nganggo layanan", "Profil ini kada mamakai layanan" },
                new[] { "Ada layanan yang gagal", "A service has failed", "Ana layanan sing gagal", "Ada layanan nang gagal" },
                new[] { "Menyalakan layanan...", "Starting services...", "Nguripake layanan...", "Mahidupakan layanan..." },
                new[] { "Mematikan layanan...", "Stopping services...", "Matheni layanan...", "Mamajahakan layanan..." },
                new[] { "Sebagian layanan jalan", "Some services are running", "Sebagian layanan mlaku", "Sabagian layanan bajalan" },
                new[] { "Layanan berhenti", "Services stopped", "Layanan mandheg", "Layanan bapajah" },
                new[] { "{0} situs", "{0} sites", "{0} situs", "{0} situs" },
                new[] { "Rincian layanan", "Service details", "Rincian layanan", "Rincian layanan" },
                new[] { "Nyalakan", "Start", "Uripake", "Hidupakan" },

                // Proyek Node
                new[] { "Proyek Node", "Node projects", "Proyek Node", "Proyek Node" },
                new[] { "Kelola proyek", "Manage projects", "Atur proyek", "Atur proyek" },
                new[] { "{0} jalan · {1} proyek", "{0} of {1} running", "{0} mlaku · {1} proyek", "{0} bajalan · {1} proyek" },
                new[] { "Buka", "Open", "Bukak", "Buka" },
                new[] { "Proyek belum dipilih.", "No project selected.", "Durung ana proyek sing dipilih.", "Balum ada proyek nang dipilih." },
                new[] { "Belum ada proyek Node. Tambahkan proyek Next.js, Vite, atau Astro di halaman Node, lalu jalankan dari sini atau dari menu baki.",
                        "No Node projects yet. Add a Next.js, Vite or Astro project on the Node page, then run it from here or from the tray menu.",
                        "Durung ana proyek Node. Tambahna proyek Next.js, Vite, utawa Astro ing kaca Node, banjur uripake saka kene utawa saka menu baki.",
                        "Balum ada proyek Node. Tambahakan proyek Next.js, Vite, atawa Astro di laman Node, imbah itu jalanakan matan sini atawa matan menu baki." },

                // Situs PHP terakhir
                new[] { "Situs PHP terakhir", "Recent PHP sites", "Situs PHP pungkasan", "Situs PHP tarakhir" },
                new[] { "Situs yang kamu buka dari Phoron akan muncul di sini.",
                        "Sites you open from Phoron will show up here.",
                        "Situs sing kokbukak saka Phoron bakal katon ing kene.",
                        "Situs nang pian buka matan Phoron bakal muncul di sini." },
                new[] { "Semua situs", "All sites", "Kabeh situs", "Samunyaan situs" },

                // Menu baki
                new[] { "Jalankan {0}", "Run {0}", "Uripake {0}", "Jalanakan {0}" },
                new[] { "Ganti profil", "Switch profile", "Ganti profil", "Ganti profil" },
                new[] { "Buka folder proyek", "Open project folder", "Bukak folder proyek", "Buka folder proyek" },
            };
        }
    }
}
