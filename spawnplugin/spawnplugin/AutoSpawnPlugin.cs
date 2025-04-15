using Rocket.API;
using Rocket.Unturned.Player;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Rocket.Core.Plugins;
using Rocket.Core.Commands;
using Rocket.Unturned.Events;
using Rocket.Core;
using System;
using SDG.NetTransport;
using static StrefaOdrodzenia.AutoSpawnPlugin;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using StrefaOdrodzenia;

/// Any problems? ///
/// Contact me on Discord:macwoof.exe///

namespace StrefaOdrodzenia
{
    public class AutoSpawnPlugin : RocketPlugin<PluginConfiguration>
    {
        public static AutoSpawnPlugin Instance;
        public Dictionary<Steamworks.CSteamID, List<Vector3>> Points = new Dictionary<Steamworks.CSteamID, List<Vector3>>();
        public Dictionary<Steamworks.CSteamID, TrappedPlayerData> TrappedPlayers = new Dictionary<Steamworks.CSteamID, TrappedPlayerData>();
        private Harmony harmony;

        protected override void Load()
        {
            Instance = this;
            harmony = new Harmony("com.yourplugin.strefaodrodzenia");

            try
            {
                // Patch methods individually instead of using PatchAll
                var inventoryPatch = typeof(InventoryPatches.InventoryOpenPatch);
                var interactPatch = typeof(InventoryPatches.InteractPatch);
                var useItemPatch = typeof(InventoryPatches.UseItemPatch);

                harmony.Patch(
                    original: AccessTools.Method(typeof(PlayerInventory), "ServerOpenStorage"),
                    prefix: new HarmonyMethod(inventoryPatch.GetMethod("Prefix"))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(PlayerInteract), "ServerSimulate"),
                    prefix: new HarmonyMethod(interactPatch.GetMethod("Prefix"))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(PlayerEquipment), "use"),
                    prefix: new HarmonyMethod(useItemPatch.GetMethod("Prefix"))
                );

                Rocket.Core.Logging.Logger.Log("Successfully applied Harmony patches!");
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogError($"Error applying Harmony patches: {e}");
            }

            if (Configuration.Instance.Zones == null)
            {
                Configuration.Instance.Zones = new List<ZoneConfiguration>();
                Configuration.Save();
            }

            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;
            PlayerEquipment.OnUseableChanged_Global += OnEquipmentChanged;

