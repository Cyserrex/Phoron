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

        public static Ini Load(string path)
        {
            var ini = new Ini();
            if (!File.Exists(path)) return ini;
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    ini.EnsureSection(section);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                ini.Set(section, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return ini;
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
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
