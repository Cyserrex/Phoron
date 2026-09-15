using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Phoron.Core
{
    /// <summary>
    /// Penulis berkas log: berputar sebelum membesar, dan aman dipanggil dari
    /// banyak utas.
    ///
    /// Dua masalah yang diselesaikannya, keduanya nyata dan keduanya diam.
    ///
    /// Pertama, phoron.log dulu tumbuh tanpa batas. Di mesin penulis ia mencapai
    /// 365 KB dalam lima hari pemakaian biasa, dan apache-error.log 1,28 MB.
    /// Tidak ada yang menyadarinya karena halaman Log hanya membaca 400 KB
    /// terakhir, jadi layarnya tetap gesit sementara berkasnya terus membengkak.
    ///
    /// Kedua, penulisannya dulu File.AppendAllText telanjang di dalam
    /// try/catch kosong. Engine.Say dipanggil dari utas layar DAN dari utas
    /// kolam - pengawas Exited dan pembaca keluaran httpd/mysqld semuanya
    /// berujung ke sana. Dua penulis berbarengan membuat salah satunya melempar
    /// IOException, yang lalu ditelan: barisnya lenyap tanpa bekas, dan justru
    /// baris saat keadaan sedang sibuk yang paling dibutuhkan.
    /// </summary>
    public static class LogFile
    {
        /// <summary>Berkas berputar setelah melewati ukuran ini.</summary>
        public const long BatasBytes = 2 * 1024 * 1024;

        /// <summary>Sebanyak ini berkas lama disimpan: phoron.log.1 sampai .3.</summary>
        public const int SimpanLama = 3;

        /// <summary>
        /// Kunci selebar proses. BUKAN Mutex bernama: hanya satu Phoron yang
        /// berjalan - dijaga mutex instans tunggal - dan pembantu ber-admin
        /// --sinkron-hosts tidak pernah membuat Engine, jadi tidak pernah menulis
        /// ke sini. Mutex bernama hanya menambah ongkos tanpa menutup apa pun.
        /// </summary>
        static readonly object _kunci = new object();

        public static void Tambah(string path, string baris)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (_kunci)
            {
                for (int coba = 0; coba < 3; coba++)
                {
                    try
                    {
                        if (Panjang(path) >= BatasBytes) Putar(path);

                        // FileShare.ReadWrite supaya halaman Log yang membaca tiap
                        // dua detik tidak terhalang - dan tidak menghalangi.
                        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write,
                                                       FileShare.ReadWrite))
                        using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                            w.WriteLine(baris ?? "");
                        return;
                    }
                    catch (IOException)
                    {
                        // Berkasnya sedang dipegang sesaat oleh pembaca lain;
                        // sebentar lagi biasanya sudah lepas.
                        Thread.Sleep(15);
                    }
                    catch
                    {
                        // Izin atau jalur yang salah - mencoba lagi tidak akan
                        // mengubah apa pun.
                        return;
                    }
                }
            }
        }

        static long Panjang(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Geser phoron.log jadi .1, .1 jadi .2, dan seterusnya; yang paling tua
        /// dibuang. Tiap langkah dijaga sendiri: rotasi yang gagal harus berakhir
        /// pada "tetap menulis ke berkas yang besar", tidak pernah pada baris
        /// yang hilang atau galat yang menjalar ke pemanggil.
        /// </summary>
        public static void Putar(string path)
        {
            try { File.Delete(path + "." + SimpanLama); } catch { }

            for (int i = SimpanLama - 1; i >= 1; i--)
            {
                try
                {
                    var dari = path + "." + i;
                    var ke = path + "." + (i + 1);
                    if (File.Exists(dari))
                    {
                        try { File.Delete(ke); } catch { }
                        File.Move(dari, ke);
                    }
                }
                catch { }
            }

            try
            {
                if (File.Exists(path))
                {
                    try { File.Delete(path + ".1"); } catch { }
                    File.Move(path, path + ".1");
                }
            }
            catch { }
        }
    }
}
