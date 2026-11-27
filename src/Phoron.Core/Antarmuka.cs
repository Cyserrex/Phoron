using System;
using System.Collections.Generic;

namespace Phoron.Core
{
    /// <summary>
    /// Bagian pengendali layanan yang dipakai UI. ServiceManager (layanan di
    /// proses ini) dan Jarak.LayananJarak (layanan di phorond) sama-sama
    /// memenuhinya, jadi halaman tidak perlu tahu di mana Apache berjalan.
    /// </summary>
    public interface ILayanan
    {
        ServiceState WebState { get; }
        ServiceState DbState { get; }
        event Action<ServiceKind, ServiceState> StateChanged;
        void StopAll();
    }

    /// <summary>Bagian pengendali proyek Node yang dipakai UI; lihat <see cref="ILayanan"/>.</summary>
    public interface IProsesNode
    {
        /// <summary>(folder, baris) - keluaran proses.</summary>
        event Action<string, string> Output;
        /// <summary>(folder, sedangJalan)</summary>
        event Action<string, bool> StateChanged;
        /// <summary>(folder, url) saat alamat terdeteksi.</summary>
        event Action<string, string> UrlFound;
        bool IsRunning(string folder);
        string UrlOf(string folder);
        IEnumerable<string> RunningFolders { get; }
        void Stop(string folder);
        void StopAll();
        List<string> Riwayat(string folder);
        int PortBentrok(string folder);
        void Sisipkan(string folder, string baris);
    }
}
