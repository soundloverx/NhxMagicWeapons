using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // How the staff's lightning is drawn (LightningArcs), shared by its bolt and its strike: its materials and colors, and
    // its size by the staff's level. A component on an object of its own, which their prefabs refer to
    internal class LightningLook : MonoBehaviour
    {
        // Particle materials with a soft band across the line; the lines' colors tint them. The bolts' and forks' (Legacy
        // Shaders/Particles/Alpha Blended) doubles them, alpha included; the glow's (Legacy Shaders/Particles/Additive)
        // adds its color times its alpha to what's behind it
        public Material m_coreMaterial;
        public Material m_glowMaterial;
        public Color m_coreColor = new Color(0.85f, 0.9f, 1f, 1f);
        public Color m_glowColor = new Color(0.35f, 0.45f, 1f, 0.7f);
        public Color m_lightColor = new Color(0.55f, 0.65f, 1f);

        // Its size by level (Size); higher levels look like m_maxLevel. The bolts side by side are m_strandSize wide (of a
        // full bolt) at the level the last one came, and m_strandSizePerLevel more at each level after. The glow along
        // their middle and the light where they end are m_glowSize big at level 1, and m_glowSizePerLevel more at each
        // level. The first bolt forks m_branchesPerLevel times per level on average
        public int m_maxLevel = 20;
        public float m_strandSize = 0.35f;
        public float m_strandSizePerLevel = 0.075f;
        public float m_glowSize = 0.37f;
        public float m_glowSizePerLevel = 0.045f;
        public float m_branchesPerLevel = 0.1f;

        // On an object of its own under `container`, with the bolts drawn with the material of `materialEffect`'s "Flash"
        // (a Magic Supremacy effect: Thundersurge's sparks, Legacy Shaders/Particles/Alpha Blended, untextured), with soft
        // edges. The glow fades from the middle to nothing at the edges: drawn over the bolt with the core's material, it
        // looked like a second, fainter line
        internal static LightningLook Create(Transform container, string name, string materialEffect)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(container, false);
            LightningLook look = go.AddComponent<LightningLook>();
            look.m_coreMaterial = LineMaterials.Copy(materialEffect, "Flash");
            look.m_coreMaterial.mainTexture = LineMaterials.Band("nhx_bolt_core", across => 1f - across * across * across * across);
            look.m_glowMaterial = LineMaterials.Additive(look.m_coreMaterial);
            look.m_glowMaterial.mainTexture = LineMaterials.Band("nhx_bolt_glow", across => (1f - across) * (1f - across));
            return look;
        }

        // Lightning drawn for a staff at `level`, with one bolt from level `countFrom` (or 1), and one more every
        // `levelsPerStrand` levels counted from there, up to `maxStrands`. Each bolt is thin again when one more comes
        internal LightningSize Size(int level, int countFrom, int levelsPerStrand, int maxStrands)
        {
            level = Mathf.Clamp(level, Mathf.Max(1, countFrom), m_maxLevel);
            int strands = Mathf.Min(maxStrands, 1 + (level - countFrom) / levelsPerStrand);
            int lastStrandLevel = Mathf.Max(1, countFrom + (strands - 1) * levelsPerStrand);
            return new LightningSize
            {
                m_strands = strands,
                m_strandSize = m_strandSize + (level - lastStrandLevel) * m_strandSizePerLevel,
                m_glowSize = m_glowSize + (level - 1) * m_glowSizePerLevel,
                m_branches = level * m_branchesPerLevel,
            };
        }
    }

    // What an arc of lightning is made of (LightningArcs.Add): its bolts side by side and their width (of a full bolt), the
    // size of its glow and light, and how many forks it has on average
    internal struct LightningSize
    {
        public int m_strands;
        public float m_strandSize;
        public float m_glowSize;
        public float m_branches;
    }
}
