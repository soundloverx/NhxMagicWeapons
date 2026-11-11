using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // The Firecaller's Regalia's red: the Stormcaller set's textures and icons recoloured by Tools\recolor_armor.py
    // (embedded in the DLL, read by RecoloredTextures), put on copies of the set's materials, and its blue glows in Firecaller's orange. Only the red
    // pieces get the copies: Stormcaller's Raiment keeps the blue, and so does the Staff of Storms, whose crystals use a
    // material of the same name and look
    internal sealed class FirecallerColors
    {
        private const string Resource = "firecaller_regalia";
        private const string Suffix = "_nhxfire";
        // The crystals' and eyes' glow: Firecaller's eyes' (3, 1.05, 0) twice over, about as bright as the blue (2.55, 2.13,
        // 5.99). The right shoulder's rim: Firecaller's fresnel pieces'
        private static readonly Color Glow = new Color(6f, 2.1f, 0f);
        private static readonly Color Rim = new Color(4.237f, 0.751f, 0f);
        // The paint the robe and greaves put on the body under them (their m_armorMaterial, which VisEquipment reads)
        private static readonly string[] PaintProperties = { "_ChestTex", "_LegsTex" };

        // By the original texture's name, or "<material>/<property>" for a body paint
        private readonly Dictionary<string, Texture2D> m_textures;
        // By the original sprite's name
        private readonly Dictionary<string, Sprite> m_icons;
        // Each original material's red copy (or itself, when nothing of it is blue), so that pieces sharing a material share
        // its copy
        private readonly Dictionary<Material, Material> m_materials = new Dictionary<Material, Material>();

        // Null on a dedicated server, which draws nothing, or when the file is missing or unreadable: the red pieces then
        // look blue
        internal static FirecallerColors Load()
        {
            RecoloredTextures loaded = RecoloredTextures.Load(Resource, Suffix, "the Firecaller's Regalia looks like Stormcaller's Raiment");
            return loaded == null ? null : new FirecallerColors(loaded);
        }

        private FirecallerColors(RecoloredTextures loaded)
        {
            m_textures = loaded.Textures;
            m_icons = loaded.Icons;
        }

        // Makes a piece red: its models' materials (worn, dropped, everything under it), the paint it puts on the body,
        // and its icons
        internal void Apply(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material red = Red(materials[i]);
                    changed |= red != materials[i];
                    materials[i] = red;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
            if (shared.m_armorMaterial)
            {
                shared.m_armorMaterial = RedPaint(shared.m_armorMaterial);
            }
            if (shared.m_icons != null)
            {
                Sprite[] icons = (Sprite[])shared.m_icons.Clone();
                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i] = Icon(icons[i]);
                }
                shared.m_icons = icons;
            }
        }

        // An icon of the set in red, or as it is if it has none
        internal Sprite Icon(Sprite original)
        {
            return original && m_icons.TryGetValue(original.name, out Sprite red) ? red : original;
        }

        private Material Red(Material material)
        {
            if (!material)
            {
                return material;
            }
            if (m_materials.TryGetValue(material, out Material red))
            {
                return red;
            }
            Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Texture2D texture = null;
            bool recolored = main && m_textures.TryGetValue(main.name, out texture);
            bool glow = IsBlue(material, "_EmissionColor");
            bool rim = IsBlue(material, "_FresnelColor");
            red = material;
            if (recolored || glow || rim)
            {
                red = new Material(material) { name = material.name + Suffix };
                if (recolored)
                {
                    red.SetTexture("_MainTex", RecoloredTextures.Matching(texture, main));
                }
                if (glow)
                {
                    red.SetColor("_EmissionColor", Glow);
                }
                if (rim)
                {
                    red.SetColor("_FresnelColor", Rim);
                }
            }
            m_materials[material] = red;
            return red;
        }

        private Material RedPaint(Material material)
        {
            if (m_materials.TryGetValue(material, out Material red))
            {
                return red;
            }
            red = material;
            foreach (string property in PaintProperties)
            {
                if (material.HasProperty(property) && m_textures.TryGetValue(material.name + "/" + property, out Texture2D texture))
                {
                    if (red == material)
                    {
                        red = new Material(material) { name = material.name + Suffix };
                    }
                    red.SetTexture(property, RecoloredTextures.Matching(texture, material.GetTexture(property)));
                }
            }
            m_materials[material] = red;
            return red;
        }

        private static bool IsBlue(Material material, string property)
        {
            if (!material.HasProperty(property))
            {
                return false;
            }
            Color color = material.GetColor(property);
            return color.b > color.r && color.b > color.g && color.b > 0.5f;
        }
    }
}
