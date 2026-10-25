# Renders item prefabs from an asset bundle as PNG pictures: their meshes with the materials' colors, textures and glow,
# so we can see a model without starting the game. Used to survey Magic Supremacy's staves (2026-10-25).
#
#   python Tools\render_items.py Assets\magic_supremacy <out dir> StaffStormcallerShocker_DO,StaffFirecallerRing_DO:face
#
# Needs UnityPy and numpy: pip install UnityPy numpy (or into a temporary folder on PYTHONPATH, then run without -I).
#
# For each prefab it writes, on a transparent background (the glow spills into the alpha):
# - <prefab>_full.png: the whole item, its longest axis up and its widest end (a staff's head) on top, seen from a little
#   to the side and above, trimmed to what's drawn
# - <prefab>_head.png: a close-up of the top third
# - <prefab>_icon.png: the item's inventory icon, if it's an ItemDrop
# A prefab can end in a view mode:
# - ":face" for flat items (thrown blades, a boomerang): looks at its widest side, and writes no close-up
# - ":handle" for items wider than they're long (a parasol): puts up the axis whose length stands apart from the other two
#
# How it draws: an orthographic camera and a z-buffer, rendered at twice the size and scaled down. Per pixel: the main
# texture × color, lit by three fixed lights (diffuse, specular from the smoothness, metals tinted by their color, a
# little environment light), plus the emission (× its emission map), then a tone map that keeps hues and a bloom from
# the emissive surfaces. Transparent materials are blended back to front after the rest. Left out: normal maps,
# occlusion maps, particles, custom shaders' own effects (scrolling, pulsing, fresnel), and anything in other bundles
# (a texture or shader referenced from another file is skipped). Renderers hidden, disabled or past LOD 0 aren't drawn.
import math
import os
import sys

import numpy as np
import UnityPy
from PIL import Image
from UnityPy.helpers.MeshHelper import MeshHandler

SUPERSAMPLE = 2
FULL_SIZE = (300, 1100)  # width, height before trimming
FACE_SIZE = (700, 700)
HEAD_SIZE = (560, 560)
HEAD_CROP = 0.32  # the top fraction of the item in the close-up
YAW, PITCH = 20, 15  # degrees around the item's long axis, then down from level
FACE_YAW, FACE_PITCH = 8, 8
# Directions towards the lights in view space (the camera is at -z looking at +z, y up) and their strengths
LIGHTS = [((-0.5, 0.6, -0.65), 2.2), ((0.75, 0.05, -0.45), 0.6), ((0.2, 0.45, 0.85), 1.1)]


