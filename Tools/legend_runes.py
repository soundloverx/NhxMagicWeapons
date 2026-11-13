# Makes Assets\legend_runes from Assets\magic_supremacy: the runes' glow maps (Armor\ArmorLegendRunes.cs). The user,
# 2026-11-13, after mock-up renders: "Let's also try the runes"; then "Can we keep runes for the Firecaller set only?",
# so only the Firecaller's Regalia's maps are made (SETS; "storm" makes Stormcaller's Raiment's too). Light spreads
# over the sets' details as they level up: the modeled gems (the hood's forehead gem, the gems on the robe, shoulders
# and belt: T_1018303_Gem_D), the painted magenta gems on the metal (T_1018303_Equip_03_D, hues 270-310 degrees; its
# lavender cloth wraps are bluer, 240-255), and fainter, the violet bands on the cloth (T_1018303_Equip_01_D, hues
# 256-280, well saturated; the dark cloth isn't). Each
# glows in its own hue leaning halfway to the set's glow: Stormcaller's blue-violet, the Firecaller's orange (its hues
# taken from its red textures, as Tools\recolor_armor.py makes them). The cloak isn't here: it has its own look
# (Tools\legend_cloaks.py), which takes its only emission map.
# Embedded in the DLL.
#
#   python -B Tools\legend_runes.py Assets\magic_supremacy Assets\legend_runes
#
# Needs UnityPy and numpy (pip install UnityPy numpy, or into a temporary folder on PYTHONPATH, then run without -I).
#
# The file is Tools\recolor_armor.py's ("NHXR", raw deflate), textures only (kind 0), DXT1, with mipmaps as the
# originals: "fire/<texture>" (and "storm/<texture>" if asked) for each of the three textures. The maps are colored;
# the shader multiplies them by _EmissionColor, which the mod sets per piece as it reaches its level.
import os
import struct
import sys
import zlib

import numpy as np
import UnityPy
from UnityPy.enums import TextureFormat as TF

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from recolor_armor import check, encode_texture, entry, recolor, rgb_to_hsv, to_image  # noqa: E402

GEM, CLOTH, METAL = "T_1018303_Gem_D", "T_1018303_Equip_01_D", "T_1018303_Equip_03_D"
# The sets' glows the runes lean toward (the crystals' colors, brightest component 1)
SET_GLOW = {"storm": np.array([0.42, 0.36, 1.0]), "fire": np.array([1.0, 0.35, 0.0])}
SETS = ["fire"]
LEAN = 0.5
# The bands glow this much of the gems' strength
BANDS = 0.75


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def band(h, lo, hi, soft=8.0):
    return ss(lo - soft, lo, h) * (1 - ss(hi, hi + soft, h))


# How much each texel lights up (0-1), from the original (blue) texture
def mask(name, a):
    h, s, v = rgb_to_hsv(a[..., :3])
    if name == GEM:
        return np.clip(0.35 + 0.65 * v, 0, 1)
    if name == METAL:
        return band(h, 270, 310) * ss(0.45, 0.6, s) * ss(0.25, 0.45, v)
    return band(h, 256, 280) * ss(0.45, 0.6, s) * ss(0.2, 0.3, v) * BANDS


# The glow's color: the texel's own hue (as the set shows it), leaning toward the set's glow
def glow(m, shown, set_glow):
    rgb = shown[..., :3] / np.maximum(shown[..., :3].max(-1, keepdims=True), 1e-3)
    color = (1 - LEAN) * rgb + LEAN * set_glow
    out = np.concatenate([np.clip(m[..., None] * color, 0, 1), np.ones(m.shape + (1,))], -1)
    return out.astype(np.float32)


def main(src, dst):
    env = UnityPy.load(src)
    entries = []
    for name in (GEM, CLOTH, METAL):
        found = [o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() == name]
        if len(found) != 1:
            raise SystemExit(f"{len(found)} textures named {name} in the bundle")
        tex = found[0].read()
        original = np.asarray(tex.image.convert("RGBA"), dtype=np.float32) / 255.0
        m = mask(name, original)
        for set_name in SETS:
            shown = recolor(original) if set_name == "fire" else original
            image = to_image(glow(m, shown, SET_GLOW[set_name]))
            data = encode_texture(image, TF.DXT1, tex.m_MipCount)
            key = f"{set_name}/{name}"
            check(key, image, data, TF.DXT1, tex.m_Width, tex.m_Height)
            entries.append(entry(key, 0, TF.DXT1, tex.m_Width, tex.m_Height, tex.m_MipCount, data))
            print(f"  {key}: {tex.m_Width}x{tex.m_Height} DXT1, {tex.m_MipCount} mips, lit {m.mean() * 100:.1f}%")
    raw = b"NHXR" + struct.pack("<HH", 1, len(entries)) + b"".join(entries)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = packer.compress(raw) + packer.flush()
    open(dst, "wb").write(out)
    print(f"Wrote {len(entries)} entries, {len(raw):,} bytes, {len(out):,} deflated, to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: legend_runes.py <magic_supremacy bundle> <output file>")
    main(sys.argv[1], sys.argv[2])
