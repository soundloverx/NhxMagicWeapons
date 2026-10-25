using HarmonyLib;
using System.Collections.Generic;

namespace NhxMagicWeapons.Staves
{
    // Held attacks (looping, as the Staff of Frost's: they shoot again and again while the button is down) stop as soon as
    // the button is up (Player aborts them), which can be before their animation reaches its first shot: a quick click did
    // nothing. For our weapons, they always make their first shot; staggering still stops them, and so does letting go
    // when the shot doesn't come in time
    internal static class FirstShot
    {
        // How long a let-go attack may wait for its first shot
        private const float MaxWait = 1.5f;

        // By the items' name ($item_...)
        private static readonly HashSet<string> s_items = new HashSet<string>();

        internal static void Add(ItemDrop.ItemData.SharedData shared)
        {
            s_items.Add(shared.m_name);
        }

        // Its only callers: Player, when the button of a looping attack is up, and Attack.Update, when staggering
        [HarmonyPatch(typeof(Attack), nameof(Attack.Abort))]
        private static class Abort
        {
            private static bool Prefix(Attack __instance)
            {
                ItemDrop.ItemData weapon = __instance.m_weapon;
                bool waiting = weapon != null && s_items.Contains(weapon.m_shared.m_name) && __instance.m_projectileBurstsFired == 0
                    && __instance.m_time < MaxWait && __instance.m_character && !__instance.m_character.IsStaggering();
                return !waiting;
            }
        }
    }
}