class Bundle:
    def __init__(self, path):
        self.env = UnityPy.load(path)
        self.objs = {o.path_id: o for o in self.env.objects}
        self.trees, self.meshes, self.textures, self.materials = {}, {}, {}, {}

    def tree(self, pid):
        if pid not in self.trees:
            self.trees[pid] = self.objs[pid].read_typetree()
        return self.trees[pid]

    def components(self, go):
        return [c["component"]["m_PathID"] for c in self.tree(go)["m_Component"] if c["component"]["m_PathID"] in self.objs]

    def transform(self, go):
        return next(c for c in self.components(go) if self.objs[c].type.name in ("Transform", "RectTransform"))

    # The prefab's root: a GameObject of that name without a parent
    def find_root(self, name):
        for o in self.env.objects:
            if o.type.name == "GameObject" and o.peek_name() == name:
                if self.tree(self.transform(o.path_id))["m_Father"]["m_PathID"] == 0:
                    return o.path_id
        return None

    # The ItemDrop's shared data, recognized by its fields (the game's bundles keep the script's class in another file)
    def item_shared(self, go):
        for c in self.components(go):
            if self.objs[c].type.name == "MonoBehaviour":
                t = self.tree(c)
                if "m_itemData" in t:
                    return t["m_itemData"]["m_shared"]
        return None

    def mesh(self, pid):
        if pid not in self.meshes:
            mesh = self.objs[pid].read()
            h = MeshHandler(mesh)
            h.process()
            v = np.array(h.m_Vertices, dtype=np.float64)[:, :3]
            n = np.array(h.m_Normals, dtype=np.float64)[:, :3] if h.m_Normals else None
            uv = np.array(h.m_UV0, dtype=np.float64)[:, :2] if h.m_UV0 else np.zeros((len(v), 2))
            tris = [np.array(s, dtype=np.int64).reshape(-1, 3) for s in h.get_triangles()]
            self.meshes[pid] = (v, n, uv, tris, mesh.m_Name)
        return self.meshes[pid]

    def texture(self, pid):
        if pid not in self.textures:
            try:
                self.textures[pid] = np.asarray(self.objs[pid].read().image.convert("RGBA"), dtype=np.float32) / 255.0
            except Exception as e:
                print(f"  texture {pid} can't be decoded: {e}")
                self.textures[pid] = None
        return self.textures[pid]

    def material(self, pid):
        if pid not in self.materials:
            self.materials[pid] = self.read_material(pid)
        return self.materials[pid]

    def read_material(self, pid):
        t = self.tree(pid)
        props = t["m_SavedProperties"]
        texs = dict(props["m_TexEnvs"])
        colors = dict(props["m_Colors"])
        floats = dict(props["m_Floats"])
        shader = "?"
        sh = t["m_Shader"]
        if sh["m_FileID"] == 0 and sh["m_PathID"] in self.objs:
            try:
                shader = self.objs[sh["m_PathID"]].read().m_ParsedForm.m_Name
            except Exception:
                shader = self.objs[sh["m_PathID"]].peek_name() or "?"
        elif sh["m_PathID"]:
            shader = "(in another file)"
        keywords = " ".join(t.get("m_ValidKeywords") or []) + " " + (t.get("m_ShaderKeywords") or "")
        queue = t.get("m_CustomRenderQueue", -1)

        def tex(name):
            e = texs.get(name)
            if not e or not e["m_Texture"]["m_PathID"] or e["m_Texture"]["m_FileID"] != 0:
                return None
            a = self.texture(e["m_Texture"]["m_PathID"])
            if a is None:
                return None
            return a, (e["m_Scale"]["x"], e["m_Scale"]["y"]), (e["m_Offset"]["x"], e["m_Offset"]["y"])

        def color(name, default):
            c = colors.get(name)
            return np.array([c["r"], c["g"], c["b"], c["a"]]) if c else np.array(default, dtype=float)

        lower = shader.lower()
        main = tex("_MainTex") or tex("_BaseMap") or tex("_BaseColorMap")
        base = color("_Color", color("_BaseColor", [1, 1, 1, 1]))
        if "_TintColor" in colors and "particles" in lower:
            base = color("_TintColor", [0.5, 0.5, 0.5, 0.5]) * 2
        emissive = "_EMISSION" in keywords or "emission" in lower
        additive = "additive" in lower or (floats.get("_DstBlend", 0) == 1 and floats.get("_SrcBlend", 1) in (1, 5))
        return {
            "name": t["m_Name"], "shader": shader, "queue": queue, "main": main, "color": base,
            "emission": color("_EmissionColor", [0, 0, 0, 0])[:3] if emissive else np.zeros(3),
            "emission_map": tex("_EmissionMap") if emissive else None,
            "metallic": floats.get("_Metallic", 0.0), "smooth": floats.get("_Glossiness", floats.get("_Smoothness", 0.5)),
            "additive": additive,
            "transparent": queue >= 2900 or additive or floats.get("_Mode", 0) >= 2 or any(w in lower for w in ("particle", "transparent", "fade")),
            "cutout": floats.get("_Mode", 0) == 1 or "cutout" in lower or "_ALPHATEST_ON" in keywords,
            "cutoff": floats.get("_Cutoff", 0.5),
        }


def quaternion_matrix(q):
    x, y, z, w = q["x"], q["y"], q["z"], q["w"]
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def local_matrix(t):
    m = np.eye(4)
    s, p = t["m_LocalScale"], t["m_LocalPosition"]
    m[:3, :3] = quaternion_matrix(t["m_LocalRotation"]) @ np.diag([s["x"], s["y"], s["z"]])
    m[:3, 3] = [p["x"], p["y"], p["z"]]
    return m


