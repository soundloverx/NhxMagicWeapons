# Renders armor sets as worn, as PNG pictures: every piece's meshes under its attach_skin posed on the player's
# skeleton, rigid helmets on the head joint, and chest and legs textures painted onto the body, over the game's player
# body in plain grey. Made to survey Magic Supremacy's armor (2026-11-09); materials, shading and rasterizing come from
# render_items.py, next to this file.
#
# Needs UnityPy and numpy, run as render_items.py (pip install UnityPy numpy, or into a temporary folder on PYTHONPATH,
# then run without -I). Two steps:
#
#   python Tools\render_armor.py body <the game's c4210710 bundle> <out .npz> [male|female]
#     Takes the player's body from the game's Player prefab (valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710,
#     756 MB: a few minutes and a few GB of memory) and saves it posed, for the renders, with its UVs, its 53 bones
#     and VisEquipment's helmet joint. Once per body is enough.
#
#   python Tools\render_armor.py render Assets\magic_supremacy <out dir> <body .npz or -> <sets>
#     <sets> is "Name=Prefab+Prefab+...;Name=..." with each set's pieces, e.g.
#       Firecaller=ArmorFirecallerChest_DO+ArmorFirecallerLegs_DO+ArmorFirecallerHelmet_DO+ArmorFirecallerCape_DO
#     For each set it writes <Name>_front.png and <Name>_back.png (the whole figure, seen a little from the side),
#     <Name>_head.png (a close-up from the shoulders up), and <Prefab>_icon.png for each piece. A name starting with "solo-"
#     (solo-RingBronze=ArmorInspirationTrinketBlackforest_DO) draws the pieces alone, without the body, from above at an
#     angle: for rings and other small pieces. "-" for the body draws the armor alone everywhere.
#
# How it poses a piece, as the game does when it's worn (VisEquipment): a SkinnedMeshRenderer's vertices go through
# bones, each vertex blending up to 4 of (bone's world matrix × the mesh's bind pose for that bone) by its weights. The
# bones are the player's, taken by index from the body .npz (the game sets the renderer's bones to the body's); without
# a rig (an older .npz, or "-") they're the prefab's own Armature, lined up by its Hips (prefabs put it at 0 or 50 m,
# and some mods leave it out of rest pose: a Vikings_Magic chest lay on its back that way). A MeshRenderer under
# attach_skin is placed by its transform. A helmet without attach_skin is the game's rigid kind: its "attach" child goes
# on the helmet joint. A chest or legs piece's m_armorMaterial _ChestTex / _LegsTex is laid over the body's grey by its
# alpha, in the body's UVs, as the game paints leather, troll or root armor onto the body. attach_skin is inactive in the
# prefab (the game turns it on when the piece is worn), so it's drawn anyway; anything else inactive or disabled isn't.
# The same limits as render_items.py: no normal maps, particles, custom shaders' effects, cloth simulation (capes hang
# as modeled), or anything in other bundles.
import math
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_items as ri  # noqa: E402
from UnityPy.helpers.MeshHelper import MeshHandler  # noqa: E402

SS = ri.SUPERSAMPLE
HIPS = np.array([0.0, 0.91, -0.007])  # where every figure's Hips bone goes (the player prefab's, less its 50 m)
YAW, PITCH = 28, 6  # degrees: the full figures, front and back
# The close-up shows what's above this height (m, the feet at 0, the head bone at 1.67): shoulders and up, however tall
# the helmet (a fraction of the figure showed only Nemesis' horns)
HEAD_YAW, HEAD_PITCH, HEAD_FROM = 22, 4, 1.45
SOLO_YAW, SOLO_PITCH = 35, 30
FIGURE_SIZE, SQUARE_SIZE = (520, 900), (520, 520)  # width, height before trimming
# Dreanegade's Metallic and AlphaHair shaders clip by the main texture's alpha (feathers, frayed hems), which
# render_items.py can't tell from their names
CUTOUT_SHADERS = ("Dreanegade/Metallic", "AlphaHair")
MANNEQUIN = {"name": "mannequin", "shader": "-", "queue": -1, "main": None, "color": np.array([0.34, 0.33, 0.35, 1.0]),
             "emission": np.zeros(3), "emission_map": None, "metallic": 0.0, "smooth": 0.15, "additive": False,
             "transparent": False, "cutout": False, "cutoff": 0.5}


