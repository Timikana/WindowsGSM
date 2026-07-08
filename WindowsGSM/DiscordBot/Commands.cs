using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WindowsGSM.Functions;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.DiscordBot
{
    class Commands
    {
        private readonly DiscordSocketClient _client;

        public Commands(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += CommandReceivedAsync;
        }

        private async Task CommandReceivedAsync(SocketMessage message)
        {
            // The bot should never respond to itself.
            if (message.Author.Id == _client.CurrentUser.Id) { return; }

            // Return if the author is not admin
            List<string> adminIds = Configs.GetBotAdminIds();
            if (!adminIds.Contains(message.Author.Id.ToString())) { return; }

            // Return if the message is not WindowsGSM prefix
            var prefix = Configs.GetBotPrefix();
            var commandLen = prefix.Length + 4;
            if (message.Content.Length < commandLen) { return; }
            if (message.Content.Length == commandLen && message.Content == $"{prefix}wgsm")
            {
                await SendHelpEmbed(message);
                return;
            }

            if (message.Content.Length >= commandLen + 1 && message.Content.Substring(0, commandLen + 1) == $"{prefix}wgsm ")
            {
                // Remote Actions
                string[] args = message.Content.Split(new[] { ' ' }, 2);
                string[] splits = args[1].Split(' ');

                switch (splits[0])
                {
                    case "start":
                    case "stop":
                    case "restart":
                    case "kill":
                    case "send":
                    case "list":
                    case "check":
                    case "backup":
                    case "update":
                    case "stats":
                    case "info":
                    case "players":
                    case "console":
                    case "log":
                        List<string> serverIds = Configs.GetServerIdsByAdminId(message.Author.Id.ToString());
                        if (splits[0] == "check")
                        {
                            await message.Channel.SendMessageAsync(
                                serverIds.Contains("0") ?
                                Loc.T("Bot.CheckFull") :
                                Loc.T("Bot.CheckPartial", string.Join(",", serverIds.ToArray())));
                            break;
                        }

                        // Global commands (full permission required): list, players (without id)
                        if (splits[0] == "list" && serverIds.Contains("0"))
                        {
                            await Action_List(message);
                        }
                        else if (splits[0] == "players" && splits.Length == 1 && serverIds.Contains("0"))
                        {
                            await Action_PlayersAll(message);
                        }
                        else if (splits[0] == "stats" && serverIds.Contains("0"))
                        {
                            // stats exposes host CPU/RAM/disk -> full permission only (like list/players-all).
                            await Action_Stats(message);
                        }
                        else if (splits[0] != "list" && splits.Length >= 2 && (serverIds.Contains("0") || serverIds.Contains(splits[1])))
                        {
                            switch (splits[0])
                            {
                                case "start": await Action_Start(message, args[1]); break;
                                case "stop": await Action_Stop(message, args[1]); break;
                                case "restart": await Action_Restart(message, args[1]); break;
                                case "kill": await Action_Kill(message, args[1]); break;
                                case "send": await Action_SendCommand(message, args[1]); break;
                                case "backup": await Action_Backup(message, args[1]); break;
                                case "update": await Action_Update(message, args[1]); break;
                                case "info": await Action_Info(message, args[1]); break;
                                case "players": await Action_Players(message, args[1]); break;
                                case "console":
                                case "log": await Action_Console(message, args[1]); break;
                            }
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.NoPermOrMissingId"));
                        }
                        break;
                    default: await SendHelpEmbed(message); break;
                }
            }
        }

        private async Task Action_List(SocketMessage message)
        {
            await Application.Current.Dispatcher.Invoke(async () =>
            {
                MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;

                var list = WindowsGSM.GetServerList();

                string ids = string.Empty;
                string status = string.Empty;
                string servers = string.Empty;

                foreach ((string id, string state, string server) in list)
                {
                    ids += $"`{id}`\n";
                    status += $"`{state}`\n";
                    servers += $"`{server}`\n";
                }

                var embed = new EmbedBuilder { Color = Color.Teal };
                embed.AddField(Loc.T("Bot.FieldId"), ids, inline: true);
                embed.AddField(Loc.T("Bot.FieldStatus"), status, inline: true);
                embed.AddField(Loc.T("Bot.FieldServerName"), servers, inline: true);

                await message.Channel.SendMessageAsync(embed: embed.Build());
            });
        }

        private async Task Action_Start(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Stopped)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.StartingServer", args[1]));
                            bool started = await WindowsGSM.StartServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(started ? Loc.T("Bot.ServerStarted", args[1]) : Loc.T("Bot.ServerFailStart", args[1]));
                        }
                        else if (serverStatus == MainWindow.ServerStatus.Started)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.ServerAlreadyStarted", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotStart", args[1], serverStatus.ToString()));
                        }

                        await SendServerEmbed(message, Color.Green, args[1], WindowsGSM.GetServerStatus(args[1]).ToString(), WindowsGSM.GetServerName(args[1]));
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageStart", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Stop(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Started)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.StoppingServer", args[1]));
                            bool started = await WindowsGSM.StopServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(started ? Loc.T("Bot.ServerStopped", args[1]) : Loc.T("Bot.ServerFailStop", args[1]));
                        }
                        else if (serverStatus == MainWindow.ServerStatus.Stopped)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.ServerAlreadyStopped", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotStop", args[1], serverStatus.ToString()));
                        }

                        await SendServerEmbed(message, Color.Orange, args[1], WindowsGSM.GetServerStatus(args[1]).ToString(), WindowsGSM.GetServerName(args[1]));
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageStop", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Restart(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Started)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.RestartingServer", args[1]));
                            bool started = await WindowsGSM.RestartServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(started ? Loc.T("Bot.ServerRestarted", args[1]) : Loc.T("Bot.ServerFailRestart", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotRestart", args[1], serverStatus.ToString()));
                        }

                        await SendServerEmbed(message, Color.Blue, args[1], WindowsGSM.GetServerStatus(args[1]).ToString(), WindowsGSM.GetServerName(args[1]));
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageRestart", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_SendCommand(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length >= 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Started)
                        {
                            string sendCommand = command.Substring(args[1].Length + 6);
                            bool sent = await WindowsGSM.SendCommandById(args[1], sendCommand, message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync((sent ? Loc.T("Bot.CommandSent", args[1]) : Loc.T("Bot.FailSendCommand", args[1])) + $" | `{sendCommand}`");
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotSendCommand", args[1], serverStatus.ToString()));
                        }
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageSend", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Backup(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length >= 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Stopped)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.BackupStarted", args[1]));
                            bool backuped = await WindowsGSM.BackupServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(backuped ? Loc.T("Bot.BackupComplete", args[1]) : Loc.T("Bot.FailBackupCmd", args[1]));
                        }
                        else if (serverStatus == MainWindow.ServerStatus.Backuping)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.AlreadyBackuping", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotBackup", args[1], serverStatus.ToString()));
                        }
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageBackup", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Update(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length >= 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Stopped)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.UpdateStarted", args[1]));
                            bool updated = await WindowsGSM.UpdateServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(updated ? Loc.T("Bot.ServerUpdated", args[1]) : Loc.T("Bot.FailUpdateCmd", args[1]));
                        }
                        else if (serverStatus == MainWindow.ServerStatus.Updating)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.AlreadyUpdating", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.CannotUpdate", args[1], serverStatus.ToString()));
                        }
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageUpdate", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Kill(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        MainWindow.ServerStatus serverStatus = WindowsGSM.GetServerStatus(args[1]);
                        if (serverStatus == MainWindow.ServerStatus.Stopped)
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.ServerAlreadyStopped", args[1]));
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync(Loc.T("Bot.KillingServer", args[1]));
                            bool killed = await WindowsGSM.KillServerById(args[1], message.Author.Id.ToString(), message.Author.Username);
                            await message.Channel.SendMessageAsync(killed ? Loc.T("Bot.ServerKilled", args[1]) : Loc.T("Bot.ServerFailKill", args[1]));
                            await SendServerEmbed(message, Color.Red, args[1], WindowsGSM.GetServerStatus(args[1]).ToString(), WindowsGSM.GetServerName(args[1]));
                        }
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageKill", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Info(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        var embed = new EmbedBuilder { Color = Color.Teal, Title = Loc.T("Bot.InfoTitle", args[1], WindowsGSM.GetServerName(args[1])) };
                        embed.AddField(Loc.T("Bot.FieldStatus"), WindowsGSM.GetServerStatus(args[1]).ToString(), inline: true);
                        embed.AddField(Loc.T("Bot.FieldGame"), string.IsNullOrWhiteSpace(WindowsGSM.GetServerGame(args[1])) ? "—" : WindowsGSM.GetServerGame(args[1]), inline: true);
                        embed.AddField(Loc.T("Bot.FieldPlayers"), WindowsGSM.GetServerPlayers(args[1]), inline: true);
                        embed.AddField(Loc.T("Bot.FieldConnect"), $"`{WindowsGSM.GetServerConnectInfo(args[1])}`", inline: false);
                        await message.Channel.SendMessageAsync(embed: embed.Build());
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageInfo", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Players(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length == 2 && int.TryParse(args[1], out int i))
            {
                await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (WindowsGSM.IsServerExist(args[1]))
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.PlayersLine", args[1], WindowsGSM.GetServerName(args[1]), WindowsGSM.GetServerPlayers(args[1])));
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    }
                });
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsagePlayers", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_PlayersAll(SocketMessage message)
        {
            await Application.Current.Dispatcher.Invoke(async () =>
            {
                MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                var list = WindowsGSM.GetServerList();

                string ids = string.Empty, servers = string.Empty, players = string.Empty;
                foreach ((string id, string state, string server) in list)
                {
                    ids += $"`{id}`\n";
                    servers += $"`{server}`\n";
                    players += $"`{WindowsGSM.GetServerPlayers(id)}`\n";
                }

                if (string.IsNullOrEmpty(ids)) { ids = servers = players = "—"; }

                var embed = new EmbedBuilder { Color = Color.Teal, Title = Loc.T("Bot.PlayersOnlineTitle") };
                embed.AddField(Loc.T("Bot.FieldId"), ids, inline: true);
                embed.AddField(Loc.T("Bot.FieldServerName"), servers, inline: true);
                embed.AddField(Loc.T("Bot.FieldPlayers"), players, inline: true);
                await message.Channel.SendMessageAsync(embed: embed.Build());
            });
        }

        private async Task Action_Console(SocketMessage message, string command)
        {
            string[] args = command.Split(' ');
            if (args.Length >= 2 && int.TryParse(args[1], out int i))
            {
                int lines = 15;
                if (args.Length >= 3 && int.TryParse(args[2], out int n)) { lines = Math.Max(1, Math.Min(40, n)); }

                string tail = await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    if (!WindowsGSM.IsServerExist(args[1])) { return null; }
                    return await Task.FromResult(WindowsGSM.GetServerConsoleTail(args[1], lines));
                });

                if (tail == null)
                {
                    await message.Channel.SendMessageAsync(Loc.T("Bot.ServerNotExists", args[1]));
                    return;
                }
                if (string.IsNullOrWhiteSpace(tail)) { tail = Loc.T("Bot.ConsoleEmpty"); }
                // Discord limits to 2000 characters: we truncate the code block.
                if (tail.Length > 1900) { tail = tail.Substring(tail.Length - 1900); }
                await message.Channel.SendMessageAsync(Loc.T("Bot.ConsoleHeader", args[1], lines) + $"\n```\n{tail}\n```");
            }
            else
            {
                await message.Channel.SendMessageAsync(Loc.T("Bot.UsageConsole", Configs.GetBotPrefix()));
            }
        }

        private async Task Action_Stats(SocketMessage message)
        {
            var system = new SystemMetrics();
            await Task.Run(() => system.GetCPUStaticInfo());
            await Task.Run(() => system.GetRAMStaticInfo());
            await Task.Run(() => system.GetDiskStaticInfo());

            await message.Channel.SendMessageAsync(embed: (await GetMessageEmbed(system)).Build());
        }

        private async Task SendServerEmbed(SocketMessage message, Color color, string serverId, string serverStatus, string serverName)
        {
            var embed = new EmbedBuilder { Color = color };
            embed.AddField(Loc.T("Bot.FieldId"), serverId, inline: true);
            embed.AddField(Loc.T("Bot.FieldStatus"), serverStatus, inline: true);
            embed.AddField(Loc.T("Bot.FieldServerName"), serverName, inline: true);

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }

        private async Task SendHelpEmbed(SocketMessage message)
        {
            var embed = new EmbedBuilder
            {
                Title = Loc.T("Bot.HelpTitle"),
                Color = Color.Teal
            };

            string prefix = Configs.GetBotPrefix();
            embed.AddField(Loc.T("Bot.HelpFieldCommand"), $"{prefix}wgsm check\n{prefix}wgsm list\n{prefix}wgsm players [<SERVERID>]\n{prefix}wgsm info <SERVERID>\n{prefix}wgsm console <SERVERID> [lines]\n{prefix}wgsm start <SERVERID>\n{prefix}wgsm stop <SERVERID>\n{prefix}wgsm restart <SERVERID>\n{prefix}wgsm kill <SERVERID>\n{prefix}wgsm update <SERVERID>\n{prefix}wgsm send <SERVERID> <COMMAND>\n{prefix}wgsm backup <SERVERID>\n{prefix}wgsm stats", inline: true);
            embed.AddField(Loc.T("Bot.HelpFieldUsage"), Loc.T("Bot.HelpUsageValue"), inline: true);

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }

        private string GetProgressBar(double progress)
        {
            // ▌ // ▋ // █ // Which one is the best?
            const int MAX_BLOCK = 23;
            string display = $" {(int)progress}% ";

            int startIndex = MAX_BLOCK / 2 - display.Length / 2;
            string progressBar = string.Concat(Enumerable.Repeat("█", (int)(progress / 100 * MAX_BLOCK))).PadRight(MAX_BLOCK).Remove(startIndex, display.Length).Insert(startIndex, display);

            return $"**`{progressBar}`**";
        }

        private string GetActivePlayersString(int activePlayers)
        {
            const int MAX_BLOCK = 23;
            string display = $" {activePlayers} ";

            int startIndex = MAX_BLOCK / 2 - display.Length / 2;
            string activePlayersString = string.Concat(Enumerable.Repeat(" ", MAX_BLOCK)).Remove(startIndex, display.Length).Insert(startIndex, display);

            return $"**`{activePlayersString}`**";
        }

        private async Task<(int, int, int)> GetGameServerDashBoardDetails()
        {
            if (Application.Current != null)
            {
                return await Application.Current.Dispatcher.Invoke(async () =>
                {
                    MainWindow WindowsGSM = (MainWindow)Application.Current.MainWindow;
                    return (WindowsGSM.GetServerCount(), WindowsGSM.GetStartedServerCount(), WindowsGSM.GetActivePlayers());
                });
            }

            return (0, 0, 0);
        }

        private async Task<EmbedBuilder> GetMessageEmbed(SystemMetrics system)
        {
            var embed = new EmbedBuilder
            {
                Title = ":small_orange_diamond: " + Loc.T("Bot.SystemMetricsTitle"),
                Description = Loc.T("Bot.MetricsServerName", Environment.MachineName),
                Color = Color.Blue
            };

            embed.AddField(Loc.T("Bot.FieldCpu"), GetProgressBar(await Task.Run(() => system.GetCPUUsage())), true);
            double ramUsage = await Task.Run(() => system.GetRAMUsage());
            embed.AddField(Loc.T("Bot.FieldMemory") + ": " + SystemMetrics.GetMemoryRatioString(ramUsage, system.RAMTotalSize), GetProgressBar(ramUsage), true);
            double diskUsage = await Task.Run(() => system.GetDiskUsage());
            embed.AddField(Loc.T("Bot.FieldDisk") + ": " + SystemMetrics.GetDiskRatioString(diskUsage, system.DiskTotalSize), GetProgressBar(diskUsage), true);

            (int serverCount, int startedCount, int activePlayers) = await GetGameServerDashBoardDetails();
            embed.AddField(Loc.T("Bot.FieldServers", serverCount, MainWindow.MAX_SERVER), GetProgressBar(serverCount * 100 / MainWindow.MAX_SERVER), true);
            embed.AddField(Loc.T("Bot.FieldOnline", startedCount, serverCount), GetProgressBar((serverCount == 0) ? 0 : startedCount * 100 / serverCount), true);
            embed.AddField(Loc.T("Bot.FieldActivePlayers"), GetActivePlayersString(activePlayers), true);

            embed.WithFooter(new EmbedFooterBuilder().WithIconUrl("https://github.com/WindowsGSM/WindowsGSM/raw/master/WindowsGSM/Images/WindowsGSM.png").WithText($"WindowsGSM {MainWindow.WGSM_VERSION} | " + Loc.T("Bot.SystemMetricsTitle")));
            embed.WithCurrentTimestamp();

            return embed;
        }
    }
}
