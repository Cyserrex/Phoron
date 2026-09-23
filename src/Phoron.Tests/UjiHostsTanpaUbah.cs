using System;
using System.IO;
using System.Linq;
using System.Threading;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Sinkronisasi hosts yang tidak mengubah apa pun tidak boleh menulis.
        ///
        /// Keluhannya: Phoron yang Virtual Host-nya mati, dengan berkas hosts
        /// yang tidak berisi blok Phoron, menampilkan "Berkas hosts tidak bisa
        /// ditulis" pada setiap Apply. Tidak ada yang perlu ditulis - tapi Sync
        /// tetap mencoba menulis ulang isi yang sama persis, dan tanpa hak
        /// Administrator percobaan itu ditolak.
        ///
        /// Yang diperiksa: berkasnya tidak disentuh sama sekali - stempel
        /// waktunya tetap, dan tidak ada cadangan baru di sebelahnya. Menulis
        /// isi yang sama pun dihitung gagal, sebab di mesin pengguna penulisan
        /// itulah yang ditolak.
        /// </summary>
        static void UjiHostsTanpaUbah()
        {
            Bagian("Sinkronisasi hosts tanpa perubahan tidak menulis");

            var folder = Path.Combine(Paths.Tmp, "hosts-tanpa-ubah");
            Directory.CreateDirectory(folder);
            var berkas = Path.Combine(folder, "hosts");
            var semula = Paths.HostsFile;
            Paths.HostsFile = berkas;
            try
            {
                // Hosts biasa milik orang, tanpa blok Phoron - persis keadaan
                // mesin pengguna. Diakhiri baris kosong, seperti lazimnya.
                File.WriteAllText(berkas,
                    "# Copyright (c) Microsoft Corp.\r\n" +
                    "127.0.0.1\tlocalhost\r\n" +
                    "10.1.2.3\tserver-kantor\r\n" +
                    "\r\n");
                var waktu = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(berkas, waktu);
                var isiAwal = File.ReadAllText(berkas);
                var berkasAwal = Directory.GetFiles(folder).Length;

                // Virtual Host mati = daftar nama kosong.
                HostsFile.Sync(new string[0]);

                Ok("Berkasnya tidak ditulis ulang",
                   File.GetLastWriteTimeUtc(berkas) == waktu,
                   "stempel waktunya berubah - di mesin tanpa hak Administrator "
                   + "inilah yang memunculkan peringatan");
                Ok("Tidak ada cadangan baru",
                   Directory.GetFiles(folder).Length == berkasAwal,
                   string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
                Ok("Isinya utuh, termasuk baris kosong di ujung",
                   File.ReadAllText(berkas) == isiAwal, "");

                // Pembanding: kalau memang ADA yang perlu ditulis, ia harus tetap
                // ditulis. Tanpa ini, "tidak pernah menulis apa pun" juga lulus.
                HostsFile.Sync(new[] { "tokoonline.test" });
                var isiBaru = File.ReadAllText(berkas);
                Ok("Nama baru tetap ditulis", isiBaru.Contains("tokoonline.test"), isiBaru);
                Ok("Baris milik orang lain tetap ada",
                   isiBaru.Contains("server-kantor"), isiBaru);

                // Dan menulis nama yang sama untuk kedua kalinya juga tidak perlu.
                File.SetLastWriteTimeUtc(berkas, waktu);
                HostsFile.Sync(new[] { "tokoonline.test" });
                Ok("Nama yang sudah tercatat tidak ditulis ulang",
                   File.GetLastWriteTimeUtc(berkas) == waktu, "");

                // Mematikan Virtual Host lagi: bloknya harus benar-benar dibuang.
                HostsFile.Sync(new string[0]);
                Ok("Blok Phoron dibuang saat daftarnya kosong",
                   !File.ReadAllText(berkas).Contains("tokoonline.test"), "");
            }
            finally
            {
                Paths.HostsFile = semula;
                try { Directory.Delete(folder, true); } catch { }
            }
        }
    }
}
