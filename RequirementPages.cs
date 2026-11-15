using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons
{
    // The crafting panel has four slots for a recipe's materials; with more, the game turns pages, one a second
    // (InventoryGui.SetupRequirementList). The armor's recipes have five since the twins' drops joined them, and the user
    // saw none of the drops (2026-11-15: "I couldn't see the new materials in the recipes [...] it should cycle through
    // the "pages" of materials"). The cause is other mods' shared helper library (ItemManager, in Jewelcrafting,
    // Artisan Mastery and ValheimCuisine; the first to load applies it): its transpiler, written before the game turned
    // pages, makes the loop start over at the first material and the first slot once it runs out, so the second page is
    // drawn and then covered by the first, and nothing past the fourth material ever shows, for any recipe. This draws
    // the list again after the game, as the game means to: the same page, the same slots (with the game alone, the same
    // picture)
    [HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
    internal static class RequirementPages
    {
        private static void Postfix(InventoryGui __instance, int quality, Player player, bool allowedQuality, int amount)
        {
            GameObject[] slots = __instance.m_recipeRequirementList;
            var requirements = __instance.m_reqList;
            if (!allowedQuality || slots == null || slots.Length == 0 || requirements == null || requirements.Count <= slots.Length)
            {
                return;
            }
            int pages = Mathf.CeilToInt((float)requirements.Count / slots.Length);
            int first = (int)Time.fixedTime % pages * slots.Length;
            int slot = 0;
            for (int i = first; i < requirements.Count && slot < slots.Length; i++)
            {
                if (InventoryGui.SetupRequirement(slots[slot].transform, requirements[i], player, true, quality, amount))
                {
                    slot++;
                }
            }
            for (; slot < slots.Length; slot++)
            {
                InventoryGui.HideRequirement(slots[slot].transform);
            }
        }
    }
}
