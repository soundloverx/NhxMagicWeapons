using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The altar's wave (the user, 2026-11-22: "what if we make the altar itself shoot out waves of energy every 5 seconds
    // and the players will lose 10% of their max eitr and stamina, depending on the staff they wield (similar to the
    // debuff they get) [...] which will force them to take breaks from hitting the bosses, which means the fight will
    // last longer and offer more chances for mistakes"; with Claude's suggestions, agreed: by the last staff drawn, as the
    // charges go, so putting the staff away doesn't dodge it; no roll through it; seen as it comes):
    // - Its time: every Every seconds from First seconds into the fight (the twins' summon, TwinsBond.Since, in the
    //   network's time: every game agrees, nothing is sent), its front leaving the altar and crossing the ring at Speed
    // - On each player's own game (Update): as the front reaches them, if they're in the fight, alive, not judged and
    //   with a staff drawn, it takes Share of their max eitr (the Staff of Storms the last drawn) or of their max stamina
    //   (the Wrath of Muspelheim), as a drain (their regen goes on), and that bar flashes, the game's own flash for an
    //   empty bar (the user: "Oh we can make the bars flash? I didn't know that.")
    // - On every game, its look on the altar (WaveLook)
    internal static class TwinsWave
    {
        internal const float Every = 5f;
        internal const float First = 10f;
        internal const float Speed = 20f;
        private const float Share = 0.1f;
        // A front that passed the player longer ago than this (seconds) took nothing: they came into the fight after it
        private const float Window = 1f;
        // Two arenas' middles nearer than this are the same arena (as TwinsRing's)
        private const float SameArena = 1f;

        // The wave that last took from the local player, in this fight
        private static int s_taken = -1;
        private static bool s_flashing;

        // While the wave flashes a bar (TwinsLogPlayer doesn't count it as running out)
        internal static bool Flashing => s_flashing;

        // The wave about `boss`'s arena: which it is (counted from the fight's first), and how long since its front left
        // the altar (seconds); false before the first
        internal static bool Front(CallerBoss boss, out int wave, out float elapsed)
        {
            wave = -1;
            elapsed = 0f;
            float since = TwinsBond.Since(boss);
            if (float.IsInfinity(since) || since < First)
            {
                return false;
            }
            float time = since - First;
            wave = Mathf.FloorToInt(time / Every);
            elapsed = time - wave * Every;
            return true;
        }

        // A living twin called into the arena about `arena`, or null
        internal static CallerBoss TwinOf(Vector3 arena)
        {
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (boss && boss.m_humanoid && !boss.m_humanoid.IsDead() && TwinsBond.Arena(boss, out Vector3 theirs)
                    && Utils.DistanceXZ(theirs, arena) < SameArena)
                {
                    return boss;
                }
            }
            return null;
        }

        // Every frame (Plugin.Update), for the local player
        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (!TwinsDuel.InFight || !player || player.IsDead())
            {
                s_taken = -1;
                return;
            }
            Element drawn = TwinsDuel.Drawn;
            if (drawn == Element.None || TwinsJudgment.Judged)
            {
                return;
            }
            Vector3 at = player.transform.position;
            if (!TwinsRing.Fight(at, out CallerBoss twin) || !TwinsBond.Arena(twin, out Vector3 arena) || !Front(twin, out int wave, out float elapsed))
            {
                return;
            }
            float passed = elapsed - Utils.DistanceXZ(at, arena) / Speed;
            if (passed < 0f || passed > Window || wave == s_taken)
            {
                return;
            }
            s_taken = wave;
            Take(player, drawn == Element.Storm);
        }

        private static void Take(Player player, bool eitr)
        {
            float max = eitr ? player.GetMaxEitr() : player.GetMaxStamina();
            float before = eitr ? player.GetEitr() : player.GetStamina();
            float after = Mathf.Max(0f, before - max * Share);
            if (eitr)
            {
                player.m_eitr = after;
            }
            else
            {
                player.m_stamina = after;
            }
            if (Hud.instance && max > 0f)
            {
                s_flashing = true;
                try
                {
                    if (eitr)
                    {
                        Hud.instance.EitrBarEmptyFlash();
                    }
                    else
                    {
                        Hud.instance.StaminaBarEmptyFlash();
                    }
                }
                finally
                {
                    s_flashing = false;
                }
            }
            TwinsLog.Waved(player, eitr, before, after, max);
        }
    }
}
