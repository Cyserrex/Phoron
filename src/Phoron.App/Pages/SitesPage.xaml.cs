using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class SitesPage : UserControl
    {
        readonly Engine _e = AppState.Engine;

        public class Baris
        {
            public string Alamat { get; set; }
            public string Folder { get; set; }
            public string Root { get; set; }
            public string DocRoot { get; set; }
            public string Hosts { get; set; }
            public string Vhost { get; set; }
            public List<PilihanPhp> PilihanPhp { get; set; }
            public PilihanPhp PhpTerpilih { get; set; }
            /// <summary>php.ini yang dipakai situs ini - tooltip kotak PHP.</summary>
            public string KeteranganPhp { get; set; }
            public Site Situs;
        }

        /// <summary>Satu pilihan di kotak PHP. Id kosong = ikut PHP profil.</summary>
        public class PilihanPhp
        {
            public string Id { get; set; }
            public string Teks { get; set; }
            public override string ToString() { return Teks; }
        }

        public SitesPage()
        {
            InitializeComponent();
            Isi();
        }

        void Isi()
        {
            _e.RefreshSites();
            var roots = SiteScanner.Roots(_e.Active);
            TxtInfo.Text = roots.Count == 1
                ? "Tiap subfolder di " + roots[0] + " otomatis dapat alamat sendiri. "
                  + "Klik ganda untuk membuka di browser."
                : "Memindai " + roots.Count + " folder proyek: " + string.Join(", ", roots)
                  + ". Klik ganda untuk membuka di browser.";

            LblFolderProyek.Text = _e.Active != null
                ? "Folder proyek profil \"" + _e.Active.Name + "\""
                : "Folder proyek";
            _mengisi = true;
            TxtDocRoot.Text = _e.Active != null
                ? string.Join(Environment.NewLine, _e.Active.ProjectRoots)
                : "";
            _mengisi = false;

            CmbRootBaru.ItemsSource = roots;
            CmbRootBaru.SelectedIndex = 0;
            CmbRootBaru.Visibility = roots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            Daftar.ItemsSource = _e.Sites.Select(s => new Baris
            {
                // Kosong berarti situs ini tidak terjangkau tanpa Virtual Host -
                // ia berada di folder proyek kedua, yang tidak dilayani akar utama.
                Alamat = Kosong(_e.SiteUrl(s)),
                Folder = s.Folder,
                Root = s.Root,
                // Ditampilkan relatif terhadap folder situsnya sendiri; yang perlu
                // terlihat di kolom ini cuma apakah Phoron memilih subfolder public/.
                DocRoot = s.DocRoot != null && s.Path != null
                          && s.DocRoot.Length > s.Path.Length
                          && s.DocRoot.StartsWith(s.Path, StringComparison.OrdinalIgnoreCase)
                    ? s.DocRoot.Substring(s.Path.Length).TrimStart('\\')
                    : "(akar folder)",
                Hosts = s.InHosts ? "ada" : "-",
                Vhost = s.HasVhost ? "ada" : "-",
                Situs = s,
            }).ToList();
            foreach (Baris b in (List<Baris>)Daftar.ItemsSource) IsiPilihanPhp(b);

            TampilkanInfo();
        }

        /// <summary>
        /// Keterangan tentang daftar situs, ditempel di halaman - bukan
        /// dimunculkan sebagai kotak dialog yang menghalangi.
        /// </summary>
        /// <summary>
        /// Pilihan PHP untuk satu situs: "Ikut profil", lalu setiap versi yang
        /// terpasang. Versi yang dicatat profil tapi tidak ada di komputer ini
        /// tetap muncul - dan terpilih - dengan ID aslinya, supaya pilihannya
        /// tidak hilang diam-diam; pola yang sama dengan halaman Profil.
        /// </summary>
        void IsiPilihanPhp(Baris b)
        {
            var profil = _e.Php;
            var daftar = new List<PilihanPhp>
            {
                new PilihanPhp
                {
                    Id = "",
                    Teks = Lang.T("Ikut profil") + (profil != null ? " (" + profil.Version + ")" : ""),
                },
            };
            var semua = _e.Of(BinKind.Php).OrderByDescending(x => x.Parsed).ToList();
            foreach (var p in semua)
                daftar.Add(new PilihanPhp { Id = p.Id, Teks = LabelPhp(p, semua) });

            string simpan = null;
            if (_e.Active != null && b.Situs != null) _e.Active.PhpPerSitus.TryGetValue(b.Situs.Path, out simpan);
            var pilih = string.IsNullOrEmpty(simpan) ? daftar[0]
                      : daftar.FirstOrDefault(x => string.Equals(x.Id, simpan, StringComparison.OrdinalIgnoreCase));
            if (pilih == null)
            {
                pilih = new PilihanPhp { Id = simpan, Teks = Lang.T("{0} - tidak ada di komputer ini", simpan) };
                daftar.Insert(1, pilih);
            }
            b.PilihanPhp = daftar;
            b.PhpTerpilih = pilih;

            var dipakai = _e.PhpUntuk(b.Situs);
            b.KeteranganPhp = dipakai == null ? ""
                : "PHP " + dipakai.Version + " - php.ini: "
                  + Path.Combine(ConfigWriter.FolderPhpIni(dipakai,
                        _e.Php != null && dipakai.Id == _e.Php.Id && _e.Settings.PhpIniKeFolderPhp), "php.ini");
        }

        /// <summary>
        /// "8.3.12 · x64". Toolset dan TS/NTS sengaja tidak ditulis: situs yang
        /// memilih versi sendiri dilayani php-cgi sebagai proses terpisah, jadi
        /// keduanya tidak berpengaruh - dan label panjang terpotong di kolomnya.
        /// Nama folder baru ditambahkan bila dua paket akan tampak sama.
        /// </summary>
        static string LabelPhp(BinPackage p, List<BinPackage> semua)
        {
            var teks = p.Version + (string.IsNullOrEmpty(p.Arch) ? "" : " · " + p.Arch);
            bool kembar = semua.Count(x => x.Version == p.Version && x.Arch == p.Arch) > 1;
            return kembar ? teks + " · " + p.Id : teks;
        }

        /// <summary>
        /// Versi PHP sebuah situs diganti.
        ///
        /// SelectionChanged juga menyala saat kotaknya pertama kali digambar -
        /// karena itu pilihan dibandingkan dulu dengan yang tersimpan, dan hanya
        /// perubahan sungguhan yang menulis profil dan konfigurasi.
        /// </summary>
        void CmbPhpSitus_Changed(object sender, SelectionChangedEventArgs e)
        {
            var cmb = sender as ComboBox;
            var b = cmb != null ? cmb.DataContext as Baris : null;
            var pilih = cmb != null ? cmb.SelectedItem as PilihanPhp : null;
            if (b == null || b.Situs == null || pilih == null || _e.Active == null) return;

            string lama;
            _e.Active.PhpPerSitus.TryGetValue(b.Situs.Path, out lama);
            if (string.Equals(lama ?? "", pilih.Id ?? "", StringComparison.OrdinalIgnoreCase)) return;

            if (string.IsNullOrEmpty(pilih.Id)) _e.Active.PhpPerSitus.Remove(b.Situs.Path);
            else _e.Active.PhpPerSitus[b.Situs.Path] = pilih.Id;
            b.PhpTerpilih = pilih;
            ProfileStore.Save(_e.Active);
            _e.Apply();

            var dipakai = _e.PhpUntuk(b.Situs);
            _e.Say("Situs " + b.Situs.Folder + " kini memakai PHP "
                   + (dipakai != null ? dipakai.Version : "profil") + ".");

            // php_value dan php_flag di .htaccess hanya dimengerti mod_php. Di
            // bawah FastCGI keduanya diabaikan TANPA SUARA - setelan yang selama
            // ini dipakai aplikasi lenyap begitu saja.
            bool lewatKolam = dipakai != null && (_e.Php == null || dipakai.Id != _e.Php.Id);
            if (lewatKolam)
            {
                var berkas = PhpValueDiHtaccess(b.Situs);
                if (berkas != null)
                    AppState.Warn(".htaccess di " + b.Situs.Folder + " memakai php_value atau php_flag ("
                                  + berkas + "). Di PHP " + dipakai.Version
                                  + " situs ini dilayani lewat FastCGI, dan baris semacam itu diabaikan. "
                                  + "Pindahkan setelannya ke .user.ini di folder yang sama.");
            }
            IsiPilihanPhp(b);
        }

        /// <summary>Berkas .htaccess situs yang memuat php_value/php_flag, atau null.</summary>
        static string PhpValueDiHtaccess(Site s)
        {
            foreach (var folder in new[] { s.Path, s.DocRoot })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                var f = Path.Combine(folder, ".htaccess");
                try
                {
                    if (File.Exists(f) && File.ReadAllLines(f).Any(l =>
                            System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*php_(value|flag|admin_value|admin_flag)\b",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        return f;
                }
                catch { }
            }
            return null;
        }

        void TampilkanInfo()
        {
            var pesan = new List<string>(_e.SiteWarnings ?? new List<string>());
            PanelInfo.Visibility = pesan.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (pesan.Count == 0) return;
            LblInfo.Text = pesan.Count == 1
                ? "1 catatan tentang daftar situs"
                : pesan.Count + " catatan tentang daftar situs";
            TxtInfoSitus.Text = string.Join(Environment.NewLine + Environment.NewLine, pesan.ToArray());
        }

        Site Terpilih()
        {
            var b = Daftar.SelectedItem as Baris;
            return b != null ? b.Situs : null;
        }

        /// <summary>Menahan penangan saat kotak diisi program, bukan oleh pengguna.</summary>
        bool _mengisi;

        /// <summary>
        /// Folder proyek adalah setelan PROFIL, bukan setelan global - jadi yang
        /// disunting di sini adalah profil yang sedang aktif, dan labelnya
        /// menyebut namanya supaya itu tidak jadi kejutan.
        /// </summary>
        void TxtDocRoot_Lepas(object sender, RoutedEventArgs e)
        {
            if (_mengisi || _e.Active == null) return;
            var sebelum = string.Join(";", _e.Active.ProjectRoots);
            _e.Active.ProjectRoots = (TxtDocRoot.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimEnd('\\')).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.Join(";", _e.Active.ProjectRoots) == sebelum) return;

            ProfileStore.Save(_e.Active);
            // Daftar situs di bawah langsung ikut berubah - itulah gunanya kotak
            // ini berada di halaman yang sama.
            foreach (var w in _e.Apply()) _e.Say(w);
            AppState.RaiseChanged();
            Isi();

            var hilang = _e.Active.ProjectRoots
                .Where(r => !System.IO.Directory.Exists(r)).ToList();
            TxtStatusRoot.Text = hilang.Count > 0
                ? "Tersimpan, tapi folder ini belum ada: " + string.Join(", ", hilang.ToArray())
                : "Tersimpan " + DateTime.Now.ToString("HH:mm:ss") + ".";
        }

        void BtnPilihFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_e.Active == null) return;
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Pilih folder proyek untuk ditambahkan ke profil ini";
                dlg.SelectedPath = Paths.Www;
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                // Ditambahkan sebagai baris baru, bukan menimpa: tombol ini ada
                // justru untuk menyusun daftar berisi beberapa folder.
                var ada = (TxtDocRoot.Text ?? "").TrimEnd();
                if (ada.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Any(x => string.Equals(x.Trim().TrimEnd('\\'),
                                               dlg.SelectedPath.TrimEnd('\\'),
                                               StringComparison.OrdinalIgnoreCase)))
                {
                    AppState.Info("Folder itu sudah ada di daftar.");
                    return;
                }
                TxtDocRoot.Text = ada.Length == 0
                    ? dlg.SelectedPath
                    : ada + Environment.NewLine + dlg.SelectedPath;
                TxtDocRoot_Lepas(sender, e);
            }
        }

        void Daftar_DoubleClick(object sender, RoutedEventArgs e) { BtnBuka_Click(sender, e); }

        static string Kosong(string url)
        {
            return string.IsNullOrEmpty(url)
                ? Lang.T("(tidak terjangkau tanpa Virtual Host)") : url;
        }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            if (s == null) return;
            var url = _e.SiteUrl(s);
            if (string.IsNullOrEmpty(url))
            {
                AppState.Info(Lang.T("Situs ini ada di folder proyek kedua, yang hanya terjangkau lewat Virtual Host. Nyalakan Virtual Host di Pengaturan, atau pindahkan foldernya ke folder proyek utama."));
                return;
            }
            if (_e.Services.WebState != ServiceState.Jalan
                && !AppState.Ask("Web server belum jalan, jadi halamannya kemungkinan besar tidak terbuka. "
                                 + "Tetap buka?")) return;
            Shell.Open(url);
        }

        void BtnFolder_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            Shell.Open(s != null ? s.Path : SiteScanner.DocumentRoot(_e.Active));
        }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            var s = Terpilih();
            // PHP milik SITUS ini yang ada di PATH - php artisan dan composer
            // untuk proyek Laravel butuh PHP 8.x walau profilnya PHP 5.6.
            Shell.OpenTerminal(_e.Settings.Terminal,
                s != null ? s.Path : SiteScanner.DocumentRoot(_e.Active), _e.ToolEnv(s));
        }

        void BtnSegarkan_Click(object sender, RoutedEventArgs e)
        {
            var warnings = _e.Apply();
            Isi();
            if (warnings.Any(w => w.Contains("Administrator")))
            {
                if (Program.RestartAsAdmin("Berkas hosts hanya bisa disunting dengan hak Administrator.")) return;
            }
            TampilkanInfo();
        }

        void BtnHosts_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.HostsFile); }

        void BtnBuat_Click(object sender, RoutedEventArgs e)
        {
            var nama = InputDialog.Tanya(Window.GetWindow(this), "Buat situs",
                "Nama folder proyek baru. Phoron membuat foldernya berikut index.php "
                + "contoh, lalu mendaftarkan alamatnya.",
                "Akan dibuat di " + (CmbRootBaru.SelectedItem as string
                                     ?? SiteScanner.DocumentRoot(_e.Active)));
            if (nama == null) return;
            if (nama.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { AppState.Warn("Nama folder mengandung karakter yang tidak boleh dipakai."); return; }

            var root = CmbRootBaru.SelectedItem as string ?? SiteScanner.DocumentRoot(_e.Active);
            var dir = Path.Combine(root, nama);
            if (Directory.Exists(dir)) { AppState.Warn("Folder itu sudah ada."); return; }
            try
            {
                Directory.CreateDirectory(dir);
                var host = SiteScanner.SafeHost(nama) + "." + SiteScanner.Suffix(_e.Active);
                File.WriteAllText(Path.Combine(dir, "index.php"),
                    "<?php\n"
                    + "// Dibuat oleh Phoron.\n"
                    // Nama folder dikodekan sebagai HTML sebelum masuk ke string PHP
                    // berkutip tunggal. Dulu disisipkan mentah, jadi folder
                    // bernama O'Neil memutus stringnya dan halaman pertama situs
                    // baru itu langsung berupa galat sintaks PHP. HtmlEncode
                    // mengubah apostrof jadi &#39;, dan nama folder Windows tidak
                    // bisa memuat garis miring terbalik - jadi tidak ada lagi yang
                    // bisa memutus string.
                    + "echo '<h1>" + System.Net.WebUtility.HtmlEncode(nama) + "</h1>';\n"
                    + "echo '<p>PHP ' . PHP_VERSION . ' lewat ' . php_sapi_name() . '</p>';\n",
                    new UTF8Encoding(false));
                _e.Apply();
                Isi();
                _e.Say("Situs " + host + " dibuat.");
                AppState.Info("Situs siap di " + host + ".");
            }
            catch (Exception ex) { AppState.Warn("Gagal membuat situs: " + ex.Message); }
        }
    }
}
