using System;
using System.Windows.Markup;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Ekstensi markup penerjemah: {loc:T Beranda}
    ///
    /// Kuncinya teks Indonesia apa adanya, jadi berkas XAML tetap terbaca
    /// seperti kalimat biasa - bukan deretan kode seperti "nav.beranda" yang
    /// membuat tata letaknya mustahil dipahami tanpa membuka kamus.
    ///
    /// Diterjemahkan sekali saat halaman dibuat, bukan lewat pengikatan hidup.
    /// Halaman di Phoron memang dibuat ulang tiap kali dibuka, jadi mengganti
    /// bahasa cukup dengan menggambar ulang layar - dan itu jauh lebih sederhana
    /// daripada menyeret INotifyPropertyChanged ke seluruh berkas.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class TExtension : MarkupExtension
    {
        public string Teks { get; set; }

        public TExtension() { }
        public TExtension(string teks) { Teks = teks; }

        public override object ProvideValue(IServiceProvider penyedia)
        {
            return Lang.T(Teks ?? "");
        }
    }
}
