using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Project Zomboid <c>SandboxVars.lua</c> model. Parses the Lua table
    /// <c>SandboxVars = { Key = Value, ... }</c> including nested sub-tables (Map, ZombieLore,
    /// ZombieConfig, Plumbing), keying nested entries as "Table.Key". Rewrites values IN PLACE,
    /// preserving indentation, trailing commas and the game's comments. Line-based like ConfigFile.
    /// </summary>
    public class ZomboidSandboxConfig : IConfigModel
    {
        public string Path { get; private set; }
        public string Format => "zomboidsandbox";

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private string[] _lines;

        // group1=indent, 2=key, 3=value (non-greedy), 4=optional comma, 5=optional trailing comment
        private static readonly Regex Assign = new Regex(@"^(\s*)([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*(,?)\s*(--.*)?$");

        public static ZomboidSandboxConfig Load(string path)
        {
            var c = new ZomboidSandboxConfig { Path = path };
            c._lines = File.ReadAllText(path).Split('\n');
            c.Parse();
            return c;
        }

        private void Parse()
        {
            _entries.Clear();
            var stack = new List<string>(); // nested table names (root excluded)
            int depth = 0;
            for (int i = 0; i < _lines.Length; i++)
            {
                string t = _lines[i].TrimEnd('\r').Trim();
                if (t.Length == 0 || t.StartsWith("--")) { continue; }
                if (t.StartsWith("}"))
                {
                    if (depth > 0) { depth--; if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); } }
                    continue;
                }
                var m = Assign.Match(_lines[i].TrimEnd('\r'));
                if (!m.Success) { continue; }
                string key = m.Groups[2].Value;
                string val = m.Groups[3].Value;
                if (val == "{") // table opening "KEY = {"
                {
                    if (depth == 0) { depth = 1; }          // root SandboxVars -> no prefix
                    else { stack.Add(key); depth++; }
                    continue;
                }
                string section = string.Join(".", stack);
                string fullKey = string.IsNullOrEmpty(section) ? key : section + "." + key;
                _entries.Add(new ConfigEntry { Section = section, Key = fullKey, Value = val, LineIndex = i });
            }
        }

        public void Set(ConfigEntry entry, string newValue)
        {
            if (entry == null || entry.LineIndex < 0 || entry.LineIndex >= _lines.Length) { return; }
            string raw = _lines[entry.LineIndex];
            string cr = raw.EndsWith("\r") ? "\r" : string.Empty;
            var m = Assign.Match(raw.TrimEnd('\r'));
            if (!m.Success) { return; }
            string indent = m.Groups[1].Value;
            string key = m.Groups[2].Value;
            string comma = m.Groups[4].Value;
            string comment = m.Groups[5].Value;
            string rebuilt = indent + key + " = " + newValue + comma;
            if (!string.IsNullOrEmpty(comment)) { rebuilt += " " + comment; }
            _lines[entry.LineIndex] = rebuilt + cr;
            entry.Value = newValue;
        }

        public void Save()
        {
            try { File.Copy(Path, Path + ".wgsmbak", true); } catch { /* best-effort */ }
            File.WriteAllText(Path, string.Join("\n", _lines));
        }
    }
}
