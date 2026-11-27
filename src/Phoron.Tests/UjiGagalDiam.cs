using System;
using System.IO;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Kegagalan yang tidak bersuara: tidak ada galat, tidak ada pesan,
        /// hanya akibat yang menumpuk sampai seseorang menyadarinya.
        ///
        /// - Halaman yang berlangganan di konstruktor bocor bila tidak pernah
        ///   Loaded (jendela dibuat ulang dari baki ke tab lain).
        /// - Sandi DB tersandi tertimpa kosong setelah satu kali DPAPI gagal.
        /// - Init MySQL yang terputus dianggap berhasil karena mysql\ sudah ada.
        /// - mysqld --console mengabaikan log-error, jadi mysql-error.log kosong.
        /// </summary>
        static void UjiGagalDiam(string sandbox)
        {
            Bagian("Kegagalan yang diam");

            // ------------------------------------------- Langganan halaman
            foreach (var halaman in new[] { "DashboardPage.xaml.cs", "DatabasePage.xaml.cs", "NodePage.xaml.cs" })
            {
                var isi = BacaSumber("Phoron.App", "Pages", halaman);
                if (isi == null) continue;
                var ktor = Potong(isi, "public " + halaman.Replace(".xaml.cs", "") + "()", "\n        }\n");
                // Di konstruktor, setiap "+=" ke peristiwa milik objek berumur
                // panjang harus berada di dalam penangan Loaded.
                var tanpaLoaded = Regex.Replace(ktor, @"Loaded \+= \(s, \w+\) =>\s*\{.*?\n            \};",
                                                "", RegexOptions.Singleline);
                Ok(halaman + ": tidak berlangganan peristiwa luar langsung di konstruktor",
                   ktor.Length > 0 && !Regex.IsMatch(tanpaLoaded, @"(_e\.|AppState\.)[\w.]+ \+= "),
                   "langganan di konstruktor tidak dilepas bila halaman tidak pernah Loaded");
            }

            // ------------------------------------------- Sandi tetap selamat
            var tersandi = Rahasia.Tutup("rahasia");
            Ok("Sandi yang tidak diubah ditulis ulang apa adanya",
               Rahasia.TutupUntukSimpan("rahasia", tersandi, "rahasia") == tersandi);
            Ok("Sandi yang gagal dibuka tidak tertimpa kosong",
               Rahasia.TutupUntukSimpan("", "dari-mesin-lain==", "") == "dari-mesin-lain==",
               "satu kali DPAPI gagal menghapus sandi selamanya");
            var baru = Rahasia.TutupUntukSimpan("sandi-baru", tersandi, "rahasia");
            Ok("Sandi yang diganti disandi ulang",
               baru != tersandi && Rahasia.Buka(baru) == "sandi-baru");
            Ok("Sandi yang sengaja dikosongkan benar-benar kosong",
               Rahasia.TutupUntukSimpan("", tersandi, "rahasia") == "");
            Ok("Belum pernah ada sandi tetap kosong",
               Rahasia.TutupUntukSimpan("", "", "") == "");

            // ------------------------------------------- Init MySQL
            var data = Path.Combine(sandbox, "init-mysql");
            Directory.CreateDirectory(Path.Combine(data, "mysql"));
            var habis = new Shell.RunResult { ExitCode = -1, TimedOut = true };
            Ok("Init yang melewati batas waktu gagal walau mysql\\ ada",
               !ServiceManager.InitBerhasil(habis, data));
            Ok("Init dibatalkan gagal",
               !ServiceManager.InitBerhasil(new Shell.RunResult { Dibatalkan = true }, data));
            Ok("Kode keluar bukan 0 tanpa tabel sistem gagal",
               !ServiceManager.InitBerhasil(new Shell.RunResult { ExitCode = 1 }, data));
            Ok("Kode keluar 0 berhasil",
               ServiceManager.InitBerhasil(new Shell.RunResult { ExitCode = 0 }, data));

            File.WriteAllText(Path.Combine(data, "mysql.ibd"), "");
            Ok("Kode keluar bukan 0 dimaafkan bila tabel sistem MySQL 8 ada",
               ServiceManager.InitBerhasil(new Shell.RunResult { ExitCode = 1 }, data));
            File.Delete(Path.Combine(data, "mysql.ibd"));
            File.WriteAllText(Path.Combine(data, "mysql", "global_priv.MAD"), "");
            Ok("Tabel sistem MariaDB 10.4+ dikenali", ServiceManager.TabelSistemAda(data));
            File.Delete(Path.Combine(data, "mysql", "global_priv.MAD"));
            File.WriteAllText(Path.Combine(data, "mysql", "user.frm"), "");
            Ok("Tabel sistem MySQL 5.x dikenali", ServiceManager.TabelSistemAda(data));

            // ------------------------------------------- mysql-error.log
            var sm = BacaSumber("Phoron.Core", "ServiceManager.cs");
            if (sm != null)
                Ok("Keluaran mysqld disalin ke mysql-error.log",
                   Regex.IsMatch(sm, @"Spawn\(mysqld,[^;]*MysqlErrorLog\);"),
                   "--console mengabaikan log-error; tanpa salinan berkasnya tidak pernah ada");
        }
    }
}
