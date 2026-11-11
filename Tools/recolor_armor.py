# Makes Assets\firecaller_regalia from Assets\magic_supremacy: the Stormcaller set's textures and icons in the Firecaller's
# Regalia's red, which the mod puts on its red copy of the set (Armor\FirecallerColors.cs). Embedded in the DLL.
#
#   python Tools\recolor_armor.py Assets\magic_supremacy Assets\firecaller_regalia
#
# Needs UnityPy and numpy (pip install UnityPy numpy, or into a temporary folder on PYTHONPATH, then run without -I).
#
# The recolour (the user's pick, 2026-11-10, "A" of the mock-ups): the set's blue-violet texels (hues 195-320 degrees,
# easing out by 165 and 345; greys and the warm trims stay as they are) take the hue of Firecaller's red (354 degrees,
# the median of its "Albedo RED" texture), keeping 30% of how far each was from the set's blue (260 degrees), with 35% more
# saturation and each texel's brightness kept (a red as bright as a blue in HSV looks much lighter). The textures: each
# piece's main textures, the paint the robe and greaves put on the body (their m_armorMaterial's _ChestTex and _LegsTex,
# all named PlayerMaterial_Base_color in the bundle, so they're found through the materials), and the icons.
#
# The file (raw deflate, little-endian): "NHXR", version (u16), count (u16), then for each: key (u16 length, UTF-8), kind
# (u8: 0 texture, 1 icon), Unity TextureFormat (i32), width, height (u16), mip count (u8), pivot x, y and pixels per unit
# (f32, icons), data length (u32), data. Textures as the game's GPU takes them: DXT1 or DXT5 (as the originals, which
# are crunched), with their mipmaps, bottom row first; icons RGBA32. A texture's key is its name, a body paint's
# "<material>/<property>", an icon's its sprite's name.
import struct
import sys
import zlib

import numpy as np
import UnityPy
from PIL import Image
from UnityPy.enums import TextureFormat as TF
from UnityPy.export.Texture2DConverter import image_to_texture2d

TEXTURES = ["T_1018303_Equip_01_D", "T_1018303_Equip_02_D", "T_1018303_Equip_03_D", "T_1018303_Equip_04_D",
            "T_1018303_Equip_05_D", "T_1018303_Gem_D"]
PAINTS = [("ArmorStormcallerSkinChestDO", "_ChestTex"), ("ArmorStormcallerSkinLegsDO", "_LegsTex")]
ICONS = ["StormHelmet", "StormChest", "StormLegs", "StormCape"]

BLUE = 260.0       # the set's blue-violet, in degrees
TARGET = 354.0     # Firecaller's red
SPREAD = 0.3       # how much of each texel's distance from the blue stays
SATURATION = 1.35
KEEP_BRIGHTNESS = 1.0


def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rm = nz & (mx == r)
    gm = nz & (mx == g) & ~rm
    bm = nz & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h * 60.0, s, mx


def hsv_to_rgb(h, s, v):
    c = v * s
    hp = (h % 360) / 60.0
    x = c * (1 - np.abs(hp % 2 - 1))
    z = np.zeros_like(h)
    conds = [hp < 1, hp < 2, hp < 3, hp < 4, hp < 5, hp >= 5]
    r = np.select(conds, [c, x, z, z, x, c])
    g = np.select(conds, [x, c, c, x, z, z])
    b = np.select(conds, [z, z, x, c, c, x])
    m = v - c
    return np.stack([r + m, g + m, b + m], -1)


def luma(rgb):
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    return lin @ np.array([0.2126, 0.7152, 0.0722])


# a: RGBA floats 0-1, as decoded (sRGB)
def recolor(a):
    rgb = a[..., :3]
    h, s, v = rgb_to_hsv(rgb)
    w = np.clip((h - 165) / 30, 0, 1) * np.clip((345 - h) / 25, 0, 1)
    w = w * np.clip(s / 0.12, 0, 1)
    mapped = TARGET + (((h - BLUE + 540) % 360) - 180) * SPREAD
    h2 = h + w * ((((mapped - h) + 540) % 360) - 180)
    s2 = np.clip(s * (1 + w * (SATURATION - 1)), 0, 1)
    new = hsv_to_rgb(h2, s2, v)
    l0, l1 = luma(rgb), luma(np.clip(new, 0, 1))
    k = np.where(l1 > 1e-5, l0 / np.maximum(l1, 1e-5), 1.0) ** (1 / 2.4)
    new = np.clip(new * (1 + KEEP_BRIGHTNESS * w[..., None] * (k[..., None] - 1)), 0, 1)
    return np.concatenate([new, a[..., 3:]], -1).astype(np.float32)


def to_image(a):
    return Image.fromarray((np.clip(a, 0, 1) * 255).round().astype(np.uint8), "RGBA")


