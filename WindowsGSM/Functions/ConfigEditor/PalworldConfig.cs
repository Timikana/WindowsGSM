using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Palworld-specific model: PalWorldSettings.ini packs ALL settings into a single inline value
    /// "OptionSettings=(Key=Val,Key=Val,...)". This model breaks that value down into individual
    /// key/value entries and rewrites IN PLACE (preserves unmanaged keys, order, quotes and
    /// sub-lists like CrossplayPlatforms=(Steam,Xbox,...)).
    /// </summary>
    public class PalworldConfig : IConfigModel
    {
        public string Path { get; private set; }
        public string Format => "palworld";

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private string[] _allLines;     // all lines of the .ini
        private int _optLineIndex = -1; // index of the "OptionSettings=..." line
        private string _prefix;         // "OptionSettings=(" (before the content)
        private string _suffix;         // ")<\r?>" (after the content)
        private string _inner;          // content between the parentheses (mutable)

        public static PalworldConfig Load(string path)
        {
            var cf = new PalworldConfig { Path = path };
            cf._allLines = File.ReadAllText(path).Split('\n');
            cf.LocateAndParse();
            return cf;
        }

        private void LocateAndParse()
        {
            _entries.Clear();
            for (int i = 0; i < _allLines.Length; i++)
            {
                string raw = _allLines[i];
                int eq = raw.IndexOf('=');
                if (eq < 0) { continue; }
                if (!raw.Substring(0, eq).Trim().Equals("OptionSettings", StringComparison.OrdinalIgnoreCase)) { continue; }

                int open = raw.IndexOf('(', eq);
                int close = raw.LastIndexOf(')');
                if (open < 0 || close <= open) { continue; }

                _optLineIndex = i;
                _prefix = raw.Substring(0, open + 1);
                _inner = raw.Substring(open + 1, close - open - 1);
                _suffix = raw.Substring(close); // ")" + optional "\r"
                break;
            }
            if (_optLineIndex < 0) { return; }

            foreach (var tok in TopLevelTokens(_inner))
            {
                int e = _inner.IndexOf('=', tok.Start);
                if (e < 0 || e >= tok.End) { continue; }
                string key = _inner.Substring(tok.Start, e - tok.Start).Trim();
                string val = _inner.Substring(e + 1, tok.End - (e + 1));
                _entries.Add(new ConfigEntry { Section = "", Key = key, Value = val, LineIndex = _optLineIndex });
            }
        }

        private struct Range { public int Start; public int End; public Range(int s, int e) { Start = s; End = e; } }

        /// <summary>Splits the inline content into top-level tokens (commas outside quotes/parentheses).</summary>
        private static IEnumerable<Range> TopLevelTokens(string s)
        {
            int depth = 0; bool inq = false; int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') { inq = !inq; }
                else if (!inq && c == '(') { depth++; }
                else if (!inq && c == ')') { if (depth > 0) { depth--; } }
                else if (!inq && depth == 0 && c == ',')
                {
                    yield return new Range(start, i);
                    start = i + 1;
                }
            }
            if (start <= s.Length) { yield return new Range(start, s.Length); }
        }

        /// <summary>Finds the span of a key's value within _inner (after the "=").</summary>
        private bool FindValueSpan(string key, out int valStart, out int valLen)
        {
            valStart = 0; valLen = 0;
            foreach (var tok in TopLevelTokens(_inner))
            {
                int e = _inner.IndexOf('=', tok.Start);
                if (e < 0 || e >= tok.End) { continue; }
                string k = _inner.Substring(tok.Start, e - tok.Start).Trim();
                if (k.Equals(key, StringComparison.Ordinal))
                {
                    valStart = e + 1;
                    valLen = tok.End - (e + 1);
                    return true;
                }
            }
            return false;
        }

        public void Set(ConfigEntry entry, string newValue)
        {
            if (entry == null || _optLineIndex < 0) { return; }
            if (!FindValueSpan(entry.Key, out int vs, out int vl)) { return; }
            _inner = _inner.Substring(0, vs) + newValue + _inner.Substring(vs + vl);
            entry.Value = newValue;
        }

        public void Save()
        {
            if (_optLineIndex < 0) { return; }
            try { File.Copy(Path, Path + ".wgsmbak", true); }
            catch { /* best-effort backup */ }

            _allLines[_optLineIndex] = _prefix + _inner + _suffix;
            File.WriteAllText(Path, string.Join("\n", _allLines));
        }
    }
}
