using System.Text.RegularExpressions;

namespace Phoron.Tests
{
    public static partial class Program
    {
        /// <summary>
        /// Penjaga untuk 1.35.2 dan 1.35.3: dropdown yang "tergulir kembali ke asal".
        ///
        /// Gaya ComboBox WPF UI menyembunyikan scrollbar daftarnya
        /// (VerticalScrollBarVisibility=Hidden). Tanpa scrollbar, pengguna
        /// menggulir dengan menekan lalu menyeret isi daftar - dan ComboBox WPF
        /// menjawab seretan yang keluar dari tepi atas dengan melompat ke item
        /// paling atas, lalu memilihnya saat tombol dilepas. Dibuktikan dengan
        /// mouse sungguhan pada halaman Versi: offset 192 -> 0, terpilih item 0.
        /// Dengan scrollbar tampak, seretan thumb yang sama tidak memilih apa pun.
        /// </summary>
        static void UjiDropdown()
        {
            Bagian("Dropdown punya scrollbar");

            var app = BacaSumber("Phoron.App", "App.xaml");
            if (app == null) return;
            var gaya = Regex.Match(app,
                @"<Style\s+TargetType=""ComboBox""\s+BasedOn=""\{StaticResource DefaultComboBoxStyle\}""\s*>(.*?)</Style>",
                RegexOptions.Singleline);
            // BasedOn="{StaticResource {x:Type ComboBox}}" terurai menjadi null di
            // App.xaml - dibuktikan dengan membaca Style.BasedOn saat aplikasi
            // berjalan - dan semua dropdown jatuh ke tampilan klasik Windows (1.35.2).
            Ok("Gaya implisit ComboBox dibangun di atas DefaultComboBoxStyle milik WPF UI", gaya.Success,
               "BasedOn {x:Type ComboBox} terurai null: dropdown tampil klasik; tanpa gaya ini scrollbarnya tersembunyi");
            Ok("Scrollbar daftar dropdown tidak disembunyikan",
               gaya.Success && Regex.IsMatch(gaya.Groups[1].Value,
                   @"Property=""ScrollViewer\.VerticalScrollBarVisibility""\s+Value=""(Auto|Visible)"""),
               "tanpa scrollbar, menyeret daftar melompat ke item teratas dan memilihnya");
            Ok("Gaya itu implisit (tanpa x:Key) sehingga mengenai semua dropdown",
               gaya.Success && !Regex.IsMatch(gaya.Value, @"^<Style[^>]*x:Key"), "");
        }
    }
}
