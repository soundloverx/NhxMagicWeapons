using System.Collections.Generic;
using HarmonyLib;
using NhxMagicWeapons.Staves;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // The elements hold the arena (the user, 2026-11-15, of the creatures that wander into the twins' fight: "what if we
    // let creatures spawn and enter the arena, but then they are put in stasis and effectively their AI gets disabled
    // until the end of the fight (when the bosses are either killed or despawn) [...] They could also get some dark red
    // runes at their feet to suggest that"). While an altar's twins live (their arena: TwinsBond.Arena), every creature
    // within its ring, wild or tamed (not a player or a twin), come in or spawned there, stands still where it is, a
    // flyer hanging in the air: its AI waits (MonsterAI, AnimalAI). A tame following its player in stops at the ring; a
    // ridden mount too, its rider on it (riding goes through the AI: Sadle.UpdateRiding), as the user decided
    // (2026-11-15: "Mounts are not allowed, just like unmounted tames. if they get frozen, the player should dismount and
    // fight or flee"). It can't be hurt (no loot from creatures that can't
    // fight back), and the staves pass it by (StaffHits.IsValidTarget: no bolt, chain, tendril or flame stops on it). A
    // dark red rune turns slowly at its feet (StasisRune). Once the twins are both gone (dead or vanished), it's free.
    // Every game works out who's held from where things are (no ZDO): the creature's own game holds its AI and refuses
    // its hits, every game draws the runes and spares it from its staves.
    // The twins walk through what's held (the user, 2026-11-15: "I feel like the "froezen" tames and enemies in the arena
    // affect pathing for the bosses [...] Perhaps the bosses should clip through other entities?"): a held creature
    // stands its ground against any push, and the game's paths don't go around creatures. So their colliders and the
    // twins' ignore each other (Physics.IgnoreCollision) while it's held; players still collide with both
    // The bats of NhxExtraJewels' Cover of Darkness gem go instead (the user, 2026-11-20: "they appear quite often. It can
    // cause a spam and also put many tiny frozen bats around the arena. DO you think the twins could just despawn them as
    // they appear without any voice lines or anything"; the gem rolls on every hit, the twins' too). One in a ring of
    // living twins is removed, not held, from their summon on (no HoldAfter: no line to wait for), by the game that runs
    // it: as it starts (DismissNew), so it's gone before it's seen, or at the next refresh if it flies in. No effect, no
    // rune, no line. Only the gem's bats (its mark in their ZDO: GemSummonKey); a wild bat is held as any creature, and
    // so is the gem's spirit wolf (one at a time, then a cooldown)
    internal static class TwinsStasis
    {
        // Held from within the ring; kept held until this far past it (a flyer may drift, a push may move it)
        private const float HoldMargin = 10f;
        private const float Interval = 0.25f;
        // killall still kills them (as the twins: TwinsRules)
        private const float CheatKill = 1e9f;

        // The hold begins this long after the twins are summoned (the user, 2026-11-15: "6 seconds after the bosses spawn,
        // they will start placing tames and other enemies that roam into the arena in stasis"), as they finish speaking
        private const float HoldAfter = 6f;
        // Two twins' arenas closer than this are the same
        private const float SameArena = 1f;

        // NhxExtraJewels' mark on its summons and its number for a bat (its EffectHelpers\Summons.cs: KindKey, Kind.Bat),
        // read without referencing that mod: keep the two in step
        private static readonly int GemSummonKey = "NhxExtraJewels_summon".GetStableHashCode();
        private const int GemBat = 2;

        private static readonly HashSet<Character> s_held = new HashSet<Character>();
        private static readonly Dictionary<Character, StasisRune> s_runes = new Dictionary<Character, StasisRune>();
        // The arenas holding their creatures (their twins summoned HoldAfter ago), and all those of living twins, which
        // the gem's bats leave at once
        private static readonly List<Vector3> s_arenas = new List<Vector3>();
        private static readonly List<Vector3> s_liveArenas = new List<Vector3>();
        private static readonly List<Character> s_dismissing = new List<Character>();
        private static int s_dismissed;
        // For each arena, the twin that speaks for it (the living one with the lowest id, the same on every game), and
        // whether it has just held new creatures
        private static readonly List<CallerBoss> s_speakers = new List<CallerBoss>();
        private static readonly List<int> s_cast = new List<int>();
        private static readonly List<string> s_newlyHeld = new List<string>();
        private static readonly Dictionary<Character, Collider[]> s_colliders = new Dictionary<Character, Collider[]>();
        private static readonly List<Collider> s_twinColliders = new List<Collider>();
        private static readonly List<Character> s_released = new List<Character>();
        // The twins whose colliders s_twinColliders holds, and the held creatures already set to pass through them: the
        // colliders are looked up, and each pair told to ignore the other, only when a creature is newly held or the
        // twins change (one raised, loaded or gone), not at every refresh
        private static readonly List<Character> s_twinsNow = new List<Character>();
        private static readonly List<Character> s_twinsBefore = new List<Character>();
        private static readonly HashSet<Character> s_apart = new HashSet<Character>();
        private static float s_timer;

        internal static bool Held(Character character) => character && s_held.Contains(character);

        // Every frame (Plugin.Update)
        internal static void Update(float dt)
        {
            s_timer -= dt;
            if (s_timer > 0f)
            {
                return;
            }
            s_timer = Interval;
            s_arenas.Clear();
            s_liveArenas.Clear();
            s_speakers.Clear();
            s_cast.Clear();
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (!boss || !boss.m_humanoid || boss.m_humanoid.IsDead() || !TwinsBond.Arena(boss, out Vector3 arena))
                {
                    continue;
                }
                if (!s_liveArenas.Exists(other => Utils.DistanceXZ(other, arena) < SameArena))
                {
                    s_liveArenas.Add(arena);
                }
                if (TwinsBond.Since(boss) < HoldAfter)
                {
                    continue;
                }
                int index = s_arenas.FindIndex(other => Utils.DistanceXZ(other, arena) < SameArena);
                if (index < 0)
                {
                    s_arenas.Add(arena);
                    s_speakers.Add(boss);
                    s_cast.Add(0);
                }
                else if (boss.m_nview.GetZDO().m_uid.CompareTo(s_speakers[index].m_nview.GetZDO().m_uid) < 0)
                {
                    s_speakers[index] = boss;
                }
            }

            // The gem's bats that came in from outside, or were there before the twins
            if (s_liveArenas.Count > 0)
            {
                s_dismissing.Clear();
                foreach (Character character in Character.GetAllCharacters())
                {
                    if (Unwelcome(character))
                    {
                        s_dismissing.Add(character);
                    }
                }
                foreach (Character bat in s_dismissing)
                {
                    Dismiss(bat);
                }
            }
            if (s_dismissed > 0)
            {
                TwinsLog.Event($"Dismissed {s_dismissed} gem bat(s) from the arena.");
                s_dismissed = 0;
            }

            UpdateTwinColliders();
            s_released.Clear();
            foreach (Character held in s_held)
            {
                if (!held || held.IsDead() || !Within(held, TwinsAltars.ArenaRadius + HoldMargin))
                {
                    s_released.Add(held);
                }
            }
            foreach (Character released in s_released)
            {
                s_held.Remove(released);
                s_apart.Remove(released);
                Collide(released, true);
                s_colliders.Remove(released);
            }
            if (s_released.Count > 0)
            {
                TwinsLog.Event($"Released {s_released.Count} creature(s) from stasis.");
            }
            s_newlyHeld.Clear();
            if (s_arenas.Count > 0)
            {
                foreach (Character character in Character.GetAllCharacters())
                {
                    int arena = Holdable(character) ? ArenaOf(s_arenas, character, TwinsAltars.ArenaRadius) : -1;
                    if (arena >= 0 && s_held.Add(character))
                    {
                        s_cast[arena]++;
                        s_newlyHeld.Add(TwinsLog.Name(character) + (character.IsTamed() ? " (tame)" : ""));
                    }
                }
            }
            if (s_newlyHeld.Count > 0)
            {
                TwinsLog.Event($"Held in stasis: {string.Join(", ", s_newlyHeld)} ({s_held.Count} in all).");
            }
            // The twins pass through them: those newly held, or all again when the twins have changed (a twin raised since)
            foreach (Character held in s_held)
            {
                if (s_apart.Add(held))
                {
                    Collide(held, false);
                }
            }
            // A new hold: one of the twins says so (TwinsVoices), from the game that runs the twin speaking for the arena
            for (int i = 0; i < s_arenas.Count; i++)
            {
                if (s_cast[i] > 0 && s_speakers[i].m_nview.IsOwner())
                {
                    TwinsVoices.Stasis(s_arenas[i], s_cast[i]);
                }
            }
            UpdateRunes();
        }

        // The twins' colliders, looked up again only when the twins on this game have changed; then every held creature
        // is set apart from them again (s_apart), and they're set apart from each other and the altars (PassThrough), as
        // they are when an altar comes or goes
        private static int s_altarsSeen = -1;

        private static void UpdateTwinColliders()
        {
            s_twinsNow.Clear();
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (boss && boss.m_humanoid)
                {
                    s_twinsNow.Add(boss.m_humanoid);
                }
            }
            bool same = s_twinsNow.Count == s_twinsBefore.Count;
            for (int i = 0; same && i < s_twinsNow.Count; i++)
            {
                same = s_twinsNow[i] == s_twinsBefore[i];
            }
            if (same && s_altarsSeen == TwinsAltar.Version)
            {
                return;
            }
            s_altarsSeen = TwinsAltar.Version;
            if (!same)
            {
                s_twinsBefore.Clear();
                s_twinsBefore.AddRange(s_twinsNow);
                s_twinColliders.Clear();
                foreach (Character twin in s_twinsNow)
                {
                    s_twinColliders.AddRange(Solid(twin));
                }
                s_apart.Clear();
            }
            PassThrough();
        }

        // The twins pass through each other and through what stands on their arenas, the ground aside: the altar's
        // pillars, stones and crystals (TwinsAltar.Solids). The user, 2026-11-22: "make the bosses only collide with the
        // edge of the arena", as they now walk straight at where they're going there (TwinsAI.Straight); held creatures
        // they already passed through (Collide). Players still stop them, and the ring holds them (TwinsRing)
        private static void PassThrough()
        {
            for (int i = 0; i < s_twinsNow.Count; i++)
            {
                for (int j = i + 1; j < s_twinsNow.Count; j++)
                {
                    Ignore(Solid(s_twinsNow[i]), Solid(s_twinsNow[j]));
                }
            }
            foreach (TwinsAltar altar in TwinsAltar.All)
            {
                if (altar && altar.gameObject.activeInHierarchy)
                {
                    Ignore(s_twinColliders, altar.Solids);
                }
            }
        }

        // Each of `these` and each of `those` pass through each other (both must be active: the game says so otherwise)
        private static void Ignore(IList<Collider> these, IList<Collider> those)
        {
            foreach (Collider one in these)
            {
                if (!Active(one))
                {
                    continue;
                }
                foreach (Collider other in those)
                {
                    if (Active(other))
                    {
                        Physics.IgnoreCollision(one, other, true);
                    }
                }
            }
        }

        private static bool Active(Collider collider) => collider && collider.enabled && collider.gameObject.activeInHierarchy;

        // Whether the twins and `character` bump into each other
        private static void Collide(Character character, bool collide)
        {
            if (!character)
            {
                return;
            }
            Collider[] mine = Solid(character);
            foreach (Collider twin in s_twinColliders)
            {
                foreach (Collider collider in mine)
                {
                    if (collider && twin)
                    {
                        Physics.IgnoreCollision(collider, twin, !collide);
                    }
                }
            }
        }

        // A character's colliders that bump (not its triggers)
        private static Collider[] Solid(Character character)
        {
            if (s_colliders.TryGetValue(character, out Collider[] colliders))
            {
                return colliders;
            }
            List<Collider> solid = new List<Collider>();
            foreach (Collider collider in character.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger)
                {
                    solid.Add(collider);
                }
            }
            colliders = solid.ToArray();
            // The twins' are looked up at each refresh (they come and go); the held creatures' are kept while held
            if (!CallerBoss.IsTwin(character))
            {
                s_colliders[character] = colliders;
            }
            return colliders;
        }

        // Which of `arenas` `character` is within `radius` of, or -1
        private static int ArenaOf(List<Vector3> arenas, Character character, float radius)
        {
            Vector3 position = character.transform.position;
            for (int i = 0; i < arenas.Count; i++)
            {
                if (Utils.DistanceXZ(position, arenas[i]) <= radius)
                {
                    return i;
                }
            }
            return -1;
        }

        // Tames too (the user: "The tames should also freeze when the bosses spawn. My friend loves carrying an army of
        // fairies with him and I don't want them to be of any use during the fight haha"): anything with an AI but the
        // players and the twins, whoever it follows; not the gem's bats, which go (Unwelcome), nor anything already gone
        // from the network (a bat just dismissed, there until the frame ends)
        private static bool Holdable(Character character)
        {
            return character && character.m_nview && character.m_nview.IsValid() && !character.IsDead() && !character.IsPlayer()
                && !CallerBoss.IsTwin(character) && character.GetBaseAI() && !IsGemBat(character);
        }

        private static bool Within(Character character, float radius) => ArenaOf(s_arenas, character, radius) >= 0;

        // A bat of NhxExtraJewels' Cover of Darkness gem, from the mark it leaves in the bat's ZDO
        private static bool IsGemBat(Character character)
        {
            ZDO zdo = character && character.m_nview && character.m_nview.IsValid() ? character.m_nview.GetZDO() : null;
            return zdo != null && zdo.GetInt(GemSummonKey) == GemBat;
        }

        // A gem bat in a ring of living twins
        private static bool Unwelcome(Character character)
        {
            return IsGemBat(character) && ArenaOf(s_liveArenas, character, TwinsAltars.ArenaRadius) >= 0;
        }

        // Gone, silently (no death, effect or drop), on the game that runs it; the others lose it with its ZDO
        private static void Dismiss(Character bat)
        {
            if (bat.m_nview.IsOwner())
            {
                ZNetScene.instance.Destroy(bat.gameObject);
                s_dismissed++;
            }
        }

        // A gem bat starting in a ring goes before it's drawn: NhxExtraJewels marks it right after making it, and Start
        // comes after that, before its first frame
        private static void DismissNew(Character character)
        {
            if (s_liveArenas.Count > 0 && Unwelcome(character))
            {
                Dismiss(character);
            }
        }

        // A rune for each creature held, on every game but a dedicated server; the rest fade away
        private static void UpdateRunes()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }
            s_released.Clear();
            foreach (KeyValuePair<Character, StasisRune> entry in s_runes)
            {
                if (!entry.Key || !s_held.Contains(entry.Key) || !entry.Value)
                {
                    s_released.Add(entry.Key);
                    if (entry.Value)
                    {
                        entry.Value.Release();
                    }
                }
            }
            foreach (Character released in s_released)
            {
                s_runes.Remove(released);
            }
            foreach (Character held in s_held)
            {
                if (!s_runes.ContainsKey(held))
                {
                    s_runes[held] = StasisRune.Create(held);
                }
            }
        }

        // A held creature's AI waits, standing (on the game that runs it)
        private static bool Hold(BaseAI ai, ref bool result)
        {
            if (!Held(ai.m_character))
            {
                return true;
            }
            ai.StopMoving();
            result = true;
            return false;
        }

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class HoldMonster
        {
            private static bool Prefix(MonsterAI __instance, ref bool __result) => Hold(__instance, ref __result);
        }

        [HarmonyPatch(typeof(AnimalAI), nameof(AnimalAI.UpdateAI))]
        private static class HoldAnimal
        {
            private static bool Prefix(AnimalAI __instance, ref bool __result) => Hold(__instance, ref __result);
        }

        // Character.Start is empty and Humanoid.Start (a biting creature's, as a bat) doesn't call it: both
        [HarmonyPatch(typeof(Character), nameof(Character.Start))]
        private static class DismissNewCharacter
        {
            private static void Postfix(Character __instance) => DismissNew(__instance);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
        private static class DismissNewHumanoid
        {
            private static void Postfix(Humanoid __instance) => DismissNew(__instance);
        }

        // Nothing hurts a held creature, on its own game, before anything is taken
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Untouchable
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                return !Held(__instance) || (hit != null && hit.GetTotalDamage() >= CheatKill);
            }
        }
    }

    // The rune at a held creature's feet, on one game: the sigil's three rings in dark red, a little wider than the
    // creature, dropped onto the ground under it (again if it moves), turning slowly, breathing faintly; it fades in, and
    // out when the creature is free. An object of its own, following the creature; not networked
    internal class StasisRune : MonoBehaviour
    {
        private const int Rings = 6;
        private const int Segments = 40;
        // Its size: the creature's radius times this, plus this, within these bounds (meters)
        private const float RadiusScale = 1.5f;
        private const float RadiusAdd = 0.4f;
        private const float MinRadius = 0.8f;
        private const float MaxRadius = 4f;
        // As the twins' runes: dropped within this of the creature's feet, lifted this much; again past this move
        private const float GroundReach = 3f;
        private const float Lift = 0.15f;
        private const float Moved = 0.5f;
        // A flyer's rune is on the ground under it, looked for this far down
        private const float GroundSearch = 40f;
        private static readonly Color Crimson = new Color(0.5f, 0.03f, 0.03f);
        private static readonly float[] Spin = { 6f, -4f, 3f };
        private const float PulseRate = 0.25f;
        private const float PulseDepth = 0.15f;
        private const float FadeIn = 0.5f;
        private const float FadeOut = 0.8f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");

        private Character m_creature;
        private float m_radius;
        private Vector3 m_builtAt;
        private Vector2[] m_flat;
        private Vector2[] m_uvs;
        private Vector3[] m_vertices;
        private Mesh[] m_meshes;
        private MeshRenderer[] m_renderers;
        private float[] m_turns;
        private MaterialPropertyBlock m_block;
        private float m_alpha;
        private float m_age;
        private bool m_released;

        internal static StasisRune Create(Character creature)
        {
            StasisRune rune = new GameObject("NhxStasisRune").AddComponent<StasisRune>();
            rune.Build(creature);
            return rune;
        }

        // Free: it fades, then goes
        internal void Release()
        {
            m_released = true;
        }

        private void Build(Character creature)
        {
            m_creature = creature;
            m_radius = Mathf.Clamp(creature.GetRadius() * RadiusScale + RadiusAdd, MinRadius, MaxRadius);
            Material[] materials = ElementRune.Materials();
            if (materials == null)
            {
                return;
            }
            m_block = new MaterialPropertyBlock();
            int count = 1 + Rings * Segments;
            m_flat = new Vector2[count];
            m_uvs = new Vector2[count];
            m_vertices = new Vector3[count];
            for (int ring = 1; ring <= Rings; ring++)
            {
                float radius = m_radius * ring / Rings;
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / Segments;
                    m_flat[1 + (ring - 1) * Segments + segment] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }
            List<int> triangles = new List<int>();
            for (int segment = 0; segment < Segments; segment++)
            {
                int next = (segment + 1) % Segments;
                triangles.Add(0);
                triangles.Add(1 + next);
                triangles.Add(1 + segment);
                for (int ring = 1; ring < Rings; ring++)
                {
                    int inner = 1 + (ring - 1) * Segments;
                    int outer = inner + Segments;
                    triangles.Add(inner + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);
                    triangles.Add(outer + segment);
                }
            }
            int[] indices = triangles.ToArray();
            m_meshes = new Mesh[materials.Length];
            m_renderers = new MeshRenderer[materials.Length];
            m_turns = new float[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                GameObject go = new GameObject("layer" + i);
                go.transform.SetParent(transform, false);
                Mesh mesh = new Mesh { name = "NhxStasisRune" };
                mesh.MarkDynamic();
                mesh.vertices = m_vertices;
                mesh.uv = m_uvs;
                mesh.triangles = indices;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                renderer.sortingOrder = i;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                m_meshes[i] = mesh;
                m_renderers[i] = renderer;
                m_turns[i] = Random.Range(0f, 360f);
            }
            Drop();
        }

        // Onto the ground under the creature
        private void Drop()
        {
            Vector3 feet = m_creature.transform.position;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit below, GroundSearch, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
            {
                feet = below.point;
            }
            transform.position = feet;
            m_builtAt = m_creature.transform.position;
            for (int i = 0; i < m_flat.Length; i++)
            {
                Vector3 above = feet + new Vector3(m_flat[i].x, GroundReach, m_flat[i].y);
                float height = 0f;
                if (Physics.Raycast(above, Vector3.down, out RaycastHit hit, 2f * GroundReach, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    height = hit.point.y - feet.y;
                }
                m_vertices[i] = new Vector3(m_flat[i].x, height + Lift, m_flat[i].y);
            }
            foreach (Mesh mesh in m_meshes)
            {
                mesh.vertices = m_vertices;
                mesh.RecalculateBounds();
            }
        }

        private void OnDestroy()
        {
            if (m_meshes != null)
            {
                foreach (Mesh mesh in m_meshes)
                {
                    Destroy(mesh);
                }
            }
        }

        private void Update()
        {
            if (m_renderers == null)
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            if (!m_creature || m_creature.IsDead())
            {
                m_released = true;
            }
            m_alpha = m_released ? Mathf.MoveTowards(m_alpha, 0f, dt / FadeOut) : Mathf.MoveTowards(m_alpha, 1f, dt / FadeIn);
            if (m_released && m_alpha <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (m_creature && (m_creature.transform.position - m_builtAt).sqrMagnitude > Moved * Moved)
            {
                Drop();
            }
            m_age += dt;
            float bright = 1f + PulseDepth * Mathf.Sin(2f * Mathf.PI * PulseRate * m_age);
            for (int i = 0; i < m_renderers.Length; i++)
            {
                m_turns[i] += Spin[i % Spin.Length] * dt;
                float turn = m_turns[i] * Mathf.Deg2Rad;
                float cos = Mathf.Cos(turn) * 0.5f / m_radius;
                float sin = Mathf.Sin(turn) * 0.5f / m_radius;
                for (int v = 0; v < m_uvs.Length; v++)
                {
                    Vector2 flat = m_flat[v];
                    m_uvs[v] = new Vector2(0.5f + flat.x * cos - flat.y * sin, 0.5f + flat.x * sin + flat.y * cos);
                }
                m_meshes[i].uv = m_uvs;
                Color tint = Crimson * bright;
                tint.a = 0.5f * m_alpha;
                m_block.SetColor(s_tint, tint);
                m_renderers[i].SetPropertyBlock(m_block);
            }
        }
    }
}
