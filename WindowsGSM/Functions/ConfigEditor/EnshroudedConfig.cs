using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Enshrouded model: <c>enshrouded_server.json</c>. Exposes the top-level scalars, every
    /// <c>gameSettings</c> value as "gameSettings.&lt;key&gt;" and every user group setting as
    /// "userGroups.&lt;group name&gt;.&lt;key&gt;". Everything else (tags, bannedAccounts...) is kept as is;
    /// the game rewrites this file itself, so re-serialising through JObject is safe.
    ///
    /// Two conveniences over the raw file:
    ///  - durations are stored in nanoseconds; they are shown and edited in minutes;
    ///  - the keys WindowsGSM writes at every start (name, ip, queryPort, slotCount) and the folders are
    ///    hidden, since an edit here would be overwritten by the server's WindowsGSM settings anyway.
    /// </summary>
    public class EnshroudedConfig : IConfigModel
    {
        public string Path { get; private set; }
        public string Format => "enshroudedjson";

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private JObject _root;
        private System.Text.Encoding _enc;

        private const string Settings = "gameSettings";
        private const string Groups = "userGroups";
        private const double NsPerMinute = 60_000_000_000d;

        // Owned by WindowsGSM (synced from the server settings at every start) or paths.
        private static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "ip", "queryPort", "gamePort", "slotCount", "saveDirectory", "logDirectory"
        };

        // gameSettings values stored in nanoseconds.
        private static readonly HashSet<string> Nanoseconds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dayTimeDuration", "nightTimeDuration", "fromHungerToStarving"
        };

        public static EnshroudedConfig Load(string path)
        {
            var c = new EnshroudedConfig { Path = path };
            c._root = JObject.Parse(ConfigIO.Read(path, out c._enc));
            c.Parse();
            return c;
        }

        private void Parse()
        {
            _entries.Clear();
            foreach (var prop in _root.Properties())
            {
                if (Hidden.Contains(prop.Name) || !IsScalar(prop.Value)) { continue; }
                Add(string.Empty, prop.Name, Show(prop.Name, prop.Value, false));
            }
            if (_root[Settings] is JObject gs)
            {
                foreach (var prop in gs.Properties().Where(x => IsScalar(x.Value)))
                {
                    Add(Settings, Settings + "." + prop.Name, Show(prop.Name, prop.Value, true));
                }
            }
            if (_root[Groups] is JArray groups)
            {
                foreach (var g in groups.OfType<JObject>())
                {
                    string name = (string)g["name"];
                    if (string.IsNullOrEmpty(name)) { continue; }
                    foreach (var prop in g.Properties().Where(x => x.Name != "name" && IsScalar(x.Value)))
                    {
                        Add(Groups, Groups + "." + name + "." + prop.Name, Show(prop.Name, prop.Value, false));
                    }
                }
            }
        }

        private void Add(string section, string key, string value)
            => _entries.Add(new ConfigEntry { Section = section, Key = key, Value = value, LineIndex = -1 });

        private static bool IsScalar(JToken t) => !(t is JObject) && !(t is JArray);

        private static string Show(string key, JToken v, bool inSettings)
        {
            if (v.Type == JTokenType.Boolean) { return (bool)v ? "true" : "false"; }
            if (inSettings && Nanoseconds.Contains(key) && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
            {
                return ((double)v / NsPerMinute).ToString("0.##", CultureInfo.InvariantCulture);
            }
            if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer)
            {
                return ((double)v).ToString("0.######", CultureInfo.InvariantCulture);
            }
            return v.ToString();
        }

        /// <summary>Finds the JSON property an entry key points to ("a", "gameSettings.a", "userGroups.G.a").</summary>
        private JProperty Locate(string key, out bool nanoseconds)
        {
            nanoseconds = false;
            if (key.StartsWith(Settings + ".", StringComparison.OrdinalIgnoreCase))
            {
                string k = key.Substring(Settings.Length + 1);
                nanoseconds = Nanoseconds.Contains(k);
                return (_root[Settings] as JObject)?.Property(k, StringComparison.OrdinalIgnoreCase);
            }
            if (key.StartsWith(Groups + ".", StringComparison.OrdinalIgnoreCase))
            {
                string rest = key.Substring(Groups.Length + 1);
                int dot = rest.LastIndexOf('.');
                if (dot <= 0) { return null; }
                string group = rest.Substring(0, dot), field = rest.Substring(dot + 1);
                var g = (_root[Groups] as JArray)?.OfType<JObject>()
                    .FirstOrDefault(x => string.Equals((string)x["name"], group, StringComparison.OrdinalIgnoreCase));
                return g?.Property(field, StringComparison.OrdinalIgnoreCase);
            }
            return _root.Property(key, StringComparison.OrdinalIgnoreCase);
        }

        public void Set(ConfigEntry entry, string newValue)
        {
            if (entry == null) { return; }
            var prop = Locate(entry.Key, out bool ns);
            if (prop == null) { return; }
            newValue = (newValue ?? string.Empty).Trim();
            bool num = double.TryParse(newValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double d);

            // Re-type against the CURRENT token type so the JSON keeps proper types.
            switch (prop.Value.Type)
            {
                case JTokenType.Boolean:
                    prop.Value = string.Equals(newValue, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case JTokenType.Integer:
                    if (!num) { return; }
                    // The game writes whole factors as integers ("1"): keep an integer when the new value
                    // is whole, but never round a decimal one (1.5 must not silently become 2).
                    if (ns) { prop.Value = (long)Math.Round(d * NsPerMinute); }
                    else if (d == Math.Floor(d)) { prop.Value = (long)d; }
                    else { prop.Value = d; }
                    break;
                case JTokenType.Float:
                    if (!num) { return; }
                    prop.Value = ns ? Math.Round(d * NsPerMinute) : d;
                    break;
                default:
                    prop.Value = newValue;
                    break;
            }
            entry.Value = Show(prop.Name, prop.Value, ns);
        }

        public void Save()
        {
            try { System.IO.File.Copy(Path, Path + ".wgsmbak", true); } catch { /* best-effort */ }
            ConfigIO.Write(Path, _root.ToString(Formatting.Indented), _enc);
        }
    }
}
