using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // How the staff's fire beam is drawn (FireBeams): its materials and colors, and its size by the staff's level. A
    // component on an object of its own, which the beam's prefab refers to
    internal class BeamLook : MonoBehaviour
    {
        // The beams' core: alpha blended, with flames flowing along it (a texture repeating along the line), its color
        // half again as bright as given (hot, nearly white, and more opaque). Around them, a sheath of fire, alpha
        // blended too, softer and more ragged, which gives the ray its body in daylight; and a glow, added to what's
        // behind it, as light. With the core alone and the glow (until 2026-10-25), the user found the beam "too dim"
        public Material m_coreMaterial;
        public Material m_sheathMaterial;
        public Material m_glowMaterial;
        public Color m_coreColor = new Color(1f, 0.86f, 0.55f, 1f);
        public Color m_sheathColor = new Color(1f, 0.42f, 0.08f, 0.5f);
        public Color m_glowColor = new Color(1f, 0.38f, 0.06f, 0.9f);
        public Color m_lightColor = new Color(1f, 0.55f, 0.2f);

        // Its size by level (Size); higher levels look like m_maxLevel. The beams side by side are m_strandSize wide (of a
        // full beam) at the level the last one came, and m_strandSizePerLevel more at each level after, up to
        // m_maxStrandSize. The sheath and the glow around them and the light where they end are m_glowSize big at level 1,
        // and m_glowSizePerLevel more at each level
        public int m_maxLevel = 20;
        public float m_strandSize = 0.35f;
        public float m_strandSizePerLevel = 0.075f;
        public float m_maxStrandSize = 0.8f;
        public float m_glowSize = 0.37f;
        public float m_glowSizePerLevel = 0.045f;

        // On an object of its own under `container`, drawn with the material of `materialEffect`'s `materialChild` (a
        // Magic Supremacy effect with Legacy Shaders/Particles/Alpha Blended)
        internal static BeamLook Create(Transform container, string name, string materialEffect, string materialChild)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(container, false);
            BeamLook look = go.AddComponent<BeamLook>();
            // The shader draws 2 × this × the line's color: 0.5 is neutral
            look.m_coreMaterial = LineMaterials.Copy(materialEffect, materialChild);
            look.m_coreMaterial.SetColor("_TintColor", new Color(0.75f, 0.75f, 0.75f, 0.75f));
            look.m_coreMaterial.mainTexture = LineMaterials.Texture("nhx_beam_core", 64, 32, Core);
            look.m_sheathMaterial = LineMaterials.Copy(materialEffect, materialChild);
            look.m_sheathMaterial.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            look.m_sheathMaterial.mainTexture = LineMaterials.Texture("nhx_beam_sheath", 64, 32, Sheath);
            look.m_glowMaterial = LineMaterials.Additive(look.m_coreMaterial);
            look.m_glowMaterial.mainTexture = LineMaterials.Band("nhx_beam_glow", across => (1f - across) * (1f - across));
            return look;
        }

        // Tongues of flame along the line: brighter and dimmer streaks that lean across it, so that they seem to lick
        // outward as the texture flows (0 to 1)
        private static float Wave(float along, float across)
        {
            return 0.5f + 0.25f * Mathf.Sin(2f * Mathf.PI * (along * 3f + across * 0.8f) + 1f)
                + 0.25f * Mathf.Sin(2f * Mathf.PI * (along * 7f - across * 1.3f) + 2.3f);
        }

        // Solid in the middle, soft at the edges, the flames showing a little, mostly toward the edges
        private static float Core(float along, float across)
        {
            float wave = Wave(along, across);
            return (1f - across * across * across * across) * Mathf.Lerp(0.8f, 1f, wave) * Mathf.Lerp(1f, wave, across);
        }

        // Fading from the middle out, the flames showing a lot
        private static float Sheath(float along, float across)
        {
            return Mathf.Pow(1f - across, 1.5f) * Mathf.Lerp(0.45f, 1f, Wave(along, across));
        }

        // The beam for a staff at `level`: one from level 1, and one more every `levelsPerStrand` levels, up to
        // `maxStrands`. Each is thin again when one more comes
        internal BeamSize Size(int level, int levelsPerStrand, int maxStrands)
        {
            level = Mathf.Clamp(level, 1, m_maxLevel);
            int strands = Mathf.Min(maxStrands, 1 + level / levelsPerStrand);
            int lastStrandLevel = Mathf.Max(1, (strands - 1) * levelsPerStrand);
            return new BeamSize
            {
                m_strands = strands,
                m_strandSize = Mathf.Min(m_maxStrandSize, m_strandSize + (level - lastStrandLevel) * m_strandSizePerLevel),
                m_glowSize = m_glowSize + (level - 1) * m_glowSizePerLevel,
            };
        }
    }

    // What a beam is made of: its strands side by side and their width (of a full beam), and the size of its glow and light
    internal struct BeamSize
    {
        public int m_strands;
        public float m_strandSize;
        public float m_glowSize;
    }
}
