using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Membuka basis data profil aktif di pengelola basis data GRAFIS milik
    /// orang lain - HeidiSQL, dan yang sekeluarga dengannya.
    ///
    /// KENAPA ADA, PADAHAL PHORON SUDAH PUNYA TAB BASIS DATA SENDIRI. Keduanya
    /// menjawab kebutuhan yang berbeda, dan itu bukan pengulangan. Tab bawaan
    /// untuk pekerjaan sehari-hari yang harus cepat: lihat isi tabel, ubah satu
    /// nilai, jalankan satu kueri - tanpa berpindah jendela dan tanpa menunggu
    /// aplikasi lain menyala. HeidiSQL untuk pekerjaan yang memang lebih berat:
    /// menyunting struktur kolom, memindahkan data antar-server, menjalankan
    /// dump besar, memeriksa proses yang sedang jalan.
    ///
    /// YANG TIDAK DILAKUKAN: Phoron tidak ikut mengirimkan HeidiSQL. Ia dipakai
    /// kalau MEMANG SUDAH ADA di komputer ini - bawaan Laragon, atau pemasangan
    /// tersendiri. Menyertakannya berarti ikut menanggung pembaruan keamanannya
    /// dan membengkakkan installer beberapa puluh megabyte untuk sesuatu yang
    /// bukan milik Phoron.
    /// </summary>
    public static class KlienLuar
    {
        public sealed class Temuan
        {
            public string Nama = "";
            public string Jalur = "";
            public bool Ada { get { return Jalur.Length > 0 && File.Exists(Jalur); } }
        }

        /// <summary>
        /// Cari HeidiSQL. Urutannya disengaja: jalur yang disetel orang menang
        /// atas tebakan mana pun, lalu folder bin yang sudah dikenal Phoron -
        /// di situlah salinan milik Laragon berada - baru pemasangan biasa.
        /// </summary>
        public static Temuan CariHeidi(Settings setelan)
        {
            var hasil = new Temuan { Nama = "HeidiSQL" };

            if (setelan != null && !string.IsNullOrEmpty(setelan.HeidiSql))
            {
                var pilihan = setelan.HeidiSql;
                if (Directory.Exists(pilihan))
                    pilihan = Path.Combine(pilihan, "heidisql.exe");
                if (File.Exists(pilihan)) { hasil.Jalur = pilihan; return hasil; }
                // Jalur yang disetel tapi sudah tidak ada TIDAK diam-diam
                // diganti tebakan: orang yang menyetelnya perlu tahu bahwa
                // setelannya sudah basi, bukan mendapat aplikasi lain.
                return hasil;
            }

            var calon = new List<string>();
            var akarBin = setelan != null && setelan.BinRoots != null
                ? setelan.BinRoots : new List<string>();
            foreach (var akar in akarBin)
            {
                if (string.IsNullOrEmpty(akar)) continue;
                calon.Add(Path.Combine(akar, "heidisql", "heidisql.exe"));
                calon.Add(Path.Combine(akar, "HeidiSQL", "heidisql.exe"));
            }
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                calon.Add(Path.Combine(pf, "HeidiSQL", "heidisql.exe"));
            }

            foreach (var c in calon)
            {
                try { if (File.Exists(c)) { hasil.Jalur = c; return hasil; } }
                catch { }
            }
            return hasil;
        }

        /// <summary>
        /// Susun argumen untuk HeidiSQL. Dipisah dari peluncurannya supaya bisa
        /// diuji tanpa menyalakan aplikasi apa pun.
        ///
        /// SANDI SENGAJA TIDAK IKUT. Di Windows, baris perintah sebuah proses
        /// bisa dibaca proses lain di sesi yang sama - Task Manager pun
        /// menampilkannya. Phoron sudah menolak menaruh sandi di baris perintah
        /// mysql.exe dan memakai berkas setelan sementara sebagai gantinya;
        /// menaruhnya di sini berarti membatalkan keputusan itu lewat pintu
        /// belakang. Pemasangan bawaan Phoron memang tidak memakai sandi, dan
        /// kalau ada, HeidiSQL akan menanyakannya sendiri.
        /// </summary>
        public static string ArgumenHeidi(string namaProfil, int port, string pengguna)
        {
            var sb = new StringBuilder();
            sb.Append("--host=\"127.0.0.1\"");
            sb.Append(" --port=").Append(port);
            sb.Append(" --user=\"").Append((pengguna ?? "root").Replace("\"", "")).Append('"');
            // Nama sesi yang terbaca di jendela HeidiSQL. Menyebut profilnya
            // penting: orang yang punya beberapa profil perlu tahu server mana
            // yang sedang dibukanya.
            var nama = "Phoron" + (string.IsNullOrEmpty(namaProfil) ? "" : ": " + namaProfil);
            sb.Append(" --description=\"").Append(nama.Replace("\"", "")).Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Buka HeidiSQL untuk profil aktif. Mengembalikan null bila berhasil,
        /// atau kalimat keberatan bila tidak.
        /// </summary>
        public static string BukaHeidi(Engine e)
        {
            if (e == null || e.Active == null) return Lang.T("Belum ada profil yang aktif.");
            if (!e.Active.PakaiMySql) return Lang.T("Profil aktif tidak memakai basis data.");

            var temuan = CariHeidi(e.Settings);
            if (!temuan.Ada)
                return Lang.T("HeidiSQL tidak ditemukan di komputer ini. Pasang dulu, atau "
                              + "sebutkan jalurnya di Pengaturan.");

            try
            {
                var arg = ArgumenHeidi(e.Active.Name, e.Active.MySqlPort,
                                       e.Settings != null ? e.Settings.DbPengguna : "root");
                Process.Start(new ProcessStartInfo(temuan.Jalur, arg)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(temuan.Jalur) ?? Paths.Root,
                });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
