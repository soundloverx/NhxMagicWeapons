# Makes Assets\parasol_closed.bin: the Springbloom Parasol's model closed, for its look on the back (PrefabItems.cs puts it
# in the item's attach_back, which the game shows instead of attach when the weapon is sheathed). The user, 2026-11-28:
# "an umbrella that is always open. Do you see a way of closing it when sheathing the weapon?"
#
#   python -I Tools\fold_parasol.py Assets\nhx_magic_weapons_317 Assets\parasol_closed.bin
#
# Needs UnityPy and numpy (pip install UnityPy numpy). The model (Magic Supremacy's mesh "WindUmbrella", a paper wagasa)
# is one rigid mesh, without bones or blend shapes, and the game can't edit it (it's loaded without its data kept
# readable), so the closed one is made here and loaded as a model part (Staves\ModelParts.cs).
#
# How it folds, in mesh units (the model scales them by 28): the shaft runs along z, the handle below, the canopy a
# shallow cone of 0.0263 (0.74 m) whose ribs meet on the shaft just under the finial (the pivot, PIVOT_Z). As a real one
# closes, each point of the canopy swings down around the pivot: a point that lies a length S along the paper from the
# pivot goes S below it, at the bundle's radius, keeping its angle around the shaft (so the paper gathers into folds).
# What's under the paper (the ribs, the stretchers) goes as far inside the bundle as it was below the paper (DEPTH times
# that), so the paper stays outermost; the stretchers end up inside the shaft. The paper's top is the upper hull of the
# canopy's profile (radius, height): the paper has no thickness (its two sides lie on each other), and its vertices are
# on other rings than the ribs', so the hull is what passes over the ribs. The shaft, the runner and the finial (nearer
# the shaft than FOLD_FROM) don't move; between FOLD_FROM and FOLD_TO a point moves part of the way, so nothing tears.
# Normals and tangents turn with the paper's slope where they are. The closed bundle covers the upper 60% of the shaft;
# it's round (RADIUS_SHAFT + RADIUS_BULGE), narrowing under the finial over its first 30% and a little to its end.
import math
import struct
import sys

import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

MESH = "WindUmbrella"
PIVOT_Z, PIVOT_R = 0.0392, 0.0010
FOLD_FROM, FOLD_TO = 0.0016, 0.0026
SHAFT_TOP = 0.026            # below it: the handle and the shaft, never folded
RADIUS_SHAFT, RADIUS_BULGE = 0.0011, 0.0014
DEPTH = 1.5
MIN_RADIUS = 0.0004


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# The paper's top: the upper hull of the canopy's profile points (radius, height), from the shaft outward
def upper_hull(r, z):
    hull = []
    for p in sorted(set(zip(np.round(r, 7), np.round(z, 7)))):
        while len(hull) >= 2 and (hull[-1][0] - hull[-2][0]) * (p[1] - hull[-2][1]) - (hull[-1][1] - hull[-2][1]) * (p[0] - hull[-2][0]) >= 0:
            hull.pop()
        hull.append(p)
    return np.array([h[0] for h in hull]), np.array([h[1] for h in hull])


# Turns directions (normals, tangents' xyz) at angle `th` around the shaft by `beta` in the plane of the shaft and the
# radius (positive toward up)
def turn(d, th, beta):
    c, s = np.cos(th), np.sin(th)
    radial = d[:, 0] * c + d[:, 1] * s
    around = -d[:, 0] * s + d[:, 1] * c
    cb, sb = np.cos(beta), np.sin(beta)
    radial2 = radial * cb - d[:, 2] * sb
    up2 = radial * sb + d[:, 2] * cb
    return np.stack([radial2 * c - around * s, radial2 * s + around * c, up2], 1)


def fold(v, n, tangents):
    x, y, z = v[:, 0], v[:, 1], v[:, 2]
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    weight = smoothstep(FOLD_FROM, FOLD_TO, r) * (z > SHAFT_TOP)
    canopy = weight > 0.99
    rc, zt = upper_hull(r[canopy], z[canopy])
    # The paper's length from the pivot and its slope (below level), by radius
    rs, zs = np.concatenate([[PIVOT_R], rc]), np.concatenate([[PIVOT_Z], zt])
    length = np.concatenate([[0], np.cumsum(np.hypot(np.diff(rs), np.diff(zs)))])
    slope = np.arctan2(-np.diff(zs), np.diff(rs))
    S = np.interp(r, rs, length)
    phi = np.interp(r, (rs[:-1] + rs[1:]) / 2, slope)
    below = z - np.interp(r, rc, zt)
    u = np.clip(S / length[-1], 0, 1)
    radius = RADIUS_SHAFT + RADIUS_BULGE * smoothstep(0, 0.3, u) * (1 - 0.3 * u)
    rf = np.maximum(radius + DEPTH * below, MIN_RADIUS)
    folded = np.stack([rf * np.cos(th), rf * np.sin(th), PIVOT_Z - S], 1)
    out = v + (folded - v) * weight[:, None]

    # The paper's slope turned to straight down: its upper side faces out
    beta = -(math.pi / 2 - phi)

    def turned(d):
        t = d + (turn(d, th, beta) - d) * weight[:, None]
        return t / (np.linalg.norm(t, axis=1, keepdims=True) + 1e-12)

    out_tangents = tangents.copy()
    out_tangents[:, :3] = turned(tangents[:, :3])
    print(f"Paper: {length[-1]:.4f} long, {rc.max():.4f} wide; {int(canopy.sum())} of {len(v)} vertices folded, "
          f"{int(((weight > 0) & ~canopy).sum())} partly")
    return out, turned(n), out_tangents


def main(src, dst):
    env = UnityPy.load(src)
    meshes = [o for o in env.objects if o.type.name == "Mesh" and o.peek_name() == MESH]
    if len(meshes) != 1:
        raise SystemExit(f"{len(meshes)} meshes named {MESH} in {src}")
    handler = MeshHandler(meshes[0].read())
    handler.process()
    v = np.array(handler.m_Vertices, dtype=np.float64).reshape(-1, 3)
    n = np.array(handler.m_Normals, dtype=np.float64).reshape(-1, 3)
    tangents = np.array(handler.m_Tangents, dtype=np.float64).reshape(-1, 4)
    uv = np.array(handler.m_UV0, dtype=np.float64).reshape(-1, 2)
    subs = [np.array(s, dtype=np.int64).reshape(-1) for s in handler.get_triangles()]
    if len(v) > 65535:
        raise SystemExit("Too many vertices for 16-bit indices")
    v, n, tangents = fold(v, n, tangents)

    # One part, "closed", at the mesh's origin, its submeshes with their own materials (see Tools\extract_staff_parts.py
    # for the layout)
    out = bytearray(b"NHXP") + struct.pack("<ii", 1, 1)
    name = b"closed"
    out += struct.pack("<i", len(name)) + name + struct.pack("<3f", 0, 0, 0) + struct.pack("<i", len(v))
    out += v.astype("<f4").tobytes() + n.astype("<f4").tobytes() + tangents.astype("<f4").tobytes() + uv.astype("<f4").tobytes()
    out += struct.pack("<i", len(subs))
    for s, indices in enumerate(subs):
        out += struct.pack("<ii", s, len(indices)) + indices.astype("<u2").tobytes()
    open(dst, "wb").write(out)
    print(f"Wrote {len(out):,} bytes to {dst}: {len(v)} vertices, submeshes {[len(s) // 3 for s in subs]} triangles")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: fold_parasol.py <bundle with the Springbloom Parasol> <output .bin>")
    main(sys.argv[1], sys.argv[2])
