using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Phoron.Core;
using Forms = System.Windows.Forms;

namespace Phoron.App.Pages
{
    public partial class NodePage : UserControl
    {
        readonly Engine _e = AppState.Engine;
        bool _mengisi;

        /// <summary>Keluaran per proyek, disimpan di halaman supaya tetap ada saat berpindah pilihan.</summary>
        static readonly Dictionary<string, Queue<string>> _log =
            new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);

        public class Baris
        {
            public string Nama { get; set; }
            public string Folder { get; set; }
            public string Kerangka { get; set; }
            public string Perintah { get; set; }
            public string Status { get; set; }
            public string Alamat { get; set; }
            public NodeApp App;
        }

        public NodePage()
        {
            InitializeComponent();
            _e.Node.Output += OnOutput;
            _e.Node.StateChanged += OnState;
            _e.Node.UrlFound += OnUrl;
            Unloaded += (s, ev) =>
            {
                _e.Node.Output -= OnOutput;
                _e.Node.StateChanged -= OnState;
                _e.Node.UrlFound -= OnUrl;
            };
            IsiNode();
            Isi();
        }

        // ------------------------------------------------------------- Tampilan

        void IsiNode()
        {
            var rows = new List<object>();
            // Entri pertama: Node dari PATH sistem. Itu yang dipakai orang di
            // terminal sehari-hari, jadi jadikan pilihan bakunya.
            rows.Add(new { Teks = "Node dari PATH sistem", Pkg = (BinPackage)null });
            foreach (var n in _e.Of(BinKind.Node))
                rows.Add(new { Teks = n.Id + "   —   " + n.SourceRoot, Pkg = n });
            CmbNode.ItemsSource = rows;
            CmbNode.DisplayMemberPath = "Teks";
            CmbNode.SelectedIndex = 0;
        }

        void Isi()
        {
            _mengisi = true;
            var terpilih = (Daftar.SelectedItem as Baris);
            var jalurTerpilih = terpilih != null ? terpilih.App.Path : null;

            Daftar.ItemsSource = _e.NodeAppsList.Select(a => new Baris
            {
                Nama = a.DisplayName,
                Folder = a.Path,
                Kerangka = PackageJson.DetectFramework(a.Path),
                Perintah = (a.Manager == "yarn" ? a.Manager + " " : a.Manager + " run ") + a.Script,
                Status = _e.Node.IsRunning(a.Path) ? "jalan"
                       : Directory.Exists(a.Path) ? "berhenti" : "folder hilang",
                Alamat = _e.Node.UrlOf(a.Path) ?? "",
                App = a,
            }).ToList();

            if (jalurTerpilih != null)
                Daftar.SelectedItem = Daftar.Items.Cast<Baris>()
                    .FirstOrDefault(b => b.App.Path == jalurTerpilih);
            if (Daftar.SelectedItem == null && Daftar.Items.Count > 0) Daftar.SelectedIndex = 0;

            TxtInfo.Text = _e.NodeAppsList.Count == 0
                ? "Belum ada proyek. Tekan \"Tambah proyek...\" lalu pilih folder yang berisi package.json — "
                  + "boleh di mana saja, tidak harus di dalam www."
                : _e.NodeAppsList.Count + " proyek terdaftar. Klik ganda untuk menjalankan atau menghentikan.";
            _mengisi = false;
            SegarkanPilihan();
        }

        NodeApp Terpilih()
        {
            var b = Daftar.SelectedItem as Baris;
            return b != null ? b.App : null;
        }

        void SegarkanPilihan()
        {
            var a = Terpilih();
            var jalan = a != null && _e.Node.IsRunning(a.Path);
            BtnJalan.Content = jalan ? "Hentikan" : "Jalankan";
            BtnJalan.Appearance = jalan
                ? Wpf.Ui.Controls.ControlAppearance.Danger
                : Wpf.Ui.Controls.ControlAppearance.Primary;
            BtnJalan.IsEnabled = a != null;

            _mengisi = true;
            if (a != null)
            {
                var skrip = PackageJson.Scripts(a.Path);
                if (skrip.Count == 0) skrip.Add(a.Script);
                if (!skrip.Contains(a.Script)) skrip.Insert(0, a.Script);
                CmbSkrip.ItemsSource = skrip;
                CmbSkrip.SelectedItem = a.Script;

                var rows = CmbNode.ItemsSource as List<object>;
                if (rows != null)
                {
                    var cocok = rows.FirstOrDefault(r =>
                    {
                        var pkg = r.GetType().GetProperty("Pkg").GetValue(r, null) as BinPackage;
                        return pkg != null && string.Equals(pkg.Id, a.NodeId, StringComparison.OrdinalIgnoreCase);
                    });
                    CmbNode.SelectedItem = cocok ?? rows[0];
                }
            }
            else CmbSkrip.ItemsSource = null;
            _mengisi = false;

            TxtJudulLog.Text = a == null ? "Keluaran" : "Keluaran — " + a.DisplayName;
            TampilkanLog(a != null ? a.Path : null);
        }

        void TampilkanLog(string folder)
        {
            Queue<string> q;
            TxtLog.Text = folder != null && _log.TryGetValue(folder, out q)
                ? string.Join(Environment.NewLine, q) : "";
            TxtLog.ScrollToEnd();
        }

        // -------------------------------------------------------------- Kejadian

