using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins' movement in the log (the user, 2026-11-22, after a test fight where the twins walked toward the arena's
    // edge, and "the hunter twin just walk[ed] around until the teleport cooldown ended, then it returned to chase me":
    // "movement tracking for bosses will help. But it should be easy to turn off because I imagine the log spam"). Its
    // own setting, apart from the fight log (Plugin.TwinsMoveLog, off unless asked for). On the game that runs each twin
    // (where its AI runs), every Every seconds, a line for each, starting "Move:":
    // - Where it is, and how far from its arena's middle
    // - Its role and target, how far from them, the ground it gained or lost since the last line, and their speed
    // - Its own speed and heading (toward them, across, away), what it has in hand, and what holds it (an attack, a
    //   stagger, its second phase's coming or its comet, its rune answering a swap)
    // - What its AI last told it to move by (the game's chase, TwinsAI's closing in, idling, wandering, fleeing,
    //   circling, stopping...), where to, and how the path went (the game stops a creature it finds no path for), with
    //   the other orders since the last line
    // Moving away from its target while losing ground starts the line "Move AWAY:"
    internal static class TwinsMoveLog
    {
        private const float Every = 1f;
        // Moving: faster than this (m/s); losing ground: more than this since the last line (m); a path ending this far
        // short of where it was asked to go is partial (m)
        private const float Moving = 0.5f;
        private const float LostGround = 1f;
        private const float PartialGap = 2f;

        private static bool On => Plugin.TwinsMoveLog != null && Plugin.TwinsMoveLog.Value;

        private class Track
        {
            internal float m_lastDistance = -1f;
            internal string m_order;
            internal Vector3 m_orderPoint;
            internal bool m_orderHasPoint;
            internal string m_path;
            internal readonly HashSet<string> m_orders = new HashSet<string>();
        }

        private static readonly Dictionary<CallerBoss, Track> s_tracks = new Dictionary<CallerBoss, Track>();
        private static readonly List<CallerBoss> s_gone = new List<CallerBoss>();
        private static float s_timer;

        // The twin whose AI is updating (MonsterAI.UpdateAI, on its game): its AI, its track, how deep in the AI's moves
        // (only the outermost is the order: IdleMovement walks by RandomMovement, which walks by MoveTo), and whether the
        // game's own update is done (a move after it is TwinsAI.Close's)
        private static BaseAI s_currentAI;
        private static Track s_current;
        private static int s_depth;
        private static bool s_afterGame;

        private static Track TrackOf(CallerBoss boss)
        {
            if (!s_tracks.TryGetValue(boss, out Track track))
            {
                track = new Track();
                s_tracks[boss] = track;
            }
            return track;
        }

        // ---- What its AI orders ----

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Updating
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(MonsterAI __instance)
            {
                Clear();
                if (On && CallerBoss.ByAI.TryGetValue(__instance, out CallerBoss boss) && boss)
                {
                    s_currentAI = __instance;
                    s_current = TrackOf(boss);
                }
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix() => s_afterGame = true;

            private static void Finalizer() => Clear();
        }

        private static void Clear()
        {
            s_currentAI = null;
            s_current = null;
            s_depth = 0;
            s_afterGame = false;
        }

        private static bool Tracking(BaseAI ai) => s_current != null && ai == s_currentAI;

        // A move begins: the outermost is the order
        private static void Begin(BaseAI ai, string order, bool hasPoint, Vector3 point)
        {
            if (!Tracking(ai))
            {
                return;
            }
            if (s_depth++ == 0)
            {
                s_current.m_order = order;
                s_current.m_orderHasPoint = hasPoint;
                s_current.m_orderPoint = point;
                s_current.m_orders.Add(order);
            }
        }

        private static void End(BaseAI ai)
        {
            if (Tracking(ai) && s_depth > 0)
            {
                s_depth--;
            }
        }

        [HarmonyPatch(typeof(BaseAI), "MoveTo")]
        private static class MoveTo
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance, Vector3 point)
                => Begin(__instance, s_afterGame ? "closing in (TwinsAI)" : "the game's chase", true, point);

            // How its path went, at any depth: the move that walked it
            private static void Postfix(BaseAI __instance, Vector3 point, float dist, bool __result)
            {
                if (Tracking(__instance))
                {
                    s_current.m_path = PathOf(__instance, point, dist, __result);
                }
            }

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
        private static class Idle
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance) => Begin(__instance, "idling (TwinsAI stands it, facing its target, if it has one)", false, Vector3.zero);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "RandomMovement")]
        private static class Wander
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance, Vector3 centerPoint) => Begin(__instance, "wandering about a spot", true, centerPoint);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "RandomMovementArroundPoint")]
        private static class Circle
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance, Vector3 point) => Begin(__instance, "circling a spot", true, point);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "Flee")]
        private static class Flee
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance, Vector3 from) => Begin(__instance, "fleeing from a spot", true, from);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "Follow")]
        private static class Follow
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance, GameObject go)
                => Begin(__instance, "following " + (go ? go.name : "nothing"), (bool)go, go ? go.transform.position : Vector3.zero);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "MoveAwayAndDespawn")]
        private static class Leave
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance) => Begin(__instance, "moving away to despawn", false, Vector3.zero);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.StopMoving))]
        private static class Stop
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BaseAI __instance) => Begin(__instance, "stopping", false, Vector3.zero);

            private static void Finalizer(BaseAI __instance) => End(__instance);
        }

        // As BaseAI.MoveTo goes: there already (it stops), straight at it (a twin in an arena: TwinsAI.Straight), no path
        // found (it stops), its path walked to its end, a path ending short of the point (partial: the navmesh doesn't
        // reach it), or a path
        private static string PathOf(BaseAI ai, Vector3 point, float dist, bool stopped)
        {
            Vector3 at = ai.transform.position;
            if (stopped && Utils.DistanceXZ(point, at) < Mathf.Max(dist, 1f) + 0.01f)
            {
                return "there (it stops)";
            }
            if (TwinsAI.WalksStraight(ai))
            {
                return "none: straight at it";
            }
            if (!ai.m_lastFindPathResult)
            {
                return "no path found (it stops)";
            }
            List<Vector3> path = ai.m_path;
            if (path == null || path.Count == 0)
            {
                return "its path walked (it stops)";
            }
            float gap = Utils.DistanceXZ(path[path.Count - 1], point);
            return gap > PartialGap ? $"a partial path, ending {gap:0} m short" : "a path";
        }

        // ---- The lines ----

        // Every frame (Plugin.Update)
        internal static void Update(float dt)
        {
            if (!On)
            {
                if (s_tracks.Count > 0)
                {
                    s_tracks.Clear();
                }
                return;
            }
            s_timer -= dt;
            if (s_timer > 0f)
            {
                return;
            }
            s_timer = Every;
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (boss && boss.m_nview && boss.m_nview.IsValid() && boss.m_nview.IsOwner() && boss.m_humanoid && !boss.m_humanoid.IsDead())
                {
                    Write(boss, TrackOf(boss));
                }
            }
            s_gone.Clear();
            foreach (CallerBoss boss in s_tracks.Keys)
            {
                if (!boss)
                {
                    s_gone.Add(boss);
                }
            }
            foreach (CallerBoss boss in s_gone)
            {
                s_tracks.Remove(boss);
            }
        }

        private static void Write(CallerBoss boss, Track track)
        {
            Humanoid twin = boss.m_humanoid;
            Vector3 position = twin.transform.position;
            Vector3 velocity = twin.GetVelocity();
            velocity.y = 0f;
            float speed = velocity.magnitude;
            Character target = boss.m_target;
            bool away = false;

            StringBuilder line = new StringBuilder();
            line.Append(TwinsLog.Name(boss)).Append(" (").Append(boss.m_role);
            if (target)
            {
                line.Append(" of ").Append(TwinsLog.Name(target));
            }
            line.Append(") at ").Append(TwinsLog.At(position));
            if (TwinsBond.Arena(boss, out Vector3 arena))
            {
                line.Append($", {Utils.DistanceXZ(position, arena):0} m from the middle (the ring is {TwinsAltars.ArenaRadius:0})");
            }

            if (target)
            {
                Vector3 to = target.transform.position - position;
                to.y = 0f;
                float distance = to.magnitude;
                Vector3 theirs = target.GetVelocity();
                theirs.y = 0f;
                line.Append($"; {distance:0.0} m from them");
                float change = track.m_lastDistance >= 0f ? distance - track.m_lastDistance : 0f;
                if (track.m_lastDistance >= 0f)
                {
                    line.Append(change >= 0f ? $" (lost {change:0.0} m" : $" (gained {-change:0.0} m");
                    line.Append($" in {Every:0} s, they move at {theirs.magnitude:0.0} m/s)");
                }
                track.m_lastDistance = distance;
                if (speed > Moving && distance > 0.01f)
                {
                    float angle = Vector3.Angle(velocity, to);
                    string heading = angle < 45f ? "toward them" : angle <= 135f ? "across" : "away from them";
                    line.Append($"; moving {speed:0.0} m/s {heading} ({angle:0} degrees off)");
                    away = angle > 90f && change > LostGround;
                }
                else
                {
                    line.Append($"; standing ({speed:0.0} m/s)");
                }
            }
            else
            {
                track.m_lastDistance = -1f;
                line.Append(speed > Moving ? $"; moving {speed:0.0} m/s" : $"; standing ({speed:0.0} m/s)");
            }

            ItemDrop.ItemData weapon = twin.GetCurrentWeapon();
            line.Append("; in hand: ").Append(WeaponName(weapon));
            List<string> holds = new List<string>();
            if (twin.InAttack())
            {
                holds.Add("in an attack");
            }
            if (twin.IsStaggering())
            {
                holds.Add("staggered");
            }
            if (boss.m_phase && boss.m_phase.Busy(out _))
            {
                holds.Add("standing for its second phase or its comet");
            }
            if (boss.Answering)
            {
                holds.Add("its rune out for a swap");
            }
            if (holds.Count > 0)
            {
                line.Append(" (").Append(string.Join(", ", holds)).Append(')');
            }

            if (track.m_order != null)
            {
                line.Append("; its AI: ").Append(track.m_order);
                if (track.m_orderHasPoint)
                {
                    line.Append(" at ").Append(TwinsLog.At(track.m_orderPoint));
                    if (target)
                    {
                        line.Append($", {Utils.DistanceXZ(track.m_orderPoint, target.transform.position):0} m from its target");
                    }
                }
                if (track.m_path != null)
                {
                    line.Append("; its last path: ").Append(track.m_path);
                }
                track.m_orders.Remove(track.m_order);
                if (track.m_orders.Count > 0)
                {
                    line.Append("; also since the last line: ").Append(string.Join(", ", track.m_orders));
                }
            }
            else
            {
                line.Append("; its AI: no move since the last line");
            }
            track.m_order = null;
            track.m_path = null;
            track.m_orders.Clear();

            Plugin.Log.LogInfo($"[Twins {DateTime.Now:HH:mm:ss.f}] Move{(away ? " AWAY" : "")}: {line}");
        }

        private static string WeaponName(ItemDrop.ItemData weapon)
        {
            if (weapon == null)
            {
                return "nothing";
            }
            return weapon.m_dropPrefab ? weapon.m_dropPrefab.name : weapon.m_shared.m_name;
        }
    }
}
