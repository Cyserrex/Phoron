using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace Phoron.Tests
{
    /// <summary>
    /// Uji asap antarmuka: Phoron.exe sungguhan, dikendalikan lewat UI Automation.
    ///
    /// Uji di harness utama sebagian besar membaca sumber ("kabelnya masih
    /// terpasang"), dan itu tidak menangkap tombol yang terpasang tapi tidak
    /// berbuat apa-apa - seperti menu "..." yang sempat tidak pernah terbuka.
    /// Yang ini menekan tombolnya betulan: panel ringkas, laci, pindah ke
    /// jendela lengkap, kembali, lalu keluar dengan rapi.
    ///
    /// Sengaja TIDAK ikut uji biasa maupun CI: butuh desktop sungguhan, dan
    /// tidak bisa jalan selama Phoron lain terbuka (mutex instans tunggal).
    /// Jalankan dengan: build.bat ui
    /// </summary>
    internal static class UjiAsapUi
    {
        static int _lulus, _gagal;

        static void Ok(string nama, bool syarat, string ket = "")
        {
            if (syarat) { _lulus++; Console.WriteLine("  ok    " + nama); }
            else { _gagal++; Console.WriteLine("  GAGAL " + nama + (ket.Length > 0 ? " - " + ket : "")); }
        }

        public static int Jalankan()
        {
            Console.WriteLine("-- Uji asap antarmuka (UI Automation)");
            if (Process.GetProcessesByName("Phoron").Length > 0)
            {
                Console.WriteLine("  dilewati: Phoron lain sedang berjalan - tutup dulu (mutex instans tunggal).");
                return 0;
            }

            var exe = CariExe();
            if (exe == null) { Console.WriteLine("  GAGAL Phoron.exe belum dibangun"); return 1; }

            // Sandbox: bahasa Indonesia supaya nama tombol bisa dicari apa
            // adanya; folder bin yang dipindai dibiarkan bawaan.
            var akar = Path.Combine(Path.GetTempPath(), "phoron-asap-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(akar);
            File.WriteAllText(Path.Combine(akar, "phoron.ini"),
                "[umum]\r\nbahasa=id\r\nauto_start=0\r\nminimize_ke_tray=1\r\ncek_pembaruan=0\r\n");

            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.EnvironmentVariables["PHORON_HOME"] = akar;
            Process p = null;
            try
            {
                p = Process.Start(psi);

                var panel = TungguJendela(p.Id, "BtnLaci", 30);
                Ok("Phoron terbuka sebagai panel ringkas", panel != null);
                if (panel == null) return Selesai(p, akar);

                Tekan(panel, "BtnLaci");
                var tombolSitus = Tunggu(() => CariNama(panel, "Situs"), 5);
                Ok("Laci terbuka dan menampilkan tombol halaman", tombolSitus != null);
                if (tombolSitus == null) return Selesai(p, akar);

                Invoke(tombolSitus);
                var lengkap = TungguJendela(p.Id, "Nav", 15);
                Ok("Tombol Situs membuka jendela lengkap", lengkap != null);
                Ok("Panel ringkas tertutup sesudahnya", Tunggu(() => CariId(p.Id, "BtnLaci") == null ? "ya" : null, 5) != null);
                if (lengkap == null) return Selesai(p, akar);

                Tekan(lengkap, "BtnRingkas");
                Ok("Tombol tampilan ringkas kembali ke panel", TungguJendela(p.Id, "BtnLaci", 15) != null);
                Ok("Jendela lengkap tertutup sesudahnya", Tunggu(() => CariId(p.Id, "Nav") == null ? "ya" : null, 5) != null);
                Ok("Phoron tetap berjalan selama berpindah jendela", !p.HasExited);
            }
            catch (Exception ex)
            {
                Ok("Uji asap selesai tanpa pengecualian", false, ex.Message);
            }
            return Selesai(p, akar);
        }

        static int Selesai(Process p, string akar)
        {
            if (p != null && !p.HasExited)
            {
                // Jalur keluar yang sama dengan pemasang: sinyal bernama, bukan bunuh.
                EventWaitHandle h;
                if (EventWaitHandle.TryOpenExisting("Phoron.KeluarSekarang", out h)) { h.Set(); h.Dispose(); }
                bool keluar = p.WaitForExit(15000);
                Ok("Phoron keluar dengan rapi lewat sinyalnya", keluar);
                if (!keluar) try { p.Kill(); } catch { }
            }
            try { Directory.Delete(akar, true); } catch { }
            Console.WriteLine();
            Console.WriteLine("=== " + _lulus + " lulus, " + _gagal + " gagal ===");
            return _gagal == 0 ? 0 : 1;
        }

        static string CariExe()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Phoron.App"))) dir = dir.Parent;
            if (dir == null) return null;
            foreach (var konf in new[] { "Debug", "Release" })
            {
                var f = Path.Combine(dir.FullName, "src", "Phoron.App", "bin", konf, "net48", "Phoron.exe");
                if (File.Exists(f)) return f;
            }
            return null;
        }

        static AutomationElement Jendela(int pid, string idDiDalam)
        {
            var semua = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
            foreach (AutomationElement w in semua)
                if (w.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, idDiDalam)) != null) return w;
            return null;
        }

        static AutomationElement CariId(int pid, string id) { return Jendela(pid, id); }

        static AutomationElement TungguJendela(int pid, string idDiDalam, int detik)
        {
            return Tunggu(() => Jendela(pid, idDiDalam), detik);
        }

        static AutomationElement CariNama(AutomationElement akar, string nama)
        {
            return akar.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, nama),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
        }

        static T Tunggu<T>(Func<T> cari, int detik) where T : class
        {
            var batas = DateTime.UtcNow.AddSeconds(detik);
            while (DateTime.UtcNow < batas)
            {
                try { var h = cari(); if (h != null) return h; } catch (ElementNotAvailableException) { }
                Thread.Sleep(250);
            }
            return null;
        }

        static void Tekan(AutomationElement akar, string id)
        {
            var b = akar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
            if (b == null) throw new InvalidOperationException("Tombol tidak ditemukan: " + id);
            Invoke(b);
        }

        static void Invoke(AutomationElement b)
        {
            ((InvokePattern)b.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
    }
}
