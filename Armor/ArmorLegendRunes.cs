using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Armor
{
    // One of the legendary looks at high levels (ArmorLegend): runes, light spreading over the set's gems and violet
    // bands as it levels up, a part at a time (the user, 2026-11-13, after mock-up renders: "Let's also try the runes";
    // then "Can we keep runes for the Firecaller set only?": only the Firecaller's Regalia has them, but this works for
    // either). The shoulders from 12, the robe from 14, the greaves from 16, the hood from 18 (the cloak has its own
    // look, ArmorLegendCloak, from 15 and 20). Glow maps made by Tools\legend_runes.py for the textures with something
    // to light (the gems', the metal's, the cloth's), set per renderer and slot with a MaterialPropertyBlock, so the
    // materials are shared as they are and each part lights up on its own. On each piece's attach_skin (not the
    // cloak's), on every game but a dedicated server, with the whole set on (and the staff, if the looks need it), at
    // the set's level. The robe's right shoulder piece can't glow: its fresnel shader has no emission
    internal class ArmorLegendRunes : MonoBehaviour
    {
        // The set's four pieces and staff (their prefab names' hashes), its name, and which one it is
        public int[] m_set = new int[0];
        public int m_staff;
        public string m_setName = "";
        public bool m_storm;

        // From which level each part lights up, by its renderer's name
        internal static readonly Dictionary<string, int> Stages = new Dictionary<string, int>
        {
            { "x_Plechi", 12 }, { "x_NaruchLeft", 12 }, { "x_Grud", 14 }, { "x_Yubka", 14 }, { "z_Pants", 16 }, { "y_Helmet", 18 },
        };
        // The glow's strength, times the maps' colors (the mock-ups'; the crystals' glow is about 6 at its brightest)
        private const float Strength = 2f;
        private const float LevelInterval = 0.25f;

        private const string Resource = "legend_runes";
        // The Firecaller's red textures are named as the originals and this
        private const string RedSuffix = "_nhxfire";
        private static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_emissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");

        private static RecoloredTextures s_maps;
        private static bool s_loaded;

        private class Part
        {
            public Renderer m_renderer;
            public int m_slot;
            public int m_level;
            public Texture2D m_map;
            public bool m_lit;
        }

        private readonly List<Part> m_parts = new List<Part>();
        private VisEquipment m_equipment;
        private Character m_wearer;
        private MaterialPropertyBlock m_block;
        private float m_levelTimer;

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
            if (!s_loaded)
            {
                s_loaded = true;
                s_maps = RecoloredTextures.Load(Resource, "_nhxrunes", "the armor has no runes");
            }
            string set = m_storm ? "storm/" : "fire/";
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (s_maps == null || !Stages.TryGetValue(renderer.name, out int level))
                {
                    continue;
                }
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    Texture main = material && material.HasProperty(s_mainTex) && material.HasProperty(s_emissionMap) ? material.GetTexture(s_mainTex) : null;
                    if (!main)
                    {
                        continue;
                    }
                    string name = main.name.EndsWith(RedSuffix) ? main.name.Substring(0, main.name.Length - RedSuffix.Length) : main.name;
                    if (s_maps.Textures.TryGetValue(set + name, out Texture2D map))
                    {
                        RecoloredTextures.Matching(map, main);
                        m_parts.Add(new Part { m_renderer = renderer, m_slot = i, m_level = level, m_map = map });
                    }
                }
            }
            if (m_parts.Count == 0)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            m_levelTimer -= Time.deltaTime;
            if (m_levelTimer > 0f)
            {
                return;
            }
            m_levelTimer = LevelInterval;
            int level = ArmorLegend.On(ArmorLegend.Ideas.Runes) && ArmorLegend.Shows(m_equipment, m_set, m_staff)
                ? ArmorLegend.Level(m_wearer, m_setName) : 0;
            if (m_block == null)
            {
                m_block = new MaterialPropertyBlock();
            }
            foreach (Part part in m_parts)
            {
                bool lit = level >= part.m_level;
                if (lit == part.m_lit || !part.m_renderer)
                {
                    continue;
                }
                part.m_lit = lit;
                part.m_renderer.GetPropertyBlock(m_block, part.m_slot);
                m_block.SetTexture(s_emissionMap, part.m_map);
                m_block.SetColor(s_emissionColor, lit ? Color.white * Strength : Color.black);
                part.m_renderer.SetPropertyBlock(m_block, part.m_slot);
            }
        }
    }
}
