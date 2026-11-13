# Makes Assets\legend_cloaks from Assets\magic_supremacy: the cloaks' high-level textures (Armor\ArmorLegendCloak.cs).
# The user, 2026-11-13, after mock-ups of patterns on the whole sets' cloth: "the sets will look too "busy" :) Let's try
# to only apply the idea on the cloak instead of the entire set. Static texture, but with a faint pulse." The cloak has a
# texture of its own (T_1018303_Equip_05_D: violet cloth with painted swirls), so only it changes:
# - Stormcaller's Raiment: storm clouds rolling through the cloth, and thin lightning in them, which glows (level 20)
# - Firecaller's Regalia (its red, as Tools\recolor_armor.py makes it): the cloth a little charred with dark ember veins,
#   which glow orange (level 20)
# Embedded in the DLL.
#
#   python -B Tools\legend_cloaks.py Assets\magic_supremacy Assets\legend_cloaks
#
# Needs UnityPy and numpy (pip install UnityPy numpy, or into a temporary folder on PYTHONPATH, then run without -I).
#
# The patterns are noise in the texture's space, the same every run (seeded): smooth value noise (random grids scaled up
# bicubically) summed in octaves for the clouds, and "ridges" of it (1 - |2n - 1|, thin where the noise crosses its
# middle) for the veins and the lightning. Only on the cloth: texels a little saturated and not bright (all of the cloak
# but its brightest swirl highlights).
#
# The file is Tools\recolor_armor.py's ("NHXR", raw deflate), textures only (kind 0), DXT1 as the original, with its
# mipmaps: "storm/T_1018303_Equip_05_D" and "storm/emission", "fire/T_1018303_Equip_05_D" and "fire/emission". The
# emission maps are colored (the shader multiplies them by _EmissionColor, which the mod sets and pulses).
import os
import struct
import sys
import zlib

import numpy as np
import UnityPy
from PIL import Image
from UnityPy.enums import TextureFormat as TF

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from recolor_armor import check, encode_texture, entry, recolor, rgb_to_hsv, to_image  # noqa: E402

TEXTURE = "T_1018303_Equip_05_D"
SEED = 7
# How much of the pattern shows in the cloth: the user, 2026-11-13, after trying it: "Can we make the pattern about 80%
# of current visibility" (the glow's strength, ArmorLegendCloak's, went down as much)
PATTERN_STRENGTH = 0.8

# Stormcaller's clouds: how much they lighten and darken the cloth, and the storm grey-blue they mix in; the lightning's
# color in the emission map
CLOUD_CONTRAST = 0.6
CLOUD_DARKEN = 0.4
CLOUD_TINT = np.array([0.33, 0.37, 0.55])
LIGHTNING = np.array([0.6, 0.7, 1.0])
# The Firecaller's: how charred the cloth gets, the veins' color in the cloth, and their glow (with a soft halo around them)
CHAR = 0.2
EMBER = np.array([0.55, 0.1, 0.02])
EMBER_GLOW = np.array([1.0, 0.45, 0.1])
HALO = 0.12


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def value_noise(size, cells, rng):
    grid = (rng.random((cells + 1, cells + 1)) * 255).astype(np.uint8)
    return np.asarray(Image.fromarray(grid, "L").resize((size, size), Image.BICUBIC), dtype=np.float32) / 255


def fbm(size, base, octaves, rng):
    total, amp, norm = np.zeros((size, size), np.float32), 1.0, 0.0
    for i in range(octaves):
        total += amp * value_noise(size, base * 2 ** i, rng)
        norm += amp
        amp *= 0.5
    return total / norm


def patterns(size):
    rng = np.random.default_rng(SEED)
    clouds = fbm(size, 4, 5, rng)
    ridge = 1 - np.abs(2 * fbm(size, 5, 5, rng) - 1)
    ridge2 = 1 - np.abs(2 * fbm(size, 9, 4, rng) - 1)
    return {"clouds": clouds, "veins": ss(0.90, 0.975, ridge), "halo": ss(0.75, 0.97, ridge), "fine": ss(0.94, 0.985, ridge2)}


# Where the cloth is (0-1), from the original (blue) texture: a little saturated, not bright, blue to violet
def cloth_mask(a):
    h, s, v = rgb_to_hsv(a[..., :3])
    hue = ss(185, 205, h) * (1 - ss(300, 320, h))
    return (ss(0.08, 0.18, s) * (1 - ss(0.5, 0.65, v)) * hue)[..., None]


def with_alpha(rgb, a):
    return np.concatenate([np.clip(rgb, 0, 1), a[..., 3:]], -1).astype(np.float32)


# The cloth with PATTERN_STRENGTH of the pattern: between the plain cloth and the fully patterned one
def shown(plain, patterned):
    return plain + PATTERN_STRENGTH * (patterned - plain)


# (texture, emission map), RGBA floats
def storm(original, p):
    m = cloth_mask(original)
    rgb = original[..., :3]
    light = np.clip((p["clouds"] - 0.45) * 2.2, -1, 1)[..., None]
    lighter = np.clip(light, 0, 1)
    rgb = rgb * (1 + m * CLOUD_CONTRAST * light) * (1 - m * CLOUD_DARKEN * lighter) + CLOUD_TINT * m * CLOUD_DARKEN * lighter
    fine = (p["fine"] * ss(0.5, 0.65, p["clouds"]))[..., None]
    glow = np.clip(m * fine, 0, 1) * LIGHTNING
    return with_alpha(shown(original[..., :3], rgb), original), with_alpha(glow, np.ones_like(original))


def fire(original, p):
    m = cloth_mask(original)
    red = recolor(original)[..., :3]
    rgb = red
    veins = p["veins"][..., None]
    rgb = rgb * (1 - m * CHAR)
    rgb = rgb * (1 - m * veins) + EMBER * m * veins
    glow = np.clip(m * (veins + HALO * p["halo"][..., None]), 0, 1) * EMBER_GLOW
    return with_alpha(shown(red, rgb), original), with_alpha(glow, np.ones_like(original))


def main(src, dst):
    env = UnityPy.load(src)
    found = [o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() == TEXTURE]
    if len(found) != 1:
        raise SystemExit(f"{len(found)} textures named {TEXTURE} in the bundle")
    tex = found[0].read()
    original = np.asarray(tex.image.convert("RGBA"), dtype=np.float32) / 255.0
    p = patterns(original.shape[0])
    entries = []
    for name, make in (("storm", storm), ("fire", fire)):
        texture, emission = make(original, p)
        for key, picture in ((f"{name}/{TEXTURE}", texture), (f"{name}/emission", emission)):
            image = to_image(picture)
            data = encode_texture(image, TF.DXT1, tex.m_MipCount)
            check(key, image, data, TF.DXT1, tex.m_Width, tex.m_Height)
            entries.append(entry(key, 0, TF.DXT1, tex.m_Width, tex.m_Height, tex.m_MipCount, data))
            print(f"  {key}: {tex.m_Width}x{tex.m_Height} DXT1, {tex.m_MipCount} mips, {len(data):,} bytes")
    raw = b"NHXR" + struct.pack("<HH", 1, len(entries)) + b"".join(entries)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = packer.compress(raw) + packer.flush()
    open(dst, "wb").write(out)
    print(f"Wrote {len(entries)} entries, {len(raw):,} bytes, {len(out):,} deflated, to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: legend_cloaks.py <magic_supremacy bundle> <output file>")
    main(sys.argv[1], sys.argv[2])
