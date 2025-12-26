using System;
using System.Collections.Generic;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PVE", "FadirStave", "1.0.36")]
    public class PVE : RustPlugin
    {
        private const string PermissionBypass = "pve.bypass";

        /* =========================
         * MESSAGE CONFIG
         * ========================= */

        private const float InteractionMessageCooldown = 2f;
        private const float DamageMessageCooldown = 20f;

        private const string Prefix = "<color=#d17a22>[PVE]</color> ";

        private const string Msg_NoAccess = Prefix + "You can't access this. It's not yours or your teams.";
        private const string Msg_NoPickup = Prefix + "You can't pick this. It's not yours or your teams.";
        private const string Msg_NoSleeperLoot = Prefix + "You can't loot this player. They aren't on your team.";
        private const string Msg_NoTC = Prefix + "You can't access this. It's not yours or your teams.";
        private const string Msg_NoDamage = Prefix + "You can't damage this. It's not yours or your teams.";

        private readonly Dictionary<ulong, float> lastInteractionMessage = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastDamageMessage = new Dictionary<ulong, float>();

        private void Init()
        {
            permission.RegisterPermission(PermissionBypass, this);
        }

        private void NotifyInteraction(BasePlayer player, string message)
        {
            float now = Time.realtimeSinceStartup;

            float last;
            if (lastInteractionMessage.TryGetValue(player.userID, out last) &&
                now - last < InteractionMessageCooldown)
            {
                return;
            }

            lastInteractionMessage[player.userID] = now;
            player.ChatMessage(message);
        }

        private void NotifyDamage(BasePlayer player)
        {
            float now = Time.realtimeSinceStartup;

            float last;
            if (lastDamageMessage.TryGetValue(player.userID, out last) &&
                now - last < DamageMessageCooldown)
            {
                return;
            }

            lastDamageMessage[player.userID] = now;
            player.ChatMessage(Msg_NoDamage);
        }

        /* =========================
         * HELPERS
         * ========================= */

        private bool SameTeam(BasePlayer player, ulong ownerId)
        {
            if (player == null)
                return false;

            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance == null
                ? null
                : RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);

            if (team == null)
            {
                if (player.currentTeam == 0)
                    return false;

                BasePlayer owner = BasePlayer.FindByID(ownerId);
                if (owner == null)
                    owner = BasePlayer.FindSleeping(ownerId);

                return owner != null && owner.currentTeam == player.currentTeam;
            }

            return team.members.Contains(ownerId);
        }

        private bool HasBypass(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermissionBypass);
        }

        private ulong GetOwnerId(BaseEntity entity)
        {
            if (entity == null)
                return 0;

            if (entity.OwnerID != 0)
                return entity.OwnerID;

            BaseEntity parent = entity.GetParentEntity();
            return parent != null ? parent.OwnerID : 0;
        }

        private bool HasBuildingAccess(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
                return false;

            if (HasBypass(player))
                return true;

            ulong ownerId = GetOwnerId(entity);

            if (ownerId == player.userID)
                return true;

            if (ownerId != 0)
            {
                if (SameTeam(player, ownerId))
                    return true;
            }

            BuildingPrivlidge privilege = entity.GetBuildingPrivilege();
            if (privilege != null && privilege.IsAuthed(player))
                return true;

            return false;
        }

        private bool IsPlayerPlaced(BaseEntity entity)
        {
            return entity != null && entity.OwnerID != 0;
        }

        private bool IsHumanNPC(BaseEntity entity)
        {
            BasePlayer player = entity as BasePlayer;
            if (player == null)
                return false;

            return !player.IsConnected && player.userID < 10000000000000000UL;
        }

        private bool IsVendingMachine(BaseEntity entity)
        {
            return entity is VendingMachine;
        }

        /* =========================
         * DAMAGE
         * ========================= */

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return null;

            BasePlayer attacker = info.InitiatorPlayer;

            if (entity is BaseAnimalNPC || info.Initiator is BaseAnimalNPC)
                return null;

            if (entity is BasePlayer && attacker != null)
            {
                if (IsHumanNPC(entity))
                    return null;

                if (IsHumanNPC(attacker))
                    return null;

                if (info.Initiator is BaseAnimalNPC)
                    return null;

                if (attacker == entity || info.damageTypes.Has(DamageType.Suicide))
                    return null;

                NotifyDamage(attacker);
                return false;
            }

            if (attacker != null && IsPlayerPlaced(entity))
            {
                if (HasBuildingAccess(attacker, entity))
                    return null;

                NotifyDamage(attacker);
                return false;
            }

            return null;
        }

        /* =========================
         * ACCESS / INTERACTION
         * ========================= */

        private object CanInteract(BasePlayer player, BaseEntity entity)
        {
            // ✅ Allow vending machine purchases
            if (IsVendingMachine(entity))
                return null;

            if (IsHumanNPC(entity) || !IsPlayerPlaced(entity))
                return null;

            if (HasBuildingAccess(player, entity))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanUseEntity(BasePlayer player, BaseEntity entity)
        {
            // ✅ Allow vending machine use
            if (IsVendingMachine(entity))
                return null;

            BuildingPrivlidge privilege = entity as BuildingPrivlidge;
            if (privilege != null)
            {
                if (HasBuildingAccess(player, privilege))
                    return null;

                NotifyInteraction(player, Msg_NoTC);
                return false;
            }

            return null;
        }

        private object OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (HasBuildingAccess(player, privilege))
                return null;

            NotifyInteraction(player, Msg_NoTC);
            return false;
        }

        private object CanOpenDoor(BasePlayer player, Door door)
        {
            if (HasBuildingAccess(player, door))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock lockEntity)
        {
            BaseEntity parent = lockEntity.GetParentEntity();
            if (parent != null && HasBuildingAccess(player, parent))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (IsVendingMachine(entity))
                return null; // buying ≠ looting

            if (IsHumanNPC(entity) || !IsPlayerPlaced(entity))
                return null;

            if (HasBuildingAccess(player, entity))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (HasBuildingAccess(player, entity))
                return null;

            NotifyInteraction(player, Msg_NoPickup);
            return false;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (HasBypass(looter))
                return null;

            if (IsHumanNPC(target))
                return null;

            NotifyInteraction(looter, Msg_NoSleeperLoot);
            return false;
        }
    }
}