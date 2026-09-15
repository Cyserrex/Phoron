using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class ExtensionsPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        readonly List<CheckBox> _kotak = new List<CheckBox>();

        /// <summary>Sedang mengisi layar - perubahan kendali bukan dari pengguna, jangan disimpan.</summary>
        bool _muat;

        /// <summary>php.ini sudah ditulis ulang tapi web server masih memakai yang lama.</summary>
        bool _perluRestart;

        /// <summary>
        /// Mencentang belasan ekstensi berturut-turut tidak boleh memicu belasan
        /// penulisan konfigurasi penuh. Centangan dikumpulkan dulu sebentar, baru
        /// ditulis sekali.
        /// </summary>
        readonly System.Windows.Threading.DispatcherTimer _tunda =
            new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(700) };

        public ExtensionsPage()
        {
            InitializeComponent();
            _tunda.Tick += (s, e) => Simpan();
            Isi();
            SegarkanBilah();
        }

        void Isi()
        {
            _muat = true;
            try { IsiDalam(); }
            finally { _muat = false; }
        }

        void IsiDalam()
        {
            var php = _e.Php;
            var p = _e.Active;
            if (php == null || p == null)
            {
                TxtInfo.Text = "Profil aktif belum menunjuk versi PHP.";
                return;
            }
            TxtInfo.Text = "Ekstensi yang tersedia di " + php.Id + ". Centangan disimpan di profil \""
                         + p.Name + "\", jadi tiap profil bisa punya daftar sendiri.";

            var aktif = new HashSet<string>(p.PhpExtensions, StringComparer.OrdinalIgnoreCase);
            _kotak.Clear();
            foreach (var ext in ConfigWriter.AvailableExtensions(php))
            {
                var cb = new CheckBox
                {
                    // Teks dibungkus TextBlock, bukan diberikan sebagai string:
                    // ContentPresenter memperlakukan garis bawah sebagai penanda
                    // tombol akses, jadi "pdo_mysql" akan tampil "pdomysql".
                    Content = new TextBlock { Text = ext },
                    Tag = ext,
                    IsChecked = aktif.Contains(ext),
                    Width = 165,
                    Margin = new Thickness(0, 2, 8, 2),
                };
                cb.Checked += Kotak_Ubah;
                cb.Unchecked += Kotak_Ubah;
                _kotak.Add(cb);
            }
            DaftarExt.ItemsSource = _kotak;

            TxtMemory.Text = Nilai(p, "memory_limit");
            TxtUpload.Text = Nilai(p, "upload_max_filesize");
            TxtPost.Text = Nilai(p, "post_max_size");
            TxtExec.Text = Nilai(p, "max_execution_time");
            TxtTz.Text = Nilai(p, "date.timezone");
            var de = Nilai(p, "display_errors");
            ChkErrors.IsChecked = de.Length == 0 || de.Equals("On", StringComparison.OrdinalIgnoreCase);
            // Tanpa penimpaan di profil, yang berlaku adalah nilai dari php.ini
            // dasar - jadi kotaknya mencerminkan berkas itu, bukan menebak Off.
            var sot = Nilai(p, "short_open_tag");
            ChkShortTag.IsChecked = sot.Length > 0
                ? sot.Equals("On", StringComparison.OrdinalIgnoreCase)
                : PhpIniAktif("short_open_tag");
        }

        static string Nilai(Profile p, string key)
        {
            string v;
            return p.PhpIniOverrides.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>Nilai direktif menurut php.ini yang sedang berlaku, ditanyakan ke php.exe sendiri.</summary>
        bool PhpIniAktif(string key)
        {
            var php = _e.Php;
            if (php == null) return false;
            try
            {
                var res = Shell.Run(Path.Combine(php.Path, "php.exe"),
                    "-r \"echo ini_get('" + key + "') ? 1 : 0;\"", php.Path, 15000,
                    ServiceManager.EnvFor(php));
                return res.StdOut.Trim() == "1";
            }
            catch { return false; }
        }

        void TxtCari_Changed(object sender, TextChangedEventArgs e)
        {
            var q = (TxtCari.Text ?? "").Trim();
            foreach (var cb in _kotak)
                cb.Visibility = q.Length == 0
                    || (cb.Tag ?? "").ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Centang ulang mengikuti php.ini yang berlaku sebelum Phoron ikut
        /// campur. Pengambilalihan otomatis hanya terjadi pada profil yang belum
        /// punya daftar; profil yang sudah terlanjur berisi sebagian ekstensi
        /// butuh jalan sadar seperti ini.
        /// </summary>
        void BtnAmbil_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Php;
            if (php == null) { AppState.Warn("Profil belum menunjuk PHP."); return; }
            var dasar = ConfigWriter.EkstensiDariPhpIniDasar(php);
            if (dasar.Count == 0)
            {
                AppState.Info("php.ini asli di " + php.Id + " tidak mengaktifkan ekstensi apa pun.");
                return;
            }
            if (!AppState.Ask("php.ini asli mengaktifkan " + dasar.Count + " ekstensi:\n\n"
                              + string.Join(", ", dasar)
                              + "\n\nGanti centangan sekarang dengan daftar itu?")) return;

            var set = new HashSet<string>(dasar, StringComparer.OrdinalIgnoreCase);
            foreach (var cb in _kotak) cb.IsChecked = set.Contains((cb.Tag ?? "").ToString());

            var hilang = dasar.Where(d => !_kotak.Any(c =>
                string.Equals((c.Tag ?? "").ToString(), d, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hilang.Count > 0)
                AppState.Warn("Tidak ada DLL-nya di " + php.Id + ", jadi dilewati: "
                              + string.Join(", ", hilang));
            Simpan();
            AppState.Info("Centangan disesuaikan dan langsung disimpan.");
        }

        void Kotak_Ubah(object sender, RoutedEventArgs e) { Jadwalkan(); }

        void Chk_Ubah(object sender, RoutedEventArgs e) { Jadwalkan(); }

        // Kotak teks tidak ditunda: fokus sudah lepas, artinya pengguna selesai
        // mengetik dan menunggu hasilnya.
        void Teks_Lepas(object sender, RoutedEventArgs e) { Simpan(); }

        void Jadwalkan()
        {
            if (_muat) return;
            _tunda.Stop();
            _tunda.Start();
        }

        /// <summary>
        /// Menulis centangan ke profil lalu menulis ulang php.ini. Sengaja TIDAK
        /// memunculkan kotak dialog apa pun: fungsi ini berjalan sendiri setiap
        /// kali ada perubahan, dan dialog yang muncul tiap centangan lebih buruk
        /// daripada tombol simpan yang tadinya ada.
        /// </summary>
        void Simpan()
        {
            _tunda.Stop();
            if (_muat) return;
            var p = _e.Active;
            if (p == null) return;

            p.PhpExtensions = _kotak.Where(c => c.IsChecked == true)
                                    .Select(c => (c.Tag ?? "").ToString()).ToList();

            Set(p, "memory_limit", TxtMemory.Text);
            Set(p, "upload_max_filesize", TxtUpload.Text);
            Set(p, "post_max_size", TxtPost.Text);
            Set(p, "max_execution_time", TxtExec.Text);
            Set(p, "date.timezone", TxtTz.Text);
            p.PhpIniOverrides["display_errors"] = ChkErrors.IsChecked == true ? "On" : "Off";
            p.PhpIniOverrides["short_open_tag"] = ChkShortTag.IsChecked == true ? "On" : "Off";

            ProfileStore.Save(p);
            // Peringatan Apply masuk ke log, bukan ke kotak dialog - lihat alasan
            // di ringkasan fungsi ini.
            foreach (var w in _e.Apply()) _e.Say(w);

            if (_e.Services.WebState == ServiceState.Jalan) _perluRestart = true;
            TxtStatusSimpan.Text = "Tersimpan " + DateTime.Now.ToString("HH:mm:ss")
                + " - " + p.PhpExtensions.Count + " ekstensi dicentang, php.ini ditulis ulang.";
            SegarkanBilah();
        }

        void SegarkanBilah()
        {
            bool jalan = _e.Services.WebState == ServiceState.Jalan;
            PanelRestart.Visibility = _perluRestart && jalan ? Visibility.Visible : Visibility.Collapsed;
            if (PanelRestart.Visibility == Visibility.Visible)
                TxtRestart.Text = "php.ini sudah ditulis ulang, tapi web server membacanya HANYA saat start - "
                    + "jadi perubahan ini belum berlaku di browser sampai dinyalakan ulang.";
        }

        async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            Simpan();   // rapikan perubahan yang masih tertunda sebelum restart
            var main = Window.GetWindow(this) as MainWindow;
            await _e.StopWebAsync();
            await _e.StartWebAsync();
            if (main != null) main.RefreshStatus();
            _perluRestart = false;
            SegarkanBilah();
        }

        static void Set(Profile p, string key, string value)
        {
            value = (value ?? "").Trim();
            // Kolom kosong berarti "pakai bawaan php.ini", bukan "setel ke kosong" -
            // menulis nilai kosong ke php.ini justru mematikan direktifnya.
            if (value.Length == 0) p.PhpIniOverrides.Remove(key);
            else p.PhpIniOverrides[key] = value;
        }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            if (_e.Php == null) return;
            // Tombol ini bernama "hasil", jadi ia WAJIB menulis dulu. Dulu ia
            // hanya menulis kalau belum pernah menulis sama sekali, sehingga
            // centangan yang baru diubah tidak kelihatan di berkas yang dibuka -
            // dan itu terbaca sebagai "centangannya tidak berpengaruh".
            Simpan();
            var dir = _e.LastBuild != null ? _e.LastBuild.PhpIniDir : null;
            if (dir == null) { AppState.Warn("php.ini belum pernah ditulis."); return; }
            Shell.Open(Path.Combine(dir, "php.ini"));
        }


        /// <summary>
        /// Menerjemahkan dua galat pemuatan DLL yang paling membingungkan, dan
        /// menyertakan hasil pemeriksaan Oracle Instant Client di komputer ini.
        ///
        /// Kedua kalimat itu murni kalimat Windows, dan tak satu pun menyebut
        /// apa yang sebenarnya kurang - orang lalu menyangka ekstensinya rusak
        /// atau Phoron gagal menulis php.ini, padahal berkasnya sudah benar.
        /// </summary>
        string Petunjuk(string keluaran)
        {
            var t = keluaran ?? "";
            bool arsitektur = t.IndexOf("not a valid Win32 application", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hilang = t.IndexOf("specified module could not be found", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!arsitektur && !hilang) return "";

            var sb = new System.Text.StringBuilder();
            sb.Append(Environment.NewLine + Environment.NewLine + "--- Penjelasan ---" + Environment.NewLine);
            if (arsitektur)
                sb.Append("\"is not a valid Win32 application\" berarti BEDA ARSITEKTUR, dan yang salah "
                    + "arsitektur biasanya bukan DLL ekstensinya, melainkan pustaka yang dipanggilnya."
                    + Environment.NewLine);
            if (hilang)
                sb.Append("\"The specified module could not be found\" berarti DLL ekstensinya ADA, tapi "
                    + "pustaka yang dibutuhkannya tidak ketemu sama sekali." + Environment.NewLine);

            // Kasus tersering sejauh ini: oci8. Karena itu keadaan NYATA komputer
            // ini ikut dilaporkan, bukan cuma teori umum - dugaan yang terdengar
            // masuk akal tapi tidak diperiksa justru mengirim orang ke arah salah.
            if (t.IndexOf("oci", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var o = Oracle.Periksa(_e.Php);
                sb.Append(Environment.NewLine + "Oracle Instant Client di komputer ini: ");
                sb.Append(o.Ada
                    ? o.JalurDll + " (" + (o.Arsitektur.Length > 0 ? o.Arsitektur : "arsitektur tak terbaca") + ")"
                    : "TIDAK DITEMUKAN di PATH");
                sb.Append(o.Pesan.Length > 0
                    ? Environment.NewLine + Environment.NewLine + o.Pesan
                    : Environment.NewLine + "Arsitekturnya sepadan dengan PHP, jadi sebab galatnya ada di tempat lain.");
            }

            sb.Append(Environment.NewLine + Environment.NewLine
                + "Phoron tidak bisa memperbaiki ini dari sini: Instant Client dan PATH itu "
                + "milik Windows, bukan milik profil.");
            return sb.ToString();
        }

        void BtnUji_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Php;
            if (php == null) { AppState.Warn("Profil belum menunjuk PHP."); return; }
            Simpan();
            var res = Shell.Run(Path.Combine(php.Path, "php.exe"), "-m", php.Path, 30000,
                                ServiceManager.EnvFor(php));
            AppState.Info(res.All + Petunjuk(res.All), "Modul yang benar-benar dimuat");
        }
    }
}
