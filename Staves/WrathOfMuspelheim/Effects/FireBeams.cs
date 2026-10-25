using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // Draws the staff's fire beam under an object: from the staff's head to where it hits, one beam, or several side by
    // side at higher levels, each a smooth, fluid waveform (the user, 2026-10-25: "a rounded, more 'fluid' lightning, more
    // similar to a waveform which looks very chaotic when shot and then straightens out" until it's gone). Each is a few
    // waves laid over each other, from long swells to small ripples, swinging both ways around the beam's line and pinned
    // at both ends, travelling toward the target; they die down over the beam's life, the ripples first, so it calms
    // and straightens as it fades. Flames flow along each beam, in a sheath of fire, with a glow around them all and a
    // light where they end. Each game picks its own waves. Used by FireBeam on every game
    internal class FireBeams
    {
        // Along each beam: enough for its finest ripples on a long shot
        private const int Points = 128;
        // At size 1. The sheath and the glow fade out toward their edges, so they look about half as wide
        private const float CoreWidth = 0.14f;
        private const float SheathWidth = 0.25f;
        private const float GlowWidth = 0.9f;
        private const float LightIntensity = 4f;
        private const float LightRange = 10f;
        // Fully bright for this share of its life, then fading and thinning out
        private const float FullBright = 0.3f;
        private const float FlickerInterval = 0.05f;
        // How far the waves swing at first: this share of the beam's length, at most MaxSwing meters (5% and 1.2 m until
        // the user asked for "just a little" less, with fewer curves)
        private const float Swing = 0.04f;
        private const float MaxSwing = 1f;
        // The waves laid over each other: how many times each rises and falls along a 15 m beam (more on a longer one,
        // fewer on a shorter one, within a half and one and a half times as many; a fifth more of each before), their
        // share of the swing, how many times a second each travels its length toward the target, and how fast it dies
        // down: its swing goes as (life left) to this power, so the ripples are gone first and the swells last
        private static readonly float[] WaveCycles = { 1f, 2.3f, 4.9f, 10f };
        private static readonly float[] WaveShares = { 1f, 0.55f, 0.3f, 0.15f };
        private static readonly float[] WaveSpeeds = { 0.6f, 1.2f, 2.2f, 3.5f };
        private static readonly float[] WaveCalming = { 1.2f, 2f, 2.8f, 3.6f };
        private const float CyclesLength = 15f;
        // The flames along the cores and sheaths repeat every FlameLength meters and flow toward the target this fast
        private const float FlameLength = 1.5f;
        private const float FlowSpeed = 8f;

        private static readonly int s_mainTexST = Shader.PropertyToID("_MainTex_ST");

        private readonly BeamLook m_look;
        private readonly float m_duration;
        private readonly Transform m_from;
        private readonly Vector3 m_fromPoint;
        private readonly Vector3 m_to;
        private readonly LineRenderer[] m_strands;
        private readonly LineRenderer[] m_sheaths;
        private readonly float[] m_flickers;
        private readonly float m_strandWidth;
        private readonly float m_sheathWidth;
        // Each beam's waves: their phases (on each side of the line) and their share of the swing
        private readonly Vector2[,] m_phases;
        private readonly float[,] m_shares;
        private readonly LineRenderer m_glow;
        private readonly Light m_light;
        private readonly float m_lightIntensity;
        private readonly Vector3[] m_positions = new Vector3[Points];
        private readonly Vector3[] m_middle = new Vector3[Points];
        private readonly float[] m_calm = new float[WaveCycles.Length];
        private readonly MaterialPropertyBlock m_block = new MaterialPropertyBlock();
        private float m_flickerTimer;

        // From `from` as it moves (the staff's tip), or `fromPoint` if it's gone, to `to`, for `duration` seconds
        public FireBeams(Transform parent, BeamLook look, float duration, Transform from, Vector3 fromPoint, Vector3 to, BeamSize size)
        {
            m_look = look;
            m_duration = duration;
            m_from = from;
            m_fromPoint = fromPoint;
            m_to = to;
            // Drawn in this order, the glow first: they're all at the same place, so their distance doesn't sort them
            m_glow = CreateLine(parent, "glow", GlowWidth * size.m_glowSize, look.m_glowMaterial, 0);
            m_sheathWidth = SheathWidth * size.m_glowSize;
            m_strandWidth = CoreWidth * size.m_strandSize;
            m_strands = new LineRenderer[size.m_strands];
            m_sheaths = new LineRenderer[size.m_strands];
            m_flickers = new float[size.m_strands];
            m_phases = new Vector2[size.m_strands, WaveCycles.Length];
            m_shares = new float[size.m_strands, WaveCycles.Length];
            for (int s = 0; s < size.m_strands; s++)
            {
                m_sheaths[s] = CreateLine(parent, "sheath", m_sheathWidth, look.m_sheathMaterial, 1);
                m_sheaths[s].textureMode = LineTextureMode.Tile;
                m_strands[s] = CreateLine(parent, "beam", m_strandWidth, look.m_coreMaterial, 2);
                m_strands[s].textureMode = LineTextureMode.Tile;
                for (int w = 0; w < WaveCycles.Length; w++)
                {
                    m_phases[s, w] = new Vector2(Random.Range(0f, 2f * Mathf.PI), Random.Range(0f, 2f * Mathf.PI));
                    m_shares[s, w] = WaveShares[w] * Random.Range(0.7f, 1.3f);
                }
            }

            GameObject light = new GameObject("light");
            light.transform.SetParent(parent, false);
            m_light = light.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = look.m_lightColor;
            m_light.range = LightRange * size.m_glowSize;
            m_light.shadows = LightShadows.None;
            m_lightIntensity = LightIntensity * size.m_glowSize;
            Show(false);
        }

        // At `age` seconds since it was cast
        public void Draw(float age)
        {
            if (age < 0f || age >= m_duration)
            {
                Show(false);
                return;
            }
            Show(true);

            Vector3 start = m_from ? m_from.position : m_fromPoint;
            Vector3 delta = m_to - start;
            float length = delta.magnitude;
            Vector3 dir = length > 0.001f ? delta / length : Vector3.forward;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(dir, Vector3.forward);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(right, dir);

            m_flickerTimer -= Time.deltaTime;
            if (m_flickerTimer <= 0f)
            {
                m_flickerTimer = FlickerInterval;
                for (int i = 0; i < m_flickers.Length; i++)
                {
                    m_flickers[i] = Random.Range(0.8f, 1f);
                }
            }

            float life = age / m_duration;
            float fade = Mathf.Clamp01(life < FullBright ? 1f : 1f - (life - FullBright) / (1f - FullBright));
            // Thinner as it fades
            float thinning = Mathf.Lerp(0.5f, 1f, fade);
            float swing = Mathf.Min(length * Swing, MaxSwing);
            float cyclesScale = Mathf.Clamp(length / CyclesLength, 0.5f, 1.5f);
            // Each wave's swing now, as it calms
            for (int w = 0; w < m_calm.Length; w++)
            {
                m_calm[w] = swing * Mathf.Pow(1f - life, WaveCalming[w]);
            }

            float brightest = 0f;
            for (int i = 0; i < Points; i++)
            {
                m_middle[i] = Vector3.zero;
            }
            // The flames flow along the cores: the texture repeats every FlameLength meters, moving toward the target
            m_block.SetVector(s_mainTexST, new Vector4(1f / FlameLength, 1f, -age * FlowSpeed / FlameLength, 0f));
            for (int s = 0; s < m_strands.Length; s++)
            {
                for (int i = 0; i < Points; i++)
                {
                    float t = (float)i / (Points - 1);
                    // Pinned at both ends, swinging freely in between
                    float pinned = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.6f);
                    float x = 0f;
                    float y = 0f;
                    for (int w = 0; w < WaveCycles.Length; w++)
                    {
                        float angle = 2f * Mathf.PI * (WaveCycles[w] * cyclesScale * t - WaveSpeeds[w] * age);
                        float amount = m_calm[w] * m_shares[s, w];
                        x += amount * Mathf.Sin(angle + m_phases[s, w].x);
                        y += amount * Mathf.Sin(angle + m_phases[s, w].y);
                    }
                    m_positions[i] = start + delta * t + (right * x + up * y) * pinned;
                    m_middle[i] += m_positions[i] / m_strands.Length;
                }
                float alpha = fade * m_flickers[s];
                Line(m_strands[s], m_strandWidth * thinning, m_look.m_coreColor, alpha);
                Line(m_sheaths[s], m_sheathWidth * thinning, m_look.m_sheathColor, alpha);
                brightest = Mathf.Max(brightest, m_flickers[s]);
            }

            float glowAlpha = fade * brightest;
            m_glow.SetPositions(m_middle);
            SetColor(m_glow, m_look.m_glowColor, glowAlpha);
            m_light.transform.position = m_to;
            m_light.intensity = m_lightIntensity * glowAlpha;
        }

        // A beam's core or sheath along m_positions, with the flames flowing
        private void Line(LineRenderer line, float width, Color color, float alpha)
        {
            line.SetPositions(m_positions);
            line.widthMultiplier = width;
            line.SetPropertyBlock(m_block);
            SetColor(line, color, alpha);
        }

        private void Show(bool show)
        {
            m_glow.enabled = show;
            m_light.enabled = show;
            for (int s = 0; s < m_strands.Length; s++)
            {
                m_strands[s].enabled = show;
                m_sheaths[s].enabled = show;
            }
        }

        private static LineRenderer CreateLine(Transform parent, string name, float width, Material material, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sortingOrder = order;
            line.useWorldSpace = true;
            line.positionCount = Points;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            // A little wider where it hits
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 1f));
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static void SetColor(LineRenderer line, Color color, float alpha)
        {
            color.a *= alpha;
            line.startColor = color;
            line.endColor = color;
        }
    }
}
