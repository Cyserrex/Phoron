using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Jalur FastCGI untuk PHP non-thread-safe, yang di Windows punya dua
        /// jebakan dan keduanya PERNAH membuat Phoron menyajikan galat untuk
        /// setiap berkas .php tanpa kecuali.
        ///
        /// 1. Alamat FastCGI harus diakhiri garis miring. Apache menyambung
        ///    jalur berkas langsung ke belakangnya; di Linux jalur itu diawali
        ///    "/" sehingga hasilnya sah, di Windows ia diawali "C:" sehingga
        ///    yang terbentuk "fcgi://127.0.0.1:9123C:/..." - dan jawabannya 400
        ///    "URI cannot be parsed", tanpa menyebut PHP sama sekali.
        ///
        /// 2. SCRIPT_FILENAME harus disetel sendiri. Dengan garis miring tadi,
        ///    Apache mengirim SELURUH alamat proxy sebagai nama berkas, dan
        ///    php-cgi menjawab "No input file specified" untuk semua permintaan.
        ///
        /// Keduanya sudah diukur pada Apache 2.4.38 + PHP 8.3 yang sungguhan:
        /// sebelum diperbaiki 400 untuk setiap permintaan, sesudahnya 200.
        /// </summary>
        static void UjiFastCgi()
        {
            Bagian("FastCGI untuk PHP non-thread-safe");

            var apache = Nyata().FirstOrDefault(p => p.Kind == BinKind.Apache);
            if (apache == null) { Console.WriteLine("     dilewati: tidak ada Apache terpasang"); return; }

            // Di komputer ini belum tentu ada build PHP NTS - ketiganya bisa
            // saja thread-safe. Yang diuji perilaku ConfigWriter, jadi paketnya
            // dibuat sendiri: folder dengan php-cgi.exe kosong sudah cukup untuk
            // menempuh jalur yang sama.
            var folder = Path.Combine(Paths.Tmp, "php-nts-uji");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "ext"));
            File.WriteAllText(Path.Combine(folder, "php-cgi.exe"), "");
            var php = new BinPackage
            {
                Kind = BinKind.Php,
                Id = "php-8.3.12-nts-uji",
                Path = folder,
                Version = "8.3.12",
                Arch = "x64",
                ThreadSafe = false,      // inilah yang menentukan jalurnya
            };

            var profil = new Profile
            {
                Name = "Uji FastCGI",
                PhpId = php.Id,
                ApacheId = apache.Id,
                HttpPort = 8080,
                HttpsPort = 8443,
            };
            var r = ConfigWriter.Build(profil, php, apache, null, null, new List<Site>());
            Ok("Konfigurasi memakai jalur FastCGI", r.PhpFastCgi, "malah memakai mod_php");

            var berkas = Path.Combine(Paths.EtcApache, "mod_php.conf");
            Ok("mod_php.conf terbentuk", File.Exists(berkas), berkas);
            if (!File.Exists(berkas)) return;
            var isi = File.ReadAllText(berkas);

            var alamat = "fcgi://127.0.0.1:" + r.FastCgiPort;
            Ok("Alamat FastCGI diakhiri garis miring",
               isi.Contains(alamat + "/\""),
               "tanpa garis miring, setiap berkas .php dijawab 400 di Windows");
            Ok("SCRIPT_FILENAME disetel sendiri",
               isi.Contains("ProxyFCGISetEnvIf") && isi.Contains("SCRIPT_FILENAME"),
               "tanpa ini php-cgi menjawab \"No input file specified\"");
            Ok("SCRIPT_FILENAME dirakit dari akar dokumen dan alamat permintaan",
               isi.Contains("%{DOCUMENT_ROOT}%{REQUEST_URI}"), "");

            // mod_proxy dan mod_proxy_fcgi harus ikut dimuat, kalau tidak
            // direktifnya tidak dikenal dan Apache menolak start.
            var httpd = r.HttpdConf != null && File.Exists(r.HttpdConf)
                ? File.ReadAllText(r.HttpdConf) : "";
            Ok("mod_proxy dimuat", httpd.Contains("mod_proxy.so"), "");
            Ok("mod_proxy_fcgi dimuat", httpd.Contains("mod_proxy_fcgi.so"), "");

            // Apache tua tidak mengenal ProxyFCGISetEnvIf, dan direktif yang
            // tidak dikenal membuat httpd MENOLAK START - jauh lebih buruk
            // daripada PHP yang tidak jalan. Yang harus muncul: keluhan, bukan
            // direktifnya.
            var tua = new BinPackage
            {
                Kind = BinKind.Apache,
                Id = "httpd-2.4.9-uji",
                Path = apache.Path,
                Version = "2.4.9",
                Arch = "x64",
                MainExe = apache.MainExe,
            };
            var r2 = ConfigWriter.Build(profil, php, tua, null, null, new List<Site>());
            var isi2 = File.ReadAllText(berkas);
            Ok("Apache lama: ProxyFCGISetEnvIf tidak ditulis",
               !isi2.Contains("ProxyFCGISetEnvIf"),
               "ditulis juga - Apache 2.4.9 akan menolak start sama sekali");
            Ok("Apache lama: orangnya diberi tahu kenapa",
               r2.Warnings.Any(w => w.IndexOf("ProxyFCGISetEnvIf", StringComparison.OrdinalIgnoreCase) >= 0),
               "tidak ada keluhan - PHP diam-diam tidak jalan");

            try { Directory.Delete(folder, true); } catch { }
        }
    }
}
