using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Phoron.Core.Jarak
{
    /// <summary>
    /// Sambungan UI ke phorond lewat named pipe: satu baris JSON per pesan.
    ///
    /// Satu utas pembaca memisahkan dua jenis pesan yang datang: JAWABAN (punya
    /// "id") menyelesaikan Task milik permintaannya, KABAR (tanpa "id") dinaikkan
    /// sebagai event. Penulisan dikunci, jadi permintaan boleh dikirim dari utas
    /// mana pun.
    ///
    /// Pipe yang putus disambung ulang sendiri; daemon yang hilang dijalankan
    /// lagi dari folder Phoron.exe. Yang tidak disambung ulang hanya dua keadaan:
    /// klien sudah dibuang, atau daemon sudah berpamitan.
    /// </summary>
    public sealed class KlienDaemon : IDisposable
    {
        /// <summary>Nama pipe tanpa awalan \\.\pipe\.</summary>
        public readonly string NamaPipe;

        /// <summary>phorond.exe yang dijalankan bila pipe tidak ada; null = jangan jalankan apa pun.</summary>
        public string JalurDaemon;

        /// <summary>Versi UI yang disebut saat halo.</summary>
        public string VersiUi = AppInfo.Version;

        /// <summary>Batas waktu bawaan satu permintaan.</summary>
        public TimeSpan BatasBawaan = TimeSpan.FromSeconds(30);

        /// <summary>Lama menunggu pipe muncul sesudah daemon dijalankan.</summary>
        public TimeSpan BatasTungguDaemon = TimeSpan.FromSeconds(10);

        /// <summary>(jenis, data) - kabar dari daemon, dinaikkan di utas pembaca.</summary>
        public event Action<string, object> Kabar;

        /// <summary>Halo berhasil, termasuk sesudah sambung ulang.</summary>
        public event Action Tersambung;

        /// <summary>Pipe putus; permintaan yang sedang menunggu sudah digagalkan.</summary>
        public event Action Terputus;

        public string VersiDaemon { get; private set; }

        /// <summary>Folder akar yang disebut daemon saat halo; kosong pada daemon 1.42.0.</summary>
        public string AkarDaemon { get; private set; }

        /// <summary>
        /// Folder akar UI ini. Bila diisi, daemon yang menyebut akar lain
        /// ditolak saat jabat tangan (galat akar_beda). Null = tidak diperiksa.
        /// </summary>
        public string AkarHarapan;

        /// <summary>Dua jalur folder sama? Tanpa peka besar-kecil, tanpa garis miring di ujung.</summary>
        public static bool SamaFolder(string a, string b)
        {
            Func<string, string> rapi = p =>
            {
                try { p = Path.GetFullPath(p); } catch { }
                return p.TrimEnd('\\', '/');
            };
            return string.Equals(rapi(a), rapi(b), StringComparison.OrdinalIgnoreCase);
        }

        public bool Aktif { get { return _pipe != null && _siap; } }

        readonly object _kunciTulis = new object();
        readonly object _kunciSambung = new object();
        readonly Dictionary<long, TaskCompletionSource<Jawaban>> _menunggu =
            new Dictionary<long, TaskCompletionSource<Jawaban>>();
        readonly Dictionary<long, Action<Jawaban>> _segera = new Dictionary<long, Action<Jawaban>>();

        /// <summary>Penanda utas pembaca: menunggu jawaban dari utas ini sama dengan menunggu diri sendiri.</summary>
        [ThreadStatic] static bool _diUtasPembaca;
        long _id;
        NamedPipeClientStream _pipe;
        StreamWriter _penulis;
        volatile bool _siap;
        volatile bool _dibuang;
        volatile bool _pamit;
        /// <summary>Daemon menolak jabat tangan (protokol beda): menyambung ulang tidak ada gunanya.</summary>
        volatile bool _ditolak;
        int _menyambungUlang;
        DateTime _terakhirMenjalankan = DateTime.MinValue;
        /// <summary>Tick UTC sampai kapan daemon diharapkan menjalankan ulang dirinya (mulai_ulang_admin).</summary>
        long _mulaiUlangSampai;

        /// <summary>
        /// Daemon akan menjalankan ulang dirinya (mulai_ulang_admin). Selama
        /// <paramref name="lama"/>: kabar pamit dari daemon lama TIDAK
        /// menghentikan sambung ulang (dan tidak diteruskan sebagai kabar,
        /// supaya UI tidak ikut keluar), dan klien tidak menjalankan phorond
        /// sendiri - itu akan membuat daemon kedua tanpa hak Administrator
        /// yang berebut pipe dengan daemon barunya.
        /// </summary>
        public void HarapkanMulaiUlang(TimeSpan lama)
        {
            Interlocked.Exchange(ref _mulaiUlangSampai, (DateTime.UtcNow + lama).Ticks);
        }

        public void BatalMulaiUlang() { Interlocked.Exchange(ref _mulaiUlangSampai, 0); }

        bool MenungguMulaiUlang { get { return DateTime.UtcNow.Ticks < Interlocked.Read(ref _mulaiUlangSampai); } }

        public KlienDaemon() : this(null) { }

        public KlienDaemon(string namaPipe)
        {
            NamaPipe = string.IsNullOrEmpty(namaPipe) ? Protokol.NamaPipeBawaan : namaPipe;
        }

        string JalurPipe { get { return @"\\.\pipe\" + NamaPipe; } }

        // ------------------------------------------------------------ Sambung

        /// <summary>
        /// Sambung dan jabat tangan. Bila pipe tidak ada dan JalurDaemon diisi,
        /// daemon dijalankan lalu ditunggu sampai BatasTungguDaemon. Melempar
        /// GalatDaemon dengan pesan yang siap ditampilkan.
        /// </summary>
        public void Sambung()
        {
            lock (_kunciSambung)
            {
                if (_dibuang) throw new ObjectDisposedException("KlienDaemon");
                if (Aktif) return;
                _pamit = false;
                _ditolak = false;
                var pipe = BukaPipe(TimeSpan.FromMilliseconds(300));
                if (pipe == null && JalankanDaemon())
                    pipe = BukaPipe(BatasTungguDaemon);
                if (pipe == null)
                    throw new GalatDaemon("tidak_ada", JalurDaemon != null && !File.Exists(JalurDaemon)
                        ? Lang.T("phorond.exe tidak ditemukan di {0}.", JalurDaemon)
                        : Lang.T("Daemon Phoron (phorond.exe) tidak menjawab."));
                Pasang(pipe);
            }
            JabatTangan();
        }

        void Pasang(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            var utf8 = new UTF8Encoding(false);
            _penulis = new StreamWriter(pipe, utf8, 4096) { AutoFlush = true, NewLine = "\n" };
            var pembaca = new StreamReader(pipe, utf8, false, 4096);
            var t = new Thread(() => BacaTerus(pipe, pembaca))
            {
                IsBackground = true,
                Name = "Phoron.KlienDaemon",
            };
            t.Start();
        }

        void JabatTangan()
        {
            Jawaban j;
            try
            {
                j = KirimMentah("halo", new Dictionary<string, object>
                {
                    { "versi_ui", VersiUi ?? "" },
                    { "protokol", Protokol.Versi },
                }, TimeSpan.FromSeconds(10), null).GetAwaiter().GetResult();
            }
            catch (GalatDaemon) { Putuskan(); throw; }
            if (!j.Ok)
            {
                _ditolak = true;
                Putuskan();
                // Pesan yang jelas, bukan galat mentah: satu-satunya jalan keluar
                // dari keadaan ini adalah memasang ulang Phoron, dan orangnya harus
                // tahu itu tanpa membaca log.
                if (j.JenisGalat == "protokol_beda")
                    throw new GalatDaemon("protokol_beda", Lang.T(
                        "Versi Phoron ({0}) dan daemonnya tidak sepadan (protokol {1} dan {2}). Phoron perlu dipasang ulang.",
                        VersiUi, Protokol.Versi, Protokol.Angka(j.Galat, "daemon")));
                throw new GalatDaemon(j.JenisGalat, j.PesanGalat());
            }
            var h = Protokol.Objek(j.Hasil);
            VersiDaemon = Protokol.Teks(h, "versi_daemon") ?? "";
            long p = Protokol.Angka(h, "protokol", Protokol.Versi);
            if (p != Protokol.Versi)
            {
                _ditolak = true;
                Putuskan();
                throw new GalatDaemon("protokol_beda", Lang.T(
                    "Versi Phoron ({0}) dan daemonnya tidak sepadan (protokol {1} dan {2}). Phoron perlu dipasang ulang.",
                    VersiUi, Protokol.Versi, p));
            }
            // Nama pipe sama untuk semua salinan Phoron. Tanpa pemeriksaan ini,
            // Phoron.exe dari folder lain (mis. build di repo) tersambung ke
            // daemon C:\Phoron: layarnya membaca profil dan sertifikat foldernya
            // sendiri, sedangkan tombolnya menyalakan layanan milik folder lain.
            // Akar kosong = daemon 1.42.0 yang belum menyebutnya; diterima.
            AkarDaemon = Protokol.Teks(h, "akar") ?? "";
            if (AkarHarapan != null && AkarDaemon.Length > 0 && !SamaFolder(AkarDaemon, AkarHarapan))
            {
                _ditolak = true;
                Putuskan();
                throw new GalatDaemon("akar_beda", Lang.T(
                    "Phoron lain sedang berjalan dari {0}, sedangkan jendela ini milik {1}. Keduanya memakai daemon yang sama, jadi jendela ini tidak dibuka supaya tidak mengendalikan layanan milik folder lain. Keluarkan Phoron yang sedang berjalan (klik kanan ikon baki > Keluar), lalu buka jendela ini lagi.",
                    AkarDaemon, AkarHarapan));
            }
            _siap = true;
            var ev = Tersambung;
            if (ev != null) ev();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WaitNamedPipe(string nama, int batasMs);

        /// <summary>
        /// Buka pipe, menunggu sampai <paramref name="batas"/> bila belum ada.
        ///
        /// Tidak memakai NamedPipeClientStream.Connect(batas) begitu saja: di
        /// .NET Framework, pipe yang BELUM ADA membuatnya berputar tanpa jeda
        /// sampai batasnya habis - satu inti CPU penuh selama sepuluh detik.
        /// </summary>
        NamedPipeClientStream BukaPipe(TimeSpan batas)
        {
            var sampai = DateTime.UtcNow + batas;
            do
            {
                // ERROR_FILE_NOT_FOUND (2): pipe belum ada sama sekali - tunggu.
                // Selain itu (ada, atau semua instansnya sibuk) coba buka.
                bool ada = WaitNamedPipe(JalurPipe, 0) || Marshal.GetLastWin32Error() != 2;
                if (ada)
                {
                    var p = new NamedPipeClientStream(".", NamaPipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try
                    {
                        p.Connect(1000);
                        return p;
                    }
                    catch (TimeoutException) { p.Dispose(); }
                    catch (IOException) { p.Dispose(); }
                }
                if (_dibuang) return null;
                Thread.Sleep(100);
            } while (DateTime.UtcNow < sampai);
            return null;
        }

        /// <summary>Jalankan phorond.exe --baki. Paling sering sekali per 20 detik.</summary>
        bool JalankanDaemon()
        {
            if (string.IsNullOrEmpty(JalurDaemon) || !File.Exists(JalurDaemon)) return false;
            if ((DateTime.UtcNow - _terakhirMenjalankan).TotalSeconds < 20) return true;
            _terakhirMenjalankan = DateTime.UtcNow;
            try
            {
                var psi = new ProcessStartInfo(JalurDaemon, "--baki")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(JalurDaemon),
                };
                // Nama pipe uji ikut diturunkan, supaya daemon yang dijalankan
                // klien uji tidak membuka pipe milik Phoron yang sedang dipakai.
                if (NamaPipe != Protokol.NamaPipeBawaan) psi.EnvironmentVariables["PHORON_PIPE"] = NamaPipe;
                using (Process.Start(psi)) { }
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------- Kirim

        /// <summary>Kirim perintah dan tunggu jawabannya (Task selesai di utas latar).</summary>
        public Task<Jawaban> KirimAsync(string perintah, object data = null, TimeSpan? batas = null)
        {
            return KirimAsync(perintah, data, batas, null);
        }

        /// <summary>
        /// Seperti <see cref="KirimAsync(string, object, TimeSpan?)"/>, dengan
        /// <paramref name="segera"/> yang dijalankan DI UTAS PEMBACA begitu
        /// jawabannya tiba - sebelum kabar berikutnya diproses. Dipakai untuk
        /// jawaban yang berisi keadaan: menerapkannya belakangan bisa menimpa
        /// kabar yang lebih baru.
        /// </summary>
        public Task<Jawaban> KirimAsync(string perintah, object data, TimeSpan? batas, Action<Jawaban> segera)
        {
            if (!Aktif)
            {
                // Pipe yang baru putus sedang disambung ulang; beri ia waktu
                // sebentar alih-alih langsung gagal.
                if (!_dibuang && !_pamit && !_ditolak) MulaiSambungUlang();
                return Task.Run(async () =>
                {
                    for (int i = 0; i < 50 && !Aktif && !_dibuang && !_pamit; i++) await Task.Delay(100);
                    if (!Aktif) throw new GalatDaemon("terputus", Lang.T("Tidak tersambung ke daemon Phoron."));
                    return await KirimMentah(perintah, data, batas, segera);
                });
            }
            return KirimMentah(perintah, data, batas, segera);
        }

        /// <summary>Versi menunggu dari <see cref="KirimAsync"/>. Melempar GalatDaemon.</summary>
        public Jawaban Kirim(string perintah, object data = null, TimeSpan? batas = null)
        {
            // Pendengar kabar yang menunggu jawaban akan menahan satu-satunya
            // utas yang bisa membacanya - gagal sekarang, bukan sesudah batas waktu.
            if (_diUtasPembaca)
                throw new GalatDaemon("terputus", Lang.T("Perintah {0} tidak boleh ditunggu dari utas pembaca pipe.", perintah));
            try { return KirimAsync(perintah, data, batas).GetAwaiter().GetResult(); }
            catch (AggregateException ae) { throw ae.InnerException; }
        }

        Task<Jawaban> KirimMentah(string perintah, object data, TimeSpan? batas, Action<Jawaban> segera)
        {
            long id = Interlocked.Increment(ref _id);
            // RunContinuationsAsynchronously: kelanjutan pemanggil TIDAK boleh
            // jalan di utas pembaca, sebab pemanggil yang lalu mengirim dan
            // menunggu lagi akan menahan satu-satunya utas yang bisa menjawabnya.
            var tcs = new TaskCompletionSource<Jawaban>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_menunggu)
            {
                _menunggu[id] = tcs;
                if (segera != null) _segera[id] = segera;
            }

            var pesan = new Dictionary<string, object> { { "id", id }, { "perintah", perintah } };
            if (data != null) pesan["data"] = data;
            string baris = Protokol.Tulis(pesan);
            try
            {
                lock (_kunciTulis)
                {
                    var w = _penulis;
                    if (w == null) throw new IOException("pipe tertutup");
                    w.WriteLine(baris);
                }
            }
            catch (Exception ex)
            {
                Lepas(id);
                tcs.TrySetException(new GalatDaemon("terputus", Lang.T("Tidak tersambung ke daemon Phoron."), ex));
                return tcs.Task;
            }

            var waktu = batas ?? BatasBawaan;
            var cts = new CancellationTokenSource();
            Task.Delay(waktu, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                if (Lepas(id))
                    tcs.TrySetException(new GalatDaemon("batas_waktu",
                        Lang.T("Daemon Phoron tidak menjawab perintah {0} dalam {1} detik.",
                               perintah, (int)waktu.TotalSeconds)));
            }, TaskScheduler.Default);
            tcs.Task.ContinueWith(_ => { cts.Cancel(); cts.Dispose(); }, TaskScheduler.Default);
            return tcs.Task;
        }

        bool Lepas(long id)
        {
            lock (_menunggu)
            {
                _segera.Remove(id);
                return _menunggu.Remove(id);
            }
        }

        // ------------------------------------------------------------- Baca

        void BacaTerus(NamedPipeClientStream pipe, StreamReader pembaca)
        {
            _diUtasPembaca = true;
            try
            {
                string baris;
                while ((baris = pembaca.ReadLine()) != null)
                {
                    if (baris.Length == 0) continue;
                    if (baris.Length > Protokol.BatasBaris) continue;
                    IDictionary<string, object> d;
                    try { d = Protokol.Objek(Protokol.Urai(baris)); }
                    catch { continue; }   // baris rusak dari daemon: lewati, jangan putus
                    if (d == null) continue;

                    if (d.ContainsKey("id")) Selesaikan(Jawaban.Dari(d));
                    else
                    {
                        var jenis = Protokol.Teks(d, "kabar");
                        if (jenis == null) continue;
                        if (jenis == "pamit")
                        {
                            // Daemon lama yang keluar untuk dijalankan ulang
                            // ber-Administrator: sambungan ke penggantinya
                            // menyusul, UI tidak ikut keluar.
                            if (MenungguMulaiUlang) continue;
                            _pamit = true;
                        }
                        var h = Kabar;
                        if (h != null)
                        {
                            try { h(jenis, Protokol.Ambil(d, "data")); }
                            catch { /* pendengar yang meledak tidak boleh memutus pipe */ }
                        }
                    }
                }
            }
            catch { /* pipe putus */ }
            finally
            {
                bool milikIni = false;
                lock (_kunciSambung)
                {
                    if (_pipe == pipe)
                    {
                        milikIni = true;
                        _siap = false;
                        lock (_kunciTulis) _penulis = null;
                        _pipe = null;
                    }
                }
                try { pipe.Dispose(); } catch { }
                if (milikIni)
                {
                    GagalkanSemua();
                    var h = Terputus;
                    if (h != null) { try { h(); } catch { } }
                    if (!_dibuang && !_pamit && !_ditolak) MulaiSambungUlang();
                }
            }
        }

        void Selesaikan(Jawaban j)
        {
            TaskCompletionSource<Jawaban> tcs;
            Action<Jawaban> segera;
            lock (_menunggu)
            {
                if (!_menunggu.TryGetValue(j.Id, out tcs)) return;   // sudah lewat batas waktu
                _menunggu.Remove(j.Id);
                if (_segera.TryGetValue(j.Id, out segera)) _segera.Remove(j.Id);
            }
            if (segera != null) { try { segera(j); } catch { } }
            tcs.TrySetResult(j);
        }

        void GagalkanSemua()
        {
            List<TaskCompletionSource<Jawaban>> semua;
            lock (_menunggu)
            {
                semua = new List<TaskCompletionSource<Jawaban>>(_menunggu.Values);
                _menunggu.Clear();
                _segera.Clear();
            }
            foreach (var t in semua)
                t.TrySetException(new GalatDaemon("terputus", Lang.T("Sambungan ke daemon Phoron terputus.")));
        }

        /// <summary>Sambung ulang di latar sampai berhasil, klien dibuang, atau daemon berpamitan.</summary>
        void MulaiSambungUlang()
        {
            if (Interlocked.CompareExchange(ref _menyambungUlang, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int coba = 0;
                    while (!_dibuang && !_pamit && !_ditolak && !Aktif)
                    {
                        bool sibuk = false;
                        try
                        {
                            NamedPipeClientStream pipe = null;
                            lock (_kunciSambung)
                            {
                                if (Aktif) break;
                                // Pipe terpasang tapi belum siap: Sambung() sedang
                                // berjabat tangan, atau pembacanya belum selesai
                                // membereskan pipe yang putus. Pipe kedua akan
                                // menimpa _pipe dan meninggalkan pembaca yatim.
                                if (_pipe != null) sibuk = true;
                                else
                                {
                                    pipe = BukaPipe(TimeSpan.FromMilliseconds(500));
                                    // Daemon yang mati sendiri (crash) dihidupkan
                                    // lagi - itulah pemilik Apache dan MySQL.
                                    // Kecuali daemon sedang menjalankan ulang
                                    // dirinya: penggantinya sedang naik.
                                    if (pipe == null && coba >= 2 && !MenungguMulaiUlang && JalankanDaemon())
                                        pipe = BukaPipe(BatasTungguDaemon);
                                    if (pipe != null) Pasang(pipe);
                                }
                            }
                            if (pipe != null) { JabatTangan(); break; }
                        }
                        catch { }
                        if (sibuk) { Thread.Sleep(100); continue; }
                        coba++;
                        Thread.Sleep(Math.Min(2000, 200 * coba));
                    }
                }
                finally { Interlocked.Exchange(ref _menyambungUlang, 0); }
            });
        }

        void Putuskan()
        {
            NamedPipeClientStream p;
            lock (_kunciSambung)
            {
                p = _pipe;
                _siap = false;
            }
            if (p != null) { try { p.Dispose(); } catch { } }
        }

        public void Dispose()
        {
            _dibuang = true;
            Putuskan();
            GagalkanSemua();
        }
    }
}
