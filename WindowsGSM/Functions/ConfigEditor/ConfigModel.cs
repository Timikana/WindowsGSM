using System.Collections.Generic;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Common contract for all editable config models (universal INI/propxml, inline Palworld...).
    /// The UI only knows this much: a list of key/value entries + Set + faithful Save.
    /// </summary>
    public interface IConfigModel
    {
        string Path { get; }
        string Format { get; }
        IReadOnlyList<ConfigEntry> Entries { get; }
        void Set(ConfigEntry e, string newValue);
        void Save();
    }

    /// <summary>A key/value entry (shared across all models).</summary>
    public class ConfigEntry
    {
        public string Section;   // "" if no section
        public string Key;
        public string Value;
        public int LineIndex;    // for line-based models (ini/propxml); -1 otherwise
        public string Display => string.IsNullOrEmpty(Section) ? Key : $"[{Section}] {Key}";
    }
}
