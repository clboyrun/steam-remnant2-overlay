"""Decompress a Remnant 2 .sav file.

Usage:  python decompress_remnant_save.py path\\to\\profile.sav
Writes  profile.sav.bin (raw data) and profile.sav.strings.txt (readable text with offsets).
"""
import re
import struct
import sys
import zlib

path = sys.argv[1]
data = open(path, "rb").read()

# File header (12 bytes): CRC32, total uncompressed size, save version
crc, size, version = struct.unpack_from("<IiI", data, 0)
print(f"CRC32 0x{crc:08X}  size {size}  version {version}")

out = bytearray()
offset = 12
while offset < len(data):
    # Chunk header (49 bytes): tag(8) max_chunk(8) type(1) comp(8) uncomp(8) comp(8) uncomp(8)
    comp_size, uncomp_size = struct.unpack_from("<QQ", data, offset + 17)
    start = offset + 49
    out += zlib.decompress(data[start:start + comp_size])
    print(f"chunk: {comp_size} -> {uncomp_size} bytes")
    offset = start + comp_size

open(path + ".bin", "wb").write(out)
with open(path + ".strings.txt", "w") as f:
    for m in re.finditer(rb"[\x20-\x7e]{6,}", out):
        f.write(f"{m.start():8d}  {m.group().decode()}\n")
print(f"Wrote {path}.bin ({len(out)} bytes) and {path}.strings.txt")
