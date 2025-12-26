using System;
using System.Collections.Generic;
using Oxide.Core;
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
        private const string Msg_NoHarvest = Prefix + "You can't harvest this. It's not yours or your teams.";

        private readonly Dictionary<ulong, float> lastInteractionMessage = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastDamageMessage = new Dictionary<ulong, float>();
        private readonly List<ToggleGroup> toggleGroupList = new List<ToggleGroup>();
        private readonly Dictionary<string, ToggleGroup> toggleGroupLookup = new Dictionary<string, ToggleGroup>(StringComparer.OrdinalIgnoreCase);
        private StoredData storedData;

        private class StoredData
        {
            public Dictionary<ulong, HashSet<string>> EnabledGroupsByOwner = new Dictionary<ulong, HashSet<string>>();
        }

        private class ToggleItem
        {
            public string DisplayName { get; private set; }
            public string PrefabName { get; private set; }
            public string PrefabKey { get; private set; }

            public ToggleItem(string displayName, string prefabName)
            {
                DisplayName = displayName;
                PrefabName = prefabName;
                PrefabKey = prefabName.ToLowerInvariant();
            }
        }

        private class ToggleGroup
        {
            public string Name { get; private set; }
            public bool Enabled { get; set; }
            public List<ToggleItem> Items { get; private set; }
            public HashSet<string> PrefabKeys { get; private set; }

            public ToggleGroup(string name, IEnumerable<ToggleItem> items)
            {
                Name = name;
                Items = new List<ToggleItem>(items);
                PrefabKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ToggleItem item in Items)
                {
                    PrefabKeys.Add(item.PrefabKey);
                }
            }
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionBypass, this);
            LoadData();
            RegisterToggleGroups();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        private void LoadData()
        {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            if (storedData.EnabledGroupsByOwner == null)
                storedData.EnabledGroupsByOwner = new Dictionary<ulong, HashSet<string>>();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
        }

        private void RegisterToggleGroups()
        {
            toggleGroupList.Clear();
            toggleGroupLookup.Clear();

            AddToggleGroup("Building", new[]
            {
                new ToggleItem("Tool Cupboard", "cupboard.tool")
            });

            AddToggleGroup("Comfort", new[]
            {
                new ToggleItem("Bed", "bed"),
                new ToggleItem("Chair", "chair"),
                new ToggleItem("Sofa", "sofa"),
                new ToggleItem("Sofa Pattern", "sofa.pattern"),
                new ToggleItem("BBQ", "bbq"),
                new ToggleItem("Camp Fire", "campfire"),
                new ToggleItem("Electric Heater", "electric.heater")
            });

            AddToggleGroup("Crafting", new[]
            {
                new ToggleItem("Repair Bench", "box.repair.bench"),
                new ToggleItem("Repair Bench", "repairbench"),
                new ToggleItem("Research Table", "research.table"),
                new ToggleItem("Research Table", "researchtable"),
                new ToggleItem("Mixing Table", "mixingtable"),
                new ToggleItem("Composter", "composter")
            });

            AddToggleGroup("Farm", new[]
            {
                new ToggleItem("Bathtub Planter", "bathtub.planter"),
                new ToggleItem("Large Planter", "planter.large"),
                new ToggleItem("Minecart Planter", "minecart.planter"),
                new ToggleItem("Rail Road Planter", "rail.road.planter"),
                new ToggleItem("Small Planter", "planter.small"),
                new ToggleItem("Triangle Planter", "planter.triangle"),
                new ToggleItem("Triangle Rail Road Planter", "triangle.rail.road.planter"),
                new ToggleItem("Hitch & Trough", "hitchtrough")
            });

            AddToggleGroup("Furnace", new[]
            {
                new ToggleItem("Electric Furnace", "electric.furnace"),
                new ToggleItem("Furnace", "furnace"),
                new ToggleItem("Large Furnace", "furnace.large"),
                new ToggleItem("Small Oil Refinery", "small.oil.refinery")
            });

            AddToggleGroup("Storage", new[]
            {
                new ToggleItem("Storage Box", "box.wooden"),
                new ToggleItem("Large Wood Box", "box.wooden.large"),
                new ToggleItem("Locker", "locker"),
                new ToggleItem("Drop Box", "dropbox"),
                new ToggleItem("Mailbox", "mailbox"),
                new ToggleItem("Fridge", "fridge"),
                new ToggleItem("Mini Fridge", "mini fridge")
            });

            AddToggleGroup("Switch", new[]
            {
                new ToggleItem("Electric Switch", "electrical.switch"),
                new ToggleItem("Electric Switch", "switch")
            });

            AddToggleGroup("Water", new[]
            {
                new ToggleItem("Water Catcher (Small)", "water.catcher.small"),
                new ToggleItem("Water Catcher (Large)", "water.catcher.large"),
                new ToggleItem("Water Barrel", "water.barrel")
            });
        }

        private void AddToggleGroup(string name, IEnumerable<ToggleItem> items)
        {
            ToggleGroup group = new ToggleGroup(name, items);
            toggleGroupList.Add(group);
            toggleGroupLookup[name] = group;
        }

        private HashSet<string> GetOwnerGroups(ulong ownerId)
        {
            HashSet<string> groups;
            if (!storedData.EnabledGroupsByOwner.TryGetValue(ownerId, out groups))
            {
                groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                storedData.EnabledGroupsByOwner[ownerId] = groups;
            }

            return groups;
        }

        private bool IsGroupEnabledForOwner(ulong ownerId, string groupName)
        {
            HashSet<string> groups = GetOwnerGroups(ownerId);
            return groups.Contains(groupName);
        }

        private void SetGroupEnabledForOwner(ulong ownerId, string groupName, bool enabled)
        {
            HashSet<string> groups = GetOwnerGroups(ownerId);
            if (enabled)
                groups.Add(groupName);
            else
                groups.Remove(groupName);
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
         * COMMANDS
         * ========================= */

        [ChatCommand("pve")]
        private void PveCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (args.Length == 0)
            {
                player.ChatMessage(Prefix + "Usage: /pve toggle <group>");
                return;
            }

            if (!args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                player.ChatMessage(Prefix + "Unknown command. Use /pve toggle to list groups.");
                return;
            }

            if (args.Length == 1)
            {
                ListToggleGroups(player);
                return;
            }

            if (args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length == 2)
                {
                    SetAllToggleGroups(player.userID, true);
                    player.ChatMessage(Prefix + "All groups toggled on.");
                    return;
                }

                bool enableAll;
                if (TryParseToggleState(args[2], out enableAll))
                {
                    SetAllToggleGroups(player.userID, enableAll);
                    string stateLabel = enableAll ? "on" : "off";
                    player.ChatMessage(string.Format("{0}All groups toggled {1}.", Prefix, stateLabel));
                    return;
                }

                player.ChatMessage(Prefix + "Usage: /pve toggle all <on|off>");
                return;
            }

            string groupName = string.Join(" ", args, 1, args.Length - 1);
            ToggleGroup group;
            if (!toggleGroupLookup.TryGetValue(groupName, out group))
            {
                player.ChatMessage(Prefix + "Unknown toggle group. Use /pve toggle to list groups.");
                return;
            }

            bool enabled = !IsGroupEnabledForOwner(player.userID, group.Name);
            SetGroupEnabledForOwner(player.userID, group.Name, enabled);
            SaveData();
            string status = enabled ? "on" : "off";
            string accessMessage = enabled ? "can now access" : "can no longer access";
            player.ChatMessage(string.Format("{0}Group {1} toggled {2}, players {3} these items.", Prefix, group.Name, status, accessMessage));
        }

        private void ListToggleGroups(BasePlayer player)
        {
            player.ChatMessage(Prefix + "Available toggle groups:");
            string allStatus = AreAllGroupsEnabled(player.userID) ? "on" : "off";
            player.ChatMessage(string.Format("{0}All ({1})", Prefix, allStatus));
            foreach (ToggleGroup group in GetSortedToggleGroups())
            {
                string status = IsGroupEnabledForOwner(player.userID, group.Name) ? "on" : "off";
                player.ChatMessage(string.Format("{0}{1} ({2})", Prefix, group.Name, status));
            }
        }

        private bool AreAllGroupsEnabled(ulong ownerId)
        {
            foreach (ToggleGroup group in toggleGroupList)
            {
                if (!IsGroupEnabledForOwner(ownerId, group.Name))
                    return false;
            }

            return toggleGroupList.Count > 0;
        }

        private void SetAllToggleGroups(ulong ownerId, bool enabled)
        {
            foreach (ToggleGroup group in toggleGroupList)
            {
                SetGroupEnabledForOwner(ownerId, group.Name, enabled);
            }

            SaveData();
        }

        private bool TryParseToggleState(string value, out bool enabled)
        {
            enabled = false;
            if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                return true;
            }

            if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private List<ToggleGroup> GetSortedToggleGroups()
        {
            List<ToggleGroup> groups = new List<ToggleGroup>(toggleGroupList);
            groups.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return groups;
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
            if (entity == null)
                return false;

            if (entity is BuildingPrivlidge)
                return true;

            if (!string.IsNullOrEmpty(entity.PrefabName) &&
                entity.PrefabName.IndexOf("/deployable/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return entity.OwnerID != 0;
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

        private bool IsToggleAccess(BaseEntity entity)
        {
            if (entity == null)
                return false;

            ulong ownerId = GetOwnerId(entity);
            if (ownerId == 0)
                return false;

            string shortName = entity.ShortPrefabName;
            string prefabName = entity.PrefabName;
            if (string.IsNullOrEmpty(shortName))
                shortName = entity.PrefabName;
            if (string.IsNullOrEmpty(shortName))
                shortName = string.Empty;
            else
                shortName = shortName.ToLowerInvariant();

            if (string.IsNullOrEmpty(prefabName))
                prefabName = string.Empty;
            else
                prefabName = prefabName.ToLowerInvariant();

            string normalizedShortName = NormalizePrefabKey(shortName);
            string normalizedPrefabName = NormalizePrefabKey(prefabName);
            foreach (ToggleGroup group in toggleGroupList)
            {
                if (!IsGroupEnabledForOwner(ownerId, group.Name))
                    continue;

                foreach (string prefabKey in group.PrefabKeys)
                {
                    string normalizedKey = NormalizePrefabKey(prefabKey);
                    if (prefabKey.Equals(shortName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (!string.IsNullOrEmpty(normalizedKey) &&
                        normalizedKey.Equals(normalizedShortName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (prefabName.IndexOf(prefabKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    if (!string.IsNullOrEmpty(normalizedKey) &&
                        normalizedPrefabName.IndexOf(normalizedKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        private string NormalizePrefabKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] buffer = new char[value.Length];
            int index = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsLetterOrDigit(current))
                {
                    buffer[index] = char.ToLowerInvariant(current);
                    index++;
                }
            }

            return index == 0 ? string.Empty : new string(buffer, 0, index);
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

            if (IsToggleAccess(entity))
                return null;

            if (HasBuildingAccess(player, entity))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object OnEntityUse(BasePlayer player, BaseEntity entity, ulong usage)
        {
            if (player == null || entity == null)
                return null;

            if (!(entity is ResearchTable) && !(entity is LiquidContainer))
                return null;

            if (IsToggleAccess(entity))
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

            if (IsToggleAccess(entity))
                return null;

            if (IsHumanNPC(entity) || !IsPlayerPlaced(entity))
                return null;

            if (HasBuildingAccess(player, entity))
                return null;

            BuildingPrivlidge privilege = entity as BuildingPrivlidge;
            if (privilege != null)
            {
                NotifyInteraction(player, Msg_NoTC);
                return false;
            }

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanDrink(BasePlayer player, LiquidContainer container)
        {
            if (container == null)
                return null;

            if (IsToggleAccess(container))
                return null;

            if (IsHumanNPC(container) || !IsPlayerPlaced(container))
                return null;

            if (HasBuildingAccess(player, container))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanTakeCutting(BasePlayer player, GrowableEntity growable)
        {
            if (player == null || growable == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object OnGrowableGather(GrowableEntity growable, Item item, BasePlayer player)
        {
            if (player == null || growable == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object OnGrowableGather(GrowableEntity growable, BasePlayer player)
        {
            if (player == null || growable == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object OnGrowableHarvest(GrowableEntity growable, BasePlayer player)
        {
            if (player == null || growable == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
        {
            if (player == null || growable == null || item == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object CanHarvestEntity(BasePlayer player, BaseEntity entity)
        {
            GrowableEntity growable = entity as GrowableEntity;
            if (growable == null)
                return null;

            return CropsProtected(player, growable);
        }

        private object CropsProtected(BasePlayer player, GrowableEntity growable)
        {
            BaseEntity planter = growable.GetParentEntity();
            if (planter == null)
                return null;

            if (IsToggleAccess(planter))
                return null;

            ulong ownerId = GetOwnerId(planter);
            if (ownerId != 0)
            {
                if (ownerId == player.userID || SameTeam(player, ownerId))
                    return null;
            }
            else if (HasBuildingAccess(player, planter))
                return null;

            player.ChatMessage(Msg_NoHarvest);
            return true;
        }

        private object OnSwitchToggle(IOEntity entity, BasePlayer player)
        {
            if (entity == null || player == null)
                return null;

            if (IsToggleAccess(entity))
                return null;

            if (IsHumanNPC(entity) || !IsPlayerPlaced(entity))
                return null;

            if (HasBuildingAccess(player, entity))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable mountable)
        {
            if (mountable == null)
                return null;

            if (IsToggleAccess(mountable))
                return null;

            if (IsHumanNPC(mountable) || !IsPlayerPlaced(mountable))
                return null;

            if (HasBuildingAccess(player, mountable))
                return null;

            NotifyInteraction(player, Msg_NoAccess);
            return false;
        }

        private object OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (IsToggleAccess(privilege))
                return null;

            if (HasBuildingAccess(player, privilege))
                return null;

            NotifyInteraction(player, Msg_NoTC);
            return false;
        }

        private object OnCupboardClearList(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (IsToggleAccess(privilege))
                return null;

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

            if (IsToggleAccess(entity))
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

            if (SameTeam(looter, target.userID))
                return null;

            NotifyInteraction(looter, Msg_NoSleeperLoot);
            return false;
        }
    }
}