# The texture with its mipmaps down to 1x1, each halved from the one before, compressed as the game's GPU takes it
def encode_texture(image, fmt, mips):
    data = bytearray()
    level = image
    for i in range(mips):
        encoded, _ = image_to_texture2d(level, fmt)
        data += encoded
        level = level.resize((max(1, level.width // 2), max(1, level.height // 2)), Image.Resampling.BOX)
    return bytes(data)


def entry(key, kind, fmt, width, height, mips, data, pivot=(0.0, 0.0), ppu=0.0):
    name = key.encode("utf-8")
    return (struct.pack("<H", len(name)) + name + struct.pack("<BiHHB3fI", kind, int(fmt), width, height, mips,
                                                                  pivot[0], pivot[1], ppu, len(data)) + data)


def main(src, dst):
    env = UnityPy.load(src)
    objects = {obj.path_id: obj for obj in env.objects}
    by_name = {}
    for obj in env.objects:
        if obj.type.name in ("Texture2D", "Material", "Sprite"):
            by_name.setdefault((obj.type.name, obj.peek_name()), []).append(obj)

    def unique(kind, name):
        found = by_name.get((kind, name), [])
        if len(found) != 1:
            raise SystemExit(f"{len(found)} {kind}s named {name} in the bundle")
        return found[0]

    def texture_format(tex):
        return TF.DXT5 if tex.m_TextureFormat in (TF.DXT5, TF.DXT5Crunched) else TF.DXT1

    entries = []
    sources = [(name, unique("Texture2D", name)) for name in TEXTURES]
    for material, prop in PAINTS:
        envs = dict(unique("Material", material).read_typetree()["m_SavedProperties"]["m_TexEnvs"])
        ref = envs[prop]["m_Texture"]
        if ref["m_FileID"] != 0 or not ref["m_PathID"]:
            raise SystemExit(f"{material} has no {prop} in the bundle")
        sources.append((f"{material}/{prop}", objects[ref["m_PathID"]]))
    for key, obj in sources:
        tex = obj.read()
        original = np.asarray(tex.image.convert("RGBA"), dtype=np.float32) / 255.0
        red = to_image(recolor(original))
        fmt = texture_format(tex)
        data = encode_texture(red, fmt, tex.m_MipCount)
        entries.append(entry(key, 0, fmt, tex.m_Width, tex.m_Height, tex.m_MipCount, data))
        check(key, red, data, fmt, tex.m_Width, tex.m_Height)
        print(f"  {key}: {tex.m_Width}x{tex.m_Height} {fmt.name}, {tex.m_MipCount} mips, {len(data):,} bytes")
    for name in ICONS:
        sprite = unique("Sprite", name).read()
        # Its whole rectangle (64x64): UnityPy's sprite.image crops to what its tight mesh covers
        sheet = sprite.m_RD.texture.read().image.convert("RGBA")
        r = sprite.m_Rect
        icon = sheet.crop((round(r.x), sheet.height - round(r.y + r.height), round(r.x + r.width), sheet.height - round(r.y)))
        red = to_image(recolor(np.asarray(icon, dtype=np.float32) / 255.0))
        data = np.asarray(red)[::-1].tobytes()  # bottom row first, as Unity's textures
        pivot = (float(sprite.m_Pivot.x), float(sprite.m_Pivot.y))
        entries.append(entry(name, 1, TF.RGBA32, red.width, red.height, 1, data, pivot, float(sprite.m_PixelsToUnits)))
        print(f"  {name}: icon {red.width}x{red.height}, pivot {pivot}, {sprite.m_PixelsToUnits} pixels per unit")

    raw = b"NHXR" + struct.pack("<HH", 1, len(entries)) + b"".join(entries)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = packer.compress(raw) + packer.flush()
    open(dst, "wb").write(out)
    print(f"Wrote {len(entries)} entries, {len(raw):,} bytes, {len(out):,} deflated, to {dst}")


# Decodes the top level back and compares it with the recoloured picture: DXT's loss only
def check(key, image, data, fmt, width, height):
    import texture2ddecoder
    size = (width // 4) * (height // 4) * (8 if fmt == TF.DXT1 else 16)
    decode = texture2ddecoder.decode_bc1 if fmt == TF.DXT1 else texture2ddecoder.decode_bc3
    decoded = np.frombuffer(decode(data[:size], width, height), dtype=np.uint8).reshape(height, width, 4)[::-1]
    expected = np.asarray(image, dtype=np.int16)
    # texture2ddecoder decodes to BGRA
    error = np.abs(decoded[..., [2, 1, 0]].astype(np.int16) - expected[..., :3]).mean()
    if error > 6:
        raise SystemExit(f"{key}: the compressed texture is off by {error:.1f} on average")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: recolor_armor.py <magic_supremacy bundle> <output file>")
    main(sys.argv[1], sys.argv[2])
