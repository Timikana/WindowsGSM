using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>
    /// Management of a server's file/folder mods. Enable/disable = MOVE the item to a
    /// sibling folder "&lt;mods&gt;_disabled" (reversible, and the game loader never sees it — works
    /// for .jar (Minecraft), .dll (Valheim), .cs (Rust) as well as for subfolders (7DtD, SML)).
    /// </summary>
    public static class ModFolder
    {
        public class ModItem
        {
            public string Name;          // file/folder name
            public string FullPath;      // current location
            public bool IsDirectory;
            public bool Enabled;
            public long SizeBytes;       // 0 for a folder (not computed)
        }

        public static string DisabledDir(string modDir) => modDir.TrimEnd('\\', '/') + "_disabled";

        public static List<ModItem> List(string serverFiles, ModProfile profile)
        {
            var res = new List<ModItem>();
            if (profile == null || string.IsNullOrEmpty(profile.ModFolderRelative)) { return res; }
            string modDir = Path.Combine(serverFiles ?? "", profile.ModFolderRelative);
            Collect(modDir, profile, true, res);
            Collect(DisabledDir(modDir), profile, false, res);
            res.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return res;
        }

        private static void Collect(string dir, ModProfile profile, bool enabled, List<ModItem> res)
        {
            if (!Directory.Exists(dir)) { return; }
            try
            {
                if (profile.FolderEntries)
                {
                    foreach (string d in Directory.EnumerateDirectories(dir))
                    {
                        res.Add(new ModItem { Name = Path.GetFileName(d), FullPath = d, IsDirectory = true, Enabled = enabled });
                    }
                }
                else
                {
                    foreach (string f in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(f);
                        if (profile.Extensions != null && profile.Extensions.Length > 0 &&
                            !profile.Extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                        { continue; }
                        long size = 0;
                        try { size = new FileInfo(f).Length; } catch { }
                        res.Add(new ModItem { Name = Path.GetFileName(f), FullPath = f, IsDirectory = false, Enabled = enabled, SizeBytes = size });
                    }
                }
            }
            catch { /* unreadable folder -> ignore */ }
        }

        /// <summary>Toggles enabled/disabled by moving the item. Returns the new path.</summary>
        public static string Toggle(string serverFiles, ModProfile profile, ModItem item)
        {
            string modDir = Path.Combine(serverFiles ?? "", profile.ModFolderRelative);
            string disabledDir = DisabledDir(modDir);
            string target = item.Enabled ? Path.Combine(disabledDir, item.Name) : Path.Combine(modDir, item.Name);
            Directory.CreateDirectory(item.Enabled ? disabledDir : modDir);
            if (item.IsDirectory) { Directory.Move(item.FullPath, target); }
            else { File.Move(item.FullPath, target, true); }
            item.FullPath = target;
            item.Enabled = !item.Enabled;
            return target;
        }

        /// <summary>Adds a file mod (copies into the mods folder).</summary>
        public static void AddFile(string serverFiles, ModProfile profile, string sourceFile)
        {
            string modDir = Path.Combine(serverFiles ?? "", profile.ModFolderRelative);
            Directory.CreateDirectory(modDir);
            File.Copy(sourceFile, Path.Combine(modDir, Path.GetFileName(sourceFile)), true);
        }

        public static string ModDirPath(string serverFiles, ModProfile profile)
            => Path.Combine(serverFiles ?? "", profile?.ModFolderRelative ?? "");
    }
}
