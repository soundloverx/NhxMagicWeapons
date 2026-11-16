using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The ring, the twins' fight's one boundary (the fight's redesign, agreed with the user 2026-11-15, its change 4: "A
    // player is in an altar's fight if they are inside its ring, and only then; the ring is drawn while the twins live,
    // and it holds everyone inside"):
    // - One test of who's in an altar's twins' fight (Inside): within the ring, ArenaRadius from its middle. Charges,
    //   targeting, the twins' spells, the hits they take, who keeps the fight alive: all ask it. Before the redesign
    //   three rules and six radii did (50 m from a twin to join, 80 to leave, 80 for the arcs, the ring with 2 m of
    //   grace, 90 for the altar to be busy), around an edge that wasn't drawn. Twins without an arena (nhx_boss summon,
    //   spawn: for testing) keep one radius about each of them, FreeRadius
    // - It holds: whoever is inside is stopped at its edge, players (each on their own game, while they're in the fight:
    //   TwinsDuel) and twins (on the game that runs them), and whoever is outside walks in freely, so a player back from
    //   the dead rejoins. Stopped before they move, not put back after: what would carry them outward (their steps, a
    //   roll's or an attack's lunge, a push, the speed they have) loses its outward part within HoldBack of the edge
    //   (Hold, before the game moves them), so they slide along it. Lightning Step, the comet and the twins' rush and
    //   comet end at it (Stops, Clamp). So no judgment for leaving any more (TwinsJudgment), no deaths at the edge from
    //   a push or a blink, and no decoy outside: the twins don't see who isn't in
    // The ring is drawn by the altar while its twins live (RingLook)
    internal static class TwinsRing
    {
        // Twins without an arena: their fight is this near either of them (meters)
        internal const float FreeRadius = 50f;
        // The edge holds this far inside the ring (meters), so nobody held is ever out of it by a step's worth; a blink
        // may end a little nearer (along the edge, it would otherwise not go at all); past SnapBack they're put back at
        // the hold, and past LetGo beyond the ring they aren't held (a cheat took them: debug flying isn't held either)
        private const float HoldBack = 0.5f;
        private const float BlinkBack = 0.2f;
        private const float SnapBack = 0.1f;
        private const float LetGo = 3f;
        // Two arenas' middles nearer than this are the same arena
        private const float SameArena = 1f;

        private static float HoldRadius => TwinsAltars.ArenaRadius - HoldBack;

        // The local player's hold (TwinsDuel, every frame of their fight)
        private static bool s_holdPlayer;
        private static Vector3 s_playerArena;

        // Whether `point` is in this twin's fight: in its arena's ring, or without an arena, within FreeRadius of it
        internal static bool Inside(CallerBoss boss, Vector3 point)
        {
            if (!boss)
            {
                return false;
            }
            return TwinsBond.Arena(boss, out Vector3 arena)
                ? Utils.DistanceXZ(point, arena) <= TwinsAltars.ArenaRadius
                : Vector3.Distance(point, boss.transform.position) <= FreeRadius;
        }

        // Whether `point` is in the fight of a living twin on this game, and the nearest such twin
        internal static bool Fight(Vector3 point, out CallerBoss twin)
        {
            twin = null;
            float best = float.PositiveInfinity;
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (!boss || !boss.m_humanoid || boss.m_humanoid.IsDead() || !boss.m_nview || !boss.m_nview.IsValid() || !Inside(boss, point))
                {
                    continue;
                }
                float distance = Vector3.Distance(point, boss.transform.position);
                if (distance < best)
                {
                    best = distance;
                    twin = boss;
                }
            }
            return twin;
        }

        // Whether twins called into the arena at `arena` live, on this game (the altar: its ring drawn, its dais busy)
        internal static bool Called(Vector3 arena)
        {
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (boss && boss.m_humanoid && !boss.m_humanoid.IsDead() && TwinsBond.Arena(boss, out Vector3 theirs)
                    && Utils.DistanceXZ(theirs, arena) < SameArena)
                {
                    return true;
                }
            }
            return false;
        }

        // The local player's hold, on or off (TwinsDuel)
        internal static void HoldPlayer(bool hold, Vector3 arena)
        {
            s_holdPlayer = hold;
            s_playerArena = arena;
        }

        // Whether the ring holds this character on this game, and its middle: the local player in a ring's fight, or a
        // twin of an arena that this game runs
        private static bool Held(Character character, out Vector3 arena)
        {
            arena = s_playerArena;
            if (character == Player.m_localPlayer)
            {
                return s_holdPlayer;
            }
            CallerBoss twin = CallerBoss.Of(character);
            return twin && twin.m_nview && twin.m_nview.IsValid() && twin.m_nview.IsOwner() && TwinsBond.Arena(twin, out arena);
        }

        // Whether a blink of `character` (Lightning Step, the comet, a twin's rush: LightningStep.Walk) stops before
        // `to`: held, and it would end past the edge
        internal static bool Stops(Character character, Vector3 to)
        {
            return CallerBoss.All.Count > 0 && Held(character, out Vector3 arena)
                && Utils.DistanceXZ(to, arena) > TwinsAltars.ArenaRadius - BlinkBack;
        }

        // `point` brought within the edge, for a held character's landing (a twin's rush, its comet past its mark)
        internal static Vector3 Clamp(Character character, Vector3 point)
        {
            if (!Held(character, out Vector3 arena))
            {
                return point;
            }
            Vector3 flat = point - arena;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance <= HoldRadius)
            {
                return point;
            }
            Vector3 inside = arena + flat / distance * HoldRadius;
            inside.y = point.y;
            return inside;
        }

        // Before the game moves a character (every physics step, on the game that runs it): held and at the edge, nothing
        // carries it outward
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        private static class Edge
        {
            private static void Prefix(Character __instance)
            {
                if (CallerBoss.All.Count == 0 || !Held(__instance, out Vector3 arena) || __instance.IsDead() || __instance.IsDebugFlying())
                {
                    return;
                }
                Hold(__instance, arena);
            }
        }

        private static void Hold(Character character, Vector3 arena)
        {
            Vector3 position = character.transform.position;
            Vector3 flat = position - arena;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < HoldRadius || distance > TwinsAltars.ArenaRadius + LetGo)
            {
                return;
            }
            Vector3 outward = flat / distance;
            character.m_moveDir = Inward(character.m_moveDir, outward);
            character.m_currentVel = Inward(character.m_currentVel, outward);
            character.m_rootMotion = Inward(character.m_rootMotion, outward);
            character.m_pushForce = Inward(character.m_pushForce, outward);
            Rigidbody body = character.m_body;
            if (body)
            {
                body.linearVelocity = Inward(body.linearVelocity, outward);
            }
            if (distance > TwinsAltars.ArenaRadius - SnapBack)
            {
                Vector3 back = arena + outward * HoldRadius;
                back.y = position.y;
                character.transform.position = back;
                if (body)
                {
                    body.position = back;
                }
            }
        }

        // `vector` without what of it points along `outward`
        private static Vector3 Inward(Vector3 vector, Vector3 outward)
        {
            float along = Vector3.Dot(vector, outward);
            return along > 0f ? vector - outward * along : vector;
        }
    }
}
