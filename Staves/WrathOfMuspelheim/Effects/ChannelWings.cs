using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // Wings of flame, one of the Firecaller's Regalia's three showpieces (the user, 2026-11-13: "I like the subtle effects
    // of the Firecaller set, but there's nothing that really makes me say 'wow, that's so cool'", then of the three
    // offered: "I want to try all 3 of them on", and having tried them: "I like the visuals"). With the set at level 20
    // (ArmorLegend.WingsFrom), once the channelled beam (FireChannel) has been held for its third hit, two wings burst
    // open from the wearer's shoulder blades, and fold and burn out when the channel ends.
    // Each wing is a fan of thin strands of fire, drawn like the beam's (BeamLook's core, sheath and glow, flames flowing
    // along them): all leave up and out along the wing's arm and bend to their tips, the top ones long and high, the
    // lower ones shorter and drooping. The user was "skeptical about the wings... would they look right?", saw three
    // shapes drawn from these formulas, and picked this one, the first: "I have to say I like the v1 wings best" (the
    // others had a thick leading edge with broad feathers of flame hanging from it). They sway, beat slowly, spread and
    // brighten at each of the channel's hits, with small flames hanging on them and embers shed from their tips.
    // Every game draws them, for whoever holds them up this frame (Hold): the channel on each game, or nhx_armorlook
    // show. An object of its own in the world, following the wearer's upper chest; it removes itself once folded
    internal class ChannelWings : MonoBehaviour
    {
        // The channel's hit they open at (a hit a second: the third comes after 2 s)
        internal const int OpenAtTick = 3;

        // The wing's shape, in the wearer's frame (right, up, forward), from the upper chest (Spine2): the numbers of the
        // drawing the user picked. Each wing's strands start at Root (out, up, back). Their tips fan from TopAngle above
        // the horizon (the first, TopLength long) to BottomAngle (the last, BottomLength), in the wing's plane, which is
        // swept back by Sweep; each leaves along the wing's arm (ArmAngle) for ArmShare of its length, then bends to its
        // tip. Folded, they all hang at FoldAngle, swept back by FoldSweep, FoldLength as long
        private const int Feathers = 7;
        private static readonly Vector3 Root = new Vector3(0.10f, 0.10f, -0.16f);
        private const float TopAngle = 40f;
        private const float BottomAngle = -72f;
        private const float TopLength = 2.3f;
        private const float BottomLength = 0.85f;
        private const float ArmAngle = 52f;
        private const float ArmShare = 0.5f;
        private const float Sweep = 20f;
        private const float FoldAngle = -80f;
        private const float FoldSweep = 60f;
        private const float FoldLength = 0.25f;
        // Where the chest is without its bone
        private const float ChestHeight = 1.38f;
        // The strands sway: this share of their length at their tips, two waves travelling out along them
        private const float Wave = 0.05f;
        // A slow beat lifts the upper strands by this many degrees, this many times a second; a hit spreads them by this
        private const float BeatAngle = 4f;
        private const float BeatRate = 0.4f;
        private const float FlareAngle = 6f;
        private const int Points = 40;
        // Each strand's core, sheath and glow at their widest (meters; the sheath and the glow fade toward their edges,
        // so they look about half as wide), the lowest strand this much thinner than the top one; at a hit, this much
        // wider. The glow is this bright
        private const float CoreWidth = 0.07f;
        private const float SheathWidth = 0.35f;
        private const float GlowWidth = 0.95f;
        private const float Thinner = 0.3f;
        private const float FlareWidth = 0.3f;
        private const float GlowAlpha = 0.3f;
        // Opening and folding take this long; folded, it stays this long for its last flames, then goes
        private const float UnfurlTime = 0.45f;
        private const float FoldTime = 0.6f;
        private const float Linger = 2.5f;
        // The flames along the strands repeat every FlameLength meters and flow toward the tips this fast
        private const float FlameLength = 1f;
        private const float FlowSpeed = 2.5f;
        // Small flames hanging on the strands and embers shed from their tips, a second, for both wings; and a burst of
        // embers as they open
        private const float FlamesPerSecond = 150f;
        private const float EmbersPerSecond = 26f;
        private const int BurstEmbers = 50;
        private const float LightIntensity = 1.5f;
        private const float LightRange = 7f;
        // A hit's flare dies down this fast
        private const float FlareTime = 0.25f;
        private const float FlickerInterval = 0.05f;

        private static readonly int s_mainTexST = Shader.PropertyToID("_MainTex_ST");
        private static readonly Dictionary<Character, ChannelWings> s_wings = new Dictionary<Character, ChannelWings>();
        private static AnimationCurve s_width;

        private class Feather
        {
            public LineRenderer m_core;
            public LineRenderer m_sheath;
            public LineRenderer m_glow;
            public readonly Vector3[] m_points = new Vector3[Points];
            // 0 for the wing's top strand, 1 for its lowest; its size; its two waves' phases
            public float m_share;
            public float m_size;
            public Vector2 m_phase;
        }

        private Character m_wearer;
        private Transform m_chest;
        private BeamLook m_look;
        // The left wing's strands, then the right's
        private Feather[] m_feathers;
        private ParticleSystem m_flames;
        private ParticleSystem m_embers;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private int m_heldFrame = -1;
        private bool m_wasHeld;
        private float m_flareIn;
        private float m_burst;
        // How far through opening they are (0 to 1), how far they're spread, and how bright
        private float m_open;
        private float m_spread;
        private float m_alpha;
        private float m_age;
        private float m_folded;
        private float m_flameDebt;
        private float m_emberDebt;
        private float m_flicker = 1f;
        private float m_flickerTimer;
        private bool m_shown = true;

        // Called every frame the wearer's wings are up (`open`), with how much their channel's last hit still flares (1
        // at the hit). Not called, or not open: they fold
        internal static void Hold(Character wearer, bool open, float flare)
        {
            if (!wearer || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }
            if (!s_wings.TryGetValue(wearer, out ChannelWings wings) || !wings)
            {
                if (!open || !Staff.Look)
                {
                    return;
                }
                wings = new GameObject("NhxFirecallerWings").AddComponent<ChannelWings>();
                wings.Build(wearer, Staff.Look);
                s_wings[wearer] = wings;
            }
            if (open)
            {
                wings.m_heldFrame = Time.frameCount;
                wings.m_flareIn = Mathf.Max(wings.m_flareIn, flare);
            }
        }

        private void Build(Character wearer, BeamLook look)
        {
            m_wearer = wearer;
            m_look = look;
            m_chest = FindBone(wearer, "Spine2");
            m_block = new MaterialPropertyBlock();
            if (s_width == null)
            {
                s_width = Curve(Width, 12);
            }
            m_feathers = new Feather[2 * Feathers];
            for (int i = 0; i < m_feathers.Length; i++)
            {
                float share = (float)(i % Feathers) / (Feathers - 1);
                m_feathers[i] = new Feather
                {
                    m_share = share,
                    m_size = 1f - Thinner * share,
                    m_phase = new Vector2(Random.Range(0f, 2f * Mathf.PI), Random.Range(0f, 2f * Mathf.PI)),
                    // Drawn in this order: they cross each other near the shoulder blades
                    m_glow = CreateLine("glow", look.m_glowMaterial, 0, LineTextureMode.Stretch),
                    m_sheath = CreateLine("sheath", look.m_sheathMaterial, 1, LineTextureMode.Tile),
                    m_core = CreateLine("core", look.m_coreMaterial, 2, LineTextureMode.Tile),
                };
            }
            m_flames = FireParticles.Flames(transform, 0.1f, 0.2f, 0.4f, 0.7f, 300);
            m_embers = FireParticles.Embers(transform, 1f, 2f, 200);

            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(transform, false);
            m_light = lightObject.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = look.m_lightColor;
            m_light.range = LightRange;
            m_light.intensity = 0f;
            m_light.shadows = LightShadows.None;
            Show(false);
        }

        // A strand's width along it: nothing at the shoulder blade, widest a third of the way, a point at its tip
        private static float Width(float t)
        {
            return Mathf.Pow(Mathf.Sin(0.5f * Mathf.PI * Mathf.Min(t / 0.5f, 1f)), 0.7f) * Mathf.Pow(1f - t, 0.8f);
        }

        private static AnimationCurve Curve(System.Func<float, float> width, int keys)
        {
            Keyframe[] frames = new Keyframe[keys];
            for (int i = 0; i < keys; i++)
            {
                float t = (float)i / (keys - 1);
                float before = Mathf.Max(0f, t - 0.02f);
                float after = Mathf.Min(1f, t + 0.02f);
                float slope = (width(after) - width(before)) / (after - before);
                frames[i] = new Keyframe(t, width(t), slope, slope);
            }
            return new AnimationCurve(frames);
        }

        private LineRenderer CreateLine(string name, Material material, int order, LineTextureMode mode)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sortingOrder = order;
            line.useWorldSpace = true;
            line.positionCount = Points;
            line.sharedMaterial = material;
            line.widthCurve = s_width;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = mode;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        internal static Transform FindBone(Character character, string name)
        {
            Humanoid humanoid = character as Humanoid;
            VisEquipment equipment = humanoid ? humanoid.m_visEquipment : null;
            SkinnedMeshRenderer body = equipment ? equipment.m_bodyModel : null;
            if (!body)
            {
                return null;
            }
            foreach (Transform bone in body.bones)
            {
                if (bone && bone.name == name)
                {
                    return bone;
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            if (!ReferenceEquals(m_wearer, null) && s_wings.TryGetValue(m_wearer, out ChannelWings wings) && wings == this)
            {
                s_wings.Remove(m_wearer);
            }
        }

        // After the animation has posed the bones, and after whoever holds them up has said so (Update)
        private void LateUpdate()
        {
            if (!m_wearer || m_wearer.IsDead())
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            bool held = m_heldFrame == Time.frameCount;
            bool opened = held && !m_wasHeld;
            m_wasHeld = held;
            if (held)
            {
                if (opened)
                {
                    m_burst = 1f;
                    // From however far they had folded (about where the opening passes that spread)
                    m_open = 0.3f * Mathf.Pow(Mathf.Clamp01(m_spread), 1.6f);
                }
                // They burst open: nearly spread within a third of the time, a little past it, then settling
                m_open = Mathf.MoveTowards(m_open, 1f, dt / UnfurlTime);
                m_spread = EaseOutBack(m_open);
                m_alpha = Mathf.Clamp01(m_open * 3f);
            }
            else
            {
                // They fold steadily, burning out over the last of it
                m_spread = Mathf.MoveTowards(m_spread, 0f, dt / FoldTime);
                m_alpha = Mathf.Clamp01(m_spread * 1.5f);
            }
            if (m_spread <= 0f)
            {
                // Folded: what's left of its flames burns out, then it goes
                Show(false);
                m_folded += dt;
                if (m_folded > Linger)
                {
                    Destroy(gameObject);
                }
                return;
            }
            m_folded = 0f;
            m_age += dt;
            float flare = Mathf.Max(m_flareIn, m_burst);
            m_flareIn = 0f;
            m_burst *= Mathf.Exp(-dt / FlareTime);
            m_flickerTimer -= dt;
            if (m_flickerTimer <= 0f)
            {
                m_flickerTimer = FlickerInterval;
                m_flicker = Random.Range(0.85f, 1f);
            }
            Show(true);
            Draw(dt, flare, opened);
        }

        private void Draw(float dt, float flare, bool opened)
        {
            Quaternion facing = m_wearer.transform.rotation;
            Vector3 chest = m_chest ? m_chest.position : m_wearer.transform.position + Vector3.up * ChestHeight;
            // How far they're spread (past 1 for a moment as they open), and how bright
            float e = m_spread;
            float alpha = m_alpha * m_flicker;
            float wider = 1f + FlareWidth * flare;
            float beat = Mathf.Sin(2f * Mathf.PI * BeatRate * m_age);
            float sweep = Mathf.LerpUnclamped(FoldSweep, Sweep, e) * Mathf.Deg2Rad;
            float arm = Mathf.LerpUnclamped(FoldAngle, ArmAngle, e) * Mathf.Deg2Rad;
            float lengthScale = Mathf.LerpUnclamped(FoldLength, 1f, e);
            Vector3 up = Vector3.up;
            m_block.SetVector(s_mainTexST, new Vector4(1f / FlameLength, 1f, -m_age * FlowSpeed / FlameLength, 0f));

            for (int i = 0; i < m_feathers.Length; i++)
            {
                Feather feather = m_feathers[i];
                float side = i < Feathers ? -1f : 1f;
                // The wing's plane: outward and up; the strands sway mostly across it
                Vector3 outward = facing * new Vector3(side * Mathf.Cos(sweep), 0f, -Mathf.Sin(sweep));
                Vector3 across = Vector3.Cross(outward, up) * side;
                Vector3 root = chest + facing * new Vector3(Root.x * side, Root.y, Root.z);
                float share = feather.m_share;
                float angle = Mathf.Lerp(TopAngle, BottomAngle, Mathf.Pow(share, 0.85f)) + (BeatAngle * beat + FlareAngle * flare) * (1f - share);
                angle = Mathf.LerpUnclamped(FoldAngle, angle, e) * Mathf.Deg2Rad;
                float length = Mathf.Lerp(TopLength, BottomLength, Mathf.Pow(share, 0.75f)) * lengthScale;
                Vector3 tip = root + (outward * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * length;
                Vector3 control = root + (outward * Mathf.Cos(arm) + up * Mathf.Sin(arm)) * (length * ArmShare);
                for (int p = 0; p < Points; p++)
                {
                    float t = (float)p / (Points - 1);
                    float u = 1f - t;
                    float sway = Wave * length * t * Mathf.Sqrt(t);
                    float wave = Mathf.Sin(2f * Mathf.PI * (1.3f * t - 0.8f * m_age) + feather.m_phase.x);
                    float ripple = Mathf.Sin(2f * Mathf.PI * (2.9f * t - 1.7f * m_age) + feather.m_phase.y);
                    feather.m_points[p] = u * u * root + 2f * u * t * control + t * t * tip
                        + across * (sway * (wave + 0.4f * ripple)) + up * (sway * 0.5f * ripple);
                }
                float size = feather.m_size * wider;
                Line(feather.m_core, feather.m_points, CoreWidth * size, m_look.m_mainColor, alpha, true);
                Line(feather.m_sheath, feather.m_points, SheathWidth * size, m_look.m_sheathColor, alpha, true);
                Line(feather.m_glow, feather.m_points, GlowWidth * size, m_look.m_glowColor, alpha * GlowAlpha * (1f + flare), false);
            }

            // Small flames hanging on the strands, drawn along toward the tips and rising; embers shed from the tips
            m_flameDebt += FlamesPerSecond * alpha * dt;
            while (m_flameDebt >= 1f)
            {
                m_flameDebt -= 1f;
                Vector3[] points = m_feathers[Random.Range(0, m_feathers.Length)].m_points;
                int p = Random.Range(Points / 6, Points - 1);
                Vector3 along = points[p + 1] - points[p];
                FireParticles.Emit(m_flames, points[p] + Random.insideUnitSphere * 0.04f, along.normalized * 0.4f + up * 0.2f);
            }
            m_emberDebt += EmbersPerSecond * alpha * dt;
            while (m_emberDebt >= 1f)
            {
                m_emberDebt -= 1f;
                Vector3[] points = m_feathers[Random.Range(0, m_feathers.Length)].m_points;
                FireParticles.Emit(m_embers, points[Points - 1 - Random.Range(0, 5)], Random.insideUnitSphere * 0.3f + up * 0.3f);
            }
            if (opened)
            {
                // They burst open: embers thrown out from the back
                for (int i = 0; i < BurstEmbers; i++)
                {
                    Vector3[] points = m_feathers[Random.Range(0, m_feathers.Length)].m_points;
                    Vector3 point = points[Random.Range(Points / 4, Points)];
                    Vector3 away = point - chest;
                    away.y = Mathf.Abs(away.y);
                    FireParticles.Emit(m_embers, point, away.normalized * Random.Range(1f, 3f) + up * Random.Range(0.3f, 1.2f));
                }
            }

            m_light.transform.position = chest + facing * new Vector3(0f, 0.3f, -0.5f);
            m_light.intensity = LightIntensity * alpha * (1f + 0.5f * flare);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        private void Line(LineRenderer line, Vector3[] points, float width, Color color, float alpha, bool flowing)
        {
            line.SetPositions(points);
            line.widthMultiplier = width;
            if (flowing)
            {
                line.SetPropertyBlock(m_block);
            }
            color.a *= Mathf.Clamp01(alpha);
            line.startColor = color;
            line.endColor = color;
        }

        private void Show(bool show)
        {
            if (show == m_shown)
            {
                return;
            }
            m_shown = show;
            foreach (Feather feather in m_feathers)
            {
                feather.m_core.enabled = show;
                feather.m_sheath.enabled = show;
                feather.m_glow.enabled = show;
            }
            m_light.enabled = show;
        }
    }
}
