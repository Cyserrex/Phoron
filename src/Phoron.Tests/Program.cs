using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Phoron.Core;

namespace Phoron.Tests
{
    /// <summary>
    /// Harness uji tanpa kerangka kerja pihak ketiga. Uji yang penting di sini
    /// bukan uji unit murni: yang benar-benar ingin dipastikan adalah httpd.exe
    /// SUNGGUHAN menerima konfigurasi yang dihasilkan Phoron, dan php.exe
    /// SUNGGUHAN memuat php.ini yang ditulis Phoron. Menirunya dengan mock hanya
    /// akan menguji tiruan itu sendiri.
    /// </summary>
    public static partial class Program
    {
        static int _lulus, _gagal;
        static readonly List<string> _kegagalan = new List<string>();

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("live", StringComparison.OrdinalIgnoreCase))
                return LiveTest.Jalankan();

            // Dipanggil oleh UjiObjekAntarProses: harness ini menjalankan dirinya
            // sendiri dengan hak Low, dan anak itu melapor lewat kode keluar.
            if (args.Length > 0 && args[0] == ArgumenBukaEvent)
                return CobaBukaEvent(args);

            // Semua uji jalan di folder sementara supaya instalasi Phoron milik
            // pengguna tidak pernah tersentuh.
            var sandbox = Path.Combine(Path.GetTempPath(), "phoron-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sandbox);
            Paths.Root = sandbox;
            Console.WriteLine("Sandbox: " + sandbox);
            Console.WriteLine();

            try
            {
                UjiIni();
                UjiPenulisanAtomik();
                UjiIniRusak();
                UjiSetelanRusak();
                UjiLaporanGalat();
                UjiPemindai();
                UjiProfil();
                UjiVersiTidakDipakai();
                UjiLayananTidakDipakai();
                UjiVirtualHostMati();
                UjiSitus();
                UjiBanyakFolderProyek();
                UjiPhpIni();
                UjiDaftarEkstensiTersedia();
                UjiWarisanPhpIni();
                UjiPhpIniKeFolderPhp();
                UjiBeranda();
                UjiHalamanSambutan();
                UjiKonfigurasiApache();
                UjiHosts();
                UjiHostsAman();
                UjiPortCheck();
                UjiNodeApps();
                UjiPembaruan();
                UjiAutostart();
                UjiLabelVersi();
                UjiBahasa();
                UjiBahasaPemasang();
                UjiBahasaDariPemasang();
                UjiHostsTool();
                UjiTataLetakBin();
                UjiOracle();
                UjiRuntimeVc();
                UjiKonfigurasiNginx();
                UjiLogWarna();
                UjiKeluaranNode();
                UjiRiwayatLog();
                UjiPutaranLog();
                UjiBalapLayanan();
                UjiUmpanAtom();
                UjiSalinanKedua();
                UjiObjekAntarProses();
                UjiOpcache();
                UjiXdebug();
                UjiPanelLog();
                UjiFastCgi();
                UjiHostsTanpaUbah();
                UjiLayananBertabrakan();
                UjiHsts();
                UjiSqlBerbaris();
                UjiSqlUrai();
                UjiSqlLepasLolos();
                UjiSqlKutip();
                UjiRahasia();
                UjiSetelanBasisData(sandbox);
                UjiSambunganBasisData(sandbox);
                UjiKamusBanjar();
                UjiArgumenHeidi();
                UjiPaketHeidi();
                UjiTanpaAksaraKendali();
                UjiLanggananHalamanBasisData();
                UjiLabelTidakBentrok();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Harness meledak: " + ex);
                _gagal++;
            }
            finally
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("=== " + _lulus + " lulus, " + _gagal + " gagal ===");
            foreach (var f in _kegagalan) Console.WriteLine("  GAGAL: " + f);
            return _gagal == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------ Utilitas

        static void Ok(string nama, bool syarat, string keterangan = "")
        {
            if (syarat) { _lulus++; Console.WriteLine("  ok    " + nama); }
            else
            {
                _gagal++;
                _kegagalan.Add(nama + (keterangan.Length > 0 ? " - " + keterangan : ""));
                Console.WriteLine("  GAGAL " + nama + (keterangan.Length > 0 ? " - " + keterangan : ""));
            }
        }

        static void Bagian(string judul)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + judul);
        }

        /// <summary>Paket asli dari folder bin yang ada di mesin ini; uji yang butuh biner dilewati bila kosong.</summary>
        static List<BinPackage> Nyata()
        {
            return BinScanner.ScanAll(Settings.DefaultBinRoots()
                .Concat(new[] { @"C:\laragon\bin" }).Distinct());
        }

        // ---------------------------------------------------------------- Uji

        static void UjiPenulisanAtomik()
        {
            Bagian("Penulisan atomik");
            var dir = Path.Combine(Paths.Tmp, "atomik");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "coba.txt");

            // Tujuan yang BELUM ada adalah jebakan File.Replace: ia melempar
            // FileNotFoundException kalau berkas tujuannya tidak ada. Cabang ini
            // terpakai terus - tiap profil baru, tiap vhost, dan phoron.ini pada
            // jalan pertama.
            AtomicFile.WriteAllText(p, "pertama");
            Ok("Tulis atomik membuat berkas yang belum ada",
               File.Exists(p) && File.ReadAllText(p) == "pertama");

            AtomicFile.WriteAllText(p, "kedua");
            Ok("Tulis kedua menimpa isi lama", File.ReadAllText(p) == "kedua");

            var sisa = Directory.GetFiles(dir, "*.ptmp*");
            Ok("Berkas sementara tidak tertinggal", sisa.Length == 0,
               string.Join(", ", sisa.Select(Path.GetFileName).ToArray()));

            // Berkas hosts Windows ber-atribut ReadOnly. Cara lama menolak
            // berkas seperti itu - dibuktikan di bawah, bukan diandaikan.
            var pRo = Path.Combine(dir, "readonly.txt");
            AtomicFile.WriteAllText(pRo, "awal");
            File.SetAttributes(pRo, FileAttributes.ReadOnly);
            var caraLamaMenolak = false;
            try { File.WriteAllText(pRo, "lewat cara lama"); }
            catch (UnauthorizedAccessException) { caraLamaMenolak = true; }
            Ok("File.WriteAllText memang menolak berkas ReadOnly", caraLamaMenolak);

            AtomicFile.WriteAllText(pRo, "sesudah");
            Ok("Tulis atomik tetap berhasil pada berkas ReadOnly",
               File.ReadAllText(pRo) == "sesudah");
            Ok("Atribut ReadOnly dikembalikan seperti semula",
               (File.GetAttributes(pRo) & FileAttributes.ReadOnly) != 0,
               File.GetAttributes(pRo).ToString());
            File.SetAttributes(pRo, FileAttributes.Normal);

            // Isi besar: memastikan penukarannya bukan cuma bekerja untuk berkas
            // mungil yang muat dalam satu blok.
            var besar = new string('x', 500 * 1024);
            AtomicFile.WriteAllText(p, besar);
            Ok("Isi besar tertulis utuh",
               new FileInfo(p).Length == besar.Length && File.ReadAllText(p) == besar,
               new FileInfo(p).Length.ToString());

            var baris = new[] { "satu", "dua", "tiga" };
            var pBaris = Path.Combine(dir, "baris.txt");
            AtomicFile.WriteAllLines(pBaris, baris);
            Ok("WriteAllLines menghasilkan baris yang sama dengan cara lama",
               File.ReadAllLines(pBaris).SequenceEqual(baris));

            // Karantina: berkas cacat DIPINDAH, bukan dihapus - isinya harus
            // masih bisa dilihat pengguna sesudahnya.
            var pRusak = Path.Combine(dir, "rusak.ini");
            File.WriteAllText(pRusak, "isi yang mau diselamatkan");
            var karantina = AtomicFile.Karantina(pRusak, "rusak");
            Ok("Karantina memindahkan berkasnya",
               karantina != null && !File.Exists(pRusak) && File.Exists(karantina));
            Ok("Isi berkas yang dikarantina tidak hilang",
               karantina != null && File.ReadAllText(karantina) == "isi yang mau diselamatkan");
            Ok("Karantina berkas yang tidak ada mengembalikan null",
               AtomicFile.Karantina(Path.Combine(dir, "hantu.ini"), "rusak") == null);
        }

        static void UjiLaporanGalat()
        {
            Bagian("Laporan galat");
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-crash-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;

                var jalur = Crash.Tulis(new InvalidOperationException("contoh kegagalan"),
                                        "sedang menguji");
                Ok("Laporan galat tertulis", jalur != null && File.Exists(jalur), jalur ?? "(null)");

                var isi = jalur != null ? File.ReadAllText(jalur) : "";
                Ok("Laporan memuat pesan galatnya", isi.Contains("contoh kegagalan"));
                Ok("Laporan memuat konteksnya", isi.Contains("sedang menguji"));
                Ok("Laporan memuat versi Phoron", isi.Contains(AppInfo.Version));

                // Yang paling menjelaskan biasanya galat yang paling DALAM, jadi
                // seluruh rantainya harus ikut tercatat - bukan lapisan luarnya
                // saja, yang sering cuma berbunyi "operasi gagal".
                var berlapis = new Exception("lapisan luar",
                    new IOException("lapisan dalam yang sebenarnya menjelaskan"));
                var isiBerlapis = Crash.Susun(berlapis, "uji");
                Ok("Galat berlapis tercatat sampai ke dalam",
                   isiBerlapis.Contains("lapisan luar")
                   && isiBerlapis.Contains("lapisan dalam yang sebenarnya menjelaskan"));

                var kumpulan = new AggregateException(
                    new Exception("kembar satu"), new Exception("kembar dua"));
                var isiKumpulan = Crash.Susun(kumpulan, "uji");
                Ok("Galat berkumpul tidak menyembunyikan salah satunya",
                   isiKumpulan.Contains("kembar satu") && isiKumpulan.Contains("kembar dua"));

                // Pelapor ini dipanggil DARI penangan galat terakhir. Pelapor yang
                // ikut meledak adalah cara paling umum membuat lingkaran tak
                // berujung, jadi ia harus tahan terhadap masukan yang aneh.
                Ok("Exception null tidak membuat pelapor meledak",
                   Crash.Tulis(null, "uji") != null);
                Ok("Konteks null tidak membuat pelapor meledak",
                   Crash.Tulis(new Exception("x"), null) != null);

                // Bagian yang paling menolong saat menelusuri: apa yang sedang
                // dikerjakan Phoron tepat sebelum semuanya berantakan.
                var riwayat = new List<BarisLog>
                {
                    new BarisLog { Waktu = DateTime.Now, Teks = "Apache jalan di port 80." },
                    new BarisLog { Waktu = DateTime.Now, Teks = "Konfigurasi Nginx ditolak:" },
                };
                var isiRiwayat = Crash.Susun(new Exception("x"), "uji", riwayat);
                Ok("Riwayat Aktivitas ikut dilampirkan",
                   isiRiwayat.Contains("Apache jalan di port 80.")
                   && isiRiwayat.Contains("Konfigurasi Nginx ditolak:"));

                for (int i = 0; i < Crash.LaporanMaks + 6; i++)
                    Crash.Tulis(new Exception("ke-" + i), "banyak");
                var jumlah = Directory.GetFiles(Paths.Logs, "crash-*.log").Length;
                Ok("Laporan lama dibuang di atas batas",
                   jumlah <= Crash.LaporanMaks, jumlah.ToString());

                UjiPemasanganKait();
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        /// <summary>
        /// Urutan pemasangan kait galat, diperiksa di SUMBERNYA.
        ///
        /// Harness ini tidak mereferensi WPF - dan sebaiknya tetap begitu - jadi
        /// kait dispatcher tidak bisa dibuktikan dari dalam sini. Yang bisa
        /// dijaga adalah hal yang paling gampang tergeser tanpa ketahuan:
        /// urutannya. Kait yang dipasang sesudah Engine dibuat tidak berguna
        /// untuk kegagalan yang paling mungkin terjadi saat start.
        /// </summary>
        static void UjiPemasanganKait()
        {
            var dirApp = CariFolderApp();
            if (dirApp == null)
            {
                Ok("Folder sumber Phoron.App ditemukan", false,
                   "tidak ketemu dari " + AppDomain.CurrentDomain.BaseDirectory);
                return;
            }

            var program = File.ReadAllText(Path.Combine(dirApp, "Program.cs"));
            var iKait = program.IndexOf("App.PasangPenangkapGalat()", StringComparison.Ordinal);
            var iRakit = program.IndexOf("EmbeddedAssemblies.Install()", StringComparison.Ordinal);
            var iEngine = program.IndexOf("new Phoron.Core.Engine()", StringComparison.Ordinal);

            Ok("Kait galat dipasang di Program.Main", iKait >= 0);
            Ok("Kait galat dipasang sebelum apa pun yang bisa gagal",
               iKait >= 0 && iRakit > iKait && iEngine > iKait,
               "kait=" + iKait + " rakit=" + iRakit + " engine=" + iEngine);
            Ok("Engine dibuat di dalam try saat start", iEngine >= 0
               && program.IndexOf("try { engine = new Phoron.Core.Engine(); }",
                                  StringComparison.Ordinal) >= 0);

            // Penginisialisasi medan jalan sebelum badan konstruktor mana pun,
            // jadi tidak ada satu tempat pun yang bisa menangkapnya. Inilah yang
            // membuat phoron.ini terkunci berujung pada kematian saat start.
            var jendela = File.ReadAllText(Path.Combine(dirApp, "MainWindow.xaml.cs"));
            Ok("Engine tidak lagi dibuat sebagai penginisialisasi medan",
               !jendela.Contains("readonly Engine _engine = new Engine()"));
            Ok("MainWindow menerima Engine dari luar",
               jendela.Contains("public MainWindow(Engine engine)"));
        }

        static void UjiPutaranLog()
        {
            Bagian("Putaran berkas log");
            var dir = Path.Combine(Paths.Tmp, "putaran");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "phoron.log");
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);

            // Satu baris panjang supaya batasnya cepat terlampaui tanpa menulis
            // ratusan ribu kali.
            var gemuk = new string('x', 64 * 1024);
            var perluBaris = (int)(LogFile.BatasBytes / gemuk.Length) + 2;
            for (int i = 0; i < perluBaris; i++) LogFile.Tambah(p, gemuk);

            Ok("Log berputar setelah melewati batas", File.Exists(p + ".1"));
            Ok("Berkas aktif menyusut sesudah berputar",
               new FileInfo(p).Length < LogFile.BatasBytes,
               new FileInfo(p).Length.ToString());

            // Baris yang tergeser harus PINDAH, bukan hilang.
            LogFile.Tambah(p, "penanda sebelum putaran");
            LogFile.Putar(p);
            LogFile.Tambah(p, "penanda sesudah putaran");
            Ok("Baris terbaru ada di berkas aktif",
               File.ReadAllText(p).Contains("penanda sesudah putaran"));
            Ok("Baris lama pindah ke .1, bukan hilang",
               File.ReadAllText(p + ".1").Contains("penanda sebelum putaran"));

            // Tiap putaran didahului penulisan, seperti di kenyataan: Putar
            // hanya dipanggil dari Tambah, jadi log yang diam tidak pernah
            // berputar. Memutar berkali-kali tanpa menulis justru menghabiskan
            // seluruh generasi - benar, tapi bukan keadaan yang pernah terjadi.
            for (int i = 0; i < 6; i++)
            {
                LogFile.Tambah(p, "isi generasi " + i);
                LogFile.Putar(p);
            }
            var lama = Directory.GetFiles(dir, "phoron.log.*").Length;
            Ok("Hanya tiga berkas lama disimpan", lama == LogFile.SimpanLama, lama.ToString());
            Ok("Generasi terbaru ada di .1",
               File.ReadAllText(p + ".1").Contains("isi generasi 5"));
            Ok("Generasi terlama sudah dibuang",
               !File.ReadAllText(p + ".3").Contains("isi generasi 0"));

            // ---------------- inilah yang membuktikan kerusakannya ----------------
            // Engine.Say dipanggil dari utas layar DAN dari utas kolam - pengawas
            // Exited dan pembaca keluaran httpd/mysqld semuanya berujung ke sana.
            // Dengan File.AppendAllText telanjang, dua penulis berbarengan membuat
            // salah satunya melempar IOException, yang lalu ditelan try/catch
            // kosong: barisnya lenyap tanpa bekas.
            var pRamai = Path.Combine(dir, "ramai.log");
            try { File.Delete(pRamai); } catch { }
            const int utas = 8, perUtas = 200;
            var daftarUtas = new List<System.Threading.Thread>();
            for (int u = 0; u < utas; u++)
            {
                var nomor = u;
                var t = new System.Threading.Thread(() =>
                {
                    for (int i = 0; i < perUtas; i++)
                        LogFile.Tambah(pRamai, "utas " + nomor + " baris " + i);
                });
                daftarUtas.Add(t);
            }
            foreach (var t in daftarUtas) t.Start();
            foreach (var t in daftarUtas) t.Join();

            var baris = File.ReadAllLines(pRamai).Where(x => x.Length > 0).ToList();
            Ok("Penulisan dari banyak utas tidak menghilangkan baris",
               baris.Count == utas * perUtas,
               baris.Count + " dari " + (utas * perUtas));
            Ok("Tidak ada baris yang tercampur setengah-setengah",
               baris.All(x => x.StartsWith("utas ") && x.Contains(" baris ")),
               baris.FirstOrDefault(x => !x.StartsWith("utas ")) ?? "");

            // Halaman Log membaca berkas yang sedang dipegang Apache/MySQL, jadi
            // penulisnya tidak boleh mengunci eksklusif.
            using (new FileStream(pRamai, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                LogFile.Tambah(pRamai, "ditulis sambil dibaca");
            }
            Ok("Berkas yang sedang dibaca tetap bisa ditulisi",
               File.ReadAllText(pRamai).Contains("ditulis sambil dibaca"));
        }

        static void UjiIniRusak()
        {
            Bagian("INI yang cacat dilaporkan");
            var dir = Path.Combine(Paths.Tmp, "ini-rusak");
            Directory.CreateDirectory(dir);

            var bersih = Path.Combine(dir, "bersih.ini");
            File.WriteAllText(bersih, "[umum]" + Environment.NewLine + "nama=Phoron" + Environment.NewLine);
            var hBersih = Ini.Baca(bersih);
            Ok("Berkas bersih tidak dituduh rusak", !hBersih.Rusak && hBersih.Ada);
            Ok("Berkas bersih tetap terbaca isinya", hBersih.Isi.Get("umum", "nama") == "Phoron");

            // Bedanya "tidak ada" dengan "ada tapi cacat" adalah inti persoalan
            // di sini: yang pertama wajar, yang kedua tidak boleh ditimpa.
            var hilang = Ini.Baca(Path.Combine(dir, "tidak-ada.ini"));
            Ok("Berkas yang tidak ada bukan berkas rusak", !hilang.Ada && !hilang.Rusak);
            Ok("Ini.Load lama tetap mengembalikan kosong untuk berkas hilang",
               !Ini.Load(Path.Combine(dir, "tidak-ada.ini")).Sections.Any());

            var tanpaSama = Path.Combine(dir, "tanpa-sama.ini");
            File.WriteAllText(tanpaSama, "[umum]" + Environment.NewLine + "nama=Phoron"
                              + Environment.NewLine + "baris sampah tanpa apa pun" + Environment.NewLine);
            var h1 = Ini.Baca(tanpaSama);
            Ok("Baris tanpa tanda sama dengan dilaporkan", h1.Rusak, string.Join("; ", h1.Keluhan));
            Ok("Baris lain tetap terbaca walau ada yang cacat",
               h1.Isi.Get("umum", "nama") == "Phoron");

            var seksiTerbuka = Path.Combine(dir, "seksi.ini");
            File.WriteAllText(seksiTerbuka, "[umum" + Environment.NewLine + "nama=Phoron" + Environment.NewLine);
            Ok("Seksi tanpa kurung tutup dilaporkan", Ini.Baca(seksiTerbuka).Rusak);

            // Tanda khas berkas yang terpotong saat listrik mati.
            var terpotong = Path.Combine(dir, "terpotong.ini");
            File.WriteAllText(terpotong, "[umum]" + Environment.NewLine + "bahasa=bjn"
                              + Environment.NewLine + "nama=Pho" + new string('\0', 200));
            var h2 = Ini.Baca(terpotong);
            Ok("Byte NUL dikenali sebagai berkas terpotong",
               h2.Rusak && h2.Keluhan.Any(k => k.Contains("NUL")),
               string.Join("; ", h2.Keluhan));
            // Melaporkan saja tidak cukup: NUL terbaca sebagai bagian dari NILAI,
            // dan tanpa dibersihkan ia ikut tertulis ke berkas hasil penyelamatan
            // - yang lalu terbaca cacat lagi, selamanya.
            Ok("Byte NUL tidak ikut jadi bagian nilai",
               h2.Isi.Get("umum", "nama") == "Pho",
               "[" + h2.Isi.Get("umum", "nama") + "]");
            Ok("Nilai sebelum bagian yang terpotong tetap utuh",
               h2.Isi.Get("umum", "bahasa") == "bjn");
        }

        static void UjiSetelanRusak()
        {
            Bagian("Setelan yang cacat dikarantina");
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-setelan-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                Settings.KeluhanTerakhir = "";

                // phoron.ini yang terpotong, tapi bahasa=bjn MASIH terbaca di
                // dalamnya. Inilah yang dulu hilang selamanya: Load menghasilkan
                // Settings yang tampak sah, lalu Save() berikutnya - yang terjadi
                // sendiri saat mengecek pembaruan - menuliskan bawaan kembali ke
                // berkas, menimpa baris yang sebenarnya masih selamat.
                var berkas = Paths.SettingsFile;
                File.WriteAllText(berkas,
                    "[umum]" + Environment.NewLine +
                    "bahasa=bjn" + Environment.NewLine +
                    "terminal=powershell" + Environment.NewLine +
                    "tema=gel" + new string('\0', 120));

                var hasil = Settings.Muat();
                Ok("Setelan cacat dikenali sebagai cacat",
                   hasil.Asal == Settings.Sumber.Rusak, hasil.Asal.ToString());
                Ok("Nilai yang masih terbaca tetap terpakai",
                   hasil.Setelan.Bahasa == "bjn" && hasil.Setelan.Terminal == "powershell",
                   hasil.Setelan.Bahasa + " / " + hasil.Setelan.Terminal);

                hasil.Setelan.Save();

                var karantina = Directory.GetFiles(akar, "phoron.ini.rusak-*");
                Ok("Berkas cacat dikarantina sebelum ditimpa", karantina.Length == 1,
                   karantina.Length.ToString());
                Ok("Isi lama masih bisa dibaca dari karantina",
                   karantina.Length == 1 && File.ReadAllText(karantina[0]).Contains("bahasa=bjn"));
                Ok("Keluhannya disiapkan untuk ditampilkan",
                   Settings.KeluhanTerakhir.Contains("phoron.ini"), Settings.KeluhanTerakhir);
                Ok("Setelan baru tetap tertulis", File.Exists(berkas)
                   && File.ReadAllText(berkas).Contains("bahasa=bjn"));

                // Karantina sekali saja, bukan tiap kali menyimpan.
                hasil.Setelan.Save();
                Ok("Penyimpanan berikutnya tidak mengarantina lagi",
                   Directory.GetFiles(akar, "phoron.ini.rusak-*").Length == 1);

                // Berkas yang sehat tidak boleh ikut dikarantina.
                Settings.KeluhanTerakhir = "";
                var sehat = Settings.Muat();
                Ok("Berkas yang sehat dibaca sebagai sehat",
                   sehat.Asal == Settings.Sumber.Terbaca, sehat.Asal.ToString());
                sehat.Setelan.Save();
                Ok("Berkas yang sehat tidak dikarantina",
                   Directory.GetFiles(akar, "phoron.ini.rusak-*").Length == 1);

                // Profil yang rusak: dilewati, tapi dilaporkan dan tidak dihapus.
                Directory.CreateDirectory(Paths.Profiles);
                var pRusak = Path.Combine(Paths.Profiles, "rusak.ini");
                File.WriteAllText(pRusak, "[profil]" + Environment.NewLine
                                  + "nama=Rusak" + Environment.NewLine + "baris sampah");
                var keluhan = new List<string>();
                ProfileStore.LoadAll(keluhan);
                Ok("Profil cacat dilaporkan, bukan dilewati diam-diam",
                   keluhan.Count >= 1, string.Join(" | ", keluhan.ToArray()));
                Ok("Profil cacat tidak ikut hilang dari disk", File.Exists(pRusak));
            }
            finally
            {
                Paths.Root = akarLama;
                Settings.KeluhanTerakhir = "";
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiBalapLayanan()
        {
            Bagian("Perlombaan rujukan layanan");
            // Dua pihak berebut rujukan proses yang sama: StopWebAsync di utas
            // layar, dan ProsesMati di utas kolam lewat Process.Exited. Dulu
            // keduanya membandingkan lalu mengosongkan sebagai dua langkah
            // terpisah, jadi keduanya bisa sama-sama menang - dan penghentian
            // yang DISENGAJA pengguna ikut dilaporkan sebagai "berhenti sendiri"
            // lalu statusnya berubah jadi Gagal.
            //
            // Process yang tidak pernah dijalankan sudah cukup: yang diuji
            // rujukannya, bukan prosesnya.
            var sm = new ServiceManager();
            var proses = new System.Diagnostics.Process();
            var lain = new System.Diagnostics.Process();

            sm.PasangWeb(proses);
            Ok("Melepas proses yang bukan miliknya tidak mengubah apa pun",
               !sm.LepasWebJika(lain) && sm.LepasWebJika(proses));

            sm.PasangWeb(proses);
            Ok("Ambil-lepas mengembalikan prosesnya lalu mengosongkan",
               ReferenceEquals(sm.AmbilLepasWeb(), proses) && sm.AmbilLepasWeb() == null);

            const int putaran = 1000;
            var menangGanda = 0;
            var takAdaYangMenang = 0;
            for (int i = 0; i < putaran; i++)
            {
                sm.PasangWeb(proses);
                var hasil = new bool[2];
                var pintu = new System.Threading.Barrier(2);
                var utas = new System.Threading.Thread[2];
                for (int u = 0; u < 2; u++)
                {
                    var nomor = u;
                    utas[u] = new System.Threading.Thread(() =>
                    {
                        pintu.SignalAndWait();          // berangkat berbarengan
                        hasil[nomor] = sm.LepasWebJika(proses);
                    });
                    utas[u].Start();
                }
                foreach (var t in utas) t.Join();

                var menang = (hasil[0] ? 1 : 0) + (hasil[1] ? 1 : 0);
                if (menang > 1) menangGanda++;
                if (menang == 0) takAdaYangMenang++;
            }
            Ok("Hanya satu pihak yang bisa melepas rujukan yang sama",
               menangGanda == 0, menangGanda + " dari " + putaran + " putaran menang ganda");
            Ok("Selalu ada tepat satu yang menang",
               takAdaYangMenang == 0, takAdaYangMenang + " putaran tanpa pemenang");

            // StopAll harus mengosongkan ketiganya, bukan sebagian.
            sm.PasangWeb(proses);
            sm.StopAll();
            Ok("StopAll mengosongkan rujukan web", sm.AmbilLepasWeb() == null);
            Ok("StopAll menyetel keadaan jadi berhenti",
               sm.WebState == ServiceState.Berhenti && sm.DbState == ServiceState.Berhenti);

            proses.Dispose();
            lain.Dispose();
        }

        /// <summary>
        /// Pemasang dan aplikasi harus sepakat soal bahasa.
        ///
        /// Kode bahasa di [Languages] dipakai apa adanya sebagai nilai "bahasa"
        /// di phoron.ini. Satu huruf beda - "jw" bukan "jv" - membuat Phoron
        /// menganggapnya tidak sah lalu diam-diam jatuh ke Indonesia, dan orang
        /// yang baru saja memilih Basa Jawa di pemasang tidak pernah tahu
        /// kenapa pilihannya diabaikan.
        ///
        /// Diperiksa di sumber .iss karena harness ini tidak merakit installer;
        /// Inno Setup pun hanya ada di CI.
        /// </summary>
        static void UjiBahasaPemasang()
        {
            Bagian("Bahasa pemasang");
            var iss = CariBerkasPemasang();
            if (iss == null)
            {
                Ok("Berkas installer\\setup.iss ditemukan", false,
                   "tidak ketemu dari " + AppDomain.CurrentDomain.BaseDirectory);
                return;
            }
            var teks = File.ReadAllText(iss);

            // Kerusakan yang pernah benar-benar terjadi: karakter TAB sungguhan
            // tertulis di tempat "\t" seharusnya berada, sehingga jalur
            // {sys}\taskkill.exe berubah jadi berkas yang tidak pernah ada -
            // dan jalur paksa penutupan Phoron diam-diam tidak berbuat apa-apa
            // selama beberapa rilis. Tidak ada TAB yang sah di berkas ini.
            Ok("Tidak ada karakter TAB di skrip pemasang",
               teks.IndexOf('\t') < 0,
               "TAB pertama di indeks " + teks.IndexOf('\t'));

            var blokBahasa = Blok(teks, "[Languages]");
            var kode = new List<string>();
            foreach (Match m in Regex.Matches(blokBahasa, "Name:\\s*\"([^\"]+)\""))
                kode.Add(m.Groups[1].Value);

            Ok("Pemasang menawarkan empat bahasa", kode.Count == 4,
               string.Join(", ", kode.ToArray()));
            Ok("Tiap kode bahasa pemasang dikenal Phoron",
               kode.All(k => Lang.Sah(k)),
               string.Join(", ", kode.Where(k => !Lang.Sah(k)).ToArray()));
            Ok("Tidak ada bahasa Phoron yang terlewat di pemasang",
               Lang.Semua.All(k => kode.Contains(k)),
               string.Join(", ", Lang.Semua.Where(k => !kode.Contains(k)).ToArray()));
            Ok("Indonesia terdaftar pertama, jadi ia yang jadi bawaan",
               kode.Count > 0 && kode[0] == Lang.Indonesia,
               kode.Count > 0 ? kode[0] : "(kosong)");

            // Tanpa keduanya, Windows berbahasa Inggris akan memilih Inggris
            // sendiri - padahal bahasa asal aplikasi ini Indonesia.
            Ok("Dialog bahasa selalu ditampilkan",
               Regex.IsMatch(teks, @"(?m)^\s*ShowLanguageDialog\s*=\s*yes"));
            Ok("Bahasa tidak ditebak dari lokal Windows",
               Regex.IsMatch(teks, @"(?m)^\s*LanguageDetectionMethod\s*=\s*none"));

            // Pilihan bahasa harus benar-benar sampai ke phoron.ini.
            Ok("Bahasa pilihan ditulis ke phoron.ini",
               teks.Contains("SetIniString('umum', 'bahasa', sekarang, ini)"));
            Ok("Bahasa hanya ditulis ulang kalau pilihannya berubah",
               teks.Contains("bahasa_pemasang"));

            // CustomMessage yang tidak punya padanan gagal saat PEMASANGAN
            // berjalan, bukan saat dirakit - jadi kesalahannya baru ketahuan di
            // komputer orang lain.
            var didefinisikan = new Dictionary<string, HashSet<string>>();
            foreach (Match m in Regex.Matches(Blok(teks, "[CustomMessages]"),
                                              @"(?m)^\s*(\w+)\.(\w+)\s*="))
            {
                var nama = m.Groups[2].Value;
                if (!didefinisikan.ContainsKey(nama))
                    didefinisikan[nama] = new HashSet<string>();
                didefinisikan[nama].Add(m.Groups[1].Value);
            }

            var dipakai = new HashSet<string>();
            foreach (Match m in Regex.Matches(teks, @"\{cm:(\w+)\}"))
                dipakai.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(teks, @"CustomMessage\('(\w+)'\)"))
                dipakai.Add(m.Groups[1].Value);

            Ok("Pesan khusus memang dipakai di skrip", dipakai.Count >= 10,
               dipakai.Count.ToString());
            // Kalau pencari bloknya rusak, daftar di bawah jadi kosong dan
            // seluruh pemeriksaan berikutnya lulus tanpa memeriksa apa pun.
            Ok("Daftar pesan khusus terbaca dari berkasnya",
               didefinisikan.Count >= 10, didefinisikan.Count.ToString());

            var kurang = new List<string>();
            foreach (var nama in dipakai.OrderBy(x => x))
            {
                HashSet<string> punya;
                if (!didefinisikan.TryGetValue(nama, out punya)) { kurang.Add(nama + " (tidak ada sama sekali)"); continue; }
                foreach (var k in Lang.Semua)
                    if (!punya.Contains(k)) kurang.Add(nama + "/" + k);
            }
            Ok("Tiap pesan khusus punya padanan di keempat bahasa",
               kurang.Count == 0, string.Join(", ", kurang.ToArray()));

            // Entri [Messages] tanpa awalan bahasa berlaku untuk SEMUA bahasa.
            // Itulah keadaan sebelum pemasang punya lebih dari satu bahasa, dan
            // akibatnya teks Indonesia ikut muncul saat memilih English.
            var tanpaAwalan = new List<string>();
            foreach (var baris in Blok(teks, "[Messages]")
                     .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = baris.Trim();
                if (t.Length == 0 || t[0] == ';') continue;
                var sama = t.IndexOf('=');
                if (sama <= 0) continue;
                var kunci = t.Substring(0, sama).Trim();
                if (kunci.IndexOf('.') < 0) tanpaAwalan.Add(kunci);
            }
            Ok("Tiap pesan bawaan diberi awalan bahasa",
               tanpaAwalan.Count == 0, string.Join(", ", tanpaAwalan.ToArray()));
        }

        /// <summary>
        /// Isi satu bagian berkas .iss, dari judulnya sampai judul berikutnya.
        ///
        /// Judulnya dicari HARUS di awal baris. Versi pertama fungsi ini memakai
        /// IndexOf biasa, dan ia menemukan "[CustomMessages]" yang kebetulan
        /// disebut di dalam komentar di atas bagiannya - sehingga bloknya
        /// terbaca kosong dan dua uji di bawah lulus tanpa memeriksa apa pun.
        /// </summary>
        static string Blok(string teks, string judul)
        {
            var awal = Regex.Match(teks, @"(?m)^" + Regex.Escape(judul) + @"\s*$");
            if (!awal.Success) return "";
            var i = awal.Index + awal.Length;
            var j = Regex.Match(teks.Substring(i), @"(?m)^\[[A-Za-z]+\]\s*$");
            return j.Success ? teks.Substring(i, j.Index) : teks.Substring(i);
        }

        static string CariBerkasPemasang()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null)
            {
                var calon = Path.Combine(d.FullName, Path.Combine("installer", "setup.iss"));
                if (File.Exists(calon)) return calon;
                d = d.Parent;
            }
            return null;
        }

        /// <summary>
        /// Sisi aplikasi dari perpindahan bahasa pemasang ke Phoron.
        ///
        /// Pemasang menulis dua kunci ke phoron.ini: "bahasa" yang dikenal
        /// Phoron, dan "bahasa_pemasang" yang TIDAK dikenalnya. Yang kedua
        /// dipakai untuk membedakan "pengguna mengubah bahasa di dalam Phoron"
        /// dari "pengguna memilih bahasa lain di pemasang", supaya pemasangan
        /// ulang tidak mengembalikan pilihan orang tanpa sebab.
        ///
        /// Itu hanya bekerja kalau kunci asing benar-benar selamat melewati
        /// Settings.Save(), yang memuat ulang berkasnya lalu menulis ulang
        /// seluruhnya. Di situlah uji ini berdiri.
        /// </summary>
        static void UjiBahasaDariPemasang()
        {
            Bagian("Bahasa dari pemasang");
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-bhs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;

                // Persis bentuk yang ditinggalkan pemasang sesudah memilih Jawa.
                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine +
                    "bahasa=jv" + Environment.NewLine +
                    "bahasa_pemasang=jv" + Environment.NewLine);

                var setelan = Settings.Load();
                Ok("Bahasa pilihan pemasang terbaca Phoron", setelan.Bahasa == "jv", setelan.Bahasa);

                setelan.Save();
                var isi = File.ReadAllText(Paths.SettingsFile);
                Ok("Bahasa tetap tersimpan sesudah menyimpan setelan",
                   isi.Contains("bahasa=jv"));
                Ok("Kunci milik pemasang tidak ikut terhapus",
                   isi.Contains("bahasa_pemasang=jv"), isi.Replace(Environment.NewLine, " | "));

                // Pengguna lalu mengganti bahasa dari dalam Phoron.
                setelan.Bahasa = Lang.Banjar;
                setelan.Save();
                var isi2 = File.ReadAllText(Paths.SettingsFile);
                Ok("Perubahan bahasa dari dalam Phoron tersimpan",
                   isi2.Contains("bahasa=bjn"));
                Ok("Catatan pilihan pemasang tetap utuh sesudahnya",
                   isi2.Contains("bahasa_pemasang=jv"));

                // Kode yang tidak dikenal tidak boleh membuat layar jadi aneh.
                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine + "bahasa=zz" + Environment.NewLine);
                Ok("Kode bahasa yang tidak dikenal jatuh ke Indonesia",
                   Settings.Load().Bahasa == Lang.Indonesia);
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        /// <summary>
        /// "(tidak dipakai)" harus benar-benar berarti tidak dipakai.
        ///
        /// Gejalanya di mesin pengguna: profil yang MySQL-nya dikosongkan tetap
        /// menyalakan MySQL - dan yang dinyalakan adalah versi TERTINGGI di
        /// seluruh folder bin yang dipindai, yaitu MariaDB 10.1.38 milik XAMPP
        /// di D:\xampp. Phoron menyalakan basis data milik pemasangan lain, di
        /// port 3306, tanpa diminta, dan panel Perhatian tidak berkata apa-apa.
        ///
        /// Sebabnya Pakai() memperlakukan id kosong sama dengan id yang disebut
        /// profil tapi tidak ada di komputer ini, lalu memakai penggantian
        /// antar-perangkat. Uji ini menjaga keduanya tetap berbeda: yang kedua
        /// HARUS tetap bekerja, sebab itulah yang membuat profil tetap jalan
        /// saat dibawa ke laptop lain.
        /// </summary>
        static void UjiVersiTidakDipakai()
        {
            Bagian("Versi yang tidak dipakai");
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-pakai-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;

                // Folder bin tiruan. Nama foldernya sudah menyebut versi, jadi
                // pemindai tidak perlu menjalankan binernya sama sekali.
                var bin = Path.Combine(akar, "binpalsu");
                BuatPaket(bin, Path.Combine("php", "php-8.3.12-Win32-vs16-x64"), "php.exe");
                BuatPaket(bin, Path.Combine("apache", "httpd-2.4.57-win64-VS16"),
                          Path.Combine("bin", "httpd.exe"));
                BuatPaket(bin, Path.Combine("mysql", "mysql-5.7.38-winx64"),
                          Path.Combine("bin", "mysqld.exe"));
                // Yang paling tinggi versinya - inilah yang dulu terpilih
                // diam-diam, meniru MariaDB 10.1.38 milik XAMPP.
                BuatPaket(bin, Path.Combine("mysql", "mariadb-10.1.38-winx64"),
                          Path.Combine("bin", "mysqld.exe"));

                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine +
                    "bin_roots=" + bin + Environment.NewLine +
                    "profil_aktif=tanpa-db" + Environment.NewLine +
                    "kelola_hosts=0" + Environment.NewLine +
                    "vhost_otomatis=0" + Environment.NewLine);

                Directory.CreateDirectory(Paths.Profiles);
                File.WriteAllText(Path.Combine(Paths.Profiles, "tanpa-db.ini"),
                    "[profil]" + Environment.NewLine +
                    "nama=Tanpa MySQL" + Environment.NewLine +
                    "php=php-8.3.12-Win32-vs16-x64" + Environment.NewLine +
                    "apache=httpd-2.4.57-win64-VS16" + Environment.NewLine +
                    "mysql=" + Environment.NewLine);

                var e = new Engine();
                e.Reload();
                Ok("Folder bin tiruan terbaca",
                   e.Of(BinKind.MySql).Count() == 2, e.Of(BinKind.MySql).Count().ToString());

                // Profil aktif dipilih lewat phoron.ini, sama seperti di
                // aplikasinya - Active memang tidak boleh disetel dari luar.
                Ok("Profil tanpa MySQL jadi profil aktif",
                   e.Active != null && e.Active.FileName == "tanpa-db",
                   e.Active != null ? e.Active.FileName : "(null)");

                // Inilah yang membuktikan kerusakannya.
                Ok("Profil tanpa MySQL tidak memakai MySQL mana pun",
                   e.MySql == null, e.MySql != null ? e.MySql.Id : "(null)");
                Ok("PHP dan Apache yang disebut profil tetap terpakai",
                   e.Php != null && e.Apache != null);
                Ok("Tidak ada penyesuaian yang dilaporkan untuk profil ini",
                   e.Penyesuaian().Count == 0,
                   string.Join(" | ", e.Penyesuaian().ToArray()));

                // Sisi sebaliknya: versi yang DISEBUT profil tapi tidak ada di
                // komputer ini harus tetap diganti - itulah penyesuaian antar
                // perangkat, dan ia tidak boleh ikut mati oleh perbaikan ini.
                e.Active.MySqlId = "mysql-9.9.9-winx64";
                Ok("Versi yang disebut tapi tidak ada tetap diganti",
                   e.MySql != null, "(null)");
                Ok("Penggantinya dilaporkan, tidak diam-diam",
                   e.Penyesuaian().Any(x => x.Contains("mysql-9.9.9-winx64")),
                   string.Join(" | ", e.Penyesuaian().ToArray()));

                e.Active.MySqlId = "";
                Ok("Dikosongkan lagi, MySQL kembali tidak dipakai", e.MySql == null);

                // Menyalakan layanan yang memang tidak dipakai bukan kegagalan.
                // Dulu jalur web berakhir dengan keadaan Gagal - titik MERAH di
                // layar - untuk profil yang sengaja hanya menjalankan MySQL.
                Ok("Menyalakan MySQL yang tidak dipakai tidak dianggap gagal",
                   !e.StartDbAsync().Result
                   && e.Services.DbState == ServiceState.Berhenti,
                   e.Services.DbState.ToString());

                e.Active.ApacheId = "";
                Ok("Profil tanpa web server tidak memakai web", !e.Active.PakaiWeb);
                Ok("Menyalakan web yang tidak dipakai tidak dianggap gagal",
                   !e.StartWebAsync().Result
                   && e.Services.WebState == ServiceState.Berhenti,
                   e.Services.WebState.ToString());

                // Dan keadaannya dikatakan apa adanya di riwayat, bukan didiamkan.
                Ok("Alasannya dicatat di Aktivitas",
                   e.Riwayat().Any(x => x.Teks.Contains("tidak memakai web server")),
                   string.Join(" | ", e.Riwayat().Select(x => x.Teks).ToArray()));
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        /// <summary>Folder paket tiruan: cukup ada exe-nya, isinya tidak dibaca.</summary>
        static void BuatPaket(string akarBin, string relatif, string exe)
        {
            var folder = Path.Combine(akarBin, relatif);
            var berkas = Path.Combine(folder, exe);
            Directory.CreateDirectory(Path.GetDirectoryName(berkas));
            File.WriteAllText(berkas, "");
        }

        /// <summary>
        /// Layanan yang tidak dipakai profil tidak boleh diperlakukan sebagai
        /// kegagalan, dan portnya tidak boleh diperiksa sama sekali.
        ///
        /// Dua gejalanya sama-sama bikin bingung. Profil yang hanya menjalankan
        /// MySQL menyalakan titik MERAH di sisi web setiap kali tombol Nyalakan
        /// ditekan, lengkap dengan baris "Profil belum menunjuk web server" -
        /// padahal memang tidak diminta. Dan profil tanpa MySQL mengeluh port
        /// 3306 dipegang orang lain, padahal yang memegangnya sering justru
        /// Laragon atau XAMPP milik orang itu sendiri yang sedang dipakai.
        /// </summary>
        static void UjiLayananTidakDipakai()
        {
            Bagian("Layanan yang tidak dipakai");

            var pWeb = new Profile { ApacheId = "httpd-2.4.57-win64-VS16", WebServer = "apache" };
            Ok("Profil dengan Apache dianggap memakai web", pWeb.PakaiWeb);
            Ok("Profil dengan Apache tapi tanpa MySQL", !pWeb.PakaiMySql);

            // Yang dilihat adalah web server YANG DIPILIH, bukan sembarang yang
            // terisi: profil bernginx dengan Apache terisi tetap tidak punya web.
            var pNginx = new Profile { ApacheId = "httpd-2.4.57-win64-VS16", NginxId = "", WebServer = "nginx" };
            Ok("Nginx dipilih tapi kosong berarti tidak memakai web", !pNginx.PakaiWeb);
            pNginx.NginxId = "nginx-1.27.1";
            Ok("Nginx terisi berarti memakai web", pNginx.PakaiWeb);
            Ok("Yang dibaca id web server yang dipilih", pNginx.WebId == "nginx-1.27.1", pNginx.WebId);

            // ---- port layanan yang tidak dipakai tidak diperiksa ----
            var port = PortBebas();
            var pendengar = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, port);
            pendengar.Start();
            try
            {
                Ok("Port percobaan benar-benar terpakai", !PortCheck.IsFree(port), port.ToString());

                var tanpaDb = new Profile
                {
                    ApacheId = "httpd-2.4.57-win64-VS16",
                    WebServer = "apache",
                    MySqlId = "",
                    HttpPort = PortBebas(),
                    MySqlPort = port,
                };
                Ok("Port MySQL tidak diperiksa saat MySQL tidak dipakai",
                   PortCheck.Conflicts(tanpaDb, false).Count == 0,
                   string.Join(" | ", PortCheck.Conflicts(tanpaDb, false)
                       .Select(x => x.Describe()).ToArray()));

                tanpaDb.MySqlId = "mysql-5.7.38-winx64";
                Ok("Port MySQL diperiksa lagi begitu MySQL dipakai",
                   PortCheck.Conflicts(tanpaDb, false).Any(x => x.Port == port));

                var tanpaWeb = new Profile
                {
                    ApacheId = "",
                    WebServer = "apache",
                    MySqlId = "",
                    HttpPort = port,
                    MySqlPort = PortBebas(),
                };
                Ok("Port web tidak diperiksa saat web tidak dipakai",
                   PortCheck.Conflicts(tanpaWeb, false).Count == 0);
            }
            finally { try { pendengar.Stop(); } catch { } }

            UjiKonfigurasiIkutWebServer();
        }

        /// <summary>
        /// Konfigurasi yang ditulis harus mengikuti web server YANG DIPILIH
        /// profil, bukan paket mana yang kebetulan tersedia.
        ///
        /// Bentuk lamanya - "nginx kalau ada, kalau tidak Apache" - membuat
        /// profil bernginx yang versinya dikosongkan diam-diam menghasilkan
        /// konfigurasi Apache, dan keluhannya selalu menyebut Apache walau yang
        /// dipilih profil itu Nginx.
        /// </summary>
        static void UjiKonfigurasiIkutWebServer()
        {
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-webcfg-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;

                var apache = new BinPackage
                {
                    Kind = BinKind.Apache,
                    Id = "httpd-2.4.57-win64-VS16",
                    Version = "2.4.57",
                    Path = Path.Combine(akar, "apache-palsu"),
                };
                var situs = new List<Site>();

                // Profil bernginx, versi Nginx dikosongkan, Apache kebetulan ada.
                var pNginx = new Profile
                {
                    Name = "Nginx tanpa versi",
                    WebServer = "nginx",
                    NginxId = "",
                    ApacheId = apache.Id,
                };
                var rNginx = ConfigWriter.Build(pNginx, null, apache, null, null, situs);
                Ok("Profil bernginx tidak menghasilkan konfigurasi Apache",
                   rNginx.HttpdConf == null, rNginx.HttpdConf ?? "(null)");
                Ok("Keluhannya tidak menyebut Apache untuk profil bernginx",
                   !rNginx.Warnings.Any(w => w.Contains("Apache")),
                   string.Join(" | ", rNginx.Warnings.ToArray()));

                // Profil yang sengaja tanpa web server: tidak ada keluhan sama sekali.
                var pTanpaWeb = new Profile { Name = "MySQL saja", WebServer = "apache", ApacheId = "" };
                var rTanpa = ConfigWriter.Build(pTanpaWeb, null, apache, null, null, situs);
                Ok("Profil tanpa web server tidak menghasilkan konfigurasi web",
                   rTanpa.HttpdConf == null && rTanpa.NginxConf == null);
                Ok("Profil tanpa web server tidak dikeluhkan",
                   rTanpa.Warnings.Count == 0,
                   string.Join(" | ", rTanpa.Warnings.ToArray()));

                // Sisi sebaliknya: web server yang DIMINTA tapi tidak ada tetap dikeluhkan.
                var pMinta = new Profile { Name = "Nginx hilang", WebServer = "nginx", NginxId = "nginx-9.9.9" };
                var rMinta = ConfigWriter.Build(pMinta, null, null, null, null, situs);
                Ok("Web server yang diminta tapi tidak ada tetap dikeluhkan",
                   rMinta.Warnings.Any(w => w.Contains("Nginx")),
                   string.Join(" | ", rMinta.Warnings.ToArray()));
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        /// <summary>Port tinggi yang sedang bebas, untuk dipakai percobaan.</summary>
        static int PortBebas()
        {
            for (var p = 34100; p < 34200; p++)
                if (PortCheck.IsFree(p)) return p;
            return 34199;
        }

        /// <summary>
        /// Panel keluaran Node memakai penggolong warna yang sama dengan panel
        /// Aktivitas di Beranda, jadi penggolongnya harus mengenal dua hal yang
        /// tidak pernah muncul di Beranda: keluaran perkakas Node yang selalu
        /// berbahasa Inggris, dan penanda Phoron sendiri yang IKUT berganti
        /// bahasa.
        ///
        /// Yang kedua itu gampang terlewat. Menerjemahkan "-- dihentikan --"
        /// tanpa menambahkan padanannya ke daftar kata membuat baris yang sama
        /// kehilangan warnanya begitu bahasanya diganti.
        /// </summary>
        static void UjiKeluaranNode()
        {
            Bagian("Keluaran Node");

            Ok("npm ERR! dianggap galat",
               LogWarna.Golongkan("npm ERR! code ELIFECYCLE") == JenisPesan.Galat);
            Ok("Port terpakai dianggap galat",
               LogWarna.Golongkan("Error: listen EADDRINUSE: address already in use :::3000")
               == JenisPesan.Galat);
            Ok("Modul hilang dianggap galat",
               LogWarna.Golongkan("Cannot find module 'next'") == JenisPesan.Galat);
            Ok("npm WARN dianggap peringatan",
               LogWarna.Golongkan("npm WARN deprecated") == JenisPesan.Peringatan);
            Ok("Server siap dianggap berhasil",
               LogWarna.Golongkan("  - Ready in 1.2s") == JenisPesan.Berhasil);

            // Penanda milik Phoron, di keempat bahasa.
            Ok("Penanda dihentikan berwarna sama di tiap bahasa",
               LogWarna.Golongkan("-- dihentikan --") == JenisPesan.Berhasil
               && LogWarna.Golongkan("-- stopped --") == JenisPesan.Berhasil
               && LogWarna.Golongkan("-- dipateni --") == JenisPesan.Berhasil
               && LogWarna.Golongkan("-- dipajahakan --") == JenisPesan.Berhasil);

            Ok("Proses yang berakhir dengan galat berwarna merah",
               LogWarna.Golongkan("-- proses berakhir dengan galat (kode 1) --") == JenisPesan.Galat
               && LogWarna.Golongkan("-- process ended with an error (code 1) --") == JenisPesan.Galat
               && LogWarna.Golongkan("-- proses rampung kanthi galat (kode 1) --") == JenisPesan.Galat
               && LogWarna.Golongkan("-- proses baranti lawan kasalahan (kode 1) --") == JenisPesan.Galat);

            // Berakhir dengan kode 0 BUKAN kegagalan, jadi tidak boleh merah.
            Ok("Proses yang berakhir normal tidak diwarnai galat",
               LogWarna.Golongkan("-- proses berakhir (kode 0) --") != JenisPesan.Galat
               && LogWarna.Golongkan("-- process ended (code 0) --") != JenisPesan.Galat);

            // Teks halaman Node benar-benar berganti saat bahasanya diganti -
            // inilah keluhan yang memulai perbaikan ini.
            try
            {
                Lang.Pakai(Lang.Inggris);
                Ok("Status proyek ikut berganti bahasa",
                   Lang.T("jalan") == "running" && Lang.T("berhenti") == "stopped"
                   && Lang.T("folder hilang") == "folder missing",
                   Lang.T("jalan") + " / " + Lang.T("berhenti") + " / " + Lang.T("folder hilang"));
                Ok("Penanda keluaran ikut berganti bahasa",
                   Lang.T("-- dihentikan --") == "-- stopped --");
                Ok("Pesan berparameter tetap menyisipkan nilainya",
                   Lang.T("Proyek {0} siap di {1}", "kalsel", "http://localhost:3000")
                   == "Project kalsel is ready at http://localhost:3000",
                   Lang.T("Proyek {0} siap di {1}", "kalsel", "http://localhost:3000"));
            }
            finally { Lang.Pakai(Lang.Indonesia); }
        }

        /// <summary>
        /// Mematikan Virtual Host harus benar-benar mematikannya.
        ///
        /// Gejala yang diuji, diambil dari mesin pengguna: Virtual Host sudah
        /// dimatikan dan tidak ada satu pun vhost per situs tersisa, TAPI nama
        /// .test masih terdaftar di berkas hosts. Namanya jadi tetap menunjuk
        /// 127.0.0.1 dan dijawab vhost bawaan - satu alamat membalas galat 500,
        /// yang lain membalas isi proyek yang sama sekali berbeda. Alamat yang
        /// "jalan" tapi menampilkan hal yang salah lebih menyesatkan daripada
        /// alamat yang jelas-jelas tidak ada.
        /// </summary>
        static void UjiVirtualHostMati()
        {
            Bagian("Virtual Host dimatikan");
            var akarLama = Paths.Root;
            var hostsLama = Paths.HostsFile;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-vh-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                var hosts = Path.Combine(akar, "hosts");
                Paths.HostsFile = hosts;
                File.WriteAllText(hosts, "127.0.0.1 localhost" + Environment.NewLine
                                         + "10.0.0.1 kantor.internal" + Environment.NewLine);

                // Dua folder proyek: yang kedua hanya terjangkau lewat Virtual
                // Host, sebab akar yang dilayani Apache cuma yang pertama.
                var utama = Path.Combine(akar, "proyek");
                var kedua = Path.Combine(akar, "proyek-lain");
                Directory.CreateDirectory(Path.Combine(utama, "toko"));
                Directory.CreateDirectory(Path.Combine(kedua, "gudang"));
                // Nama yang bentrok: dua folder "kembar" di akar berbeda akan
                // memperebutkan host kembar.test, dan pemindai mencatatnya.
                Directory.CreateDirectory(Path.Combine(utama, "kembar"));
                Directory.CreateDirectory(Path.Combine(kedua, "kembar"));

                Directory.CreateDirectory(Paths.Profiles);
                File.WriteAllText(Path.Combine(Paths.Profiles, "uji.ini"),
                    "[profil]" + Environment.NewLine +
                    "nama=Uji" + Environment.NewLine +
                    "folder_proyek=" + utama + ";" + kedua + Environment.NewLine +
                    "akhiran_situs=test" + Environment.NewLine);

                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine +
                    "profil_aktif=uji" + Environment.NewLine +
                    "auto_vhost=1" + Environment.NewLine +
                    "kelola_hosts=1" + Environment.NewLine);

                var e = new Engine();
                e.Reload();
                Ok("Keempat situs terbaca", e.Sites.Count == 4, e.Sites.Count.ToString());
                Ok("Nama yang bentrok dicatat saat Virtual Host menyala",
                   e.SiteWarnings.Any(w => w.Contains("kembar.test")),
                   string.Join(" | ", e.SiteWarnings.ToArray()));

                var toko = e.Sites.FirstOrDefault(x => x.Folder == "toko");
                var gudang = e.Sites.FirstOrDefault(x => x.Folder == "gudang");
                Ok("Situs dari kedua folder proyek ditemukan", toko != null && gudang != null);

                // --- Virtual Host menyala ---
                Ok("Alamat memakai nama .test saat Virtual Host menyala",
                   e.SiteUrl(toko) == "http://toko.test/", e.SiteUrl(toko));
                e.Apply();
                Ok("Nama .test terdaftar di hosts saat Virtual Host menyala",
                   HostsFile.AllNames().Contains("toko.test"));

                // --- Virtual Host dimatikan ---
                e.Settings.AutoVhost = false;
                e.Settings.Save();
                e.Apply();

                Ok("Blok hosts dikosongkan saat Virtual Host dimatikan",
                   !HostsFile.AllNames().Contains("toko.test"),
                   string.Join(", ", HostsFile.AllNames().ToArray()));
                Ok("Baris milik orang lain tetap selamat",
                   File.ReadAllText(hosts).Contains("10.0.0.1 kantor.internal"));

                // Alamat yang ditampilkan harus yang BENAR-BENAR bekerja.
                Ok("Alamat berubah jadi jalur localhost",
                   e.SiteUrl(toko) == "http://localhost/toko/", e.SiteUrl(toko));
                Ok("Situs di folder proyek kedua dinyatakan tidak terjangkau",
                   e.SiteUrl(gudang) == "", "[" + e.SiteUrl(gudang) + "]");

                // Dan tidak ada lagi yang ditawarkan untuk didaftarkan.
                Ok("Tidak ada nama yang perlu didaftarkan lagi",
                   HostsTool.SemuaNamaSitus().Count == 0,
                   string.Join(", ", HostsTool.SemuaNamaSitus().ToArray()));

                // Seluruh catatan pemindai situs bicara soal NAMA host. Tanpa
                // Virtual Host nama itu tidak dipakai siapa pun, jadi keberatan
                // tentang nama yang bentrok tidak berlaku lagi - dan panel
                // catatan yang bicara soal hal yang tidak berlaku cuma melatih
                // orang mengabaikannya.
                Ok("Catatan nama host hilang saat Virtual Host mati",
                   e.SiteWarnings.Count == 0,
                   string.Join(" | ", e.SiteWarnings.ToArray()));

                // --- dinyalakan lagi: harus pulih seutuhnya ---
                e.Settings.AutoVhost = true;
                e.Settings.Save();
                e.Apply();
                Ok("Dinyalakan lagi, nama .test kembali terdaftar",
                   HostsFile.AllNames().Contains("toko.test"));
                Ok("Dinyalakan lagi, alamatnya kembali memakai .test",
                   e.SiteUrl(toko) == "http://toko.test/", e.SiteUrl(toko));
                Ok("Dinyalakan lagi, catatan nama host kembali muncul",
                   e.SiteWarnings.Any(w => w.Contains("kembar.test")));
            }
            finally
            {
                Paths.Root = akarLama;
                Paths.HostsFile = hostsLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiIni()
        {
            Bagian("Ini");
            var path = Path.Combine(Paths.Tmp, "coba.ini");
            var ini = new Ini();
            ini.Set("umum", "nama", "Phoron");
            ini.Set("umum", "angka", "42");
            ini.Set("lain", "kosong", "");
            ini.Save(path);

            var lagi = Ini.Load(path);
            Ok("Ini bolak-balik teks", lagi.Get("umum", "nama") == "Phoron");
            Ok("Ini baca angka", lagi.GetInt("umum", "angka", 0) == 42);
            Ok("Ini nilai bawaan", lagi.GetInt("umum", "tidakada", 7) == 7);
            Ok("Ini seksi kedua", lagi.Get("lain", "kosong") == "");

            lagi.Set("umum", "nama", "Baru");
            Ok("Ini menimpa bukan menggandakan",
                lagi.Items("umum").Count(kv => kv.Key == "nama") == 1);
        }

        static void UjiPemindai()
        {
            Bagian("BinScanner");
            var pkgs = Nyata();
            Console.WriteLine("     (" + pkgs.Count + " paket terdeteksi di mesin ini)");

            var php = pkgs.Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            Ok("PHP punya nomor versi", php.All(p => p.Parsed.Major > 0));
            Ok("PHP punya php.exe", php.All(p => File.Exists(p.MainExe)));
            Ok("PHP terurut versi menurun",
                php.Select(p => p.Parsed).SequenceEqual(php.Select(p => p.Parsed).OrderByDescending(v => v)));

            var php5 = php.FirstOrDefault(p => p.Parsed.Major == 5);
            var php7 = php.FirstOrDefault(p => p.Parsed.Major == 7);
            var php8 = php.FirstOrDefault(p => p.Parsed.Major == 8);
            if (php5 != null) Ok("Modul Apache PHP 5 = php5_module", BinScanner.ApacheModuleName(php5) == "php5_module");
            if (php7 != null) Ok("Modul Apache PHP 7 = php7_module", BinScanner.ApacheModuleName(php7) == "php7_module");
            if (php8 != null) Ok("Modul Apache PHP 8 = php_module", BinScanner.ApacheModuleName(php8) == "php_module");

            if (php5 != null)
                Ok("Ekstensi PHP 5 pakai nama berkas DLL",
                    ConfigWriter.ExtensionValue(php5, "curl") == "php_curl.dll");
            if (php8 != null)
                Ok("Ekstensi PHP 8 pakai nama telanjang",
                    ConfigWriter.ExtensionValue(php8, "curl") == "curl");

            var apache = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
            if (apache.Count > 0)
            {
                Ok("Apache punya httpd.exe", apache.All(a => File.Exists(a.MainExe)));
                // Bukan "semua Apache punya toolset": XAMPP menamai foldernya
                // cuma "apache", jadi toolsetnya memang tidak tertulis di mana
                // pun. Yang diuji adalah PENGURAINYA - folder yang menyebut
                // toolset harus terbaca.
                var bertoolset = apache.Where(a => a.Id.IndexOf("-vc", StringComparison.OrdinalIgnoreCase) >= 0
                                               || a.Id.IndexOf("-vs", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                Ok("Toolset terurai dari nama folder yang menyebutnya",
                    bertoolset.All(a => a.Compiler.Length > 0),
                    string.Join(", ", bertoolset.Where(a => a.Compiler.Length == 0).Select(a => a.Id).ToArray()));
            }

            // Pasangan toolset adalah alasan utama Apache gagal start; ini inti
            // dari saran otomatis yang diberikan Phoron saat membuat profil.
            if (php.Count > 0 && apache.Count > 0)
            {
                var cocok = ProfileStore.PickApache(php[0], apache);
                Ok("Apache dipasangkan dengan toolset yang sama",
                    cocok != null && (apache.All(a => a.Compiler != php[0].Compiler)
                                      || cocok.Compiler == php[0].Compiler),
                    "PHP " + php[0].Compiler + " -> Apache " + (cocok != null ? cocok.Compiler : "null"));
            }

            Ok("Folder acak bukan paket", BinScanner.Identify(Paths.Tmp) == null);
        }

        static void UjiProfil()
        {
            Bagian("ProfileStore");
            var p = new Profile
            {
                Name = "Uji PHP 7",
                PhpId = "php-7.4.22-Win32-VC15-x64",
                ApacheId = "httpd-2.4.46-win64-VC15",
                MySqlId = "mysql-5.7.38-winx64",
                HttpPort = 8080,
                MySqlPort = 3307,
                SiteSuffix = "lokal",
                Notes = "catatan uji",
            };
            p.PhpExtensions.AddRange(new[] { "curl", "mbstring", "pdo_mysql" });
            p.PhpIniOverrides["memory_limit"] = "512M";
            p.FileName = ProfileStore.UniqueFileName(p.Name);
            ProfileStore.Save(p);

            var muat = ProfileStore.LoadAll().Single();
            Ok("Profil bolak-balik nama", muat.Name == p.Name);
            Ok("Profil bolak-balik port", muat.HttpPort == 8080 && muat.MySqlPort == 3307);
            Ok("Profil bolak-balik ekstensi",
                muat.PhpExtensions.SequenceEqual(new[] { "curl", "mbstring", "pdo_mysql" }));
            Ok("Profil bolak-balik php.ini", muat.PhpIniOverrides["memory_limit"] == "512M");
            Ok("Profil bolak-balik akhiran situs", muat.SiteSuffix == "lokal");

            var kedua = ProfileStore.UniqueFileName(p.Name);
            Ok("Nama berkas profil tidak menabrak yang ada", kedua != p.FileName);

            var salinan = muat.Clone();
            salinan.PhpExtensions.Add("gd");
            Ok("Clone tidak berbagi daftar ekstensi", muat.PhpExtensions.Count == 3);

            ProfileStore.Delete(muat);
            Ok("Hapus profil", ProfileStore.LoadAll().Count == 0);
        }

        static void UjiSitus()
        {
            Bagian("SiteScanner");
            var www = Paths.Www;
            Directory.CreateDirectory(Path.Combine(www, "Proyek Satu"));
            Directory.CreateDirectory(Path.Combine(www, "laravel-app", "public"));
            File.WriteAllText(Path.Combine(www, "laravel-app", "public", "index.php"), "<?php");
            Directory.CreateDirectory(Path.Combine(www, "punya-aset", "public"));  // tanpa index
            Directory.CreateDirectory(Path.Combine(www, ".git"));

            var profil = new Profile { SiteSuffix = "test" };
            var situs = SiteScanner.Scan(profil);

            Ok("Folder titik dilewati", situs.All(s => !s.Folder.StartsWith(".")));
            Ok("Spasi jadi tanda hubung di nama host",
                situs.Any(s => s.HostName == "proyek-satu.test"),
                string.Join(", ", situs.Select(s => s.HostName)));
            var laravel = situs.FirstOrDefault(s => s.Folder == "laravel-app");
            Ok("public/ berisi index.php jadi document root",
                laravel != null && laravel.DocRoot.EndsWith("public"));
            var aset = situs.FirstOrDefault(s => s.Folder == "punya-aset");
            Ok("public/ tanpa index.php TIDAK jadi document root",
                aset != null && !aset.DocRoot.EndsWith("public"));

            Ok("Tanpa folder proyek, www bawaan yang dipakai",
                SiteScanner.Roots(new Profile()).Single() == Paths.Www);
        }

        static void UjiBanyakFolderProyek()
        {
            Bagian("Banyak folder proyek");
            var kedua = Path.Combine(Paths.Root, "proyek-lain");
            Directory.CreateDirectory(Path.Combine(kedua, "toko"));
            Directory.CreateDirectory(Path.Combine(kedua, "gudang"));
            // Sengaja bernama sama dengan folder di www - inilah kasus yang
            // membuat dua vhost berebut ServerName yang sama.
            Directory.CreateDirectory(Path.Combine(kedua, "laravel-app"));

            var profil = new Profile { SiteSuffix = "test" };
            profil.ProjectRoots.Add(Paths.Www);
            profil.ProjectRoots.Add(kedua);

            var peringatan = new List<string>();
            var situs = SiteScanner.Scan(profil, peringatan);

            Ok("Situs dari folder kedua ikut terbaca",
                situs.Any(s => s.HostName == "toko.test" && s.Root == kedua),
                string.Join(", ", situs.Select(s => s.HostName)));
            Ok("Situs dari folder pertama tetap ada",
                situs.Any(s => s.HostName == "proyek-satu.test" && s.Root == Paths.Www));
            Ok("Akar utama tetap yang pertama", SiteScanner.DocumentRoot(profil) == Paths.Www);

            var bentrok = situs.Where(s => s.Folder == "laravel-app").ToList();
            Ok("Folder bernama sama tidak saling menghapus", bentrok.Count == 2);
            Ok("Folder pertama memegang nama aslinya",
                bentrok.Any(s => s.HostName == "laravel-app.test" && s.Root == Paths.Www));
            Ok("Folder kedua diberi nama berangka, bukan dibuang",
                bentrok.Any(s => s.HostName == "laravel-app-2.test" && s.Root == kedua),
                string.Join(", ", bentrok.Select(s => s.HostName)));
            Ok("Bentrok nama dilaporkan sebagai peringatan",
                peringatan.Any(w => w.Contains("laravel-app.test")),
                string.Join(" | ", peringatan));
            Ok("Nama host tetap unik seluruhnya",
                situs.Select(s => s.HostName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == situs.Count);

            // Folder yang salah ketik harus berbunyi, bukan diam-diam kosong.
            var salah = new Profile { SiteSuffix = "test" };
            salah.ProjectRoots.Add(Path.Combine(Paths.Root, "tidak-ada-folder-ini"));
            var p2 = new List<string>();
            SiteScanner.Scan(salah, p2);
            Ok("Folder proyek yang tidak ada dilaporkan",
                p2.Any(w => w.Contains("tidak ada")), string.Join(" | ", p2));

            // Bolak-balik ke berkas profil: daftar folder harus utuh.
            profil.Name = "Uji banyak folder";
            profil.FileName = ProfileStore.UniqueFileName(profil.Name);
            ProfileStore.Save(profil);
            var muat = ProfileStore.Load(Path.Combine(Paths.Profiles, profil.FileName + ".ini"));
            Ok("Daftar folder proyek bolak-balik utuh",
                muat.ProjectRoots.SequenceEqual(new[] { Paths.Www, kedua }),
                string.Join(";", muat.ProjectRoots));
            ProfileStore.Delete(muat);

            // Profil lama memakai kunci document_root; membacanya harus tetap jalan.
            var lama = Path.Combine(Paths.Profiles, "profil-lama.ini");
            var ini = new Ini();
            ini.Set("profil", "nama", "Profil lama");
            ini.Set("profil", "document_root", kedua);
            ini.Save(lama);
            var dibaca = ProfileStore.Load(lama);
            Ok("Profil lama dengan document_root tetap terbaca",
                dibaca.ProjectRoots.SequenceEqual(new[] { kedua }),
                string.Join(";", dibaca.ProjectRoots));
            File.Delete(lama);

            Directory.Delete(kedua, true);
        }

        static void UjiPhpIni()
        {
            Bagian("php.ini");
            var php = Nyata().Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            foreach (var v in php)
            {
                var profil = new Profile { Name = "Uji " + v.Version };
                // Ekstensi dipilih dari yang benar-benar ada supaya uji ini menguji
                // penulisan php.ini, bukan ketersediaan berkas DLL.
                var tersedia = ConfigWriter.AvailableExtensions(v);
                profil.PhpExtensions = tersedia.Where(x => x == "curl" || x == "mbstring" || x == "openssl").ToList();
                profil.PhpIniOverrides["memory_limit"] = "333M";

                var hasil = new ConfigWriter.Result();
                var dir = ConfigWriter.WritePhpIni(profil, v, hasil);
                var isi = File.ReadAllText(Path.Combine(dir, "php.ini"));

                Ok(v.Version + ": extension_dir menunjuk folder ext yang benar",
                    isi.Contains("extension_dir = \"" + Paths.Fwd(Path.Combine(v.Path, "ext")) + "\""));
                Ok(v.Version + ": memory_limit dari profil dipakai", isi.Contains("333M"));

                // php.exe sendiri yang jadi hakim: kalau ada direktif salah bentuk
                // atau DLL tidak cocok, ia mencetak peringatan ke stderr.
                var res = Shell.Run(v.MainExe, "-c \"" + dir + "\" -m", v.Path, 30000);
                var modul = res.StdOut.Split('\n').Select(l => l.Trim().ToLowerInvariant()).ToList();
                Ok(v.Version + ": php.exe menerima php.ini tanpa keluhan",
                    !res.StdErr.ToLowerInvariant().Contains("unable to load"),
                    res.StdErr.Trim());
                foreach (var ext in profil.PhpExtensions)
                    Ok(v.Version + ": ekstensi " + ext + " benar-benar dimuat",
                        modul.Contains(ext), string.Join(",", modul.Take(6)));

                var limit = Shell.Run(v.MainExe, "-c \"" + dir + "\" -r \"echo ini_get('memory_limit');\"",
                                      v.Path, 30000);
                Ok(v.Version + ": memory_limit terbaca PHP sebagai 333M",
                    limit.StdOut.Trim() == "333M", limit.All);
            }
        }

        static void UjiDaftarEkstensiTersedia()
        {
            Bagian("Daftar ekstensi tersedia");
            var palsu = Path.Combine(Paths.Root, "php-ext-daftar");
            var ext = Path.Combine(palsu, "ext");
            Directory.CreateDirectory(ext);
            File.WriteAllText(Path.Combine(ext, "php_mbstring.dll"), "x");
            File.WriteAllText(Path.Combine(ext, "php_oci8_12c.dll"), "x");
            // Cara orang menonaktifkan ekstensi: ganti nama berkasnya. Pola
            // "*.dll" Windows masih menjaringnya lewat pencocokan nama 8.3.
            File.WriteAllText(Path.Combine(ext, "php_oci8_12c.dllaaa"), "x");
            File.WriteAllText(Path.Combine(ext, "php_curl.dll.mati"), "x");
            File.WriteAllText(Path.Combine(ext, "catatan.txt"), "x");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-ext", Path = palsu, Version = "5.6.40" };

            var daftar = ConfigWriter.AvailableExtensions(php);
            Ok("Hanya berkas .dll sungguhan yang terdaftar",
                daftar.SequenceEqual(new[] { "mbstring", "oci8_12c" }),
                string.Join(",", daftar));
            Ok("Berkas yang dinonaktifkan (.dllaaa) tidak ikut",
                daftar.Count(x => x == "oci8_12c") == 1, string.Join(",", daftar));

            // --- daftar yang disarankan untuk profil baru ---
            // php.ini-development bawaan: semua ekstensi dikomentari, jadi
            // daftar baku yang harus dipakai - kalau tidak, profil baru lahir
            // tanpa satu pun ekstensi.
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                ";extension=php_mbstring.dll\n;extension=php_curl.dll\n");
            foreach (var e in new[] { "curl", "openssl", "gd2", "mysqli", "tidak_disarankan" })
                File.WriteAllText(Path.Combine(ext, "php_" + e + ".dll"), "x");

            var baku = ConfigWriter.EkstensiDisarankan(php);
            Ok("PHP baru unduh dapat daftar baku, bukan kosong", baku.Count > 0,
                string.Join(",", baku));
            Ok("Daftar baku memuat mbstring", baku.Contains("mbstring"), string.Join(",", baku));
            Ok("Daftar baku disaring ke DLL yang ada",
                !baku.Contains("intl") && !baku.Contains("tidak_disarankan"),
                string.Join(",", baku));
            Ok("Urutan baku menjaga exif setelah mbstring",
                !baku.Contains("exif")
                || baku.IndexOf("mbstring") < baku.IndexOf("exif"),
                string.Join(",", baku));

            // php.ini yang SUDAH mengaktifkan sesuatu selalu menang atas daftar baku.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), "extension=php_oci8_12c.dll\n");
            var dariIni = ConfigWriter.EkstensiDisarankan(php);
            Ok("php.ini yang sudah ada mengalahkan daftar baku",
                dariIni.SequenceEqual(new[] { "oci8_12c" }), string.Join(",", dariIni));

            Directory.Delete(palsu, true);
        }

        static void UjiWarisanPhpIni()
        {
            Bagian("php.ini yang sudah ada dijadikan dasar");
            // Kasus nyata: folder PHP dipinjam dari Laragon, yang menyetel
            // short_open_tag=On bertahun-tahun. Memulai dari php.ini-development
            // bawaan vendor mengembalikannya ke Off diam-diam, dan CodeIgniter
            // beralih ke jalur eval() lalu gagal mengurai view-nya.
            var palsu = Path.Combine(Paths.Root, "php-warisan");
            Directory.CreateDirectory(Path.Combine(palsu, "ext"));
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                "short_open_tag = Off\nmemory_limit = 128M\nmax_input_vars = 1000\n");
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "short_open_tag = On\nmemory_limit = 128M\nmax_input_vars = 5000\n");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-warisan", Path = palsu, Version = "5.6.40" };

            var profil = new Profile { Name = "Uji warisan" };
            var dir = ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result());
            var isi = File.ReadAllText(Path.Combine(dir, "php.ini"));

            Ok("short_open_tag dari php.ini yang dipakai ikut terbawa",
                Regex.IsMatch(isi, @"(?m)^short_open_tag\s*=\s*On"),
                Baris(isi, "short_open_tag"));
            Ok("Setelan lain dari php.ini itu juga ikut",
                Regex.IsMatch(isi, @"(?m)^max_input_vars\s*=\s*5000"),
                Baris(isi, "max_input_vars"));

            // Ekstensi ikut diambil alih ketika profil belum punya daftar sendiri.
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "short_open_tag = On\nmax_input_vars = 5000\n"
                + "extension=php_mbstring.dll\n"
                + "extension=php_exif.dll   ; harus setelah mbstring\n"
                + ";extension=php_tidak_dipakai.dll\n"
                + "extension=php_oci8_12c.dll\n");
            foreach (var e in new[] { "mbstring", "exif", "oci8_12c" })
                File.WriteAllText(Path.Combine(palsu, "ext", "php_" + e + ".dll"), "x");

            var profilKosong = new Profile { Name = "Belum punya daftar" };
            var r2 = new ConfigWriter.Result();
            var isiAdopsi = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profilKosong, php, r2), "php.ini"));
            Ok("Ekstensi aktif diambil alih dari php.ini dasar",
                r2.AdoptedExtensions != null
                && r2.AdoptedExtensions.SequenceEqual(new[] { "mbstring", "exif", "oci8_12c" }),
                r2.AdoptedExtensions == null ? "null" : string.Join(",", r2.AdoptedExtensions));
            Ok("Ekstensi yang dikomentari tidak ikut",
                r2.AdoptedExtensions != null && !r2.AdoptedExtensions.Contains("tidak_dipakai"));
            Ok("mbstring benar-benar ditulis ke php.ini hasil",
                Regex.IsMatch(isiAdopsi, @"(?m)^extension\s*=\s*php_mbstring\.dll"),
                Baris(isiAdopsi, "extension = php_mbstring"));
            Ok("Urutan dipertahankan (exif setelah mbstring)",
                isiAdopsi.IndexOf("php_mbstring.dll", StringComparison.Ordinal)
                    < isiAdopsi.IndexOf("php_exif.dll", StringComparison.Ordinal));

            // Profil yang SUDAH punya daftar tidak boleh diambil alih.
            var profilPunya = new Profile { Name = "Sudah punya" };
            profilPunya.PhpExtensions.Add("oci8_12c");
            var r2b = new ConfigWriter.Result();
            var isiPunya = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profilPunya, php, r2b), "php.ini"));
            Ok("Profil yang sudah punya daftar tidak diambil alih", r2b.AdoptedExtensions == null);
            Ok("Daftar profil yang dipakai, bukan daftar berkas dasar",
                !Regex.IsMatch(isiPunya, @"(?m)^extension\s*=\s*php_mbstring\.dll"));

            // Profil tetap berkuasa di atas berkas dasar.
            profil.PhpIniOverrides["short_open_tag"] = "Off";
            var isi2 = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result()), "php.ini"));
            Ok("Penimpaan profil mengalahkan php.ini dasar",
                Regex.IsMatch(isi2, @"(?m)^short_open_tag\s*=\s*Off"),
                Baris(isi2, "short_open_tag"));

            // php.ini yang ternyata keluaran Phoron tidak boleh dipakai sebagai dasar.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), isi);
            var isi3 = File.ReadAllText(Path.Combine(
                ConfigWriter.WritePhpIni(new Profile { Name = "Uji" }, php, new ConfigWriter.Result()), "php.ini"));
            Ok("Keluaran Phoron tidak dijadikan dasar (tidak menumpuk)",
                isi3.Length < isi.Length + 200,
                isi.Length + " lalu " + isi3.Length);

            Directory.Delete(palsu, true);
        }

        /// <summary>Baris pertama yang memuat sebuah kunci - dipakai untuk pesan kegagalan.</summary>
        static string Baris(string teks, string kunci)
        {
            foreach (var l in teks.Split('\n'))
                if (l.TrimStart().StartsWith(kunci, StringComparison.OrdinalIgnoreCase)) return l.Trim();
            return "(tidak ada baris " + kunci + ")";
        }

        static void UjiPhpIniKeFolderPhp()
        {
            Bagian("php.ini di dalam folder PHP");
            // Folder PHP TIRUAN di dalam sandbox. Uji ini tidak boleh menyentuh
            // folder PHP sungguhan: di mesin pengembang, folder itu milik Laragon.
            var palsu = Path.Combine(Paths.Root, "php-palsu");
            Directory.CreateDirectory(Path.Combine(palsu, "ext"));
            File.WriteAllText(Path.Combine(palsu, "php.ini-development"),
                "memory_limit = 128M\nextension=php_bawaan.dll\n");
            File.WriteAllText(Path.Combine(palsu, "php.ini"),
                "; punya pengelola lain\nmemory_limit = 999M\nextension=php_punya_orang.dll\n");
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-palsu", Path = palsu, Version = "8.3.0" };

            var profil = new Profile { Name = "Uji ini" };
            profil.PhpIniOverrides["memory_limit"] = "256M";

            var r1 = new ConfigWriter.Result();
            var dir = ConfigWriter.WritePhpIni(profil, php, r1, true);
            Ok("php.ini ditulis ke folder PHP", dir == palsu, dir);

            var cadangan = Path.Combine(palsu, "php.ini.sebelum-phoron");
            Ok("php.ini asli dicadangkan", File.Exists(cadangan));
            Ok("Cadangan berisi berkas asli, bukan hasil Phoron",
                File.ReadAllText(cadangan).Contains("punya pengelola lain"));

            var isi1 = File.ReadAllText(Path.Combine(palsu, "php.ini"));
            Ok("Nilai dari profil dipakai", isi1.Contains("256M"));
            Ok("Ekstensi pengelola lain dimatikan",
                !isi1.Contains("\nextension=php_punya_orang.dll"));

            // Ditulis dua kali: berkas tidak boleh menumpuk karena memakai
            // keluarannya sendiri sebagai dasar.
            ConfigWriter.WritePhpIni(profil, php, new ConfigWriter.Result(), true);
            var isi2 = File.ReadAllText(Path.Combine(palsu, "php.ini"));
            Ok("Penulisan kedua tidak menggandakan isi", isi1.Length == isi2.Length,
                isi1.Length + " lalu " + isi2.Length);
            Ok("Cadangan tidak ditimpa hasil Phoron",
                File.ReadAllText(cadangan).Contains("punya pengelola lain"));

            // Folder PHP di luar bin milik Phoron harus memicu peringatan.
            var r3 = new ConfigWriter.Result();
            ConfigWriter.WritePhpIni(profil, php, r3, true);
            Ok("Folder PHP pinjaman diperingatkan",
                r3.Warnings.Any(w => w.Contains("TIDAK milik Phoron")),
                string.Join(" | ", r3.Warnings));

            // Baku (mati) tetap menulis ke etc\ dan tidak menyentuh folder PHP.
            File.WriteAllText(Path.Combine(palsu, "php.ini"), "; disentuh pengelola lain\n");
            var r4 = new ConfigWriter.Result();
            var dir4 = ConfigWriter.WritePhpIni(profil, php, r4, false);
            Ok("Baku menulis ke etc\\php", dir4 == Path.Combine(Paths.Etc, "php", php.Id), dir4);
            Ok("Baku tidak menyentuh php.ini folder PHP",
                File.ReadAllText(Path.Combine(palsu, "php.ini")) == "; disentuh pengelola lain\n");

            Directory.Delete(palsu, true);
        }

        static void UjiBeranda()
        {
            Bagian("Beranda Phoron");
            var profil = new Profile { Name = "Uji beranda", HttpPort = 8080, MySqlPort = 3307 };
            var situs = new List<Site>
            {
                new Site { Folder = "toko", Path = @"C:\proyek\toko", HostName = "toko.test" },
                // Nama yang memuat petik dan garis miring terbalik: satu saja
                // cukup merusak seluruh halaman kalau kutipnya tidak diamankan.
                new Site { Folder = "d'Art" + @"\beta", Path = @"C:\proyek\d'Art", HostName = "d-art.test" },
            };
            var php = new BinPackage { Kind = BinKind.Php, Id = "php-uji", Version = "8.3.0" };
            Beranda.Tulis(profil, situs, php, null, null);

            var berkas = Path.Combine(Beranda.Folder, "index.php");
            Ok("Beranda ditulis ke etc\\dashboard", File.Exists(berkas), berkas);
            var isi = File.ReadAllText(berkas);
            Ok("Nama profil ikut disuntikkan", isi.Contains("Uji beranda"));
            Ok("Port situs mengikuti profil", isi.Contains("http://toko.test:8080/"));
            Ok("Petik dalam nama diamankan", isi.Contains(@"d\'Art"), Baris(isi, "  array('nama'"));
            Ok("Beranda TIDAK ditulis ke folder proyek",
                !File.Exists(Path.Combine(SiteScanner.DocumentRoot(profil), "index.php"))
                || !File.ReadAllText(Path.Combine(SiteScanner.DocumentRoot(profil), "index.php")).Contains("Uji beranda"));

            var daftarPhp = Nyata().Where(x => x.Kind == BinKind.Php).ToList();
            if (daftarPhp.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }
            foreach (var v in daftarPhp)
            {
                var res = Shell.Run(v.MainExe, "-n -l \"" + berkas + "\"", v.Path, 30000);
                Ok(v.Version + ": beranda lolos php -l", res.Ok, res.All.Trim());
            }
        }

        static void UjiHalamanSambutan()
        {
            Bagian("Halaman sambutan");
            var php = Nyata().Where(p => p.Kind == BinKind.Php).ToList();
            if (php.Count == 0) { Console.WriteLine("     dilewati: tidak ada PHP terpasang"); return; }

            var file = Path.Combine(Paths.Tmp, "sambutan.php");
            File.WriteAllText(file, Engine.HalamanSambutan(), new System.Text.UTF8Encoding(false));

            // Halaman ini ikut dikirim ke pengguna, jadi harus sah di SEMUA versi
            // PHP yang bisa dipilih - termasuk 5.6, yang tidak mengenal "??".
            foreach (var v in php)
            {
                var res = Shell.Run(v.MainExe, "-n -l \"" + file + "\"", v.Path, 30000);
                Ok(v.Version + ": halaman sambutan lolos php -l", res.Ok, res.All.Trim());
            }
        }

        static void UjiUmpanAtom()
        {
            Bagian("Umpan Atom rilis");
            // Umpan ini dilayani github.com, bukan api.github.com, jadi TIDAK
            // tunduk pada batas 60 permintaan per jam - itulah sebabnya ia jadi
            // jalur utama dan token GitHub tidak dibutuhkan siapa pun.
            var contoh =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<feed xmlns=\"http://www.w3.org/2005/Atom\">\n" +
                "  <title>Release notes from Phoron</title>\n" +
                "  <entry>\n" +
                "    <id>tag:github.com,2008:Repository/1365049606/v1.20.0</id>\n" +
                "    <link rel=\"alternate\" type=\"text/html\" " +
                "href=\"https://github.com/Cyserrex/Phoron/releases/tag/v1.20.0\"/>\n" +
                "    <title>Phoron 1.20.0</title>\n" +
                "    <content type=\"html\">&lt;p&gt;&lt;strong&gt;Full Changelog&lt;/strong&gt;: v1.19.0...v1.20.0&lt;/p&gt;</content>\n" +
                "  </entry>\n" +
                "  <entry>\n" +
                "    <link rel=\"alternate\" type=\"text/html\" " +
                "href=\"https://github.com/Cyserrex/Phoron/releases/tag/v1.19.0\"/>\n" +
                "    <title>Phoron 1.19.0</title>\n" +
                "  </entry>\n" +
                "</feed>";

            var h = Updater.UraiAtom(contoh);
            Ok("Tidak ada galat pada umpan yang sah", h.Galat == null, h.Galat);
            Ok("Versi diambil dari entri PERTAMA, bukan sembarang entri",
               h.Versi == "1.20.0", h.Versi);
            Ok("Awalan v dibuang dari nomor versi", !h.Versi.StartsWith("v"), h.Versi);
            Ok("Halaman rilis menunjuk tag yang benar",
               h.UrlHalaman.EndsWith("/releases/tag/v1.20.0"), h.UrlHalaman);
            // Umpan Atom tidak menyebut berkas aset, jadi alamatnya disusun dari
            // pola penamaan CI. Kalau pola itu berubah, uji ini yang berbunyi.
            Ok("Alamat installer disusun sesuai pola CI",
               h.UrlInstaller == "https://github.com/Cyserrex/Phoron/releases/download/"
                                 + "v1.20.0/Phoron-1.20.0-Setup.exe", h.UrlInstaller);
            Ok("Alamat installer memakai github.com, BUKAN api.github.com",
               h.UrlInstaller.IndexOf("api.github.com", StringComparison.OrdinalIgnoreCase) < 0);
            Ok("Catatan rilis dibersihkan dari tag HTML",
               h.Catatan.IndexOf('<') < 0 && h.Catatan.IndexOf("Full Changelog", StringComparison.Ordinal) >= 0,
               h.Catatan);

            Ok("Umpan kosong dilaporkan sebagai galat", Updater.UraiAtom("").Galat != null);
            Ok("Teks sampah dilaporkan sebagai galat, bukan versi karangan",
               Updater.UraiAtom("bukan xml sama sekali").Galat != null);

            // Perbandingan versi tetap dipakai jalur ini.
            Ok("Versi umpan dibandingkan dengan yang terpasang",
               Updater.LebihBaru("1.20.1", "1.20.0") && !Updater.LebihBaru("1.20.0", "1.20.0"));
        }

        static void UjiRiwayatLog()
        {
            Bagian("Riwayat Aktivitas");
            // Gejala yang diuji: panel Aktivitas mendadak kosong sepulang dari
            // tab lain. Sebabnya halaman Beranda dibuat ulang tiap navigasi,
            // sementara antriannya dulu tinggal DI DALAM halaman itu.
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-riwayat-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                var e = new Engine();

                Ok("Riwayat mula-mula kosong", e.Riwayat().Count == 0);

                e.Say("baris pertama");
                e.Say("baris kedua");
                var r = e.Riwayat();
                Ok("Baris tercatat berurutan",
                   r.Count == 2 && r[0].Teks == "baris pertama" && r[1].Teks == "baris kedua",
                   string.Join(" | ", r.Select(x => x.Teks).ToArray()));
                Ok("Waktunya ikut tercatat", r[0].Waktu > DateTime.Now.AddMinutes(-1));

                // Inilah inti perbaikannya: pembaca baru - halaman yang baru
                // dibuat - harus melihat seluruh riwayat, bukan layar kosong.
                var salinan = e.Riwayat();
                Ok("Pembaca baru melihat riwayat yang sudah ada", salinan.Count == 2);

                // Salinan, bukan antrian aslinya: mengubahnya tidak boleh
                // merusak riwayat yang dipegang Engine.
                salinan.Clear();
                Ok("Yang dikembalikan salinan, bukan antrian aslinya", e.Riwayat().Count == 2);

                // Batas 200 harus membuang yang TERTUA, bukan berhenti mencatat.
                for (int i = 0; i < 260; i++) e.Say("baris " + i);
                var penuh = e.Riwayat();
                Ok("Riwayat dibatasi 200 baris", penuh.Count == 200, penuh.Count.ToString());
                Ok("Yang dibuang adalah yang tertua",
                   penuh[penuh.Count - 1].Teks == "baris 259", penuh[penuh.Count - 1].Teks);
                Ok("Baris paling awal sudah tidak ada",
                   !penuh.Any(x => x.Teks == "baris pertama"));

                // Say juga menulis ke berkas; keduanya tidak boleh saling ganggu.
                Ok("phoron.log tetap ditulis",
                   File.Exists(Path.Combine(Paths.Logs, "phoron.log")));

                e.Say(null);
                Ok("Teks null tidak membuat pencatat meledak",
                   e.Riwayat()[e.Riwayat().Count - 1].Teks == "");
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiLogWarna()
        {
            Bagian("Warna log");
            // Baris-baris di bawah ini disalin dari layar Aktivitas yang
            // sebenarnya, bukan dikarang. Aturan berbasis kata kunci mudah sekali
            // salah tangkap, dan salah warna pada baris galat lebih buruk
            // daripada tidak berwarna sama sekali.
            Action<string, JenisPesan, string> cek = (baris, harap, nama) =>
                Ok(nama, LogWarna.Golongkan(baris) == harap,
                   "dapat " + LogWarna.Golongkan(baris) + " untuk: " + baris);

            cek("Konfigurasi Nginx ditolak:", JenisPesan.Galat, "\"ditolak\" = galat");
            cek("nginx: [emerg] CreateFile() \"fastcgi_params\" failed (2: The system cannot find the file specified)",
                JenisPesan.Galat, "[emerg] nginx = galat");
            cek("Apache berhenti seketika (kode 1).", JenisPesan.Galat, "berhenti seketika = galat");
            cek("Cannot load php5apache2_4.dll into server: %1 is not a valid Win32 application.",
                JenisPesan.Galat, "gagal muat modul = galat");

            cek("Peringatan: Nama balas-api.test sudah dipakai C:\\laragon\\www\\balas_api",
                JenisPesan.Peringatan, "\"Peringatan:\" = peringatan");
            cek("[mysql] 2026-09-15T02:42:55 0 [Warning] TIMESTAMP with implicit DEFAULT value is deprecated.",
                JenisPesan.Peringatan, "[Warning] mysqld = peringatan");
            cek("Peringatan: Nama folder \"php-5.6.40-Win32-VC11-x64\" ada di lebih dari satu folder bin",
                JenisPesan.Peringatan, "folder kembar = peringatan");

            cek("Apache 2.4.38 jalan di port 80 (PID 23804).", JenisPesan.Berhasil, "jalan di port = berhasil");
            cek("Konfigurasi profil \"PHP 5.6.40 + Apache 2.4.38\" ditulis ulang.",
                JenisPesan.Berhasil, "ditulis ulang = berhasil");

            cek("MySQL dimatikan.", JenisPesan.Biasa, "baris netral tetap biasa");
            cek("", JenisPesan.Biasa, "baris kosong tidak meledak");

            // Satu baris bisa memuat kata dari dua golongan sekaligus; yang
            // menang harus galat, karena itulah yang dicari orang.
            cek("Peringatan: berkas hosts gagal ditulis", JenisPesan.Galat,
                "galat menang atas peringatan dalam satu baris");

            Ok("Tiap golongan punya warna, kecuali Biasa",
               LogWarna.Heks(JenisPesan.Galat).Length > 0
               && LogWarna.Heks(JenisPesan.Peringatan).Length > 0
               && LogWarna.Heks(JenisPesan.Berhasil).Length > 0
               && LogWarna.Heks(JenisPesan.Biasa).Length == 0);
            Ok("Warnanya berbeda satu sama lain",
               LogWarna.Heks(JenisPesan.Galat) != LogWarna.Heks(JenisPesan.Peringatan)
               && LogWarna.Heks(JenisPesan.Peringatan) != LogWarna.Heks(JenisPesan.Berhasil));
        }

        static void UjiKonfigurasiNginx()
        {
            Bagian("nginx.conf (diuji nginx.exe sungguhan)");
            // Selama ini hanya httpd.conf yang diuji dengan binernya sendiri, dan
            // nginx.conf lolos begitu saja - padahal ia meng-include
            // "fastcgi_params" sebagai nama telanjang, yang membuat nginx menolak
            // start dengan "CreateFile() ... failed (2)". Uji inilah yang
            // seharusnya menangkapnya sejak awal.
            var nginx = Nyata().FirstOrDefault(p => p.Kind == BinKind.Nginx);
            if (nginx == null) { Console.WriteLine("     dilewati: tidak ada Nginx terpasang"); return; }
            var php = Nyata().FirstOrDefault(p => p.Kind == BinKind.Php);

            Directory.CreateDirectory(Path.Combine(Paths.Www, "situs-nginx"));
            File.WriteAllText(Path.Combine(Paths.Www, "situs-nginx", "index.php"), "<?php echo 1;");
            // Nama panjang yang nyata: "bandarmasih-mobile-pm-service.test" 34
            // karakter, sedangkan baku server_names_hash_bucket_size cuma 32.
            // nginx menolak SELURUH konfigurasi, bukan cuma situs itu.
            Directory.CreateDirectory(Path.Combine(Paths.Www, "bandarmasih-mobile-pm-service"));
            File.WriteAllText(Path.Combine(Paths.Www, "bandarmasih-mobile-pm-service", "index.php"),
                              "<?php echo 1;");

            var profil = new Profile
            {
                Name = "Uji Nginx",
                WebServer = "nginx",
                NginxId = nginx.Id,
                PhpId = php != null ? php.Id : "",
                HttpPort = 8080,
            };
            var situs = SiteScanner.Scan(profil);
            var hasil = ConfigWriter.Build(profil, php, null, null, nginx, situs);

            Ok("nginx.conf terbentuk", hasil.NginxConf != null && File.Exists(hasil.NginxConf));
            if (hasil.NginxConf == null) return;

            var isi = File.ReadAllText(hasil.NginxConf);
            Ok("fastcgi_params di-include dengan jalur penuh, bukan nama telanjang",
               !Regex.IsMatch(isi, @"include\s+fastcgi_params\s*;"), "masih ada nama telanjang");
            Ok("Situs bernama panjang ikut terdaftar",
               isi.IndexOf("bandarmasih-mobile-pm-service", StringComparison.OrdinalIgnoreCase) >= 0);
            Ok("Ukuran ember hash cukup untuk nama terpanjang",
               ConfigWriter.EmberHash(new[] { "bandarmasih-mobile-pm-service.test" }) >= 64,
               ConfigWriter.EmberHash(new[] { "bandarmasih-mobile-pm-service.test" }).ToString());
            Ok("Nama sangat panjang menaikkan embernya lagi",
               ConfigWriter.EmberHash(new[] { new string('a', 120) }) >= 128,
               ConfigWriter.EmberHash(new[] { new string('a', 120) }).ToString());
            Ok("Banyak situs menaikkan kapasitas tabel",
               ConfigWriter.MaksHash(500) > ConfigWriter.MaksHash(10));

            foreach (Match m in Regex.Matches(isi, "include\\s+\"([^\"]+)\""))
                Ok("Berkas yang di-include ada: " + Path.GetFileName(m.Groups[1].Value),
                   File.Exists(m.Groups[1].Value), m.Groups[1].Value);

            // Hakim sesungguhnya: nginx sendiri. -t menguraikan seluruh berkas,
            // termasuk setiap include, lalu menolak kalau ada yang tidak ada.
            var res = Shell.Run(nginx.MainExe, "-t -c \"" + hasil.NginxConf + "\" -p \"" + nginx.Path + "\"",
                                nginx.Path, 30000);
            Ok("nginx.exe -t menerima konfigurasi",
               res.All.IndexOf("test is successful", StringComparison.OrdinalIgnoreCase) >= 0,
               res.All);

            // Dua sakelar yang dulu hanya dihormati Apache. Dengan Nginx, log
            // akses tetap ditulis walau "Catat log rinci" mati, dan beranda tidak
            // pernah muncul di http://localhost/ walau sakelarnya menyala.
            Ok("Beranda di akar: alamat akar persis diarahkan ke beranda",
               isi.Contains("location = / { rewrite ^ " + Beranda.Alias + "/index.php last; }"),
               "tidak ada location = /");
            Ok("Beranda di akar: HANYA di server bawaan, bukan di situs proyek",
               Regex.Matches(isi, @"location = / ").Count == 1,
               Regex.Matches(isi, @"location = / ").Count + " kali");
            Ok("Log rinci menyala: access_log ditulis",
               isi.Contains("nginx-access.log") && !isi.Contains("access_log off"), "");

            var hasil2 = ConfigWriter.Build(profil, php, null, null, nginx, situs,
                                            false, false, false, true);
            var isi2 = File.ReadAllText(hasil2.NginxConf);
            Ok("Log rinci mati: access_log off", isi2.Contains("access_log off;"),
               "log akses tetap ditulis walau sakelarnya mati");
            Ok("Beranda di akar mati: tidak ada pengalihan", !isi2.Contains("location = /"), "");
            var res2 = Shell.Run(nginx.MainExe, "-t -c \"" + hasil2.NginxConf + "\" -p \"" + nginx.Path + "\"",
                                 nginx.Path, 30000);
            Ok("nginx.exe -t menerima konfigurasi dengan kedua sakelar mati",
               res2.All.IndexOf("test is successful", StringComparison.OrdinalIgnoreCase) >= 0, res2.All);
        }

        static void UjiKonfigurasiApache()
        {
            Bagian("httpd.conf (diuji httpd.exe sungguhan)");
            var pkgs = Nyata();
            var apaches = pkgs.Where(p => p.Kind == BinKind.Apache).ToList();
            var phps = pkgs.Where(p => p.Kind == BinKind.Php).ToList();
            if (apaches.Count == 0) { Console.WriteLine("     dilewati: tidak ada Apache terpasang"); return; }

            Directory.CreateDirectory(Path.Combine(Paths.Www, "situs-uji"));
            File.WriteAllText(Path.Combine(Paths.Www, "situs-uji", "index.php"), "<?php echo 1;");

            foreach (var apache in apaches)
            {
                // Tiap Apache dipasangkan dengan PHP yang ARSITEKTURNYA sepadan
                // dan toolsetnya sama - aturan yang sama persis dipakai
                // ProfileStore.PickApache untuk pengguna. Mencocokkan toolset
                // saja pernah memasangkan PHP x86 milik XAMPP dengan Apache x64,
                // dan httpd menolaknya dengan "%1 is not a valid Win32
                // application" yang tidak menyebut sebabnya.
                var php = phps.FirstOrDefault(p => ProfileStore.ArsitekturSepadan(p.Arch, apache.Arch)
                                               && string.Equals(p.Compiler, apache.Compiler,
                                                                StringComparison.OrdinalIgnoreCase));
                var profil = new Profile
                {
                    Name = "Uji " + apache.Id,
                    ApacheId = apache.Id,
                    PhpId = php != null ? php.Id : "",
                    HttpPort = 8080,
                    HttpsPort = 8443,
                };
                var situs = SiteScanner.Scan(profil);
                var hasil = ConfigWriter.Build(profil, php, apache, null, null, situs);

                Ok(apache.Id + ": httpd.conf terbentuk",
                    hasil.HttpdConf != null && File.Exists(hasil.HttpdConf));
                if (hasil.HttpdConf == null) continue;

                var isi = File.ReadAllText(hasil.HttpdConf);
                Ok(apache.Id + ": ServerRoot menunjuk paket yang dipilih",
                    isi.Contains(Paths.Fwd(apache.Path)));
                Ok(apache.Id + ": port profil dipakai", isi.Contains("Listen 8080"));
                Ok(apache.Id + ": tidak ada Include ke luar milik pengelola lain",
                    !isi.Contains("C:/laragon/etc") && !isi.Contains("C:\\laragon\\etc"));

                if (php != null)
                {
                    var modPhp = File.ReadAllText(Path.Combine(Paths.EtcApache, "mod_php.conf"));
                    Ok(apache.Id + ": mod_php menunjuk DLL PHP " + php.Version,
                        modPhp.Contains(BinScanner.ApacheModuleName(php)));
                }

                // Hakim sesungguhnya.
                var res = Shell.Run(Path.Combine(apache.Path, "bin", "httpd.exe"),
                    "-f \"" + hasil.HttpdConf + "\" -d \"" + apache.Path + "\" -t",
                    apache.Path, 60000, ServiceManager.EnvFor(php));
                Ok(apache.Id + ": httpd.exe -t menerima konfigurasi", res.Ok, res.All.Trim());

                // Vhost otomatis harus benar-benar dikenali Apache, bukan sekadar
                // ada berkasnya.
                var vhosts = Shell.Run(Path.Combine(apache.Path, "bin", "httpd.exe"),
                    "-f \"" + hasil.HttpdConf + "\" -d \"" + apache.Path + "\" -S",
                    apache.Path, 60000, ServiceManager.EnvFor(php));
                Ok(apache.Id + ": vhost situs-uji.test terdaftar",
                    vhosts.All.Contains("situs-uji.test"), vhosts.All.Trim());
            }

            // Vhost milik folder yang sudah dihapus harus ikut hilang, kalau tidak
            // Apache menolak start karena DocumentRoot-nya tidak ada lagi.
            var sisa = Directory.GetFiles(Paths.SitesEnabled, "auto.*.conf");
            Directory.Delete(Path.Combine(Paths.Www, "situs-uji"), true);
            var apache2 = apaches[0];
            var profil2 = new Profile { ApacheId = apache2.Id, HttpPort = 8080 };
            ConfigWriter.Build(profil2, null, apache2, null, null, SiteScanner.Scan(profil2));
            Ok("Vhost folder yang dihapus ikut dibersihkan",
                !Directory.GetFiles(Paths.SitesEnabled, "auto.*.conf")
                          .Any(f => f.Contains("situs-uji")),
                "sebelumnya " + sisa.Length + " berkas");
        }

        static void UjiHosts()
        {
            Bagian("HostsFile");
            // Berkas hosts sungguhan tidak disentuh; yang diuji adalah penyusunan
            // bloknya, yang merupakan bagian paling gampang salah.
            var contoh = new[]
            {
                "127.0.0.1 localhost",
                "# komentar",
                HostsFile.Begin,
                "127.0.0.1\tlama.test",
                HostsFile.End,
                "10.0.0.1 kantor.internal",
            };
            var tanpaBlok = new List<string>(contoh);
            int b = tanpaBlok.IndexOf(HostsFile.Begin);
            int e = tanpaBlok.IndexOf(HostsFile.End);
            tanpaBlok.RemoveRange(b, e - b + 1);
            Ok("Blok Phoron bisa dipotong utuh", tanpaBlok.Count == 3);
            Ok("Baris milik orang lain tidak ikut terpotong",
                tanpaBlok.Contains("10.0.0.1 kantor.internal")
                && tanpaBlok.Contains("127.0.0.1 localhost"));
            Ok("Penanda blok tidak berubah bentuk",
                HostsFile.Begin.StartsWith("#") && HostsFile.End.StartsWith("#"));
        }

        static void UjiHostsAman()
        {
            Bagian("Keselamatan berkas hosts");
            // Berkas hosts sungguhan TIDAK disentuh: Paths.HostsFile dialihkan ke
            // berkas sementara. Sebelum ada pengalihan itu, perilaku penulisan
            // hosts tidak bisa diuji sama sekali - dan justru bagian itulah yang
            // paling mahal kalau salah.
            var akarLama = Paths.Root;
            var hostsLama = Paths.HostsFile;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-hosts-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                var hosts = Path.Combine(akar, "hosts");
                Paths.HostsFile = hosts;

                var milikPengguna = new[]
                {
                    "127.0.0.1 localhost",
                    "# catatan lama",
                    "10.0.0.1 kantor.internal",
                    "0.0.0.0 iklan.contoh",
                };
                File.WriteAllLines(hosts, milikPengguna);

                HostsFile.Sync(new[] { "toko.test" });
                var sesudah = File.ReadAllLines(hosts);
                Ok("Blok Phoron ditulis", sesudah.Any(l => l.Contains("toko.test")));
                Ok("Baris milik pengguna selamat semuanya",
                   milikPengguna.All(m => sesudah.Contains(m)),
                   string.Join(" | ", sesudah));

                var asli = Path.Combine(HostsFile.FolderCadangan, HostsFile.NamaAsli);
                Ok("Cadangan pertama bernama hosts-asli", File.Exists(asli));
                Ok("Cadangan asli memuat keadaan sebelum Phoron menyentuhnya",
                   File.Exists(asli) && File.ReadAllLines(asli).SequenceEqual(milikPengguna));

                // Sinkron kedua MEMANG mencadangkan: keadaan awalnya sudah
                // berbeda, sebab kini memuat blok Phoron. Itu keadaan baru yang
                // pantas disimpan sebelum ditimpa.
                //
                // Nama kedua ditambahkan supaya memang ADA yang ditimpa. Sinkron
                // dengan daftar yang sama persis tidak lagi menulis apa pun -
                // lihat UjiHostsTanpaUbah - jadi tidak ada pula yang perlu
                // dicadangkan.
                var sesudahSatu = HostsFile.DaftarCadangan().Count;
                HostsFile.Sync(new[] { "toko.test", "kedai.test" });
                Ok("Keadaan yang berubah ikut tercadang",
                   HostsFile.DaftarCadangan().Count == sesudahSatu + 1,
                   sesudahSatu + " -> " + HostsFile.DaftarCadangan().Count);

                // Yang harus dijamin: Apply() jalan tiap start dan tiap ganti
                // profil, jadi sinkron yang tidak mengubah apa pun tidak boleh
                // menumpuk berkas kembar.
                var sebelum = HostsFile.DaftarCadangan().Count;
                HostsFile.Sync(new[] { "toko.test", "kedai.test" });
                HostsFile.Sync(new[] { "toko.test", "kedai.test" });
                Ok("Sinkron berulang tanpa perubahan tidak menumpuk cadangan",
                   HostsFile.DaftarCadangan().Count == sebelum,
                   sebelum + " -> " + HostsFile.DaftarCadangan().Count);

                // ---------------- inilah yang membuktikan kerusakannya ----------------
                // Berkas hosts dikunci sehingga tidak bisa dibaca. Dulu
                // pembacaannya memaafkan dan mengembalikan larik KOSONG; logika
                // pemotongan blok jadi tidak berbuat apa-apa, dan berkasnya
                // ditulis ulang hanya berisi blok Phoron. Seluruh baris milik
                // pengguna lenyap, tanpa satu pun galat terangkat.
                var isiSebelumDikunci = File.ReadAllText(hosts);
                var melempar = false;
                using (new FileStream(hosts, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try { HostsFile.Sync(new[] { "baru.test" }); }
                    catch (Exception) { melempar = true; }
                }
                Ok("Gagal membaca hosts berarti gagal menulis", melempar);
                Ok("Isi hosts tidak berubah sedikit pun saat pembacaannya gagal",
                   File.ReadAllText(hosts) == isiSebelumDikunci);
                Ok("Baris pengguna masih ada sesudah pembacaan yang gagal",
                   milikPengguna.All(m => File.ReadAllLines(hosts).Contains(m)));

                // Batas jumlah cadangan bertanggal.
                for (int i = 0; i < 15; i++)
                    AtomicFile.WriteAllLines(
                        Path.Combine(HostsFile.FolderCadangan, "hosts-lama" + i + ".bak"),
                        new[] { "cadangan lama " + i });
                HostsFile.Sync(new[] { "lain.test" });   // isinya berubah, jadi memangkas
                var bertanggal = HostsFile.DaftarCadangan().Count(c => !c.Asli);
                Ok("Cadangan bertanggal dibatasi sepuluh",
                   bertanggal == HostsFile.CadanganMaks, bertanggal.ToString());
                Ok("Cadangan asli tidak ikut dipangkas", File.Exists(asli));

                // Memulihkan.
                var isiSekarang = File.ReadAllText(hosts);
                HostsFile.Pulihkan(asli);
                Ok("Pulihkan mengembalikan isi cadangan",
                   File.ReadAllLines(hosts).SequenceEqual(milikPengguna),
                   File.ReadAllText(hosts).Replace(Environment.NewLine, " | "));
                Ok("Keadaan sebelum pemulihan ikut dicadangkan lebih dulu",
                   HostsFile.DaftarCadangan().Any(c => !c.Asli && Sama(c.Path, isiSekarang)));
            }
            finally
            {
                Paths.Root = akarLama;
                Paths.HostsFile = hostsLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static bool Sama(string path, string isi)
        {
            try { return File.ReadAllText(path) == isi; }
            catch { return false; }
        }

        static void UjiLabelVersi()
        {
            Bagian("Label versi");
            // Penjaga kerusakan pengodean: set_version.ps1 pernah membaca berkas
            // sumber dengan codepage ANSI lalu menulisnya sebagai UTF-8, dan
            // titik tengah di label ini berubah jadi "Â·". Kerusakannya hanya
            // terlihat di layar aplikasi, jauh dari skrip penyebabnya.
            var pkg = new BinPackage
            {
                Kind = BinKind.Php,
                Version = "8.3.12",
                Compiler = "vs16",
                Arch = "x64",
                ThreadSafe = true,
            };
            var label = pkg.Label;
            Ok("Label memakai titik tengah yang benar", label == "8.3.12 · VS16 · x64 · TS", label);
            Ok("Label tidak mengandung sisa mojibake", !label.Contains("Â"), label);
        }

        static void UjiRuntimeVc()
        {
            Bagian("Runtime Visual C++");
            // Gejala yang diuji di sini: satu profil gagal sementara profil lain
            // di komputer yang sama jalan mulus, karena tiap toolset butuh
            // redistributable berbeda dan yang kurang tidak pernah disebut
            // namanya oleh Windows.
            var vc11 = new BinPackage
            {
                Kind = BinKind.Php, Id = "php-5.6.40-Win32-VC11-x64", Version = "5.6.40",
                Compiler = "VC11", Arch = "x64", Path = Paths.Tmp,
            };
            var vs16 = new BinPackage
            {
                Kind = BinKind.Php, Id = "php-8.3.12-Win32-vs16-x64", Version = "8.3.12",
                Compiler = "VS16", Arch = "x64", Path = Paths.Tmp,
            };
            var takKenal = new BinPackage
            {
                Kind = BinKind.MySql, Id = "mysql-8.0.30-winx64", Version = "8.0.30",
                Compiler = "", Arch = "x64", Path = Paths.Tmp,
            };

            Ok("VC11 dipetakan ke msvcr110.dll",
               RuntimeVc.Periksa(vc11).Dll == "msvcr110.dll", RuntimeVc.Periksa(vc11).Dll);
            Ok("VS16 dipetakan ke vcruntime140.dll",
               RuntimeVc.Periksa(vs16).Dll == "vcruntime140.dll", RuntimeVc.Periksa(vs16).Dll);
            Ok("Paket tanpa toolset tidak dituntut runtime apa pun",
               !RuntimeVc.Periksa(takKenal).Perlu);

            // Mesin pengembang lazimnya punya semua redistributable. Yang penting
            // dipastikan: kalau ADA, tidak boleh ada tuduhan palsu.
            var h11 = RuntimeVc.Periksa(vc11);
            var adaSungguhan = File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msvcr110.dll"));
            Ok("Runtime yang ADA tidak dilaporkan kurang",
               !adaSungguhan || h11.Ada, "ada=" + adaSungguhan + " lapor=" + h11.Ada);

            // Yang bisa dipastikan di mesin mana pun: DLL karangan pasti tidak ada,
            // dan pesannya harus menyebut nama paket yang dicari orang, bukan
            // sekadar nama DLL-nya.
            var palsu = new BinPackage
            {
                Kind = BinKind.Apache, Id = "httpd-2.4.38-win64-VC9", Version = "2.4.38",
                Compiler = "VC9", Arch = "x64", Path = Paths.Tmp,
            };
            var hp = RuntimeVc.Periksa(palsu);
            if (!File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msvcr90.dll")))
            {
                Ok("Runtime yang tidak ada dilaporkan kurang", !hp.Ada);
                Ok("Pesannya menyebut nama paket Microsoft, bukan cuma nama DLL",
                   hp.Pesan.IndexOf("Visual C++ 2008", StringComparison.OrdinalIgnoreCase) >= 0, hp.Pesan);
            }

            Ok("Redistributable yang sama tidak diadukan dua kali",
               RuntimeVc.PeriksaSemua(vs16, vs16).Count <= 1);
        }

        static void UjiOracle()
        {
            Bagian("Oracle Instant Client");
            // PE sungguhan berarsitektur pasti, tersedia di setiap Windows 64-bit:
            // System32 berisi x64, SysWOW64 berisi x86. Memakai berkas palsu tidak
            // membuktikan apa pun - pembaca header PE akan mengembalikan "" dan
            // "" selalu dianggap sepadan.
            var x64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                   "System32", "kernel32.dll");
            var x86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                   "SysWOW64", "kernel32.dll");
            if (!File.Exists(x64) || !File.Exists(x86))
            { Console.WriteLine("     dilewati: bukan Windows 64-bit"); return; }

            Ok("Pembaca PE membedakan x64 dan x86",
               BinProbe.Arsitektur(x64) == "x64" && BinProbe.Arsitektur(x86) == "x86",
               BinProbe.Arsitektur(x64) + " / " + BinProbe.Arsitektur(x86));

            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-oracle-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var d86 = Path.Combine(akar, "client86");
                var d64 = Path.Combine(akar, "client64");
                Directory.CreateDirectory(d86);
                Directory.CreateDirectory(d64);
                File.Copy(x86, Path.Combine(d86, "oci.dll"));
                File.Copy(x64, Path.Combine(d64, "oci.dll"));

                var phpX64 = new BinPackage
                {
                    Kind = BinKind.Php, Id = "php-uji", Version = "5.6.40",
                    Arch = "x64", Path = akar, MainExe = x64,
                };

                var hanya86 = Oracle.Periksa(phpX64, d86);
                Ok("Client x86 saja dengan PHP x64 dinyatakan TIDAK layak",
                   !hanya86.Layak && hanya86.Ada, hanya86.Pesan);
                Ok("Pesannya menyebut arsitektur yang harus dipasang",
                   hanya86.Pesan.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0);

                // Inti uji ini: Windows MELEWATI client yang tidak sepadan dan
                // meneruskan pencarian - dibuktikan dengan php.exe sungguhan.
                // Berhenti di yang pertama akan menuduh keadaan yang sehat.
                var duaduanya = Oracle.Periksa(phpX64, d86 + ";" + d64);
                Ok("x86 di depan tapi x64 ada di belakang tetap dinyatakan layak",
                   duaduanya.Layak, duaduanya.Pesan);
                Ok("Yang dilaporkan adalah client yang sepadan, bukan yang pertama",
                   duaduanya.JalurDll != null
                   && duaduanya.JalurDll.IndexOf("client64", StringComparison.OrdinalIgnoreCase) >= 0,
                   duaduanya.JalurDll);

                var kosong = Oracle.Periksa(phpX64, Path.Combine(akar, "tidak-ada"));
                Ok("Tanpa client mana pun dinyatakan tidak layak", !kosong.Layak && !kosong.Ada);
                Ok("Pesannya menyuruh memasang, bukan menyalahkan DLL ekstensi",
                   kosong.Pesan.IndexOf("Instant Client", StringComparison.OrdinalIgnoreCase) >= 0);

                Ok("Nama ekstensi Oracle dikenali",
                   Oracle.AdalahEkstensiOracle("oci8_11g") && Oracle.AdalahEkstensiOracle("pdo_oci")
                   && !Oracle.AdalahEkstensiOracle("mysqli"));
            }
            finally { try { Directory.Delete(akar, true); } catch { } }
        }

        static void UjiTataLetakBin()
        {
            Bagian("Tata letak folder bin");
            // Tiap pengelola menata foldernya sendiri-sendiri. Yang menentukan
            // sebuah folder itu paket atau bukan adalah ADA TIDAKNYA exe, bukan
            // namanya - jadi tata letak baru tidak perlu ditambahkan satu per satu.
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-binlayout-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Action<string> buat = jalur =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jalur));
                    File.WriteAllText(jalur, "");
                };

                // Gaya Laragon: <akar>\php\<folder berversi>
                buat(Path.Combine(akar, @"laragon\php\php-8.3.12-Win32-vs16-x64\php.exe"));
                // Folder versi langsung di akar
                buat(Path.Combine(akar, @"lepas\php-7.4.22-Win32-VC15-x64\php.exe"));
                // Gaya XAMPP: folder tanpa nomor versi sama sekali
                buat(Path.Combine(akar, @"xampp\php\php.exe"));
                buat(Path.Combine(akar, @"xampp\apache\bin\httpd.exe"));
                buat(Path.Combine(akar, @"xampp\mysql\bin\mysqld.exe"));
                // Gaya WAMP: satu tingkat lebih dalam, di bawah "bin"
                buat(Path.Combine(akar, @"wamp64\bin\php\php8.1.0\php.exe"));
                buat(Path.Combine(akar, @"wamp64\bin\apache\apache2.4.51\bin\httpd.exe"));
                // Jebakan: folder proyek pengguna tidak boleh ikut dirayapi.
                buat(Path.Combine(akar, @"wamp64\www\proyek-saya\php.exe"));

                Func<string, List<BinPackage>> pindai =
                    r => BinScanner.ScanAll(new[] { Path.Combine(akar, r) });

                var laragon = pindai("laragon");
                Ok("Gaya Laragon terbaca",
                   laragon.Any(x => x.Kind == BinKind.Php && x.Version == "8.3.12"),
                   laragon.Count + " paket");

                var lepas = pindai("lepas");
                Ok("Folder versi langsung di akar terbaca",
                   lepas.Any(x => x.Kind == BinKind.Php && x.Version == "7.4.22"),
                   lepas.Count + " paket");

                var xampp = pindai("xampp");
                Ok("Gaya XAMPP: PHP terbaca walau folder tak berversi",
                   xampp.Any(x => x.Kind == BinKind.Php), xampp.Count + " paket");
                Ok("Gaya XAMPP: Apache terbaca",
                   xampp.Any(x => x.Kind == BinKind.Apache), xampp.Count + " paket");
                Ok("Gaya XAMPP: MySQL terbaca",
                   xampp.Any(x => x.Kind == BinKind.MySql), xampp.Count + " paket");

                var wamp = pindai("wamp64");
                Ok("Gaya WAMP: PHP di bawah bin terbaca",
                   wamp.Any(x => x.Kind == BinKind.Php && x.Version == "8.1.0"),
                   string.Join(", ", wamp.Select(x => x.Kind + ":" + x.Id).ToArray()));
                Ok("Gaya WAMP: Apache di bawah bin terbaca",
                   wamp.Any(x => x.Kind == BinKind.Apache && x.Version == "2.4.51"),
                   string.Join(", ", wamp.Select(x => x.Kind + ":" + x.Id).ToArray()));
                Ok("Folder www pengguna TIDAK ikut dirayapi",
                   !wamp.Any(x => x.Path.IndexOf("proyek-saya", StringComparison.OrdinalIgnoreCase) >= 0),
                   string.Join(", ", wamp.Select(x => x.Path).ToArray()));

                // Penjaga arsitektur: header PE dibaca dari exe sungguhan, karena
                // salah arsitektur membuat Apache mati tanpa pesan apa pun.
                var php = Nyata().FirstOrDefault(x => x.Kind == BinKind.Php);
                if (php != null)
                    Ok("Arsitektur terbaca dari header PE exe sungguhan",
                       BinProbe.Arsitektur(php.MainExe) == "x64"
                       || BinProbe.Arsitektur(php.MainExe) == "x86",
                       BinProbe.Arsitektur(php.MainExe));
                Ok("Berkas bukan PE tidak membuat penyelidik meledak",
                   BinProbe.Arsitektur(Path.Combine(akar, @"xampp\php\php.exe")) == "");
            }
            finally { try { Directory.Delete(akar, true); } catch { } }
        }

        static void UjiHostsTool()
        {
            Bagian("HostsTool");
            // Yang dijaga di sini: nama dikumpulkan dari SELURUH profil, bukan
            // cuma yang aktif. Kalau cuma yang aktif, berganti profil berarti
            // minta hak Administrator lagi - persis yang ingin dihindari.
            //
            // BelumTerdaftar() sengaja TIDAK diuji: ia membaca berkas hosts
            // mesin yang sedang dipakai, jadi hasilnya bergantung keadaan mesin.
            var akarLama = Paths.Root;
            var akar = Path.Combine(Path.GetTempPath(),
                                    "phoron-hoststool-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(akar);
                Paths.Root = akar;
                // Pengumpul nama menghormati Virtual Host, dan bawaannya
                // sekarang MATI - tanpa baris ini tidak ada nama yang
                // dikumpulkan, dan yang diuji di bawah jadi tidak berarti.
                File.WriteAllText(Paths.SettingsFile,
                    "[umum]" + Environment.NewLine + "auto_vhost=1" + Environment.NewLine);

                var satu = Path.Combine(akar, "proyek-satu");
                var dua = Path.Combine(akar, "proyek-dua");
                Directory.CreateDirectory(Path.Combine(satu, "alfa"));
                Directory.CreateDirectory(Path.Combine(satu, "beta"));
                Directory.CreateDirectory(Path.Combine(dua, "gama"));

                var pA = new Profile { Name = "A", SiteSuffix = "test" };
                pA.ProjectRoots.Add(satu);
                ProfileStore.Save(pA);

                var pB = new Profile { Name = "B", SiteSuffix = "dev" };
                pB.ProjectRoots.Add(dua);
                ProfileStore.Save(pB);

                // Profil ketiga menunjuk folder yang sama dengan A: namanya
                // harus menyatu, bukan berlipat.
                var pC = new Profile { Name = "C", SiteSuffix = "test" };
                pC.ProjectRoots.Add(satu);
                ProfileStore.Save(pC);

                var nama = HostsTool.SemuaNamaSitus();
                var gabung = string.Join(", ", nama.ToArray());

                Ok("Nama dari profil pertama ikut", nama.Contains("alfa.test"), gabung);
                Ok("Nama dari profil KEDUA ikut juga", nama.Contains("gama.dev"), gabung);
                Ok("Akhiran tiap profil dihormati", nama.Contains("beta.test"), gabung);
                Ok("Nama yang sama tidak berlipat", nama.Count == 3, gabung);
                Ok("Terurut", nama.SequenceEqual(nama.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), gabung);
                Ok("localhost tidak ikut", !nama.Contains("localhost"), gabung);
            }
            finally
            {
                Paths.Root = akarLama;
                try { Directory.Delete(akar, true); } catch { }
            }
        }

        static void UjiBahasa()
        {
            Bagian("Bahasa");

            // Kamus dibangun di konstruktor statis. Satu kunci ganda saja membuat
            // SELURUH aplikasi mati begitu teks pertama diterjemahkan, dan
            // kompilasi tidak melihatnya sama sekali - sudah pernah terjadi.
            string ledak = null;
            try
            {
                foreach (var kode in Lang.Semua)
                {
                    Lang.Pakai(kode);
                    Lang.T("Beranda");
                }
            }
            catch (Exception ex)
            {
                var akar = ex;
                while (akar.InnerException != null) akar = akar.InnerException;
                ledak = akar.Message;
            }
            finally { Lang.Pakai(Lang.Indonesia); }
            Ok("Kamus tiap bahasa terbentuk (tidak ada kunci ganda)", ledak == null, ledak);

            Lang.Pakai(Lang.Inggris);
            Ok("Teks diterjemahkan", Lang.T("Beranda") == "Home", Lang.T("Beranda"));
            Ok("Teks tanpa padanan jatuh ke Indonesia",
               Lang.T("Kalimat yang tidak ada di kamus") == "Kalimat yang tidak ada di kamus");
            Lang.Pakai(Lang.Indonesia);
            Ok("Bahasa Indonesia mengembalikan kuncinya sendiri", Lang.T("Beranda") == "Beranda");

            // Parser markup extension WPF memakan "\" di dalam argumen, jadi
            // {loc:T 'etc\catalog.ini'} tampil sebagai "etccatalog.ini" tanpa
            // galat apa pun. Teks berbackslash harus memakai bentuk elemen
            // <loc:T Teks="..."/>.
            var dirApp = CariFolderApp();
            if (dirApp == null)
            {
                Ok("Folder sumber XAML ditemukan", false, "tidak ketemu dari " + AppDomain.CurrentDomain.BaseDirectory);
                return;
            }
            var nakal = new List<string>();
            foreach (var f in Directory.GetFiles(dirApp, "*.xaml", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{loc:T\s+'([^']*)'\}"))
                    if (m.Groups[1].Value.IndexOf('\\') >= 0)
                        nakal.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
            }
            Ok("Tidak ada loc:T berbentuk atribut yang memuat backslash",
               nakal.Count == 0, string.Join(" | ", nakal.ToArray()));

            UjiCakupanTerjemahan(dirApp);
            UjiBanjarTidakBercampur(dirApp);
        }

        /// <summary>
        /// Setiap KALIMAT di layar harus punya padanan di ketiga bahasa.
        ///
        /// Istilah teknis pendek - Port HTTP, vhost, Toolset, "php -m" - memang
        /// sengaja dibiarkan apa adanya; menerjemahkannya justru membuat layar
        /// lebih sulit dibaca, dan itu tertulis sebagai alasan di kepala Lang.cs.
        /// Yang tidak boleh dibiarkan adalah kalimat utuh: satu paragraf
        /// berbahasa Indonesia di tengah layar berbahasa Inggris terbaca sebagai
        /// kerusakan, bukan sebagai pilihan. Batas 25 karakter memisahkan
        /// keduanya - cukup panjang untuk melewatkan istilah, cukup pendek untuk
        /// menangkap kalimat terpendek yang ada.
        ///
        /// Uji ini lahir dari kejadian sungguhan: kalimat di Pengaturan ditulis
        /// ulang waktu kotak Token GitHub dibuang, kamusnya tidak ikut, dan
        /// kalimat itu tampil berbahasa Indonesia di KETIGA bahasa tanpa satu pun
        /// galat - tidak terlihat oleh kompilasi maupun oleh uji muat XAML.
        /// </summary>
        static void UjiCakupanTerjemahan(string dirApp)
        {
            const int ambang = 25;
            var teks = new List<string>();
            foreach (var f in Directory.GetFiles(dirApp, "*.xaml", SearchOption.AllDirectories))
            {
                var isi = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(isi, @"\{loc:T\s+'([^']*)'\}"))
                    teks.Add(WebUtility.HtmlDecode(m.Groups[1].Value));
                foreach (Match m in Regex.Matches(isi, "<loc:T\\s+Teks=\"([^\"]*)\""))
                    teks.Add(WebUtility.HtmlDecode(m.Groups[1].Value));
            }
            var kalimat = teks.Where(t => t.Length >= ambang).Distinct().ToList();
            // Kalau pengumpulannya sendiri rusak - regexnya tidak cocok lagi -
            // daftar kalimatnya jadi kosong dan uji di bawah LULUS tanpa
            // memeriksa apa pun. Jumlahnya ikut diperiksa supaya itu ketahuan.
            Ok("Kalimat antarmuka terkumpul dari XAML", kalimat.Count >= 20, kalimat.Count.ToString());

            foreach (var kode in new[] { Lang.Inggris, Lang.Jawa, Lang.Banjar })
            {
                // Ditanya ADA tidaknya kunci di kamus, bukan berubah tidaknya
                // teksnya. Versi pertama uji ini membandingkan Lang.T(t) dengan
                // t, dan itu menuduh padanan yang justru sudah benar: sebagian
                // padanan Banjar memang sama persis dengan bahasa Indonesianya,
                // seperti "Buka www". Ketahuan begitu tombol "Buka folder
                // cadangan hosts" ditambahkan.
                var bocor = kalimat.Where(t => !Lang.Punya(kode, t)).ToList();
                Ok("Tiap kalimat punya padanan " + Lang.NamaBahasa(kode),
                   bocor.Count == 0,
                   string.Join(" | ", bocor.Select(
                       t => t.Substring(0, Math.Min(60, t.Length))).ToArray()));
            }
        }

        /// <summary>
        /// Kata Indonesia yang punya padanan Banjar dan karena itu tidak boleh
        /// muncul di kalimat Banjar (nang, wan, matan, gasan, kada, atawa, amun,
        /// samunyaan, barakas, daptar, surang, kawa, lawan, hanyar, rancak,
        /// suah, ngaran, laman, kulihan, janis, musti).
        ///
        /// Dijadikan medan bersama karena dipakai dua penjaga: yang memindai
        /// XAML, dan yang memindai seluruh kamus.
        /// </summary>
        static readonly string[] KataTugasIndonesia =
        {
            "yang", "dan", "dari", "untuk", "tidak", "atau", "kalau", "semua",
            "berkas", "daftar", "sendiri", "bisa", "dengan", "baru", "sering",
            "pernah", "nama", "halaman", "hasil", "jenis", "harus", "setiap",
        };

        static void UjiBanjarTidakBercampur(string dirApp)
        {
            // Keluhan yang menimbulkan uji ini: bahasa Banjarnya "bercampur".
            // Penyebabnya bukan kata yang salah, melainkan kata tugas Indonesia
            // yang lolos di tengah kalimat - satu "yang" atau "dan" saja sudah
            // membuat seluruh kalimat terasa bukan Banjar. Kata di bawah ini
            // punya padanan Banjar yang wajib dipakai (nang, wan, matan, gasan,
            // kada, atawa, amun, samunyaan, barakas, daptar, surang, kawa,
            // lawan, hanyar, rancak, suah, ngaran, laman, kulihan, janis).
            var terlarang = KataTugasIndonesia;

            Lang.Pakai(Lang.Banjar);
            var bocor = new List<string>();
            foreach (var f in Directory.GetFiles(dirApp, "*.xaml", SearchOption.AllDirectories))
            {
                var isi = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(isi, @"\{loc:T\s+'([^']*)'\}"))
                    PeriksaBocor(m.Groups[1].Value, terlarang, bocor);
                foreach (Match m in Regex.Matches(isi, "<loc:T\\s+Teks=\"([^\"]*)\"\\s*/>"))
                    PeriksaBocor(WebUtility.HtmlDecode(m.Groups[1].Value), terlarang, bocor);
            }
            Lang.Pakai(Lang.Indonesia);

            Ok("Terjemahan Banjar tidak bercampur kata tugas Indonesia",
               bocor.Count == 0, string.Join(" | ", bocor.ToArray()));
        }

        static void PeriksaBocor(string kunci, string[] terlarang, List<string> bocor)
        {
            var hasil = Lang.T(kunci);
            if (hasil == kunci) return;   // memang belum/tidak perlu diterjemahkan
            foreach (var w in terlarang)
                if (Regex.IsMatch(hasil, "(?<![A-Za-z])" + w + "(?![A-Za-z])", RegexOptions.IgnoreCase))
                    bocor.Add("\"" + w + "\" di: " + hasil.Substring(0, Math.Min(50, hasil.Length)));
        }

        /// <summary>
        /// Menekan ikon Phoron di taskbar sementara Phoron sudah berjalan dan
        /// menyusut ke baki sistem harus MEMUNCULKAN jendelanya, bukan menegur
        /// pengguna dengan kotak "Phoron sudah berjalan".
        ///
        /// Uji ini memerankan instans pertama: ia memegang mutex instans tunggal
        /// dan memasang event yang ditunggu jendela sungguhan, lalu menjalankan
        /// Phoron.exe sebagai salinan kedua. Yang dibuktikan: salinan kedua
        /// mengirim sinyalnya lalu keluar sendiri, tanpa kotak pesan yang
        /// menggantung menunggu diklik.
        /// </summary>
        static void UjiSalinanKedua()
        {
            Bagian("Salinan kedua Phoron");

            var exe = CariPhoronExe();
            if (exe == null) { Console.WriteLine("     dilewati: Phoron.exe belum dibangun"); return; }

            // Kalau Phoron milik pengguna sedang berjalan, uji ini akan
            // membangunkan JENDELANYA - jendela sungguhan yang melompat ke depan
            // di tengah pekerjaan orang. Lebih baik dilewati.
            Mutex milikOrang;
            if (Mutex.TryOpenExisting("Phoron.SingleInstance", out milikOrang))
            {
                milikOrang.Close();
                Console.WriteLine("     dilewati: Phoron sedang berjalan di mesin ini");
                return;
            }

            bool baru;
            using (var mutex = new Mutex(true, "Phoron.SingleInstance", out baru))
            using (var sinyal = new EventWaitHandle(false, EventResetMode.AutoReset, "Phoron.Tampilkan"))
            {
                if (!baru) { Console.WriteLine("     dilewati: mutex direbut proses lain"); return; }

                Process p = null;
                try
                {
                    p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
                    bool diminta = sinyal.WaitOne(TimeSpan.FromSeconds(15));
                    Ok("Salinan kedua meminta jendela dimunculkan, bukan menampilkan kotak pesan", diminta);

                    bool keluar = p.WaitForExit(10000);
                    Ok("Salinan kedua keluar sendiri", keluar,
                       keluar ? "" : "masih hidup - kemungkinan besar tergantung di kotak pesan modal");
                    Ok("Salinan kedua keluar dengan kode 0", keluar && p.ExitCode == 0,
                       keluar ? "kode " + p.ExitCode : "belum keluar");
                }
                finally
                {
                    if (p != null)
                    {
                        try { if (!p.HasExited) p.Kill(); } catch { }
                        try { p.Dispose(); } catch { }
                    }
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        /// <summary>
        /// Deteksi header HSTS dari server pengembangan Node.
        ///
        /// Yang membuat gejalanya membingungkan: sebabnya di satu proyek Node
        /// ber-HTTPS, akibatnya di situs PHP pada port 80 - dan baru terasa
        /// sesudah server Node-nya dimatikan. Aturan mana yang berlaku dan mana
        /// yang tidak diuji satu per satu di sini, sebab peringatan palsu pada
        /// hal seperti ini lebih merusak daripada diam.
        /// </summary>
        static void UjiHsts()
        {
            Bagian("Deteksi HSTS localhost");

            // HTTPS + nama localhost: inilah satu-satunya bentuk yang dicatat browser.
            Ok("https://localhost:3000 dianggap berisiko", HstsPeriksa.Berlaku("https://localhost:3000"));
            Ok("Subdomain .localhost ikut dianggap berisiko", HstsPeriksa.Berlaku("https://app.localhost:3000/"));

            // Header HSTS lewat HTTP polos DIABAIKAN browser; memperingatkan di
            // sini hanya akan membuat orang mengejar hantu.
            Ok("http:// polos tidak dianggap berisiko", !HstsPeriksa.Berlaku("http://localhost:3000"));

            // Browser tidak menyimpan HSTS untuk alamat IP telanjang.
            Ok("IP telanjang tidak dianggap berisiko", !HstsPeriksa.Berlaku("https://127.0.0.1:3000"));
            Ok("[::1] tidak dianggap berisiko", !HstsPeriksa.Berlaku("https://[::1]:3000"));

            Ok("Host lain bukan urusan Phoron", !HstsPeriksa.Berlaku("https://contoh.test/"));
            Ok("Alamat ngawur tidak meledak", !HstsPeriksa.Berlaku("bukan alamat"));

            Ok("max-age dibaca jadi hari",
               HstsPeriksa.HariDariMaxAge("max-age=63072000; includeSubDomains") == 730,
               "dapat " + HstsPeriksa.HariDariMaxAge("max-age=63072000; includeSubDomains"));
            Ok("max-age tanpa nilai tidak meledak", HstsPeriksa.HariDariMaxAge("max-age") == 0);
            Ok("Header kosong = 0 hari", HstsPeriksa.HariDariMaxAge(null) == 0);

            Ok("Tanpa header, tidak ada keluhan", HstsPeriksa.Keluhan("proyek", "https://localhost:3000", null) == null);

            var keluhan = HstsPeriksa.Keluhan("izin_keluar_peg", "https://localhost:3000",
                                              "max-age=63072000; includeSubDomains");
            Ok("Keluhan menyebut nama proyeknya",
               keluhan != null && keluhan.Contains("izin_keluar_peg"));
            // Tanpa kalimat ini peringatannya tidak menjawab pertanyaan yang
            // sebenarnya dipunyai pengguna: "kenapa PHP saya mati?"
            Ok("Keluhan menyebut akibatnya pada port 80",
               keluhan != null && keluhan.Contains("port 80"));

            // Baris ini harus terlihat sebagai peringatan di keempat bahasa.
            var semula = Lang.Kode;
            try
            {
                foreach (var kode in Lang.Semua)
                {
                    Lang.Pakai(kode);
                    var pesan = HstsPeriksa.Keluhan("proyek", "https://localhost:3000", "max-age=31536000");
                    Ok("Peringatan HSTS berwarna peringatan (" + kode + ")",
                       LogWarna.Golongkan(pesan) == JenisPesan.Peringatan,
                       "digolongkan " + LogWarna.Golongkan(pesan));
                }
            }
            finally { Lang.Pakai(semula); }

            // Pemeriksaannya harus benar-benar TERPASANG. Logika yang benar tapi
            // tidak pernah dipanggil adalah kegagalan yang paling sunyi.
            var mesin = new Engine();
            var medan = typeof(NodeRunner).GetField("UrlFound",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Ok("Engine mendengarkan alamat yang diumumkan proyek Node",
               medan != null && medan.GetValue(mesin.Node) != null);

            // Peringatan harus sampai ke keluaran proyeknya juga, bukan cuma ke
            // catatan Beranda: di situlah mata pengguna berada saat itu.
            string folderTerlapor = null, barisTerlapor = null;
            mesin.Node.Output += (f, b) => { folderTerlapor = f; barisTerlapor = b; };
            mesin.Node.Sisipkan(@"C:\proyek", "halo");
            Ok("Peringatan bisa disisipkan ke keluaran proyek",
               folderTerlapor == @"C:\proyek" && barisTerlapor == "halo");
        }

        static string CariPhoronExe()
        {
            var app = CariFolderApp();
            if (app == null) return null;
            foreach (var rasa in new[] { "Release", "Debug" })
            {
                var calon = Path.Combine(app, Path.Combine("bin", Path.Combine(rasa, Path.Combine("net48", "Phoron.exe"))));
                if (File.Exists(calon)) return calon;
            }
            return null;
        }

        static string CariFolderApp()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null)
            {
                var calon = Path.Combine(d.FullName, Path.Combine("src", "Phoron.App"));
                if (Directory.Exists(calon)) return calon;
                d = d.Parent;
            }
            return null;
        }

        static void UjiNodeApps()
        {
            Bagian("Proyek Node");
            var proyek = Path.Combine(Paths.Root, "proyek-node");
            Directory.CreateDirectory(proyek);
            // package.json sungguhan punya nilai yang memuat kurung kurawal dan
            // tanda kutip di dalamnya - pengurai sederhana gampang tersandung di
            // situ, jadi contoh ini sengaja dibuat menyerupai yang asli.
            File.WriteAllText(Path.Combine(proyek, "package.json"),
                "{\n" +
                "  \"name\": \"contoh\",\n" +
                "  \"scripts\": {\n" +
                "    \"dev\": \"next dev --experimental-https\",\n" +
                "    \"build\": \"astro check && astro build\",\n" +
                "    \"aneh\": \"node -e \\\"console.log({a:1})\\\"\",\n" +
                "    \"start\": \"node server.mjs\"\n" +
                "  },\n" +
                "  \"dependencies\": { \"next\": \"^15.0.0\" }\n" +
                "}\n");

            var skrip = PackageJson.Scripts(proyek);
            Ok("Nama skrip terbaca sesuai urutan",
                skrip.SequenceEqual(new[] { "dev", "build", "aneh", "start" }),
                string.Join(",", skrip));
            Ok("Kurung kurawal di dalam nilai tidak mengacaukan pembacaan",
                skrip.Count == 4, string.Join(",", skrip));
            Ok("Kerangka terdeteksi dari dependensi",
                PackageJson.DetectFramework(proyek) == "next",
                PackageJson.DetectFramework(proyek));
            Ok("Tanpa berkas kunci, pengelolanya npm",
                PackageJson.DetectManager(proyek) == "npm");

            File.WriteAllText(Path.Combine(proyek, "pnpm-lock.yaml"), "");
            Ok("pnpm-lock.yaml menentukan pnpm", PackageJson.DetectManager(proyek) == "pnpm");
            File.Delete(Path.Combine(proyek, "pnpm-lock.yaml"));

            Ok("Folder tanpa package.json bukan proyek Node",
                !PackageJson.LooksLikeNodeProject(Paths.Tmp));
            Ok("Daftar skrip folder bukan proyek itu kosong",
                PackageJson.Scripts(Paths.Tmp).Count == 0);

            // Bolak-balik ke apps.ini - jalur Windows memuat ":" dan "\",
            // keduanya harus selamat sebagai nama seksi.
            var apps = new List<NodeApp>
            {
                new NodeApp { Path = proyek, Name = "Contoh", Script = "build", Manager = "pnpm", NodeId = "node-v18" },
                new NodeApp { Path = @"D:\kerjaan\situs", Script = "dev", Manager = "npm" },
            };
            NodeAppStore.SaveAll(apps);
            var muat = NodeAppStore.LoadAll();
            Ok("Dua proyek tersimpan dan terbaca lagi", muat.Count == 2, muat.Count.ToString());
            var satu = muat.FirstOrDefault(x => x.Path == proyek);
            Ok("Jalur, skrip, pengelola, dan Node bolak-balik utuh",
                satu != null && satu.Name == "Contoh" && satu.Script == "build"
                && satu.Manager == "pnpm" && satu.NodeId == "node-v18",
                satu == null ? "null" : satu.Script + "/" + satu.Manager + "/" + satu.NodeId);
            var dua = muat.FirstOrDefault(x => x.Path == @"D:\kerjaan\situs");
            Ok("Jalur berhuruf drive lain tetap utuh", dua != null, "");
            Ok("Nama tampilan jatuh ke nama folder bila kosong",
                dua != null && dua.DisplayName == "situs",
                dua == null ? "null" : dua.DisplayName);

            File.Delete(Path.Combine(Paths.Root, "apps.ini"));
            Directory.Delete(proyek, true);
        }

        static void UjiPembaruan()
        {
            Bagian("Cek pembaruan");
            // Perbandingan angka per bagian, bukan teks. Secara abjad "1.10.0"
            // lebih kecil daripada "1.9.0" - kalau dibandingkan sebagai teks,
            // pembaruan justru berhenti ditawarkan persis saat versi minor
            // menembus angka sepuluh.
            Ok("1.10.0 lebih baru daripada 1.9.0", Updater.LebihBaru("1.10.0", "1.9.0"));
            Ok("1.7.1 lebih baru daripada 1.7.0", Updater.LebihBaru("1.7.1", "1.7.0"));
            Ok("Versi sama bukan pembaruan", !Updater.LebihBaru("1.7.1", "1.7.1"));
            Ok("Versi lebih tua bukan pembaruan", !Updater.LebihBaru("1.6.0", "1.7.0"));
            Ok("Awalan v diabaikan", Updater.LebihBaru("v1.8.0", "1.7.1"));
            // Versi rakitan membawa ekor "+<sha>"; System.Version menolaknya
            // mentah-mentah, dan tanpa pembersihan ini pengecekan selalu diam.
            Ok("Ekor +sha tidak mengacaukan perbandingan",
                Updater.LebihBaru("1.8.0", "1.7.1+efdc376628af09d14093dd6a1113dcc0"));
            Ok("Teks sampah tidak dianggap pembaruan", !Updater.LebihBaru("entah", "1.0.0"));

            var json = "{\"tag_name\":\"v1.9.0\","
                     + "\"html_url\":\"https://github.com/Cyserrex/Phoron/releases/tag/v1.9.0\","
                     + "\"body\":\"Baris satu\r\nBaris \\\"dua\\\"\","
                     + "\"assets\":[{\"browser_download_url\":\"https://x/Phoron.exe\"},"
                     + "{\"browser_download_url\":\"https://x/Phoron-1.9.0-Setup.exe\"}]}";
            var h = Updater.Urai(json);
            Ok("Versi terurai dari tag_name", h.Versi == "1.9.0", h.Versi);
            Ok("Halaman rilis terurai", h.UrlHalaman.EndsWith("/releases/tag/v1.9.0"), h.UrlHalaman);
            // Rilis memuat beberapa aset; yang diambil harus installer-nya,
            // bukan aset pertama yang kebetulan Phoron.exe.
            Ok("Aset yang diambil adalah installer, bukan exe biasa",
                h.UrlInstaller.EndsWith("Phoron-1.9.0-Setup.exe"), h.UrlInstaller);
            Ok("Catatan rilis dibaca dan escape-nya dipulihkan",
                h.Catatan.Contains("Baris satu") && h.Catatan.Contains("\"dua\""), h.Catatan);
            Ok("Tidak ada galat pada jawaban yang sah", h.Galat == null, h.Galat ?? "");

            var rusak = Updater.Urai("{\"pesan\":\"apa pun\"}");
            Ok("Jawaban tanpa tag_name dilaporkan sebagai galat", rusak.Galat != null);
            Ok("Jawaban kosong dilaporkan sebagai galat", Updater.Urai("").Galat != null);
        }

        static void UjiAutostart()
        {
            Bagian("Autostart");
            // Registri mesin ini TIDAK disentuh - yang diuji adalah penguraian
            // nilai Run, bagian yang menentukan apakah sakelar di Pengaturan
            // tampil menyala atau padam.
            const string exe = @"C:\Phoron\Phoron.exe";
            Ok("Nilai berkutip dengan argumen dikenali",
                Autostart.MenunjukKe("\"" + exe + "\" --tray", exe));
            Ok("Nilai berkutip tanpa argumen dikenali",
                Autostart.MenunjukKe("\"" + exe + "\"", exe));
            Ok("Nilai tanpa kutip dikenali",
                Autostart.MenunjukKe(exe + " --tray", exe));
            Ok("Beda huruf besar-kecil tetap dikenali",
                Autostart.MenunjukKe("\"c:\\phoron\\PHORON.EXE\" --tray", exe));
            Ok("Salinan di folder lain TIDAK dianggap milik kita",
                !Autostart.MenunjukKe("\"D:\\Lain\\Phoron.exe\" --tray", exe));
            Ok("Nilai kosong tidak dianggap terdaftar", !Autostart.MenunjukKe("", exe));
            Ok("Nama nilai registri sama dengan yang ditulis installer",
                Autostart.NamaNilai == "Phoron");
        }

        static void UjiPortCheck()
        {
            Bagian("PortCheck");
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            try
            {
                var pakai = PortCheck.Check(port);
                Ok("Port yang sedang didengarkan terdeteksi terpakai", pakai.InUse);
                Ok("Pesan konflik menyebut nomor port", pakai.Describe().Contains(port.ToString()));
            }
            finally { l.Stop(); }

            // Port yang baru saja dilepas harus kembali terbaca bebas.
            Ok("Port yang sudah dilepas terbaca bebas", PortCheck.IsFree(port));
        }
    }
}
