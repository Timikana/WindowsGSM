using System;
using System.IO;
using System.Text;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Encoding-preserving text I/O for the config models. Editing an ASCII value (a number, a key)
    /// must never corrupt a file's non-ASCII bytes. Project Zomboid, for instance, writes its .ini and
    /// SandboxVars.lua in the system ANSI code page (Windows-1252), not UTF-8.
    /// Detection: honour a BOM; else accept strict UTF-8; else fall back to Latin-1, which maps every
    /// byte 0-255 to a char and back 1:1, so an unchanged line round-trips byte-for-byte regardless of
    /// the file's real code page. The same encoding is used to write back.
    /// </summary>
    internal static class ConfigIO
    {
        public static string Read(string path, out Encoding enc)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); return new UTF8Encoding(false).GetString(b, 3, b.Length - 3); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = Encoding.Unicode; return enc.GetString(b, 2, b.Length - 2); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return enc.GetString(b, 2, b.Length - 2); }
            try
            {
                var strict = new UTF8Encoding(false, true);
                string t = strict.GetString(b);
                enc = new UTF8Encoding(false); // valid UTF-8, no BOM
                return t;
            }
            catch (Exception)
            {
                enc = Encoding.Latin1; // byte-preserving fallback (covers Windows-1252 and any ANSI)
                return Encoding.Latin1.GetString(b);
            }
        }

        public static void Write(string path, string text, Encoding enc)
        {
            File.WriteAllText(path, text, enc ?? new UTF8Encoding(false));
        }
    }
}
