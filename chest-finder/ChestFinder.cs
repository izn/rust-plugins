using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Carbon.Plugins
{
    [Info("ChestFinder", "Pretend", "0.1.0")]
    [Description("Find matching items in nearby containers you can access.")]
    public class ChestFinder : CarbonPlugin
    {
        private const string PermissionUse = "chestfinder.use";
        private const float ConnectorStartHeight = 0.75f;
        private const float ConnectorLabelGap = 0.25f;
        private PluginConfig pluginConfig;
        private readonly Dictionary<ulong, float> lastSearchByPlayer = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, List<Marker>> markersByPlayer = new Dictionary<ulong, List<Marker>>();

        private sealed class PluginConfig
        {
            [JsonProperty(PropertyName = "Maximum search distance")]
            public float MaxSearchDistance = 500f;

            [JsonProperty(PropertyName = "Marker duration (seconds)")]
            public float MarkerDuration = 10f;

            [JsonProperty(PropertyName = "Search cooldown (seconds)")]
            public float SearchCooldown = 5f;

            [JsonProperty(PropertyName = "Minimum search length")]
            public int MinimumSearchLength = 3;

            [JsonProperty(PropertyName = "Maximum marked containers")]
            public int MaxMarkedContainers = 20;

            [JsonProperty(PropertyName = "Marker height")]
            public float MarkerHeight = 1.9f;

            [JsonProperty(PropertyName = "Text scale")]
            public float TextScale = 1.1f;

            [JsonProperty(PropertyName = "Text color")]
            public string TextColor = "#F5F5EFF2";

            [JsonProperty(PropertyName = "Connector color")]
            public string ConnectorColor = "#57BFA86B";
        }

        private class Match
        {
            public string Name;
            public int Amount;
        }

        private class Marker
        {
            public List<Match> Matches = new List<Match>();
            public string Label;
            public int DrawId;
            public Vector3 Position;
        }

        protected override void LoadDefaultConfig()
        {
            pluginConfig = new PluginConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            pluginConfig = Config.ReadObject<PluginConfig>();
            if (pluginConfig == null)
            {
                pluginConfig = new PluginConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(pluginConfig, true);
        }

        private static Color ParseColor(string value)
        {
            ColorUtility.TryParseHtmlString(value, out Color color);
            return color;
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to use /finditem.",
                ["Usage"] = "Usage: /finditem <item>",
                ["MinimumLength"] = "Search terms must be at least {0} characters.",
                ["Cooldown"] = "Please wait {0} more seconds before searching again.",
                ["NoMatches"] = "No matching items found in nearby containers.",
                ["Found"] = "Found {0} matching containers."
            }, this, "en");
        }

        private void Unload()
        {
            ClearMarkersForAllPlayers();
            markersByPlayer.Clear();
            lastSearchByPlayer.Clear();
        }

        private void ClearMarkersForAllPlayers()
        {
            foreach (KeyValuePair<ulong, List<Marker>> entry in markersByPlayer)
            {
                BasePlayer player = BasePlayer.FindByID(entry.Key);
                if (player != null && player.IsConnected) ClearMarkers(player, entry.Value);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string _)
        {
            ClearMarkersForPlayer(player);
            lastSearchByPlayer.Remove(player.userID);
        }

        [ChatCommand("finditem")]
        private void FindItem(BasePlayer player, string _, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                SendMessage(player, "NoPermission");
                return;
            }

            string query = string.Join(" ", args).Trim();
            if (string.IsNullOrEmpty(query))
            {
                SendMessage(player, "Usage");
                return;
            }

            if (query.Length < pluginConfig.MinimumSearchLength)
            {
                SendMessage(player, "MinimumLength", pluginConfig.MinimumSearchLength);
                return;
            }

            float cooldown = GetCooldown(player);
            if (cooldown > 0f)
            {
                SendMessage(player, "Cooldown", Mathf.CeilToInt(cooldown));
                return;
            }

            lastSearchByPlayer[player.userID] = Time.realtimeSinceStartup;
            // NOTE: Clear old markers if the cooldown ends before they expire
            ClearMarkersForPlayer(player);

            List<Marker> found = FindMatchingContainers(player, query);

            List<Marker> selected = found
                .OrderBy(m => (m.Position - player.transform.position).sqrMagnitude)
                .Take(pluginConfig.MaxMarkedContainers).ToList();
            if (selected.Count == 0)
            {
                SendMessage(player, "NoMatches");
                return;
            }

            markersByPlayer[player.userID] = selected;
            DrawMarkers(player, selected);
            SendMessage(player, "Found", found.Count);
        }

        private void SendMessage(BasePlayer player, string key, params object[] args)
        {
            SendReply(player, string.Format(
                lang.GetMessage(key, this, player.UserIDString), args));
        }

        private float GetCooldown(BasePlayer player)
        {
            if (!lastSearchByPlayer.TryGetValue(player.userID, out float previous))
                return 0f;

            return pluginConfig.SearchCooldown -
                   (Time.realtimeSinceStartup - previous);
        }

        private void ClearMarkersForPlayer(BasePlayer player)
        {
            if (!markersByPlayer.TryGetValue(player.userID, out List<Marker> markers)) return;

            if (player.IsConnected) ClearMarkers(player, markers);
            markersByPlayer.Remove(player.userID);
        }

        private List<Marker> FindMatchingContainers(BasePlayer player, string query)
        {
            var matches = new List<Marker>();
            var nearbyEntities = new List<BaseEntity>();
            var localizedNames = new Dictionary<int, string>();
            var language = lang.GetLanguage(player.UserIDString);

            Vis.Entities(player.transform.position, pluginConfig.MaxSearchDistance, nearbyEntities);
            foreach (var nearbyEntity in nearbyEntities)
            {
                ItemContainer inventory = GetContainerInventory(nearbyEntity);
                if (inventory == null) continue;
                if (!CanSearchContainer(player, nearbyEntity)) continue;

                var marker = new Marker
                {
                    Position = nearbyEntity.transform.position
                };

                AddMatchingItems(marker, inventory, query, language, localizedNames);
                if (marker.Matches.Count == 0) continue;

                marker.Matches = marker.Matches
                    .OrderBy(match => match.Name)
                    .ToList();
                marker.Label = string.Join("\n", marker.Matches.Select(match =>
                    $"{match.Amount}x {match.Name}"));
                marker.DrawId = matches.Count + 1;
                matches.Add(marker);
            }

            return matches;
        }

        private static ItemContainer GetContainerInventory(BaseEntity entity)
        {
            return (entity as IItemContainerEntity)?.inventory;
        }

        private bool CanSearchContainer(BasePlayer player, BaseEntity container)
        {
            return container.GetBuildingPrivilege()?.IsAuthed(player) == true &&
                   (container.GetSlot(BaseEntity.Slot.Lock) as BaseLock)?.IsLocked() != true;
        }

        private void AddMatchingItems(
            Marker marker,
            ItemContainer inventory,
            string query,
            string language,
            Dictionary<int, string> localizedNames)
        {
            foreach (var item in inventory.itemList)
            {
                var definition = item.info;

                if (!localizedNames.TryGetValue(definition.itemid, out string name))
                {
                    name = definition.displayName.english;
                    var translated = Translate.GetServerTranslation(
                        definition.displayName.token,
                        language);
                    if (!string.IsNullOrEmpty(translated) &&
                        translated != definition.displayName.token)
                    {
                        name = translated;
                    }

                    if (string.IsNullOrEmpty(name))
                        name = definition.shortname;

                    localizedNames[definition.itemid] = name;
                }

                if (!Matches(query, name, definition.displayName.english, definition.shortname))
                    continue;

                var existing = marker.Matches.FirstOrDefault(match => match.Name == name);
                if (existing == null)
                {
                    marker.Matches.Add(new Match
                    {
                        Name = name,
                        Amount = item.amount
                    });
                }
                else
                {
                    existing.Amount += item.amount;
                }
            }
        }

        private static bool Matches(string query, params string[] names)
        {
            foreach (string name in names)
            {
                if (name != null &&
                    name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void DrawMarkers(BasePlayer player, List<Marker> markers)
        {
            Color textColor = ParseColor(pluginConfig.TextColor);
            Color connectorColor = ParseColor(pluginConfig.ConnectorColor);

            foreach (Marker marker in markers)
            {
                Vector3 position = marker.Position;
                Vector3 textPosition = position + Vector3.up * pluginConfig.MarkerHeight;

                player.SendConsoleCommand(
                    "ddraw.text", pluginConfig.MarkerDuration, textColor, textPosition,
                    marker.Label, 0f, false, pluginConfig.TextScale, 0, marker.DrawId);
                player.SendConsoleCommand(
                    "ddraw.line", pluginConfig.MarkerDuration, connectorColor,
                    position + Vector3.up * ConnectorStartHeight,
                    textPosition - Vector3.up * ConnectorLabelGap,
                    0f, false, 0, marker.DrawId);
            }
        }

        private void ClearMarkers(BasePlayer player, List<Marker> markers)
        {
            foreach (Marker marker in markers)
            {
                Vector3 textPosition = marker.Position + Vector3.up * pluginConfig.MarkerHeight;

                player.SendConsoleCommand(
                    "ddraw.text", 0f, Color.clear, textPosition, string.Empty,
                    0f, false, pluginConfig.TextScale, 0, marker.DrawId);
                player.SendConsoleCommand(
                    "ddraw.line", 0f, Color.clear,
                    marker.Position + Vector3.up * ConnectorStartHeight,
                    textPosition - Vector3.up * ConnectorLabelGap,
                    0f, false, 0, marker.DrawId);
            }
        }

    }
}
