using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // Whom each twin goes after, and how, on the game that runs it (CallerBoss, every half second), from the players in its
    // fight (TwinsRing.Inside: in its arena's ring) and what each last drew (their ZDOs: TwinsDuel):
    // - Hunter of the player who drew its staff last (the user, 2026-11-14: "Most recent draw"; the swap's twin "proceed[s]
    //   to following them")
    // - Nobody holds its staff, two players or more: hunting with its twin, on its twin's quarry (both on the one player
    //   when both hold the same staff, the user's idea)
    // - Nobody holds its staff, one player: a watcher (the user: "Yes, I like the watcher"): it keeps back, casting only its
    //   telegraphed spell (BossAttacks.IsWatcherSpell) and half again as rarely; between spells it stands and watches its
    //   player (no wandering about: IdleMovement), and when they keep hurting it, it flashes behind its hunting twin
    //   (CallerBoss.Evade)
    // - Its twin fallen (TwinsBond.Alone): always a hunter, of whoever drew the fallen twin's staff last, the player who
    //   can hurt it (a staff cannot hurt its own caller: TwinsRules); with no such player, of who drew its own, or the
    //   nearest. The fight's redesign, agreed with the user 2026-11-15: alone with one player, the only staff that hurts
    //   the survivor is the fallen twin's, so by the rules above it would have stood and watched to the end
    // Its AI goes after that target (UpdateTarget); the attacks, moves and the rest are the game's
    internal static class TwinsAI
    {
        // A watcher's spell waits this many times its interval
        private const float WatcherSlowdown = 1.5f;

        private static readonly List<Player> s_fighters = new List<Player>();
        // BaseAI.SetAlerted is protected (virtual: MonsterAI's own runs): called through a delegate, not directly
        private static readonly System.Action<BaseAI, bool> s_setAlerted =
            AccessTools.MethodDelegate<System.Action<BaseAI, bool>>(AccessTools.Method(typeof(BaseAI), "SetAlerted"));

        internal static void UpdateRole(CallerBoss boss)
        {
            s_fighters.Clear();
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsRing.Inside(boss, player.transform.position))
                {
                    s_fighters.Add(player);
                }
            }
            int fighters = s_fighters.Count;
            CallerBoss.Role role = CallerBoss.Role.None;
            Character target = null;
            // A player the elements have judged comes first: both turn to them, and only channel (TwinsJudgment)
            Player judged = Judged(boss);
            if (judged)
            {
                role = CallerBoss.Role.Judging;
                target = judged;
            }
            else if (s_fighters.Count > 0)
            {
                Player mine = LatestDrawer(boss.Element);
                if (TwinsBond.Alone(boss))
                {
                    Player bane = LatestDrawer(TwinsDuel.Other(boss.Element));
                    role = CallerBoss.Role.Hunter;
                    target = bane ? bane : mine ? mine : Nearest(boss);
                }
                else if (mine)
                {
                    role = CallerBoss.Role.Hunter;
                    target = mine;
                }
                else if (s_fighters.Count == 1)
                {
                    role = CallerBoss.Role.Watcher;
                    target = s_fighters[0];
                }
                else
                {
                    role = CallerBoss.Role.Hunter;
                    target = LatestDrawer(boss.Element == Element.Storm ? Element.Fire : Element.Storm);
                }
            }
            s_fighters.Clear();
            Character before = boss.m_target;
            CallerBoss.Role roleBefore = boss.m_role;
            boss.m_role = role;
            boss.m_target = target;
            if (role != roleBefore || target != before)
            {
                TwinsLog.Event($"{TwinsLog.Name(boss)}: {role}{(target ? " of " + TwinsLog.Name(target) : ", no target")} ({fighters} player(s) in the fight; was {roleBefore}{(before ? " of " + TwinsLog.Name(before) : "")}).");
            }
            // Turning to someone (someone new, or starting to hunt them): it may have something to say to them
            // (TwinsVoices)
            if (target is Player turnedTo && (target != before || (role == CallerBoss.Role.Hunter && roleBefore != CallerBoss.Role.Hunter)))
            {
                TwinsVoices.Targeted(boss, turnedTo);
            }
            // Or what it has waited to say to the one it's after: the taunt its turn was too early for, the pair's first
            else
            {
                TwinsVoices.Pending(boss);
            }

            // Each puts away what its role can't use, or its AI would go on with it (MonsterAI attacks with what's in hand)
            Humanoid humanoid = boss.m_humanoid;
            ItemDrop.ItemData held = humanoid.m_rightItem;
            bool unusable = role == CallerBoss.Role.Judging
                || (role == CallerBoss.Role.Watcher ? !BossAttacks.IsWatcherSpell(held) || Enraged(boss) : role == CallerBoss.Role.Hunter && BossAttacks.IsWatcherOnly(held));
            if (held != null && unusable && !humanoid.InAttack())
            {
                humanoid.UnequipItem(held, false);
            }
        }

        // The nearest living player within reach the elements have judged
        private static Player Judged(CallerBoss boss)
        {
            Player judged = null;
            float best = float.PositiveInfinity;
            foreach (Player player in s_fighters)
            {
                ZDO zdo = player.m_nview && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
                float distance = Vector3.Distance(player.transform.position, boss.transform.position);
                if (zdo != null && zdo.GetBool(TwinsJudgment.JudgedKey) && distance < best)
                {
                    best = distance;
                    judged = player;
                }
            }
            return judged;
        }

        // Among s_fighters, who drew `element`'s staff last
        private static Player LatestDrawer(Element element)
        {
            Player latest = null;
            long latestTime = long.MinValue;
            foreach (Player player in s_fighters)
            {
                ZDO zdo = player.m_nview && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
                if (zdo == null || zdo.GetInt(TwinsDuel.ElementKey) != (int)element)
                {
                    continue;
                }
                long drawn = zdo.GetLong(TwinsDuel.DrawnKey);
                if (drawn > latestTime)
                {
                    latestTime = drawn;
                    latest = player;
                }
            }
            return latest;
        }

        // Among s_fighters, the nearest to this twin
        private static Player Nearest(CallerBoss boss)
        {
            Player nearest = null;
            float best = float.PositiveInfinity;
            foreach (Player player in s_fighters)
            {
                float distance = Vector3.Distance(player.transform.position, boss.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = player;
                }
            }
            return nearest;
        }

        // In its second phase (TwinsPhase), where a watcher casts its phase's spell in place of its own
        private static bool Enraged(CallerBoss boss) => boss.m_phase && boss.m_phase.Enraged;

        // A target and nothing to cast: standing, facing it. A watcher between spells, a judge; a hunter only for the
        // moment its AI takes to pick up its attack again (Humanoid.EquipBestWeapon, every second), where it would wander
        private static bool Watching(BaseAI ai, out CallerBoss boss)
        {
            return CallerBoss.ByAI.TryGetValue(ai, out boss) && boss && boss.m_target && boss.m_role != CallerBoss.Role.None;
        }

        // Its target, before the AI picks its own (it keeps what it has while m_updateTargetTimer runs)
        [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
        private static class Target
        {
            private static void Prefix(MonsterAI __instance)
            {
                if (!CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss) || !boss || !boss.m_target || boss.m_target.IsDead())
                {
                    return;
                }
                __instance.m_targetCreature = boss.m_target;
                __instance.m_targetStatic = null;
                __instance.m_updateTargetTimer = 1f;
                if (!__instance.IsAlerted())
                {
                    s_setAlerted?.Invoke(__instance, true);
                }
            }

            // Only players in its fight: with no target of its own, the AI would pick the nearest enemy, and a tame is one
            // (held in the arena, it can't be hurt: the twin stood by it), or a player watching from outside the ring
            private static void Postfix(MonsterAI __instance, ref bool canHearTarget, ref bool canSeeTarget)
            {
                Character picked = __instance.m_targetCreature;
                if (picked && CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss)
                    && (!(picked is Player) || !TwinsRing.Inside(boss, picked.transform.position)))
                {
                    __instance.m_targetCreature = null;
                    canHearTarget = false;
                    canSeeTarget = false;
                }
            }
        }

        // A twin with a player to go after goes after them, seen or not: the game's AI otherwise walks to where it last
        // saw them and wanders there (MonsterAI.UpdateAI, its view being 40 m in an arena 80 m across), and drops a target
        // it hasn't attacked for a minute; hunting a player, it doesn't
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.HuntPlayer))]
        private static class Pursue
        {
            private static void Postfix(MonsterAI __instance, ref bool __result)
            {
                if (!__result && CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss) && boss && boss.m_target is Player)
                {
                    __result = true;
                }
            }
        }

        // A watcher's attacks: its spell only, and more rarely. A hunting Firecaller's: not its sigil, which it would cast
        // from afar as often as not instead of closing in (the user, 2026-11-14, once it had it: "the Firecaller boss isn't
        // as relentless in his pursuit as he was before [...] he's ok not chasing his target that much anymore"); the
        // Stormcaller keeps its call as a hunter (its bolt brings it in)
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanUseAttack))]
        private static class Attacks
        {
            private static void Postfix(BaseAI __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!__result || !CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss) || !boss)
                {
                    return;
                }
                // Not before this game has taken up its timers, as it begins to run it (CallerBoss.Resume)
                if (!boss.Running)
                {
                    __result = false;
                    return;
                }
                // Standing for its phase's coming or its comet's warning: nothing else (TwinsPhase)
                if (boss.m_phase && boss.m_phase.Busy(out _))
                {
                    __result = false;
                    return;
                }
                // Its rune is out for a clean staff's draw: nothing new until it has struck, so it can land on it
                // (CallerBoss.Arrive)
                if (boss.Answering)
                {
                    __result = false;
                    return;
                }
                if (!boss.m_target)
                {
                    return;
                }
                if (boss.m_role == CallerBoss.Role.Judging)
                {
                    __result = false;
                }
                else if (boss.m_role == CallerBoss.Role.Watcher)
                {
                    if (!BossAttacks.IsWatcherSpell(item) || Enraged(boss) || Time.time - item.m_lastAttackTime < item.m_shared.m_aiAttackInterval * WatcherSlowdown)
                    {
                        __result = false;
                    }
                }
                else if (boss.m_role == CallerBoss.Role.Hunter && BossAttacks.IsWatcherOnly(item))
                {
                    __result = false;
                }
            }
        }

        // Standing still, facing where it must, as its second phase comes or while it winds up its comet (TwinsPhase.Busy):
        // its AI waits (on its owner's game, where the AI runs)
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Hold
        {
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                // A twin whose fight was left does nothing in the moment before it goes (CallerBoss.m_abandoned)
                if (CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss left) && left && left.m_abandoned)
                {
                    __instance.StopMoving();
                    __result = true;
                    return false;
                }
                if (!CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss) || !boss || !boss.m_phase || !boss.m_phase.Busy(out Vector3 look))
                {
                    return true;
                }
                __instance.StopMoving();
                if (look.sqrMagnitude > 0.0001f)
                {
                    __instance.LookTowards(look.normalized);
                }
                __result = true;
                return false;
            }
        }

        // A twin with nothing to cast stands and watches its player, rather than wandering about (alerted, the game's
        // idle wandering is a run): Watching
        [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
        private static class Idle
        {
            private static bool Prefix(BaseAI __instance)
            {
                if (!Watching(__instance, out CallerBoss boss))
                {
                    return true;
                }
                __instance.StopMoving();
                // As BaseAI.LookAt (protected), through its public half
                Vector3 look = boss.m_target.GetCenterPoint() - boss.m_humanoid.m_eye.position;
                if (look.x * look.x + look.z * look.z > 0.0001f)
                {
                    __instance.LookTowards(look.normalized);
                }
                return false;
            }
        }
    }
}
