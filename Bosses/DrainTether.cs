using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using UnityEngine;
using UnityEngine.Rendering;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The altar's tether on a player it drains (TwinsDrain, the fight's change 8; the user, 2026-11-17: "a bloody red
    // magic effect tethers each player to the altar instead of each other and we can pretend their life force is being
    // drained"), on every game, from the player's ZDO (TwinsDrain.DrainedKey), on any player near a twin
    // (TwinsDuel.GiveLooks). Its look, picked by the user the same day from four drawn for them ("let's go with C, the
    // life motes"; the others were a coiling thread, a taut beam and veins on the ground): no solid line, but red motes
    // drifting out of the player's body and streaming up to the twins' crystals circling over the dais, along a faint
    // arching thread. More of them with every hit, and a burst at each (DrainHitsKey), its flare growing with the hit; a
    // red glow about the player, and another at the crystals as their life arrives. Once the tether goes, the motes out
    // finish their way. To the altar, never between the players, so it can't be taken for the Stormcaller's arcs
    // (TwinsPhase). Looks only
    internal class DrainTether : MonoBehaviour
    {
        // From this far over the body's middle (meters), to the dais's crystals, or this far over the altar's middle if
        // its looks aren't found; the way arches up by this share of its length
        private const float ChestLift = 0.3f;
        private const float AnchorLift = 2.5f;
        private const float Arch = 0.12f;
        // The motes: a second, and this many more for each hit of the tether; how fast (m/s); from how far about the body
        // (meters across, up and down), drawn in to the way as they go; how much they waver (meters, how fast); how big
        // (a core this wide, in a glow GlowScale as wide); how many at most
        // (24 and 16 at first; the user, 2026-11-17, after testing it: "can we make it a bit brighter or increase the number
        // of particles? Or both?")
        private const float BaseRate = 40f;
        private const float RatePerHit = 26f;
        private const float MinSpeed = 8f;
        private const float MaxSpeed = 12f;
        private const float BodySide = 0.3f;
        private const float BodyUp = 0.45f;
        private const float Wobble = 0.08f;
        private const float WobbleRate = 5f;
        private const float MinSize = 0.05f;
        private const float MaxSize = 0.1f;
        private const float GlowScale = 2.4f;
        private const float CoreScale = 0.8f;
        private const int MaxMotes = 900;
        // How bright the motes are drawn (the shader's tint, from its neutral: 1 at first)
        private const float Brightness = 1.6f;
        // A hit's burst: this many, and this many more for each hit so far, faster and from further about the body (18
        // and 4 at first)
        private const int BurstBase = 30;
        private const int BurstPerHit = 7;
        private const float BurstSpeed = 1.7f;
        private const float BurstSpread = 1.8f;
        // A hit's flare: from FlareFrom of its full at the first hit to all of it by FullFlareHits, gone within a second
        private const float FlareFrom = 0.45f;
        private const int FullFlareHits = 6;
        // The colors (the shader draws twice them), the thread under the motes, and the glows (meters, intensity)
        private static readonly Color Blood = new Color(0.85f, 0.06f, 0.04f);
        private static readonly Color Core = new Color(1f, 0.86f, 0.82f);
        private const int ThreadPoints = 24;
        private const float ThreadWidth = 0.1f;
        private const float ThreadAlpha = 0.2f;
        private const float LightRange = 3f;
        private const float LightIntensity = 2f;
        private const float AnchorRange = 5f;
        private const float AnchorIntensity = 2.5f;
        // Shown and hidden over this long (seconds); the ZDO read this often
        private const float FadeIn = 0.3f;
        private const float FadeOut = 0.6f;
        private const float ReadInterval = 0.1f;
        // An altar's looks this near (meters) to the twins' arena are its own
        private const float SameAltar = 1f;
        private const string CrystalsPath = "dais/twins";

        private static Material s_moteMaterial;
        private static Material s_threadMaterial;

        private struct Mote
        {
            public float m_p;
            public float m_speed;
            public float m_side;
            public float m_up;
            public float m_wob;
            public float m_size;
        }

        private Player m_player;
        private ParticleSystem m_particles;
        private ParticleSystem.Particle[] m_buffer;
        private readonly List<Mote> m_motes = new List<Mote>();
        private LineRenderer m_thread;
        private Light m_light;
        private Light m_anchorLight;
        private Vector3[] m_points;
        private Vector3 m_anchor;
        private bool m_hasAnchor;
        private bool m_drained;
        private bool m_built;
        private bool m_cleared = true;
        private int m_hits = -1;
        private int m_hitsAtTether;
        private int m_bursts;
        private float m_amp;
        private float m_flareAge = float.PositiveInfinity;
        private float m_shown;
        private float m_readTimer;
        private float m_debt;

        private void Awake()
        {
            m_player = GetComponent<Player>();
            if (!m_player || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            m_readTimer -= dt;
            if (m_readTimer <= 0f)
            {
                m_readTimer = ReadInterval;
                Read();
            }
            bool show = m_drained && m_hasAnchor;
            m_shown = Mathf.MoveTowards(m_shown, show ? 1f : 0f, dt / (show ? FadeIn : FadeOut));
            m_flareAge += dt;
            float flare = m_flareAge < 1f ? m_amp * (1f - m_flareAge) * (1f - m_flareAge) : 0f;
            if (m_shown <= 0f && m_motes.Count == 0)
            {
                Hide();
                return;
            }
            if (!m_built)
            {
                Build();
            }
            m_cleared = false;

            Vector3 start = m_player.GetCenterPoint() + Vector3.up * ChestLift;
            Vector3 end = m_anchor;
            Vector3 control = (start + end) * 0.5f + Vector3.up * (Arch * Vector3.Distance(start, end));
            if (show)
            {
                int tetherHits = Mathf.Max(0, m_hits - m_hitsAtTether);
                for (; m_bursts > 0; m_bursts--)
                {
                    for (int i = 0; i < BurstBase + BurstPerHit * tetherHits; i++)
                    {
                        Spawn(BurstSpeed, BurstSpread);
                    }
                }
                m_debt += (BaseRate + RatePerHit * tetherHits) * dt;
                while (m_debt >= 1f)
                {
                    m_debt -= 1f;
                    Spawn(1f, 1f);
                }
            }
            else
            {
                m_bursts = 0;
                m_debt = 0f;
            }
            DrawMotes(start, control, end, flare, dt);
            DrawThread(start, control, end, flare);
            m_light.enabled = m_shown > 0f;
            m_light.transform.position = start;
            m_light.intensity = LightIntensity * m_shown * (1f + flare);
            m_anchorLight.enabled = m_shown > 0f;
            m_anchorLight.transform.position = end;
            m_anchorLight.intensity = AnchorIntensity * (0.3f * m_shown + flare);
        }

        // Whether they're drained, a hit since the last read (a burst and a flare), and where the altar's crystals are
        private void Read()
        {
            ZDO zdo = m_player.m_nview && m_player.m_nview.IsValid() ? m_player.m_nview.GetZDO() : null;
            bool drained = zdo != null && !m_player.IsDead() && zdo.GetBool(TwinsDrain.DrainedKey);
            int hits = zdo != null ? zdo.GetInt(TwinsDrain.DrainHitsKey) : 0;
            if (drained && !m_drained)
            {
                m_hitsAtTether = hits;
            }
            if (drained && m_hits >= 0 && hits > m_hits)
            {
                m_bursts += hits - m_hits;
                m_amp = Mathf.Lerp(FlareFrom, 1f, Mathf.Clamp01((float)(hits - m_hitsAtTether) / FullFlareHits));
                m_flareAge = 0f;
            }
            m_hits = hits;
            m_drained = drained;
            // Fading out, the motes out finish their way to where it was
            if (drained)
            {
                m_hasAnchor = Anchor(out m_anchor);
            }
        }

        // The twins' crystals over the altar of the player's fight (its looks, RingLook's root), or over its middle
        private bool Anchor(out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (!TwinsRing.Fight(m_player.transform.position, out CallerBoss twin) || !TwinsBond.Arena(twin, out Vector3 arena))
            {
                return false;
            }
            anchor = arena + Vector3.up * AnchorLift;
            foreach (RingLook ring in RingLook.All)
            {
                if (ring && Utils.DistanceXZ(ring.transform.position, arena) < SameAltar)
                {
                    Transform crystals = ring.transform.Find(CrystalsPath);
                    if (crystals)
                    {
                        anchor = crystals.position;
                    }
                    break;
                }
            }
            return true;
        }

        // One more mote, leaving the body
        private void Spawn(float speed, float spread)
        {
            if (m_motes.Count >= MaxMotes)
            {
                return;
            }
            m_motes.Add(new Mote
            {
                m_p = 0f,
                m_speed = Random.Range(MinSpeed, MaxSpeed) * speed,
                m_side = Random.Range(-1f, 1f) * BodySide * spread,
                m_up = Random.Range(-1f, 1f) * BodyUp * spread,
                m_wob = Random.value * 2f * Mathf.PI,
                m_size = Random.Range(MinSize, MaxSize),
            });
        }

        // Each mote on its way along the arch from `start` to `end`, drawn in from about the body to the way as it goes:
        // its glow and its pale core
        private void DrawMotes(Vector3 start, Vector3 control, Vector3 end, float flare, float dt)
        {
            float length = Mathf.Max(0.5f, Vector3.Distance(start, end));
            Vector3 flat = end - start;
            flat.y = 0f;
            Vector3 side = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : transform.right;
            float time = Time.time;
            float bright = 1f + flare;
            int count = 0;
            for (int i = m_motes.Count - 1; i >= 0; i--)
            {
                Mote mote = m_motes[i];
                mote.m_p += mote.m_speed * dt / length;
                if (mote.m_p >= 1f)
                {
                    m_motes.RemoveAt(i);
                    continue;
                }
                m_motes[i] = mote;
                float p = mote.m_p;
                float q = 1f - p;
                float fade = Mathf.Pow(q, 1.6f);
                Vector3 point = q * q * start + 2f * q * p * control + p * p * end
                    + side * (mote.m_side * fade + Mathf.Sin(time * WobbleRate + mote.m_wob) * Wobble)
                    + Vector3.up * (mote.m_up * fade);
                float alpha = Mathf.Clamp01((1f - 0.5f * p) * Mathf.Min(1f, p * 20f + 0.15f));
                Color glow = Blood * Mathf.Min(1.15f, bright);
                glow.a = alpha;
                Color core = Core;
                core.a = Mathf.Clamp01(alpha * bright);
                m_buffer[count++] = Particle(point, mote.m_size * GlowScale * (1f + 0.4f * flare), glow);
                m_buffer[count++] = Particle(point, mote.m_size * CoreScale, core);
            }
            m_particles.SetParticles(m_buffer, count);
        }

        private static ParticleSystem.Particle Particle(Vector3 position, float size, Color color)
        {
            return new ParticleSystem.Particle
            {
                position = position,
                startSize = size,
                startColor = color,
                startLifetime = 100f,
                remainingLifetime = 100f,
            };
        }

        // The faint thread the motes follow
        private void DrawThread(Vector3 start, Vector3 control, Vector3 end, float flare)
        {
            m_thread.enabled = m_shown > 0f;
            if (!m_thread.enabled)
            {
                return;
            }
            for (int i = 0; i < ThreadPoints; i++)
            {
                float p = (float)i / (ThreadPoints - 1);
                float q = 1f - p;
                m_points[i] = q * q * start + 2f * q * p * control + p * p * end;
            }
            m_thread.SetPositions(m_points);
            Color color = Blood;
            color.a = Mathf.Clamp01(ThreadAlpha * m_shown * (1f + 2f * flare));
            m_thread.startColor = color;
            m_thread.endColor = color;
            m_thread.widthMultiplier = ThreadWidth * (1f + 0.5f * flare);
        }

        private void Hide()
        {
            if (!m_built || m_cleared)
            {
                return;
            }
            m_cleared = true;
            m_particles.SetParticles(m_buffer, 0);
            m_thread.enabled = false;
            m_light.enabled = false;
            m_anchorLight.enabled = false;
        }

        private void Build()
        {
            m_built = true;
            if (!s_moteMaterial)
            {
                // A soft round dot, and a band across the thread, both full in their middle
                Texture2D dot = LineMaterials.Texture("NhxDrainMote", 32, 32, (along, across) =>
                {
                    float x = (along + 0.5f / 32f) * 2f - 1f;
                    float r = Mathf.Clamp01(Mathf.Sqrt(x * x + across * across));
                    return (1f - r) * (1f - r);
                });
                dot.wrapModeU = TextureWrapMode.Clamp;
                s_moteMaterial = LineMaterials.Additive(Wrath.Look ? Wrath.Look.m_glowMaterial : null);
                s_moteMaterial.mainTexture = dot;
                // The shader draws 2 × this × the mote's color: past its neutral half, brighter than the colors alone go
                float tint = 0.5f * Brightness;
                s_moteMaterial.SetColor("_TintColor", new Color(tint, tint, tint, 0.5f));
                s_threadMaterial = LineMaterials.Additive(Wrath.Look ? Wrath.Look.m_glowMaterial : null);
                s_threadMaterial.mainTexture = LineMaterials.Band("NhxDrainThread", across => (1f - across) * (1f - across));
            }

            // Motes set where they go every frame (SetParticles): nothing emitted, nothing moved by the system
            GameObject motes = new GameObject("NhxDrainMotes");
            motes.transform.SetParent(transform, false);
            m_particles = motes.AddComponent<ParticleSystem>();
            m_particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = m_particles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 100f;
            main.gravityModifier = 0f;
            main.maxParticles = 2 * MaxMotes;
            ParticleSystem.EmissionModule emission = m_particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = m_particles.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = motes.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = s_moteMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_particles.Play();
            m_buffer = new ParticleSystem.Particle[2 * MaxMotes];

            GameObject thread = new GameObject("NhxDrainThread");
            thread.transform.SetParent(transform, false);
            m_thread = thread.AddComponent<LineRenderer>();
            m_thread.useWorldSpace = true;
            m_thread.sharedMaterial = s_threadMaterial;
            m_thread.textureMode = LineTextureMode.Stretch;
            m_thread.positionCount = ThreadPoints;
            m_thread.numCapVertices = 2;
            m_thread.shadowCastingMode = ShadowCastingMode.Off;
            m_thread.receiveShadows = false;
            m_points = new Vector3[ThreadPoints];

            m_light = Glow("NhxDrainGlow", LightRange);
            m_anchorLight = Glow("NhxDrainAltarGlow", AnchorRange);
        }

        private Light Glow(string name, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Blood;
            light.range = range;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }
    }
}
