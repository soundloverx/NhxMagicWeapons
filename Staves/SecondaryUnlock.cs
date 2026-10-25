using HarmonyLib;
using System.Collections.Generic;

namespace NhxMagicWeapons.Staves
{
    // Weapons whose secondary attack comes at a level: below it, middle mouse does nothing and its key hint doesn't show.
    // Patching ItemData.HaveSecondaryAttack alone, which both ask, didn't stop the attack in-game, so each place is patched
    internal static class SecondaryUnlock
    {
        // By the items' name ($item_...)
        private static readonly Dictionary<string, int> s_levels = new Dictionary<string, int>();

        internal static void Add(ItemDrop.ItemData.SharedData shared, int level)
        {
            s_levels[shared.m_name] = level;
        }

        private static bool Locked(ItemDrop.ItemData item)
        {
            return item != null && s_levels.TryGetValue(item.m_shared.m_name, out int level) && item.m_quality < level;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.HaveSecondaryAttack))]
        private static class HaveSecondaryAttack
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
            {
                if (__result && Locked(__instance))
                {
                    __result = false;
                }
            }
        }

        // Middle mouse (Player.Update), and a creature's AI
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class StartAttack
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
            {
                if (secondaryAttack && Locked(__instance.GetCurrentWeapon()))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // The key hints of combat (KeyHints.UpdateHints shows the secondary attack's when the weapon has one)
        [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
        private static class UpdateHints
        {
            private static void Postfix(KeyHints __instance)
            {
                if (Player.m_localPlayer && Locked(Player.m_localPlayer.GetCurrentWeapon()))
                {
                    __instance.m_secondaryAttackGP.SetActive(false);
                    __instance.m_secondaryAttackKB.SetActive(false);
                }
            }
        }
    }
}
