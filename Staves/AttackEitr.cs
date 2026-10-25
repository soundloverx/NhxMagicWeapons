using HarmonyLib;
using System;
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

        // What the attack takes as it starts, or at each burst (Attack.Start, Update, FireProjectileBurst)
        [HarmonyPatch(typeof(Attack), "GetAttackEitr", new Type[0])]
        private static class GetAttackEitr
        {
            private static void Postfix(Attack __instance, ref float __result)
            {
                ItemDrop.ItemData weapon = __instance.m_weapon;
                if (weapon == null || !__instance.m_character || !s_items.Contains(weapon.m_shared.m_name))
                {
                    return;
                }
                float eitr = __instance.m_attackEitr;
                __result = eitr <= 0f ? 0f : eitr - eitr * 0.33f * __instance.m_character.GetSkillFactor(weapon.m_shared.m_skillType);
            }
        }
    }
}
