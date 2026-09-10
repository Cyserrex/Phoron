using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Phoron.App
{
    /// <summary>
    /// Muat DLL pendamping dari dalam exe-nya sendiri, supaya Phoron.exe tetap
    /// satu berkas. Aplikasi ini dipindah-pindah bersama folder instalasinya;
    /// exe yang menuntut DLL di sebelahnya gagal dengan pesan yang tidak
    /// menjelaskan apa pun begitu ada satu berkas tertinggal.
    ///
    /// Dipasang dari baris pertama Main dan berada di kelas terpisah dengan
    /// sengaja: CLR memuat assembly saat metode yang MEMAKAI tipenya di-JIT,
    /// bukan saat barisnya dijalankan.
    /// </summary>
    internal static class EmbeddedAssemblies
    {
        public static void Install()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string wanted = new AssemblyName(args.Name).Name + ".dll";
                var host = Assembly.GetExecutingAssembly();

                string resource = null;
                bool gzip = false;
                foreach (string name in host.GetManifestResourceNames())
                {
                    if (name.EndsWith(wanted + ".gz", StringComparison.OrdinalIgnoreCase))
                    { resource = name; gzip = true; break; }
                    if (name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    { resource = name; break; }
                }
                if (resource == null) return null;

                using (Stream raw = host.GetManifestResourceStream(resource))
                {
                    if (raw == null) return null;
                    using (Stream stream = gzip
                               ? (Stream)new GZipStream(raw, CompressionMode.Decompress)
                               : raw)
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        return Assembly.Load(buffer.ToArray());
                    }
                }
            };
        }
    }
}
