using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace WindowsGSM.Functions.Localization
{
    /// <summary>
    /// Lightweight app localization for the fork's UI. Key-based lookup with fallback:
    /// current language -> English -> the key itself (so a missing entry is visible, never a crash).
    /// Language is stored in HKCU\SOFTWARE\WindowsGSM\Language ("en"/"fr"/"es"/"de"); first run
    /// auto-detects from the OS UI culture. Strings live in <see cref="LocStrings"/>.
    /// Layout note: consumers must size labels/buttons to content (Auto width / MinWidth / wrapping)
    /// because translated strings vary a lot in length (German is the longest).
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Supported = { "en", "fr", "es", "de" };
        public static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "en", "English" }, { "fr", "Français" }, { "es", "Español" }, { "de", "Deutsch" }
        };

        private const string RegPath = @"SOFTWARE\WindowsGSM";
        private const string RegKey = "Language";

        private static string _lang = LoadLang();

        /// <summary>Active language code ("en"/"fr"/"es"/"de").</summary>
        public static string Lang => _lang;

        private static string LoadLang()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RegPath);
                string v = k?.GetValue(RegKey)?.ToString();
                if (!string.IsNullOrEmpty(v) && Array.IndexOf(Supported, v) >= 0) { return v; }
            }
            catch { }
            // First run: follow the OS UI language if we support it, else English.
            try
            {
                string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (Array.IndexOf(Supported, iso) >= 0) { return iso; }
            }
            catch { }
            return "en";
        }

        /// <summary>Set and persist the active language.</summary>
        public static void SetLang(string lang)
        {
            if (string.IsNullOrEmpty(lang) || Array.IndexOf(Supported, lang) < 0) { return; }
            _lang = lang;
            try { using var k = Registry.CurrentUser.CreateSubKey(RegPath); k.SetValue(RegKey, lang); } catch { }
        }

        /// <summary>Translate a key for the active language (fallback: en, then the key).</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) { return string.Empty; }
            var all = LocStrings.All;
            if (all.TryGetValue(_lang, out var d) && d.TryGetValue(key, out var s)) { return s; }
            if (all.TryGetValue("en", out var e) && e.TryGetValue(key, out var s2)) { return s2; }
            return key;
        }

        /// <summary>Translate + string.Format with args.</summary>
        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }
    }
}
