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
        /// Log bawaan vendor tidak boleh tersalin ke httpd.conf.
        ///
        /// conf\original\httpd.conf membawa CustomLog "logs/access.log" dan
        /// ErrorLog "logs/error.log" yang RELATIF ke ServerRoot - folder paket
        /// di bin. Dulu baris itu ikut tersalin, jadi Apache menulis log akses
        /// ke folder Laragon/XAMPP apa pun setelan "Catat log rinci". Yang
        /// boleh tersisa hanya log milik Phoron di folder logs.
        /// </summary>
        static void UjiLogVendor()
        {
            Bagian("httpd.conf tanpa log bawaan vendor");
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-logvendor-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                var folder = Path.Combine(akar, "apache-palsu");
                Directory.CreateDirectory(Path.Combine(folder, "conf", "original"));
                File.WriteAllText(Path.Combine(folder, "conf", "original", "httpd.conf"),
                    "Define SRVROOT \"C:/lama\"\nServerRoot \"C:/lama\"\nListen 80\n"
                    + "ErrorLog \"logs/error.log\"\nErrorLogFormat \"[%t] %M\"\n"
                    + "<IfModule log_config_module>\n"
                    + "    CustomLog \"logs/access.log\" common\n"
                    + "    customlog logs/lain.log combined\n"
                    + "</IfModule>\n"
                    + "TransferLog \"C:/vendor/transfer.log\"\n");
                var apache = new BinPackage
                {
                    Kind = BinKind.Apache,
                    Id = "httpd-2.4.54-win64-VS16",
                    Version = "2.4.54",
                    Path = folder,
                };
                var profil = new Profile { Name = "Uji log vendor", ApacheId = apache.Id, HttpPort = 8080 };
                var logs = Paths.Fwd(Paths.Logs);

                foreach (var logAkses in new[] { true, false })
                {
                    var r = ConfigWriter.Build(profil, null, apache, null, null, new List<Site>(),
                                               logAkses: logAkses);
                    var isi = File.ReadAllText(r.HttpdConf);
                    var aktif = isi.Split('\n')
                        .Select(b => b.TrimEnd('\r').TrimStart())
                        .Where(b => b.StartsWith("CustomLog", StringComparison.OrdinalIgnoreCase)
                                 || b.StartsWith("TransferLog", StringComparison.OrdinalIgnoreCase)
                                 || b.StartsWith("ErrorLog ", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var harap = new List<string> { "ErrorLog \"" + logs + "/apache-error.log\"" };
                    if (logAkses) harap.Add("CustomLog \"" + logs + "/apache-access.log\" common");
                    Ok("Log akses " + (logAkses ? "menyala" : "mati") + ": hanya log Phoron yang aktif",
                       aktif.SequenceEqual(harap), string.Join(" | ", aktif.ToArray()));
                    Ok("Log akses " + (logAkses ? "menyala" : "mati") + ": CustomLog vendor dikomentari",
                       isi.Contains("#     CustomLog \"logs/access.log\" common   # dinonaktifkan Phoron"));
                    Ok("Log akses " + (logAkses ? "menyala" : "mati") + ": ErrorLog vendor dikomentari",
                       isi.Contains("# ErrorLog \"logs/error.log\"   # dinonaktifkan Phoron"));
                    Ok("Log akses " + (logAkses ? "menyala" : "mati") + ": ErrorLogFormat tidak ikut dikomentari",
                       isi.Contains("\r\nErrorLogFormat \"[%t] %M\"\r\n"));
                }
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }
    }
}
