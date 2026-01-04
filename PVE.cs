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
        private const float ToggleRaycastDistance = 2f;

        private readonly Dictionary<string, List<string>> legacyGroupItems = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private StoredData storedData;

        private class StoredData
        {
            public Dictionary<ulong, HashSet<string>> SharedItemsByOwner = new Dictionary<ulong, HashSet<string>>();
            public Dictionary<ulong, HashSet<string>> EnabledGroupsByOwner = new Dictionary<ulong, HashSet<string>>();
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionBypass, this);
            RegisterLegacyGroupItems();
            LoadData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        private void LoadData()
        {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            if (storedData.SharedItemsByOwner == null)
                storedData.SharedItemsByOwner = new Dictionary<ulong, HashSet<string>>();
            if (storedData.EnabledGroupsByOwner == null)
                storedData.EnabledGroupsByOwner = new Dictionary<ulong, HashSet<string>>();

            MigrateLegacyToggleGroups();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
        }

        private void RegisterLegacyGroupItems()
        {
            legacyGroupItems.Clear();

            AddToggleItems("Building", new[]
            {
                "cupboard.tool"
            });

            AddToggleItems("Comfort", new[]
            {
                "bed",
                "chair",
                "sofa",
                "sofa.pattern",
                "bbq",
                "campfire",
                "electric.heater"
            });

            AddToggleItems("Crafting", new[]
            {
                "box.repair.bench",
                "repairbench",
                "research.table",
                "researchtable",
                "mixingtable",
                "composter"
            });

            AddToggleItems("Farm", new[]
            {
                "bathtub.planter",
                "planter.large",
                "minecart.planter",
                "rail.road.planter",
                "planter.small",
                "planter.triangle",
                "triangle.rail.road.planter",
                "hitchtrough"
            });

            AddToggleItems("Furnace", new[]
            {
                "electric.furnace",
                "furnace",
                "furnace.large",
                "small.oil.refinery"
            });

            AddToggleItems("Storage", new[]
            {
                "box.wooden",
                "box.wooden.large",
                "locker",
                "dropbox",
                "mailbox",
                "fridge",
                "mini fridge"
            });

            AddToggleItems("Switch", new[]
            {
                "electrical.switch",
                "switch"
            });

            AddToggleItems("Water", new[]
            {
                "water.catcher.small",
                "water.catcher.large",
                "water.barrel"
            });
        }

        private void AddToggleItems(string legacyGroupName, IEnumerable<string> items)
        {
            List<string> legacyItems;
            if (!legacyGroupItems.TryGetValue(legacyGroupName, out legacyItems))
            {
                legacyItems = new List<string>();
                legacyGroupItems[legacyGroupName] = legacyItems;
            }

            foreach (string item in items)
            {
                legacyItems.Add(item.ToLowerInvariant());
            }
        }

        private HashSet<string> GetOwnerItems(ulong ownerId)
        {
            HashSet<string> items;
            if (!storedData.SharedItemsByOwner.TryGetValue(ownerId, out items))
            {
                items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                storedData.SharedItemsByOwner[ownerId] = items;
            }

            return items;
        }

        private bool IsItemSharedForOwner(ulong ownerId, string itemKey)
        {
            HashSet<string> items = GetOwnerItems(ownerId);
            return items.Contains(itemKey);
        }

        private void SetItemSharedForOwner(ulong ownerId, string itemKey, bool enabled)
        {
            HashSet<string> items = GetOwnerItems(ownerId);
            if (enabled)
                items.Add(itemKey);
            else
                items.Remove(itemKey);
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
                player.ChatMessage(Prefix + "Usage: /pve toggle [item.shortname]");
                return;
            }

            if (!args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                player.ChatMessage(Prefix + "Unknown command. Use /pve toggle to manage shared items.");
                return;
            }

            if (args.Length == 1)
            {
                string toggleMessage;
                if (TryToggleLookedItem(player, out toggleMessage))
                {
                    player.ChatMessage(toggleMessage);
                    return;
                }

                ListSharedItems(player);
                return;
            }

            string itemKey = string.Join(" ", args, 1, args.Length - 1);
            HashSet<string> sharedItems = GetOwnerItems(player.userID);
            if (sharedItems.Remove(itemKey))
            {
                SaveData();
                player.ChatMessage(string.Format("{0}Stopped sharing {1}.", Prefix, GetItemDisplayName(itemKey)));
                return;
            }

            player.ChatMessage(string.Format("{0}{1} is not currently shared.", Prefix, GetItemDisplayName(itemKey)));
        }

        private void ListSharedItems(BasePlayer player)
        {
            HashSet<string> sharedItems = GetOwnerItems(player.userID);
            if (sharedItems.Count == 0)
            {
                player.ChatMessage(Prefix + "You are not sharing any items.");
                return;
            }

            player.ChatMessage(Prefix + "You are sharing:");
            List<string> entries = new List<string>();
            foreach (string itemKey in sharedItems)
            {
                string displayName = GetItemDisplayName(itemKey);
                entries.Add(string.Format("{0} ({1})", displayName, itemKey));
            }

            entries.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                player.ChatMessage(string.Format("{0}{1}", Prefix, entry));
            }
        }

        private bool TryToggleLookedItem(BasePlayer player, out string message)
        {
            message = null;

            BaseEntity entity;
            if (!TryGetLookEntity(player, out entity))
                return false;

            string itemKey;
            string displayName;
            if (!TryGetShareKeyForEntity(entity, out itemKey, out displayName))
                return false;

            bool enabled = !IsItemSharedForOwner(player.userID, itemKey);
            SetItemSharedForOwner(player.userID, itemKey, enabled);
            SaveData();

            string status = enabled ? "now sharing" : "no longer sharing";
            message = string.Format("{0}You are {1} {2}.", Prefix, status, displayName);
            return true;
        }

        private bool TryGetLookEntity(BasePlayer player, out BaseEntity entity)
        {
            entity = null;
            if (player == null)
                return false;

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, ToggleRaycastDistance,
                Layers.Mask.Deployed | Layers.Mask.Default | Layers.Mask.Construction))
                return false;

            entity = hit.GetEntity();
            return entity != null;
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

        private bool IsUnownedDeployable(BaseEntity entity)
        {
            if (entity == null)
                return false;

            if (entity.OwnerID != 0)
                return false;

            return !string.IsNullOrEmpty(entity.PrefabName) &&
                entity.PrefabName.IndexOf("/deployable/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsUnownedPlanter(BaseEntity entity)
        {
            if (entity == null)
                return false;

            if (GetOwnerId(entity) != 0)
                return false;

            if (entity is PlanterBox)
                return true;

            string shortName = entity.ShortPrefabName ?? string.Empty;
            if (shortName.IndexOf("planter", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string prefabName = entity.PrefabName ?? string.Empty;
            return prefabName.IndexOf("planter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsUnownedIoEntity(BaseEntity entity)
        {
            if (!(entity is IOEntity))
                return false;

            return GetOwnerId(entity) == 0;
        }

        private bool IsHumanNPC(BaseEntity entity)
        {
            BasePlayer player = entity as BasePlayer;
            if (player == null)
                return false;

            return !player.IsConnected && player.userID < 10000000000000000UL;
        }

        private bool IsNpcLoot(BaseEntity entity)
        {
            LootableCorpse corpse = entity as LootableCorpse;
            if (corpse != null)
            {
                if (corpse.playerSteamID != 0 && corpse.playerSteamID < 10000000000000000UL)
                    return true;

                if (corpse.OwnerID != 0 && corpse.OwnerID < 10000000000000000UL)
                    return true;

                return corpse.playerSteamID == 0 && corpse.OwnerID == 0;
            }

            DroppedItemContainer dropped = entity as DroppedItemContainer;
            if (dropped == null)
            {
                StashContainer stash = entity as StashContainer;
                if (stash == null)
                    return false;

                if (stash.OwnerID != 0 && stash.OwnerID < 10000000000000000UL)
                    return true;

                return stash.OwnerID == 0;
            }

            if (dropped.playerSteamID != 0 && dropped.playerSteamID < 10000000000000000UL)
                return true;

            if (dropped.OwnerID != 0 && dropped.OwnerID < 10000000000000000UL)
                return true;

            return dropped.playerSteamID == 0 && dropped.OwnerID == 0;
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

            HashSet<string> sharedItems = GetOwnerItems(ownerId);
            if (sharedItems.Count == 0)
                return false;

            foreach (string itemKey in sharedItems)
            {
                if (EntityMatchesPrefabKey(entity, itemKey))
                {
                    return true;
                }
            }

            return false;
        }

        private bool EntityMatchesPrefabKey(BaseEntity entity, string prefabKey)
        {
            if (entity == null || string.IsNullOrEmpty(prefabKey))
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

            return false;
        }

        private bool TryGetShareKeyForEntity(BaseEntity entity, out string itemKey, out string displayName)
        {
            itemKey = null;
            displayName = null;
            if (entity == null)
                return false;

            string shortName = entity.ShortPrefabName;
            if (string.IsNullOrEmpty(shortName))
                shortName = entity.PrefabName;

            if (string.IsNullOrEmpty(shortName))
                return false;

            itemKey = shortName.ToLowerInvariant();
            displayName = GetItemDisplayName(itemKey, entity);
            return true;
        }

        private string GetItemDisplayName(string itemKey, BaseEntity entity = null)
        {
            if (string.IsNullOrEmpty(itemKey))
                return "Unknown Item";

            ItemDefinition definition = ItemManager.FindItemDefinition(itemKey);
            if (definition != null && definition.displayName != null)
                return definition.displayName.english;

            if (entity != null && !string.IsNullOrEmpty(entity.ShortPrefabName))
                return entity.ShortPrefabName;

            return itemKey;
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

        private void MigrateLegacyToggleGroups()
        {
            if (storedData == null || storedData.EnabledGroupsByOwner == null)
                return;

            if (storedData.SharedItemsByOwner == null)
                storedData.SharedItemsByOwner = new Dictionary<ulong, HashSet<string>>();

            bool migrated = false;
            foreach (KeyValuePair<ulong, HashSet<string>> entry in storedData.EnabledGroupsByOwner)
            {
                if (entry.Value == null || entry.Value.Count == 0)
                    continue;

                HashSet<string> sharedItems = GetOwnerItems(entry.Key);
                foreach (string groupName in entry.Value)
                {
                    List<string> legacyItems;
                    if (!legacyGroupItems.TryGetValue(groupName, out legacyItems))
                        continue;

                    foreach (string itemKey in legacyItems)
                    {
                        sharedItems.Add(itemKey);
                        migrated = true;
                    }
                }
            }

            if (migrated)
                storedData.EnabledGroupsByOwner = new Dictionary<ulong, HashSet<string>>();
        }

        /* =========================
         * DAMAGE
         * ========================= */

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return null;

            BasePlayer attacker = info.InitiatorPlayer;

            if (attacker != null && entity is GunTrap && entity.OwnerID == 0)
                return null;

            if (attacker != null && entity is StorageContainer && entity.OwnerID == 0)
                return null;

            if (attacker != null && IsUnownedDeployable(entity))
                return null;

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

            if (IsUnownedPlanter(entity))
                return null;

            if (IsNpcLoot(entity))
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

            if (IsUnownedPlanter(entity))
                return null;

            if (IsNpcLoot(entity))
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

            if (IsUnownedPlanter(planter))
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

            if (IsUnownedIoEntity(entity))
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

            if (IsNpcLoot(entity))
                return null;

            if (IsUnownedPlanter(entity))
                return null;

            if (IsUnownedDeployable(entity))
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