def world_matrix(bundle, transform):
    chain = []
    while transform:
        t = bundle.tree(transform)
        chain.append(t)
        transform = t["m_Father"]["m_PathID"]
    m = np.eye(4)
    for t in reversed(chain):
        m = m @ ri.local_matrix(t)
    return m


def children(bundle, go):
    return [bundle.tree(c["m_PathID"])["m_GameObject"]["m_PathID"] for c in bundle.tree(bundle.transform(go))["m_Children"]]


def find_named(bundle, go, name):
    if bundle.tree(go)["m_Name"] == name:
        return go
    for child in children(bundle, go):
        found = find_named(bundle, child, name)
        if found is not None:
            return found
    return None


def mesh_data(bundle, pid):
    key = ("skinned", pid)
    if key not in bundle.meshes:
        mesh = bundle.objs[pid].read()
        h = MeshHandler(mesh)
        h.process()
        v = np.array(h.m_Vertices, dtype=np.float64)[:, :3]
        n = np.array(h.m_Normals, dtype=np.float64)[:, :3] if h.m_Normals else None
        uv = np.array(h.m_UV0, dtype=np.float64)[:, :2] if h.m_UV0 else np.zeros((len(v), 2))
        tris = [np.array(s, dtype=np.int64).reshape(-1, 3) for s in h.get_triangles()]
        weights = np.array(h.m_BoneWeights, dtype=np.float64) if h.m_BoneWeights else None
        indices = np.array(h.m_BoneIndices, dtype=np.int64) if h.m_BoneIndices else None
        bind = [np.array([[b.e00, b.e01, b.e02, b.e03], [b.e10, b.e11, b.e12, b.e13], [b.e20, b.e21, b.e22, b.e23], [0, 0, 0, 1]])
                for b in (mesh.m_BindPose or [])]
        bundle.meshes[key] = (v, n, uv, tris, weights, indices, bind, mesh.m_Name)
    return bundle.meshes[key]


# A renderer's mesh in the prefab's space: (vertices, unit normals or None, UVs, triangles per submesh, mesh name,
# whether the player's rig posed it). mesh: another mesh to pose on the renderer's bones instead of its own (the female
# body). rig: the player's 53 bone matrices from a body .npz: as the game does when armor is worn
# (VisEquipment.AttachArmor sets the renderer's bones to the body's, by index), a skinned mesh with 53 bind poses is posed
# on those, not on the prefab's own armature (which some mods leave out of rest pose)
def posed_mesh(bundle, renderer, go, mesh=None, rig=None):
    r = bundle.tree(renderer)
    skinned = bundle.objs[renderer].type.name == "SkinnedMeshRenderer"
    if skinned:
        ref = mesh or r["m_Mesh"]
    else:
        filters = [c for c in bundle.components(go) if bundle.objs[c].type.name == "MeshFilter"]
        ref = bundle.tree(filters[0])["m_Mesh"] if filters else None
    if not ref or not ref["m_PathID"] or ref["m_FileID"] != 0 or ref["m_PathID"] not in bundle.objs:
        return None
    v, n, uv, tris, weights, indices, bind, name = mesh_data(bundle, ref["m_PathID"])
    bones = [b["m_PathID"] for b in r["m_Bones"]] if skinned else []
    on_rig = skinned and rig is not None and weights is not None and len(bind) == len(rig)
    if on_rig or (bones and weights is not None and len(bind) == len(bones)):
        if on_rig:
            matrices = np.stack([rig[i] @ bind[i] for i in range(len(bind))])
        else:
            matrices = np.stack([world_matrix(bundle, b) @ bind[i] if b else np.eye(4) for i, b in enumerate(bones)])
        homogeneous = np.c_[v, np.ones(len(v))]
        pv = np.zeros((len(v), 3))
        pn = np.zeros((len(v), 3)) if n is not None else None
        for k in range(indices.shape[1]):
            m = matrices[indices[:, k]]
            pv += weights[:, k:k + 1] * np.einsum("nij,nj->ni", m, homogeneous)[:, :3]
            if n is not None:
                pn += weights[:, k:k + 1] * np.einsum("nij,nj->ni", m[:, :3, :3], n)
    else:
        m = world_matrix(bundle, bundle.transform(go))
        pv = v @ m[:3, :3].T + m[:3, 3]
        pn = n @ np.linalg.inv(m[:3, :3]) if n is not None else None
    if pn is not None:
        pn /= np.linalg.norm(pn, axis=1, keepdims=True) + 1e-12
    return pv, pn, uv, tris, name, on_rig


