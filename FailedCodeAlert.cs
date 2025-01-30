/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core;
using System.Collections.Generic;
using System.IO;

namespace Oxide.Plugins
{
    [Info("Failed Code Alert", "VisEntities", "1.1.0")]
    [Description("Warns players when someone tries to access their code lock with the wrong code.")]
    public class FailedCodeAlert : RustPlugin
    {
        #region Fields

        private static FailedCodeAlert _plugin;
        private StoredData _storedData;

        #endregion Fields

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

        #endregion Notifications

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "failedcodealert.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
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
            public const string FailedAttempt = "FailedAttempt";
            public const string AlertsEnabled = "AlertsEnabled";
            public const string AlertsDisabled = "AlertsDisabled";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.FailedAttempt] = "Someone is trying to access your code lock at {1} but they failed! Intruder: {0}",
                [Lang.AlertsEnabled] = "Code lock intrusion alerts enabled!",
                [Lang.AlertsDisabled] = "Code lock intrusion alerts disabled!"
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