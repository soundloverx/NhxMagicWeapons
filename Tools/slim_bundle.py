# Makes Assets\nhx_magic_weapons from Assets\magic_supremacy: a copy of Magic Supremacy's bundle with only the prefabs the
# mod loads (KEEP) and what they use, so that the mod ships a few MB instead of the whole bundle (97 MB).
#
#   python -I Tools\slim_bundle.py Assets\magic_supremacy Assets\nhx_magic_weapons
#
# Needs UnityPy (pip install UnityPy). It keeps the prefabs' objects and everything they refer to (meshes, materials,
# textures, shaders, sounds, the audio mixer), found by walking their references and their entries in the bundle's
# preload table, plus every MonoScript (the file's script table refers to them). The texture, mesh and sound data, kept
# outside the objects in the bundle's .resS and .resource files, are copied into smaller ones and their offsets updated.
# The bundle's container lists the kept prefabs only, with their own preload entries. The objects keep their path IDs, so
# nothing else changes; the bundle keeps its internal names (CAB-...), so it still can't be loaded next to Magic
# Supremacy's.
# Add a prefab here when the mod starts loading another from the bundle, and run this again.
import sys

import UnityPy
from UnityPy.streams import EndianBinaryReader

KEEP = [
    "StaffStormcallerShocker_DO",                # the Staff of Storms' model (Thundersurge)
    "fx_stormcaller_staff_shock_DO",             # its buzz, and its sparks' material for the bolt
    "fx_stormcaller_staff_sphere_trap_land_DO",  # the crackle
    "fx_stormcaller_staff_sphere_hit_DO",        # the bolt's hit: a bang and a flash
    "fx_frostcaller_staff_shard_hit_DO",         # its material's additive particle shader, for the bolt's glow
    "StaffFirecallerBlaster_DO",                 # the Wrath of Muspelheim's model (the Incinerator)
    "sfx_firecaller_staff_blaster_shot_DO",      # its beam's shot
    "fx_firecallercaller_staff_blaster_hit_DO",  # its blast, and the embers rising from its head
    "fx_firecaller_staff_thrower_DO",            # its flamethrower's flames
]
ALIGN = 16


def references(value, found):
    if isinstance(value, dict):
        if "m_FileID" in value and "m_PathID" in value:
            if value["m_FileID"] == 0 and value["m_PathID"]:
                found.append(value["m_PathID"])
        else:
            for v in value.values():
                references(v, found)
    elif isinstance(value, list):
        for v in value:
            references(v, found)


# The dictionaries in an object's data that point into a resource file: (dict, offset key, size key, path key)
def resources(value, found):
    if isinstance(value, dict):
        if {"offset", "size", "path"} <= value.keys() and value["path"]:
            found.append((value, "offset", "size", "path"))
        elif {"m_Offset", "m_Size", "m_Source"} <= value.keys() and value["m_Source"]:
            found.append((value, "m_Offset", "m_Size", "m_Source"))
        else:
            for v in value.values():
                resources(v, found)
    elif isinstance(value, list):
        for v in value:
            resources(v, found)