            Rocket.Core.Logging.Logger.Log("###StrefaOdrodzenia Loaded!");
        }

        protected override void Unload()
        {
            foreach (var data in TrappedPlayers.Values)
            {
                if (data.Coroutine != null)
                    StopCoroutine(data.Coroutine);

                if (data.Player != null && !data.Player.Player.life.isDead)
                {
                    EffectManager.askEffectClearByID(8490, data.Player.Player.channel.owner.transportConnection);
                }
            }

            harmony?.UnpatchAll();
            harmony = null;
            TrappedPlayers.Clear();

            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
            PlayerEquipment.OnUseableChanged_Global -= OnEquipmentChanged;

            Rocket.Core.Logging.Logger.Log("###StrefaOdrodzenia Unloaded");
        }

        private void LockInventoryState(UnturnedPlayer player)
        {
            if (player?.Player?.inventory == null) return;

            try
            {
                player.Player.inventory.isStoring = true;
                player.Player.equipment.sendUpdateState();
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError($"Inventory Block Error {ex}");
            }
        }

        private void ForceInventoryLock(UnturnedPlayer player)
        {
            if (player?.Player?.equipment == null) return;

            try
            {
                player.Player.equipment.dequip();
                player.Player.equipment.isBusy = true;
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError("Inventory Block Error : " + ex);
            }
        }

        private void ForceClothingOff(UnturnedPlayer player)
        {
            if (player?.Player?.clothing == null) return;

            try
            {
                player.Player.clothing.askWearBackpack(0, 0, new byte[0], true);
                player.Player.clothing.askWearGlasses(0, 0, new byte[0], true);
                player.Player.clothing.askWearHat(0, 0, new byte[0], true);
                player.Player.clothing.askWearMask(0, 0, new byte[0], true);
                player.Player.clothing.askWearPants(0, 0, new byte[0], true);
                player.Player.clothing.askWearShirt(0, 0, new byte[0], true);
                player.Player.clothing.askWearVest(0, 0, new byte[0], true);
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError("Taking Off Clotches Error: " + ex.ToString());
            }
        }

        private void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, Steamworks.CSteamID murderer)
        {
            if (Configuration.Instance.Zones.Count == 0) return;

            player.Player.life.askRespawn(player.CSteamID, true);

            if (!TrappedPlayers.ContainsKey(player.CSteamID))
            {
                TrappedPlayers[player.CSteamID] = new TrappedPlayerData();
            }
            TrappedPlayers[player.CSteamID].IsWaitingForRespawn = true;
        }

        private void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle)
        {
            if (!TrappedPlayers.TryGetValue(player.CSteamID, out var data) || !data.IsWaitingForRespawn)
                return;

            if (player.Player.life.isDead)
                return;

            ZoneConfiguration zone = FindSuitableZoneForPlayer(player, position);
            if (zone != null)
            {
                Vector3 spawnPos = CalculateSpawnPosition(zone);
                player.Teleport(spawnPos, 0);

                if (zone.Bypass != "t")
                {
                    TrapPlayer(player, zone);
                }
                else
                {
                    UnturnedChat.Say(player, $"You Spawned at {zone.ZoneName} (bypass)", Color.green);
                }
            }
            data.IsWaitingForRespawn = false;
        }

        private ZoneConfiguration FindSuitableZoneForPlayer(UnturnedPlayer player, Vector3 position)
        {
            ZoneConfiguration nearestZone = null;
            float nearestDistance = float.MaxValue;

            foreach (var zone in Configuration.Instance.Zones)
            {
                if (!string.IsNullOrEmpty(zone.RequiredPermission))
                {
                    if (!Rocket.Core.R.Permissions.HasPermission(player, zone.RequiredPermission))
                    {
                        continue;
                    }
                }

                Vector3 zoneCenter = new Vector3(zone.Center.X, zone.Center.Y, zone.Center.Z);
                float distance = Vector3.Distance(position, zoneCenter);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestZone = zone;
                }
            }
            return nearestZone;
        }

        private Vector3 CalculateSpawnPosition(ZoneConfiguration zone)
        {
            for (int i = 0; i < 10; i++)
            {
                float randomX = UnityEngine.Random.Range(zone.MinX + 1f, zone.MaxX - 1f);
                float randomZ = UnityEngine.Random.Range(zone.MinZ + 1f, zone.MaxZ - 1f);
                Vector3 rayOrigin = new Vector3(randomX, zone.MaxY + 10f, randomZ);

                RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, Mathf.Infinity, RayMasks.BLOCK_COLLISION);

                foreach (var hit in hits)
                {
                    float y = hit.point.y;
                    if (y >= zone.MinY && y <= zone.MaxY)
                    {
                        if (hit.point.x >= zone.MinX && hit.point.x <= zone.MaxX &&
                            hit.point.z >= zone.MinZ && hit.point.z <= zone.MaxZ)
                        {
                            return new Vector3(hit.point.x, y + 0.2f, hit.point.z);
                        }
                    }
                }
            }

            return new Vector3(
                zone.Center.X,
                Mathf.Clamp(zone.Center.Y, zone.MinY, zone.MaxY),
                zone.Center.Z
            );
        }

        public bool IsPlayerTrapped(UnturnedPlayer player)
        {
            return TrappedPlayers.ContainsKey(player.CSteamID) && !player.Player.life.isDead;
        }

        public void TrapPlayer(UnturnedPlayer player, ZoneConfiguration zone)
        {
            if (player.Player.life.isDead) return;

            if (TrappedPlayers.ContainsKey(player.CSteamID))
            {
                ForceReleasePlayer(player);
            }
            player.Player.inventory.isStoring = true;
            player.Player.interact.enabled = false;
            var closeMethod = typeof(PlayerInventory).GetMethod("closeStorage", BindingFlags.Public | BindingFlags.Instance)
                  ?? typeof(PlayerInventory).GetMethod("CloseStorage", BindingFlags.Public | BindingFlags.Instance);
            closeMethod?.Invoke(player.Player.inventory, null);

            EffectManager.sendUIEffect(8490, 8490, player.Player.channel.owner.transportConnection, true);
            EffectManager.sendUIEffectText(8490, player.Player.channel.owner.transportConnection, true, "ileczasu",
                $"Jailed For: {FormatTime(zone.TrapTimeSeconds)}");

            TrappedPlayers[player.CSteamID] = new TrappedPlayerData
            {
                Zone = zone,
                Player = player,
                StartTime = Time.time,
                Coroutine = StartCoroutine(MonitorTrappedPlayer(player, zone)),
                EffectId = 8490,
                EffectKey = 8490
            };

            UnturnedChat.Say(player, $"You got jailed in zone {zone.ZoneName} for {FormatTime(zone.TrapTimeSeconds)}!", Color.yellow);
        }

        public string FormatTime(float seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.Minutes > 0)
            {
                return $"{timeSpan.Minutes} m{(timeSpan.Minutes != 1 ? "y" : "a")} i {timeSpan.Seconds} s{(timeSpan.Seconds != 1 ? "y" : "a")}";
            }
            return $"{timeSpan.Seconds} seconds{(timeSpan.Seconds != 1 ? "y" : "a")}";
        }

        private void UpdateUITimer(UnturnedPlayer player, float remainingTime, float totalTime)
        {
            string formattedTime = $"{FormatUITime(remainingTime)} / {FormatUITime(totalTime)}";
            EffectManager.askEffectClearByID(8490, player.Player.channel.owner.transportConnection);
            EffectManager.sendUIEffect(8490, 8490, player.Player.channel.owner.transportConnection, true,
                "ileczasu",
                formattedTime);
        }

        private string FormatUITime(float seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            return $"{timeSpan.Minutes}:{timeSpan.Seconds:00}";
        }

        private IEnumerator MonitorTrappedPlayer(UnturnedPlayer player, ZoneConfiguration zone)
        {
            float trappedTime = 0f;
            int lastDisplayedTime = zone.TrapTimeSeconds;
            WaitForSeconds wait = new WaitForSeconds(0.3f);

            while (trappedTime < zone.TrapTimeSeconds && player != null && !player.Player.life.isDead)
            {
                if (TrappedPlayers.TryGetValue(player.CSteamID, out var data) && data != null)
                {
                    if (trappedTime % 3f < 0.3f)
                    {
                        ForceClothingOff(player);
                    }

                    CheckPlayerPosition(player, data.Zone);
                    trappedTime += 0.3f;

                    int currentTime = zone.TrapTimeSeconds - (int)trappedTime;
                    if (currentTime != lastDisplayedTime)
                    {
                        lastDisplayedTime = currentTime;
                        EffectManager.sendUIEffectText(8490, player.Player.channel.owner.transportConnection, true, "ileczasu",
                            $"Jailed For: {FormatTime(currentTime)}");
                    }
                }
                yield return wait;
            }

            if (player != null && !player.Player.life.isDead)
            {
                UnturnedChat.Say(player, "Your time is up!", Color.green);
                EffectManager.askEffectClearByID(8490, player.Player.channel.owner.transportConnection);
            }
            ForceReleasePlayer(player);
        }

        private void CheckPlayerPosition(UnturnedPlayer player, ZoneConfiguration zone)
        {
            if (player.Player.life.isDead) return;

            Vector3 position = player.Position;
            float tolerance = 0.1f;

            bool isInside =
                position.x >= zone.MinX - tolerance && position.x <= zone.MaxX + tolerance &&
                position.z >= zone.MinZ - tolerance && position.z <= zone.MaxZ + tolerance &&
                position.y >= zone.MinY - tolerance && position.y <= zone.MaxY + tolerance;

            if (!isInside)
            {
                Vector3 newPos = new Vector3(
                    Mathf.Clamp(position.x, zone.MinX + 0.5f, zone.MaxX - 0.5f),
                    Mathf.Clamp(position.y, zone.MinY + 0.5f, zone.MaxY - 0.5f),
                    Mathf.Clamp(position.z, zone.MinZ + 0.5f, zone.MaxZ - 0.5f)
                );

                if (Vector3.Distance(newPos, position) > 2f)
                {
                    newPos = CalculateSpawnPosition(zone);
                }

                player.Teleport(newPos, player.Rotation);
                UnturnedChat.Say(player, "You can't leave the zone!", Color.red);
            }
        }

        private void OnEquipmentChanged(PlayerEquipment equipment)
        {
            UnturnedPlayer player = UnturnedPlayer.FromPlayer(equipment.player);
            if (IsPlayerTrapped(player))
            {
                equipment.dequip();
                UnturnedChat.Say(player, "You cannot use items in the zone!", Color.red);
            }
        }

        public void ForceReleasePlayer(UnturnedPlayer player, bool sendMessage = true, bool isAdminRelease = false)
        {
            if (TrappedPlayers.TryGetValue(player.CSteamID, out var data))
            {
                if (data.Coroutine != null)
                    StopCoroutine(data.Coroutine);

                EffectManager.askEffectClearByID(8490, player.Player.channel.owner.transportConnection);
                TrappedPlayers.Remove(player.CSteamID);

                if (sendMessage)
                {
                    if (isAdminRelease)
                    {
                        UnturnedChat.Say(player, "You were released via command", Color.green);
                    }
                    else if (!player.Player.life.isDead)
                    {
                        UnturnedChat.Say(player, "You were released!", Color.green);
                    }
                }
            }
        }

        public bool ReleasePlayer(UnturnedPlayer admin, UnturnedPlayer target)
        {
            if (!IsPlayerTrapped(target))
            {
                UnturnedChat.Say(admin, "Not Jailed", Color.red);
                return false;
            }

            ForceReleasePlayer(target, true, true);
            UnturnedChat.Say(admin, $"You Released {target.DisplayName} from zone!", Color.green);
            return true;
        }

        public class PluginConfiguration : IRocketPluginConfiguration
        {
            public int DefaultTrapTimeSeconds = 20;
            public List<ZoneConfiguration> Zones = new List<ZoneConfiguration>();

            public void LoadDefaults()
            {
                DefaultTrapTimeSeconds = 20;
                Zones = new List<ZoneConfiguration>();
            }
        }

        public class ZoneConfiguration
        {
            public string ZoneName = "Strefa Odrodzenia";
            public int TrapTimeSeconds = 20;
            public float MinX;
            public float MinZ;
            public float MaxX;
            public float MaxZ;
            public float MinY;
            public float MaxY;
            public SimpleVector3 Center;
            public string RequiredPermission = "";
            public string Bypass = "n";
        }
        public class TrappedPlayerData
        {
            public Coroutine Coroutine;
            public ZoneConfiguration Zone;
            public UnturnedPlayer Player;
            public float StartTime;
            public bool IsWaitingForRespawn;
            public ushort EffectId = 8490;
            public short EffectKey = 8490;
        }

        public class SimpleVector3
        {
            public float X;
            public float Y;
            public float Z;

            public SimpleVector3() { }
            public SimpleVector3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
    }
}