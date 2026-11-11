using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // A set's glow (its crystals and eyes, and the right shoulder's rim) is minimal unless its wearer holds the staff it
    // goes with (the user, 2026-11-10: "This armor set goes great with the staff of Muspelheim, while the stormcaller set
    // goes with the staff of storms. [...] Can we keep the glow minimal unless the player is weilding the correct
    // staff?"), and only with the whole set on (the user's friends, 2026-11-11: "the armor set glow when wielding the
    // correct weapon should require the full set being equipped"), fading up as the staff is drawn and down as it's put
    // away or a piece comes off.
    // On a piece's attach_skin, which VisEquipment copies onto whoever wears it, on every game: what's worn and the staff
    // in hand are what VisEquipment shows (m_currentRightItemHash: staves are two-handed, held in the right hand; on the
    // back it doesn't count). An armor stand has a VisEquipment too, so the whole set and the staff on one light it.
    // Dimmed per renderer and material with a MaterialPropertyBlock, as the staves' glow by level (LevelLook)
    internal class ArmorGlow : MonoBehaviour
    {
        // The set's four pieces and the staff (their prefab names' hashes)
        public int[] m_set = new int[0];
        public int m_staff;
        // The glow without it, of the full glow; and how long it takes from one to the other
        public float m_minGlow = 0.2f;
        public float m_fadeTime = 0.5f;

        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_fresnelColor = Shader.PropertyToID("_FresnelColor");

        private struct Glow
        {
            public Renderer m_renderer;
            public int m_slot;
            public int m_property;
            public Color m_color;
        }

        private readonly List<Glow> m_glows = new List<Glow>();
        private VisEquipment m_equipment;
        private MaterialPropertyBlock m_block;
        private float m_glow = -1f;

        private void Awake()
        {
            m_equipment = GetComponentInParent<VisEquipment>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Add(renderer, i, materials[i], s_emissionColor);
                    Add(renderer, i, materials[i], s_fresnelColor);
                }
            }
            if (m_glows.Count == 0)
            {
                enabled = false;
            }
        }

        // The whole set worn and its staff in hand, as this VisEquipment shows them (ArmorCrackle too)
        internal static bool Wielding(VisEquipment equipment, int[] set, int staff)
        {
            if (!equipment || staff == 0 || equipment.m_currentRightItemHash != staff)
            {
                return false;
            }
            foreach (int piece in set)
            {
                if (piece != equipment.m_currentHelmetItemHash && piece != equipment.m_currentChestItemHash
                    && piece != equipment.m_currentLegItemHash && piece != equipment.m_currentShoulderItemHash)
                {
                    return false;
                }
            }
            return true;
        }

        // A material slot's color that glows (black emission, as on most of the set, doesn't)
        private void Add(Renderer renderer, int slot, Material material, int property)
        {
            if (material && material.HasProperty(property))
            {
                Color color = material.GetColor(property);
                if (color.maxColorComponent > 0.5f)
                {
                    m_glows.Add(new Glow { m_renderer = renderer, m_slot = slot, m_property = property, m_color = color });
                }
            }
        }

        private void Update()
        {
            float target = Wielding(m_equipment, m_set, m_staff) ? 1f : m_minGlow;
            // As it is at first; then fading
            float glow = m_glow < 0f ? target : Mathf.MoveTowards(m_glow, target, (1f - m_minGlow) * Time.deltaTime / m_fadeTime);
            if (glow == m_glow)
            {
                return;
            }
            m_glow = glow;
            if (m_block == null)
            {
                m_block = new MaterialPropertyBlock();
            }
            foreach (Glow part in m_glows)
            {
                if (!part.m_renderer)
                {
                    continue;
                }
                part.m_renderer.GetPropertyBlock(m_block, part.m_slot);
                Color color = part.m_color * glow;
                color.a = part.m_color.a;
                m_block.SetColor(part.m_property, color);
                part.m_renderer.SetPropertyBlock(m_block, part.m_slot);
            }
        }
    }
}
