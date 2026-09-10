using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Phoron.Core
{
    /// <summary>
    /// Menyalakan Phoron sendiri saat Windows dinyalakan, lewat kunci Run milik
    /// pengguna (HKCU). Sengaja BUKAN Windows Service dan bukan Scheduled Task:
    /// keduanya butuh hak admin untuk dipasang, sedangkan kunci Run bisa ditulis
    /// pengguna biasa - dan Phoron memang dirancang jalan tanpa admin.
    /// </summary>
    public static class Autostart
    {
        const string KunciRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Nama nilai registri. Sama persis dengan yang ditulis installer, supaya keduanya tidak saling menggandakan entri.</summary>
        public const string NamaNilai = "Phoron";

        /// <summary>
        /// Argumen yang dipakai saat dijalankan Windows. Aplikasi mulai langsung
        /// mengecil ke baki sistem - jendela yang menyembul di setiap kali boot
        /// adalah alasan orang mematikan fitur semacam ini.
        /// </summary>
        public const string ArgumenTray = "--tray";

        public static string ExePath
        {
            get
            {
                try
                {
                    using (var p = Process.GetCurrentProcess())
                        return p.MainModule.FileName;
                }
                catch
                {
                    return System.Reflection.Assembly.GetEntryAssembly().Location;
                }
            }
        }

        static string NilaiSeharusnya()
        {
            return "\"" + ExePath + "\" " + ArgumenTray;
        }

        /// <summary>Terdaftar untuk exe INI. Entri milik salinan Phoron di folder lain dianggap bukan milik kita.</summary>
        public static bool Aktif
        {
            get
            {
                var nilai = NilaiTerdaftar();
                if (nilai == null) return false;
                return MenunjukKe(nilai, ExePath);
            }
        }

        /// <summary>Ada entri, tapi menunjuk exe Phoron di lokasi lain - mis. sisa pemasangan lama.</summary>
        public static string EntriAsing()
        {
            var nilai = NilaiTerdaftar();
            if (nilai == null || MenunjukKe(nilai, ExePath)) return null;
            return nilai;
        }

        static string NilaiTerdaftar()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KunciRun, false))
                    return key == null ? null : key.GetValue(NamaNilai) as string;
            }
            catch { return null; }
        }

        /// <summary>
        /// Apakah sebuah nilai registri Run menunjuk ke exe tertentu. Publik supaya
        /// bisa diuji tanpa menyentuh registri mesin yang sedang dipakai.
        /// </summary>
        public static bool MenunjukKe(string nilai, string exe)
        {
            // Nilai di registri berbentuk "C:\...\Phoron.exe" --tray, jadi jalurnya
            // harus dikupas dari tanda kutip dan argumen sebelum dibandingkan.
            var jalur = (nilai ?? "").Trim();
            if (jalur.StartsWith("\""))
            {
                int tutup = jalur.IndexOf('"', 1);
                jalur = tutup > 0 ? jalur.Substring(1, tutup - 1) : jalur.Trim('"');
            }
            else
            {
                int spasi = jalur.IndexOf(' ');
                if (spasi > 0) jalur = jalur.Substring(0, spasi);
            }
            try
            {
                return string.Equals(Path.GetFullPath(jalur), Path.GetFullPath(exe),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Pasang atau cabut entri. Mengembalikan pesan kesalahan, atau null bila berhasil.</summary>
        public static string Set(bool aktif)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KunciRun))
                {
                    if (key == null) return "Kunci registri Run tidak bisa dibuka.";
                    if (aktif) key.SetValue(NamaNilai, NilaiSeharusnya(), RegistryValueKind.String);
                    else key.DeleteValue(NamaNilai, false);
                }
                return null;
            }
            catch (Exception ex) { return "Gagal menulis registri: " + ex.Message; }
        }
    }
}
