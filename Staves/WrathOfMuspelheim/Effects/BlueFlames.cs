using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The flamethrower's flames in blue, for a caster wearing the whole Firecaller's Regalia (FlameJet): on a copy of the
    // Incinerator's flames, the particles' colours, the materials' tints and the light turn blue as a hotter flame's, and
    // the two textures with colour in them (the fire's flipbook, white with orange and red edges, and the embers' glow)
    // are replaced by Tools\recolor_flames.py's (embedded in the DLL), which turns their texels blue the same way. The
    // flames' warm hues (-40 to 80 degrees, easing out by -60 and 100) go from yellow (60) to cyan (190), orange (30) to
    // azure (215), red (0) to deep blue (240), keeping their saturation, brightness and opacity; whites stay white. Keep
    // the numbers as recolor_flames.py's
    internal static class BlueFlames
    {
        private const string Resource = "blue_flames";
        private const string Suffix = "_nhxblue";
        private const float WarmFrom = -40f;
        private const float WarmTo = 80f;
        private const float WarmEase = 20f;
        private const float BlueAtRed = 240f;
        private const float BluePerDegree = -50f / 60f;
        private static readonly string[] TintProperties = { "_Color", "_TintColor" };

        // Without the textures (a dedicated server, which draws nothing, or a missing file) only the colours change
        internal static void Apply(GameObject flames)
        {
            RecoloredTextures textures = RecoloredTextures.Load(Resource, Suffix, "the Wrath of Muspelheim's blue flames keep orange edges");
            foreach (ParticleSystem particles in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startColor = Blue(main.startColor);
                ParticleSystem.ColorOverLifetimeModule overLifetime = particles.colorOverLifetime;
                overLifetime.color = Blue(overLifetime.color);
                ParticleSystem.ColorBySpeedModule bySpeed = particles.colorBySpeed;
                bySpeed.color = Blue(bySpeed.color);
                ParticleSystem.TrailModule trails = particles.trails;
                trails.colorOverLifetime = Blue(trails.colorOverLifetime);
                trails.colorOverTrail = Blue(trails.colorOverTrail);
            }
            // The flames' materials are the bundle's, which the orange flames keep: copies, shared where they were
            Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
            foreach (Renderer renderer in flames.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material blue = Blue(materials[i], copies, textures);
                    changed |= blue != materials[i];
                    materials[i] = blue;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
            foreach (Light light in flames.GetComponentsInChildren<Light>(true))
            {
                light.color = Blue(light.color);
            }
        }

        internal static Color Blue(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            float hue = h * 360f;
            float warm = Mathf.Repeat(hue + 180f, 360f) - 180f;
            float weight = Mathf.Clamp01((warm - (WarmFrom - WarmEase)) / WarmEase) * Mathf.Clamp01((WarmTo + WarmEase - warm) / WarmEase);
            float target = BlueAtRed + BluePerDegree * warm;
            Color blue = Color.HSVToRGB(Mathf.Repeat(hue + weight * Mathf.DeltaAngle(hue, target), 360f) / 360f, s, v, true);
            blue.a = color.a;
            return blue;
        }

        // A particle module's colour, whichever way it's given (its setters keep its mode)
        private static ParticleSystem.MinMaxGradient Blue(ParticleSystem.MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    gradient.color = Blue(gradient.color);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    gradient.colorMin = Blue(gradient.colorMin);
                    gradient.colorMax = Blue(gradient.colorMax);
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    gradient.gradient = Blue(gradient.gradient);
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    gradient.gradientMin = Blue(gradient.gradientMin);
                    gradient.gradientMax = Blue(gradient.gradientMax);
                    break;
            }
            return gradient;
        }

        // The module's gradient is a copy of its own, which it takes back when the module is set
        private static Gradient Blue(Gradient gradient)
        {
            if (gradient == null)
            {
                return null;
            }
            GradientColorKey[] keys = gradient.colorKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].color = Blue(keys[i].color);
            }
            gradient.SetKeys(keys, gradient.alphaKeys);
            return gradient;
        }

        private static Material Blue(Material material, Dictionary<Material, Material> copies, RecoloredTextures textures)
        {
            if (!material)
            {
                return material;
            }
            if (copies.TryGetValue(material, out Material blue))
            {
                return blue;
            }
            blue = material;
            Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            if (main && textures != null && textures.Textures.TryGetValue(main.name, out Texture2D texture))
            {
                blue = new Material(material) { name = material.name + Suffix };
                blue.SetTexture("_MainTex", RecoloredTextures.Matching(texture, main));
            }
            foreach (string property in TintProperties)
            {
                if (!material.HasProperty(property))
                {
                    continue;
                }
                Color tint = material.GetColor(property);
                Color blueTint = Blue(tint);
                if (blueTint != tint)
                {
                    if (blue == material)
                    {
                        blue = new Material(material) { name = material.name + Suffix };
                    }
                    blue.SetColor(property, blueTint);
                }
            }
            copies[material] = blue;
            return blue;
        }
    }
}
