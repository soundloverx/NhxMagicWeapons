using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // A boss's telegraphed spell (the user, 2026-11-14: "we don't want the players to be instantly targeted and hit. We
    // could stick to [...] the secondary lightning hit, but with some form of rune on the ground which appears 2 seconds
    // before the lightning hits"): a rune circle on the ground where its target stood, as wide as what it hits, lighting
    // up ring by ring (the Firecaller's sigil's rings: ChannelSigil.Textures) and pulsing faster as it charges, in the
    // storm's blue or Muspelheim's orange; then the Staff of Storms' strike falls on it (Staff.Strike, at the staff's level
    // 20 look), or it erupts in the Wrath of Muspelheim's blast (FireBeam.ShowBlastOfRadius), and everything within it but
    // the caster's friends takes the hit (LightningStrike.HitArea). Out of it in time, a player isn't touched.
    // With no delay and no caster, only its look, at once: the bosses' smites (TwinsDuel.Smite), whose damage comes apart.
    // Networked, so every game draws it (and each shows the blast itself: it isn't networked); the game that created it
    // (the boss's: BossCall) removes it. Its delay, size and hit are in its ZDO.
    // Who it hits, each game judges for its own player (Claude's review, 2026-11-17, its #1, built at the user's word: "I
    // would like to implement #1 [...] my latency is around 40ms to the server and my friend's is around 180ms"): the
    // game that created it struck everything within it, as it saw them, and it saw the other player where they had been
    // 0.2 s or more before, so a rune could hit them after they had left it on their screen. Now every game's rune strikes
    // its delay after it appeared there, and hits that game's player if they're within it then and not rolling (HitLocal),
    // with the hit the creating game wrote in its ZDO (SetHit): the warning they saw is the one they get, and where they
    // stand is where they are. Nothing but players is hit any more (the arena's creatures are held: TwinsStasis)
    internal class ElementRune : MonoBehaviour
    {
        public bool m_fire;
        public float m_delay = 2f;
        public float m_radius = 3.5f;
        // The storm's strike from the sky (networked, created by the owner)
        public GameObject m_strike;
        public int m_strikeLevel = 20;

        // On the creating game, right after creating it
        internal Character m_caster;
        internal HitData m_hit;

        private static readonly int s_delayKey = "nhx_rune_delay".GetStableHashCode();
        private static readonly int s_radiusKey = "nhx_rune_radius".GetStableHashCode();
        // Its hit, as RPC_Damage sends one (HitData.Serialize), for every game to deal to its own player
        private static readonly int s_hitKey = "nhx_rune_hit".GetStableHashCode();
        // A player this much past its edge as it strikes is a near miss, for the log (meters)
        private const float NearMiss = 1.5f;

        // The disc it's drawn on, as the sigil's: rings of vertices dropped onto the ground (within GroundReach up or down
        // of its middle) and lifted a little: 15 cm (6 at first, as the sigil's; the user, 2026-11-15: "The runes on the
        // floor aren't always visible because of the terrain not being perfectly even and textures overlapping with the
        // ground. [...] spawn the runes slightly above ground")
        private const int Rings = 8;
        private const int Segments = 56;
        private const float GroundReach = 2f;
        private const float Lift = 0.15f;
        // The rings light up one after another over the delay, each fading in this fast; they turn (degrees a second, the
        // middle first), faster as the strike nears
        private const float FadeIn = 0.25f;
        private static readonly float[] Spin = { 20f, -14f, 9f };
        private const float SpinUp = 2f;
        // Its colors (the shader draws twice this), a pulse quickening from PulseFrom to PulseTo a second, PulseDepth deep,
        // the flash as it strikes, and how long it takes to fade after
        internal static readonly Color Storm = new Color(0.35f, 0.55f, 1f);
        internal static readonly Color Fire = new Color(1f, 0.42f, 0.09f);
        private const float PulseFrom = 1.5f;
        private const float PulseTo = 7f;
        private const float PulseDepth = 0.35f;
        private const float Flash = 2.5f;
        private const float FadeOut = 0.5f;
        private const float LightIntensity = 2f;
        private const float LightRange = 9f;
        // Gone this long after it strikes, whatever happens
        private const float Lifetime = 3f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");
        private static Material[] s_materials;

        private ZNetView m_nview;
        private MeshRenderer[] m_renderers;
        private Vector2[] m_flat;
        private Vector2[] m_uvs;
        private Mesh[] m_layerMeshes;
        private float[] m_turns;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private float m_age;
        private float m_phase;
        private bool m_struck;
        private bool m_started;

        // On the creating game: a rune of `prefab`'s kind at `point`, striking after `delay`, `radius` around
        internal static ElementRune Create(GameObject prefab, Vector3 point, float delay, float radius)
        {
            ElementRune rune = Instantiate(prefab, point, Quaternion.identity).GetComponent<ElementRune>();
            ZDO zdo = rune && rune.m_nview && rune.m_nview.IsValid() ? rune.m_nview.GetZDO() : null;
            if (zdo != null)
            {
                zdo.Set(s_delayKey, delay);
                zdo.Set(s_radiusKey, radius);
            }
            return rune;
        }

        // On the creating game, right after Create: the rune strikes for `caster`, with `hit`, on every game
        internal void SetHit(Character caster, HitData hit)
        {
            m_caster = caster;
            m_hit = hit;
            if (hit == null || !m_nview || !m_nview.IsValid())
            {
                return;
            }
            if (caster)
            {
                hit.SetAttacker(caster);
            }
            ZPackage package = new ZPackage();
            hit.Serialize(ref package);
            m_nview.GetZDO().Set(s_hitKey, package.GetArray());
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        // After the creating game has set its delay and size (Create), and on other games once its ZDO has come
        private void Start()
        {
            if (m_nview && m_nview.IsValid())
            {
                ZDO zdo = m_nview.GetZDO();
                m_delay = zdo.GetFloat(s_delayKey, m_delay);
                m_radius = zdo.GetFloat(s_radiusKey, m_radius);
            }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Build();
            }
            m_started = true;
        }

        private void OnDestroy()
        {
            if (m_layerMeshes != null)
            {
                foreach (Mesh mesh in m_layerMeshes)
                {
                    Destroy(mesh);
                }
            }
        }

        private void Update()
        {
            if (!m_started)
            {
                return;
            }
            float dt = Time.deltaTime;
            m_age += dt;
            bool owner = m_nview && m_nview.IsValid() && m_nview.IsOwner();
            if (!m_struck && m_age >= m_delay)
            {
                m_struck = true;
                Strike(owner);
            }
            if (owner && m_age > m_delay + Lifetime)
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            Draw(dt);
        }

        // The lightning or the blast, and the hit for this game's player if they're within it
        private void Strike(bool owner)
        {
            Vector3 point = transform.position;
            if (m_fire)
            {
                // Every game shows its own: the blast isn't networked
                if (Wrath.Beam && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    Wrath.Beam.ShowBlastOfRadius(point, m_radius, 1f);
                }
            }
            else if (owner && m_strike)
            {
                StrikeLightning.SetLevel(Instantiate(m_strike, point, Quaternion.identity), m_strikeLevel);
            }
            HitLocal(point);
        }

        // This game's player, if they're within it as it strikes here, and not rolling: its hit, on their own game
        private void HitLocal(Vector3 point)
        {
            Player player = Player.m_localPlayer;
            HitData hit = player && !player.IsDead() ? Hit() : null;
            Character caster = hit != null ? hit.GetAttacker() : null;
            // A look only (no hit), or a caster gone (dead, or left): nothing is hit
            if (!caster || caster.IsDead())
            {
                return;
            }
            Collider body = player.GetCollider();
            if (!body)
            {
                return;
            }
            Vector3 closest = body.ClosestPoint(point);
            float distance = Vector3.Distance(closest, point);
            string rune = $"{TwinsLog.Name(caster)}'s {(m_fire ? "fire" : "storm")} rune ({m_radius:0.#} m)";
            if (distance > m_radius)
            {
                if (distance <= m_radius + NearMiss)
                {
                    TwinsLog.Event($"{TwinsLog.Name(player)} is clear of {rune} by {distance - m_radius:0.0} m as it strikes.");
                }
                return;
            }
            // Rolling through it (StaffHits.IsValidTarget asks IsDodgeInvincible), or anything else that spares them
            if (!StaffHits.IsValidTarget(caster, player))
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} {(player.IsDodgeInvincible() ? "rolls through" : "is spared by")} {rune}, {m_radius - distance:0.0} m inside its edge.");
                return;
            }
            Vector3 dir = point - caster.transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : caster.transform.forward;
            Vector3 away = closest - point;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : dir;
            TwinsLog.Event($"{TwinsLog.Name(player)} is struck by {rune}, {m_radius - distance:0.0} m inside its edge.");
            StaffHits.Damage(caster, hit, body, closest, away, out _);
        }

        // Its hit, from its ZDO (or as the creating game set it), or null for a rune that's only a look
        private HitData Hit()
        {
            byte[] bytes = m_nview && m_nview.IsValid() ? m_nview.GetZDO().GetByteArray(s_hitKey) : null;
            if (bytes != null && bytes.Length > 0)
            {
                ZPackage package = new ZPackage(bytes);
                HitData hit = new HitData();
                hit.Deserialize(ref package);
                return hit;
            }
            return m_hit != null ? m_hit.Clone() : null;
        }

        // The sigil's three rings as additive materials, made once (also the stasis runes': TwinsStasis); null without them
        internal static Material[] Materials()
        {
            Texture2D[] textures = ChannelSigil.Textures;
            if (textures == null || textures.Length == 0)
            {
                return null;
            }
            if (s_materials == null)
            {
                s_materials = new Material[textures.Length];
                Material fallback = Wrath.Look ? Wrath.Look.m_glowMaterial : null;
                for (int i = 0; i < textures.Length; i++)
                {
                    Material material = LineMaterials.Additive(fallback);
                    material.mainTexture = textures[i];
                    // With soft particles on, the shader fades what's close in front of the ground: only the last centimeters
                    if (material.HasProperty("_InvFade"))
                    {
                        material.SetFloat("_InvFade", 30f);
                    }
                    s_materials[i] = material;
                }
            }
            return s_materials;
        }

        private void Build()
        {
            Texture2D[] textures = ChannelSigil.Textures;
            if (Materials() == null)
            {
                return;
            }
            m_block = new MaterialPropertyBlock();

            int count = 1 + Rings * Segments;
            Vector3[] vertices = new Vector3[count];
            m_flat = new Vector2[count];
            m_uvs = new Vector2[count];
            for (int ring = 1; ring <= Rings; ring++)
            {
                float radius = m_radius * ring / Rings;
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / Segments;
                    m_flat[1 + (ring - 1) * Segments + segment] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }
            // On the ground as it is here: it doesn't move
            Vector3 center = transform.position;
            for (int i = 0; i < count; i++)
            {
                Vector3 above = center + new Vector3(m_flat[i].x, GroundReach, m_flat[i].y);
                float height = 0f;
                if (Physics.Raycast(above, Vector3.down, out RaycastHit hit, 2f * GroundReach, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    height = hit.point.y - center.y;
                }
                vertices[i] = new Vector3(m_flat[i].x, height + Lift, m_flat[i].y);
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

            m_renderers = new MeshRenderer[textures.Length];
            m_layerMeshes = new Mesh[textures.Length];
            m_turns = new float[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                GameObject go = new GameObject("layer" + i);
                go.transform.SetParent(transform, false);
                Mesh mesh = new Mesh { name = "NhxElementRune" };
                mesh.MarkDynamic();
                mesh.vertices = vertices;
                mesh.uv = m_uvs;
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = s_materials[i];
                renderer.sortingOrder = i;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                m_renderers[i] = renderer;
                m_layerMeshes[i] = mesh;
                m_turns[i] = Random.Range(0f, 360f);
            }

            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            m_light = lightObject.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = m_fire ? (Wrath.Look ? Wrath.Look.m_lightColor : Fire) : (Staff.Look ? Staff.Look.m_lightColor : Storm);
            m_light.range = LightRange;
            m_light.intensity = 0f;
            m_light.shadows = LightShadows.None;
        }

        private void Draw(float dt)
        {
            if (m_renderers == null)
            {
                return;
            }
            float progress = m_delay > 0f ? Mathf.Clamp01(m_age / m_delay) : 1f;
            float after = m_age - m_delay;
            // Charging: pulsing faster and faster; struck: a flash, fading
            float bright;
            float fade;
            if (after < 0f)
            {
                m_phase += Mathf.Lerp(PulseFrom, PulseTo, progress * progress) * dt;
                bright = 1f + PulseDepth * Mathf.Sin(2f * Mathf.PI * m_phase) * progress;
                fade = 1f;
            }
            else
            {
                fade = Mathf.Clamp01(1f - after / FadeOut);
                bright = Mathf.Lerp(1f, Flash, fade);
            }
            Color color = m_fire ? Fire : Storm;
            float brightest = 0f;
            for (int i = 0; i < m_renderers.Length; i++)
            {
                // At once for a smite (no delay): all its rings flash together
                float since = m_age - m_delay * i / m_renderers.Length;
                float alpha = (m_delay > 0f ? Mathf.Clamp01(since / FadeIn) : 1f) * fade;
                brightest = Mathf.Max(brightest, alpha);
                MeshRenderer renderer = m_renderers[i];
                renderer.enabled = alpha > 0f;
                if (alpha <= 0f)
                {
                    continue;
                }
                m_turns[i] += Spin[i % Spin.Length] * (1f + SpinUp * progress) * dt;
                float turn = m_turns[i] * Mathf.Deg2Rad;
                float cos = Mathf.Cos(turn) * 0.5f / m_radius;
                float sin = Mathf.Sin(turn) * 0.5f / m_radius;
                for (int v = 0; v < m_uvs.Length; v++)
                {
                    Vector2 flat = m_flat[v];
                    m_uvs[v] = new Vector2(0.5f + flat.x * cos - flat.y * sin, 0.5f + flat.x * sin + flat.y * cos);
                }
                m_layerMeshes[i].uv = m_uvs;
                Color tint = color * bright;
                tint.a = 0.5f * alpha;
                m_block.SetColor(s_tint, tint);
                renderer.SetPropertyBlock(m_block);
            }
            m_light.enabled = brightest > 0f;
            m_light.intensity = LightIntensity * brightest * bright * (after < 0f ? progress : 1f);
        }
    }
}