def main(src, dst):
    env = UnityPy.load(src)
    bundle = env.file
    cab_name, cab = next((n, f) for n, f in bundle.files.items() if hasattr(f, "objects"))
    objects = cab.objects
    asset_bundle = next(o for o in objects.values() if o.type.name == "AssetBundle")
    tree = asset_bundle.read_typetree()

    # The prefabs' container entries, and what they use
    entries = []
    for name in KEEP:
        suffix = "/" + name.lower() + ".prefab"
        match = next(((path, info) for path, info in tree["m_Container"] if path.endswith(suffix)), None)
        if not match:
            raise SystemExit(f"{name} isn't in the bundle's container")
        entries.append(match)
    keep = {asset_bundle.path_id} | {o.path_id for o in objects.values() if o.type.name == "MonoScript"}
    stack = []
    for _, info in entries:
        stack.append(info["asset"]["m_PathID"])
        preload = tree["m_PreloadTable"][info["preloadIndex"]:info["preloadIndex"] + info["preloadSize"]]
        stack += [p["m_PathID"] for p in preload if p["m_FileID"] == 0]
    while stack:
        path_id = stack.pop()
        if path_id in keep:
            continue
        if path_id not in objects:
            raise SystemExit(f"Object {path_id} is referenced but not in the bundle")
        keep.add(path_id)
        found = []
        references(objects[path_id].read_typetree(), found)
        stack += [p for p in found if p not in keep]

    # What the new bundle must hold, to check it: each kept object's data as it is, or for those pointing into the resource
    # files, the bytes they point to
    expected_raw = {p: bytes(objects[p].get_raw_data()) for p in keep}
    expected_spans = {}

    # The resource data of what's kept, copied into new files
    new_files = {}
    for path_id in sorted(keep):
        obj = objects[path_id]
        if obj.type.name not in ("Texture2D", "AudioClip", "Mesh", "Cubemap", "Texture2DArray", "Texture3D", "VideoClip"):
            continue
        data = obj.read_typetree()
        spans = []
        resources(data, spans)
        if not spans:
            continue
        for span, offset_key, size_key, path_key in spans:
            file_name = span[path_key].split("/")[-1]
            source = bundle.files[file_name]
            target = new_files.setdefault(file_name, bytearray())
            target += b"\0" * (-len(target) % ALIGN)
            start, size = span[offset_key], span[size_key]
            source.Position = start
            chunk = source.read_bytes(size)
            span[offset_key] = len(target)
            target += chunk
            expected_spans.setdefault(path_id, []).append(bytes(chunk))
        obj.save_typetree(data)
        del expected_raw[path_id]
    del expected_raw[asset_bundle.path_id]
    for file_name, data in new_files.items():
        replaced = EndianBinaryReader(bytes(data))
        replaced.flags = bundle.files[file_name].flags
        bundle.files[file_name] = replaced

    # The container and preload table: the kept prefabs only
    preload = []
    container = []
    for path, info in entries:
        old = tree["m_PreloadTable"][info["preloadIndex"]:info["preloadIndex"] + info["preloadSize"]]
        container.append((path, dict(info, preloadIndex=len(preload), preloadSize=len(old))))
        preload += old
    tree["m_Container"] = container
    tree["m_PreloadTable"] = preload
    asset_bundle.save_typetree(tree)

    total = len(objects)
    for path_id in [p for p in objects if p not in keep]:
        del objects[path_id]
    # The layout of the game's own bundles (flags 0x243): blocks info right after the header, padded, LZ4HC, and the data
    # in 128 KB LZ4HC chunks. UnityPy's "lz4hc" puts the blocks info at the end (0xC3)
    out = bundle.save(packer=(0x243, 3))
    open(dst, "wb").write(out)
    print(f"Kept {len(keep)} objects of {total}; resource files {[(n, len(d)) for n, d in new_files.items()]}; "
          f"wrote {len(out):,} bytes to {dst}")
    check(dst, keep, expected_raw, expected_spans)


# Reads the new bundle back: the same objects, each one's data as it was (or what it points to in the resource files),
# every sound starting with its FMOD header, every texture readable, and the container listing the kept prefabs
def check(path, keep, expected_raw, expected_spans):
    env = UnityPy.load(path)
    bundle = env.file
    objects = next(f for f in bundle.files.values() if hasattr(f, "objects")).objects
    if set(objects) != keep:
        raise SystemExit("The new bundle doesn't hold the kept objects")
    for path_id, obj in objects.items():
        data = obj.read_typetree()
        if path_id in expected_raw and bytes(obj.get_raw_data()) != expected_raw[path_id]:
            raise SystemExit(f"Object {path_id} ({obj.type.name}) changed")
        if path_id in expected_spans:
            spans = []
            resources(data, spans)
            for (span, offset_key, size_key, path_key), expected in zip(spans, expected_spans[path_id]):
                source = bundle.files[span[path_key].split("/")[-1]]
                source.Position = span[offset_key]
                if source.read_bytes(span[size_key]) != expected:
                    raise SystemExit(f"The data of {obj.peek_name()} ({obj.type.name}) changed")
                if obj.type.name == "AudioClip" and expected[:4] != b"FSB5":
                    raise SystemExit(f"The sound {obj.peek_name()} doesn't start with FSB5")
        if obj.type.name == "Texture2D":
            obj.read().image
    names = sorted(path.split("/")[-1] for path, _ in env.container.items())
    if names != sorted(name.lower() + ".prefab" for name in KEEP):
        raise SystemExit(f"Unexpected container: {names}")
    print(f"Checked {len(objects)} objects, {sum(len(s) for s in expected_spans.values())} resource spans; container: {names}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: slim_bundle.py <magic_supremacy bundle> <output bundle>")
    main(sys.argv[1], sys.argv[2])
