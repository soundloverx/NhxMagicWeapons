# Makes Assets\blue_flames from Assets\magic_supremacy: the textures of the Incinerator's flamethrower flames
# (fx_firecaller_staff_thrower_DO) in blue, for the Wrath of Muspelheim's flamethrower with the whole Firecaller's Regalia
# on (Staves\WrathOfMuspelheim\Effects\BlueFlames.cs, which makes the particles' colours and the light blue the same way).
# Embedded in the DLL.
#
#   python Tools\recolor_flames.py Assets\magic_supremacy Assets\blue_flames
#
# Needs UnityPy and numpy (pip install UnityPy numpy, or into a temporary folder on PYTHONPATH, then run without -I).
#
# The recolour: the flames' warm hues (-40 to 80 degrees, easing out by -60 and 100) turn blue as a hotter flame's would,
# yellow (60) to cyan (190), orange (30) to azure (215), red (0) to deep blue (240), keeping each texel's saturation,
# brightness and opacity; whites stay white. Only the two textures with colour in them: the fire's flipbook (white cores,
# orange and red edges) and the embers' glow. The others (the glow, the smoke) are grey, coloured by their particles.
#
# The file is Tools\recolor_armor.py's ("NHXR", raw deflate), with textures only (kind 0), each keyed by its name, DXT5
# as the originals (crunched), with their mipmaps.
import os
import struct
import sys
import zlib

import numpy as np
import UnityPy
from UnityPy.enums import TextureFormat as TF

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from recolor_armor import check, encode_texture, entry, hsv_to_rgb, rgb_to_hsv, to_image  # noqa: E402

PREFAB = "fx_firecaller_staff_thrower_DO"
TEXTURES = ["fire6x6_alternate", "glow_colored"]

# Keep these as BlueFlames.cs's
WARM_FROM = -40.0
WARM_TO = 80.0
WARM_EASE = 20.0
BLUE_AT_RED = 240.0
BLUE_PER_DEGREE = -50.0 / 60.0


# Degrees 0-360 in, degrees out
def blue_hue(h):
    d = ((h + 180.0) % 360.0) - 180.0
    w = np.clip((d - (WARM_FROM - WARM_EASE)) / WARM_EASE, 0, 1) * np.clip(((WARM_TO + WARM_EASE) - d) / WARM_EASE, 0, 1)
    mapped = BLUE_AT_RED + BLUE_PER_DEGREE * d
    return (h + w * ((((mapped - h) + 540.0) % 360.0) - 180.0)) % 360.0


# a: RGBA floats 0-1, as decoded (sRGB)
def recolor(a):
    h, s, v = rgb_to_hsv(a[..., :3])
    new = np.clip(hsv_to_rgb(blue_hue(h), s, v), 0, 1)
    return np.concatenate([new, a[..., 3:]], -1).astype(np.float32)


# The prefab's textures by name, found through its renderers' materials (other prefabs may have their own of a name)
def prefab_textures(env):
    objects = {obj.path_id: obj for obj in env.objects}
    roots = [obj for obj in env.objects if obj.type.name == "GameObject" and obj.peek_name() == PREFAB]
    if len(roots) != 1:
        raise SystemExit(f"{len(roots)} prefabs named {PREFAB} in the bundle")
    found = {}
    pending = [roots[0].read()]
    while pending:
        go = pending.pop()
        for ref in go.m_Component:
            comp = ref.component if hasattr(ref, "component") else ref
            kind = comp.type.name
            if kind == "Transform":
                pending += [child.read().m_GameObject.read() for child in comp.read().m_Children]
            elif kind.endswith("Renderer"):
                for material in comp.read().m_Materials:
                    if not material.m_PathID:
                        continue
                    for _, tex_env in material.read().m_SavedProperties.m_TexEnvs:
                        tex = tex_env.m_Texture
                        if tex.m_FileID == 0 and tex.m_PathID:
                            obj = objects[tex.m_PathID]
                            found[obj.peek_name()] = obj
    return found


def main(src, dst):
    env = UnityPy.load(src)
    textures = prefab_textures(env)
    entries = []
    for name in TEXTURES:
        if name not in textures:
            raise SystemExit(f"{PREFAB} has no texture named {name}")
        tex = textures[name].read()
        if tex.m_TextureFormat not in (TF.DXT5, TF.DXT5Crunched):
            raise SystemExit(f"{name} is {tex.m_TextureFormat.name}, not DXT5")
        original = np.asarray(tex.image.convert("RGBA"), dtype=np.float32) / 255.0
        blue = to_image(recolor(original))
        data = encode_texture(blue, TF.DXT5, tex.m_MipCount)
        entries.append(entry(name, 0, TF.DXT5, tex.m_Width, tex.m_Height, tex.m_MipCount, data))
        check(name, blue, data, TF.DXT5, tex.m_Width, tex.m_Height)
        print(f"  {name}: {tex.m_Width}x{tex.m_Height} DXT5, {tex.m_MipCount} mips, {len(data):,} bytes")

    raw = b"NHXR" + struct.pack("<HH", 1, len(entries)) + b"".join(entries)
    packer = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = packer.compress(raw) + packer.flush()
    open(dst, "wb").write(out)
    print(f"Wrote {len(entries)} entries, {len(raw):,} bytes, {len(out):,} deflated, to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: recolor_flames.py <magic_supremacy bundle> <output file>")
    main(sys.argv[1], sys.argv[2])
