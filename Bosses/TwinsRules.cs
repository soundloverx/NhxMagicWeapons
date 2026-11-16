using HarmonyLib;
using UnityEngine;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // Two of the twins' rules against cheesing them (Claude's suggestions, 2026-11-14, taken with step 2):
    // - Only the two staves hurt them: a hit counts only from a player holding one of them, as every game sees it (their
    //   VisEquipment), so tames, summons, traps, bombs and the rest do nothing; so do players with anything else, who
    //   are being judged anyway (TwinsJudgment). Every hit of the staves' spells is the caster's (chains, blasts, tendrils,
    //   the storm, Lightning Step, the comet); burning goes past this (SE_Burning applies its damage itself), as it
    //   should, the staves' fire burning on, but waits while a twin's second phase comes (BurningWaits).
    //   devcommands' killall (one hit of 10^10, from no one) still kills them
    // - A fight left is lost (the user: "Can the bosses just despawn and require re-summon instead of just healing?"): with
    //   no living player in the fight (TwinsRing.Inside) for Abandon seconds, the twins go, each in a flash of its rune.
    //   Abandon was 30 s; the redesign (agreed with the user 2026-11-15, its change 5) makes it 5: "The twins withdraw
    //   5 s after no living player is left in the ring, solo and in co-op alike", so alone a death is a wipe (no
    //   respawning and coming back to twins still hurt), and together the fight goes on while anyone lives. Who's
    //   there: each player's own game says so while its player lives in the fight (TwinsDuel: CallerBoss.PresentRpc,
    //   every second), and the twin's game looks too (AnyoneInside): one alone would miss a player too far across the
    //   ring for the twin's game to have loaded, or one whose game stalls. The last time someone was there is in each
    //   one's ZDO (SeenKey), so a fight left behind (a crash, everyone gone, the area unloaded) ends as the twins load
    //   again: on every game that loads them, before anything of the fight happens (LeftBehind, from
    //   CallerBoss.Awake). At first that was only the owner's half-second check, which found the player back already (a
    //   portal by the arena) and let the fight go on; the user, 2026-11-15: "they should despawn if possible, before
    //   any other abilities are used or anything else - no voice lines, nothing"
    // And of the fight's redesign (agreed with the user, 2026-11-15, its change 1):
    // - A staff cannot hurt its own caller: a hit from a player holding the Staff of Storms on the Stormcaller, or the
    //   Wrath of Muspelheim on the Firecaller, is refused whole (StavesOnly), so a staff swap is a target swap: the twin
    //   chasing a player is the one they can't hurt. By the staff in hand, not the damage's type: the staves are mixed
    //   (the Staff of Storms has spirit from level 5 and fire from 10, two fifths of the Wrath's hit is pierce), and by
    //   type it leaked. The twin's ward flashes where it was struck (CallerBoss.Ward: a refused hit shows no number).
    //   What's aimed at it still ends on it (it shields its twin); the staves' spells that pick their own targets leave
    //   it alone (Immune, asked by Staves\StaffHits.Seeks)
    internal static class TwinsRules
    {
        private const float Abandon = 5f;
        // How often the time someone was there is written (seconds)
        private const float SeenEvery = 1f;
        private const float VanishRadius = 3f;
        private const float CheatKill = 1e9f;

        private static readonly int s_seenKey = "nhx_twins_seen".GetStableHashCode();
        private static int s_stormHash;
        private static int s_wrathHash;

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        // On the twin's owner's game, every half second (CallerBoss); whether it has gone
        internal static bool CheckAbandoned(CallerBoss boss)
        {
            ZDO zdo = boss.m_nview.GetZDO();
            long seen = zdo.GetLong(s_seenKey);
            long now = Now;
            if (AnyoneInside(boss))
            {
                boss.Present();
            }
            // A twin just made, or just taken over by this game (which hasn't heard its players yet), has the whole time
            float since = boss.SincePresent();
            if (seen == 0L || since < SeenEvery)
            {
                if (now - seen > System.TimeSpan.FromSeconds(SeenEvery).Ticks)
                {
                    zdo.Set(s_seenKey, now);
                }
                return false;
            }
            if (since < Abandon)
            {
                return false;
            }
            Vanish(boss, $"no living player in the fight for {Abandon:0} s");
            return true;
        }

        // A twin just loaded whose fight was left: nobody in it for Abandon seconds before it unloaded (on any game)
        internal static bool LeftBehind(ZDO zdo)
        {
            long seen = zdo != null ? zdo.GetLong(s_seenKey) : 0L;
            return seen != 0L && Now - seen >= System.TimeSpan.FromSeconds(Abandon).Ticks;
        }

        // A living player in this twin's fight, as this game sees them
        private static bool AnyoneInside(CallerBoss boss)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsRing.Inside(boss, player.transform.position))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void Vanish(CallerBoss boss, string why)
        {
            GameObject rune = boss.Element == Element.Storm ? TwinsDuel.s_stormRune : TwinsDuel.s_fireRune;
            if (rune)
            {
                ElementRune.Create(rune, boss.transform.position, 0f, VanishRadius);
            }
            TwinsBond.Tell(boss.transform.position, "$msg_nhx_twins_withdraw");
            TwinsLog.Event($"{TwinsLog.Name(boss)} vanished at {TwinsLog.At(boss.transform.position)}: {why}.");
            ZNetScene.instance.Destroy(boss.gameObject);
        }

        // The element of the staff this player holds, as every game sees it (none: anything else in hand, or nothing)
        internal static Element StaffOf(Player player)
        {
            if (s_stormHash == 0)
            {
                s_stormHash = Storms.ItemName.GetStableHashCode();
                s_wrathHash = Wrath.ItemName.GetStableHashCode();
            }
            VisEquipment equipment = player ? player.m_visEquipment : null;
            int hash = equipment ? equipment.m_currentRightItemHash : 0;
            return hash == s_stormHash ? Element.Storm : hash == s_wrathHash ? Element.Fire : Element.None;
        }

        // Whether `target` is a twin that `attacker` can't hurt with the staff in their hand: its own element's
        internal static bool Immune(Character attacker, Character target)
        {
            CallerBoss twin = CallerBoss.Of(target);
            return twin && attacker is Player player && StaffOf(player) == twin.Element;
        }

        // A twin's hits, on its owner's game, before anything is taken. Only from a player in its fight (TwinsRing.Inside:
        // an altar's twins', inside its ring; no sniping from beyond the stones, where their spells don't reach)
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class StavesOnly
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (!CallerBoss.IsTwin(__instance) || hit == null || hit.GetTotalDamage() >= CheatKill)
                {
                    return true;
                }
                Player player = hit.GetAttacker() as Player;
                Element staff = StaffOf(player);
                if (staff == Element.None)
                {
                    TwinsLog.Refuse(__instance, hit.GetAttacker() is Player ? "not holding a staff" : "not from a player");
                    return false;
                }
                // Pierce counts only as the Wrath of Muspelheim's, part of its fire (CallerBosses: the twins take only fire
                // and lightning)
                if (staff != Element.Fire)
                {
                    hit.m_damage.m_pierce = 0f;
                }
                CallerBoss twin = CallerBoss.Of(__instance);
                // Untouchable as its second phase comes (TwinsPhase), its ward showing where a hit from its fight landed
                // (the user, 2026-11-16, of Claude's extras: "I want all the extras")
                if (twin && twin.m_phase && twin.m_phase.Transforming)
                {
                    TwinsLog.Refuse(__instance, "entering its second phase");
                    if (TwinsRing.Inside(twin, player.transform.position))
                    {
                        twin.Ward(hit.m_point, player);
                    }
                    return false;
                }
                if (twin && !TwinsRing.Inside(twin, player.transform.position))
                {
                    TwinsLog.Refuse(__instance, "from outside the ring");
                    return false;
                }
                // Its own element's staff: refused whole, before the game takes anything of it (no sliver of the other
                // types, no burning, no stagger), and its ward shows
                if (twin && staff == twin.Element)
                {
                    TwinsLog.Refuse(__instance, "its own element's staff");
                    twin.Ward(hit.m_point, player);
                    return false;
                }
                return true;
            }
        }

        // A twin's burning waits while its second phase comes (TwinsPhase), untouchable: its timer, its next tick and the
        // damage it has left all held, so it goes on where it was once the phase has come (the user, 2026-11-16, of
        // Claude's audit: "Apply fixes for #1 and #3"). Burning applies its damage itself, past StavesOnly
        [HarmonyPatch(typeof(SE_Burning), nameof(SE_Burning.UpdateStatusEffect))]
        private static class BurningWaits
        {
            private static bool Prefix(SE_Burning __instance)
            {
                CallerBoss twin = CallerBoss.Of(__instance.m_character);
                return !(twin && twin.m_phase && twin.m_phase.Transforming);
            }
        }
    }
}
