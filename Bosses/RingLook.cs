using NhxMagicWeapons.Staves;
using UnityEngine;
using UnityEngine.Rendering;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The twins' ring, drawn (the fight's redesign, agreed with the user 2026-11-15: "While the twins live, a glowing band
    // runs along the ground between the 12 stones, with arcs between neighbouring crystals, visible from both sides"), so
    // the edge that holds (TwinsRing) is one players can see, from within and from outside. On the altar (TwinsAltars: its
    // location's root), on every game, shown while twins called into its arena live (TwinsRing.Called), fading in and out:
    // - A band of light on the ground along the ring, and a low veil rising from it, storm blue at the storm's stones and
    //   Muspelheim's orange at the fire's, one into the other between them
    // - An arc from each stone's crystal to the next, along the ring, wavering
    // The arena is leveled to the altar's height out past the stones (TwinsAltars), so the ring is flat. Looks only: no
    // collider, nothing networked
    internal class RingLook : MonoBehaviour
    {
        // Set by the altar
        public float m_radius = 40f;
        public int m_stones = 12;

        // The band on the ground (meters wide, above the ground) and the veil over it (meters high); a piece of ring this
        // many times between two stones
        private const float BandWidth = 0.7f;
        private const float BandLift = 0.12f;
        private const float VeilHeight = 2.2f;
        private const int Pieces = 16;
        // How bright each is (the shader draws twice its color), breathing by Breath every BreathTime seconds
        private const float BandBright = 0.9f;
        private const float VeilBright = 0.3f;
        private const float Breath = 0.2f;
        private const float BreathTime = 3f;
        // The arcs: points along each, how wide, how far they waver (meters), how often they change (seconds)
        private const int ArcPoints = 14;
        private const float ArcWidth = 0.09f;
        private const float ArcWaver = 0.22f;
        private const float ArcEvery = 0.09f;
        private const float ArcBright = 1.2f;
        // A crystal's height over its stone's foot, if the stones have none to ask
        private const float CrystalHeight = 1.4f;
        // Twins looked for this often; shown and hidden over this long (seconds)
        private const float CheckEvery = 0.5f;
        private const float FadeTime = 1.5f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");
        private static Texture2D s_soft;

        private GameObject m_root;
        private MeshRenderer m_band;
        private MeshRenderer m_veil;
        private LineRenderer[] m_arcs;
        private Vector3[] m_points;
        private MaterialPropertyBlock m_block;
        private float m_arcHeight;
        private float m_shown;
        private bool m_called;
        private float m_checkTimer;
        private float m_arcTimer;

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
                m_called = TwinsRing.Called(transform.position);
            }
            m_shown = Mathf.MoveTowards(m_shown, m_called ? 1f : 0f, Time.deltaTime / FadeTime);
            if (m_shown <= 0f)
            {
                if (m_root && m_root.activeSelf)
                {
                    m_root.SetActive(false);
                }
                return;
            }
            if (!m_root)
            {
                Build();
            }
            if (!m_root.activeSelf)
            {
                m_root.SetActive(true);
            }
            Draw();
        }

        private void OnDestroy()
        {
            if (m_band)
            {
                Destroy(m_band.GetComponent<MeshFilter>().sharedMesh);
                Destroy(m_veil.GetComponent<MeshFilter>().sharedMesh);
            }
        }

        // A stone's color, storm and fire in turn, as the altar builds them
        private static Color StoneColor(int stone) => stone % 2 == 1 ? Looks.Fire : Looks.Storm;

        // A point of the ring, in the altar's own space: `turn` of the way around from the first stone, `height` up
        private Vector3 Point(float turn, float radius, float height)
        {
            Vector3 point = Quaternion.AngleAxis(360f * turn, Vector3.up) * Vector3.forward * radius;
            point.y = height;
            return point;
        }

        private void Build()
        {
            m_root = new GameObject("ring");
            m_root.transform.SetParent(transform, false);
            m_block = new MaterialPropertyBlock();
            if (!s_soft)
            {
                // Full in its middle, fading to nothing at its edges
                s_soft = LineMaterials.Band("NhxRingSoft", across => (1f - across) * (1f - across));
            }
            Material material = LineMaterials.Additive(Wrath.Look ? Wrath.Look.m_glowMaterial : null);
            material.mainTexture = s_soft;

            // The band lies across the ring's line, the texture's width over its own; the veil stands on the line, the
            // texture's upper half over its height, so it's brightest at the ground
            int count = m_stones * Pieces;
            m_band = Strip("band", material, count,
                turn => Point(turn, m_radius - BandWidth * 0.5f, BandLift), turn => Point(turn, m_radius + BandWidth * 0.5f, BandLift), 0f, 1f);
            m_veil = Strip("veil", material, count,
                turn => Point(turn, m_radius, BandLift), turn => Point(turn, m_radius, BandLift + VeilHeight), 0.5f, 1f);

            Transform crystal = transform.Find("stone0/crystal");
            m_arcHeight = crystal ? crystal.localPosition.y : CrystalHeight;
            m_arcs = new LineRenderer[m_stones];
            m_points = new Vector3[ArcPoints];
            for (int i = 0; i < m_stones; i++)
            {
                GameObject go = new GameObject("arc" + i);
                go.transform.SetParent(m_root.transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = material;
                line.textureMode = LineTextureMode.Stretch;
                line.positionCount = ArcPoints;
                line.widthMultiplier = ArcWidth;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                m_arcs[i] = line;
            }
            Waver();
        }

        // A strip around the ring, `count` pieces of it, between the points `from` and `to` give for each turn, the
        // texture's width from `vFrom` to `vTo` across it, colored as the stones it passes
        private MeshRenderer Strip(string name, Material material, int count, System.Func<float, Vector3> from, System.Func<float, Vector3> to, float vFrom, float vTo)
        {
            Vector3[] vertices = new Vector3[2 * (count + 1)];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[6 * count];
            for (int i = 0; i <= count; i++)
            {
                float turn = (float)i / count;
                float stone = turn * m_stones;
                int at = Mathf.FloorToInt(stone);
                Color color = Color.Lerp(StoneColor(at), StoneColor(at + 1), Mathf.SmoothStep(0f, 1f, stone - at));
                vertices[2 * i] = from(turn);
                vertices[2 * i + 1] = to(turn);
                uvs[2 * i] = new Vector2(turn * m_stones, vFrom);
                uvs[2 * i + 1] = new Vector2(turn * m_stones, vTo);
                colors[2 * i] = colors[2 * i + 1] = color;
                if (i < count)
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
            Mesh mesh = new Mesh { name = "NhxRing" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            GameObject go = new GameObject(name);
            go.transform.SetParent(m_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void Draw()
        {
            float breath = 1f + Breath * Mathf.Sin(2f * Mathf.PI * Time.time / BreathTime);
            Tint(m_band, BandBright * breath);
            Tint(m_veil, VeilBright * breath);
            m_arcTimer -= Time.deltaTime;
            if (m_arcTimer <= 0f)
            {
                m_arcTimer = ArcEvery;
                Waver();
            }
            for (int i = 0; i < m_arcs.Length; i++)
            {
                Color from = StoneColor(i) * ArcBright;
                Color to = StoneColor(i + 1) * ArcBright;
                from.a = to.a = m_shown;
                m_arcs[i].startColor = from;
                m_arcs[i].endColor = to;
            }
        }

        // The shader draws 2 × its tint × the mesh's color × the texture: the tint's alpha fades it
        private void Tint(MeshRenderer renderer, float bright)
        {
            m_block.SetColor(s_tint, new Color(bright, bright, bright, 0.5f * m_shown));
            renderer.SetPropertyBlock(m_block);
        }

        // Each arc anew: along the ring from its crystal to the next, off its line by a little everywhere but at its ends
        private void Waver()
        {
            for (int i = 0; i < m_arcs.Length; i++)
            {
                for (int p = 0; p < ArcPoints; p++)
                {
                    float along = (float)p / (ArcPoints - 1);
                    Vector3 point = Point((i + along) / m_stones, m_radius, m_arcHeight);
                    m_points[p] = point + Random.insideUnitSphere * (ArcWaver * Mathf.Sin(Mathf.PI * along));
                }
                m_arcs[i].SetPositions(m_points);
            }
        }
    }
}
