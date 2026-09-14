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

        static async void Unduh(Engine e, HasilCek hasil)
        {
            if (string.IsNullOrEmpty(hasil.UrlInstaller))
            {
                AppState.Warn("Rilis itu tidak menyertakan berkas installer. "
                              + "Halaman rilisnya akan dibuka di browser.");
                Shell.Open(hasil.UrlHalaman);
                return;
            }

            e.Say("Mengunduh Phoron " + hasil.Versi + "...");
            string galat = null;
            var kemajuan = new Progress<int>(v =>
            {
                // Dilaporkan tiap 10% saja: tiap potongan 80 KB akan membanjiri
                // kotak Aktivitas dengan ratusan baris tanpa guna.
                if (v % 10 == 0) e.Say("Mengunduh pembaruan... " + v + "%");
            });
            var berkas = await Updater.UnduhInstallerAsync(hasil, kemajuan, g => galat = g);
            if (berkas == null)
            {
                AppState.Warn(galat ?? "Gagal mengunduh pembaruan.");
                return;
            }

            e.Say("Installer tersimpan di " + berkas + ".");
            if (!AppState.Ask("Installer sudah diunduh.\n\nJalankan sekarang?\n\n"
                              + "Phoron akan ditutup oleh pemasangnya. Proyek, basis data, "
                              + "dan profil Anda tidak disentuh."))
            {
                Shell.Open(Paths.Tmp);
                return;
            }

            try
            {
                // Layanan dimatikan lebih dulu: pemasang akan menutup Phoron,
                // dan proses anak yang ditinggalkan hidup akan tetap memegang
                // port 80 setelah aplikasinya tiada.
                e.Services.StopAll();
                e.Node.StopAll();
                Process.Start(new ProcessStartInfo(berkas) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppState.Warn("Tidak bisa menjalankan installer: " + ex.Message
                              + "\n\nBerkasnya ada di " + berkas + ".");
            }
        }
    }
}
