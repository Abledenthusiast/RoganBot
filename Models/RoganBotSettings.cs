using System.Collections;

namespace RoganBot.Models
{
    public class RoganBotSettings
    {
        public ulong OwnerId { get; set; }
        public string Token { get; set; } = string.Empty;
        public int MessageCacheSize { get; set; }
        public bool CaseSensitiveComands { get; set; }
        public bool UseMentionPrefix { get; set; }
        public string Prefix { get; set; } = "!";
        public bool CommandsEnabled { get; set; } = false;
        public Dictionary<string, RoganResponse>? AOEResponses { get; set; }
    }
}
