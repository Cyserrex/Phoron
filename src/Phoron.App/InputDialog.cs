using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Phoron.App
{
    /// <summary>
    /// Kotak kecil untuk meminta satu baris teks.
    ///
    /// Dipakai saat kendali yang biasanya menetap di layar dipindahkan ke tombol -
    /// ruang di halaman lebih berguna untuk memberi tahu keadaan daripada
    /// menampung kotak isian yang jarang disentuh.
    /// </summary>
    public class InputDialog : Window
    {
        readonly TextBox _kotak;

        public string Nilai { get { return _kotak.Text; } }

        public InputDialog(Window induk, string judul, string keterangan, string contoh)
        {
            Title = judul;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (induk != null) Owner = induk;

            // JANGAN menyalin Background jendela induk. MainWindow adalah
            // FluentWindow ber-backdrop Mica: latarnya sengaja tembus supaya
            // Windows yang melukisnya. Disalin ke jendela biasa yang tidak punya
            // backdrop, yang tersisa hanya kehampaan - dan kotak ini tampil
            // hitam pekat dengan tulisan yang ikut tak terbaca.
            //
            // Yang benar adalah meminta warna dari tema yang sedang berlaku.
            // SetResourceReference, bukan penetapan sekali jalan: warnanya ikut
            // berubah kalau temanya diganti selagi kotak ini terbuka. Kalau
            // kuncinya tidak ada, propertinya tetap di nilai bawaan Windows -
            // putih, bukan hitam.
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

            var isi = new StackPanel { Margin = new Thickness(18) };
            isi.Children.Add(new TextBlock
            {
                Text = keterangan,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });

            _kotak = new TextBox { Text = "", Padding = new Thickness(6, 4, 6, 4) };
            // Enter menyetujui, Esc membatalkan: kotak sekecil ini tidak pantas
            // memaksa tangan pindah ke tetikus.
            _kotak.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { DialogResult = true; }
                else if (e.Key == Key.Escape) { DialogResult = false; }
            };
            isi.Children.Add(_kotak);

            if (!string.IsNullOrEmpty(contoh))
                isi.Children.Add(new TextBlock
                {
                    Text = contoh,
                    Opacity = 0.7,
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });

            var baris = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            var ok = new Wpf.Ui.Controls.Button
            {
                Content = Phoron.Core.Lang.T("Buat situs"),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
            };
            ok.Click += (s, e) => { DialogResult = true; };
            var batal = new Wpf.Ui.Controls.Button
            {
                Content = Phoron.Core.Lang.T("Batalkan"),
                IsCancel = true,
            };
            batal.Click += (s, e) => { DialogResult = false; };
            baris.Children.Add(ok);
            baris.Children.Add(batal);
            isi.Children.Add(baris);

            Content = isi;
            Loaded += (s, e) => _kotak.Focus();
        }

        /// <summary>Mengembalikan teks yang diisi, atau null bila dibatalkan/kosong.</summary>
        public static string Tanya(Window induk, string judul, string keterangan, string contoh)
        {
            var d = new InputDialog(induk, judul, keterangan, contoh);
            if (d.ShowDialog() != true) return null;
            var t = (d.Nilai ?? "").Trim();
            return t.Length == 0 ? null : t;
        }
    }
}
