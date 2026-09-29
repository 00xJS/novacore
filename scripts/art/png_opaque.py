#!/usr/bin/env python3
"""Rewrite a PNG without its alpha channel, losslessly: every pixel composited
over a background colour (default black) and saved as 8-bit RGB. App Store
Connect rejects an app icon that carries an alpha channel, and headless Chrome
always writes RGBA. (Resizing is sips's job; this only drops the alpha.)

usage: png_opaque.py <in.png> <out.png> [background hex, e.g. 05030E]
"""
import struct
import sys
import zlib


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, idat, info = 8, [], {}
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            w, h, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", chunk)
            assert depth == 8 and interlace == 0, "8-bit, non-interlaced PNGs only"
            assert ctype in (2, 6), "RGB or RGBA PNGs only"
            info = {"w": w, "h": h, "channels": 4 if ctype == 6 else 3}
        elif kind == b"IDAT":
            idat.append(chunk)
        elif kind == b"IEND":
            break
    raw = zlib.decompress(b"".join(idat))
    w, h, n = info["w"], info["h"], info["channels"]
    stride = w * n
    rows, prev = [], bytearray(stride)
    i = 0
    for _ in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        if f == 1:
            for x in range(n, stride):
                line[x] = (line[x] + line[x - n]) & 255
        elif f == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                left = line[x - n] if x >= n else 0
                line[x] = (line[x] + ((left + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x - n] if x >= n else 0
                b = prev[x]
                c = prev[x - n] if x >= n else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if pa <= pb and pa <= pc else b if pb <= pc else c
                line[x] = (line[x] + pred) & 255
        rows.append(line)
        prev = line
    return w, h, n, rows


def write_rgb(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    open(path, "wb").write(png)


def main():
    src, out = sys.argv[1], sys.argv[2]
    bg = sys.argv[3] if len(sys.argv) > 3 else "000000"
    br, bgc, bb = (int(bg[i:i + 2], 16) for i in (0, 2, 4))
    w, h, n, rows = read_png(src)
    rgb_rows = []
    for line in rows:
        out_line = bytearray(w * 3)
        if n == 3:
            out_line[:] = line
        else:
            for x in range(w):
                r, g, b, a = line[x * 4:x * 4 + 4]
                if a == 255:
                    out_line[x * 3:x * 3 + 3] = bytes((r, g, b))
                else:
                    k = a / 255.0
                    out_line[x * 3] = int(round(r * k + br * (1 - k)))
                    out_line[x * 3 + 1] = int(round(g * k + bgc * (1 - k)))
                    out_line[x * 3 + 2] = int(round(b * k + bb * (1 - k)))
        rgb_rows.append(out_line)
    write_rgb(out, w, h, rgb_rows)


if __name__ == "__main__":
    main()
