using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Phoron.Tests
{
    /// <summary>
    /// Penjaga untuk objek bernama yang dipakai Phoron menyapa dirinya sendiri
    /// antar proses - penjaga instans tunggal dan permintaan "tampilkan
    /// jendelamu" yang dikirim saat ikon taskbar ditekan.
    ///
    /// Yang dijaga di sini satu hal: objek-objek itu harus tetap bisa dibuka
    /// oleh proses yang HAKNYA LEBIH RENDAH. Phoron menawarkan "jalankan ulang
    /// sebagai Administrator", dan sesudah itu klik pada ikon taskbar tetap
    /// melahirkan proses berhak biasa. Dengan `new Mutex(nama)` polos, proses
    /// itu tidak bisa menyapa siapa-siapa: jendelanya tidak muncul, dan
    /// penggunanya malah ditegur kotak galat.
    ///
    /// Uji ini tidak membaca kode, melainkan menjalankan anak berintegritas Low
    /// yang sungguhan. Jarak tingkat integritas antara anak itu dan objek buatan
    /// harness sama persis dengan jarak antara proses biasa dan objek buatan
    /// Phoron yang berhak Administrator - dan menurunkan hak diri sendiri tidak
    /// membutuhkan izin siapa pun, jadi tidak ada kotak UAC yang menyela.
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// Argumen internal: harness memanggil DIRINYA SENDIRI dengan hak Low
        /// untuk mencoba membuka event bernama. Hasilnya dilaporkan lewat kode
        /// keluar, sebab proses berhak Low nyaris tidak punya tempat menulis.
        /// </summary>
        public const string ArgumenBukaEvent = "--coba-buka-event";

        /// <summary>bit 1 = nama pertama terbuka, bit 2 = nama kedua terbuka.</summary>
        internal static int CobaBukaEvent(string[] args)
        {
            int hasil = 0;
            for (int i = 1; i < args.Length && i <= 2; i++)
            {
                try
                {
                    EventWaitHandle e;
                    if (EventWaitHandle.TryOpenExisting(args[i], out e))
                    {
                        e.Close();
                        hasil |= 1 << (i - 1);
                    }
                }
                catch { /* ditolak - biarkan bitnya nol */ }
            }
            return hasil;
        }

        static void UjiObjekAntarProses()
        {
            Bagian("Objek bernama antar proses");

            var exe = CariPhoronExe();
            if (exe == null) { Console.WriteLine("     dilewati: Phoron.exe belum dibangun"); return; }

            string kenapa;
            var jenis = MuatObjekAntarProses(exe, out kenapa);
            if (jenis == null)
            {
                Ok("Kelas ObjekAntarProses ada di Phoron.exe", false,
                   "tidak ketemu (" + kenapa + ") - penjaga instans tunggal mungkin "
                   + "kembali memakai new Mutex() polos");
                return;
            }
            Ok("Kelas ObjekAntarProses ada di Phoron.exe", true, "");

            var sufiks = Guid.NewGuid().ToString("N").Substring(0, 8);
            var namaLama = "PhoronUji.Lama." + sufiks;
            var namaBaru = "PhoronUji.Baru." + sufiks;

            EventWaitHandle lama = null, baru = null;
            try
            {
                bool dibuat;
                lama = new EventWaitHandle(false, EventResetMode.AutoReset, namaLama, out dibuat);
                Ok("Pembanding cara lama terbuat", dibuat, "");

                var metode = jenis.GetMethod("BuatEvent",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (metode == null)
                {
                    Ok("ObjekAntarProses.BuatEvent ada", false, "metodenya hilang");
                    return;
                }
                var param = new object[] { namaBaru, EventResetMode.AutoReset, false };
                baru = (EventWaitHandle)metode.Invoke(null, param);
                Ok("ObjekAntarProses.BuatEvent membuat objek baru", (bool)param[2], "");

                // Label integritas objeknya diperiksa langsung, bukan disimpulkan
                // dari kode: inilah satu-satunya yang membuka jalan ke atas.
                var sddlBaru = Sddl(baru.SafeWaitHandle.DangerousGetHandle());
                var sddlLama = Sddl(lama.SafeWaitHandle.DangerousGetHandle());
                Ok("Objek Phoron berlabel integritas Low",
                   sddlBaru != null && sddlBaru.Contains("(ML;;NW;;;LW)"), sddlBaru ?? "(tak terbaca)");
                Ok("Pembanding cara lama TIDAK berlabel Low",
                   sddlLama != null && !sddlLama.Contains("(ML;;NW;;;LW)"), sddlLama ?? "(tak terbaca)");

                // Izinnya ditulis atas nama SID pengguna, bukan dibiarkan mengikuti
                // DACL bawaan token: token Administrator yang sudah dinaikkan
                // memberi kuasa kepada grup Administrators, yang di proses berhak
                // biasa hanya dipasang untuk penolakan.
                string sidAku;
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    sidAku = id.User.Value;
                Ok("Objek Phoron memberi kuasa kepada SID pengguna",
                   sddlBaru != null && sddlBaru.Contains(sidAku), sddlBaru ?? "(tak terbaca)");

                // Dan yang paling menentukan: apakah anak berhak rendah benar-benar
                // bisa membukanya.
                int kode;
                if (!JalankanDiriBerhakRendah(namaLama, namaBaru, out kode))
                {
                    Console.WriteLine("     dilewati: proses berintegritas Low tidak bisa dijalankan di sini");
                    return;
                }
                Ok("Cara lama tertutup bagi proses berhak lebih rendah",
                   (kode & 1) == 0, "ternyata terbuka - pembandingnya tidak membuktikan apa pun");
                Ok("Objek Phoron terbuka bagi proses berhak lebih rendah",
                   (kode & 2) != 0, "masih tertutup - klik taskbar akan berakhir di kotak pesan");
            }
            finally
            {
                if (lama != null) try { lama.Close(); } catch { }
                if (baru != null) try { baru.Close(); } catch { }
            }
        }

        static Type MuatObjekAntarProses(string exe, out string kenapa)
        {
            kenapa = "";
            try
            {
                // Phoron.Core dimuat lebih dulu dari folder yang sama: Phoron.exe
                // menanamnya sebagai sumber daya terkompresi, dan pemuat biasa
                // tidak tahu cara membukanya.
                var folder = Path.GetDirectoryName(exe);
                var core = Path.Combine(folder, "Phoron.Core.dll");
                if (File.Exists(core)) Assembly.LoadFrom(core);
                var t = Assembly.LoadFrom(exe).GetType("Phoron.App.ObjekAntarProses");
                if (t == null) kenapa = "kelasnya tidak ada di dalam rakitan";
                return t;
            }
            catch (Exception ex) { kenapa = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        // ------------------------------------------------------- Baca deskriptor

        const int SE_KERNEL_OBJECT = 6;
        const int DACL_SECURITY_INFORMATION = 0x00000004;
        const int LABEL_SECURITY_INFORMATION = 0x00000010;

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint GetSecurityInfo(IntPtr objek, int jenis, int bagian,
            IntPtr pemilik, IntPtr grup, out IntPtr dacl, out IntPtr sacl, out IntPtr psd);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
            IntPtr psd, int revisi, int bagian, out IntPtr sddl, out int panjang);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr p);

        static string Sddl(IntPtr pegangan)
        {
            IntPtr dacl, sacl, psd, teks;
            int panjang;
            const int bagian = DACL_SECURITY_INFORMATION | LABEL_SECURITY_INFORMATION;
            if (GetSecurityInfo(pegangan, SE_KERNEL_OBJECT, bagian,
                    IntPtr.Zero, IntPtr.Zero, out dacl, out sacl, out psd) != 0) return null;
            try
            {
                if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(psd, 1, bagian,
                        out teks, out panjang)) return null;
                try { return Marshal.PtrToStringUni(teks); }
                finally { LocalFree(teks); }
            }
            finally { LocalFree(psd); }
        }

        // ------------------------------------------- Anak berintegritas Low

        [StructLayout(LayoutKind.Sequential)]
        struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }

        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb;
            public string r1, desktop, title;
            public int x, y, xs, ys, xc, yc, fill, flags;
            public short showWindow, r2;
            public IntPtr r3, hStdIn, hStdOut, hStdErr;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int pid, tid; }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr proses, uint akses, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool DuplicateTokenEx(IntPtr token, uint akses, IntPtr sa,
            int peniruan, int jenis, out IntPtr salinan);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertStringSidToSidW(string sddl, out IntPtr sid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool SetTokenInformation(IntPtr token, int kelas,
            ref TOKEN_MANDATORY_LABEL info, int panjang);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcessAsUserW(IntPtr token, string app, string perintah,
            IntPtr pa, IntPtr ta, bool warisi, uint bendera, IntPtr env, string folder,
            ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr pegangan, uint milidetik);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeProcess(IntPtr pegangan, out uint kode);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr pegangan);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        const int TokenIntegrityLevel = 25;
        const uint SE_GROUP_INTEGRITY = 0x00000020;
        const string SidLow = "S-1-16-4096";

        /// <summary>
        /// Menjalankan harness ini lagi dengan integritas Low, memintanya membuka
        /// kedua event, lalu mengembalikan kode keluarnya. false berarti prosesnya
        /// tidak bisa dijalankan sama sekali - di runner CI hal itu bisa terjadi
        /// karena sesi dan tokennya lain, dan itu bukan kegagalan Phoron.
        /// </summary>
        static bool JalankanDiriBerhakRendah(string nama1, string nama2, out int kode)
        {
            kode = 0;
            IntPtr token = IntPtr.Zero, salinan = IntPtr.Zero, sid = IntPtr.Zero;
            try
            {
                const uint TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, MAXIMUM_ALLOWED = 0x02000000;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out token)) return false;
                if (!DuplicateTokenEx(token, MAXIMUM_ALLOWED, IntPtr.Zero, 2, 1, out salinan)) return false;
                if (!ConvertStringSidToSidW(SidLow, out sid)) return false;

                var label = new TOKEN_MANDATORY_LABEL();
                label.Label.Sid = sid;
                label.Label.Attributes = SE_GROUP_INTEGRITY;
                if (!SetTokenInformation(salinan, TokenIntegrityLevel, ref label,
                        Marshal.SizeOf(typeof(TOKEN_MANDATORY_LABEL)) + 12)) return false;

                var diri = Assembly.GetEntryAssembly().Location;
                var perintah = "\"" + diri + "\" " + ArgumenBukaEvent + " " + nama1 + " " + nama2;
                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION pi;
                const uint CREATE_NO_WINDOW = 0x08000000;
                if (!CreateProcessAsUserW(salinan, null, perintah, IntPtr.Zero, IntPtr.Zero,
                        false, CREATE_NO_WINDOW, IntPtr.Zero, null, ref si, out pi)) return false;
                try
                {
                    WaitForSingleObject(pi.hProcess, 30000);
                    uint keluar;
                    if (!GetExitCodeProcess(pi.hProcess, out keluar)) return false;
                    kode = (int)keluar;
                    return true;
                }
                finally { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
            }
            catch { return false; }
            finally
            {
                if (sid != IntPtr.Zero) LocalFree(sid);
                if (salinan != IntPtr.Zero) CloseHandle(salinan);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
    }
}
