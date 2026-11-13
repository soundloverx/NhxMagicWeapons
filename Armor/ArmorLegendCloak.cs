using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Armor
{
    // One of the legendary looks at high levels (ArmorLegend): the cloak's cloth changes (the user, 2026-11-13, after
    // mock-ups of patterns on the whole sets' cloth: "the sets will look too "busy" :) Let's try to only apply the idea on
    // the cloak instead of the entire set. Static texture, but with a faint pulse"). From level 15 the cloak's texture is a
    // patterned copy (Tools\legend_cloaks.py: storm clouds on Stormcaller's Raiment, dark ember veins on the Firecaller's
    // Regalia); from 20 the lightning or the veins also glow, the glow swelling and ebbing a little. On the cloak's
    // attach_skin, on every game but a dedicated server, with the whole set on (and the staff, if the looks need it), at the
    // set's level. The cloak's material is swapped on the worn copy only for copies with the new textures (made once per
    // material); the glow's strength is set per renderer and slot with a MaterialPropertyBlock
    internal class ArmorLegendCloak : MonoBehaviour
    {
        // The set's four pieces and staff (their prefab names' hashes), its name, and which one it is
        public int[] m_set = new int[0];
        public int m_staff;
        public string m_setName = "";
        public bool m_storm;
        // The cloak's renderer, and its texture (by name: the Firecaller's red copy's name starts with it too)
        public string m_cloak = "v_Cape";

        // From these levels: the pattern, then its glow
        internal const int PatternFrom = 15;
        internal const int GlowFrom = 20;
        // The glow's strength (times the emission map's colors), and its pulse: down by this much and back, this often. The
        // user, 2026-11-13, after trying it: "Can we make the pattern about 80% of current visibility and make pulse from
        // almost invisible?" (the glow was 0.6 and 0.65, the pulse down by a quarter; the pattern in the cloth went to 80%
        // too, Tools\legend_cloaks.py)
        private const float StormGlow = 0.48f;
        private const float FireGlow = 0.52f;
        private const float PulseDepth = 0.95f;
        private const float PulsePeriod = 3.5f;
        private const float LevelInterval = 0.25f;

        private const string Resource = "legend_cloaks";
        private const string Texture = "T_1018303_Equip_05_D";
        private static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_emissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");

        private static RecoloredTextures s_textures;
        private static bool s_loaded;
        // Each cloak material's copies: patterned, and patterned and glowing
        private static readonly Dictionary<Material, Material[]> s_copies = new Dictionary<Material, Material[]>();

        private VisEquipment m_equipment;
        private Character m_wearer;
        private Renderer m_renderer;
        private int m_slot = -1;
        private Material[] m_own;
        private MaterialPropertyBlock m_block;
        private int m_stage;
        private float m_levelTimer;
        private float m_phase;

        private void Awake()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
            }
        }

        private void Start()
        {
            m_equipment = GetComponentInParent<VisEquipment>();
            m_wearer = GetComponentInParent<Character>();
            m_phase = m_equipment ? Mathf.Abs(m_equipment.GetInstanceID() % 997) * 0.37f : 0f;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != m_cloak)
                {
                    continue;
                }
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Texture main = materials[i] && materials[i].HasProperty(s_mainTex) ? materials[i].GetTexture(s_mainTex) : null;
                    if (main && main.name.StartsWith(Texture))
                    {
                        m_renderer = renderer;
                        m_slot = i;
                        m_own = materials;
                    }
                }
            }
            if (!m_renderer)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            m_levelTimer -= Time.deltaTime;
            if (m_levelTimer <= 0f)
            {
                m_levelTimer = LevelInterval;
                int level = ArmorLegend.On(ArmorLegend.Ideas.Cloak) && ArmorLegend.Shows(m_equipment, m_set, m_staff)
                    ? ArmorLegend.Level(m_wearer, m_setName) : 0;
                int stage = level >= GlowFrom ? 2 : level >= PatternFrom ? 1 : 0;
                if (stage != m_stage)
                {
                    Show(stage);
                }
            }
            if (m_stage == 2)
            {
                float pulse = 1f - PulseDepth * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (Time.time + m_phase) / PulsePeriod));
                m_renderer.GetPropertyBlock(m_block, m_slot);
                m_block.SetColor(s_emissionColor, Color.white * ((m_storm ? StormGlow : FireGlow) * pulse));
                m_renderer.SetPropertyBlock(m_block, m_slot);
            }
        }

        private void Show(int stage)
        {
            Material[] copies = stage > 0 ? Copies(m_own[m_slot]) : null;
            if (stage > 0 && copies == null)
            {
                return;
            }
            m_stage = stage;
            Material[] materials = (Material[])m_own.Clone();
            if (stage > 0)
            {
                materials[m_slot] = copies[stage - 1];
            }
            m_renderer.sharedMaterials = materials;
            if (m_block == null)
            {
                m_block = new MaterialPropertyBlock();
            }
            // No glow of its own but in the glowing copy
            m_block.Clear();
            m_renderer.SetPropertyBlock(m_block, m_slot);
        }

        // The material's copies with the set's new textures, or null if they aren't there (the file missing)
        private Material[] Copies(Material source)
        {
            if (s_copies.TryGetValue(source, out Material[] copies))
            {
                return copies;
            }
            if (!s_loaded)
            {
                s_loaded = true;
                s_textures = RecoloredTextures.Load(Resource, "_nhxlegend", "the cloaks look the same at every level");
            }
            string set = m_storm ? "storm/" : "fire/";
            if (s_textures == null || !s_textures.Textures.TryGetValue(set + Texture, out Texture2D pattern)
                || !s_textures.Textures.TryGetValue(set + "emission", out Texture2D emission))
            {
                s_copies[source] = null;
                return null;
            }
            Texture original = source.GetTexture(s_mainTex);
            RecoloredTextures.Matching(pattern, original);
            RecoloredTextures.Matching(emission, original);
            Material patterned = new Material(source) { name = source.name + "_nhx_pattern" };
            patterned.SetTexture(s_mainTex, pattern);
            Material glowing = new Material(patterned) { name = source.name + "_nhx_glow" };
            glowing.SetTexture(s_emissionMap, emission);
            glowing.SetColor(s_emissionColor, Color.white * (m_storm ? StormGlow : FireGlow));
            copies = new[] { patterned, glowing };
            s_copies[source] = copies;
            return copies;
        }
    }
}
