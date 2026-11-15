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
    //   should, the staves' fire burning on. devcommands' killall (one hit of 10^10, from no one) still kills them
    // - A fight left is lost (the user: "Can the bosses just despawn and require re-summon instead of just healing?"): with
    //   no living player in the fight (TwinsAI.InReach) for Abandon seconds, the twins go, each in a flash of its rune. The
    //   last time someone was there is in each one's ZDO (SeenKey), so a fight left behind (everyone portalled away, the
    //   area unloaded) ends as the twins load again: on every game that loads them, before anything of the fight
    //   happens (LeftBehind, from CallerBoss.Awake). At first that was only the owner's half-second check, which found
    //   the player back already (a portal by the arena) and let the fight go on; the user, 2026-11-15: "they should
    //   despawn if possible, before any other abilities are used or anything else - no voice lines, nothing"
    internal static class TwinsRules
    {
        private const float Abandon = 30f;
        // How often the time someone was there is written (seconds)
        private const float SeenEvery = 2f;
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
            if (seen == 0L || AnyoneNear(boss))
            {
                if (now - seen > System.TimeSpan.FromSeconds(SeenEvery).Ticks)
                {
                    zdo.Set(s_seenKey, now);
                }
                return false;
            }
            if (now - seen < System.TimeSpan.FromSeconds(Abandon).Ticks)
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

        private static bool AnyoneNear(CallerBoss boss)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsAI.InReach(boss, player))
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

        // Whether this player holds one of the two staves, as every game sees it
        private static bool WieldsStaff(Player player)
        {
            if (s_stormHash == 0)
            {
                s_stormHash = Storms.ItemName.GetStableHashCode();
                s_wrathHash = Wrath.ItemName.GetStableHashCode();
            }
            VisEquipment equipment = player.m_visEquipment;
            return equipment && (equipment.m_currentRightItemHash == s_stormHash || equipment.m_currentRightItemHash == s_wrathHash);
        }

        // A twin's hits, on its owner's game, before anything is taken. An altar's twins only take them from inside its
        // ring (the arena: no sniping from beyond the stones, where their spells don't reach)
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class StavesOnly
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (!CallerBoss.IsTwin(__instance) || hit == null || hit.GetTotalDamage() >= CheatKill)
                {
                    return true;
                }
                if (!(hit.GetAttacker() is Player player) || !WieldsStaff(player))
                {
                    TwinsLog.Refuse(__instance, hit.GetAttacker() is Player ? "not holding a staff" : "not from a player");
                    return false;
                }
                // Pierce counts only as the Wrath of Muspelheim's, part of its fire (CallerBosses: the twins take only fire
                // and lightning)
                if (player.m_visEquipment.m_currentRightItemHash != s_wrathHash)
                {
                    hit.m_damage.m_pierce = 0f;
                }
                CallerBoss twin = CallerBoss.Of(__instance);
                // Untouchable as its second phase comes (TwinsPhase)
                if (twin && twin.m_phase && twin.m_phase.Transforming)
                {
                    TwinsLog.Refuse(__instance, "entering its second phase");
                    return false;
                }
                if (TwinsBond.Arena(twin, out Vector3 arena) && Utils.DistanceXZ(player.transform.position, arena) > TwinsAltars.ArenaRadius + ArenaGrace)
                {
                    TwinsLog.Refuse(__instance, "from outside the ring");
                    return false;
                }
                return true;
            }
        }

        // A player at the ring's very edge still counts as inside (meters)
        internal const float ArenaGrace = 2f;
    }
}
