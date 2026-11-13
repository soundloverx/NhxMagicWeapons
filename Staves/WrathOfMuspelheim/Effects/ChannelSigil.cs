using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The Sigil of Muspelheim, one of the Firecaller's Regalia's three showpieces (see ChannelWings): with the set from
    // level 10 (ArmorLegend.SigilFrom; the user: "the runes on the ground can appear at level 10"), while the beam is
    // channelled (FireChannel), a rune circle burns on the ground under the wearer, lighting up ring by ring
    // with the channel's first three hits: a sun star in the middle, a band of runes around it, a ring of flame teeth
    // outside, each turning slowly its own way, pulsing at every hit, small flames licking up along the outer ring. It
    // fades when the channel ends.
    // Three layers of light (additive: they only brighten the ground) drawn on a disc that follows the ground under the
    // wearer, a little above it; in tall grass the blades hide most of it but the flames. The rings are textures made
    // here the first time one shows. Every game draws it for whoever holds it up this frame (Hold): the channel, or
    // nhx_armorlook show. An object of its own, which removes itself once it has faded
    internal class ChannelSigil : MonoBehaviour
    {
        // Its size, and the disc it's drawn on: rings of vertices, each dropped onto the ground (within GroundReach up or
        // down of the wearer's feet; farther, it stays level) and lifted a little
        private const float Radius = 2.2f;
        private const int Rings = 6;
        private const int Segments = 40;
        private const float GroundReach = 1.2f;
        private const float Lift = 0.06f;
        // The ground is looked up again when the wearer has moved this far
        private const float Moved = 0.08f;
        // The layers turn this fast (degrees a second), from the middle out
        private static readonly float[] Spin = { 14f, -9f, 6f };
        // A layer fades in or out in this time; a hit makes them all this much brighter for a moment
        private const float FadeTime = 0.35f;
        private const float FlareBright = 0.6f;
        // Its color (the shader draws twice this), and how long it stays once faded, for its last flames
        private static readonly Color Fire = new Color(1f, 0.42f, 0.09f);
        private const float Linger = 2f;
        // Flames along the outer ring (when it's lit) and embers rising inside, a second
        private const float FlamesPerSecond = 40f;
        private const float EmbersPerSecond = 10f;
        private const float FlameRing = 0.9f;
        private const float LightIntensity = 1.3f;
        private const float LightRange = 6f;

        private const int TextureSize = 512;
        // A drawn line's soft edge (a pixel), and its halo
        private const float Soft = 2f / TextureSize;
        private const float Halo = 0.3f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");
        private static readonly Dictionary<Character, ChannelSigil> s_sigils = new Dictionary<Character, ChannelSigil>();
        private static Texture2D[] s_textures;

        private class Layer
        {
            public Mesh m_mesh;
            public MeshRenderer m_renderer;
            public float m_alpha;
            public float m_turn;
        }

        private Character m_wearer;
        private Layer[] m_layers;
        private Vector3[] m_vertices;
        private Vector2[] m_flat;
        private Vector2[] m_uvs;
        private ParticleSystem m_flames;
        private ParticleSystem m_embers;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private Vector3 m_builtAt = new Vector3(float.MaxValue, 0f, 0f);
        private int m_heldFrame = -1;
        private int m_rings;
        private float m_flareIn;
        private float m_faded;
        private float m_flameDebt;
        private float m_emberDebt;

        // Called every frame the wearer's sigil shows, with how many of its rings are lit (the channel's hits so far) and
        // how much the last hit still flares (1 at the hit). Not called: it fades
        internal static void Hold(Character wearer, int rings, float flare)
        {
            if (!wearer || rings <= 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }
            if (!s_sigils.TryGetValue(wearer, out ChannelSigil sigil) || !sigil)
            {
                if (!Staff.Look)
                {
                    return;
                }
                sigil = new GameObject("NhxFirecallerSigil").AddComponent<ChannelSigil>();
                sigil.Build(wearer, Staff.Look);
                s_sigils[wearer] = sigil;
            }
            sigil.m_heldFrame = Time.frameCount;
            sigil.m_rings = rings;
            sigil.m_flareIn = Mathf.Max(sigil.m_flareIn, flare);
        }

        // Its textures made ahead (as the staff is added, while the game loads), so the first sigil doesn't stall a fight
        internal static void Prepare()
        {
            if (s_textures == null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                s_textures = MakeTextures();
            }
        }

        private void Build(Character wearer, BeamLook look)
        {
            m_wearer = wearer;
            m_block = new MaterialPropertyBlock();
            Prepare();
            transform.position = wearer.transform.position;

            int count = 1 + Rings * Segments;
            m_vertices = new Vector3[count];
            m_flat = new Vector2[count];
            m_uvs = new Vector2[count];
            Color32[] white = new Color32[count];
            white[0] = new Color32(255, 255, 255, 255);
            for (int ring = 1; ring <= Rings; ring++)
            {
                float radius = Radius * ring / Rings;
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / Segments;
                    int index = 1 + (ring - 1) * Segments + segment;
                    m_flat[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    white[index] = new Color32(255, 255, 255, 255);
                }
            }
            for (int i = 0; i < count; i++)
            {
                m_vertices[i] = new Vector3(m_flat[i].x, Lift, m_flat[i].y);
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

            m_layers = new Layer[s_textures.Length];
            for (int i = 0; i < m_layers.Length; i++)
            {
                GameObject go = new GameObject("layer" + i);
                go.transform.SetParent(transform, false);
                Mesh mesh = new Mesh { name = "NhxFirecallerSigil" };
                mesh.MarkDynamic();
                mesh.vertices = m_vertices;
                mesh.colors32 = white;
                mesh.uv = m_uvs;
                mesh.triangles = triangles.ToArray();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                Material material = LineMaterials.Additive(look.m_glowMaterial);
                material.mainTexture = s_textures[i];
                // With soft particles on, the shader fades what's close in front of the ground: only the last centimeters
                if (material.HasProperty("_InvFade"))
                {
                    material.SetFloat("_InvFade", 30f);
                }
                renderer.sharedMaterial = material;
                renderer.sortingOrder = i;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                m_layers[i] = new Layer { m_mesh = mesh, m_renderer = renderer, m_turn = Random.Range(0f, 360f) };
            }

            m_flames = FireParticles.Flames(transform, 0.16f, 0.3f, 0.4f, 0.7f, 120);
            m_embers = FireParticles.Embers(transform, 1f, 2f, 60);
            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            m_light = lightObject.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = look.m_lightColor;
            m_light.range = LightRange;
            m_light.intensity = 0f;
            m_light.shadows = LightShadows.None;
        }

        private void OnDestroy()
        {
            if (!ReferenceEquals(m_wearer, null) && s_sigils.TryGetValue(m_wearer, out ChannelSigil sigil) && sigil == this)
            {
                s_sigils.Remove(m_wearer);
            }
            if (m_layers != null)
            {
                foreach (Layer layer in m_layers)
                {
                    Destroy(layer.m_mesh);
                    if (layer.m_renderer)
                    {
                        Destroy(layer.m_renderer.sharedMaterial);
                    }
                }
            }
        }

        // After whoever holds it up has said so (Update)
        private void LateUpdate()
        {
            if (!m_wearer || m_wearer.IsDead())
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            bool held = m_heldFrame == Time.frameCount;
            float flare = m_flareIn;
            m_flareIn = 0f;
            float brightest = 0f;
            for (int i = 0; i < m_layers.Length; i++)
            {
                Layer layer = m_layers[i];
                layer.m_alpha = Mathf.MoveTowards(layer.m_alpha, held && m_rings > i ? 1f : 0f, dt / FadeTime);
                brightest = Mathf.Max(brightest, layer.m_alpha);
            }
            if (brightest <= 0f)
            {
                foreach (Layer layer in m_layers)
                {
                    layer.m_renderer.enabled = false;
                }
                m_light.enabled = false;
                m_faded += dt;
                if (m_faded > Linger)
                {
                    Destroy(gameObject);
                }
                return;
            }
            m_faded = 0f;

            // Under the wearer, on the ground as it is there
            Vector3 feet = m_wearer.transform.position;
            transform.position = feet;
            if ((feet - m_builtAt).sqrMagnitude > Moved * Moved)
            {
                m_builtAt = feet;
                Ground(feet);
                foreach (Layer layer in m_layers)
                {
                    layer.m_mesh.vertices = m_vertices;
                    layer.m_mesh.RecalculateBounds();
                }
            }

            float bright = 1f + FlareBright * flare;
            for (int i = 0; i < m_layers.Length; i++)
            {
                Layer layer = m_layers[i];
                layer.m_renderer.enabled = layer.m_alpha > 0f;
                if (layer.m_alpha <= 0f)
                {
                    continue;
                }
                // Turning: the texture turns on the disc
                layer.m_turn += Spin[i] * dt;
                float turn = layer.m_turn * Mathf.Deg2Rad;
                float cos = Mathf.Cos(turn) * 0.5f / Radius;
                float sin = Mathf.Sin(turn) * 0.5f / Radius;
                for (int v = 0; v < m_uvs.Length; v++)
                {
                    Vector2 flat = m_flat[v];
                    m_uvs[v] = new Vector2(0.5f + flat.x * cos - flat.y * sin, 0.5f + flat.x * sin + flat.y * cos);
                }
                layer.m_mesh.uv = m_uvs;
                Color tint = Fire * bright;
                tint.a = 0.5f * layer.m_alpha;
                m_block.SetColor(s_tint, tint);
                layer.m_renderer.SetPropertyBlock(m_block);
            }
            m_light.enabled = true;
            m_light.intensity = LightIntensity * brightest * bright;

            // Flames licking up along the outer ring, embers rising inside
            float outer = m_layers[m_layers.Length - 1].m_alpha;
            m_flameDebt += FlamesPerSecond * outer * dt;
            while (m_flameDebt >= 1f)
            {
                m_flameDebt -= 1f;
                FireParticles.Emit(m_flames, OnDisc(FlameRing, Random.Range(0f, 2f * Mathf.PI)), Vector3.up * 0.35f);
            }
            m_emberDebt += EmbersPerSecond * brightest * dt;
            while (m_emberDebt >= 1f)
            {
                m_emberDebt -= 1f;
                FireParticles.Emit(m_embers, OnDisc(Mathf.Sqrt(Random.value) * 0.95f, Random.Range(0f, 2f * Mathf.PI)), Vector3.up * 0.4f);
            }
        }

        // Each vertex onto the ground under it
        private void Ground(Vector3 feet)
        {
            for (int i = 0; i < m_vertices.Length; i++)
            {
                Vector3 above = feet + new Vector3(m_flat[i].x, GroundReach, m_flat[i].y);
                float height = 0f;
                if (Physics.Raycast(above, Vector3.down, out RaycastHit hit, 2f * GroundReach, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    height = hit.point.y - feet.y;
                }
                m_vertices[i] = new Vector3(m_flat[i].x, height + Lift, m_flat[i].y);
            }
        }

        // A point of the disc in the world: at this share of its radius and this angle, at the ground's height there
        // (the nearest vertex's)
        private Vector3 OnDisc(float share, float angle)
        {
            int ring = Mathf.Clamp(Mathf.RoundToInt(share * Rings), 0, Rings);
            int segment = Mathf.RoundToInt(angle / (2f * Mathf.PI) * Segments) % Segments;
            float height = ring == 0 ? m_vertices[0].y : m_vertices[1 + (ring - 1) * Segments + segment].y;
            float radius = share * Radius;
            return transform.position + new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }

        // The three rings as white textures, their lines in the alpha: the disc from -1 to 1 across
        private static Texture2D[] MakeTextures()
        {
            Color32[][] pixels = { new Color32[TextureSize * TextureSize], new Color32[TextureSize * TextureSize], new Color32[TextureSize * TextureSize] };
            Vector2[] star = new Vector2[8];
            for (int i = 0; i < star.Length; i++)
            {
                float angle = 0.25f * Mathf.PI * i;
                star[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * StarRadius;
            }
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    Vector2 point = new Vector2((x + 0.5f) / TextureSize * 2f - 1f, (y + 0.5f) / TextureSize * 2f - 1f);
                    float radius = point.magnitude;
                    float angle = Mathf.Atan2(point.y, point.x);
                    int index = y * TextureSize + x;
                    pixels[0][index] = White(radius < 0.45f ? Sun(point, radius, star) : 0f);
                    pixels[1][index] = White(radius > 0.38f && radius < 0.76f ? Runes(radius, angle) : 0f);
                    pixels[2][index] = White(radius > 0.74f && radius < 1f ? Teeth(point, radius, angle) : 0f);
                }
            }
            Texture2D[] textures = new Texture2D[pixels.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                textures[i] = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
                {
                    name = "nhx_sigil" + i,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                textures[i].SetPixels32(pixels[i]);
                textures[i].Apply(true, true);
            }
            return textures;
        }

        private static Color32 White(float alpha)
        {
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        // A line `width` wide (each side), seen from `distance`: solid, a soft edge, and a faint halo
        private static float Stroke(float distance, float width)
        {
            float line = Mathf.Clamp01(1f - (distance - width) / Soft);
            float spread = distance / (width * 3.5f);
            return Mathf.Max(line, Halo * Mathf.Exp(-spread * spread));
        }

        private static float ToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return (point - (a + ab * t)).magnitude;
        }

        // The middle: a sun star (two squares in a circle) around a small circle and a dot
        private const float StarRadius = 0.34f;

        private static float Sun(Vector2 point, float radius, Vector2[] star)
        {
            float alpha = Mathf.Max(Stroke(Mathf.Abs(radius - StarRadius), 0.008f), Stroke(Mathf.Abs(radius - 0.11f), 0.006f));
            alpha = Mathf.Max(alpha, Mathf.Clamp01(1f - (radius - 0.035f) / Soft));
            float distance = float.MaxValue;
            for (int i = 0; i < star.Length; i++)
            {
                // Each corner to the next but one: the even ones make one square, the odd ones the other
                distance = Mathf.Min(distance, ToSegment(point, star[i], star[(i + 2) % star.Length]));
            }
            return Mathf.Max(alpha, Stroke(distance, 0.006f));
        }

        // The band of runes: sixteen of the elder futhark between two circles, their heads outward. Each as strokes in a
        // box 0.6 wide (from -0.3) and 1 high
        private const float RuneFoot = 0.495f;
        private const float RuneSize = 0.15f;
        private static readonly float[][] RuneStrokes =
        {
            new[] { -0.2f, 0f, -0.2f, 1f, -0.2f, 0.55f, 0.25f, 0.85f, -0.2f, 0.25f, 0.25f, 0.55f },                         // fehu
            new[] { -0.25f, 0f, -0.25f, 1f, -0.25f, 1f, 0.25f, 0.6f, 0.25f, 0.6f, 0.25f, 0f },                             // uruz
            new[] { -0.15f, 0f, -0.15f, 1f, -0.15f, 0.75f, 0.25f, 0.5f, 0.25f, 0.5f, -0.15f, 0.25f },                      // thurisaz
            new[] { -0.2f, 0f, -0.2f, 1f, -0.2f, 1f, 0.25f, 0.7f, -0.2f, 0.65f, 0.25f, 0.35f },                            // ansuz
            new[] { -0.2f, 0f, -0.2f, 1f, -0.2f, 1f, 0.2f, 0.75f, 0.2f, 0.75f, -0.2f, 0.5f, -0.2f, 0.5f, 0.25f, 0f },      // raidho
            new[] { 0.2f, 0.9f, -0.2f, 0.5f, -0.2f, 0.5f, 0.2f, 0.1f },                                                    // kenaz
            new[] { -0.3f, 0.1f, 0.3f, 0.9f, -0.3f, 0.9f, 0.3f, 0.1f },                                                    // gebo
            new[] { -0.25f, 0f, -0.25f, 1f, 0.25f, 0f, 0.25f, 1f, -0.25f, 0.65f, 0.25f, 0.35f },                           // hagalaz
            new[] { 0f, 0f, 0f, 1f, -0.25f, 0.65f, 0.25f, 0.35f },                                                         // nauthiz
            new[] { 0.25f, 1f, -0.25f, 0.7f, -0.25f, 0.7f, 0.25f, 0.3f, 0.25f, 0.3f, -0.25f, 0f },                         // sowilo
            new[] { 0f, 0f, 0f, 1f, 0f, 1f, -0.3f, 0.65f, 0f, 1f, 0.3f, 0.65f },                                           // tiwaz
            new[] { 0f, 0f, 0f, 1f, 0f, 0.5f, -0.3f, 0.95f, 0f, 0.5f, 0.3f, 0.95f },                                       // algiz
            new[] { -0.3f, 0.15f, -0.3f, 0.85f, 0.3f, 0.15f, 0.3f, 0.85f, -0.3f, 0.85f, 0.3f, 0.15f, -0.3f, 0.15f, 0.3f, 0.85f }, // dagaz
            new[] { 0f, 1f, -0.25f, 0.65f, 0f, 1f, 0.25f, 0.65f, -0.25f, 0.65f, 0.3f, 0f, 0.25f, 0.65f, -0.3f, 0f },       // othala
            new[] { 0f, 0.9f, -0.25f, 0.5f, -0.25f, 0.5f, 0f, 0.1f, 0f, 0.1f, 0.25f, 0.5f, 0.25f, 0.5f, 0f, 0.9f },        // ingwaz
            new[] { 0f, 0f, 0f, 1f },                                                                                      // isa
        };

        private static float Runes(float radius, float angle)
        {
            float alpha = Mathf.Max(Stroke(Mathf.Abs(radius - 0.44f), 0.006f), Stroke(Mathf.Abs(radius - 0.70f), 0.006f));
            if (radius < RuneFoot - 0.03f || radius > RuneFoot + RuneSize + 0.03f)
            {
                return alpha;
            }
            float cell = 2f * Mathf.PI / RuneStrokes.Length;
            int rune = Mathf.Clamp(Mathf.FloorToInt((angle + Mathf.PI) / cell), 0, RuneStrokes.Length - 1);
            float middle = -Mathf.PI + (rune + 0.5f) * cell;
            // In the rune's box: to its right is clockwise seen from above, with its head outward
            Vector2 point = new Vector2(-(angle - middle) * radius, radius - RuneFoot) / RuneSize;
            float[] strokes = RuneStrokes[rune];
            float distance = float.MaxValue;
            for (int i = 0; i + 3 < strokes.Length; i += 4)
            {
                distance = Mathf.Min(distance, ToSegment(point, new Vector2(strokes[i], strokes[i + 1]), new Vector2(strokes[i + 2], strokes[i + 3])));
            }
            return Mathf.Max(alpha, Stroke(distance * RuneSize, 0.0065f));
        }

        // The outer ring: flame teeth pointing out between two circles, and eight small discs on the outermost
        private const int ToothCount = 24;

        private static float Teeth(Vector2 point, float radius, float angle)
        {
            float alpha = Mathf.Max(Stroke(Mathf.Abs(radius - 0.80f), 0.007f), Stroke(Mathf.Abs(radius - 0.965f), 0.007f));
            alpha = Mathf.Max(alpha, Stroke(Mathf.Abs(radius - 0.925f), 0.004f));
            float cell = 2f * Mathf.PI / ToothCount;
            int tooth = Mathf.FloorToInt((angle + Mathf.PI) / cell);
            float from = -Mathf.PI + tooth * cell;
            Vector2 foot = new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * 0.81f;
            Vector2 top = new Vector2(Mathf.Cos(from + 0.5f * cell), Mathf.Sin(from + 0.5f * cell)) * 0.915f;
            Vector2 next = new Vector2(Mathf.Cos(from + cell), Mathf.Sin(from + cell)) * 0.81f;
            alpha = Mathf.Max(alpha, Stroke(Mathf.Min(ToSegment(point, foot, top), ToSegment(point, top, next)), 0.005f));
            float eighth = 0.25f * Mathf.PI;
            float nearest = Mathf.Round(angle / eighth) * eighth;
            Vector2 disc = new Vector2(Mathf.Cos(nearest), Mathf.Sin(nearest)) * 0.965f;
            return Mathf.Max(alpha, Mathf.Clamp01(1f - ((point - disc).magnitude - 0.028f) / Soft));
        }
    }
}
