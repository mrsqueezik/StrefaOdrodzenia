using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using System;
using UnityEngine;
using SDG.Unturned;
using StrefaOdrodzenia;
using HarmonyLib;
using System.Reflection;
using static StrefaOdrodzenia.AutoSpawnPlugin;

namespace StrefaOdrodzenia
{
    public class CommandStrefaS : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefas";
        public string Help => "Make Zone Points";
        public string Syntax => "[zone_name]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.setpoint" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            string zoneName = command.Length > 0 ? command[0] : "default";

            if (!AutoSpawnPlugin.Instance.Points.ContainsKey(player.CSteamID))
                AutoSpawnPlugin.Instance.Points[player.CSteamID] = new List<Vector3>();

            AutoSpawnPlugin.Instance.Points[player.CSteamID].Add(player.Position);
            UnturnedChat.Say(player, $"Added Point {AutoSpawnPlugin.Instance.Points[player.CSteamID].Count} To Zone {zoneName}", Color.green);
        }
    }

    public class CommandStrefaM : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefam";
        public string Help => "Making Zone From Points";
        public string Syntax => "[zone_name] [confinement_time_in_seconds] [permission] [bypass(y/n)]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.create" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (!AutoSpawnPlugin.Instance.Points.ContainsKey(player.CSteamID) || AutoSpawnPlugin.Instance.Points[player.CSteamID].Count < 4)
            {
                UnturnedChat.Say(player, "You must set 4 points using /strefas", Color.red);
                return;
            }

            string zoneName = command.Length > 0 ? command[0] : "default";
            int trapTime = command.Length > 1 && int.TryParse(command[1], out int t) ? t : AutoSpawnPlugin.Instance.Configuration.Instance.DefaultTrapTimeSeconds;
            string permission = command.Length > 2 ? command[2] : "";
            string bypass = command.Length > 3 ? command[3].ToLower() : "n";

            if (bypass != "t" && bypass != "n")
            {
                UnturnedChat.Say(player, "Bypass Must Be 't'(YES) or 'n'(NO)", Color.red);
                return;
            }

            List<Vector3> points = AutoSpawnPlugin.Instance.Points[player.CSteamID];
            float minX = points[0].x, maxX = points[0].x;
            float minZ = points[0].z, maxZ = points[0].z;
            float minY = points[0].y, maxY = points[0].y;

            for (int i = 1; i < points.Count; i++)
            {
                minX = Mathf.Min(minX, points[i].x);
                maxX = Mathf.Max(maxX, points[i].x);
                minZ = Mathf.Min(minZ, points[i].z);
                maxZ = Mathf.Max(maxZ, points[i].z);
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }

            minY -= 2f;
            maxY += 2f;

            Vector3 center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);

            AutoSpawnPlugin.ZoneConfiguration zone = new AutoSpawnPlugin.ZoneConfiguration
            {
                ZoneName = zoneName,
                MinX = minX,
                MinZ = minZ,
                MaxX = maxX,
                MaxZ = maxZ,
                MinY = minY,
                MaxY = maxY,
                Center = new AutoSpawnPlugin.SimpleVector3(center.x, center.y, center.z),
                TrapTimeSeconds = trapTime,
                RequiredPermission = permission,
                Bypass = bypass
            };

            AutoSpawnPlugin.Instance.Configuration.Instance.Zones.Add(zone);
            AutoSpawnPlugin.Instance.Configuration.Save();
            AutoSpawnPlugin.Instance.Points.Remove(player.CSteamID);

            string bypassInfo = bypass == "t" ? "Bypass Active" : "";
            string permissionInfo = !string.IsNullOrEmpty(permission) ? $" Permission required\r\n: {permission}" : "";

            UnturnedChat.Say(player,
                $"Zone {zoneName} created! jail time: {AutoSpawnPlugin.Instance.FormatTime(trapTime)}{bypassInfo}{permissionInfo}",
                Color.green);
        }
    }

    public class CommandStrefaList : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefalist";
        public string Help => "Show Zone List";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.list" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (AutoSpawnPlugin.Instance.Configuration.Instance.Zones.Count == 0)
            {
                UnturnedChat.Say(player, "No spawn zones defined!\r\n", Color.yellow);
                return;
            }

            UnturnedChat.Say(player, "Respawn Zones List:", Color.blue);
            foreach (var zone in AutoSpawnPlugin.Instance.Configuration.Instance.Zones)
            {
                UnturnedChat.Say(player,
                    $"{zone.ZoneName} - Time: {AutoSpawnPlugin.Instance.FormatTime(zone.TrapTimeSeconds)}, " +
                    $"Size: {zone.MaxX - zone.MinX:F0}x{zone.MaxZ - zone.MinZ:F0}x{zone.MaxY - zone.MinY:F0}",
                    Color.cyan);
            }
        }
    }
}