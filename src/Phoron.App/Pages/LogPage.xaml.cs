using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Phoron.Core;

namespace Phoron.App.Pages
{
    public partial class LogPage : UserControl
    {
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public LogPage()
        {
            InitializeComponent();
            IsiDaftarBerkas();
            _timer.Tick += (s, e) => { if (ChkAuto.IsChecked == true) Muat(false); };
            _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        void IsiDaftarBerkas()
        {
            // Saringannya "*.log*", bukan "*.log": berkas hasil putaran
            // bernama phoron.log.1 dan seterusnya, dan justru di situlah
            // kejadian beberapa sesi lalu tersimpan.
            var berkas = Directory.Exists(Paths.Logs)
                ? Directory.GetFiles(Paths.Logs, "*.log*").OrderBy(f => f).ToList()
                : new System.Collections.Generic.List<string>();
            CmbBerkas.ItemsSource = berkas.Select(Path.GetFileName).ToList();
            if (CmbBerkas.Items.Count > 0) CmbBerkas.SelectedIndex = 0;
            else Isi.Text = "Belum ada berkas log. Nyalakan layanan dulu.";
        }

        string BerkasTerpilih()
        {
            var nama = CmbBerkas.SelectedItem as string;
            return nama == null ? null : Path.Combine(Paths.Logs, nama);
        }

        void Muat(bool paksaGulir)
        {
            var path = BerkasTerpilih();
            if (path == null || !File.Exists(path)) return;
            string teks;
            try
            {
                // Dibuka dengan share penuh: Apache dan MySQL memegang berkas log
                // ini sepanjang mereka hidup, dan File.ReadAllText akan gagal.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    // Hanya 400 KB terakhir yang dibaca - log akses bisa puluhan MB
                    // dan memuat semuanya membekukan jendela.
                    const long Batas = 400 * 1024;
                    if (fs.Length > Batas) fs.Seek(-Batas, SeekOrigin.End);
                    using (var r = new StreamReader(fs)) teks = r.ReadToEnd();
                }
            }
            catch (Exception ex) { teks = "Tidak bisa membaca berkas: " + ex.Message; }

            if (Isi.Text == teks) return;
            Isi.Text = teks;
            if (paksaGulir || ChkAuto.IsChecked == true) Isi.ScrollToEnd();
        }

        void CmbBerkas_Changed(object sender, SelectionChangedEventArgs e) { Muat(true); }
        void BtnMuat_Click(object sender, RoutedEventArgs e) { IsiDaftarBerkas(); Muat(true); }
        void BtnFolder_Click(object sender, RoutedEventArgs e) { Shell.Open(Paths.Logs); }

        void BtnKosong_Click(object sender, RoutedEventArgs e)
        {
            var path = BerkasTerpilih();
            if (path == null) return;
            if (!AppState.Ask("Kosongkan " + Path.GetFileName(path) + "?")) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    fs.SetLength(0);
                Muat(true);
            }
            catch (Exception ex) { AppState.Warn("Gagal mengosongkan: " + ex.Message); }
        }
    }
}
