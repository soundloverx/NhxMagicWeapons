using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons
{
    // Two things Magic Supremacy's code does for the staves taken from it (PrefabItems), which their prefabs can't do
    // alone: the user's pick (2026-11-28), for those two staves only, the rest of them staying as their prefabs make them.
    // Each is a component put on the prefab it changes (PrefabItems.AddFixes), so it touches nothing else.

    // Sanguine Concord's pellets (projectile_bloodcaller_staff_heal_DO) heal allies and fly through enemies, as in Magic
    // Supremacy (its ProjectileFriendlyHit). Alone, they'd only hit enemies, for no damage: the game hits allies with no
    // projectile but those set to (Projectile.m_hitFriendly), and puts the attack's status effect (none) in place of the
    // pellet's own as it's fired (Projectile.Setup). So the pellet gives the first ally it meets its effect, once, by name
    public class FriendlyHeal : MonoBehaviour
    {
        // Public, so that the pellets fired copy it from the prefab
        public string m_statusEffect = "";

        private bool m_healed;

        // Where the pellet hits, on the game that fired it (the projectile's owner)
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class OnHit
        {
            private static bool Prefix(Projectile __instance, Collider collider, bool water)
            {
                FriendlyHeal heal = __instance.GetComponent<FriendlyHeal>();
                if (!heal || water || !collider)
                {
                    return true;
                }
                GameObject hit = Projectile.FindHitObject(collider);
                Character target = hit ? hit.GetComponent<Character>() : null;
                Character owner = __instance.m_owner;
                if (!target || !owner)
                {
                    return true;
                }
                // Through its caster and enemies: the game's hit is skipped, and the pellet flies on
                if (target == owner || BaseAI.IsEnemy(owner, target))
                {
                    return false;
                }
                // An ally (players, tames): the game lets the pellet through them (not a valid target), so it goes on too.
                // By name, so that it reaches another player on their own game
                if (!heal.m_healed)
                {
                    heal.m_healed = true;
                    target.GetSEMan().AddStatusEffect(heal.m_statusEffect.GetStableHashCode(), true);
                }
                return true;
            }
        }
    }

    // The Candle of Vital Light's sigil (aoe_lightcaller_staff_sigil_DO) heals once and goes, with a flash, and each player
    // has 3 at most, the oldest going when a fourth lands, as in Magic Supremacy (its AoeOneHit and AoeCount). Alone, it
    // stays its 120 s and heals whoever is on it every 0.2 s
    public class OneHitAoe : MonoBehaviour
    {
        // At its centre as it goes; and how many each player can have (0: no limit). Public, so that copies have them
        public GameObject m_hitEffect;
        public int m_maxPerPlayer;

        private bool m_spent;

        // The local player's, by prefab, oldest first: an Aoe's Setup only runs on the game that creates it
        private static readonly Dictionary<string, List<GameObject>> s_placed = new Dictionary<string, List<GameObject>>();

        [HarmonyPatch(typeof(Aoe), "OnHit")]
        private static class OnHit
        {
            // Nothing more once it has healed: the others in it the same moment get nothing
            private static bool Prefix(Aoe __instance, ref bool __result)
            {
                OneHitAoe once = __instance.GetComponent<OneHitAoe>();
                if (once && once.m_spent)
                {
                    __result = false;
                    return false;
                }
                return true;
            }

            // It hits characters only (no props, no terrain), so a hit is someone it healed
            private static void Postfix(Aoe __instance, bool __result)
            {
                OneHitAoe once = __instance.GetComponent<OneHitAoe>();
                if (!__result || !once || once.m_spent)
                {
                    return;
                }
                once.m_spent = true;
                if (once.m_hitEffect)
                {
                    UnityEngine.Object.Instantiate(once.m_hitEffect, __instance.transform.position, __instance.transform.rotation);
                }
                ZNetScene.instance.Destroy(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
        private static class Setup
        {
            private static void Postfix(Aoe __instance, Character owner)
            {
                OneHitAoe once = __instance.GetComponent<OneHitAoe>();
                if (!once || once.m_maxPerPlayer <= 0 || !owner || owner != Player.m_localPlayer)
                {
                    return;
                }
                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (!s_placed.TryGetValue(prefab, out List<GameObject> placed))
                {
                    placed = new List<GameObject>();
                    s_placed[prefab] = placed;
                }
                placed.RemoveAll(go => !go);
                placed.Add(__instance.gameObject);
                while (placed.Count > once.m_maxPerPlayer)
                {
                    ZNetScene.instance.Destroy(placed[0]);
                    placed.RemoveAt(0);
                }
            }
        }
    }
}
