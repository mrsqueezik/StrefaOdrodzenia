using HarmonyLib;
using SDG.Unturned;
using Rocket.Unturned.Player;
using System.Reflection;

namespace StrefaOdrodzenia
{
    public static class InventoryPatches
    {
        private static bool ShouldBlock(Player player)
        {
            if (player == null) return false;
            var unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            return AutoSpawnPlugin.Instance?.IsPlayerTrapped(unturnedPlayer) ?? false;
        }

        //  Block inventory opening
        [HarmonyPatch(typeof(PlayerInventory))]
        [HarmonyPatch("ServerOpenStorage")]
        public static class InventoryOpenPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerInventory __instance)
            {
                return !ShouldBlock(__instance.player);
            }
        }

        //  Block interactions
        [HarmonyPatch(typeof(PlayerInteract))]
        [HarmonyPatch("ServerSimulate")]
        public static class InteractPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerInteract __instance)
            {
                return !ShouldBlock(__instance.player);
            }
        }

        //  Block item usage
        [HarmonyPatch(typeof(PlayerEquipment))]
        [HarmonyPatch("use")]
        public static class UseItemPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerEquipment __instance)
            {
                return !ShouldBlock(__instance.player);
            }
        }
    }
}