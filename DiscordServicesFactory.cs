using Discord;
using Discord.Commands;
using Discord.WebSocket;
using RoganBot.Models;

namespace RoganBot
{
    public static class DiscordServicesFactory
    {
        public static DiscordSocketClient NewDiscordSocketClient(RoganBotSettings roganBotSettings)
        {
            return new DiscordSocketClient(new DiscordSocketConfig
            {
                DefaultRetryMode = RetryMode.AlwaysRetry,
                MessageCacheSize = roganBotSettings.MessageCacheSize,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildVoiceStates

            });
        }

        public static CommandService CreateCommandService(RoganBotSettings botSettings)
        {
            return new CommandService(new CommandServiceConfig
            {
                DefaultRunMode = RunMode.Async,
                CaseSensitiveCommands = botSettings.CaseSensitiveComands,
                SeparatorChar = ' '
            });
        }
    }
}
