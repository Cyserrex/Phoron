using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoron.Core.Jarak
{
    /// <summary>
    /// Keadaan Apache/MySQL yang dipegang phorond, sebagaimana dikabarkan
    /// lewat pipe. Tidak menjalankan apa pun sendiri.
    /// </summary>
    public sealed class LayananJarak : ILayanan
    {
        readonly KlienDaemon _klien;
        ServiceState _web = ServiceState.Berhenti, _db = ServiceState.Berhenti;

        public LayananJarak(KlienDaemon klien) { _klien = klien; }

        public ServiceState WebState { get { return _web; } }
        public ServiceState DbState { get { return _db; } }

        /// <summary>Pesan galat terakhir per layanan dari daemon, sudah diterjemahkan; null bila tidak ada.</summary>
        public string GalatWeb { get; private set; }
        public string GalatDb { get; private set; }

        public event Action<ServiceKind, ServiceState> StateChanged;

        /// <summary>Terapkan StatusLayanan ({web, db, galat_web?, galat_db?}) dan kabarkan yang berubah.</summary>
        public void Terapkan(object status)
        {
            var d = Protokol.Objek(status);
            if (d == null) return;
            var web = Protokol.Keadaan(Protokol.Teks(d, "web"));
            var db = Protokol.Keadaan(Protokol.Teks(d, "db"));
            var gw = Pesan.Dari(Protokol.Ambil(d, "galat_web"));
            var gd = Pesan.Dari(Protokol.Ambil(d, "galat_db"));
            GalatWeb = gw != null ? gw.Teks() : null;
            GalatDb = gd != null ? gd.Teks() : null;

            bool ubahWeb = web != _web, ubahDb = db != _db;
            _web = web;
            _db = db;
            var h = StateChanged;
            if (h == null) return;
            if (ubahWeb) h(ServiceKind.Web, web);
            if (ubahDb) h(ServiceKind.Db, db);
        }

        /// <summary>
        /// Matikan semua layanan di daemon dan tunggu sebentar. Dipakai jalur
        /// keluar lama (pembaruan); jendela yang sekadar ditutup TIDAK memanggil
        /// ini - layanan milik daemon tetap hidup.
        /// </summary>
        public void StopAll()
        {
            try { _klien.Kirim("matikan_semua", null, TimeSpan.FromSeconds(120)); }
            catch (GalatDaemon) { }
        }
    }

    /// <summary>
    /// Proyek Node yang dijalankan phorond. Keluarannya ditampung di sini dari
    /// kabar node_keluaran, persis seperti NodeRunner menampung keluaran
    /// prosesnya sendiri, supaya halaman yang dibuka belakangan tetap melihat
    /// ekornya.
    /// </summary>
    public sealed class NodeJarak : IProsesNode
    {
        readonly KlienDaemon _klien;
        readonly object _kunci = new object();

        sealed class Status { public bool Jalan; public string Url; public int Bentrok; }

        readonly Dictionary<string, Status> _status =
            new Dictionary<string, Status>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Queue<string>> _riwayat =
            new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Folder yang riwayatnya sudah diambil dari daemon.</summary>
        readonly HashSet<string> _riwayatDiambil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public NodeJarak(KlienDaemon klien) { _klien = klien; }

        public event Action<string, string> Output;
        public event Action<string, bool> StateChanged;
        public event Action<string, string> UrlFound;

        static string Kunci(string folder) { return (folder ?? "").TrimEnd('\\'); }

        public bool IsRunning(string folder)
        {
            Status s;
            lock (_kunci) return folder != null && _status.TryGetValue(Kunci(folder), out s) && s.Jalan;
        }

        public string UrlOf(string folder)
        {
            Status s;
            lock (_kunci)
                return folder != null && _status.TryGetValue(Kunci(folder), out s) && s.Jalan ? s.Url : null;
        }

        public IEnumerable<string> RunningFolders
        {
            get { lock (_kunci) return _status.Where(kv => kv.Value.Jalan).Select(kv => kv.Key).ToList(); }
        }

        public int PortBentrok(string folder)
        {
            Status s;
            lock (_kunci) return folder != null && _status.TryGetValue(Kunci(folder), out s) && !s.Jalan ? s.Bentrok : 0;
        }

        public void Stop(string folder)
        {
            if (folder == null) return;
            KirimLatar("hentikan_node", new Dictionary<string, object> { { "path", folder } });
        }

        public void StopAll() { KirimLatar("hentikan_semua_node", null); }

        void KirimLatar(string perintah, object data)
        {
            // Hasilnya datang sebagai kabar node; galatnya tidak punya tempat
            // tampil yang lebih baik daripada log daemon.
            _klien.KirimAsync(perintah, data).ContinueWith(t => { var _ = t.Exception; },
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        public List<string> Riwayat(string folder)
        {
            if (folder == null) return new List<string>();
            var k = Kunci(folder);
            bool ambil;
            lock (_kunci) ambil = _riwayatDiambil.Add(k);
            if (ambil)
            {
                // Sekali per proyek: keluaran yang tercetak sebelum UI ini dibuka
                // hanya ada di daemon. Sesudahnya kabar node_keluaran yang mengisi.
                try
                {
                    var j = _klien.Kirim("riwayat_node", new Dictionary<string, object> { { "path", folder } },
                                         TimeSpan.FromSeconds(5));
                    if (j.Ok)
                    {
                        var dulu = Protokol.Larik(j.Hasil).Select(x => Convert.ToString(x)).ToList();
                        lock (_kunci)
                        {
                            Queue<string> q;
                            _riwayat.TryGetValue(k, out q);
                            var baru = new Queue<string>(dulu);
                            if (q != null) foreach (var b in q) baru.Enqueue(b);
                            while (baru.Count > NodeRunner.BatasRiwayat) baru.Dequeue();
                            _riwayat[k] = baru;
                        }
                    }
                }
                catch (GalatDaemon) { lock (_kunci) _riwayatDiambil.Remove(k); }
            }
            lock (_kunci)
            {
                Queue<string> q;
                return _riwayat.TryGetValue(k, out q) ? new List<string>(q) : new List<string>();
            }
        }

        public void Sisipkan(string folder, string baris) { Tambah(folder, baris); }

        void Tambah(string folder, string baris)
        {
            if (folder != null)
            {
                lock (_kunci)
                {
                    var k = Kunci(folder);
                    Queue<string> q;
                    if (!_riwayat.TryGetValue(k, out q)) _riwayat[k] = q = new Queue<string>();
                    q.Enqueue(baris ?? "");
                    while (q.Count > NodeRunner.BatasRiwayat) q.Dequeue();
                }
            }
            var h = Output;
            if (h != null) h(folder, baris);
        }

        /// <summary>Kabar node_keluaran: {path, baris}.</summary>
        public void TerapkanKeluaran(object data)
        {
            var d = Protokol.Objek(data);
            if (d == null) return;
            Tambah(Protokol.Teks(d, "path"), Protokol.Teks(d, "baris") ?? "");
        }

        /// <summary>Kabar node atau satu StatusNode dari keadaan: {path, berjalan, url?, port_bentrok?}.</summary>
        public void TerapkanStatus(object data)
        {
            var d = Protokol.Objek(data);
            if (d == null) return;
            var folder = Protokol.Teks(d, "path");
            if (string.IsNullOrEmpty(folder)) return;
            bool jalan = Protokol.Benar(d, "berjalan");
            var url = Protokol.Teks(d, "url");
            int bentrok = (int)Protokol.Angka(d, "port_bentrok");

            bool ubahJalan, urlBaru;
            lock (_kunci)
            {
                Status s;
                if (!_status.TryGetValue(Kunci(folder), out s)) _status[Kunci(folder)] = s = new Status();
                ubahJalan = s.Jalan != jalan;
                urlBaru = jalan && !string.IsNullOrEmpty(url) && !string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase);
                s.Jalan = jalan;
                s.Url = jalan ? url : null;
                s.Bentrok = bentrok;
            }
            if (ubahJalan) { var h = StateChanged; if (h != null) h(folder, jalan); }
            if (urlBaru) { var h = UrlFound; if (h != null) h(folder, url); }
        }

        /// <summary>Ganti seluruh daftar dari Keadaan.node; proyek yang tidak disebut dianggap berhenti.</summary>
        public void TerapkanDaftar(object node)
        {
            var disebut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Protokol.Larik(node))
            {
                var path = Protokol.Teks(Protokol.Objek(o), "path");
                if (!string.IsNullOrEmpty(path)) disebut.Add(Kunci(path));
                TerapkanStatus(o);
            }
            List<string> hilang;
            lock (_kunci) hilang = _status.Where(kv => kv.Value.Jalan && !disebut.Contains(kv.Key)).Select(kv => kv.Key).ToList();
            foreach (var f in hilang)
                TerapkanStatus(new Dictionary<string, object> { { "path", f }, { "berjalan", false } });
        }
    }
}
