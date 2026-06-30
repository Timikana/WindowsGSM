using System.Collections.Generic;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Contrat commun à tous les modèles de config éditables (INI/propxml universel, Palworld inline…).
    /// L'UI ne connaît que ça : une liste d'entrées clé/valeur + Set + Save fidèle.
    /// </summary>
    public interface IConfigModel
    {
        string Path { get; }
        string Format { get; }
        IReadOnlyList<ConfigEntry> Entries { get; }
        void Set(ConfigEntry e, string newValue);
        void Save();
    }

    /// <summary>Une entrée clé/valeur (partagée entre tous les modèles).</summary>
    public class ConfigEntry
    {
        public string Section;   // "" si pas de section
        public string Key;
        public string Value;
        public int LineIndex;    // pour les modèles à lignes (ini/propxml) ; -1 sinon
        public string Display => string.IsNullOrEmpty(Section) ? Key : $"[{Section}] {Key}";
    }
}
