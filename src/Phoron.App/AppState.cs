using System;
using System.Collections.Generic;
using System.Windows;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Satu Engine dipakai bersama semua halaman. Halaman dibuat dan dibuang
    /// mengikuti navigasi, jadi keadaan tidak boleh menumpang di dalamnya.
    /// </summary>
    internal static class AppState
    {
        public static Engine Engine { get; private set; }

        /// <summary>Diangkat tiap kali daftar profil/versi berubah, supaya halaman lain ikut menyegarkan diri.</summary>
        public static event Action Changed;

        public static void Init(Engine engine) { Engine = engine; }

        public static void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }

        public static void Info(string text, string title = "Phoron")
        {
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void Warn(string text, string title = "Phoron")
        {
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static bool Ask(string text, string title = "Phoron")
        {
            return MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        }

        public static void ShowWarnings(IEnumerable<string> warnings)
        {
            var list = new List<string>(warnings ?? new string[0]);
            if (list.Count == 0) return;
            Warn(string.Join(Environment.NewLine + Environment.NewLine, list), "Perhatian");
        }
    }
}
