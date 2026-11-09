using HarmonyLib;
using System.Collections.Generic;

namespace NhxMagicWeapons.Staves
{
    // The game charges every attack of a weapon its primary attack's eitr (Attack.GetAttackEitr reads the item's
    // m_shared.m_attack), so a staff's secondary attack costs what its primary does. Magic Supremacy patches the same for
    // its staves. Our weapons' attacks each cost their own, with the game's discount for skill (a third off at 100)
    internal static class AttackEitr
    {
        // By the items' name ($item_...)
        private static readonly HashSet<string> s_items = new HashSet<string>();

        internal static void Add(ItemDrop.ItemData.SharedData shared)
        {
            s_items.Add(shared.m_name);
        }

        // Attacks ask the private GetAttackEitr() as they start, or at each burst (Attack.Start, Update,
        // FireProjectileBurst), which only calls this one, where the game works the cost out. Other mods' discounts
        // patch the private one, multiplying its result (Jewelcrafting's Magical Bargain and Apotheosis), so the cost is
        // set here, before them: patching the private one too, after theirs, erased them (the user's test, 2026-11-09).
        // Wild Weapons patches this one the same way for every weapon
        [HarmonyPatch(typeof(Attack), "GetAttackEitr", typeof(Character), typeof(ItemDrop.ItemData))]
        private static class GetAttackEitr
        {
            private static void Postfix(Attack __instance, Character character, ItemDrop.ItemData weapon, ref float __result)
            {
                if (weapon == null || !character || !s_items.Contains(weapon.m_shared.m_name))
                {
                    return;
                }
                float eitr = __instance.m_attackEitr;
                __result = eitr <= 0f ? 0f : eitr - eitr * 0.33f * character.GetSkillFactor(weapon.m_shared.m_skillType);
            }
        }
    }
}
