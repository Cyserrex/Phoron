using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Jendela kemajuan unduhan pembaruan.
    ///
    /// Unduhan yang hanya berjalan di latar tidak memberi tahu apa-apa: berkas
    /// 5 MB di jaringan lambat bisa memakan menit, dan tanpa jendela ini orang
    /// tidak tahu apakah Phoron sedang bekerja, macet, atau sudah gagal diam-diam.
    /// </summary>
    public partial class DownloadWindow
    {
        readonly HasilCek _hasil;
        readonly CancellationTokenSource _batal = new CancellationTokenSource();

        /// <summary>Jalur berkas hasil unduhan; null bila gagal atau dibatalkan.</summary>
        public string Berkas { get; private set; }
        public string Galat { get; private set; }

        public DownloadWindow(HasilCek hasil)
        {
            InitializeComponent();
            _hasil = hasil;
            TxtJudul.Text = "Phoron " + hasil.Versi;
            TxtSumber.Text = hasil.UrlInstaller;
            Loaded += async (s, e) => await Jalankan();
        }

        async Task Jalankan()
        {
            var kemajuan = new Progress<KemajuanUnduh>(k =>
            {
                if (k.Total > 0)
                {
                    Bar.IsIndeterminate = false;
                    Bar.Value = k.Persen;
                    TxtPersen.Text = k.Persen + "%";
                }
                else
                {
                    // Server yang tidak menyebut Content-Length tetap harus
                    // terlihat bergerak, bukan diam di nol.
                    Bar.IsIndeterminate = true;
                    TxtPersen.Text = "";
                }
                TxtUkuran.Text = k.ToString();
            });

            string galat = null;
            Berkas = await Updater.UnduhInstallerAsync(_hasil, kemajuan, _batal.Token, g => galat = g);
            Galat = galat;
            DialogResult = Berkas != null;
        }

        void BtnBatal_Click(object sender, RoutedEventArgs e)
        {
            BtnBatal.IsEnabled = false;
            TxtUkuran.Text = "Membatalkan...";
            _batal.Cancel();
        }
    }
}
