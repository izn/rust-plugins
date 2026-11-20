using Oxide.Core.Libraries.Covalence;
using Newtonsoft.Json;

namespace Carbon.Plugins
{
    [Info("Join Announcements", "Pretend", "0.0.1")]
    [Description("Announces player joins and leaves in chat without external requests.")]
    public class JoinAnnouncements : CarbonPlugin
    {
        public Configuration PluginConfig;

        public class Configuration
        {
            [JsonProperty("Announce player joins")]
            public bool AnnounceJoins = true;

            [JsonProperty("Announce player leaves")]
            public bool AnnounceLeaves = true;

            [JsonProperty("Join message")]
            public string JoinMessage = "{label} {name} to the server!";

            [JsonProperty("Join label")]
            public string JoinLabel = "Welcome";

            [JsonProperty("Join label color")]
            public string JoinLabelColor = "#62C6D4FF";

            [JsonProperty("Join name color")]
            public string JoinNameColor = "#FFD166FF";

            [JsonProperty("Leave message")]
            public string LeaveMessage = "{label} {name} has left the server.";

            [JsonProperty("Leave label")]
            public string LeaveLabel = "Goodbye";

            [JsonProperty("Leave label color")]
            public string LeaveLabelColor = "#62C6D4FF";

            [JsonProperty("Leave name color")]
            public string LeaveNameColor = "#8EE3FFFF";
        }

        protected override void LoadDefaultConfig() => PluginConfig = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            PluginConfig = Config.ReadObject<Configuration>();
            if (PluginConfig == null)
            {
                PluginConfig = new Configuration();
                SaveConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(PluginConfig, true);

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!PluginConfig.AnnounceJoins)
                return;

            Server.Broadcast(FormatMessage(
                PluginConfig.JoinMessage,
                PluginConfig.JoinLabel,
                PluginConfig.JoinLabelColor,
                PluginConfig.JoinNameColor,
                Formatter.ToPlaintext(player.displayName)));
        }

        private void OnPlayerDisconnected(BasePlayer player, string _)
        {
            if (!PluginConfig.AnnounceLeaves)
                return;

            Server.Broadcast(FormatMessage(
                PluginConfig.LeaveMessage,
                PluginConfig.LeaveLabel,
                PluginConfig.LeaveLabelColor,
                PluginConfig.LeaveNameColor,
                Formatter.ToPlaintext(player.displayName)));
        }

        private static string FormatMessage(
            string template,
            string label,
            string labelColor,
            string nameColor,
            string playerName)
        {
            return template
                .Replace("{label}", $"<color={labelColor}>{label}</color>")
                .Replace("{name}", $"<color={nameColor}>{playerName}</color>");
        }
    }
}
