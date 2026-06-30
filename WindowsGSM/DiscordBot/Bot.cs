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
		// One status message per channel (key = channelId) — supports multiple Discord servers (guilds).
		private readonly Dictionary<ulong, IUserMessage> _dashboardMessages = new Dictionary<ulong, IUserMessage>();
		// Interactive administration panel: 1 message per channel + selected server per channel.
		private readonly Dictionary<ulong, IUserMessage> _adminPanelMessages = new Dictionary<ulong, IUserMessage>();
		private readonly Dictionary<ulong, string> _adminPanelSelection = new Dictionary<ulong, string>();
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
				BotLog("Startup failed: no token configured.");
				return false;
			}

			// 1) Full attempt: with the privileged Message Content intent (prefix + slash commands).
			if (await TryStart(token, withMessageContent: true)) { return true; }

			// 2) Automatic fallback WITHOUT the privileged intent: if Discord closed the connection because
			//    "Message Content" is not enabled, we reconnect in slash-only mode.
			BotLog("Retrying without the privileged intent (slash-only mode)...");
			if (await TryStart(token, withMessageContent: false))
			{
				BotLog("Connected in SLASH-ONLY MODE. The /wgsm commands work. For prefix commands (!wgsm), enable \"Message Content Intent\" in the Discord portal then restart the bot.");
				return true;
			}

			BotLog("Failed: unable to connect (invalid token/401, or network). Check the Bot Token.");
			return false;
		}

		// Attempts a connection with a given set of intents. Returns true only if the connection succeeds.
		private async Task<bool> TryStart(string token, bool withMessageContent)
		{
			// Clean up any previous instance (fallback case).
			try { if (_client != null) { await _client.StopAsync(); _client.Dispose(); _client = null; } } catch { /* ignore */ }

			BotLog(withMessageContent
				? "Connecting to the Discord gateway (with Message Content)..."
				: "Connecting to the Discord gateway (slash mode)...");

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
			_client.ButtonExecuted += On_ButtonExecuted;
			_client.SelectMenuExecuted += On_SelectMenuExecuted;

			try
			{
				await _client.LoginAsync(TokenType.Bot, token);
				await _client.StartAsync();
			}
			catch (Exception e)
			{
				BotLog($"Startup failed: {e.Message}");
				return false;
			}

			// Auth/intents can be rejected ASYNCHRONOUSLY (401 / disallowed intents) after StartAsync:
			// we wait for the actual connection (max ~8s) before declaring success.
			for (int i = 0; i < 16 && _client.ConnectionState != ConnectionState.Connected; i++)
			{
				await Task.Delay(500);
			}
			if (_client.ConnectionState != ConnectionState.Connected)
			{
				if (withMessageContent) { BotLog("Connection refused with the Message Content intent (probably not enabled/saved in the portal)."); }
				try { await _client.StopAsync(); } catch { /* ignore */ }
				return false;
			}

			// Listen Commands (prefix). In slash-only mode, message.Content will be empty but that is harmless.
			new Commands(_client);
			return true;
		}

		// Logs to the Discord Bot log in the UI (thread-safe).
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
			BotLog($"Connected as {_client.CurrentUser?.Username ?? "?"}.");
			return Task.CompletedTask;
		}

		private Task On_Bot_Disconnected(Exception e)
		{
			BotLog($"Disconnected: {e?.Message ?? "unknown reason"}.");
			return Task.CompletedTask;
		}

		private async Task On_Bot_Ready()
		{
			// #131: do NOT overwrite the bot's custom name/avatar on every connection. By default we respect
			// what is configured in the Discord developer portal. "WindowsGSM" branding is opt-in via
			// HKCU\SOFTWARE\WindowsGSM\DiscordBotBranding="True".
			try
			{
				bool brand = false;
				try { using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM"); brand = (rk?.GetValue("DiscordBotBranding")?.ToString() == "True"); } catch { }
				if (brand)
				{
					Stream stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Images/WindowsGSM{(string.IsNullOrWhiteSpace(_donorType) ? string.Empty : $"-{_donorType}")}.png")).Stream;
					await _client.CurrentUser.ModifyAsync(x =>
					{
						x.Username = "WindowsGSM";
						x.Avatar = new Image(stream);
					});
				}
			}
			catch
			{
				// ignore
			}

			await RegisterSlashCommands();

			// Bot actually connected: triggers the resolution of admin names on the UI side.
			try
			{
				Application.Current?.Dispatcher.Invoke(() =>
				{
					if (Application.Current.MainWindow is MainWindow wgsm) { wgsm.OnDiscordBotReady(); }
				});
			}
			catch { /* ignore */ }

			BotLog("Bot ready: commands and presence active.");

			List<Task> tasks = new List<Task>
			{
				StartDiscordPresenceUpdate(),
				StartDashboardMessageUpdate(),
				StartAdminPanelUpdate(),
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
						await _client.SetGameAsync($"{online}/{total} online • {players} player{(players > 1 ? "s" : string.Empty)}");
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

				// Delete the status messages (all channels) after the bot stops.
				try
				{
					foreach (var kv in _dashboardMessages)
					{
						try { await kv.Value.DeleteAsync(); } catch { }
					}
					_dashboardMessages.Clear();

					foreach (var kv in _adminPanelMessages)
					{
						try { await kv.Value.DeleteAsync(); } catch { }
					}
					_adminPanelMessages.Clear();
				}
				catch
				{
					// ignore
				}
			}
		}

		// ===== Live "dashboard" message: posts/updates a status embed in a channel =====
		private async Task StartDashboardMessageUpdate()
		{
			_dashboardMessages.Clear();
			while (_client != null && _client.ConnectionState == ConnectionState.Connected)
			{
				try
				{
					var channelIds = Configs.GetDashboardChannels();
					if (channelIds.Count > 0)
					{
						var embed = await BuildDashboardEmbed(); // built once per cycle, reused on each channel
						foreach (var cidStr in channelIds)
						{
							if (!ulong.TryParse(cidStr, out ulong chId)) { continue; }
							try
							{
								if (_client.GetChannel(chId) is IMessageChannel channel)
								{
									if (_dashboardMessages.TryGetValue(chId, out var msg) && msg != null)
									{
										await msg.ModifyAsync(m => m.Embed = embed.Build());
									}
									else
									{
										_dashboardMessages[chId] = await AdoptOrSendDashboard(channel, embed.Build());
									}
								}
							}
							catch (Exception e)
							{
								BotLog($"Dashboard message (channel {cidStr}): {e.Message}");
								_dashboardMessages.Remove(chId); // message inaccessible -> recreated next cycle
							}
						}
					}
				}
				catch (Exception e)
				{
					BotLog($"Dashboard: {e.Message}");
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
				Description = $"Host: {Environment.MachineName}",
				Color = Color.Blue
			};
			embed.AddField("CPU", $"{cpu:0.#}%", true);
			embed.AddField("RAM", $"{ram:0.#}%", true);
			embed.AddField("Disk", $"{disk:0.#}%", true);
			embed.AddField($"Servers online: {online}/{total} • {players} player(s)", list, false);
			embed.WithFooter("WindowsGSM • automatic update");
			embed.WithCurrentTimestamp();
			return embed;
		}

		// Retrieves the bot's old messages (same embed title) left in the channel after a
		// restart: keeps the most recent one (to reuse/edit) and deletes the duplicates.
		// Avoids the accumulation of panels/dashboards on each bot restart.
		private async Task<IUserMessage> AdoptOrCleanupBotMessage(IMessageChannel channel, string embedTitle)
		{
			try
			{
				var msgs = await channel.GetMessagesAsync(50).FlattenAsync();
				var mine = msgs
					.Where(m => _client.CurrentUser != null && m.Author.Id == _client.CurrentUser.Id
							 && m.Embeds != null && m.Embeds.Any(e => e.Title == embedTitle))
					.OfType<IUserMessage>()
					.OrderByDescending(m => m.Timestamp)
					.ToList();

				if (mine.Count == 0) { return null; }

				for (int i = 1; i < mine.Count; i++) // delete all but the most recent
				{
					try { await mine[i].DeleteAsync(); } catch { }
				}
				return mine[0];
			}
			catch
			{
				return null;
			}
		}

		// For the dashboard: reuses an old message left after a restart (and purges duplicates), otherwise creates one.
		private async Task<IUserMessage> AdoptOrSendDashboard(IMessageChannel channel, Embed builtEmbed)
		{
			var adopted = await AdoptOrCleanupBotMessage(channel, ":satellite: WindowsGSM Dashboard");
			if (adopted != null)
			{
				await adopted.ModifyAsync(m => m.Embed = builtEmbed);
				return adopted;
			}
			return await channel.SendMessageAsync(embed: builtEmbed);
		}

		// ===== Administration panel: permanent embed + server dropdown menu + action buttons =====
		private async Task StartAdminPanelUpdate()
		{
			_adminPanelMessages.Clear();
			while (_client != null && _client.ConnectionState == ConnectionState.Connected)
			{
				try
				{
					var channelIds = Configs.GetAdminPanelChannels();
					foreach (var cidStr in channelIds)
					{
						if (!ulong.TryParse(cidStr, out ulong chId)) { continue; }
						try
						{
							if (_client.GetChannel(chId) is IMessageChannel channel)
							{
								var (embed, comp) = await BuildAdminPanel(chId);
								if (_adminPanelMessages.TryGetValue(chId, out var msg) && msg != null)
								{
									await msg.ModifyAsync(m => { m.Embed = embed; m.Components = comp; });
								}
								else
								{
									// Reuses an old panel left after a restart (and purges duplicates).
									var adopted = await AdoptOrCleanupBotMessage(channel, "🛠️ WindowsGSM — Administration");
									if (adopted != null)
									{
										await adopted.ModifyAsync(m => { m.Embed = embed; m.Components = comp; });
										_adminPanelMessages[chId] = adopted;
									}
									else
									{
										_adminPanelMessages[chId] = await channel.SendMessageAsync(embed: embed, components: comp);
									}
								}
							}
						}
						catch (Exception e)
						{
							BotLog($"Admin panel (channel {cidStr}): {e.Message}");
							_adminPanelMessages.Remove(chId); // recreated next cycle
						}
					}
				}
				catch (Exception e)
				{
					BotLog($"Admin panel: {e.Message}");
				}

				int rate = Configs.GetDashboardRefreshRate();
				if (rate < 10) { rate = 10; }
				await Task.Delay(rate * 1000);
			}
		}

		// Builds the admin panel for a channel: embed (selected server) + dropdown menu + buttons.
		private async Task<(Embed, MessageComponent)> BuildAdminPanel(ulong channelId)
		{
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;
				var servers = w.GetServerList().ToList(); // (id, state, name)

				// Selected server for this channel (default = first server).
				string selectedId = null;
				if (_adminPanelSelection.TryGetValue(channelId, out var sel) && servers.Any(s => s.Item1 == sel))
				{
					selectedId = sel;
				}
				if (selectedId == null && servers.Count > 0) { selectedId = servers[0].Item1; }

				var embed = new EmbedBuilder()
					.WithTitle("🛠️ WindowsGSM — Administration")
					.WithColor(Color.DarkOrange)
					.WithFooter($"{MainWindow.WGSM_VERSION} • Administration panel")
					.WithCurrentTimestamp();

				if (selectedId != null && w.IsServerExist(selectedId))
				{
					string name = w.GetServerName(selectedId);
					string status = w.GetServerStatus(selectedId).ToString();
					bool started = status == "Started";
					string players = w.GetServerPlayers(selectedId);
					string conn = w.GetServerConnectInfo(selectedId);
					embed.WithDescription($"**#{selectedId} {name}**\n{(started ? "🟢" : "⚫")} {status} • Players: {players}"
						+ (string.IsNullOrEmpty(conn) ? string.Empty : $"\n`{conn}`"));
				}
				else
				{
					embed.WithDescription("No servers.");
				}

				var cb = new ComponentBuilder();
				if (servers.Count > 0)
				{
					var menu = new SelectMenuBuilder()
						.WithCustomId("wgsmadm:select")
						.WithPlaceholder("Choose a server...");
					foreach ((string id, string state, string sname) in servers.Take(25)) // Discord: 25 options max
					{
						string label = $"#{id} {sname}";
						if (label.Length > 100) { label = label.Substring(0, 100); }
						menu.AddOption(label, id, state == "Started" ? "🟢 online" : "⚫ offline", isDefault: id == selectedId);
					}
					cb.WithSelectMenu(menu);
				}

				string bid = selectedId ?? "0";
				cb.WithButton("Start", $"wgsmadm:start:{bid}", ButtonStyle.Success, row: 1)
				  .WithButton("Stop", $"wgsmadm:stop:{bid}", ButtonStyle.Danger, row: 1)
				  .WithButton("Restart", $"wgsmadm:restart:{bid}", ButtonStyle.Primary, row: 1)
				  .WithButton("Backup", $"wgsmadm:backup:{bid}", ButtonStyle.Secondary, row: 1)
				  .WithButton("Update", $"wgsmadm:update:{bid}", ButtonStyle.Secondary, row: 1);

				return await Task.FromResult((embed.Build(), cb.Build()));
			});
		}

		// Selection of a server in the admin panel dropdown menu (customId = "wgsmadm:select").
		private async Task On_SelectMenuExecuted(SocketMessageComponent comp)
		{
			try
			{
				if ((comp.Data.CustomId ?? string.Empty) != "wgsmadm:select") { return; }

				var serverIds = Configs.GetServerIdsByAdminId(comp.User.Id.ToString());
				if (serverIds.Count == 0)
				{
					await comp.RespondAsync("You do not have permission.", ephemeral: true);
					return;
				}

				string chosen = comp.Data.Values.FirstOrDefault();
				if (!string.IsNullOrEmpty(chosen)) { _adminPanelSelection[comp.Channel.Id] = chosen; }

				var (embed, components) = await BuildAdminPanel(comp.Channel.Id);
				await comp.UpdateAsync(m => { m.Embed = embed; m.Components = components; });
			}
			catch (Exception e)
			{
				try { await comp.RespondAsync($"Error: {e.Message}", ephemeral: true); } catch { }
			}
		}

		// ===== Slash commands (coexist with prefix commands) =====
		private async Task RegisterSlashCommands()
		{
			try
			{
				var cmd = new SlashCommandBuilder()
					.WithName("wgsm")
					.WithDescription("Control WindowsGSM")
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("action")
						.WithDescription("Action to execute")
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
						.AddChoice("stats", "stats")
						.AddChoice("panel", "panel"))
					.AddOption("serverid", ApplicationCommandOptionType.Integer, "Server ID (if required)", isRequired: false);

				await _client.CreateGlobalApplicationCommandAsync(cmd.Build());
			}
			catch (Exception e)
			{
				BotLog($"Slash commands (registration): {e.Message}");
			}
		}

		private async Task On_SlashCommandExecuted(SocketSlashCommand cmd)
		{
			if (cmd.CommandName != "wgsm") { return; }

			try
			{
				// Permission: same rules as the prefix commands.
				List<string> serverIds = Configs.GetServerIdsByAdminId(cmd.User.Id.ToString());
				if (serverIds.Count == 0)
				{
					await cmd.RespondAsync("You do not have permission.", ephemeral: true);
					return;
				}

				string action = cmd.Data.Options.FirstOrDefault(o => o.Name == "action")?.Value?.ToString() ?? string.Empty;
				string serverId = null;
				var sidOpt = cmd.Data.Options.FirstOrDefault(o => o.Name == "serverid");
				if (sidOpt != null) { serverId = Convert.ToInt64(sidOpt.Value).ToString(); }

				bool full = serverIds.Contains("0");

				// Global actions
				if (action == "list" || action == "players" || action == "stats")
				{
					if (!full && action != "players") { await cmd.RespondAsync("Full permission required.", ephemeral: true); return; }
					await cmd.RespondAsync(await BuildSlashGlobalResponse(action), ephemeral: false);
					return;
				}

				// Interactive control panel (Start/Stop/Restart/Backup/Update buttons)
				if (action == "panel")
				{
					if (string.IsNullOrEmpty(serverId)) { await cmd.RespondAsync("This action requires a `serverid`.", ephemeral: true); return; }
					if (!full && !serverIds.Contains(serverId)) { await cmd.RespondAsync("No permission on this server.", ephemeral: true); return; }
					var (pembed, pcomp) = await BuildPanelMessage(serverId);
					await cmd.RespondAsync(embed: pembed, components: pcomp);
					return;
				}

				// Targeted actions: require a serverid + permission on it
				if (string.IsNullOrEmpty(serverId)) { await cmd.RespondAsync("This action requires a `serverid`.", ephemeral: true); return; }
				if (!full && !serverIds.Contains(serverId)) { await cmd.RespondAsync("No permission on this server.", ephemeral: true); return; }

				await cmd.DeferAsync();
				string result = await ExecuteSlashTargetAction(action, serverId, cmd.User.Id.ToString(), cmd.User.Username);
				await cmd.FollowupAsync(result);
			}
			catch (Exception e)
			{
				try { await cmd.RespondAsync($"Error: {e.Message}", ephemeral: true); } catch { }
			}
		}

		private async Task<string> BuildSlashGlobalResponse(string action)
		{
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;
				if (action == "stats")
				{
					return await Task.FromResult($"Servers {w.GetStartedServerCount()}/{w.GetServerCount()} online • {w.GetActivePlayers()} player(s).");
				}

				string txt = string.Empty;
				foreach ((string id, string state, string name) in w.GetServerList())
				{
					string suffix = action == "players" ? $" — {w.GetServerPlayers(id)}" : string.Empty;
					txt += $"`{id}` {(state == "Started" ? "🟢" : "⚫")} {name}{suffix}\n";
				}
				return await Task.FromResult(string.IsNullOrEmpty(txt) ? "No servers." : txt);
			});
		}

		private async Task<string> ExecuteSlashTargetAction(string action, string serverId, string userId, string userName)
		{
			// IMPORTANT: these methods read ServerGrid.Items and manipulate the UI -> EVERYTHING must run
			// on the UI thread (the slash handler runs on a Discord.Net thread).
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;

				if (!w.IsServerExist(serverId)) { return $"Server #{serverId} does not exist."; }

				switch (action)
				{
					case "info":
						return $"**#{serverId} {w.GetServerName(serverId)}** — {w.GetServerStatus(serverId)} | Players {w.GetServerPlayers(serverId)} | `{w.GetServerConnectInfo(serverId)}`";
					case "start":
						return (await w.StartServerById(serverId, userId, userName)) ? $"#{serverId} started." : $"Failed to start #{serverId}.";
					case "stop":
						return (await w.StopServerById(serverId, userId, userName)) ? $"#{serverId} stopped." : $"Failed to stop #{serverId}.";
					case "restart":
						return (await w.RestartServerById(serverId, userId, userName)) ? $"#{serverId} restarted." : $"Failed to restart #{serverId}.";
					case "kill":
						return (await w.KillServerById(serverId, userId, userName)) ? $"#{serverId} killed." : $"Failed to kill #{serverId}.";
					case "backup":
						return (await w.BackupServerById(serverId, userId, userName)) ? $"#{serverId} backed up." : $"Failed to back up #{serverId}.";
					case "update":
						return (await w.UpdateServerById(serverId, userId, userName)) ? $"#{serverId} updated." : $"Failed to update #{serverId}.";
					default:
						return "Unknown action.";
				}
			});
		}

		// Builds the control panel (status embed + buttons) for a server.
		private async Task<(Embed, MessageComponent)> BuildPanelMessage(string serverId)
		{
			return await Application.Current.Dispatcher.Invoke(async () =>
			{
				MainWindow w = (MainWindow)Application.Current.MainWindow;
				bool exists = w.IsServerExist(serverId);
				string name = exists ? w.GetServerName(serverId) : $"#{serverId}";
				string status = exists ? w.GetServerStatus(serverId).ToString() : "?";
				string players = exists ? w.GetServerPlayers(serverId) : "-";
				string conn = exists ? w.GetServerConnectInfo(serverId) : string.Empty;
				bool started = status == "Started";

				var embed = new EmbedBuilder()
					.WithTitle($"🎮 #{serverId} {name}")
					.WithDescription($"{(started ? "🟢" : "⚫")} **{status}**\nPlayers: {players}" + (string.IsNullOrEmpty(conn) ? string.Empty : $"\n`{conn}`"))
					.WithColor(started ? Color.Green : Color.LightGrey)
					.WithFooter($"{MainWindow.WGSM_VERSION} • Control panel")
					.Build();

				var comp = new ComponentBuilder()
					.WithButton("Start", $"wgsm:start:{serverId}", ButtonStyle.Success)
					.WithButton("Stop", $"wgsm:stop:{serverId}", ButtonStyle.Danger)
					.WithButton("Restart", $"wgsm:restart:{serverId}", ButtonStyle.Primary)
					.WithButton("Backup", $"wgsm:backup:{serverId}", ButtonStyle.Secondary)
					.WithButton("Update", $"wgsm:update:{serverId}", ButtonStyle.Secondary)
					.Build();

				return await Task.FromResult((embed, comp));
			});
		}

		// Click on a panel button: customId = "wgsm:<action>:<serverId>".
		private async Task On_ButtonExecuted(SocketMessageComponent comp)
		{
			try
			{
				string[] parts = (comp.Data.CustomId ?? string.Empty).Split(':');
				if (parts.Length != 3 || (parts[0] != "wgsm" && parts[0] != "wgsmadm")) { return; }
				bool isAdminPanel = parts[0] == "wgsmadm";
				string action = parts[1];
				string serverId = parts[2];

				// Same permission check as the commands (per admin / per server).
				var serverIds = Configs.GetServerIdsByAdminId(comp.User.Id.ToString());
				bool full = serverIds.Contains("0");
				if (serverIds.Count == 0 || (!full && !serverIds.Contains(serverId)))
				{
					await comp.RespondAsync("You do not have permission.", ephemeral: true);
					return;
				}

				await comp.DeferAsync(ephemeral: true);
				string result = await ExecuteSlashTargetAction(action, serverId, comp.User.Id.ToString(), comp.User.Username);
				await comp.FollowupAsync(result, ephemeral: true);

				// Refreshes the panel (status/players) after the action.
				try
				{
					var (embed, components) = isAdminPanel
						? await BuildAdminPanel(comp.Channel.Id)
						: await BuildPanelMessage(serverId);
					await comp.Message.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
				}
				catch { }
			}
			catch (Exception e)
			{
				try { await comp.RespondAsync($"Error: {e.Message}", ephemeral: true); } catch { }
			}
		}

		public string GetInviteLink()
		{
			return (_client == null || _client.CurrentUser == null) ? string.Empty : $"https://discordapp.com/api/oauth2/authorize?client_id={_client.CurrentUser.Id}&permissions=67497024&scope=bot";
		}

		public bool IsConnected => _client != null && _client.ConnectionState == ConnectionState.Connected;

		/// <summary>
		/// Resolves a Discord ID to a username via the REST API (/users/{id}).
		/// Returns null if the bot is not connected or if the ID cannot be found.
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
