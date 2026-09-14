using System;
using System.Diagnostics;
using System.Windows;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Menawarkan pembaruan ke pengguna. Dipakai bersama oleh Beranda dan
    /// halaman Pengaturan supaya kalimat dan alurnya tidak berbeda di dua tempat.
    /// </summary>
    internal static class UpdateDialog
    {
        public static void Tawarkan(Engine e, HasilCek hasil)
        {
            if (hasil == null) { AppState.Info("Belum ada hasil pengecekan."); return; }
            if (hasil.Galat != null) { AppState.Warn(hasil.Galat); return; }
            if (!hasil.LebihBaru)
            {
                AppState.Info("Phoron " + AppInfo.Version + " sudah versi terbaru.");
                return;
            }

            var catatan = hasil.Catatan ?? "";
            // Catatan rilis bisa panjang sekali; kotak pesan Windows tidak bisa
            // digulir, jadi dipotong daripada menghasilkan kotak setinggi layar
            // yang ujungnya tidak terbaca.
            if (catatan.Length > 700) catatan = catatan.Substring(0, 700).TrimEnd() + "\n...";

            var pesan = "Phoron " + hasil.Versi + " sudah rilis.\n"
                      + "Yang terpasang sekarang: " + AppInfo.Version + ".\n\n"
                      + (catatan.Length > 0 ? catatan + "\n\n" : "")
                      + "Unduh installer-nya sekarang?\n\n"
                      + "[Ya] unduh lalu jalankan pemasangnya\n"
                      + "[Tidak] buka halaman rilis di browser\n"
                      + "[Batal] tidak sekarang";

            var jawab = MessageBox.Show(pesan, "Pembaruan tersedia",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (jawab == MessageBoxResult.Cancel) return;
            if (jawab == MessageBoxResult.No)
            {
                Shell.Open(string.IsNullOrEmpty(hasil.UrlHalaman) ? Updater.HalamanRilis : hasil.UrlHalaman);
                return;
            }
            Unduh(e, hasil);
        }

        static void Unduh(Engine e, HasilCek hasil)
        {
            if (string.IsNullOrEmpty(hasil.UrlInstaller))
            {
                AppState.Warn("Rilis itu tidak menyertakan berkas installer. "
                              + "Halaman rilisnya akan dibuka di browser.");
                Shell.Open(hasil.UrlHalaman);
                return;
            }

            e.Say("Mengunduh Phoron " + hasil.Versi + "...");
            var jendela = new DownloadWindow(hasil);
            var pemilik = Application.Current != null ? Application.Current.MainWindow : null;
            if (pemilik != null && pemilik.IsVisible) jendela.Owner = pemilik;

            var selesai = jendela.ShowDialog();
            if (selesai != true)
            {
                if (jendela.Galat != null) { e.Say(jendela.Galat); AppState.Warn(jendela.Galat); }
                return;
            }

            var berkas = jendela.Berkas;
            e.Say("Installer tersimpan di " + berkas + ".");

            if (!AppState.Ask("Installer sudah diunduh.\n\nJalankan sekarang?\n\n"
                              + "Phoron akan ditutup lebih dulu supaya Apache dan MySQL berhenti "
                              + "dengan rapi. Proyek, basis data, dan profil Anda tidak disentuh."))
            {
                Shell.Open(Paths.Tmp);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(berkas) { UseShellExecute = true });
                // Phoron ditutup SENDIRI lewat jalur penutupan normal, tidak
                // menunggu pemasang memaksanya. Jalur normal itulah yang meminta
                // mysqladmin shutdown; dihentikan paksa, InnoDB harus memulihkan
                // diri saat start berikutnya.
                var utama = Application.Current.MainWindow as MainWindow;
                if (utama != null) utama.TutupUntukPembaruan();
                else { e.Services.StopAll(); e.Node.StopAll(); Application.Current.Shutdown(); }
            }
            catch (Exception ex)
            {
                AppState.Warn("Tidak bisa menjalankan installer: " + ex.Message
                              + "\n\nBerkasnya ada di " + berkas + ".");
            }
        }
    }
}