        void OnOutput(string folder, string baris)
        {
            Dispatcher.Invoke(() =>
            {
                Queue<string> q;
                if (!_log.TryGetValue(folder, out q)) _log[folder] = q = new Queue<string>();
                q.Enqueue(baris);
                // Server pengembangan bisa mencetak ribuan baris; hanya ekor
                // terakhirnya yang berguna dan hanya itu yang disimpan.
                while (q.Count > 400) q.Dequeue();
                var a = Terpilih();
                if (a != null && string.Equals(a.Path, folder, StringComparison.OrdinalIgnoreCase))
                    TampilkanLog(folder);
            });
        }

        void OnState(string folder, bool jalan) { Dispatcher.Invoke(Isi); }

        void OnUrl(string folder, string url)
        {
            Dispatcher.Invoke(() =>
            {
                _e.Say("Proyek " + Path.GetFileName(folder.TrimEnd('\\')) + " siap di " + url);
                Isi();
            });
        }

        void Daftar_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            SegarkanPilihan();
        }

        void Daftar_DoubleClick(object sender, RoutedEventArgs e) { BtnJalan_Click(sender, e); }

        void CmbSkrip_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var a = Terpilih();
            var s = CmbSkrip.SelectedItem as string;
            if (a == null || s == null) return;
            a.Script = s;
            NodeAppStore.SaveAll(_e.NodeAppsList);
            Isi();
        }

        void CmbNode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_mengisi) return;
            var a = Terpilih();
            if (a == null || CmbNode.SelectedItem == null) return;
            var pkg = CmbNode.SelectedItem.GetType().GetProperty("Pkg")
                        .GetValue(CmbNode.SelectedItem, null) as BinPackage;
            a.NodeId = pkg != null ? pkg.Id : "";
            NodeAppStore.SaveAll(_e.NodeAppsList);
        }

        // ------------------------------------------------------------------ Aksi

        void BtnJalan_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a == null) return;
            if (_e.Node.IsRunning(a.Path)) { _e.Node.Stop(a.Path); return; }

            if (!PackageJson.HasNodeModules(a.Path)
                && !AppState.Ask("Folder node_modules belum ada di proyek ini, jadi perintahnya "
                                 + "kemungkinan besar gagal.\n\nJalankan juga?")) return;

            var node = string.IsNullOrEmpty(a.NodeId) ? null : _e.Find(BinKind.Node, a.NodeId);
            var err = _e.Node.Start(a, node);
            if (err != null) { AppState.Warn(err); return; }
            _e.Say("Menjalankan " + a.DisplayName + " (" + a.Manager + " " + a.Script + ").");
            Isi();
        }

        void BtnTambah_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Pilih folder proyek Node (yang berisi package.json)";
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                var folder = dlg.SelectedPath.TrimEnd('\\');

                if (_e.NodeAppsList.Any(x => string.Equals(x.Path, folder, StringComparison.OrdinalIgnoreCase)))
                { AppState.Info("Folder itu sudah terdaftar."); return; }
                if (!PackageJson.LooksLikeNodeProject(folder)
                    && !AppState.Ask("Tidak ada package.json di folder itu. Tetap tambahkan?")) return;

                var app = new NodeApp
                {
                    Path = folder,
                    Manager = PackageJson.DetectManager(folder),
                };
                // Skrip baku dipilih dari yang benar-benar ada: "dev" pada
                // kebanyakan proyek, tapi sebagian hanya punya "start".
                var skrip = PackageJson.Scripts(folder);
                app.Script = skrip.FirstOrDefault(s => s == "dev")
                          ?? skrip.FirstOrDefault(s => s == "start")
                          ?? skrip.FirstOrDefault() ?? "dev";

                _e.NodeAppsList.Add(app);
                NodeAppStore.SaveAll(_e.NodeAppsList);
                Isi();
                Daftar.SelectedItem = Daftar.Items.Cast<Baris>()
                    .FirstOrDefault(b => b.App.Path == folder);
            }
        }

        void BtnHapus_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a == null) return;
            if (!AppState.Ask("Hapus \"" + a.DisplayName + "\" dari daftar Phoron?\n\n"
                              + "Folder dan isinya TIDAK dihapus.")) return;
            if (_e.Node.IsRunning(a.Path)) _e.Node.Stop(a.Path);
            _e.NodeAppsList.Remove(a);
            NodeAppStore.SaveAll(_e.NodeAppsList);
            Isi();
        }

        void BtnBuka_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a == null) return;
            var url = _e.Node.UrlOf(a.Path);
            if (url == null)
            {
                AppState.Info("Alamatnya belum terlihat di keluaran. Jalankan proyeknya dulu, "
                              + "lalu tunggu server pengembangan mencetak alamatnya.");
                return;
            }
            Shell.Open(url);
        }

        void BtnFolder_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a != null) Shell.Open(a.Path);
        }

        void BtnTerminal_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a == null) return;
            var env = _e.ToolEnv();
            var node = string.IsNullOrEmpty(a.NodeId) ? null : _e.Find(BinKind.Node, a.NodeId);
            if (node != null) env["PATH"] = node.Path + ";" + env["PATH"];
            Shell.OpenTerminal(_e.Settings.Terminal, a.Path, env);
        }

        void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var a = Terpilih();
            if (a == null) return;
            // Dibuka di terminal, bukan dijalankan diam-diam: install bisa
            // memakan menit, kadang bertanya, dan keluarannya perlu dilihat utuh.
            var env = _e.ToolEnv();
            var node = string.IsNullOrEmpty(a.NodeId) ? null : _e.Find(BinKind.Node, a.NodeId);
            if (node != null) env["PATH"] = node.Path + ";" + env["PATH"];
            Shell.OpenTerminalWithCommand(_e.Settings.Terminal, a.Path, env,
                (a.Manager ?? "npm") + " install");
        }
    }
}
