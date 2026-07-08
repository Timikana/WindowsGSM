using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    class BackupConfig
    {
        private const int DefaultMaximumBackups = 3;

        static class SettingName
        {
            public const string BackupLocation = "backuplocation";
            public const string MaximumBackups = "maximumbackups";
            public const string BackupFolders = "backupfolders"; // #179
        }

        private readonly string _serverId;
        public string BackupLocation;
        public int MaximumBackups = DefaultMaximumBackups;
        // #179: subfolders (relative to serverfiles) to include, separated by ';'. Empty = all of serverfiles.
        public string BackupFolders = string.Empty;

        public BackupConfig(string serverId)
        {
            _serverId = serverId;
            string configPath = ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg");
            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, $"{SettingName.BackupLocation}=\"{Path.Combine(MainWindow.WGSM_PATH, "Backups", serverId)}\"{Environment.NewLine}{SettingName.MaximumBackups}=\"{DefaultMaximumBackups}\"");
            }

            LoadConfig();
        }

        public void Open()
        {
            Shell.Open(ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg"));
        }

        /// <summary>Persists the location and the maximum number of backups in BackupConfig.cfg.</summary>
        public void Save()
        {
            string configPath = ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg");
            int max = MaximumBackups <= 0 ? 1 : MaximumBackups;
            File.WriteAllText(configPath,
                $"{SettingName.BackupLocation}=\"{BackupLocation}\"{Environment.NewLine}" +
                $"{SettingName.MaximumBackups}=\"{max}\"{Environment.NewLine}" +
                $"{SettingName.BackupFolders}=\"{BackupFolders}\"");
        }

        private void LoadConfig()
        {
            string configPath = ServerPath.GetServersConfigs(_serverId, "BackupConfig.cfg");
            try
            {
                foreach (string line in File.ReadLines(configPath))
                {
                    string[] keyvalue = line.Split(new[] { '=' }, 2);
                    if (keyvalue.Length == 2)
                    {
                        keyvalue[1] = keyvalue[1].Trim('\"');
                        switch (keyvalue[0])
                        {
                            case SettingName.BackupLocation: BackupLocation = keyvalue[1]; break;
                            case SettingName.MaximumBackups: MaximumBackups = int.TryParse(keyvalue[1], out int max) ? ((max <= 0) ? 1 : max) : DefaultMaximumBackups; break;
                            case SettingName.BackupFolders: BackupFolders = keyvalue[1]; break; // #179
                        }
                    }
                }
            }
            catch { /* .cfg locked/corrupted: default values */ }
        }
    }
}