# How far the Hips bone under top is from HIPS (what to subtract to line a prefab's own armature up), or None
def hips_shift(bundle, top):
    hips = find_named(bundle, top, "Hips")
    if hips is None:
        return None
    return world_matrix(bundle, bundle.transform(hips))[:3, 3] - HIPS


# Moves posed parts so that the Hips bone under top is at HIPS
def line_up(bundle, top, parts):
    shift = hips_shift(bundle, top)
    if shift is None:
        print("  no Hips bone: left where it is")
        return parts
    return [(v - shift,) + tuple(rest) for v, *rest in parts]


# Every drawn submesh of a piece as worn: (vertices, normals, UVs, triangles, material). Skinned pieces go on the
# player's rig if given (else on the prefab's armature, lined up by its Hips); a helmet without attach_skin is the
# game's rigid kind: its "attach" child goes on the rig's helmet joint (VisEquipment.AttachItem keeps its local
# transform under m_helmet).
def collect_worn(bundle, root, rig=None):
    skin = next((c for c in children(bundle, root) if bundle.tree(c)["m_Name"] == "attach_skin"), None)
    rigid = None
    if skin is None:
        attach = next((c for c in children(bundle, root) if bundle.tree(c)["m_Name"] == "attach"), None)
        shared = bundle.item_shared(root)
        if attach is not None and shared and shared["m_itemType"] == 6 and rig is not None and "helmet" in rig:
            rigid = attach
            parent = bundle.tree(bundle.transform(attach))["m_Father"]["m_PathID"]
            to_joint = rig["helmet"] @ np.linalg.inv(world_matrix(bundle, parent))
        else:
            print("  no attach_skin" + (" (a rigid helmet: needs a body .npz with the helmet joint)" if attach is not None else "")
                  + ": not drawn")
            return []
    top = skin if rigid is None else rigid
    shift = hips_shift(bundle, skin) if skin is not None else None
    parts = []

    def walk(go, active):
        g = bundle.tree(go)
        active = active and (g["m_IsActive"] or go == top)
        for c in bundle.components(go):
            if bundle.objs[c].type.name not in ("SkinnedMeshRenderer", "MeshRenderer"):
                continue
            r = bundle.tree(c)
            if not active or not r["m_Enabled"]:
                print(f"  {g['m_Name']}: not drawn (hidden)")
                continue
            posed = posed_mesh(bundle, c, go, rig=rig["bones"] if rig else None)
            if posed is None:
                continue
            v, n, uv, tris, mesh_name, on_rig = posed
            if rigid is not None:
                v = v @ to_joint[:3, :3].T + to_joint[:3, 3]
                if n is not None:
                    n = n @ np.linalg.inv(to_joint[:3, :3])
                    n = n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)
            elif not on_rig and shift is not None:
                v = v - shift
            materials = [m["m_PathID"] for m in r["m_Materials"]]
            for i, sub in enumerate(tris):
                if i < len(materials) and materials[i] and len(sub):
                    mat = dict(bundle.material(materials[i]))
                    if any(s in mat["shader"] for s in CUTOUT_SHADERS):
                        mat["cutout"] = True
                    if mat["main"] is None and "Player" in mat["shader"]:
                        mat["main"] = player_shader_texture(bundle, materials[i])
                    parts.append((v, n, uv, sub, mat))
                    print(f"  {g['m_Name']}/{mesh_name} submesh {i}: {len(sub)} triangles, {mat['name']} ({mat['shader']})"
                          + (", transparent" if mat["transparent"] else "") + (", cutout" if mat["cutout"] else "")
                          + (", glows" if mat["emission"].max() > 0 else ""))
        for child in children(bundle, go):
            walk(child, active)

    walk(top, True)
    return parts