# Every drawn submesh of the prefab: (vertices and normals in the root's space, UVs, triangles, material)
def collect(bundle, root):
    lod0, lod_rest, found, parts = set(), set(), [], []

    def walk(go, parent, active):
        g = bundle.tree(go)
        active = active and g["m_IsActive"]
        comps = bundle.components(go)
        tr = bundle.transform(go)
        m = parent @ local_matrix(bundle.tree(tr))
        filters = [c for c in comps if bundle.objs[c].type.name == "MeshFilter"]
        for c in comps:
            kind = bundle.objs[c].type.name
            if kind == "LODGroup":
                for i, lod in enumerate(bundle.tree(c)["m_LODs"]):
                    for r in lod["renderers"]:
                        (lod0 if i == 0 else lod_rest).add(r["renderer"]["m_PathID"])
            elif kind in ("MeshRenderer", "SkinnedMeshRenderer"):
                r = bundle.tree(c)
                mesh = r["m_Mesh"] if kind == "SkinnedMeshRenderer" else (bundle.tree(filters[0])["m_Mesh"] if filters else None)
                if mesh and mesh["m_PathID"] and mesh["m_FileID"] == 0:
                    found.append((c, g["m_Name"], m, mesh["m_PathID"], [x["m_PathID"] for x in r["m_Materials"]], active and r["m_Enabled"]))
        for child in bundle.tree(tr)["m_Children"]:
            walk(bundle.tree(child["m_PathID"])["m_GameObject"]["m_PathID"], m, active)

    walk(root, np.eye(4), True)
    for renderer, name, m, mesh, materials, visible in found:
        if not visible or (renderer in lod_rest and renderer not in lod0):
            print(f"  {name}: not drawn ({'hidden' if not visible else 'past LOD 0'})")
            continue
        v, n, uv, tris, mesh_name = bundle.mesh(mesh)
        vertices = v @ m[:3, :3].T + m[:3, 3]
        normals = None
        if n is not None:
            normals = n @ np.linalg.inv(m[:3, :3])
            normals /= np.linalg.norm(normals, axis=1, keepdims=True) + 1e-12
        for i, sub in enumerate(tris):
            if i < len(materials) and materials[i] and len(sub):
                mat = bundle.material(materials[i])
                parts.append((vertices, normals, uv, sub, mat))
                print(f"  {name}/{mesh_name} submesh {i}: {len(sub)} triangles, {mat['name']} ({mat['shader']})"
                      + (", transparent" if mat["transparent"] else "") + (", glows" if mat["emission"].max() > 0 else ""))
    return parts


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def sample(texture, uv):
    a, (sx, sy), (ox, oy) = texture
    h, w = a.shape[:2]
    x = np.clip((((uv[:, 0] * sx + ox) % 1.0) * w).astype(np.int64), 0, w - 1)
    y = np.clip(((1 - (uv[:, 1] * sy + oy) % 1.0) * h).astype(np.int64), 0, h - 1)  # the image's first row is v = 1
    return a[y, x]


# Linear color, alpha and emission of the given pixels (normals facing the camera)
def shade(mat, normals, uv, to_camera):
    base = mat["color"]
    if mat["main"] is not None:
        t = sample(mat["main"], uv)
        albedo, alpha = t[:, :3] * base[:3], t[:, 3] * base[3]
    else:
        albedo, alpha = np.tile(base[:3], (len(uv), 1)), np.full(len(uv), base[3])
    albedo = srgb_to_linear(np.clip(albedo, 0, 1))
    if mat["transparent"] and "particle" in mat["shader"].lower():
        return albedo, alpha, np.zeros_like(albedo)
    metallic, smooth = mat["metallic"], mat["smooth"]
    diffuse = albedo * (1 - metallic)
    specular = 0.04 * (1 - metallic) + albedo * metallic
    shininess = 2 ** (smooth * 9 + 1)
    fresnel = (1 - np.clip(np.abs(normals @ to_camera), 0, 1)) ** 4
    color = diffuse * 0.32 + specular * (0.45 + 0.55 * fresnel[:, None]) * (0.4 + 0.6 * smooth)  # ambient, environment
    for direction, strength in LIGHTS:
        d = np.array(direction) / np.linalg.norm(direction)
        h = (d + to_camera) / np.linalg.norm(d + to_camera)
        ndl = np.clip(normals @ d, 0, 1)[:, None]
        spec = (np.clip(normals @ h, 0, 1) ** shininess * (shininess + 8) / 25.0 * smooth)[:, None]
        color = color + (diffuse + specular * spec) * ndl * strength
    emission = np.tile(mat["emission"], (len(uv), 1))
    if mat["emission_map"] is not None:
        emission = emission * srgb_to_linear(sample(mat["emission_map"], uv)[:, :3])
    return color, alpha, emission


