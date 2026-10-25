# Makes Assets\magic_supremacy from Magic Supremacy's DLL (or from a bundle already extracted from it).
#
#   python -I Tools\repack_bundle.py <Magic_Supremacy.dll or bundle> Assets\magic_supremacy
#
# Magic Supremacy embeds its asset bundle in its DLL, compressed as one LZMA block, so Unity has to decompress all of it
# (117 MB for 3.1.5) into memory when it loads. This keeps the bundle's content byte for byte and only repacks its data
# as 128 KB LZ4HC chunks, the layout of the game's own bundles, so that AssetBundle.LoadFromFile reads assets on demand.
#
# Needs the lz4 package (pip install lz4); the rest is Python's standard library.
import lzma
import struct
import sys

import lz4.block

CHUNK = 0x20000  # Unity's chunk size for LZ4 bundles


def read_bundle(path):
    data = open(path, "rb").read()
    start = data.find(b"UnityFS\0")
    if start < 0:
        raise SystemExit(f"No UnityFS bundle in {path}")
    p = start + 8 + 4  # signature, format
    p = data.index(b"\0", p) + 1  # player version
    p = data.index(b"\0", p) + 1  # engine version
    size = struct.unpack(">q", data[p:p+8])[0]
    return data[start:start+size]


def decompress(raw, kind, usize):
    if kind == 0:
        return raw
    if kind == 1:
        # Unity's LZMA blocks: 5 bytes of properties, then a raw LZMA stream
        props, dict_size = raw[0], int.from_bytes(raw[1:5], "little")
        lc, lp, pb = props % 9, props // 9 % 5, props // 45
        filters = [{"id": lzma.FILTER_LZMA1, "dict_size": dict_size, "lc": lc, "lp": lp, "pb": pb}]
        return lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=filters).decompress(raw[5:], max_length=usize)
    if kind in (2, 3):
        return lz4.block.decompress(raw, uncompressed_size=usize)
    raise SystemExit(f"Unknown compression type {kind}")


def align16(n):
    return (n + 15) // 16 * 16


def main(src, dst):
    d = read_bundle(src)

    # Header: signature, format, player and engine versions, then sizes and flags (big endian)
    p = 8
    fmt = struct.unpack(">I", d[p:p+4])[0]
    p += 4
    p = d.index(b"\0", p) + 1
    p = d.index(b"\0", p) + 1
    header = d[:p]
    _, csize, usize, flags = struct.unpack(">qIII", d[p:p+20])
    p += 20
    if not flags & 0x40 or flags & 0x80:
        raise SystemExit(f"Unexpected bundle layout, flags {flags:#x}")
    if fmt >= 7:
        p = align16(p)
    binfo = decompress(d[p:p+csize], flags & 0x3f, usize)
    p += csize
    if flags & 0x200:
        p = align16(p)

    # Blocks info: data hash (16 bytes), block count, blocks (uncompressed size, compressed size, flags), directory
    q = 16
    count = struct.unpack(">i", binfo[q:q+4])[0]
    q += 4
    stream = bytearray()
    for _ in range(count):
        bu, bc, bf = struct.unpack(">IIH", binfo[q:q+10])
        q += 10
        block = decompress(d[p:p+bc], bf & 0x3f, bu)
        if len(block) != bu:
            raise SystemExit("Block size mismatch")
        stream += block
        p += bc
    directory = binfo[q:]

    blocks = []
    packed = bytearray()
    for i in range(0, len(stream), CHUNK):
        chunk = bytes(stream[i:i+CHUNK])
        c = lz4.block.compress(chunk, mode="high_compression", compression=9, store_size=False)
        if len(c) < len(chunk):
            blocks.append((len(chunk), len(c), 3))
            packed += c
        else:
            blocks.append((len(chunk), len(chunk), 0))
            packed += chunk

    new_binfo = bytearray(binfo[:16]) + struct.pack(">i", len(blocks))
    for bu, bc, bf in blocks:
        new_binfo += struct.pack(">IIH", bu, bc, bf)
    new_binfo += directory
    new_binfo_c = lz4.block.compress(bytes(new_binfo), mode="high_compression", compression=9, store_size=False)
    new_flags = (flags & ~0x3f) | 3

    out = bytearray(header)
    size_pos = len(out)
    out += struct.pack(">qIII", 0, len(new_binfo_c), len(new_binfo), new_flags)
    if fmt >= 7:
        out += b"\0" * (align16(len(out)) - len(out))
    out += new_binfo_c
    if new_flags & 0x200:
        out += b"\0" * (align16(len(out)) - len(out))
    out += packed
    out[size_pos:size_pos+8] = struct.pack(">q", len(out))
    open(dst, "wb").write(out)
    print(f"{len(stream)} bytes of data, {count} block(s) in, {len(blocks)} out; wrote {len(out)} bytes to {dst}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__ or "usage: repack_bundle.py <source> <destination>")
    main(sys.argv[1], sys.argv[2])
