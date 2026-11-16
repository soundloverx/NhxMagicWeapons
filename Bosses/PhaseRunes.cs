using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // A twin's second phase coming (TwinsPhase; the user, 2026-11-16: "During those 5 seconds, I imagine runes floating
    // around their body, the same runes that appear when hit by the staff they are immune to", then of Claude's take on
    // it: "I like absolutely everything you described"): its ward's rune (WardFlash: ElementRune's rings, in its element's
    // color), Runes of them standing in a ring about its middle, each facing out from it, its rings turning. As the twin
    // grows they close in, go round faster and brighten, a light at its middle with them; in the last CollapseTime they
    // fall into it, and as its phase comes they burst out of it as one rune on the ground, out to how far it pushes the
    // players back (TwinsPhase.PushRadius), fading within BurstTime.
    // On each game, not networked: each game times it from when the phase began (the twin's ZDO), so a game that sees it
    // late sees the rest of it. Its lengths are a player's (meters), times the twin's size as it grows
    internal class PhaseRunes : MonoBehaviour
    {
        private const int Runes = 6;
        // Each rune's size (from its middle), the ring's height, how far out from the twin's axis (from, to), and how fast
        // it goes round (degrees a second, from, to)
        private const float RuneRadius = 0.3f;
        private const float Height = 1.1f;
        private const float OrbitFrom = 1.5f;
        private const float OrbitTo = 0.75f;
        private const float TurnFrom = 30f;
        private const float TurnTo = 240f;
        // Every other rune this much higher, the rest as much lower, bobbing by Bob over BobPeriod
        private const float Tier = 0.15f;
        private const float Bob = 0.05f;
        private const float BobPeriod = 1.5f;
        // Fading in from FadeFrom of their size; at the end falling in, down to CollapseTo of it
        private const float FadeIn = 0.6f;
        private const float FadeFrom = 0.4f;
        private const float CollapseTime = 0.35f;
        private const float CollapseTo = 0.3f;
        // Their rings' turning (degrees a second, the middle first), up to SpinGain times faster as it comes
        private static readonly float[] Spin = { 120f, -80f, 50f };
        private const float SpinGain = 3f;
        // How bright (the shader draws twice its color), from Dim to Bright as it comes
        private const float Dim = 0.6f;
        private const float Bright = 2.5f;
        // Its light: from LightFrom to LightTo as it comes, LightBurst as it bursts, fading with the burst
        private const float LightFrom = 0.5f;
        private const float LightTo = 3f;
        private const float LightBurst = 6f;
        private const float LightRange = 10f;
        // The burst: a rune on the ground from BurstFrom meters out to the radius given, fading within BurstTime, this high
        // above where the twin stands
        private const float BurstFrom = 1f;
        private const float BurstTime = 0.6f;
        private const float BurstLift = 0.3f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");

        private Character m_twin;
        private long m_start;
        private float m_duration;
        private float m_burstRadius;
        private Color m_color;
        private int m_perRune;
        private Transform[] m_runes;
        // Every rune's rings, then the burst's (the last m_perRune)
        private Transform[] m_layers;
        private MeshRenderer[] m_renderers;
        private Transform m_burst;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private float m_turn;
        private bool m_bursting;

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        // `boss`'s runes, on this game, for its phase begun at `start` (ZNet ticks) and coming over `duration` seconds
        internal static void Show(CallerBoss boss, long start, float duration, float burstRadius)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !boss || !boss.m_humanoid)
            {
                return;
            }
            Material[] materials = ElementRune.Materials();
            if (materials == null)
            {
                return;
            }
            GameObject go = new GameObject("NhxPhaseRunes");
            go.transform.position = boss.transform.position;
            go.AddComponent<PhaseRunes>().Build(boss, materials, start, duration, burstRadius);
        }

        private void Build(CallerBoss boss, Material[] materials, long start, float duration, float burstRadius)
        {
            m_twin = boss.m_humanoid;
            m_start = start;
            m_duration = duration;
            m_burstRadius = burstRadius;
            m_color = boss.Element == Element.Fire ? ElementRune.Fire : ElementRune.Storm;
            m_block = new MaterialPropertyBlock();
            m_perRune = materials.Length;
            m_runes = new Transform[Runes];
            m_layers = new Transform[(Runes + 1) * m_perRune];
            m_renderers = new MeshRenderer[m_layers.Length];
            for (int i = 0; i < Runes; i++)
            {
                m_runes[i] = new GameObject("rune" + i).transform;
                m_runes[i].SetParent(transform, false);
                AddLayers(m_runes[i], materials, i * m_perRune);
            }
            // Flat on the ground, facing up, hidden until it bursts
            m_burst = new GameObject("burst").transform;
            m_burst.SetParent(transform, false);
            m_burst.localPosition = Vector3.up * BurstLift;
            m_burst.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            AddLayers(m_burst, materials, Runes * m_perRune);
            m_burst.gameObject.SetActive(false);

            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(transform, false);
            m_light = lightObject.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = m_color;
            m_light.range = LightRange;
            m_light.shadows = LightShadows.None;
            m_light.intensity = 0f;
            Draw(0f);
        }

        // A rune's rings, one over the other, each turned at random to start with
        private void AddLayers(Transform rune, Material[] materials, int at)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                GameObject layer = new GameObject("layer" + i);
                layer.transform.SetParent(rune, false);
                layer.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                layer.AddComponent<MeshFilter>().sharedMesh = WardFlash.Quad();
                MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                renderer.sortingOrder = i;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                m_layers[at + i] = layer.transform;
                m_renderers[at + i] = renderer;
            }
        }

        private void Update()
        {
            Draw(Time.deltaTime);
        }

        private void Draw(float dt)
        {
            // Gone with its twin, or once burst
            float t = (float)System.TimeSpan.FromTicks(Now - m_start).TotalSeconds;
            if (!m_twin || m_twin.IsDead() || t >= m_duration + BurstTime)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = m_twin.transform.position;
            if (t < m_duration)
            {
                Ring(t, dt);
            }
            else
            {
                Burst(t - m_duration, dt);
            }
        }

        // Round it, closing in, then falling into it
        private void Ring(float t, float dt)
        {
            float size = Mathf.Max(0.01f, m_twin.transform.localScale.y);
            float p = Mathf.Clamp01(t / m_duration);
            float ease = Mathf.SmoothStep(0f, 1f, p);
            float fade = Mathf.Clamp01(t / FadeIn);
            float collapse = Mathf.Clamp01((t - (m_duration - CollapseTime)) / CollapseTime);
            collapse *= collapse;
            m_turn += Mathf.Lerp(TurnFrom, TurnTo, p * p) * dt;
            float orbit = Mathf.Lerp(OrbitFrom, OrbitTo, ease) * (1f - collapse) * size;
            float runeSize = RuneRadius * size * Mathf.Lerp(FadeFrom, 1f, fade) * Mathf.Lerp(1f, CollapseTo, collapse);
            Vector3 center = m_twin.transform.position + Vector3.up * Height * size;
            for (int i = 0; i < Runes; i++)
            {
                float angle = m_turn + 360f * i / Runes;
                Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                float rise = ((i % 2 == 0 ? Tier : -Tier) + Bob * Mathf.Sin(2f * Mathf.PI * (Time.time / BobPeriod + (float)i / Runes))) * size;
                m_runes[i].SetPositionAndRotation(center + outward * orbit + Vector3.up * rise, Quaternion.LookRotation(outward));
                m_runes[i].localScale = Vector3.one * runeSize;
            }
            Tint(Mathf.Lerp(Dim, Bright, ease), fade, 0, Runes * m_perRune, dt * (1f + SpinGain * ease));
            m_light.transform.position = center;
            m_light.intensity = Mathf.Lerp(Mathf.Lerp(LightFrom, LightTo, ease) * fade, LightBurst, collapse);
        }

        // Out of it along the ground, fading
        private void Burst(float t, float dt)
        {
            if (!m_bursting)
            {
                m_bursting = true;
                foreach (Transform rune in m_runes)
                {
                    rune.gameObject.SetActive(false);
                }
                m_burst.gameObject.SetActive(true);
            }
            float b = Mathf.Clamp01(t / BurstTime);
            float fade = 1f - b;
            float outward = 1f - (1f - b) * (1f - b);
            m_burst.localScale = Vector3.one * Mathf.Lerp(BurstFrom, m_burstRadius, outward);
            Tint(Mathf.Lerp(1f, Bright, fade), fade, Runes * m_perRune, m_perRune, dt * (1f + SpinGain));
            m_light.transform.position = m_burst.position + Vector3.up;
            m_light.intensity = LightBurst * fade;
        }

        // `count` rings from `from`: their color, and their turning by `spin` seconds' worth
        private void Tint(float bright, float alpha, int from, int count, float spin)
        {
            Color tint = m_color * bright;
            tint.a = 0.5f * alpha;
            m_block.SetColor(s_tint, tint);
            for (int i = from; i < from + count; i++)
            {
                m_layers[i].Rotate(0f, 0f, Spin[(i % m_perRune) % Spin.Length] * spin, Space.Self);
                m_renderers[i].SetPropertyBlock(m_block);
            }
        }
    }
}
