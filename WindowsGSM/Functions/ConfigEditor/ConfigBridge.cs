using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Thread-safe, UI-free bridge over the curated config schemas so the Discord bot can list and edit
    /// server settings (guided menu + /config command). Reuses GameSchemas + the French overlay + the same
    /// value formatting as the desktop editor. Secrets (passwords) are never exposed or editable here.
    /// </summary>
    public static class ConfigBridge
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class Field
        {
            public string Key;
            public string Label;      // French-aware
            public string Group;      // French-aware
            public string Desc;       // French-aware
            public string Current;    // unquoted current value
            public FieldKind Kind;
            public double Min, Max, Step;
            public bool HasRange;
            public string[] EnumValues;
        }

        private static (GameConfigSchema schema, IConfigModel model) Open(string game, string serverFiles)
        {
            if (string.IsNullOrEmpty(game)) { return (null, null); }
            foreach (var schema in GameSchemas.All(game))
            {
                foreach (var rel in schema.RelativePaths ?? Array.Empty<string>())
                {
                    string full = Path.Combine(serverFiles ?? string.Empty, rel);
                    if (!File.Exists(full)) { continue; }
                    try
                    {
                        IConfigModel m = schema.Model == "palworld"
                            ? (IConfigModel)PalworldConfig.Load(full)
                            : ConfigFile.Load(full);
                        return (schema, m);
                    }
                    catch { return (null, null); }
                }
            }
            return (null, null);
        }

        /// <summary>True if this server has a curated, editable config.</summary>
        public static bool Supported(string game, string serverFiles) => Open(game, serverFiles).schema != null;

        // Secrets are intentionally not editable over Discord (no password ever shown in a channel).
        private static bool Editable(FieldSpec f) => f != null && f.Kind != FieldKind.Secret;

        /// <summary>Distinct group names (French-aware) that actually have editable fields present in the file.</summary>
        public static List<string> Groups(string game, string serverFiles)
        {
            var (schema, model) = Open(game, serverFiles);
            var res = new List<string>();
            if (schema == null) { return res; }
            foreach (var f in schema.Fields)
            {
                if (!Editable(f)) { continue; }
                if (!model.Entries.Any(e => string.Equals(e.Key, f.Key, StringComparison.OrdinalIgnoreCase))) { continue; }
                string g = ConfigSchemaFr.Group(schema.GameMatch, f.Group ?? string.Empty);
                if (!res.Contains(g)) { res.Add(g); }
            }
            return res;
        }

        /// <summary>Editable fields (optionally filtered to one French group), in schema order.</summary>
        public static List<Field> Fields(string game, string serverFiles, string groupFr = null)
        {
            var (schema, model) = Open(game, serverFiles);
            var res = new List<Field>();
            if (schema == null) { return res; }
            foreach (var f in schema.Fields)
            {
                if (!Editable(f)) { continue; }
                var entry = model.Entries.FirstOrDefault(e => string.Equals(e.Key, f.Key, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { continue; }
                string gFr = ConfigSchemaFr.Group(schema.GameMatch, f.Group ?? string.Empty);
                if (groupFr != null && !string.Equals(gFr, groupFr, StringComparison.OrdinalIgnoreCase)) { continue; }
                res.Add(new Field
                {
                    Key = f.Key,
                    Label = ConfigSchemaFr.Label(schema.GameMatch, f),
                    Group = gFr,
                    Desc = ConfigSchemaFr.Desc(schema.GameMatch, f) ?? string.Empty,
                    Kind = f.Kind,
                    Min = f.Min,
                    Max = f.Max,
                    Step = f.Step,
                    HasRange = f.Max > f.Min,
                    EnumValues = f.EnumValues,
                    Current = Unquote(entry.Value ?? string.Empty)
                });
            }
            return res;
        }

        public static Field Get(string game, string serverFiles, string key)
            => Fields(game, serverFiles).FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Validate + write one setting, formatting the value exactly like the desktop editor.
        /// Returns (ok, human message). A .wgsmbak backup is written by the models on save.</summary>
        public static (bool ok, string message) Set(string game, string serverFiles, string key, string raw)
        {
            var (schema, model) = Open(game, serverFiles);
            if (schema == null) { return (false, "No editable config for this server."); }
            var spec = schema.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase) && Editable(f));
            if (spec == null) { return (false, "Unknown setting: " + key); }
            var entry = model.Entries.FirstOrDefault(e => string.Equals(e.Key, spec.Key, StringComparison.OrdinalIgnoreCase));
            if (entry == null) { return (false, "Setting not present in the file: " + key); }

            raw = (raw ?? string.Empty).Trim();
            string formatted;
            switch (spec.Kind)
            {
                case FieldKind.Bool:
                    bool? b = ParseBool(raw);
                    if (b == null) { return (false, "Expected a yes/no value (on/off)."); }
                    formatted = b.Value ? (schema.BoolTrue ?? "True") : (schema.BoolFalse ?? "False");
                    break;
                case FieldKind.Int:
                    if (!long.TryParse(raw, NumberStyles.Any, Inv, out long iv)) { return (false, "Expected a whole number."); }
                    if (spec.Max > spec.Min && (iv < (long)spec.Min || iv > (long)spec.Max)) { return (false, $"Out of range ({(long)spec.Min}-{(long)spec.Max})."); }
                    formatted = iv.ToString(Inv);
                    break;
                case FieldKind.Float:
                    if (!double.TryParse(raw, NumberStyles.Any, Inv, out double dv)) { return (false, "Expected a number."); }
                    if (spec.Max > spec.Min && (dv < spec.Min || dv > spec.Max)) { return (false, $"Out of range ({spec.Min}-{spec.Max})."); }
                    formatted = dv.ToString("0.000000", Inv);
                    break;
                case FieldKind.Enum:
                    string match = spec.EnumValues?.FirstOrDefault(x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { return (false, "Allowed values: " + string.Join(", ", spec.EnumValues ?? Array.Empty<string>())); }
                    formatted = match;
                    break;
                default: // Text
                    formatted = schema.Model == "palworld" ? "\"" + raw.Replace("\"", string.Empty) + "\"" : raw;
                    break;
            }

            try { model.Set(entry, formatted); model.Save(); }
            catch (Exception ex) { return (false, "Write error: " + ex.Message); }
            return (true, ConfigSchemaFr.Label(schema.GameMatch, spec) + " = " + Unquote(formatted));
        }

        private static bool? ParseBool(string s)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "true": case "1": case "on": case "yes": case "oui": case "vrai": return true;
                case "false": case "0": case "off": case "no": case "non": case "faux": return false;
                default: return null;
            }
        }

        private static string Unquote(string v)
        {
            v = (v ?? string.Empty).Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') { v = v.Substring(1, v.Length - 2); }
            return v;
        }
    }
}
