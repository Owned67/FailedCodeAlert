/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Failed Code Alert", "VisEntities", "1.0.0")]
    [Description("Warns players when someone tries to access their code lock with the wrong code.")]
    public class FailedCodeAlert : RustPlugin
    {
        #region Fields

        private static FailedCodeAlert _plugin;

        #endregion Fields

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
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

        #endregion Helper Classes

        #region Localization

        private class Lang
        {
            public const string FailedAttempt = "FailedAttempt";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.FailedAttempt] = "Someone is trying to access your code lock at {1} but they failed! Intruder: {0}"
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