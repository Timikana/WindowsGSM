using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Windrose per-world gameplay settings (WorldDescription.json), as documented in the
    /// DedicatedServer.md shipped with the server.
    ///
    /// Three things make this file unlike any other config we edit:
    ///  - the parameter keys are JSON strings that themselves contain JSON, e.g.
    ///    "{\"TagName\": \"WDS.Parameter.MobHealthMultiplier\"}", grouped under BoolParameters /
    ///    FloatParameters / TagParameters. We expose them under their friendly suffix and rebuild the
    ///    exact key on save;
    ///  - WorldSettings must be EMPTY for every preset except "Custom", so touching any parameter
    ///    switches WorldPresetType to Custom (otherwise the game ignores the values);
    ///  - the game does not read the file directly: R5WorldDescriptionUpdater.exe, shipped in the
    ///    server root, has to ingest it, and only while the server is stopped.
    /// </summary>
    public class WindroseWorldConfig : IConfigModel
    {
        public string Path { get; private set; }
        public string Format => "windroseworld";

        private readonly List<ConfigEntry> _entries = new List<ConfigEntry>();
        public IReadOnlyList<ConfigEntry> Entries => _entries;

        private JObject _root;
        private JObject _world;

        private const string TagPrefix = "WDS.Parameter.";
        private const string PresetKey = "WorldPresetType";
        private const string NameKey = "WorldName";

        // suffix -> group holding it in WorldSettings
        private static readonly Dictionary<string, string> _paramGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Coop.SharedQuests", "BoolParameters" },
            { "EasyExplore", "BoolParameters" },
            { "MobHealthMultiplier", "FloatParameters" },
            { "MobDamageMultiplier", "FloatParameters" },
            { "ShipsHealthMultiplier", "FloatParameters" },
            { "ShipsDamageMultiplier", "FloatParameters" },
            { "BoardingDifficultyMultiplier", "FloatParameters" },
            { "Coop.StatsCorrectionModifier", "FloatParameters" },
            { "Coop.ShipStatsCorrectionModifier", "FloatParameters" },
            { "CombatDifficulty", "TagParameters" },
        };

        /// <summary>Rebuilds the escaped key the file uses for a parameter suffix.</summary>
        private static string FullKey(string suffix) => "{\"TagName\": \"" + TagPrefix + suffix + "\"}";

        /// <summary>Reads the suffix back out of such a key ("" when it does not look like one).</summary>
        private static string SuffixOf(string fullKey)
        {
            int i = (fullKey ?? string.Empty).IndexOf(TagPrefix, StringComparison.Ordinal);
            if (i < 0) { return string.Empty; }
            string rest = fullKey.Substring(i + TagPrefix.Length);
            int end = rest.IndexOf('"');
            return end > 0 ? rest.Substring(0, end) : rest.Trim('}', '"', ' ');
        }

        public static WindroseWorldConfig Load(string path)
        {
            var c = new WindroseWorldConfig { Path = path };
            c._root = JObject.Parse(File.ReadAllText(path));
            // The file wraps everything in "WorldDescription"; tolerate a flat file too.
            c._world = c._root["WorldDescription"] as JObject ?? c._root;

            c._entries.Add(new ConfigEntry { Section = string.Empty, Key = NameKey, Value = (string)c._world[NameKey] ?? string.Empty, LineIndex = -1 });
            c._entries.Add(new ConfigEntry { Section = string.Empty, Key = PresetKey, Value = (string)c._world[PresetKey] ?? "Medium", LineIndex = -1 });

            var settings = c._world["WorldSettings"] as JObject;
            foreach (var kv in _paramGroup)
            {
                string suffix = kv.Key;
                string value = string.Empty;
                var group = settings?[kv.Value] as JObject;
                if (group != null)
                {
                    var prop = group.Properties().FirstOrDefault(p => string.Equals(SuffixOf(p.Name), suffix, StringComparison.OrdinalIgnoreCase));
                    if (prop != null) { value = Stringify(prop.Value); }
                }
                c._entries.Add(new ConfigEntry { Section = "World", Key = suffix, Value = value, LineIndex = -1 });
            }
            return c;
        }

        /// <summary>CombatDifficulty is itself a tag object; everything else is a plain scalar.</summary>
        private static string Stringify(JToken token)
        {
            if (token is JObject obj)
            {
                string tag = (string)obj["TagName"] ?? string.Empty;
                int dot = tag.LastIndexOf('.');
                return dot >= 0 ? tag.Substring(dot + 1) : tag;   // "...CombatDifficulty.Normal" -> "Normal"
            }
            if (token?.Type == JTokenType.Boolean) { return ((bool)token) ? "true" : "false"; }
            // Numbers must render with a dot (invariant): they are written back through float parsing,
            // and a culture comma ("2,5") would be read as thousands and become 25.
            if (token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer))
            {
                return ((double)token).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return token?.ToString() ?? string.Empty;
        }

        public void Set(ConfigEntry entry, string newValue)
        {
            if (entry == null) { return; }
            newValue = (newValue ?? string.Empty).Trim();

            if (entry.Key == NameKey || entry.Key == PresetKey)
            {
                _world[entry.Key] = newValue;
                entry.Value = newValue;
                return;
            }
            if (!_paramGroup.TryGetValue(entry.Key, out string groupName)) { return; }

            var settings = _world["WorldSettings"] as JObject;
            if (settings == null) { settings = new JObject(); _world["WorldSettings"] = settings; }
            var group = settings[groupName] as JObject;
            if (group == null) { group = new JObject(); settings[groupName] = group; }

            JToken token;
            if (groupName == "BoolParameters")
            {
                token = string.Equals(newValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            else if (groupName == "TagParameters")
            {
                token = new JObject { ["TagName"] = TagPrefix + entry.Key + "." + newValue };
            }
            else
            {
                if (!double.TryParse(newValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d)) { return; }
                token = d;
            }

            // Replace by suffix (the stored key may be spaced differently than the one we build).
            var existing = group.Properties().FirstOrDefault(p => string.Equals(SuffixOf(p.Name), entry.Key, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { existing.Value = token; } else { group[FullKey(entry.Key)] = token; }

            // Values only count under the Custom preset (DedicatedServer.md).
            _world[PresetKey] = "Custom";
            var presetEntry = _entries.FirstOrDefault(e => e.Key == PresetKey);
            if (presetEntry != null) { presetEntry.Value = "Custom"; }

            entry.Value = newValue;
        }

        public void Save()
        {
            File.Copy(Path, Path + ".wgsmbak", true);
            File.WriteAllText(Path, _root.ToString(Formatting.Indented));
            RunUpdater();
        }

        /// <summary>
        /// Hands the file to R5WorldDescriptionUpdater.exe, the only thing the game reads it through.
        /// Best-effort and short-lived: a missing or failing updater must not lose the edit that was
        /// just written to disk — the admin can still run it by hand, as the shipped doc describes.
        /// </summary>
        private void RunUpdater()
        {
            try
            {
                // <root>\R5\Saved\SaveProfiles\...\WorldDescription.json -> climb back to <root>
                var dir = new DirectoryInfo(System.IO.Path.GetDirectoryName(Path));
                while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "R5WorldDescriptionUpdater.exe")))
                {
                    dir = dir.Parent;
                }
                if (dir == null)
                {
                    AppLog.Warn("Windrose/World", "R5WorldDescriptionUpdater.exe not found above the world file; run it by hand to apply.");
                    return;
                }

                string exe = System.IO.Path.Combine(dir.FullName, "R5WorldDescriptionUpdater.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "\"" + Path + "\"",
                    WorkingDirectory = dir.FullName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        AppLog.Warn("Windrose/World", "R5WorldDescriptionUpdater timed out (60s), killed.");
                        return;
                    }
                    AppLog.Info("Windrose/World", $"R5WorldDescriptionUpdater exit {p.ExitCode}. {output.Replace("\r", " ").Replace("\n", " ").Trim()}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Windrose/World", "updater failed: " + ex.Message);
            }
        }
    }
}
