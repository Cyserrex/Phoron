using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Phoron.Core;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Versi PHP per situs: CodeIgniter 2 di PHP 5.6 dan Laravel di PHP 8.3,
        /// dalam satu Apache, tanpa berganti profil.
        ///
        /// Perilaku ujung-ke-ujungnya dibuktikan dengan Apache 2.4.38, mod_php
        /// 5.6, dan php-cgi 8.3 yang sungguhan: dua folder menjawab 5.6.40 dan
        /// 8.3.12 bersamaan, lewat jalur maupun vhost. Di sini yang dijaga adalah
        /// konfigurasi yang dihasilkan - diperiksa httpd.exe dan nginx.exe
        /// sungguhan - dan penyimpanan pilihannya.
        /// </summary>
        static void UjiPhpPerSitus()
        {
            Bagian("Versi PHP per situs");

            // --- Penyimpanan: pulang-pergi lewat berkas profil.
            var p = new Profile { Name = "Uji php situs", FileName = "uji-php-situs" };
            p.PhpPerSitus[@"C:\www\api-bacameter"] = "php-8.3.12-Win32-vs16-x64";
            p.PhpPerSitus[@"D:\kerja\lama"] = "php-5.6.40-Win32-VC11-x64";
            ProfileStore.Save(p);
            var dibaca = ProfileStore.Load(Path.Combine(Paths.Profiles, "uji-php-situs.ini"));
            Ok("Pilihan PHP per situs tersimpan dan terbaca kembali",
               dibaca.PhpPerSitus.Count == 2
               && dibaca.PhpPerSitus[@"C:\www\api-bacameter"] == "php-8.3.12-Win32-vs16-x64"
               && dibaca.PhpPerSitus[@"d:\kerja\lama"] == "php-5.6.40-Win32-VC11-x64",
               string.Join("; ", dibaca.PhpPerSitus.Select(kv => kv.Key + "=" + kv.Value)));
            Ok("Kunci jalur tidak peka huruf besar-kecil", dibaca.PhpPerSitus.ContainsKey(@"c:\WWW\API-BACAMETER"), "");
            Ok("Profil yang digandakan membawa pilihannya", p.Clone().PhpPerSitus.Count == 2, "");
            try { File.Delete(Path.Combine(Paths.Profiles, "uji-php-situs.ini")); } catch { }

            // --- Konfigurasi: butuh PHP TS (mod_php), PHP lain, dan Apache sungguhan.
            var pkgs = Nyata();
            var apache = pkgs.FirstOrDefault(x => x.Kind == BinKind.Apache && x.Version == "2.4.38")
                      ?? pkgs.FirstOrDefault(x => x.Kind == BinKind.Apache);
            var ts = apache == null ? null : pkgs.FirstOrDefault(x => x.Kind == BinKind.Php
                        && !ConfigWriter.PakaiFastCgi(x)
                        && ProfileStore.ArsitekturSepadan(x.Arch, apache.Arch)
                        && string.Equals(x.Compiler, apache.Compiler, StringComparison.OrdinalIgnoreCase));
            var lain = ts == null ? null : pkgs.FirstOrDefault(x => x.Kind == BinKind.Php && x.Id != ts.Id
                        && File.Exists(Path.Combine(x.Path, "php-cgi.exe")));
            if (apache == null || ts == null || lain == null)
            {
                Console.WriteLine("     dilewati: butuh Apache, PHP thread-safe yang cocok, dan satu PHP lain");
                return;
            }

            var www = Path.Combine(Paths.Tmp, "www-php-situs");
            var dirA = Path.Combine(www, "lama");
            var dirB = Path.Combine(www, "baru");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            var situsA = new Site { Folder = "lama", Path = dirA, Root = www, HostName = "lama.test" };
            var situsB = new Site { Folder = "baru", Path = dirB, Root = www, HostName = "baru.test" };

            var profil = new Profile
            {
                Name = "Uji php situs",
                PhpId = ts.Id,
                ApacheId = apache.Id,
                HttpPort = 8080,
                HttpsPort = 8443,
                PhpExtensions = new List<string> { "mbstring" },
            };
            profil.ProjectRoots.Add(www);

            // Tanpa penimpaan: tidak ada kolam, tidak ada modul proxy tambahan.
            var polos = ConfigWriter.Build(profil, ts, apache, null, null, new List<Site> { situsA, situsB });
            var httpdPolos = File.ReadAllText(polos.HttpdConf);
            var modPolos = File.ReadAllText(Path.Combine(Paths.EtcApache, "mod_php.conf"));
            Ok("Tanpa penimpaan: tidak ada kolam", polos.Kolam.Count == 0, polos.Kolam.Count + " kolam");
            Ok("Tanpa penimpaan: mod_proxy_fcgi tidak dimuat", !ModulAktif(httpdPolos, "proxy_fcgi"),
               "ongkos tambahan bagi yang tidak memakai fiturnya");
            Ok("Tanpa penimpaan: tidak ada blok FastCGI di mod_php.conf", !modPolos.Contains("proxy:fcgi"), "");

            // Dengan penimpaan: satu kolam untuk situs B.
            var kolam = new List<ConfigWriter.Kolam>
            {
                new ConfigWriter.Kolam
                {
                    Php = lain,
                    Ekstensi = new List<string> { "curl" },
                    Situs = new List<Site> { situsB },
                },
            };
            var r = ConfigWriter.Build(profil, ts, apache, null, null, new List<Site> { situsA, situsB },
                                       false, false, true, true, kolam);
            var httpd = File.ReadAllText(r.HttpdConf);
            var mod = File.ReadAllText(Path.Combine(Paths.EtcApache, "mod_php.conf"));

            Ok("Satu kolam jadi, di port sesudah php-cgi profil",
               r.Kolam.Count == 1 && r.Kolam[0].Port == r.FastCgiPort + 1,
               r.Kolam.Count + " kolam, port " + (r.Kolam.Count > 0 ? r.Kolam[0].Port : 0));
            Ok("PHP profil tetap mod_php", mod.Contains("LoadModule ") && !r.PhpFastCgi, "");
            Ok("mod_proxy_fcgi dimuat untuk kolam", ModulAktif(httpd, "proxy_fcgi"),
               "direktif SetHandler proxy:fcgi tidak akan dikenali");
            Ok("Folder situs B diarahkan ke kolamnya",
               mod.Contains("<Directory \"" + Paths.Fwd(dirB) + "\">")
               && mod.Contains("SetHandler \"proxy:fcgi://127.0.0.1:" + (r.FastCgiPort + 1) + "/\""),
               mod);
            Ok("Folder situs A TIDAK disentuh", !mod.Contains(Paths.Fwd(dirA)), "");
            Ok("Header Authorization diteruskan (CGIPassAuth On)", mod.Contains("CGIPassAuth On"),
               "API ber-token Bearer akan menolak setiap permintaan");
            Ok("SCRIPT_FILENAME dikupas untuk port kolam juga", mod.Contains(ConfigWriter.SetelNamaBerkasFcgi()), "");

            // php.ini kolam memakai daftar ekstensinya SENDIRI, dan tidak mencemari
            // profil: AdoptedExtensions profil tidak boleh berisi apa pun dari kolam.
            var iniKolam = r.Kolam.Count > 0 ? Path.Combine(r.Kolam[0].FolderIni ?? "", "php.ini") : "";
            Ok("php.ini kolam ditulis", File.Exists(iniKolam), iniKolam);
            if (File.Exists(iniKolam))
            {
                var isiKolam = File.ReadAllText(iniKolam);
                Ok("php.ini kolam memuat ekstensi milik kolam",
                   Regex.IsMatch(isiKolam, @"(?m)^\s*extension\s*=\s*(php_)?curl"), "");
                Ok("php.ini kolam TIDAK memuat ekstensi milik profil",
                   !Regex.IsMatch(isiKolam, @"(?m)^\s*extension\s*=\s*(php_)?mbstring"),
                   "daftar ekstensi profil 5.6 bocor ke PHP versi lain");
            }
            // Pengambilalihan hanya terjadi bila daftar ekstensi KOSONG - jadi kolam
            // di sini sengaja tanpa daftar. Kalau php.ini kolam ditulis dengan
            // Result milik profil, daftar yang diambil alih dari php.ini dasar PHP
            // versi lain akan disimpan Engine ke profil aktif.
            var rKosong = ConfigWriter.Build(profil, ts, apache, null, null, new List<Site> { situsA, situsB },
                                             false, false, true, true, new List<ConfigWriter.Kolam>
                                             {
                                                 new ConfigWriter.Kolam
                                                 {
                                                     Php = lain,
                                                     Ekstensi = new List<string>(),
                                                     Situs = new List<Site> { situsB },
                                                 },
                                             });
            Ok("Kolam tanpa daftar ekstensi tidak mencemari profil (AdoptedExtensions)",
               rKosong.AdoptedExtensions == null,
               "ekstensi PHP " + lain.Version + " akan disimpan ke profil PHP " + ts.Version + ": "
               + (rKosong.AdoptedExtensions != null ? string.Join(",", rKosong.AdoptedExtensions) : ""));

            // Hakim sesungguhnya: httpd.exe.
            if (File.Exists(apache.MainExe ?? ""))
            {
                var uji = Shell.Run(apache.MainExe, "-t -f \"" + r.HttpdConf + "\" -d \"" + apache.Path + "\"",
                                    apache.Path, 30000);
                Ok("httpd.exe -t menerima mod_php dan kolam FastCGI bersamaan",
                   uji.All.IndexOf("Syntax OK", StringComparison.OrdinalIgnoreCase) >= 0, uji.All);
            }

            // Apache lama: fiturnya dimatikan dengan keluhan, bukan Apache yang
            // menolak start.
            var tua = new BinPackage
            {
                Kind = BinKind.Apache, Id = "httpd-2.4.9-uji", Path = apache.Path,
                Version = "2.4.9", Arch = apache.Arch, Compiler = apache.Compiler, MainExe = apache.MainExe,
            };
            var rTua = ConfigWriter.Build(profil, ts, tua, null, null, new List<Site>(),
                                          false, false, true, true, kolam);
            Ok("Apache lama: tidak ada kolam", rTua.Kolam.Count == 0, "");
            Ok("Apache lama: orangnya diberi tahu",
               rTua.Warnings.Any(w => w.IndexOf("per situs", StringComparison.OrdinalIgnoreCase) >= 0),
               string.Join(" | ", rTua.Warnings));

            // Nginx: situs B lewat jalur diarahkan ke kolamnya.
            var nginx = pkgs.FirstOrDefault(x => x.Kind == BinKind.Nginx);
            if (nginx != null)
            {
                var pn = profil.Clone();
                pn.WebServer = "nginx";
                pn.NginxId = nginx.Id;
                var rn = ConfigWriter.Build(pn, lain, null, null, nginx, new List<Site>(),
                                            false, false, true, true, new List<ConfigWriter.Kolam>
                                            {
                                                new ConfigWriter.Kolam
                                                {
                                                    Php = ts, Ekstensi = new List<string> { "mbstring" },
                                                    Situs = new List<Site> { situsB },
                                                },
                                            });
                var isiN = File.ReadAllText(rn.NginxConf);
                Ok("Nginx: situs lewat jalur diarahkan ke kolamnya",
                   isiN.Contains("location ^~ /baru/") && isiN.Contains("fastcgi_pass   127.0.0.1:" + (rn.FastCgiPort + 1) + ";"),
                   "tidak ada location untuk /baru/");
                var ujiN = Shell.Run(nginx.MainExe, "-t -c \"" + rn.NginxConf + "\" -p \"" + nginx.Path + "\"",
                                     nginx.Path, 30000);
                Ok("nginx.exe -t menerima kolam", ujiN.All.IndexOf("test is successful", StringComparison.OrdinalIgnoreCase) >= 0,
                   ujiN.All);
            }

            try { Directory.Delete(www, true); } catch { }

            UjiInstantClientKolam(lain);
        }

        /// <summary>
        /// Instant Client yang dipilih untuk kolam: versinya harus cukup untuk
        /// oci8 kolam itu. Bergantung pada client yang terpasang di mesin, jadi
        /// dilewati bila tidak ada.
        /// </summary>
        static void UjiInstantClientKolam(BinPackage php)
        {
            var ic19 = @"C:\instantclient_19_24";
            if (php == null || !File.Exists(Path.Combine(ic19, "oci.dll")) || php.Arch != "x64")
            {
                Console.WriteLine("     dilewati: Instant Client 19 x64 tidak ada di mesin ini");
                return;
            }
            Ok("oci8_19 memilih Instant Client 19",
               string.Equals(Oracle.FolderUntuk(php, new[] { "oci8_19" }, ic19), ic19, StringComparison.OrdinalIgnoreCase), "");
            Ok("Tanpa oci8 tidak ada yang diatur", Oracle.FolderUntuk(php, new[] { "mbstring" }, ic19) == null, "");
            var ic12 = @"C:\app\instantclient_12_1";
            if (File.Exists(Path.Combine(ic12, "oci.dll")))
                Ok("Client yang tidak sepadan arsitekturnya dilewati, walau ada di depan PATH",
                   string.Equals(Oracle.FolderUntuk(php, new[] { "oci8_19" }, ic12 + ";" + ic19), ic19,
                                 StringComparison.OrdinalIgnoreCase), "");
        }
    }
}
