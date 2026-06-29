using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WindowsGSM.Functions;

namespace WindowsGSM.DiscordBot
{
    static class Configs
    {
		private static readonly string _botPath = ServerPath.Get(ServerPath.FolderName.Configs, "discordbot");

		// Chiffrement au repos du token (DPAPI, lie au compte Windows courant).
		// Prefixe pour distinguer un token chiffre d'un ancien token en clair (retro-compat).
		private const string TokenEncPrefix = "enc:v1:";
		private static readonly byte[] _tokenEntropy = Encoding.UTF8.GetBytes("WindowsGSM.DiscordBot.Token");

		private static string ProtectToken(string plain)
		{
			byte[] data = Encoding.UTF8.GetBytes(plain);
			byte[] enc = ProtectedData.Protect(data, _tokenEntropy, DataProtectionScope.CurrentUser);
			return TokenEncPrefix + Convert.ToBase64String(enc);
		}

		private static string UnprotectToken(string stored)
		{
			byte[] enc = Convert.FromBase64String(stored.Substring(TokenEncPrefix.Length));
			byte[] data = ProtectedData.Unprotect(enc, _tokenEntropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(data);
		}

		public static void CreateConfigs()
		{
			Directory.CreateDirectory(_botPath);
		}

		public static string GetCommandsList()
		{
			string prefix = GetBotPrefix();
			return $"{prefix}wgsm check\n{prefix}wgsm list\n{prefix}wgsm players [<SERVERID>]\n{prefix}wgsm info <SERVERID>\n{prefix}wgsm start <SERVERID>\n{prefix}wgsm stop <SERVERID>\n{prefix}wgsm restart <SERVERID>\n{prefix}wgsm kill <SERVERID>\n{prefix}wgsm update <SERVERID>\n{prefix}wgsm send <SERVERID> <COMMAND>\n{prefix}wgsm backup <SERVERID>\n{prefix}wgsm stats";
		}

		public static string GetBotPrefix()
		{
			try
			{
				return File.ReadAllText(Path.Combine(_botPath, "prefix.txt")).Trim();
			}
			catch
			{
				return string.Empty;
			}
		}

		public static void SetBotPrefix(string prefix)
		{
			Directory.CreateDirectory(_botPath);
			File.WriteAllText(Path.Combine(_botPath, "prefix.txt"), prefix);
		}

		public static string GetBotToken()
		{
			try
			{
				string raw = File.ReadAllText(Path.Combine(_botPath, "token.txt")).Trim();
				if (raw.Length == 0) { return string.Empty; }

				if (raw.StartsWith(TokenEncPrefix))
				{
					// Token chiffre : ne se dechiffre que sous le compte Windows qui l'a ecrit.
					try { return UnprotectToken(raw); }
					catch { return string.Empty; } // chiffre par un autre compte / corrompu -> forcer une nouvelle saisie
				}

				// Ancien format en clair : on le renvoie ET on migre le fichier vers DPAPI de maniere transparente.
				try { SetBotToken(raw); } catch { }
				return raw;
			}
			catch
			{
				return string.Empty;
			}
		}

		public static void SetBotToken(string token)
		{
			Directory.CreateDirectory(_botPath);
			string t = token.Trim();
			string toWrite = t.Length == 0 ? string.Empty : ProtectToken(t);
			File.WriteAllText(Path.Combine(_botPath, "token.txt"), toWrite);
		}

		public static string GetDashboardChannel()
		{
			try
			{
				return File.ReadAllText(Path.Combine(_botPath, "channel.txt")).Trim();
			}
			catch
			{
				return string.Empty;
			}
		}

		public static void SetDashboardChannel(string channel)
		{
			Directory.CreateDirectory(_botPath);
			File.WriteAllText(Path.Combine(_botPath, "channel.txt"), channel.Trim());
		}

		// Plusieurs canaux possibles (un par serveur Discord/guild) : IDs séparés par virgule,
		// point-virgule, espace ou retour-ligne. Tolère l'ancien format à un seul ID.
		public static List<string> GetDashboardChannels()
		{
			try
			{
				string raw = File.ReadAllText(Path.Combine(_botPath, "channel.txt"));
				return raw.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)
						  .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
			}
			catch
			{
				return new List<string>();
			}
		}

		// ===== Canal « panneau d'administration » (embed interactif permanent) =====
		// Fichier séparé du dashboard : un ou plusieurs IDs de canaux dédiés à l'administration.
		public static string GetAdminPanelChannel()
		{
			try
			{
				return File.ReadAllText(Path.Combine(_botPath, "adminpanel.txt")).Trim();
			}
			catch
			{
				return string.Empty;
			}
		}

		public static void SetAdminPanelChannel(string channel)
		{
			Directory.CreateDirectory(_botPath);
			File.WriteAllText(Path.Combine(_botPath, "adminpanel.txt"), channel.Trim());
		}

		public static List<string> GetAdminPanelChannels()
		{
			try
			{
				string raw = File.ReadAllText(Path.Combine(_botPath, "adminpanel.txt"));
				return raw.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)
						  .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
			}
			catch
			{
				return new List<string>();
			}
		}

		public static int GetDashboardRefreshRate()
		{
			try
			{
				return int.Parse(File.ReadAllText(Path.Combine(_botPath, "refreshrate.txt")).Trim());
			}
			catch
			{
				return 60;
			}
		}

		public static void SetDashboardRefreshRate(int rate)
		{
			Directory.CreateDirectory(_botPath);
			File.WriteAllText(Path.Combine(_botPath, "refreshrate.txt"), rate.ToString());
		}

		public static List<string> GetBotAdminIds()
		{
			try
			{
				var adminIds = new List<string>();
				var lines = File.ReadAllLines(Path.Combine(_botPath, "adminIDs.txt"));
				foreach (var line in lines)
				{
					string[] items = line.Split(new char[] { ' ' }, 2);
					adminIds.Add(items[0]);
				}
				return adminIds;
			}
			catch
			{
				return new List<string>();
			}
		}

		public static List<string> GetServerIdsByAdminId(string adminId)
		{
			try
			{
				var lines = File.ReadAllLines(Path.Combine(_botPath, "adminIDs.txt"));
				foreach (var line in lines)
				{
					string[] items = line.Split(new[] { ' ' }, 2);
					if (items[0] == adminId)
					{
						return items[1].Trim().Split(',').Select(s => s.Trim()).ToList();
					}
				}

				return new List<string>();
			}
			catch
			{
				return new List<string>();
			}
		}

		public static List<(string, string)> GetBotAdminList()
		{
			try
			{
				var adminList = new List<(string, string)>();
				var lines = File.ReadAllLines(Path.Combine(_botPath, "adminIDs.txt"));
				foreach (var line in lines)
				{
					string[] items = line.Split(new[] { ' ' }, 2);
					adminList.Add((items[0], items.Length == 1 ? string.Empty : items[1]));
				}
				return adminList;
			}
			catch
			{
				return new List<(string, string)>();
			}
		}

		public static void SetBotAdminList(List<(string, string)> adminList)
		{
			Directory.CreateDirectory(_botPath);

			List<string> lines = new List<string>();
			foreach ((string adminID, string serverIDs) in adminList)
			{
				lines.Add($"{adminID} {serverIDs}");
			}
			File.WriteAllText(Path.Combine(_botPath, "adminIDs.txt"), string.Join("\n", lines.ToArray()));
		}
	}
}
