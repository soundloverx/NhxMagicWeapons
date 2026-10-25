# Makes Assets\staff_of_storms_parts.bin from the repacked bundle: the Staff of Storms' model (Thundersurge's mesh
# "4MainCore") split into its body and the parts that come with the staff's level (Staves\StaffOfStorms\Effects\StaffModel.cs).
#
#   python -I Tools\extract_staff_parts.py Assets\magic_supremacy Assets\staff_of_storms_parts.bin
#
# The game can't edit the mesh: it's loaded without its data kept readable. This reads it from the bundle with UnityPy
# (pip install UnityPy), finds the parts by their place on the staff, and writes each part's own vertices and triangles.
# The parts, in mesh units (the model scales them by 30, so 1 m is 1/30):
# - prong0-3: the 4 prongs of the head, pieces of the textured submesh (2) beyond 0.6 m, reaching out more than 0.1 m:
#   in the order they come, +y, -y (the opposite one), +x, -x
# - spike0-1: the 2 spikes of the butt end, pieces of submesh 2 reaching below -1.3 m (+y, -y), each with its pivot at
#   its root (the end nearest the head), so that it can grow from there
# - crystal_head, crystal_butt: the glowing crystals, the pieces of submesh 0 (it has nothing else) beyond 0 and below
# - body: the rest, submeshes 1 to 4
#
# The file, little endian: "NHXP", version (int32), part count (int32), then for each part: its name (int32 length,
# UTF-8), pivot (3 floats), vertex count (int32), positions (3 floats each), normals (3), tangents (4), UVs (2), submesh
# count (int32), and for each submesh: the original material's index (int32), index count (int32), indices (uint16).
import math
import struct
import sys

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

MESH = "4MainCore"
SCALE = 30.0  # the model's scale: meters per mesh unit
CRYSTALS = 0  # submesh of the crystals' glowing material
TEXTURED = 2  # submesh of the prongs and spikes


def load_mesh(path):
    env = UnityPy.load(path)
    for obj in env.objects:
        if obj.type.name == "Mesh" and obj.peek_name() == MESH:
            mesh = obj.read()
            handler = MeshHandler(mesh)
            handler.process()
            triangles = [[tuple(t) for t in sub] for sub in handler.get_triangles()]
            return handler.m_Vertices, handler.m_Normals, handler.m_Tangents, handler.m_UV0, triangles
    raise SystemExit(f"No mesh {MESH} in {path}")


# Groups of triangles touching each other, by vertex position (vertices are split where their UVs or normals differ)
def pieces(vertices, triangles):
    ids = {}
    welded = [ids.setdefault(tuple(round(c, 6) for c in v), len(ids)) for v in vertices]
    parent = list(range(len(ids)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for sub in triangles:
        for t in sub:
            for a, b in ((t[0], t[1]), (t[1], t[2])):
                ra, rb = find(welded[a]), find(welded[b])
                if ra != rb:
                    parent[ra] = rb
    groups = {}
    for s, sub in enumerate(triangles):
        for i, t in enumerate(sub):
            groups.setdefault(find(welded[t[0]]), []).append((s, i))
    return list(groups.values())


def describe(piece, vertices, triangles):
    points = [vertices[v] for s, i in piece for v in triangles[s][i]]
    zs = [p[2] * SCALE for p in points]
    reach = max(math.hypot(p[0], p[1]) for p in points) * SCALE
    cx = sum(p[0] for p in points) / len(points)
    cy = sum(p[1] for p in points) / len(points)
    return {"subs": {s for s, _ in piece}, "zmin": min(zs), "zmax": max(zs), "reach": reach, "angle": math.degrees(math.atan2(cy, cx))}


def main(src, dst):
    vertices, normals, tangents, uvs, triangles = load_mesh(src)
    found = {"prong": [], "spike": [], "crystal": []}
    taken = set()
    for piece in pieces(vertices, triangles):
        d = describe(piece, vertices, triangles)
        if d["subs"] == {TEXTURED} and d["zmin"] > 0.6 and d["reach"] > 0.1:
            found["prong"].append((d, piece))
        elif d["subs"] == {TEXTURED} and d["zmin"] < -1.3:
            found["spike"].append((d, piece))
        elif d["subs"] == {CRYSTALS}:
            found["crystal"].append((d, piece))
        else:
            continue
        taken.update(piece)
    if len(found["prong"]) != 4 or len(found["spike"]) != 2 or len(found["crystal"]) != 2:
        raise SystemExit(f"Expected 4 prongs, 2 spikes and 2 crystals, found {[(k, len(v)) for k, v in found.items()]}")

    # Prongs in the order they come: +y, -y, +x, -x; spikes +y, -y; crystals head, butt
    order = {90: 0, -90: 1, 0: 2, 180: 3, -180: 3}
    prongs = sorted(found["prong"], key=lambda p: order[round(p[0]["angle"] / 90) * 90])
    spikes = sorted(found["spike"], key=lambda p: -p[0]["angle"])
    crystals = sorted(found["crystal"], key=lambda p: -p[0]["zmax"])

    parts = [("body", [(s, i) for s, sub in enumerate(triangles) for i in range(len(sub)) if (s, i) not in taken], False)]
    parts += [(f"prong{n}", piece, False) for n, (_, piece) in enumerate(prongs)]
    parts += [(f"spike{n}", piece, True) for n, (_, piece) in enumerate(spikes)]
    parts += [("crystal_head", crystals[0][1], False), ("crystal_butt", crystals[1][1], False)]

    out = bytearray(b"NHXP") + struct.pack("<ii", 1, len(parts))
    total = 0
    for name, piece, pivoted in parts:
        used = sorted({v for s, i in piece for v in triangles[s][i]})
        remap = {v: n for n, v in enumerate(used)}
        pivot = (0.0, 0.0, 0.0)
        if pivoted:
            # The root: the middle of its vertices within 1 cm of its end nearest the head
            top = max(vertices[v][2] for v in used)
            root = [vertices[v] for v in used if vertices[v][2] > top - 0.01 / SCALE]
            pivot = tuple(sum(p[a] for p in root) / len(root) for a in range(3))
        encoded = name.encode("utf-8")
        out += struct.pack("<i", len(encoded)) + encoded + struct.pack("<3f", *pivot) + struct.pack("<i", len(used))
        for v in used:
            out += struct.pack("<3f", *(vertices[v][a] - pivot[a] for a in range(3)))
        for v in used:
            out += struct.pack("<3f", *normals[v][:3])
        for v in used:
            out += struct.pack("<4f", *tangents[v][:4])
        for v in used:
            out += struct.pack("<2f", *uvs[v][:2])
        subs = sorted({s for s, _ in piece})
        out += struct.pack("<i", len(subs))
        for s in subs:
            indices = [remap[v] for s2, i in piece if s2 == s for v in triangles[s][i]]
            out += struct.pack("<ii", s, len(indices)) + struct.pack(f"<{len(indices)}H", *indices)
        total += len(piece)
        print(f"{name}: {len(used)} vertices, {len(piece)} triangles, submeshes {subs}, pivot {tuple(round(c * SCALE, 3) for c in pivot)} m")
    if total != sum(len(sub) for sub in triangles):
        raise SystemExit(f"The parts have {total} triangles, the mesh {sum(len(sub) for sub in triangles)}")
    open(dst, "wb").write(out)
    print(f"Wrote {len(out)} bytes to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: extract_staff_parts.py <magic_supremacy bundle> <output .bin>")
    main(sys.argv[1], sys.argv[2])
