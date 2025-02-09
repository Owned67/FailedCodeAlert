/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using System.Collections.Generic;
using System.IO;

namespace Oxide.Plugins
{
    [Info("Failed Code Alert", "VisEntities", "1.2.0")]
    [Description("Warns players when someone tries to access their code lock with the wrong code.")]
    public class FailedCodeAlert : RustPlugin
    {
        #region Fields

        private static FailedCodeAlert _plugin;
        private static Configuration _config;
        private StoredData _storedData;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Discord Webhook Url (Leave blank to disable)")]
            public string DiscordWebhookUrl { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DiscordWebhookUrl = ""
            };
        }

        #endregion Configuration

        #region Stored Data

        public class StoredData
        {
            [JsonProperty("Intrusion Alerts")]
            public Dictionary<ulong, bool> IntrusionAlerts { get; set; } = new Dictionary<ulong, bool>();
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
            _storedData = DataFileUtil.LoadOrCreate<StoredData>(DataFileUtil.GetFilePath());
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnCodeEntered(CodeLock codeLock, BasePlayer player, string enteredCode)
        {
            if (codeLock == null || player == null)
                return;

            if (PermissionUtil.HasPermission(player, PermissionUtil.IGNORE))
                return;

            if (codeLock.code == enteredCode)
                return;

            if (!string.IsNullOrEmpty(_config.DiscordWebhookUrl))
                SendAlertToDiscord(codeLock, player);

            ulong ownerId = codeLock.OwnerID;
            if (!_storedData.IntrusionAlerts.TryGetValue(ownerId, out bool enabled))
                enabled = true;

            if (!enabled)
                return;

            NotifyCodeLockOwners(codeLock, player);
        }

        #endregion Oxide Hooks

        #region Notifications

        private void NotifyCodeLockOwners(CodeLock codeLock, BasePlayer intruder)
        {
            ulong ownerId = codeLock.OwnerID;

            var team = PlayerUtil.GetTeam(ownerId);
            var notifiedPlayers = new HashSet<ulong>();

            BasePlayer owner = PlayerUtil.FindById(ownerId);
            if (owner != null && owner.IsConnected)
            {
                MessagePlayer(owner, Lang.FailedAttempt, intruder.displayName, MapHelper.PositionToString(codeLock.transform.position));
                notifiedPlayers.Add(ownerId);
            }

            if (team != null)
            {
                foreach (ulong teammateId in team.members)
                {
                    if (notifiedPlayers.Contains(teammateId))
                        continue;

                    BasePlayer teammate = PlayerUtil.FindById(teammateId);
                    if (teammate != null && teammate.IsConnected)
                    {
                        MessagePlayer(teammate, Lang.FailedAttempt, intruder.displayName, MapHelper.PositionToString(codeLock.transform.position));
                        notifiedPlayers.Add(teammateId);
                    }
                }
            }
        }

        private void SendAlertToDiscord(CodeLock codeLock, BasePlayer intruder)
        {
            ulong ownerId = codeLock.OwnerID;
            BasePlayer owner = PlayerUtil.FindById(ownerId);
            string ownerName;
            string ownerSteamId;

            if (owner != null)
            {
                ownerName = owner.displayName;
                ownerSteamId = owner.UserIDString;
            }
            else
            {
                ownerName = ownerId.ToString();
                ownerSteamId = ownerId.ToString();
            }

            string intruderName = intruder.displayName;
            string intruderSteamId = intruder.UserIDString;
            string location = MapHelper.PositionToString(codeLock.transform.position);

            string messageTemplate = lang.GetMessage(Lang.DiscordAlertMessage, this);
            string message = string.Format(messageTemplate, intruderName, intruderSteamId, ownerName, ownerSteamId, location);

            string payload = JsonConvert.SerializeObject(new { content = message });

            webrequest.Enqueue(
                _config.DiscordWebhookUrl,
                payload,
                (code, response) =>
                {
                    if (code >= 200 && code < 300)
                    {
                        Puts("Alert sent to Discord successfully.");
                    }
                    else
                    {
                        Puts("Failed to send alert to Discord. Code: " + code + ", Response: " + response);
                    }
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string> { { "Content-Type", "application/json" } }
            );
        }

        #endregion Notifications

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "failedcodealert.ignore";
            public const string USE = "failedcodealert.use";
            private static readonly List<string> _permissions = new List<string>
            {
                USE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Helper Classes

        public static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static RelationshipManager.PlayerTeam GetTeam(ulong playerId)
            {
                if (RelationshipManager.ServerInstance == null)
                    return null;

                return RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            }
        }

        public static class DataFileUtil
        {
            private const string FOLDER = "";

            public static string GetFilePath(string filename = null)
            {
                if (filename == null)
                    filename = _plugin.Name;

                return Path.Combine(FOLDER, filename);
            }

            public static string[] GetAllFilePaths()
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(FOLDER);
                for (int i = 0; i < filePaths.Length; i++)
                {
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);
                }

                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        #endregion Helper Classes

        #region Commands

        [ChatCommand("codealert")]
        private void cmdCodeAlert(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                MessagePlayer(player, Lang.NoPermission);
                return;
            }

            ulong userId = player.userID;
            bool currentState;

            if (_storedData.IntrusionAlerts.TryGetValue(userId, out bool state))
                currentState = state;
            else
                currentState = true;

            _storedData.IntrusionAlerts[userId] = !currentState;
            DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);

            string messageKey;
            if (currentState)
                messageKey = Lang.AlertsDisabled;
            else
                messageKey = Lang.AlertsEnabled;

            MessagePlayer(player, messageKey);
        }

        #endregion Commands

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string FailedAttempt = "FailedAttempt";
            public const string AlertsEnabled = "AlertsEnabled";
            public const string AlertsDisabled = "AlertsDisabled";
            public const string DiscordAlertMessage = "DiscordAlertMessage";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",
                [Lang.FailedAttempt] = "Someone is trying to access your code lock at {1} but they failed! Intruder: {0}",
                [Lang.AlertsEnabled] = "Code lock intrusion alerts enabled!",
                [Lang.AlertsDisabled] = "Code lock intrusion alerts disabled!",
                [Lang.DiscordAlertMessage] = "Intrusion alert: {0} ({1}) attempted to access code lock owned by {2} ({3}) at {4}"
            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        #endregion Localization
    }
}