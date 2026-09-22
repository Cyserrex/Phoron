using System;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Phoron.App
{
    /// <summary>
    /// Mutex dan event bernama yang dipakai Phoron untuk saling menyapa antar
    /// proses - penjaga instans tunggal, permintaan "tampilkan jendelamu", dan
    /// permintaan "tutup dulu, pemasang mau jalan".
    ///
    /// KENAPA TIDAK CUKUP `new Mutex(nama)` BEGITU SAJA. Phoron sendiri
    /// menawarkan "jalankan ulang sebagai Administrator" - dibutuhkan untuk
    /// menulis berkas hosts dan memasang sertifikat. Sesudah itu Phoron berjalan
    /// dengan hak tinggi, sementara klik pada ikon taskbar tetap melahirkan
    /// proses berhak biasa. Dua hal lalu menghalangi proses itu menyapa Phoron
    /// yang sudah berjalan, dan keduanya harus dibereskan:
    ///
    ///   1. DACL bawaan. Token Administrator yang sudah dinaikkan memberi kuasa
    ///      penuh kepada grup Administrators, bukan kepada SID penggunanya. Di
    ///      proses berhak biasa milik orang yang sama, grup itu hanya dipasang
    ///      untuk penolakan - jadi aksesnya ditutup. Karena itu izinnya ditulis
    ///      sendiri, atas nama SID pengguna, yang sama persis di kedua token.
    ///
    ///   2. Label integritas. Objek buatan proses berhak tinggi berlabel tinggi,
    ///      dan aturan "tidak boleh menulis ke atas" menutupnya bagi proses
    ///      berhak biasa - DACL seramah apa pun tidak mengubah itu. Labelnya
    ///      karena itu diturunkan ke Low.
    ///
    /// Yang terbuka karenanya: proses lain milik siapa pun di mesin ini bisa
    /// meminta jendela Phoron dimunculkan, dan bisa membuat Phoron yang BARU
    /// mengira sudah ada yang berjalan. Keduanya gangguan sepele dan hanya bisa
    /// dilakukan dari mesin yang sama; harganya jauh lebih murah daripada
    /// aplikasi yang menolak muncul dan menegur penggunanya dengan kotak galat.
    /// </summary>
    static class ObjekAntarProses
    {
        // ---------------------------------------------------------------- API

        /// <summary>
        /// Mutex bernama yang tetap bisa dibuka oleh proses berhak biasa milik
        /// pengguna yang sama, walau dibuat oleh Phoron yang berhak tinggi.
        /// </summary>
        public static Mutex BuatMutex(string nama, bool milikSendiri, out bool baru)
        {
            var m = new Mutex(milikSendiri, nama, out baru, IzinMutex());
            if (baru) TurunkanLabel(m.SafeWaitHandle);
            return m;
        }

        /// <summary>Event bernama, dengan pertimbangan yang sama seperti <see cref="BuatMutex"/>.</summary>
        public static EventWaitHandle BuatEvent(string nama, EventResetMode mode, out bool baru)
        {
            var e = new EventWaitHandle(false, mode, nama, out baru, IzinEvent());
            if (baru) TurunkanLabel(e.SafeWaitHandle);
            return e;
        }

        // ------------------------------------------------------------- Izin

        static SecurityIdentifier Aku()
        {
            using (var id = WindowsIdentity.GetCurrent()) return id.User;
        }

        static MutexSecurity IzinMutex()
        {
            var izin = new MutexSecurity();
            izin.AddAccessRule(new MutexAccessRule(
                Aku(), MutexRights.FullControl, AccessControlType.Allow));
            return izin;
        }

        static EventWaitHandleSecurity IzinEvent()
        {
            var izin = new EventWaitHandleSecurity();
            izin.AddAccessRule(new EventWaitHandleAccessRule(
                Aku(), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            return izin;
        }

        // ------------------------------------------------------- Label integritas

        // Label integritas disimpan di SACL, dan ObjectSecurity milik .NET
        // memperlakukan SACL semata-mata sebagai daftar audit - tidak ada jalan
        // ke sana lewat MutexSecurity. Karena itu dipasang langsung.
        const int SE_KERNEL_OBJECT = 6;
        const int LABEL_SECURITY_INFORMATION = 0x00000010;
        const int SDDL_REVISION_1 = 1;

        /// <summary>ML = mandatory label, NW = tidak boleh menulis ke atas, LW = tingkat Low.</summary>
        const string SddlLabelRendah = "S:(ML;;NW;;;LW)";

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string sddl, int revisi, out IntPtr psd, IntPtr ukuran);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetSecurityDescriptorSacl(
            IntPtr psd, out bool ada, out IntPtr sacl, out bool bawaan);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint SetSecurityInfo(
            IntPtr objek, int jenis, int bagian,
            IntPtr pemilik, IntPtr grup, IntPtr dacl, IntPtr sacl);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr p);

        /// <summary>
        /// Menurunkan label integritas objek ke Low. Gagal diam-diam: tanpa ini
        /// Phoron tetap berjalan seperti biasa selama tidak dinaikkan haknya,
        /// dan mematikan aplikasi gara-gara penyedap kenyamanan jelas keliru.
        /// </summary>
        static void TurunkanLabel(SafeWaitHandle pegangan)
        {
            IntPtr psd = IntPtr.Zero;
            try
            {
                if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                        SddlLabelRendah, SDDL_REVISION_1, out psd, IntPtr.Zero)) return;
                bool ada, bawaan;
                IntPtr sacl;
                if (!GetSecurityDescriptorSacl(psd, out ada, out sacl, out bawaan)) return;
                if (!ada) return;
                SetSecurityInfo(pegangan.DangerousGetHandle(), SE_KERNEL_OBJECT,
                    LABEL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            }
            catch { /* lihat ringkasan di atas */ }
            finally { if (psd != IntPtr.Zero) LocalFree(psd); }
        }
    }
}
