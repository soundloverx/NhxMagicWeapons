using HarmonyLib;
using System.Collections.Generic;

namespace NhxMagicWeapons.Staves
{
    // Damage by level that the game's damage per level can't give: a damage type coming at a level, or growing faster from
    // a level on. ItemData.GetDamage is what attacks and tooltips ask (upgrade previews included)
    internal static class LevelDamage
    {
        // From its level, an item gets the damage, and the damage per level for each level above it
        internal struct Step
        {
            internal readonly int Level;
            internal readonly HitData.DamageTypes Damage;
            internal readonly HitData.DamageTypes PerLevel;

            internal Step(int level, HitData.DamageTypes damage, HitData.DamageTypes perLevel)
            {
                Level = level;
                Damage = damage;
                PerLevel = perLevel;
            }
        }

        // By the items' name ($item_...)
        private static readonly Dictionary<string, Step[]> s_steps = new Dictionary<string, Step[]>();

        internal static void Set(ItemDrop.ItemData.SharedData shared, params Step[] steps)
        {
            s_steps[shared.m_name] = steps;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
        private static class GetDamage
        {
            private static void Postfix(ItemDrop.ItemData __instance, int quality, ref HitData.DamageTypes __result)
            {
                if (!s_steps.TryGetValue(__instance.m_shared.m_name, out Step[] steps))
                {
                    return;
                }
                foreach (Step step in steps)
                {
                    if (quality >= step.Level)
                    {
                        __result.Add(step.Damage);
                        __result.Add(step.PerLevel, quality - step.Level);
                    }
                }
            }
        }
    }
}
