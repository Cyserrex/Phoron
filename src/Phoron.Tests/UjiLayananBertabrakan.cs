using System;
using System.IO;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk perbaikan siklus hidup layanan di 1.34.0.
        ///
        /// Perilakunya sudah dibuktikan dengan Apache 2.4.38 dan MySQL 5.7.38
        /// yang sungguhan - dua start MySQL serentak, stop di tengah start
        /// Apache, penghentian MySQL yang rapi, StopAll, exe yang hilang. Kode
        /// lama gagal 13 dari 22 pemeriksaan itu; kode baru lulus semuanya.
        /// Menjalankan server sungguhan di setiap build terlalu lambat untuk
        /// harness ini, jadi yang dijaga di sini adalah kabel-kabelnya: pola yang
        /// kalau dicabut membuat bug yang sama kembali.
        /// </summary>
        static void UjiLayananBertabrakan()
        {
            Bagian("Start dan stop yang bertabrakan");

            var sm = BacaSumber("Phoron.Core", "ServiceManager.cs");
            if (sm == null) return;

            // Start yang masih berjalan dipakai ulang, bukan diulang.
            Ok("Start web lewat antrean giliran",
               Regex.IsMatch(sm, @"StartWebAsync\([^)]*\)\s*\{\s*return Antre\(ServiceKind\.Web"),
               "start kedua di tengah start pertama melahirkan httpd kedua");
            Ok("Start MySQL lewat antrean giliran",
               Regex.IsMatch(sm, @"StartDbAsync\([^)]*\)\s*\{\s*return Antre\(ServiceKind\.Db"),
               "dua mysqld; yang memegang 3306 tidak bisa dihentikan lagi");
            Ok("Setiap stop menaikkan giliran",
               Regex.Matches(sm, @"NaikkanGiliran\(ServiceKind\.(Web|Db)\)").Count >= 4,
               "stop yang tidak menaikkan giliran tidak membatalkan start yang sedang jalan");
            Ok("Galat di tengah start berakhir di Gagal, bukan tersangkut di Menyalakan",
               sm.Contains("tidak bisa dinyalakan: \" + ex.Message"), "");

            // MySQL tidak dibunuh paksa saat Phoron ditutup.
            var stopAll = Potong(sm, "public void StopAll()", "\n        }\n");
            Ok("StopAll mematikan MySQL dengan rapi",
               stopAll.Contains("MatikanMysqlRapi(d"), "mysqld dibunuh dengan taskkill /F setiap Phoron ditutup");
            Ok("StopAll hanya membunuh paksa web server dan php-cgi",
               Regex.IsMatch(stopAll, @"new\[\]\s*\{\s*w,\s*f\s*\}"), stopAll);
            Ok("StopAll mengabarkan layar lewat SetState",
               stopAll.Contains("SetState(ServiceKind.Db, ServiceState.Berhenti)"),
               "lampu tetap hijau untuk layanan yang sudah mati");

            // Rujukan dilepas SEBELUM mysqld diminta berhenti - kalau tidak,
            // setiap penghentian rapi dilaporkan "MySQL berhenti sendiri".
            var rapi = Potong(sm, "public async Task StopDbGracefullyAsync", "\n        }\n");
            int lepas = rapi.IndexOf("AmbilLepasDb()", StringComparison.Ordinal);
            int minta = rapi.IndexOf("MatikanMysqlRapi(", StringComparison.Ordinal);
            Ok("Penghentian rapi melepas rujukan sebelum meminta berhenti",
               lepas >= 0 && minta > lepas,
               "pengawas Exited melaporkan penghentian yang disengaja sebagai kegagalan");
            Ok("Penghentian rapi mencoba event MySQLShutdown<PID> lebih dulu",
               sm.Contains("\"MySQLShutdown\" + pid"),
               "hanya mysqladmin tanpa sandi - gagal begitu root diberi sandi");

            // Shutdown Windows: hanya saat sesi BENAR-BENAR berakhir.
            var mw = BacaSumber("Phoron.App", "MainWindow.xaml.cs");
            if (mw != null)
            {
                Ok("Layanan tidak dimatikan pada WM_QUERYENDSESSION",
                   !mw.Contains("msg == WM_QUERYENDSESSION"),
                   "shutdown yang dibatalkan meninggalkan layanan mati dengan lampu hijau");
                Ok("Layanan dimatikan hanya pada WM_ENDSESSION yang sungguhan",
                   mw.Contains("msg == WM_ENDSESSION && wParam != IntPtr.Zero"), "");
                Ok("Menu baki dinonaktifkan saat layanan sibuk",
                   mw.Contains("Items[0].Enabled = !busy"),
                   "menu baki jadi pintu belakang untuk start kedua");
            }

            Bagian("Halaman yang tidak menyimpan kerusakan");

            var prof = BacaSumber("Phoron.App", "Pages", "ProfilesPage.xaml.cs");
            if (prof != null)
            {
                // Dibuktikan dengan halaman Profil sungguhan: kode lama menyimpan
                // php= dan mysql= kosong begitu sebuah kotak teks ditinggalkan.
                Ok("Versi yang tidak ada di komputer ini tetap tersimpan",
                   Regex.IsMatch(prof, @"return row\.IdHilang \?\? """";"),
                   "profil dari komputer lain kehilangan versinya saat disimpan");
                Ok("Port HTTP dan HTTPS yang sama ditolak",
                   prof.Contains("if (p.HttpPort == p.HttpsPort)"), "");
            }

            var dash = BacaSumber("Phoron.App", "Pages", "DashboardPage.xaml.cs");
            if (dash != null)
            {
                Ok("Pemeriksaan port Beranda tidak di utas layar",
                   Regex.IsMatch(dash, @"await System\.Threading\.Tasks\.Task\.Run\(\(\) => new\s*\{\s*Bentrok"),
                   "netstat.exe dijalankan di utas layar pada setiap perubahan status");
                Ok("Port MySQL tidak diperiksa saat mysqld milik Phoron sendiri sedang jalan",
                   dash.Contains("periksaDb = _e.Services.DbState != ServiceState.Jalan"),
                   "\"port 3306 dipakai mysqld\" tentang mysqld milik sendiri");
            }

            var situs = BacaSumber("Phoron.App", "Pages", "SitesPage.xaml.cs");
            if (situs != null)
                Ok("Nama folder situs baru dikodekan sebelum masuk ke PHP",
                   situs.Contains("WebUtility.HtmlEncode(nama)"),
                   "folder bernama O'Neil menghasilkan index.php yang galat sintaks");

            UjiVersiKonfigurasi();
        }

        /// <summary>
        /// Penghitung versi konfigurasi - dasar spanduk "nyalakan ulang supaya
        /// perubahannya berlaku". Ini BISA diuji perilakunya tanpa server.
        /// </summary>
        static void UjiVersiKonfigurasi()
        {
            Bagian("Konfigurasi yang berubah di bawah server yang jalan");

            var dir = Path.Combine(Paths.Tmp, "versi-konfig");
            Directory.CreateDirectory(dir);
            try
            {
                var web0 = ConfigWriter.VersiKonfigWeb;
                var db0 = ConfigWriter.VersiKonfigDb;

                var conf = Path.Combine(dir, "httpd.conf");
                ConfigWriter.WriteIfChanged(conf, "Listen 80\n");
                Ok("Berkas web server yang berubah menaikkan versi web",
                   ConfigWriter.VersiKonfigWeb == web0 + 1, web0 + " -> " + ConfigWriter.VersiKonfigWeb);
                Ok("... dan TIDAK menaikkan versi MySQL", ConfigWriter.VersiKonfigDb == db0, "");

                ConfigWriter.WriteIfChanged(conf, "Listen 80\n");
                Ok("Isi yang sama tidak menaikkan apa pun",
                   ConfigWriter.VersiKonfigWeb == web0 + 1, "penulisan tanpa perubahan dianggap perubahan");

                ConfigWriter.WriteIfChanged(Path.Combine(dir, "my.ini"), "[mysqld]\nport=3306\n");
                Ok("my.ini yang berubah menaikkan versi MySQL",
                   ConfigWriter.VersiKonfigDb == db0 + 1, db0 + " -> " + ConfigWriter.VersiKonfigDb);
                Ok("... dan TIDAK menaikkan versi web",
                   ConfigWriter.VersiKonfigWeb == web0 + 1, "");

                // Beranda berupa PHP biasa: berlaku tanpa menyalakan ulang apa pun.
                Directory.CreateDirectory(Beranda.Folder);
                ConfigWriter.WriteIfChanged(Path.Combine(Beranda.Folder, "index.php"),
                                            "<?php echo 'uji " + Guid.NewGuid() + "';");
                Ok("Berkas beranda tidak menandai perlu restart",
                   ConfigWriter.VersiKonfigWeb == web0 + 1 && ConfigWriter.VersiKonfigDb == db0 + 1,
                   "perubahan PHP biasa memunculkan spanduk restart yang tidak perlu");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        static string BacaSumber(params string[] bagian)
        {
            var jalur = Path.Combine(AkarRepo(), "src");
            foreach (var b in bagian) jalur = Path.Combine(jalur, b);
            if (!File.Exists(jalur)) { Ok("Berkas sumber ada: " + jalur, false, ""); return null; }
            return File.ReadAllText(jalur).Replace("\r\n", "\n");
        }

        static string Potong(string isi, string awal, string akhir)
        {
            int i = isi.IndexOf(awal, StringComparison.Ordinal);
            if (i < 0) return "";
            int j = isi.IndexOf(akhir, i, StringComparison.Ordinal);
            return j < 0 ? isi.Substring(i) : isi.Substring(i, j - i + akhir.Length);
        }
    }
}
