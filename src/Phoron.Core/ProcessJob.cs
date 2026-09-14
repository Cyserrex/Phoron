using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Phoron.Core
{
    /// <summary>
    /// Mengikat seluruh proses anak (httpd, mysqld, php-cgi, node) ke sebuah Job
    /// Object Windows milik Phoron. Begitu proses Phoron berakhir - DENGAN CARA
    /// APA PUN, termasuk dihentikan paksa Task Manager, ditutup pemasang, atau
    /// mati mendadak - Windows ikut menutup seluruh anggota job itu.
    ///
    /// Tanpa ini, proses anak jadi yatim dan tetap memegang port 80 serta 3306
    /// tanpa ada jendela yang mengaku memilikinya. Itu persis yang terjadi saat
    /// installer menutup Phoron: penutupan dibatalkan oleh perilaku "mengecil ke
    /// baki sistem", pemasang lalu menghentikan prosesnya, dan pembersihan
    /// normal tidak pernah sempat berjalan.
    ///
    /// Penghentian lewat job memang tidak santun - mysqld tidak sempat menutup
    /// InnoDB dengan rapi. Tapi ini jaring pengaman, bukan jalur utama:
    /// penghentian biasa tetap lewat ServiceManager yang meminta mysqladmin
    /// shutdown lebih dulu. Jaring yang kasar jauh lebih baik daripada port yang
    /// terkunci sampai komputer di-restart.
    /// </summary>
    public static class ProcessJob
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr atribut, string nama);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetInformationJobObject(IntPtr job, int jenis, IntPtr info, uint panjang);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proses);

        const int ExtendedLimitInformation = 9;
        const uint KillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimitInformationStruct
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static readonly object _kunci = new object();
        static IntPtr _job = IntPtr.Zero;
        static bool _gagal;

        static void Pastikan()
        {
            if (_job != IntPtr.Zero || _gagal) return;
            try
            {
                // Job tanpa nama: hanya proses ini yang memegangnya, jadi dua
                // salinan Phoron tidak akan saling membunuh proses anaknya.
                var job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) { _gagal = true; return; }

                var info = new ExtendedLimitInformationStruct();
                info.BasicLimitInformation.LimitFlags = KillOnJobClose;
                int ukuran = Marshal.SizeOf(typeof(ExtendedLimitInformationStruct));
                var ptr = Marshal.AllocHGlobal(ukuran);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(job, ExtendedLimitInformation, ptr, (uint)ukuran))
                    {
                        _gagal = true;
                        return;
                    }
                }
                finally { Marshal.FreeHGlobal(ptr); }
                _job = job;
            }
            catch { _gagal = true; }
        }

        /// <summary>
        /// Masukkan sebuah proses ke job. Kegagalan sengaja didiamkan: job object
        /// hanya jaring pengaman, dan aplikasi harus tetap bisa menjalankan
        /// layanan walau Windows menolak membuatnya.
        /// </summary>
        public static void Ikat(Process p)
        {
            if (p == null) return;
            lock (_kunci)
            {
                Pastikan();
                if (_job == IntPtr.Zero) return;
                try { AssignProcessToJobObject(_job, p.Handle); } catch { }
            }
        }
    }
}
