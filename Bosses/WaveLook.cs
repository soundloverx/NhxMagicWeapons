using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using UnityEngine;
using UnityEngine.Rendering;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The altar's wave, seen (TwinsWave). Its look, picked by the user on 2026-11-22 from four drawn for them ("I like
    // option B with the crystals flashing, but I don't know how that would look in game. Let's try it. I am very curious
    // what the shimmer will look like"; the others were a violet ripple on the ground, a sweep of sparks, and wisps from
    // each player to the dais). On the altar (TwinsAltars: its location's root), on every game, while twins called into
    // its arena live, at each wave:
    // - As it leaves: the dais's three rune rings flare, the twins' crystals over it pulse brighter and farther, and a
    //   flash of the altar's violet light lights the dais and the ground about it
    // - The shimmer: a pale band of light on the ground, with a low veil of light standing on it, runs out from the dais
    //   to the stones at the wave's speed, fading as it goes
    // - As it reaches them: the stones' crystals flicker, and the ring's band, veil and arcs flare (RingLook.Pulse)
    // Looks only, from the wave's time (TwinsWave.Front), which every game knows. Made much stronger after the first test
    // (the user: "the visuals are so subtle that I couldn't see them at all"): seen from a player's height, a band flat on
    // the ground was a sliver, hence the veil and the dais's light
    internal class WaveLook : MonoBehaviour
    {
        // Set by the altar
        public float m_radius = 30f;
        public int m_stones = 12;

        // The flare as it leaves: how long (seconds); how much brighter the rune rings get at its height; the twins'
        // crystals' lights, times this much more and this much farther; the dais's light (intensity, range, how high)
        // (Toned down by 30% after the second test, the user: "it's a bit too much... Maybe tone it down by 30% and make it
        // dark purple": the altar's own lights by their numbers, the wave's band, veil and dais light by their color, a
        // darker one glowing less)
        private const float FlareTime = 1f;
        private const float FlareDisc = 2.8f;
        private const float FlareLight = 3.5f;
        private const float FlareReach = 0.7f;
        private const float DaisLight = 10f;
        private const float DaisRange = 18f;
        private const float DaisHeight = 2f;
        // The stones' flicker as the front reaches them: how long, how fast, their lights at its height (and the ring's
        // flare, which fades over the same time)
        private const float FlickerTime = 0.8f;
        private const float FlickerRate = 40f;
        private const float FlickerLight = 3.5f;
        // The shimmer: the band's width and lift (meters) and brightness (the additive shader's); the veil's height and
        // brightness; fading in this fast as it leaves the dais (seconds), from this far out (meters), points around
        private const float BandWidth = 1.4f;
        private const float BandLift = 0.1f;
        private const float BandBright = 1.6f;
        // (1.6 until the user's "reduce the height of the wave as it travels through the arena by 50%")
        private const float VeilHeight = 0.8f;
        private const float VeilBright = 1.2f;
        private const float FadeIn = 0.15f;
        private const float ShimmerFrom = 3f;
        private const int Points = 96;
        // Dark purple: the band, the veil and the dais's light (pale violet until the user's "make it dark purple")
        private static readonly Color ShimmerColor = new Color(0.45f, 0.15f, 0.9f);
        // Twins looked for this often
        private const float CheckEvery = 0.5f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");
        private static Texture2D s_soft;
        private static Mesh s_veilMesh;

        private CallerBoss m_twin;
        private float m_checkTimer;
        private bool m_built;
        // Not as the altar made them (Rest puts them back)
        private bool m_lit;
        private MaterialPropertyBlock m_block;
        private readonly List<MeshRenderer> m_discs = new List<MeshRenderer>();
        private readonly List<Color> m_discTints = new List<Color>();
        private readonly List<Light> m_crystalLights = new List<Light>();
        private readonly List<float> m_crystalBase = new List<float>();
        private readonly List<float> m_crystalRange = new List<float>();
        private readonly List<Light> m_stoneLights = new List<Light>();
        private readonly List<float> m_stoneBase = new List<float>();
        private Light m_daisLight;
        private RingLook m_ring;
        private LineRenderer m_band;
        private MeshRenderer m_veil;
        private Vector3[] m_points;

        private void Start()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            m_checkTimer -= Time.deltaTime;
            if (m_checkTimer <= 0f)
            {
                m_checkTimer = CheckEvery;
                m_twin = TwinsWave.TwinOf(transform.position);
            }
            if (!m_twin || !TwinsWave.Front(m_twin, out _, out float elapsed))
            {
                Rest();
                return;
            }
            if (!m_built)
            {
                Build();
            }
            float flare = elapsed < FlareTime ? Mathf.Pow(1f - elapsed / FlareTime, 2f) : 0f;
            float arrive = m_radius / TwinsWave.Speed;
            float since = elapsed - arrive;
            float fade = since >= 0f && since < FlickerTime ? 1f - since / FlickerTime : 0f;
            float flicker = fade * (0.5f + 0.5f * Mathf.Sin(since * FlickerRate));
            Brighten(flare, flicker, fade);
            Shimmer(elapsed);
        }

        private void OnDestroy()
        {
            Rest();
        }

        // The altar's parts it lights, as TwinsAltars builds them: the dais's rune rings ("rune") and the twins' crystals'
        // lights under it, each stone's light, the ring (RingLook); and its own: the dais's light, the band and the veil
        private void Build()
        {
            m_built = true;
            m_block = new MaterialPropertyBlock();
            m_ring = GetComponent<RingLook>();
            Transform dais = transform.Find("dais");
            if (dais)
            {
                foreach (Transform child in dais)
                {
                    MeshRenderer renderer = child.name == "rune" ? child.GetComponent<MeshRenderer>() : null;
                    if (renderer && renderer.sharedMaterial && renderer.sharedMaterial.HasProperty(s_tint))
                    {
                        m_discs.Add(renderer);
                        m_discTints.Add(renderer.sharedMaterial.GetColor(s_tint));
                    }
                }
                Transform twins = dais.Find("twins");
                if (twins)
                {
                    foreach (Light light in twins.GetComponentsInChildren<Light>(true))
                    {
                        m_crystalLights.Add(light);
                        m_crystalBase.Add(light.intensity);
                        m_crystalRange.Add(light.range);
                    }
                }
            }
            for (int i = 0; i < m_stones; i++)
            {
                Light light = transform.Find("stone" + i + "/light")?.GetComponent<Light>();
                if (light)
                {
                    m_stoneLights.Add(light);
                    m_stoneBase.Add(light.intensity);
                }
            }
            m_daisLight = Looks.Glow(transform, Vector3.up * DaisHeight, ShimmerColor, 0f, DaisRange);
            m_daisLight.enabled = false;

            if (!s_soft)
            {
                // Full in its middle, fading to nothing at its edges
                s_soft = LineMaterials.Band("NhxWaveSoft", across => (1f - across) * (1f - across));
            }
            Material material = LineMaterials.Additive(Wrath.Look ? Wrath.Look.m_glowMaterial : null);
            material.mainTexture = s_soft;

            GameObject band = new GameObject("wave band");
            band.transform.SetParent(transform, false);
            band.transform.localPosition = Vector3.up * BandLift;
            // Its points in the transform's x-y plane, which this turns flat on the ground, facing up
            band.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            m_band = band.AddComponent<LineRenderer>();
            m_band.useWorldSpace = false;
            m_band.loop = true;
            m_band.alignment = LineAlignment.TransformZ;
            m_band.positionCount = Points;
            m_band.widthMultiplier = BandWidth;
            m_band.textureMode = LineTextureMode.Stretch;
            m_band.sharedMaterial = material;
            m_band.shadowCastingMode = ShadowCastingMode.Off;
            m_band.receiveShadows = false;
            m_points = new Vector3[Points];
            band.SetActive(false);

            // The veil: a ring of radius 1 standing VeilHeight tall, brightest at the ground (the texture's upper half up
            // its height, as the ring's veil), scaled out to the front each frame (its height stays)
            if (!s_veilMesh)
            {
                s_veilMesh = VeilMesh();
            }
            GameObject veil = new GameObject("wave veil");
            veil.transform.SetParent(transform, false);
            veil.transform.localPosition = Vector3.up * BandLift;
            veil.AddComponent<MeshFilter>().sharedMesh = s_veilMesh;
            m_veil = veil.AddComponent<MeshRenderer>();
            Material veilMaterial = new Material(material);
            m_veil.sharedMaterial = veilMaterial;
            m_veil.shadowCastingMode = ShadowCastingMode.Off;
            m_veil.receiveShadows = false;
            veil.SetActive(false);
        }

        private static Mesh VeilMesh()
        {
            Vector3[] vertices = new Vector3[2 * (Points + 1)];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[6 * Points];
            for (int i = 0; i <= Points; i++)
            {
                float angle = 2f * Mathf.PI * i / Points;
                Vector3 around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[2 * i] = around;
                vertices[2 * i + 1] = around + Vector3.up * VeilHeight;
                uvs[2 * i] = new Vector2((float)i / Points, 0.5f);
                uvs[2 * i + 1] = new Vector2((float)i / Points, 1f);
                colors[2 * i] = colors[2 * i + 1] = ShimmerColor;
                if (i < Points)
                {
                    int t = 6 * i;
                    triangles[t] = 2 * i;
                    triangles[t + 1] = 2 * i + 1;
                    triangles[t + 2] = 2 * i + 2;
                    triangles[t + 3] = 2 * i + 1;
                    triangles[t + 4] = 2 * i + 3;
                    triangles[t + 5] = 2 * i + 2;
                }
            }
            Mesh mesh = new Mesh { name = "NhxWaveVeil" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            // Radius 1: the renderer's bounds scale out with it
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Brighten(float flare, float flicker, float ringFlare)
        {
            m_lit = true;
            for (int i = 0; i < m_discs.Count; i++)
            {
                if (!m_discs[i])
                {
                    continue;
                }
                Color tint = m_discTints[i];
                Color lit = tint * (1f + FlareDisc * flare);
                lit.a = Mathf.Lerp(tint.a, 1f, flare);
                m_block.SetColor(s_tint, lit);
                m_discs[i].SetPropertyBlock(m_block);
            }
            for (int i = 0; i < m_crystalLights.Count; i++)
            {
                if (m_crystalLights[i])
                {
                    m_crystalLights[i].intensity = m_crystalBase[i] * (1f + FlareLight * flare);
                    m_crystalLights[i].range = m_crystalRange[i] * (1f + FlareReach * flare);
                }
            }
            if (m_daisLight)
            {
                m_daisLight.enabled = flare > 0.001f;
                m_daisLight.intensity = DaisLight * flare;
            }
            for (int i = 0; i < m_stoneLights.Count; i++)
            {
                if (m_stoneLights[i])
                {
                    m_stoneLights[i].intensity = m_stoneBase[i] * (1f + FlickerLight * flicker);
                }
            }
            if (m_ring)
            {
                m_ring.Pulse(ringFlare);
            }
        }

        // The front `elapsed` seconds out from the altar: from the dais's edge to just past the stones, fading in as it
        // leaves the dais, and to half as it goes
        private void Shimmer(float elapsed)
        {
            float radius = elapsed * TwinsWave.Speed;
            bool shown = radius >= ShimmerFrom && radius <= m_radius + 1f;
            if (m_band.gameObject.activeSelf != shown)
            {
                m_band.gameObject.SetActive(shown);
                m_veil.gameObject.SetActive(shown);
            }
            if (!shown)
            {
                return;
            }
            for (int i = 0; i < Points; i++)
            {
                float angle = 2f * Mathf.PI * i / Points;
                m_points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            }
            m_band.SetPositions(m_points);
            float fade = Mathf.Clamp01((radius - ShimmerFrom) / (TwinsWave.Speed * FadeIn))
                * (1f - 0.5f * Mathf.Clamp01(radius / m_radius))
                * Mathf.Clamp01(m_radius + 1f - radius);
            Color color = ShimmerColor * BandBright;
            color.a = fade;
            m_band.startColor = color;
            m_band.endColor = color;
            m_veil.transform.localScale = new Vector3(radius, 1f, radius);
            m_block.SetColor(s_tint, new Color(VeilBright, VeilBright, VeilBright, 0.5f * fade));
            m_veil.SetPropertyBlock(m_block);
        }

        // As the altar made them, and no shimmer
        private void Rest()
        {
            if (!m_lit)
            {
                return;
            }
            m_lit = false;
            foreach (MeshRenderer disc in m_discs)
            {
                if (disc)
                {
                    disc.SetPropertyBlock(null);
                }
            }
            for (int i = 0; i < m_crystalLights.Count; i++)
            {
                if (m_crystalLights[i])
                {
                    m_crystalLights[i].intensity = m_crystalBase[i];
                    m_crystalLights[i].range = m_crystalRange[i];
                }
            }
            for (int i = 0; i < m_stoneLights.Count; i++)
            {
                if (m_stoneLights[i])
                {
                    m_stoneLights[i].intensity = m_stoneBase[i];
                }
            }
            if (m_daisLight)
            {
                m_daisLight.enabled = false;
            }
            if (m_ring)
            {
                m_ring.Pulse(0f);
            }
            if (m_band && m_band.gameObject.activeSelf)
            {
                m_band.gameObject.SetActive(false);
                m_veil.gameObject.SetActive(false);
            }
        }
    }
}
