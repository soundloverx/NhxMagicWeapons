using System;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // Materials and textures for the lines staves draw their spells with (LineRenderers): particle shaders, which the
    // lines' colors tint, and white textures giving their opacity
    internal static class LineMaterials
    {
        // A Magic Supremacy effect whose material has the additive particle shader (Legacy Shaders/Particles/Additive),
        // which isn't always loaded in the game
        private const string AdditivePrefab = "fx_frostcaller_staff_shard_hit_DO";

        // A copy of the material of a Magic Supremacy effect's child (its sparks...), or one the game always has
        internal static Material Copy(string effect, string child)
        {
            GameObject source = Plugin.Assets.LoadAsset<GameObject>(effect);
            Transform part = source ? source.transform.Find(child) : null;
            Renderer renderer = part ? part.GetComponent<Renderer>() : null;
            if (renderer && renderer.sharedMaterial)
            {
                return new Material(renderer.sharedMaterial);
            }
            Plugin.Log.LogWarning($"{effect} has no {child} material: lines drawn with it look plain.");
            return new Material(Shader.Find("Sprites/Default"));
        }

        // A material adding its color times its alpha to what's behind it, as light (Legacy Shaders/Particles/Additive:
        // 2 × its tint × the line's color × the texture), or a copy of `fallback`
        internal static Material Additive(Material fallback)
        {
            GameObject source = Plugin.Assets.LoadAsset<GameObject>(AdditivePrefab);
            Renderer renderer = source ? source.GetComponent<Renderer>() : null;
            if (renderer && renderer.sharedMaterial)
            {
                Material material = new Material(renderer.sharedMaterial.shader);
                // Neutral: it draws 2 × this × the line's color
                material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                return material;
            }
            Plugin.Log.LogWarning($"{AdditivePrefab} isn't in the asset bundle: glows are drawn like the lines they're on.");
            return new Material(fallback);
        }

        // A band across a line's width, for LineRenderers (stretched along the line, the texture's v runs across it):
        // white, its opacity given from the middle of the line (0) to its edges (1)
        internal static Texture2D Band(string name, Func<float, float> opacity)
        {
            return Texture(name, 1, 64, (along, across) => opacity(across));
        }

        // A white texture `along` × `across` pixels, its opacity given for each point: along the line from 0 to 1 (it
        // repeats along it), and across from its middle (0) to its edges (1)
        internal static Texture2D Texture(string name, int along, int across, Func<float, float, float> opacity)
        {
            Texture2D texture = new Texture2D(along, across, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int x = 0; x < along; x++)
            {
                for (int y = 0; y < across; y++)
                {
                    float acrossPoint = Mathf.Abs((y + 0.5f) / across * 2f - 1f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(opacity((float)x / along, acrossPoint))));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