def render(parts, width, height, yaw, pitch, mode=None, crop=None, margin=0.05):
    width, height = width * SUPERSAMPLE, height * SUPERSAMPLE
    used = np.concatenate([p[0][np.unique(p[3])] for p in parts])
    low, high = used.min(0), used.max(0)
    extent, centre = high - low, (low + high) / 2
    axes = np.eye(3)
    up_axis = int(np.argmax(extent))
    if mode == "handle":
        up_axis = int(np.argmax([min(abs(extent[i] - extent[(i + 1) % 3]), abs(extent[i] - extent[(i + 2) % 3])) for i in range(3)]))
    up = axes[up_axis]
    # The widest end goes on top: a staff's head spreads more than its butt
    along = (used - centre) @ up
    spread = np.linalg.norm((used - centre) - np.outer(along, up), axis=1)
    quarter = extent[up_axis] * 0.25
    if spread[along < -quarter].max(initial=0) > spread[along > quarter].max(initial=0):
        up = -up
    side = np.cross(up, axes[int(np.argmin(extent))]) if mode == "face" else axes[(up_axis + 1) % 3]
    rotation = np.stack([side, up, np.cross(side, up)])  # rows: screen x, screen y (up), depth
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    rotation = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]]) @ np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]) @ rotation

    views = [(p[0] - centre) @ rotation.T for p in parts]
    points = np.concatenate([v[np.unique(p[3])] for v, p in zip(views, parts)])
    low, high = points.min(0), points.max(0)
    if crop:
        low[1] = high[1] - (high[1] - low[1]) * crop
        top = points[points[:, 1] >= low[1]]
        low[0], high[0] = top[:, 0].min(), top[:, 0].max()
    size, mid = high - low, (low + high) / 2
    scale = min(width * (1 - 2 * margin) / max(size[0], 1e-6), height * (1 - 2 * margin) / max(size[1], 1e-6))

    buffers = {"depth": np.full((height, width), np.inf), "color": np.zeros((height, width, 3)),
               "glow": np.zeros((height, width, 3)), "coverage": np.zeros((height, width))}
    to_camera = np.array([0, 0, -1.0])
    later = []
    for v, (_, normals, uv, tris, mat) in zip(views, parts):
        # pixels across and down, depth in pixels too
        screen = np.column_stack([(v[:, 0] - mid[0]) * scale + width / 2, height / 2 - (v[:, 1] - mid[1]) * scale, v[:, 2] * scale])
        job = (screen, normals @ rotation.T if normals is not None else None, uv, tris, mat)
        if mat["transparent"]:
            later.append(job)
        else:
            raster(*job, buffers, to_camera)
    for job in later:
        raster(*job, buffers, to_camera)

    color, coverage = buffers["color"], np.clip(buffers["coverage"], 0, 1)[:, :, None]
    peak = color.max(2, keepdims=True)
    color = color * (1 - np.exp(-1.6 * peak)) / np.maximum(peak, 1e-6)  # tone map, keeping hues
    r = max(2, int(min(width, height) * 0.018))
    bloom = blur(buffers["glow"], r) * 0.9 + blur(buffers["glow"], r * 3) * 0.6
    peak = bloom.max(2, keepdims=True)
    bloom = bloom * (1 - np.exp(-1.2 * peak)) / np.maximum(peak, 1e-6)
    alpha = np.clip(coverage + bloom.max(2, keepdims=True), 0, 1)
    rgb = linear_to_srgb(np.clip((color * coverage + bloom * (1 - coverage * 0.5)) / np.maximum(alpha, 1e-6), 0, 1))
    image = Image.fromarray((np.dstack([rgb, alpha[:, :, 0]]) * 255).round().astype(np.uint8), "RGBA")
    return image.resize((width // SUPERSAMPLE, height // SUPERSAMPLE), Image.LANCZOS)


# Three box blurs, close to a gaussian
def blur(a, r):
    for _ in range(3):
        for axis in (0, 1):
            pad = [(0, 0)] * a.ndim
            pad[axis] = (r + 1, r)
            c = np.pad(np.cumsum(a, axis=axis), pad, mode="edge")
            c = np.moveaxis(c, axis, 0)
            c[: r + 1] = 0
            a = np.moveaxis((c[2 * r + 1:] - c[: -2 * r - 1]) / (2 * r + 1), 0, axis)
    return a


def raster(screen, normals, uv, tris, mat, buffers, to_camera):
    depth, color, glow, coverage = buffers["depth"], buffers["color"], buffers["glow"], buffers["coverage"]
    height, width = depth.shape
    sx, sy, z = screen[:, 0], screen[:, 1], screen[:, 2]
    if mat["transparent"]:
        tris = tris[np.argsort(-z[tris].sum(1))]  # far first
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sx[c] - sx[a]) * (sy[b] - sy[a])
    for i, (ia, ib, ic) in enumerate(tris):
        if abs(area[i]) < 1e-9:
            continue
        x0 = max(int(math.floor(min(sx[ia], sx[ib], sx[ic]))), 0)
        x1 = min(int(math.ceil(max(sx[ia], sx[ib], sx[ic]))), width - 1)
        y0 = max(int(math.floor(min(sy[ia], sy[ib], sy[ic]))), 0)
        y1 = min(int(math.ceil(max(sy[ia], sy[ib], sy[ic]))), height - 1)
        if x0 > x1 or y0 > y1:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w0 = ((sx[ib] - xs) * (sy[ic] - ys) - (sx[ic] - xs) * (sy[ib] - ys)) / area[i]
        w1 = ((sx[ic] - xs) * (sy[ia] - ys) - (sx[ia] - xs) * (sy[ic] - ys)) / area[i]
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue
        py, px = np.nonzero(inside)
        px, py, w0, w1, w2 = px + x0, py + y0, w0[inside], w1[inside], w2[inside]
        zz = w0 * z[ia] + w1 * z[ib] + w2 * z[ic]
        closer = zz < depth[py, px]
        if not closer.any():
            continue
        px, py, zz, w = px[closer], py[closer], zz[closer], np.column_stack([w0[closer], w1[closer], w2[closer]])
        tri_uv = w @ uv[[ia, ib, ic]]
        if normals is not None:
            n = w @ normals[[ia, ib, ic]]
            n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
        else:
            n = np.cross(screen[ib] - screen[ia], screen[ic] - screen[ia]) * [1, -1, 1]  # screen y runs down
            n = np.tile(n / (np.linalg.norm(n) + 1e-12), (len(px), 1))
        n[(n @ to_camera) < 0] *= -1  # two-sided
        col, alpha, emission = shade(mat, n, tri_uv, to_camera)
        if not mat["transparent"]:
            if mat["cutout"]:
                keep = alpha >= mat["cutoff"]
                px, py, zz, col, emission = px[keep], py[keep], zz[keep], col[keep], emission[keep]
            depth[py, px] = zz
            color[py, px] = col + emission
            glow[py, px] = emission
            coverage[py, px] = 1.0
        else:
            al = np.clip(alpha, 0, 1)[:, None]
            if mat["additive"]:
                color[py, px] += (col + emission) * al
                glow[py, px] += (col + emission) * al * 0.5
                coverage[py, px] = np.maximum(coverage[py, px], np.clip(al[:, 0] * 0.6, 0, 1))
            else:
                color[py, px] = color[py, px] * (1 - al) + (col + emission) * al
                coverage[py, px] = coverage[py, px] * (1 - al[:, 0]) + al[:, 0]


# Crops the transparent margins, keeping 4% of the picture's size around what's drawn
def trim(image):
    box = image.getchannel("A").point(lambda v: 255 if v > 10 else 0).getbbox()
    if not box:
        return image
    l, t, r, b = box
    pad = int(max(r - l, b - t) * 0.04)
    return image.crop((max(0, l - pad), max(0, t - pad), min(image.width, r + pad), min(image.height, b + pad)))


def main(src, outdir, prefabs):
    os.makedirs(outdir, exist_ok=True)
    bundle = Bundle(src)
    for entry in prefabs.split(","):
        name, _, mode = entry.partition(":")
        if mode not in ("", "face", "handle"):
            raise SystemExit(f"Unknown view mode {mode!r} for {name}: use face or handle")
        root = bundle.find_root(name)
        if root is None:
            print(f"{name}: no such prefab in {src}")
            continue
        print(name)
        parts = collect(bundle, root)
        if not parts:
            print("  nothing to draw")
            continue
        out = os.path.join(outdir, name)
        if mode == "face":
            trim(render(parts, *FACE_SIZE, math.radians(FACE_YAW), math.radians(FACE_PITCH), mode, margin=0.06)).save(out + "_full.png")
        else:
            yaw, pitch = math.radians(YAW), math.radians(PITCH)
            trim(render(parts, *FULL_SIZE, yaw, pitch, mode, margin=0.04)).save(out + "_full.png")
            render(parts, *HEAD_SIZE, yaw, pitch, mode, crop=HEAD_CROP, margin=0.07).save(out + "_head.png")
        shared = bundle.item_shared(root)
        icons = shared["m_icons"] if shared else []
        if icons and icons[0]["m_FileID"] == 0 and icons[0]["m_PathID"] in bundle.objs:
            bundle.objs[icons[0]["m_PathID"]].read().image.save(out + "_icon.png")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("usage: render_items.py <bundle> <out dir> <prefab>[:face|:handle][,<prefab>...]")
    main(*sys.argv[1:])
