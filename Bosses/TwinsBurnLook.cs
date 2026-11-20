using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins burn unseen: the user (2026-11-20): "the bosses will catch on fire and burn, but the spirit also causes a
    // burning effects which is quite bright and looks distracting. Can we remove the burning visual effects from the
    // bosses, while still allowing them to take damage?"
    // The game's fire and spirit burns are both SE_Burning (Burning, Spirit): a copy of the effect for each character
    // that gets it (SEMan.AddStatusEffect clones it, on the character's owner only), whose m_startEffects show on it for
    // as long as it burns and whose m_tickEffect plays with each second's damage. On a twin's copy, as it's set up, both
    // lose whatever can be seen (particles, lights, anything drawn), keeping only their sounds; the damage, its numbers
    // and the burn's icon are untouched. The game's own lists, shared by everyone else, are left as they are
    internal static class TwinsBurnLook
    {
        // Each effect it has left out or kept, named once in the game's log, for checking what the twins still show
        private static readonly HashSet<string> s_told = new HashSet<string>();

        [HarmonyPatch(typeof(SE_Burning), nameof(SE_Burning.Setup))]
        private static class Setup
        {
            private static void Prefix(SE_Burning __instance, Character character)
            {
                if (!CallerBoss.Of(character))
                {
                    return;
                }
                __instance.m_startEffects = Unseen(__instance.m_startEffects, __instance.name);
                __instance.m_tickEffect = Unseen(__instance.m_tickEffect, __instance.name);
            }
        }

        // A new list of `effects`' that can't be seen
        private static EffectList Unseen(EffectList effects, string burn)
        {
            List<EffectList.EffectData> kept = new List<EffectList.EffectData>();
            if (effects?.m_effectPrefabs != null)
            {
                foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
                {
                    if (effect == null || !effect.m_prefab)
                    {
                        continue;
                    }
                    bool seen = Seen(effect.m_prefab);
                    if (!seen)
                    {
                        kept.Add(effect);
                    }
                    if (s_told.Add(burn + "/" + effect.m_prefab.name))
                    {
                        Plugin.Log.LogInfo($"The twins' {burn}: {effect.m_prefab.name} {(seen ? "left out (it can be seen)" : "kept (a sound)")}.");
                    }
                }
            }
            return new EffectList { m_effectPrefabs = kept.ToArray() };
        }

        private static bool Seen(GameObject prefab)
        {
            return prefab.GetComponentInChildren<ParticleSystem>(true) || prefab.GetComponentInChildren<Light>(true)
                || prefab.GetComponentInChildren<Renderer>(true);
        }
    }
}
