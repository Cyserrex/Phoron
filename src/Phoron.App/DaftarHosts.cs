using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Menjalankan Phoron.exe --sinkron-hosts berhak Administrator, menunggu
    /// selesai, lalu melaporkan hasilnya.
    ///
    /// Yang naik hak akses cuma proses pembantu berumur sepersekian detik, BUKAN
    /// seluruh aplikasi. Jadi Apache dan MySQL yang sedang jalan tidak perlu
    /// dimatikan, jendela tidak perlu ditutup, dan sesudah UAC-nya selesai
    /// Phoron tetap jalan sebagai pengguna biasa seperti semula.
    /// </summary>
    public static class DaftarHosts
    {
        public static void Jalankan(Window induk)
        {
            List<string> nama;
            try { nama = HostsTool.SemuaNamaSitus(); }
            catch (Exception ex)
            {
                AppState.Info(Lang.T("Daftar situs tidak bisa dibaca: {0}", ex.Message));
                return;
            }

            if (nama.Count == 0)
            {
                AppState.Info(Lang.T("Belum ada situs yang perlu didaftarkan. Buat dulu proyek di folder www atau folder proyek Anda."));
                return;
            }

            var kurang = HostsTool.BelumTerdaftar();
            if (kurang.Count == 0)
            {
                AppState.Info(Lang.T("Semua {0} nama situs sudah terdaftar di berkas hosts. Tidak ada yang perlu diubah.",
                                     nama.Count));
                return;
            }

            var jawab = MessageBox.Show(induk,
                Lang.T("{0} nama situs akan ditulis ke berkas hosts Windows, {1} di antaranya belum terdaftar:",
                       nama.Count, kurang.Count)
                + "\n\n" + string.Join("\n", kurang.Take(12).ToArray())
                + (kurang.Count > 12 ? "\n... +" + (kurang.Count - 12) : "")
                + "\n\n" + Lang.T("Windows akan meminta izin Administrator sekali. Entri ini menetap, jadi sesudahnya Phoron boleh jalan tanpa Administrator dan nama .test tetap bisa dibuka.")
                + "\n\n" + Lang.T("Lanjutkan?"),
                "Phoron", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (jawab != MessageBoxResult.Yes) return;

            int kode;
            try
            {
                var psi = new ProcessStartInfo(Assembly.GetEntryAssembly().Location)
                {
                    Arguments = Program.ArgumenSinkronHosts,
                    UseShellExecute = true,
                    Verb = "runas",
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    kode = p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Pengguna menekan "No" di kotak UAC. Itu jawaban, bukan kesalahan.
                return;
            }
            catch (Exception ex)
            {
                AppState.Info(Lang.T("Gagal menjalankan pembantu: {0}", ex.Message));
                return;
            }

            if (kode == Program.KeluarBerhasil)
            {
                var sisa = HostsTool.BelumTerdaftar();
                if (sisa.Count == 0)
                    AppState.Info(Lang.T("Selesai. {0} nama situs terdaftar di berkas hosts. Phoron tidak perlu Administrator lagi untuk ini.",
                                         nama.Count));
                else
                    AppState.Info(Lang.T("Berkas hosts ditulis, tapi {0} nama masih belum terbaca: {1}",
                                         sisa.Count, string.Join(", ", sisa.Take(5).ToArray())));
            }
            else if (kode == Program.KeluarTidakBerhak)
                AppState.Info(Lang.T("Pembantu jalan tanpa hak Administrator, jadi berkas hosts tidak tersentuh."));
            else
                AppState.Info(Lang.T("Berkas hosts gagal ditulis. Berkasnya mungkin dikunci antivirus."));
        }
    }
}
