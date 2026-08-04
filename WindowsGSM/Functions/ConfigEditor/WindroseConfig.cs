using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Windrose model: <c>R5\ServerDescription.json</c>. The editable settings live in the nested
    /// "ServerDescription_Persistent" object; everything else (Version, DeploymentId,
    /// PersistentServerId...) is preserved untouched. The game itself rewrites this file, so
    /// re-serialising through JObject is safe. Values are exposed unquoted (plain strings/numbers).
    /// </summary>
    public class WindroseConfig : IConfigModel
    {
        public string Path { get; private set; }
        public string Format => "windrosejson";

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private JObject _root;
        private JObject _persistent;
        private System.Text.Encoding _enc;

        // Managed by the server / not meant to be edited (doc: "Do not edit").
        private static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PersistentServerId"
        };

        public static WindroseConfig Load(string path)
        {
            var c = new WindroseConfig { Path = path };
            c._root = JObject.Parse(ConfigIO.Read(path, out c._enc));
            c._persistent = c._root["ServerDescription_Persistent"] as JObject;
            c.Parse();
            return c;
        }

        private void Parse()
        {
            _entries.Clear();
            if (_persistent == null) { return; }
            foreach (var prop in _persistent.Properties())
            {
                if (Hidden.Contains(prop.Name)) { continue; }
                if (prop.Value is JArray || prop.Value is JObject) { continue; } // scalars only
                _entries.Add(new ConfigEntry
                {
                    Section = "ServerDescription_Persistent",
                    Key = prop.Name,
                    Value = prop.Value.Type == JTokenType.Boolean ? ((bool)prop.Value ? "true" : "false") : prop.Value.ToString(),
                    LineIndex = -1
                });
            }
        }

        public void Set(ConfigEntry entry, string newValue)
        {
            if (entry == null || _persistent == null) { return; }
            var prop = _persistent.Property(entry.Key, StringComparison.OrdinalIgnoreCase);
            if (prop == null) { return; }
            // Re-type against the CURRENT token type so the JSON keeps proper types.
            switch (prop.Value.Type)
            {
                case JTokenType.Boolean:
                    prop.Value = string.Equals(newValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case JTokenType.Integer:
                    prop.Value = long.TryParse(newValue, out long l) ? l : 0L;
                    break;
                case JTokenType.Float:
                    prop.Value = double.TryParse(newValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dd) ? dd : 0d;
                    break;
                default:
                    prop.Value = newValue ?? string.Empty;
                    break;
            }
            entry.Value = newValue;
        }

        public void Save()
        {
            try { System.IO.File.Copy(Path, Path + ".wgsmbak", true); } catch { /* best-effort */ }
            ConfigIO.Write(Path, _root.ToString(Formatting.Indented), _enc);
        }
    }
}