# A mesh drawn with the game's player shader (a leather chest's skirt, in the body's UVs) takes its colors from the
# material's _ChestTex or _LegsTex, not a main texture: the one that isn't the empty player_armor_none
def player_shader_texture(bundle, pid):
    envs = dict(bundle.tree(pid)["m_SavedProperties"]["m_TexEnvs"])
    for key in ("_ChestTex", "_LegsTex"):
        tex = envs.get(key)
        ref = tex["m_Texture"] if tex else None
        if ref and ref["m_FileID"] == 0 and ref["m_PathID"] in bundle.objs and not bundle.objs[ref["m_PathID"]].peek_name().startswith("player_armor_none"):
            image = bundle.texture(ref["m_PathID"])
            if image is not None:
                return image, (1, 1), (0, 0)
    return None


# The textures a chest or legs piece paints onto the body (its m_armorMaterial's _ChestTex or _LegsTex, as
# VisEquipment sets them on the body's material), as RGBA arrays, or {}
def painted_textures(bundle, root):
    shared = bundle.item_shared(root)
    if not shared or shared["m_itemType"] not in (7, 11):
        return {}
    ref = shared.get("m_armorMaterial")
    if not ref or not ref["m_PathID"] or ref["m_FileID"] != 0 or ref["m_PathID"] not in bundle.objs:
        return {}
    key = "_ChestTex" if shared["m_itemType"] == 7 else "_LegsTex"
    tex = dict(bundle.tree(ref["m_PathID"])["m_SavedProperties"]["m_TexEnvs"]).get(key)
    pid = tex["m_Texture"]["m_PathID"] if tex and tex["m_Texture"]["m_FileID"] == 0 else 0
    if not pid or pid not in bundle.objs or bundle.objs[pid].peek_name().startswith("player_armor_none"):
        return {}
    image = bundle.texture(pid)
    if image is None:
        return {}
    print(f"  paints {key} {bundle.objs[pid].peek_name()} on the body")
    return {key: image}


# The body (and its rig, if the .npz has one: "bones", "helmet", "uv"). painted: _ChestTex/_LegsTex arrays laid over
# the grey by their alpha, in the body's UVs
def body_parts(path, painted=None):
    if not path or path == "-":
        return [], None
    d = np.load(path)
    rig = {"bones": d["bones"], "helmet": d["helmet"]} if "bones" in d.files else None
    mat = MANNEQUIN
    uv = d["uv"] if "uv" in d.files else np.zeros((len(d["vertices"]), 2))
    if painted and "uv" in d.files:
        size = 1024
        canvas = np.ones((size, size, 4), dtype=np.float32)
        canvas[:, :, :3] = MANNEQUIN["color"][:3]
        for image in painted.values():
            layer = np.asarray(Image.fromarray((image * 255).astype(np.uint8), "RGBA").resize((size, size), Image.LANCZOS), dtype=np.float32) / 255
            a = layer[:, :, 3:4]
            canvas[:, :, :3] = canvas[:, :, :3] * (1 - a) + layer[:, :, :3] * a
        mat = dict(MANNEQUIN, main=(canvas, (1, 1), (0, 0)), color=np.array([1.0, 1, 1, 1]))
    return [(d["vertices"], d["normals"], uv, d["triangles"], mat)], rig


# Rows: screen right, screen up, depth away from the camera. Characters face +z.
def view_rotation(back, yaw, pitch):
    base = np.eye(3) if back else np.diag([-1.0, 1.0, -1.0])
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    return np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]]) @ np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]) @ base


