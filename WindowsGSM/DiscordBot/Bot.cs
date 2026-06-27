using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace WindowsGSM.DiscordBot
{
	class Bot
	{
		private DiscordSocketClient _client;
		private string _donorType;
		private SocketTextChannel _dashboardTextChannel;
		private RestUserMessage _dashboardMessage;
		private CancellationTokenSource _cancellationTokenSource;

		public Bot()
		{
			Configs.CreateConfigs();
		}

		public async Task<bool> Start()
		{
			string token = Configs.GetBotToken();
			if (string.IsNullOrWhiteSpace(token))
			{
				BotLog("Échec démarrage : aucun token configuré.");
				return false;
			}

			// 1) Tentative complète : avec l'intent privilégié Message Content (commandes préfixe + slash).
			if (await TryStart(token, withMessageContent: true)) { return true; }

			// 2) Repli automatique SANS l'intent privilégié : si Discord a fermé la connexion parce que
			//    « Message Content » n'est pas activé, on se reconnecte en mode slash uniquement.
			BotLog("Nouvelle tentative sans l'intent privilégié (mode slash uniquement)…");
			if (await TryStart(token, withMessageContent: false))
			{
				BotLog("Connecté en MODE SLASH UNIQUEMENT. Les commandes /wgsm fonctionnent. Pour les commandes préfixe (!wgsm), active « Message Content Intent » dans le portail Discord puis relance le bot.");
				return true;
			}

			BotLog("Échec : impossible de se connecter (token invalide/401, ou réseau). Vérifie le Bot Token.");
			return false;
		}

		// Tente une connexion avec un jeu d'intents donné. Renvoie true seulement si la connexion aboutit.
		private async Task<bool> TryStart(string token, bool withMessageContent)
		{
			// Nettoie une éventuelle instance précédente (cas du repli).
			try { if (_client != null) { await _client.StopAsync(); _client.Dispose(); _client = null; } } catch { /* ignore */ }

			BotLog(withMessageContent
				? "Connexion au gateway Discord (avec Message Content)…"
				: "Connexion au gateway Discord (mode slash)…");

			var intents = GatewayIntents.AllUnprivileged;
			if (withMessageContent) { intents |= GatewayIntents.MessageContent; }

			_client = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = intents,
				LogLevel = LogSeverity.Info,
				AlwaysDownloadUsers = false
			});
			_client.Ready += On_Bot_Ready;
			_client.Log += On_Bot_Log;
			_client.Disconnected += On_Bot_Disconnected;
			_client.Connected += On_Bot_Connected;
			_client.SlashCommandExecuted += On_SlashCommandExecuted;

			try
			{
				await _client.LoginAsync(TokenType.Bot, token);
				await _client.StartAsync();
			}
			catch (Exception e)
			{
				BotLog($"Échec démarrage : {e.Message}");
				return false;
			}

			// L'auth/les intents peuvent être rejetés en ASYNC (401 / disallowed intents) après StartAsync :
			// on attend la connexion réelle (max ~8s) avant de déclarer le succès.
			for (int i = 0; i < 16 && _client.ConnectionState != ConnectionState.Connected; i++)
			{
				await Task.Delay(500);
			}
			if (_client.ConnectionState != ConnectionState.Connected)
			{
				if (withMessageContent) { BotLog("Connexion refusée avec l'intent Message Content (probablement non activé/sauvé dans le portail)."); }
				try { await _client.StopAsync(); } catch { /* ignore */ }
				return false;
			}

			// Listen Commands (préfixe). En mode slash-only, message.Content sera vide mais c'est sans danger.
			new Commands(_client);
			return true;
		}

		// Journalise vers le log du Discord Bot dans l'UI (thread-safe).
		internal static void BotLog(string text)
		{
			try
			{
				Application.Current?.Dispatcher.Invoke(() =>
				{
					if (Application.Current.MainWindow is MainWindow wgsm) { wgsm.DiscordBotLog(text); }
				});
			}
			catch { /* ignore */ }
		}

		private Task On_Bot_Log(LogMessage msg)
		{
			if (msg.Severity <= LogSeverity.Warning || msg.Exception != null)
			{
				BotLog($"[{msg.Severity}] {msg.Source}: {msg.Message} {msg.Exception?.Message}".Trim());
			}
			return Task.CompletedTask;
		}

		private Task On_Bot_Connected()
		{
			BotLog($"Connecté en tant que {_client.CurrentUser?.Username ?? "?"}.");
			return Task.CompletedTask;
		}

		private Task On_Bot_Disconnected(Exception e)
		{
			BotLog($"Déconnecté : {e?.Message ?? "raison inconnue"}.");
			return Task.CompletedTask;
		}

		private async Task On_Bot_Ready()
		{
			try
			{
				Stream stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Images/WindowsGSM{(string.IsNullOrWhiteSpace(_donorType) ? string.Empty : $"-{_donorType}")}.png")).Stream;
				await _client.CurrentUser.ModifyAsync(x =>
				{
					x.Username = "WindowsGSM";
					x.Avatar = new Image(stream);
				});
			}
			catch
			{
				// ignore
			}

			await RegisterSlashCommands();

			// Bot réellement connecté : déclenche la résolution des noms d'admins côté UI.
			try
			{
				Application.Current?.Dispatcher.Invoke(() =>
				{
					if (Application.Current.MainWindow is MainWindow wgsm) { wgsm.OnDiscordBotReady(); }
				});
			}
			catch { /* ignore */ }

			BotLog("Bot prêt : commandes et présence actives.");

			List<Task> tasks = new List<Task>
			{
				StartDiscordPresenceUpdate(),
				StartDashboardMessageUpdate(),
			};

			_cancellationTokenSource = new CancellationTokenSource();

			await Task.Run(() =>
			{
				try
				{
					Task.WaitAny(tasks.ToArray(), _cancellationTokenSource.Token);
				}
				catch (AggregateException e)
				{
					System.Diagnostics.Debug.WriteLine($"{e.Message}");
				}
			});
		}

		private async Task StartDiscordPresenceUpdate()
		{
			while (_client != null && _client.CurrentUser != null)
			{
				if (Application.Current != null)
				{
					await Application.Current.Dispatcher.Invoke(async () =>
					{
						MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
						int total = WindowsGSM.ServerGrid.Items.Count;
						int online = WindowsGSM.GetStartedServerCount();
						int players = WindowsGSM.GetActivePlayers();
						await _client.SetGameAsync($"{online}/{total} en ligne • {players} joueur{(players > 1 ? "s" : string.Empty)}");
					});
				}

				await Task.Delay(60000);
			}
		}


		public void SetDonorType(string donorType)
		{
			_donorType = donorType;
		}

		public async Task Stop()
		{
			if (_client != null)
			{
				try
				{
					_cancellationTokenSource?.Cancel();
				}
				catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"{e.Message}");
                }

				await _client.StopAsync();

				// Delete the message after the bot stop
				try
				{
					if (_dashboardTextChannel != null)
					{
                        await _dashboardTextChannel.DeleteMessageAsync(_dashboardMessage);
                        _dashboardMessage = null;
                    }
				}
				catch
				{
					// ignore
				}
			}
		}

		// ===== Message « dashboard » live : poste/maj un embed d'état dans un salon =====
		private async Task StartDashboardMessageUpdate()
		{
			string channelId = Configs.GetDashboardChannel();
			if (string.IsNullOrWhiteSpace(channelId) || !ulong.TryParse(channelId, out ulong chId)) { return; }

			_dashboardMessage = null;
			while (_client != null && _client.ConnectionState == ConnectionState.Connected)
			{
				try
				{
					if (_client.GetChannel(chId) is SocketTextChannel channel)
					{
						_dashboardTextChannel = channel;
						var embed = await BuildDashboardEmbed();
						if (_dashboardMessage == null)
						{
							_dashboardMessage = await channel.SendMessageAsync(embed: embed.Build());
						}
						else
						{
							await _dashboardMessage.ModifyAsync(m => m.Embed = embed.Build());
						}
					}
				}
				catch (Exception e)
				{
					BotLog($"Dashboard message : {e.Message}");
					_dashboardMessage = null; // message supprimé/inaccessible -> on le recréera au prochain tour
				}

				int rate = Configs.GetDashboardRefreshRate();
				if (rate < 10) { rate = 10; }
				await Task.Delay(rate * 1000);
			}
		}

		private async Task<EmbedBuilder> BuildDashboardEmbed()
		{
			var system = new WindowsGSM.Functions.SystemMetrics();
			await Task.Run(() => system.GetCPUStaticInfo());
			await Task.Run(() => system.GetRAMStaticInfo());
			await Task.Run(() => system.GetDiskStaticInfo());
			double cpu = await Task.Run(() => system.GetCPUUsage());
			double ram = await Task.Run(() => system.GetRAMUsage());
			double disk = await Task.Run(() => system.GetDiskUsage());

			int total = 0, online = 0, players = 0;
			string list = "—";
			if (Application.Current != null)
			{
				(total, online, players, list) = Application.Current.Dispatcher.Invoke(() =>
				{
					MainWindow w = (MainWindow)Application.Current.MainWindow;
					int t = w.GetServerCount();
					int o = w.GetStartedServerCount();
					int p = w.GetActivePlayers();
					string l = string.Empty;
					foreach ((string id, string state, string name) in w.GetServerList())
					{
						l += $"`{id}` {(state == "Started" ? "🟢" : "⚫")} {name} — {w.GetServerPlayers(id)}\n";
					}
					if (string.IsNullOrEmpty(l)) { l = "—"; }
					return (t, o, p, l);
				});
			}

			var embed = new EmbedBuilder
			{
				Title = ":satellite: WindowsGSM Dashboard",
				Description = $"Hôte : {Environment.MachineName}",
				Color = Color.Blue
			};
			embed.AddField("CPU", $"{cpu:0.#}%", true);
			embed.AddField("RAM", $"{ram:0.#}%", true);
			embed.AddField("Disk", $"{disk:0.#}%", true);
			embed.AddField($"Serveurs en ligne : {online}/{total} • {players} joueur(s)", list, false);
			embed.WithFooter("WindowsGSM • mise à jour automatique");
			embed.WithCurrentTimestamp();
			return embed;
		}

		// ===== Slash commands (coexistent avec les commandes préfixe) =====
		private async Task RegisterSlashCommands()
		{
			try
			{
				var cmd = new SlashCommandBuilder()
					.WithName("wgsm")
					.WithDescription("Contrôle WindowsGSM")
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("action")
						.WithDescription("Action à exécuter")
						.WithType(ApplicationCommandOptionType.String)
						.WithRequired(true)
						.AddChoice("list", "list")
						.AddChoice("players", "players")
						.AddChoice("info", "info")
						.AddChoice("start", "start")
						.AddChoice("stop", "stop")
						.AddChoice("restart", "restart")
						.AddChoice("kill", "kill")
						.AddChoice("backup", "backup")
						.AddChoice("update", "update")
						.AddChoice("stats", "stats"))
					.AddOption("serverid", ApplicationCommandOptionType.Integer, "ID du serveur (si requis)", isRequired: false);

				await _client.CreateGlobalApplicationCommandAsync(cmd.Build());
			}
			catch (Exception e)
			{
				BotLog($"Slash commands (enregistrement) : {e.Message}");
			}
		}

		private async Task On_SlashCommandExecuted(SocketSlashCommand cmd)
		{
			if (cmd.CommandName != "wgsm") { return; }

			try
			{
				// Permission : mêmes règles que les commandes préfixe.
				List<string> serverIds = Configs.GetServerIdsByAdminId(cmd.User.Id.ToString());
				if (serverIds.Count == 0)
				{
					await cmd.RespondAsync("Tu n'as pas la permission.", ephemeral: true);
					return;
				}

				string action = cmd.Data.Options.FirstOrDefault(o => o.Name == "action")?.Value?.ToString() ?? string.Empty;
				string serverId = null;
				var sidOpt = cmd.Data.Options.FirstOrDefault(o => o.Name == "serverid");
				if (sidOpt != null) { serverId = Convert.ToInt64(sidOpt.Value).ToString(); }

				bool full = serverIds.Contains("0");

				// Actions globales
				if (action == "list" || action == "players" || action == "stats")
				{
					if (!full && action != "players") { await cmd.RespondAsync("Permission complète requise.", ephemeral: true); return; }
					await cmd.RespondAsync(await BuildSlashGlobalResponse(action), ephemeral: false);
					return;
				}

				// Actions ciblées : nécessitent un serverid + permission dessus
				if (string.IsNullOrEmpty(serverId)) { await cmd.RespondAsync("Cette action nécessite un `serverid`.", ephemeral: true); return; }
				if (!full && !serverIds.Contains(serverId)) { await cmd.RespondAsync("Pas de permission sur ce serveur.", ephemeral: true); return; }

				await cmd.DeferAsync();
				string result = await ExecuteSlashTargetAction(action, serverId, cmd.User.Id.ToString(), cmd.User.Username);
				await cmd.FollowupAsync(result);
			}
			catch (Exception e)
			{
				try { await cmd.RespondAsync($"Erreur : {e.Message}", ephemeral: true); } catch { }
			}
		}

		private async Task<string> BuildSlashGlobalResponse(string action)
		{
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;
				if (action == "stats")
				{
					return await Task.FromResult($"Serveurs {w.GetStartedServerCount()}/{w.GetServerCount()} en ligne • {w.GetActivePlayers()} joueur(s).");
				}

				string txt = string.Empty;
				foreach ((string id, string state, string name) in w.GetServerList())
				{
					string suffix = action == "players" ? $" — {w.GetServerPlayers(id)}" : string.Empty;
					txt += $"`{id}` {(state == "Started" ? "🟢" : "⚫")} {name}{suffix}\n";
				}
				return await Task.FromResult(string.IsNullOrEmpty(txt) ? "Aucun serveur." : txt);
			});
		}

		private async Task<string> ExecuteSlashTargetAction(string action, string serverId, string userId, string userName)
		{
			// IMPORTANT : ces méthodes lisent ServerGrid.Items et manipulent l'UI -> TOUT doit s'exécuter
			// sur le thread UI (le handler slash tourne sur un thread Discord.Net).
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;

				if (!w.IsServerExist(serverId)) { return $"Le serveur #{serverId} n'existe pas."; }

				switch (action)
				{
					case "info":
						return $"**#{serverId} {w.GetServerName(serverId)}** — {w.GetServerStatus(serverId)} | Joueurs {w.GetServerPlayers(serverId)} | `{w.GetServerConnectInfo(serverId)}`";
					case "start":
						return (await w.StartServerById(serverId, userId, userName)) ? $"#{serverId} démarré." : $"Échec démarrage #{serverId}.";
					case "stop":
						return (await w.StopServerById(serverId, userId, userName)) ? $"#{serverId} arrêté." : $"Échec arrêt #{serverId}.";
					case "restart":
						return (await w.RestartServerById(serverId, userId, userName)) ? $"#{serverId} redémarré." : $"Échec redémarrage #{serverId}.";
					case "kill":
						return (await w.KillServerById(serverId, userId, userName)) ? $"#{serverId} tué." : $"Échec kill #{serverId}.";
					case "backup":
						return (await w.BackupServerById(serverId, userId, userName)) ? $"#{serverId} sauvegardé." : $"Échec sauvegarde #{serverId}.";
					case "update":
						return (await w.UpdateServerById(serverId, userId, userName)) ? $"#{serverId} mis à jour." : $"Échec mise à jour #{serverId}.";
					default:
						return "Action inconnue.";
				}
			});
		}

		public string GetInviteLink()
		{
			return (_client == null || _client.CurrentUser == null) ? string.Empty : $"https://discordapp.com/api/oauth2/authorize?client_id={_client.CurrentUser.Id}&permissions=67497024&scope=bot";
		}

		public bool IsConnected => _client != null && _client.ConnectionState == ConnectionState.Connected;

		/// <summary>
		/// Résout un ID Discord en nom d'utilisateur via l'API REST (/users/{id}).
		/// Renvoie null si le bot n'est pas connecté ou si l'ID est introuvable.
		/// </summary>
		public async Task<string> ResolveUsername(string id)
		{
			try
			{
				if (_client == null || _client.ConnectionState != ConnectionState.Connected) { return null; }
				if (!ulong.TryParse(id, out ulong uid)) { return null; }
				var user = await _client.Rest.GetUserAsync(uid);
				if (user == null) { return null; }
				return string.IsNullOrEmpty(user.GlobalName) ? user.Username : user.GlobalName;
			}
			catch
			{
				return null;
			}
		}
	}
}
