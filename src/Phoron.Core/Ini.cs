using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Pembaca/penulis INI sederhana. Dipakai untuk phoron.ini dan berkas profil.
    /// Sengaja bukan JSON: berkas ini harus enak disunting tangan oleh pengguna,
    /// dan Core tidak boleh menarik satu pun dependensi NuGet.
    /// </summary>
    public class Ini
    {
        // Urutan seksi dan kunci dipertahankan supaya berkas yang ditulis ulang
        // tidak berantakan dibanding versi yang disunting pengguna.
        readonly List<string> _sectionOrder = new List<string>();
        readonly Dictionary<string, List<KeyValuePair<string, string>>> _data =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Sections { get { return _sectionOrder; } }

        /// <summary>
        /// Hasil pembacaan berikut keterangan tentang keadaan berkasnya.
        ///
        /// Pembacaan INI sengaja memaafkan - berkas yang disunting tangan dengan
        /// satu baris nyasar tidak boleh mematikan aplikasi. Tapi memaafkan
        /// tanpa MELAPORKAN adalah cerita lain: dulu tidak ada satu pun cara
        /// bagi pemanggil untuk tahu bahwa berkasnya cacat, sehingga phoron.ini
        /// yang terpotong dibaca sebagai sah, seluruh nilai kembali ke bawaan,
        /// dan penyimpanan berikutnya menuliskan bawaan itu kembali - sisa
        /// setelan yang tadinya selamat ikut hilang.
        /// </summary>
        public sealed class HasilBaca
        {
            public Ini Isi = new Ini();
            /// <summary>Berkasnya ada. Berkas yang tidak ada BUKAN berkas rusak.</summary>
            public bool Ada;
            /// <summary>Gagal dibaca sama sekali: izin, terkunci, drive putus.</summary>
            public bool GagalBaca;
            /// <summary>Terbaca, tapi isinya ada yang tidak masuk akal.</summary>
            public bool Rusak;
            public Exception Sebab;
            public List<string> Keluhan = new List<string>();
            public int BarisTerbaca;
            public int BarisDibuang;
        }

        /// <summary>Baca sambil melaporkan keadaan berkasnya; tidak pernah melempar.</summary>
        public static HasilBaca Baca(string path)
        {
            return BacaInti(path, false);
        }

        /// <summary>
        /// Perilaku lama, tidak berubah sedikit pun: berkas yang tidak ada
        /// menghasilkan Ini kosong, dan kegagalan baca melempar. Sekitar dua
        /// puluh pemanggil bersandar pada keduanya - termasuk
        /// ProfileStore.LoadAll, yang memakai lemparannya untuk melewati profil
        /// yang rusak.
        /// </summary>
        public static Ini Load(string path)
        {
            return BacaInti(path, true).Isi;
        }

        static HasilBaca BacaInti(string path, bool lempar)
        {
            var h = new HasilBaca();
            if (!File.Exists(path)) return h;
            h.Ada = true;

            string[] baris;
            try { baris = File.ReadAllLines(path); }
            catch (Exception ex)
            {
                h.GagalBaca = true;
                h.Sebab = ex;
                h.Keluhan.Add("tidak bisa dibaca: " + ex.Message);
                if (lempar) throw;
                return h;
            }

            // Deretan byte NUL adalah tanda khas berkas yang terpotong saat
            // listrik mati: NTFS sudah mencatat panjangnya, tapi isinya belum
            // sempat turun ke piringan. Ini petunjuk paling berharga di sini,
            // sebab sisanya masih terbaca seperti berkas yang sehat.
            foreach (var b in baris)
                if (b.IndexOf('\0') >= 0)
                {
                    h.Rusak = true;
                    h.Keluhan.Add("berisi byte NUL - berkasnya kemungkinan terpotong");
                    break;
                }

            string section = "";
            for (int i = 0; i < baris.Length; i++)
            {
                // Byte NUL dibuang, bukan sekadar dilaporkan. Ia terbaca sebagai
                // bagian dari NILAI - "tema=gel" yang terpotong menghasilkan
                // nilai "gel" berekor dua ratus NUL - dan tanpa dibersihkan,
                // sampah itu ikut tertulis ke berkas yang baru. Berkas hasil
                // penyelamatan lalu terbaca cacat lagi, selamanya.
                var line = baris[i].Replace("\0", "").Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    if (line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        h.Isi.EnsureSection(section);
                        h.BarisTerbaca++;
                        continue;
                    }
                    // Dulu baris ini jatuh ke pemeriksaan "=" lalu lenyap tanpa
                    // suara, dan seluruh kunci sesudahnya masuk ke seksi yang
                    // salah.
                    h.Rusak = true;
                    h.BarisDibuang++;
                    h.Keluhan.Add("baris " + (i + 1) + ": seksi tidak ditutup - " + Potong(line));
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    h.Rusak = true;
                    h.BarisDibuang++;
                    h.Keluhan.Add("baris " + (i + 1) + ": bukan kunci=nilai - " + Potong(line));
                    continue;
                }
                var kunci = line.Substring(0, eq).Trim();
                if (kunci.Length == 0)
                {
                    h.Rusak = true;
                    h.Keluhan.Add("baris " + (i + 1) + ": kunci kosong");
                }
                h.Isi.Set(section, kunci, line.Substring(eq + 1).Trim());
                h.BarisTerbaca++;
            }
            return h;
        }

        static string Potong(string teks)
        {
            if (teks == null) return "";
            return teks.Length <= 40 ? teks : teks.Substring(0, 40) + "...";
        }

        void EnsureSection(string section)
        {
            if (_data.ContainsKey(section)) return;
            _data[section] = new List<KeyValuePair<string, string>>();
            _sectionOrder.Add(section);
        }

        public void Set(string section, string key, string value)
        {
            EnsureSection(section);
            var list = _data[section];
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    list[i] = new KeyValuePair<string, string>(list[i].Key, value);
                    return;
                }
            list.Add(new KeyValuePair<string, string>(key, value));
        }

        public string Get(string section, string key, string fallback = null)
        {
            List<KeyValuePair<string, string>> list;
            if (!_data.TryGetValue(section ?? "", out list)) return fallback;
            foreach (var kv in list)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return fallback;
        }

        public int GetInt(string section, string key, int fallback)
        {
            int v;
            return int.TryParse(Get(section, key), out v) ? v : fallback;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            var s = Get(section, key);
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            return s == "1" || s == "true" || s == "yes" || s == "on";
        }

        public IEnumerable<KeyValuePair<string, string>> Items(string section)
        {
            List<KeyValuePair<string, string>> list;
            if (_data.TryGetValue(section ?? "", out list)) return list;
            return new List<KeyValuePair<string, string>>();
        }

        public void Remove(string section, string key)
        {
            List<KeyValuePair<string, string>> list;
            if (!_data.TryGetValue(section ?? "", out list)) return;
            list.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(string path, string header = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header))
                foreach (var l in header.Split('\n')) sb.AppendLine("; " + l.TrimEnd('\r'));
            foreach (var section in _sectionOrder)
            {
                if (section.Length > 0) sb.AppendLine("[" + section + "]");
                foreach (var kv in _data[section]) sb.AppendLine(kv.Key + "=" + kv.Value);
                sb.AppendLine();
            }
            // Lewat AtomicFile: berkas ini adalah phoron.ini DAN tiap berkas
            // profil. Penulisan langsung memotong berkasnya lebih dulu, dan
            // gangguan di celah itu menyisakan berkas nol byte.
            AtomicFile.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
