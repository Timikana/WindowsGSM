using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// UNIVERSAL config editor: detects the format, parses into key/value entries, and rewrites
    /// IN PLACE (preserves comments, order, formatting). Round-trip validated on 7DtD (property-XML),
    /// Minecraft (server.properties / key=value) and sectioned INI (ARK GameUserSettings.ini...).
    /// </summary>
    public class ConfigFile : IConfigModel
    {
        public string Path { get; private set; }
        public string Format { get; private set; }   // "ini" | "propxml"

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private List<string> _lines = new List<string>();

        private static readonly Regex ReIniKey = new Regex(@"^\s*([^=\[#;][^=]*?)\s*=\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ReProp = new Regex("<property\\s+name=\"([^\"]+)\"\\s+value=\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ConfigFile Load(string path)
        {
            var cf = new ConfigFile { Path = path };
            string text = File.ReadAllText(path);
            cf._lines = new List<string>(text.Split('\n')); // keeps any trailing \r on the line (CRLF preserved)
            cf.Format = Detect(text, path);
            cf.ParseLines();
            return cf;
        }

        private static string Detect(string text, string path)
        {
            if (text.IndexOf("<property", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("name=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "propxml";
            }
            return "ini"; // ini / properties / cfg (optional [..] sections)
        }

        private void ParseLines()
        {
            _entries.Clear();
            if (Format == "propxml")
            {
                for (int i = 0; i < _lines.Count; i++)
                {
                    var m = ReProp.Match(_lines[i]);
                    if (m.Success)
                    {
                        _entries.Add(new ConfigEntry { Section = "", Key = m.Groups[1].Value, Value = m.Groups[2].Value, LineIndex = i });
                    }
                }
                return;
            }

            // ini / properties
            string section = "";
            for (int i = 0; i < _lines.Count; i++)
            {
                string raw = _lines[i];
                string s = raw.Trim();
                if (s.Length == 0 || s[0] == '#' || s[0] == ';') { continue; }
                if (s.StartsWith("[") && s.EndsWith("]")) { section = s.Substring(1, s.Length - 2); continue; }
                var m = ReIniKey.Match(raw);
                if (m.Success)
                {
                    _entries.Add(new ConfigEntry { Section = section, Key = m.Groups[1].Value.Trim(), Value = m.Groups[2].Value.TrimEnd('\r'), LineIndex = i });
                }
            }
        }

        /// <summary>Changes an entry's value WITHIN the original line (preserves the rest: comments, /&gt;...).</summary>
        public void Set(ConfigEntry e, string newValue)
        {
            if (e == null || e.LineIndex < 0 || e.LineIndex >= _lines.Count) { return; }
            string line = _lines[e.LineIndex];
            bool crlf = line.EndsWith("\r");

            if (Format == "propxml")
            {
                var m = Regex.Match(line, "name=\"" + Regex.Escape(e.Key) + "\"\\s+value=\"");
                if (!m.Success) { return; }
                int vstart = m.Index + m.Length;
                int vend = line.IndexOf('"', vstart);
                if (vend < 0) { return; }
                line = line.Substring(0, vstart) + newValue + line.Substring(vend);
            }
            else
            {
                int eq = line.IndexOf('=');
                if (eq < 0) { return; }
                int vs = eq + 1;
                while (vs < line.Length && (line[vs] == ' ' || line[vs] == '\t')) { vs++; }
                line = line.Substring(0, vs) + newValue + (crlf ? "\r" : "");
            }

            _lines[e.LineIndex] = line;
            e.Value = newValue;
        }

        /// <summary>
        /// Edits the key if it exists (in the given section if specified), otherwise INSERTS it under the
        /// section header (created if absent). ini format only. Re-parses to refresh the line indexes.
        /// </summary>
        public void SetOrAdd(string section, string key, string value)
        {
            var existing = _entries.FirstOrDefault(e =>
                e.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(section) || e.Section.Equals(section, StringComparison.OrdinalIgnoreCase)));
            if (existing != null) { Set(existing, value); return; }

            string line = key + "=" + value;
            if (string.IsNullOrEmpty(section))
            {
                _lines.Add(line);
                ParseLines();
                return;
            }

            int header = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase)) { header = i; break; }
            }
            if (header < 0)
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) { _lines.Add(""); }
                _lines.Add("[" + section + "]");
                _lines.Add(line);
            }
            else
            {
                _lines.Insert(header + 1, line);
            }
            ParseLines();
        }

        /// <summary>Save: timestamped backup (.wgsmbak) then faithful rewrite.</summary>
        public void Save()
        {
            try
            {
                string bak = Path + ".wgsmbak";
                File.Copy(Path, bak, true);
            }
            catch { /* best-effort backup */ }
            File.WriteAllText(Path, string.Join("\n", _lines));
        }
    }
}