# crop_from: show only what's above this height (in view space, which a small pitch barely tilts), filling the picture
def render_view(parts, rotation, width, height, crop_from=None, margin=0.05):
    width, height = width * SS, height * SS
    used = np.concatenate([p[0][np.unique(p[3])] for p in parts]) @ rotation.T
    low, high = used.min(0), used.max(0)
    if crop_from is not None:
        low[1] = crop_from
        top = used[used[:, 1] >= low[1]]
        low[0], high[0] = top[:, 0].min(), top[:, 0].max()
    size, mid = high - low, (low + high) / 2
    scale = min(width * (1 - 2 * margin) / max(size[0], 1e-6), height * (1 - 2 * margin) / max(size[1], 1e-6))
    buffers = {"depth": np.full((height, width), np.inf), "color": np.zeros((height, width, 3)),
               "glow": np.zeros((height, width, 3)), "coverage": np.zeros((height, width))}
    to_camera = np.array([0, 0, -1.0])
    later = []
    for v, normals, uv, tris, mat in parts:
        view = v @ rotation.T
        screen = np.column_stack([(view[:, 0] - mid[0]) * scale + width / 2, height / 2 - (view[:, 1] - mid[1]) * scale, view[:, 2] * scale])
        job = (screen, normals @ rotation.T if normals is not None else None, uv, tris, mat)
        if mat["transparent"]:
            later.append(job)
        else:
            ri.raster(*job, buffers, to_camera)
    for job in later:
        ri.raster(*job, buffers, to_camera)

    # As render_items.render: tone map keeping hues, then a bloom from what glows (a tighter one: the figures are big)
    color, coverage = buffers["color"], np.clip(buffers["coverage"], 0, 1)[:, :, None]
    peak = color.max(2, keepdims=True)
    color = color * (1 - np.exp(-1.6 * peak)) / np.maximum(peak, 1e-6)
    r = max(2, int(min(width, height) * 0.012))
    bloom = ri.blur(buffers["glow"], r) * 0.9 + ri.blur(buffers["glow"], r * 3) * 0.6
    peak = bloom.max(2, keepdims=True)
    bloom = bloom * (1 - np.exp(-1.2 * peak)) / np.maximum(peak, 1e-6)
    alpha = np.clip(coverage + bloom.max(2, keepdims=True), 0, 1)
    rgb = ri.linear_to_srgb(np.clip((color * coverage + bloom * (1 - coverage * 0.5)) / np.maximum(alpha, 1e-6), 0, 1))
    image = Image.fromarray((np.dstack([rgb, alpha[:, :, 0]]) * 255).round().astype(np.uint8), "RGBA")
    return image.resize((width // SS, height // SS), Image.LANCZOS)


def render(src, outdir, body_path, sets):
    os.makedirs(outdir, exist_ok=True)
    bundle = ri.Bundle(src)
    _, rig = body_parts(body_path)
    for entry in sets.split(";"):
        name, _, prefabs = entry.partition("=")
        armor, painted = [], {}
        for prefab in prefabs.split("+"):
            root = bundle.find_root(prefab)
            if root is None:
                print(f"{prefab}: no such prefab in {src}")
                continue
            print(prefab)
            armor += collect_worn(bundle, root, rig)
            painted.update(painted_textures(bundle, root))
            shared = bundle.item_shared(root)
            icons = shared["m_icons"] if shared else []
            if icons and icons[0]["m_FileID"] == 0 and icons[0]["m_PathID"] in bundle.objs:
                bundle.objs[icons[0]["m_PathID"]].read().image.save(os.path.join(outdir, prefab + "_icon.png"))
        if not armor and not painted:
            print(f"{name}: nothing to draw")
            continue
        body, _ = body_parts(body_path, painted)
        if name.startswith("solo-"):
            name = name[len("solo-"):]
            rotation = view_rotation(False, math.radians(SOLO_YAW), math.radians(SOLO_PITCH))
            ri.trim(render_view(armor, rotation, *SQUARE_SIZE, margin=0.1)).save(os.path.join(outdir, name + "_front.png"))
        else:
            figure = armor + body
            out = os.path.join(outdir, name)
            yaw, pitch = math.radians(YAW), math.radians(PITCH)
            ri.trim(render_view(figure, view_rotation(False, yaw, pitch), *FIGURE_SIZE)).save(out + "_front.png")
            ri.trim(render_view(figure, view_rotation(True, -yaw, pitch), *FIGURE_SIZE)).save(out + "_back.png")
            head = view_rotation(False, math.radians(HEAD_YAW), math.radians(HEAD_PITCH))
            render_view(figure, head, *SQUARE_SIZE, crop_from=HEAD_FROM, margin=0.06).save(out + "_head.png")
        print(f"{name}: rendered")


# The Player prefab's body, posed and lined up as the armor: the male model it's made with, or the female one, the
# second of its VisEquipment's m_models (the game swaps the body's mesh for it; same bones)
def extract_body(src, out, model="male"):
    bundle = ri.Bundle(src)
    root = None
    for o in bundle.env.objects:
        if o.type.name == "GameObject" and o.peek_name() == "Player":
            transforms = [c for c in bundle.components(o.path_id) if bundle.objs[c].type.name in ("Transform", "RectTransform")]
            if transforms and bundle.tree(transforms[0])["m_Father"]["m_PathID"] == 0:
                root = o.path_id
    if root is None:
        raise SystemExit(f"No Player prefab in {src}")
    body = find_named(bundle, root, "body")
    renderer = next((c for c in bundle.components(body) if bundle.objs[c].type.name == "SkinnedMeshRenderer"), None) if body else None
    if renderer is None:
        raise SystemExit("The Player prefab has no skinned body")
    mesh = None
    if model == "female":
        vis = next((c for c in bundle.components(root) if bundle.objs[c].type.name == "MonoBehaviour" and "m_models" in bundle.tree(c)), None)
        models = bundle.tree(vis)["m_models"] if vis else []
        if len(models) < 2:
            raise SystemExit("The Player prefab's VisEquipment has no second model")
        mesh = models[1]["m_mesh"]
    elif model != "male":
        raise SystemExit(f"Unknown body model {model!r}: male or female")
    posed = posed_mesh(bundle, renderer, body, mesh)
    if posed is None:
        raise SystemExit("The body's mesh isn't in this bundle")
    v, n, uv, tris, mesh_name, _ = posed
    (v, n, _, triangles, _), = line_up(bundle, root, [(v, n, uv, np.concatenate(tris), None)])
    # The rig armor is worn on: the body's bones in its order, and VisEquipment's helmet joint, lined up the same way
    shift = hips_shift(bundle, root)
    bones = []
    for ref in bundle.tree(renderer)["m_Bones"]:
        m = world_matrix(bundle, ref["m_PathID"])
        m[:3, 3] -= shift
        bones.append(m)
    vis = next(c for c in bundle.components(root) if bundle.objs[c].type.name == "MonoBehaviour" and "m_models" in bundle.tree(c))
    helmet = world_matrix(bundle, bundle.tree(vis)["m_helmet"]["m_PathID"])
    helmet[:3, 3] -= shift
    np.savez(out, vertices=v, normals=n, triangles=triangles, uv=uv, bones=np.stack(bones), helmet=helmet)
    print(f"Saved {mesh_name}: {len(v)} vertices, {len(triangles)} triangles, {v.min(0).round(2)} to {v.max(0).round(2)}, "
          f"{len(bones)} bones, helmet joint at {helmet[:3, 3].round(3)}")


if __name__ == "__main__":
    if len(sys.argv) in (4, 5) and sys.argv[1] == "body":
        extract_body(*sys.argv[2:])
    elif len(sys.argv) == 6 and sys.argv[1] == "render":
        render(*sys.argv[2:])
    else:
        raise SystemExit("usage: render_armor.py body <game bundle> <out .npz> [male|female]\n"
                         "       render_armor.py render <bundle> <out dir> <body .npz or -> <Name=Prefab+Prefab;...>")
