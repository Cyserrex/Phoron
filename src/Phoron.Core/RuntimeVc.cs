using System;
using System.Collections.Generic;
using System.IO;

namespace Phoron.Core
{
    /// <summary>
    /// Memeriksa Visual C++ Redistributable yang dibutuhkan sebuah paket.
    ///
    /// Build PHP dan Apache untuk Windows ditautkan ke runtime Visual C++
    /// tertentu - itulah arti "VC11", "VC15", "VS16" di nama foldernya. Runtime
    /// itu TIDAK ikut dalam arsipnya; ia dipasang terpisah sebagai
    /// Visual C++ Redistributable, dan komputer yang belum punya versinya akan
    /// menolak memuat php5apache2_4.dll dengan kalimat "The specified module
    /// could not be found" - yang menunjuk DLL PHP, padahal PHP-nya baik-baik
    /// saja dan yang hilang adalah msvcr110.dll milik Microsoft.
    ///
    /// Gejalanya khas: satu profil gagal sementara profil lain di komputer yang
    /// sama jalan mulus, karena tiap toolset butuh redistributable berbeda.
    /// </summary>
    public static class RuntimeVc
    {
        public class Hasil
        {
            public bool Perlu;          // toolsetnya dikenali
            public bool Ada = true;     // runtime-nya ada
            public string Dll = "";
            public string Paket = "";   // nama yang dicari orang di situs Microsoft
            public string Pesan = "";
        }

        /// <summary>DLL penanda dan nama paketnya, per toolset.</summary>
        static bool Petakan(string toolset, out string dll, out string paket)
        {
            dll = ""; paket = "";
            var t = (toolset ?? "").ToUpperInvariant();
            if (t == "VC9") { dll = "msvcr90.dll"; paket = "Visual C++ 2008"; return true; }
            if (t == "VC11") { dll = "msvcr110.dll"; paket = "Visual C++ 2012"; return true; }
            if (t == "VC12") { dll = "msvcr120.dll"; paket = "Visual C++ 2013"; return true; }
            // VC14, VC15, VS16, VS17 semuanya memakai runtime "140" yang sama.
            if (t == "VC14" || t == "VC15" || t == "VS16" || t == "VS17")
            {
                dll = "vcruntime140.dll";
                paket = "Visual C++ 2015-2022";
                return true;
            }
            return false;
        }

        public static Hasil Periksa(BinPackage pkg)
        {
            var h = new Hasil();
            if (pkg == null) return h;

            string dll, paket;
            if (!Petakan(pkg.Compiler, out dll, out paket)) return h;
            h.Perlu = true;
            h.Dll = dll;
            h.Paket = paket;

            var arch = string.IsNullOrEmpty(pkg.Arch) ? BinProbe.Arsitektur(pkg.MainExe) : pkg.Arch;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            // Di Windows 64-bit, DLL 64-bit ada di System32 dan yang 32-bit di
            // SysWOW64 - penamaan yang memang terbalik dari dugaan.
            var folder = string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(windows, "SysWOW64")
                : Path.Combine(windows, "System32");

            var jalur = Path.Combine(folder, dll);
            // Sebagian build membawa runtime-nya sendiri di folder exe; itu sah
            // dan membuat redistributable sistem tidak diperlukan.
            var lokal = Path.Combine(pkg.Path, dll);
            var lokalBin = Path.Combine(pkg.Path, "bin", dll);

            h.Ada = File.Exists(jalur) || File.Exists(lokal) || File.Exists(lokalBin);
            if (!h.Ada)
                h.Pesan = pkg.Kind + " " + pkg.Version + " dibangun dengan " + pkg.Compiler
                    + ", jadi butuh " + paket + " Redistributable "
                    + (arch.Length > 0 ? arch : "yang sesuai")
                    + ". Di komputer ini " + dll + " tidak ada, dan tanpa itu modul PHP "
                    + "tidak bisa dimuat - Apache akan gagal start dengan pesan yang justru "
                    + "menunjuk berkas PHP. Pasang " + paket + " Redistributable dari situs Microsoft.";
            return h;
        }

        /// <summary>Pesan untuk semua paket yang runtime-nya kurang; kosong bila tidak ada masalah.</summary>
        public static List<string> PeriksaSemua(params BinPackage[] paket)
        {
            var pesan = new List<string>();
            var sudah = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paket)
            {
                var h = Periksa(p);
                if (!h.Perlu || h.Ada) continue;
                // Satu redistributable yang sama tidak perlu diadukan dua kali
                // hanya karena PHP dan Apache sama-sama memakainya.
                if (!sudah.Add(h.Dll)) continue;
                pesan.Add(h.Pesan);
            }
            return pesan;
        }
    }
}
