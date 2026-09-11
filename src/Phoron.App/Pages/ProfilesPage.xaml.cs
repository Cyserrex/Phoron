using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class ProfilesPage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        Profile _current;
        bool _loading;

        /// <summary>Baris ComboBox versi; entri kosong dipakai untuk "tidak dipakai".</summary>
        class Row
        {
            public BinPackage Pkg;
            public string Text { get; set; }
            public override string ToString() { return Text; }
        }

        public ProfilesPage()
        {
            InitializeComponent();
            IsiComboVersi();
            IsiDaftar(_e.Active);
        }

        void IsiComboVersi()
        {
            CmbPhp.ItemsSource = Rows(BinKind.Php);
            CmbApache.ItemsSource = Rows(BinKind.Apache);
            CmbNginx.ItemsSource = Rows(BinKind.Nginx);
            CmbMysql.ItemsSource = Rows(BinKind.MySql);
        }

        List<Row> Rows(BinKind kind)
        {
            var list = new List<Row> { new Row { Pkg = null, Text = "(tidak dipakai)" } };
            list.AddRange(_e.Of(kind).Select(p => new Row
            {
                Pkg = p,
                // Nama folder ikut ditampilkan karena itulah yang tersimpan di
                // berkas profil - memudahkan mencocokkan saat menyunting manual.
                Text = p.Label + "   —   " + p.Id,
            }));
            return list;
        }

        void IsiDaftar(Profile pilih)
        {
            _loading = true;
            Daftar.ItemsSource = null;
            Daftar.ItemsSource = _e.Profiles;
            _loading = false;
            Daftar.SelectedItem = _e.Profiles.FirstOrDefault(p => pilih != null && p.FileName == pilih.FileName)
                                  ?? _e.Profiles.FirstOrDefault();
        }

        void Daftar_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _current = Daftar.SelectedItem as Profile;
            TampilkanProfil(_current);
        }

        void TampilkanProfil(Profile p)
        {
            Editor.IsEnabled = p != null;
            if (p == null) return;
            _loading = true;
            TxtNama.Text = p.Name;
            Pilih(CmbWeb, p.WebServer);
            PilihPkg(CmbPhp, p.PhpId);
            PilihPkg(CmbApache, p.ApacheId);
            PilihPkg(CmbNginx, p.NginxId);
            PilihPkg(CmbMysql, p.MySqlId);
            TxtPortHttp.Text = p.HttpPort.ToString();
            TxtPortHttps.Text = p.HttpsPort.ToString();
            TxtPortMysql.Text = p.MySqlPort.ToString();
            TxtDocRoot.Text = string.Join(Environment.NewLine, p.ProjectRoots);
            TxtSuffix.Text = p.SiteSuffix ?? "test";
            TxtCatatan.Text = p.Notes ?? "";
            _loading = false;
            AturTampilanWeb();
        }

        static void Pilih(ComboBox box, string tag)
        {
            foreach (ComboBoxItem item in box.Items)
                if ((item.Tag ?? "").ToString() == tag) { box.SelectedItem = item; return; }
            box.SelectedIndex = 0;
        }

        static void PilihPkg(ComboBox box, string id)
        {
            var rows = box.ItemsSource as List<Row>;
            if (rows == null) return;
            box.SelectedItem = rows.FirstOrDefault(r => r.Pkg != null
                                   && string.Equals(r.Pkg.Id, id, StringComparison.OrdinalIgnoreCase))
                               ?? rows[0];
        }

        static string IdDari(ComboBox box)
        {
            var row = box.SelectedItem as Row;
            return row != null && row.Pkg != null ? row.Pkg.Id : "";
        }

        void CmbWeb_Changed(object sender, SelectionChangedEventArgs e) { AturTampilanWeb(); }

        /// <summary>Sembunyikan pilihan yang tidak relevan - profil Nginx tidak memakai Apache dan sebaliknya.</summary>
        void AturTampilanWeb()
        {
            var item = CmbWeb.SelectedItem as ComboBoxItem;
            bool nginx = item != null && (item.Tag ?? "").ToString() == "nginx";
            LblNginx.Visibility = CmbNginx.Visibility = nginx ? Visibility.Visible : Visibility.Collapsed;
            LblApache.Visibility = CmbApache.Visibility = nginx ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ Aksi

        bool Kumpulkan(Profile p)
        {
            if (string.IsNullOrWhiteSpace(TxtNama.Text)) { AppState.Warn("Nama profil belum diisi."); return false; }
            int http, https, mysql;
            if (!int.TryParse(TxtPortHttp.Text, out http) || http < 1 || http > 65535)
            { AppState.Warn("Port HTTP tidak masuk akal."); return false; }
            if (!int.TryParse(TxtPortHttps.Text, out https) || https < 1 || https > 65535)
            { AppState.Warn("Port HTTPS tidak masuk akal."); return false; }
            if (!int.TryParse(TxtPortMysql.Text, out mysql) || mysql < 1 || mysql > 65535)
            { AppState.Warn("Port MySQL tidak masuk akal."); return false; }

            p.Name = TxtNama.Text.Trim();
            var item = CmbWeb.SelectedItem as ComboBoxItem;
            p.WebServer = item != null ? (item.Tag ?? "apache").ToString() : "apache";
            p.PhpId = IdDari(CmbPhp);
            p.ApacheId = IdDari(CmbApache);
            p.NginxId = IdDari(CmbNginx);
            p.MySqlId = IdDari(CmbMysql);
            p.HttpPort = http;
            p.HttpsPort = https;
            p.MySqlPort = mysql;
            p.ProjectRoots = (TxtDocRoot.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimEnd('\\')).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var hilang = p.ProjectRoots.Where(r => !System.IO.Directory.Exists(r)).ToList();
            if (hilang.Count > 0
                && !AppState.Ask("Folder ini belum ada:\n\n" + string.Join("\n", hilang)
                                 + "\n\nTetap simpan?")) return false;
            p.SiteSuffix = string.IsNullOrWhiteSpace(TxtSuffix.Text) ? "test" : TxtSuffix.Text.Trim();
            p.Notes = TxtCatatan.Text;

            var php = _e.Find(BinKind.Php, p.PhpId);
            var apache = _e.Find(BinKind.Apache, p.ApacheId);
            if (p.WebServer == "apache" && php != null && apache != null
                && !string.IsNullOrEmpty(php.Compiler) && !string.IsNullOrEmpty(apache.Compiler)
                && !string.Equals(php.Compiler, apache.Compiler, StringComparison.OrdinalIgnoreCase))
            {
                // Bukan penghalang - ada kombinasi yang tetap jalan - tapi ini
                // penyebab paling umum Apache mati seketika tanpa pesan.
                if (!AppState.Ask("PHP dibangun dengan " + php.Compiler + " sedangkan Apache dengan "
                    + apache.Compiler + ".\n\nKombinasi beda toolset biasanya membuat Apache gagal start. "
                    + "Tetap simpan?")) return false;
            }
            return true;
        }

        void BtnSimpan_Click(object sender, RoutedEventArgs e) { Simpan(); }

        Profile Simpan()
        {
            if (_current == null) return null;
            if (!Kumpulkan(_current)) return null;
            ProfileStore.Save(_current);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(_current);
            _e.Say("Profil \"" + _current.Name + "\" disimpan.");
            return _current;
        }

        async void BtnSimpanSwitch_Click(object sender, RoutedEventArgs e)
        {
            var p = Simpan();
            if (p == null) return;
            var target = _e.Profiles.FirstOrDefault(x => x.FileName == p.FileName);
            if (target == null) return;
            var warnings = await _e.SwitchAsync(target);
            AppState.RaiseChanged();
            var main = Window.GetWindow(this) as MainWindow;
            if (main != null) main.RefreshStatus();
            AppState.ShowWarnings(warnings);
            AppState.Info("Sekarang memakai profil \"" + target.Name + "\".");
        }

        void BtnBaru_Click(object sender, RoutedEventArgs e)
        {
            var php = _e.Of(BinKind.Php).FirstOrDefault();
            var p = new Profile { Name = "Profil baru" };
            if (php != null)
            {
                p.PhpId = php.Id;
                var apache = ProfileStore.PickApache(php, _e.Of(BinKind.Apache));
                if (apache != null) p.ApacheId = apache.Id;
                // Sama seperti profil bawaan: lahir dengan daftar ekstensi yang
                // masuk akal, bukan kosong.
                p.PhpExtensions = ConfigWriter.EkstensiDisarankan(php);
            }
            var db = _e.Of(BinKind.MySql).FirstOrDefault();
            if (db != null) p.MySqlId = db.Id;
            p.FileName = ProfileStore.UniqueFileName(p.Name);
            ProfileStore.Save(p);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(p);
        }

        void BtnDuplikat_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var copy = _current.Clone();
            copy.Name = _current.Name + " (salinan)";
            copy.FileName = ProfileStore.UniqueFileName(copy.Name);
            ProfileStore.Save(copy);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(copy);
        }

        void BtnHapus_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            if (_e.Profiles.Count <= 1) { AppState.Warn("Sisakan minimal satu profil."); return; }
            if (!AppState.Ask("Hapus profil \"" + _current.Name + "\"?")) return;
            ProfileStore.Delete(_current);
            _e.Reload();
            AppState.RaiseChanged();
            IsiDaftar(_e.Active);
        }

        void BtnPilihFolder_Click(object sender, RoutedEventArgs e)
        {
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
            }
        }
    }
}
