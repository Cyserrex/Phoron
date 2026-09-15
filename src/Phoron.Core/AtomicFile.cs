using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Tulis-lalu-ganti: berkas tujuan tidak pernah ada dalam keadaan setengah
    /// tertulis.
    ///
    /// <see cref="File.WriteAllText(string,string)"/> memotong berkas tujuan
    /// lebih dulu, lalu mengisinya. Di antara kedua langkah itu berkasnya kosong
    /// - dan kalau proses mati, listrik padam, atau antivirus menyela di celah
    /// tersebut, yang tertinggal adalah berkas nol byte. Untuk phoron.ini,
    /// berkas profil, dan terutama berkas hosts Windows, itu berarti setelan
    /// pengguna lenyap tanpa satu pun galat.
    ///
    /// Di sini isinya ditulis ke berkas sementara lebih dulu, baru ditukar.
    /// Penukarannya satu langkah di tingkat NTFS, jadi pembaca mana pun selalu
    /// melihat isi yang utuh - yang lama atau yang baru, tidak pernah separuh.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Akhiran berkas sementara, memuat PID.
        ///
        /// Phoron bisa punya dua proses sekaligus: yang utama, dan pembantu
        /// ber-Administrator yang dipanggil dengan --sinkron-hosts. Keduanya
        /// bisa menulis berkas hosts. Tanpa PID di nama, keduanya akan memakai
        /// berkas sementara yang sama dan saling menimpa.
        /// </summary>
        static readonly string AkhiranTmp = ".ptmp" + Process.GetCurrentProcess().Id;

        static readonly Encoding Utf8TanpaBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string content, Encoding enc = null)
        {
            WriteAllBytes(path, (enc ?? Utf8TanpaBom).GetBytes(content ?? ""));
        }

        /// <summary>Seperti File.WriteAllLines: tiap baris diakhiri pemisah baris.</summary>
        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding enc = null)
        {
            var sb = new StringBuilder();
            if (lines != null)
                foreach (var l in lines) sb.Append(l).Append(Environment.NewLine);
            WriteAllText(path, sb.ToString(), enc);
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            if (bytes == null) bytes = new byte[0];

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Berkas sementara sengaja di direktori YANG SAMA, bukan di %TEMP%.
            // File.Replace menolak bekerja lintas volume, dan %TEMP% kerap ada
            // di drive lain daripada folder Phoron.
            var tmp = path + AkhiranTmp;
            var atributDisimpan = false;
            var atribut = FileAttributes.Normal;
            try
            {
                // WriteThrough + Flush(true) inilah yang benar-benar menyuruh
                // Windows menurunkan isinya ke piringan. Tanpa itu yang dijamin
                // hanya selamat dari matinya proses kita, bukan matinya listrik:
                // penukaran namanya sudah tercatat sementara isinya masih di
                // singgahan tulis.
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                               FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                // Tujuan belum ada: File.Replace akan melempar
                // FileNotFoundException, jadi di sini dipakai File.Move - yang
                // di dalam satu direktori juga penggantian nama satu langkah.
                // Cabang ini sering terpakai: tiap profil baru, tiap vhost, dan
                // phoron.ini pada jalan pertama.
                if (!File.Exists(path)) { File.Move(tmp, path); return; }

                atribut = File.GetAttributes(path);
                atributDisimpan = true;
                // Berkas hosts Windows hampir selalu ber-ReadOnly, dan
                // File.Replace menolak tujuan yang read-only.
                if ((atribut & (FileAttributes.ReadOnly | FileAttributes.Hidden
                                | FileAttributes.System)) != 0)
                    File.SetAttributes(path, FileAttributes.Normal);

                try
                {
                    // ignoreMetadataErrors: berkas hosts punya ACL yang tidak
                    // lazim, dan penyalinan metadata bisa gagal walau penukaran
                    // datanya sendiri sebenarnya berhasil.
                    File.Replace(tmp, path, null, true);
                }
                catch (Exception ex) when (ex is IOException
                                        || ex is PlatformNotSupportedException
                                        || ex is UnauthorizedAccessException)
                {
                    // Sistem berkas yang tidak mendukung penukaran (FAT32 di
                    // flashdisk, berbagi lewat jaringan). Tidak atomik, tapi
                    // persis seperti perilaku sebelum berkas ini ada - jadi
                    // tidak pernah jadi kemunduran.
                    File.Copy(tmp, path, true);
                }
            }
            finally
            {
                if (atributDisimpan)
                    try { if (File.Exists(path)) File.SetAttributes(path, atribut); }
                    catch { }
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
            }
        }

        /// <summary>
        /// Pindahkan berkas yang isinya tidak masuk akal ke samping, dan
        /// kembalikan nama barunya (null bila tidak ada yang dipindah).
        ///
        /// Dipakai sebelum menimpa berkas yang terbaca cacat. Menimpanya begitu
        /// saja berarti membuang sisa setelan yang sebenarnya masih selamat;
        /// menolak menulis sama sekali membuat Phoron mengomel tanpa jalan
        /// keluar. Menyingkirkannya lebih dulu memenuhi keduanya.
        /// </summary>
        public static string Karantina(string path, string sebab)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var dasar = path + "." + (string.IsNullOrWhiteSpace(sebab) ? "rusak" : sebab.Trim())
                          + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                // Dua kali dalam satu detik tetap harus menghasilkan dua berkas.
                var calon = dasar;
                var n = 1;
                while (File.Exists(calon)) calon = dasar + "-" + (++n);
                File.Move(path, calon);
                return calon;
            }
            catch { return null; }
        }
    }
}
