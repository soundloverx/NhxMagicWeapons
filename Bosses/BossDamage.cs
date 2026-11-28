using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins' damage, scaled for the whole world (the user, 2026-11-28: "The bosses seem to do a lot more damage in
    // multiplayer than I expected. Can we set up a variable which I can control with an admin command to decrease their
    // damage? to 90% or 80% etc"). nhx_boss damage <%> (BossCommand, a cheat: an admin's on a server) keeps it in a world
    // key (ZoneSystem's global keys: saved with the world, sent by the server to every game as it changes and as a game
    // joins), so every game reads the same share. No key is 100%: the damage as built.
    // Each game scales the damage that lands on what it runs (its own player):
    // - hits from a twin: their staves' attacks, their runes, the storm's rain, the smites, the comet. As they come in
    //   (Character.RPC_Damage), before resistances and armor, so the burning a fire hit leaves shrinks with it
    // - the fight's damage that doesn't come as a hit: the pulse, the burning way, the arcs (TwinsPhase.Hurt), and a
    //   smite from the elements with no living twin to send it (TwinsDuel.Smite)
    // Not the fight's rules, which take a share of health on their own terms: the judgment (TwinsJudgment) kills whatever
    // the share, and the drain's doubling shares (TwinsDrain) are what make sharing a staff costly
    internal static class BossDamage
    {
        private const string Key = "nhx_bossdamage";

        internal const float MinPercent = 1f;
        internal const float MaxPercent = 200f;

        // The share of their damage the twins deal: 1 as built
        internal static float Scale
        {
            get
            {
                if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(Key, out string value)
                    || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
                {
                    return 1f;
                }
                return Mathf.Clamp(percent, MinPercent, MaxPercent) / 100f;
            }
        }

        internal static float Percent => Scale * 100f;

        // Asks the server to keep `percent` for the world (100: no key). Every game has it once the server sends the
        // keys back, this one too
        internal static void Set(float percent)
        {
            if (ZoneSystem.instance == null)
            {
                return;
            }
            if (Mathf.Abs(percent - 100f) < 0.001f)
            {
                ZoneSystem.instance.RemoveGlobalKey(Key);
                return;
            }
            ZoneSystem.instance.SetGlobalKey($"{Key} {percent.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        // What this hit on `target` is scaled by: a twin's, on anything but a twin; 1 otherwise. Also for the fight's
        // log, which notes it (TwinsLog)
        internal static float For(Character target, HitData hit)
        {
            if (hit == null || !target || CallerBoss.IsTwin(target))
            {
                return 1f;
            }
            float scale = Scale;
            if (scale == 1f || !CallerBoss.IsTwin(hit.GetAttacker()))
            {
                return 1f;
            }
            return scale;
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class TwinHit
        {
            [HarmonyPriority(Priority.High)]
            private static void Prefix(Character __instance, HitData hit)
            {
                float scale = For(__instance, hit);
                if (scale != 1f)
                {
                    hit.m_damage.Modify(scale);
                }
            }
        }
    }
}
