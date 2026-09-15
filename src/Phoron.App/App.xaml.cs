using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Penangkap galat terakhir.
    ///
    /// Tanpa ini, satu galat di luar dugaan membuat Phoron mati dengan kotak
    /// galat mentah Windows dan tidak meninggalkan jejak apa pun. Yang
    /// mengalaminya tidak punya keterangan untuk diceritakan, dan yang
    /// memperbaikinya tidak punya petunjuk untuk ditelusuri.
    ///
    /// Ketiga kait di bawah sengaja berperilaku berbeda, sebab ketiganya
    /// menandakan keadaan yang berbeda pula.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Penjaga masuk-ulang: penangan yang memanggil dirinya sendiri tidak berujung.</summary>
        static int _didalam;

        static int _sudahDimaafkan;
        static DateTime _hitungSejak = DateTime.MinValue;

        /// <summary>Sebanyak ini galat layar dimaafkan sebelum Phoron memilih tutup dengan rapi.</summary>
        const int MaafMaks = 3;
        const int JendelaDetik = 60;

        /// <summary>
        /// Dipasang sebagai baris PERTAMA di Program.Main, sebelum apa pun yang
        /// bisa gagal - termasuk sebelum Engine dibuat.
        /// </summary>
        public static void PasangPenangkapGalat()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                TanganiFatal(e.ExceptionObject as Exception);

            // Tugas yang galatnya tidak pernah ditunggu siapa pun. Bukan alasan
            // menutup aplikasi, tapi tetap harus tercatat - kalau tidak, ia
            // hilang tanpa bekas begitu pemulung memori lewat.
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved();
                try { Crash.Tulis(e.Exception, "tugas latar yang galatnya tidak ditunggu", Riwayat()); }
                catch { }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, a) => { a.Handled = TanganiLayar(a.Exception); };
            base.OnStartup(e);
        }

        /// <summary>
        /// Galat di utas layar. Phoron TETAP JALAN secara baku.
        ///
        /// Alasannya: Phoron memegang Apache dan MySQL yang sedang hidup, dan
        /// ProcessJob mengikat keduanya pada proses ini - jadi mati karena satu
        /// galat kecil di sebuah halaman berarti ikut mematikan tumpukan kerja
        /// orang. Justru itulah yang sebisa mungkin dihindari.
        /// </summary>
        static bool TanganiLayar(Exception ex)
        {
            // Penangan yang sedang berjalan tidak boleh dipanggil lagi dari
            // dalam dirinya sendiri.
            if (Interlocked.CompareExchange(ref _didalam, 1, 0) != 0) return false;
            try
            {
                var jalur = TulisAman(ex, "galat di antarmuka");
                Catat("Galat tidak terduga: " + Pesan(ex)
                      + (jalur != null ? " (rincian di " + jalur + ")" : ""));

                if (TerlaluSering())
                {
                    Tampilkan("Phoron berkali-kali menemui galat dan akan ditutup supaya tidak "
                              + "menimbulkan kerusakan lain." + Dua() + Pesan(ex)
                              + (jalur != null ? Dua() + "Rincian: " + jalur : ""),
                              MessageBoxButton.OK);
                    TutupDenganRapi();
                    return true;
                }

                var jawab = Tampilkan(
                    "Ada yang tidak beres di dalam Phoron." + Dua() + Pesan(ex) + Dua()
                    + (jalur != null ? "Rincian tersimpan di:" + Environment.NewLine + jalur + Dua() : "")
                    + "Layanan yang sedang berjalan tidak terganggu. Lanjutkan memakai Phoron?",
                    MessageBoxButton.YesNo);

                if (jawab == MessageBoxResult.No) TutupDenganRapi();
                return true;
            }
            catch { return true; }   // ditangani seadanya; yang penting tidak mati
            finally { Interlocked.Exchange(ref _didalam, 0); }
        }

        /// <summary>
        /// Galat di utas lain. CLR tetap mengakhiri proses apa pun yang kita
        /// lakukan, jadi yang bisa diperbuat hanya mencatat dan berusaha
        /// menghentikan layanan dengan rapi.
        /// </summary>
        static void TanganiFatal(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _didalam, 1, 0) != 0) return;
            try
            {
                var jalur = TulisAman(ex, "galat fatal di luar utas layar");
                Tampilkan("Phoron harus ditutup karena galat yang tidak bisa dipulihkan."
                          + Dua() + Pesan(ex)
                          + (jalur != null ? Dua() + "Rincian: " + jalur : ""),
                          MessageBoxButton.OK);
                HentikanLayanan();
            }
            catch { }
            finally { Interlocked.Exchange(ref _didalam, 0); }
        }

        /// <summary>
        /// Antarmuka yang gagal berulang kali sudah rusak, dan meneruskannya cuma
        /// memberondong pengguna dengan kotak pesan yang sama.
        /// </summary>
        static bool TerlaluSering()
        {
            var sekarang = DateTime.Now;
            if (_hitungSejak == DateTime.MinValue
                || (sekarang - _hitungSejak).TotalSeconds > JendelaDetik)
            {
                _hitungSejak = sekarang;
                _sudahDimaafkan = 0;
            }
            return ++_sudahDimaafkan > MaafMaks;
        }

        static void TutupDenganRapi()
        {
            HentikanLayanan();
            // Bukan Application.Shutdown(): yang rusak bisa jadi justru
            // dispatcher-nya, dan permintaan tutup yang sopan tidak akan pernah
            // sampai.
            try { Environment.Exit(3); } catch { }
        }

        static void HentikanLayanan()
        {
            try
            {
                var e = AppState.Engine;
                if (e == null) return;
                try { e.Services.StopAll(); } catch { }
                try { e.Node.StopAll(); } catch { }
            }
            catch { }
        }

        // ------------------------------------------------------------- Bantuan
        // Tiap langkah di dalam penangan dibungkus sendiri-sendiri. Penangan yang
        // ikut meledak adalah cara paling umum membuat lingkaran tak berujung.

        static string TulisAman(Exception ex, string konteks)
        {
            try { return Crash.Tulis(ex, konteks, Riwayat()); }
            catch { return null; }
        }

        static System.Collections.Generic.List<BarisLog> Riwayat()
        {
            try
            {
                var e = AppState.Engine;
                return e != null ? e.Riwayat() : null;
            }
            catch { return null; }
        }

        static void Catat(string teks)
        {
            try
            {
                var e = AppState.Engine;
                if (e != null) e.Say(teks);
            }
            catch { }
        }

        static MessageBoxResult Tampilkan(string teks, MessageBoxButton tombol)
        {
            try
            {
                return MessageBox.Show(teks, "Phoron", tombol,
                    tombol == MessageBoxButton.YesNo ? MessageBoxImage.Warning : MessageBoxImage.Error);
            }
            catch { return MessageBoxResult.None; }
        }

        static string Pesan(Exception ex)
        {
            if (ex == null) return "(galat tanpa keterangan)";
            var dalam = ex;
            while (dalam.InnerException != null) dalam = dalam.InnerException;
            return dalam.Message;
        }

        static string Dua() { return Environment.NewLine + Environment.NewLine; }
    }
}
