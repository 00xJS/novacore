#!/usr/bin/env python3
"""The Wilds' art (2026-09-29): what a survey finds, standing on its sector of
the globe — a crystal field, a helium vent, a gold seam, a supply cache and a
relic — plus the survey beacon on a sector being charted and the harvester
drone that flies the deposits. Isometric, lit, transparent SVGs (256 x 256; the
drone 128 x 128) in the colours of the Neon Hologram style.

render_wilds.sh runs this and renders them to the game's
GalaxyRoyale/Assets/Resources/Wilds/<Name>.png (WildsView loads them).

usage: wilds_art.py <out dir> [name ...]
"""
import math
import os
import random
import sys

OUT = sys.argv[1]
ONLY = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def pts(p):
    return " ".join(f"{f(x)},{f(y)}" for x, y in p)


def doc(w, h, defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<defs>{defs}</defs>{body}</svg>')


COMMON = """
<filter id="glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="6"/></filter>
<filter id="glow2" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="14"/></filter>
<filter id="soft" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="2.2"/></filter>
<filter id="shade" x="-20%" y="-20%" width="140%" height="140%" color-interpolation-filters="sRGB">
 <feTurbulence type="fractalNoise" baseFrequency="1.1" numOctaves="2" seed="4" result="n"/>
 <feColorMatrix in="n" type="saturate" values="0" result="g"/>
 <feComposite in="g" in2="SourceAlpha" operator="in" result="gi"/>
 <feComposite in="SourceGraphic" in2="gi" operator="arithmetic" k1="0" k2="1" k3=".09" k4="-.045"/>
</filter>
<linearGradient id="rockTop" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#6E6792"/><stop offset=".55" stop-color="#4A4468"/><stop offset="1" stop-color="#383252"/></linearGradient>
<linearGradient id="rockSide" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2E2946"/><stop offset="1" stop-color="#17142A"/></linearGradient>
<linearGradient id="steelTop" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#8D9AB8"/><stop offset="1" stop-color="#4E5A75"/></linearGradient>
<linearGradient id="steelL" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#5B6782"/><stop offset="1" stop-color="#46516A"/></linearGradient>
<linearGradient id="steelR" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#343C51"/><stop offset="1" stop-color="#262C3C"/></linearGradient>
"""


def plate(cx, cy, rx, ry, seed, depth=16, pebbles=7):
    """An irregular slab of alien rock: a lit top and a dark rim below it."""
    rnd = random.Random(seed)
    n = 11
    top = []
    for i in range(n):
        a = 2 * math.pi * i / n
        k = 1 + rnd.uniform(-0.1, 0.08)
        top.append((cx + math.cos(a) * rx * k, cy + math.sin(a) * ry * k))
    below = [(x, y + depth) for x, y in top]
    # The rim: the lower half of the outline pushed down.
    lower = sorted(range(n), key=lambda i: top[i][0])
    rim = [p for p in top if p[1] >= cy - ry * 0.25]
    rim.sort(key=lambda p: p[0])
    band = rim + [(x, y + depth) for x, y in reversed(rim)]
    out = [f'<ellipse cx="{f(cx)}" cy="{f(cy + depth + 4)}" rx="{f(rx * 1.02)}" ry="{f(ry * 0.9)}" fill="#000" opacity=".45" filter="url(#soft)"/>',
           f'<polygon points="{pts(band)}" fill="url(#rockSide)" filter="url(#shade)"/>',
           f'<polygon points="{pts(top)}" fill="url(#rockTop)" filter="url(#shade)"/>',
           f'<polyline points="{pts([p for p in top if p[1] < cy])}" fill="none" stroke="#9A93C0" stroke-width="1.2" opacity=".55"/>']
    for _ in range(pebbles):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(0.35, 0.85)
        px, py = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
        s = rnd.uniform(2.5, 5.5)
        out.append(f'<ellipse cx="{f(px)}" cy="{f(py)}" rx="{f(s)}" ry="{f(s * 0.6)}" fill="#2A2540"/>'
                   f'<ellipse cx="{f(px - s * 0.2)}" cy="{f(py - s * 0.2)}" rx="{f(s * 0.6)}" ry="{f(s * 0.35)}" fill="#7A74A0" opacity=".6"/>')
    return "".join(out)


def sparkle(x, y, s, color="#FFFFFF", opacity=1.0):
    return (f'<path d="M{f(x)} {f(y - s)} L{f(x + s * 0.22)} {f(y - s * 0.22)} L{f(x + s)} {f(y)} L{f(x + s * 0.22)} {f(y + s * 0.22)} '
            f'L{f(x)} {f(y + s)} L{f(x - s * 0.22)} {f(y + s * 0.22)} L{f(x - s)} {f(y)} L{f(x - s * 0.22)} {f(y - s * 0.22)}Z" '
            f'fill="{color}" opacity="{opacity}"/>')


# ---------------------------------------------------------------- crystal field

def crystal(x, y, h, w, lean, light, mid, dark, seed):
    """A hexagonal crystal column seen from the side and above: two lit faces and a pointed tip."""
    tip = (x + lean, y - h)
    shoulder = h * 0.78
    l0, r0 = (x - w, y), (x + w, y)
    l1, r1 = (x - w + lean * 0.78, y - shoulder), (x + w + lean * 0.78, y - shoulder)
    c0, c1 = (x + w * 0.1, y + w * 0.35), (x + w * 0.1 + lean * 0.78, y - shoulder + w * 0.35)
    left = [l0, c0, c1, l1]
    right = [c0, r0, r1, c1]
    cap_l = [l1, c1, tip]
    cap_r = [c1, r1, tip]
    return (f'<polygon points="{pts(left)}" fill="{mid}"/>'
            f'<polygon points="{pts(right)}" fill="{dark}"/>'
            f'<polygon points="{pts(cap_l)}" fill="{light}"/>'
            f'<polygon points="{pts(cap_r)}" fill="{mid}"/>'
            f'<polyline points="{pts([l0, l1, tip])}" fill="none" stroke="#FFFFFF" stroke-width="1.1" opacity=".75"/>'
            f'<polyline points="{pts([c0, c1, tip])}" fill="none" stroke="#E8FFFF" stroke-width=".9" opacity=".6"/>')


def crystal_field():
    defs = COMMON + """
<linearGradient id="cL" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#C9FDFF"/><stop offset="1" stop-color="#3DD8EE"/></linearGradient>
<linearGradient id="cM" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6FF3FF"/><stop offset="1" stop-color="#1596B8"/></linearGradient>
<linearGradient id="cD" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1FA7C7"/><stop offset="1" stop-color="#08445E"/></linearGradient>
"""
    body = [plate(128, 196, 92, 36, "crystal")]
    body.append('<ellipse cx="128" cy="150" rx="70" ry="62" fill="#3DF5FF" opacity=".38" filter="url(#glow2)"/>')
    cols = [(104, 198, 72, 11, -10), (150, 200, 64, 10, 12), (128, 192, 112, 15, 3), (86, 206, 44, 8, -14),
            (170, 206, 40, 8, 14), (116, 212, 30, 7, -4), (142, 212, 34, 7, 6)]
    for i, (x, y, h, w, lean) in enumerate(cols):
        body.append(crystal(x, y, h, w, lean, "url(#cL)", "url(#cM)", "url(#cD)", i))
    for x, y, s in ((126, 76, 7), (98, 124, 4.5), (166, 140, 5), (143, 104, 3.5)):
        body.append(sparkle(x, y, s))
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- helium vent

def helium_vent():
    defs = COMMON + """
<linearGradient id="plume" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stop-color="#FFFFFF" stop-opacity=".95"/><stop offset=".25" stop-color="#7DFFC6" stop-opacity=".85"/><stop offset=".75" stop-color="#3DFFA0" stop-opacity=".35"/><stop offset="1" stop-color="#3DFFA0" stop-opacity="0"/></linearGradient>
<radialGradient id="mouth"><stop offset="0" stop-color="#E9FFF4"/><stop offset=".4" stop-color="#3DFFA0"/><stop offset="1" stop-color="#0C3B2A"/></radialGradient>
"""
    body = [plate(128, 198, 92, 34, "vent", pebbles=5)]
    # The crater: a raised rim with the glowing mouth inside.
    body.append('<ellipse cx="128" cy="188" rx="46" ry="17" fill="#2B2642"/>'
                '<ellipse cx="128" cy="184" rx="46" ry="17" fill="url(#rockTop)" filter="url(#shade)"/>'
                '<ellipse cx="128" cy="185" rx="33" ry="11" fill="url(#mouth)"/>'
                '<ellipse cx="128" cy="185" rx="33" ry="11" fill="none" stroke="#0E2C22" stroke-width="3"/>')
    body.append('<ellipse cx="128" cy="120" rx="44" ry="80" fill="#3DFFA0" opacity=".33" filter="url(#glow2)"/>')
    body.append('<path d="M104 186 C96 150 108 110 100 62 C112 84 120 40 128 18 C136 44 146 80 158 60 C150 108 162 150 152 186Z" '
                'fill="url(#plume)" filter="url(#soft)"/>')
    body.append('<path d="M116 186 C112 150 122 118 118 80 C126 96 130 64 132 50 C136 74 140 100 144 92 C140 124 146 156 142 186Z" '
                'fill="#FFFFFF" opacity=".55" filter="url(#soft)"/>')
    rnd = random.Random("bubbles")
    for _ in range(9):
        x, y, r = rnd.uniform(104, 152), rnd.uniform(40, 170), rnd.uniform(2, 4.5)
        body.append(f'<circle cx="{f(x)}" cy="{f(y)}" r="{f(r)}" fill="none" stroke="#C8FFE6" stroke-width="1.2" opacity=".8"/>')
    # A valve stack beside the vent: someone tapped it before you.
    body.append('<rect x="170" y="150" width="14" height="36" fill="url(#steelL)"/>'
                '<rect x="184" y="150" width="8" height="36" fill="url(#steelR)"/>'
                '<ellipse cx="181" cy="150" rx="11" ry="4" fill="url(#steelTop)"/>'
                '<rect x="164" y="160" width="10" height="6" fill="#3A4459"/>'
                '<circle cx="181" cy="164" r="3" fill="#3DFFA0"/><circle cx="181" cy="164" r="6" fill="#3DFFA0" opacity=".45" filter="url(#glow)"/>')
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- gold seam

def boulder(points, seed, light="#7B7399", mid="#5A5378", dark="#3A3452"):
    """A faceted rock: its outline split into lit and shadowed facets from a peak."""
    cx = sum(p[0] for p in points) / len(points)
    peak = min(points, key=lambda p: p[1])
    peak = (peak[0] * 0.6 + cx * 0.4, peak[1] + 6)
    out = []
    n = len(points)
    for i in range(n):
        a, b = points[i], points[(i + 1) % n]
        mx = (a[0] + b[0]) / 2
        tone = light if mx < peak[0] - 6 else mid if mx < peak[0] + 8 else dark
        out.append(f'<polygon points="{pts([peak, a, b])}" fill="{tone}"/>')
    out.append(f'<polygon points="{pts(points)}" fill="none" stroke="#211C33" stroke-width="1.2" opacity=".7"/>')
    return f'<g filter="url(#shade)">{"".join(out)}</g>'


def gold_seam():
    defs = COMMON + """
<linearGradient id="gold" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FFF6CF"/><stop offset=".45" stop-color="#FFD66B"/><stop offset="1" stop-color="#B37F1E"/></linearGradient>
"""
    body = [plate(128, 198, 94, 35, "seam", pebbles=6)]
    body.append('<ellipse cx="124" cy="150" rx="66" ry="50" fill="#FFD66B" opacity=".22" filter="url(#glow2)"/>')
    body.append(boulder([(66, 196), (72, 150), (96, 118), (122, 128), (136, 170), (124, 204), (90, 210)], 1))
    body.append(boulder([(112, 202), (118, 140), (146, 96), (178, 112), (192, 160), (184, 200), (150, 212)], 2))
    body.append(boulder([(164, 206), (170, 178), (192, 168), (208, 190), (200, 210)], 3))
    veins = [[(84, 176), (96, 158), (104, 150), (118, 140)], [(128, 188), (138, 160), (150, 136), (164, 118), (178, 124)],
             [(150, 196), (160, 176), (174, 164), (188, 170)], [(98, 196), (110, 184), (122, 180)]]
    for v in veins:
        body.append(f'<polyline points="{pts(v)}" fill="none" stroke="#FFD66B" stroke-width="7" opacity=".55" filter="url(#glow)"/>'
                    f'<polyline points="{pts(v)}" fill="none" stroke="url(#gold)" stroke-width="3.6" stroke-linejoin="round" stroke-linecap="round"/>'
                    f'<polyline points="{pts(v)}" fill="none" stroke="#FFFBE8" stroke-width="1.1" stroke-linecap="round"/>')
    rnd = random.Random("nuggets")
    for _ in range(8):
        x, y, s = rnd.uniform(70, 200), rnd.uniform(206, 222), rnd.uniform(4, 8)
        body.append(f'<polygon points="{pts([(x - s, y), (x - s * 0.3, y - s * 0.9), (x + s * 0.7, y - s * 0.6), (x + s, y + s * 0.2), (x, y + s * 0.5)])}" fill="url(#gold)"/>')
    for x, y, s in ((152, 110, 6.5), (100, 146, 4.5), (182, 158, 4), (120, 182, 3.5)):
        body.append(sparkle(x, y, s, "#FFFBE8"))
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- supply cache

def iso_box(cx, cy, w, d, h, top, left, right):
    """An isometric box standing on (cx, cy): w along the right axis, d along the left, h up."""
    kx, ky = math.cos(math.radians(30)), math.sin(math.radians(30))
    front = (cx, cy)
    rgt = (cx + w * kx, cy - w * ky)
    lft = (cx - d * kx, cy - d * ky)
    back = (cx + w * kx - d * kx, cy - w * ky - d * ky)
    up = lambda p: (p[0], p[1] - h)
    return (f'<polygon points="{pts([lft, front, up(front), up(lft)])}" fill="{left}"/>'
            f'<polygon points="{pts([front, rgt, up(rgt), up(front)])}" fill="{right}"/>'
            f'<polygon points="{pts([up(lft), up(front), up(rgt), up(back)])}" fill="{top}"/>'), (front, rgt, lft, back)


def supply_cache():
    defs = COMMON + """
<linearGradient id="crateT" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FFD29A"/><stop offset="1" stop-color="#FF9A3D"/></linearGradient>
<linearGradient id="crateL" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F08A33"/><stop offset="1" stop-color="#B35E1C"/></linearGradient>
<linearGradient id="crateR" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#A5531A"/><stop offset="1" stop-color="#6A3310"/></linearGradient>
"""
    body = [plate(128, 200, 90, 33, "cache", pebbles=5)]
    # A landing pad: a dark hex plate with an orange rim.
    hexpad = [(128 + math.cos(math.radians(a)) * 62, 196 + math.sin(math.radians(a)) * 24) for a in range(0, 360, 60)]
    body.append(f'<polygon points="{pts(hexpad)}" fill="#1B1D2B"/>'
                f'<polygon points="{pts(hexpad)}" fill="none" stroke="#FF9A3D" stroke-width="2" opacity=".85"/>')
    body.append('<ellipse cx="128" cy="150" rx="62" ry="48" fill="#FF9A3D" opacity=".22" filter="url(#glow2)"/>')
    box, corners = iso_box(128, 210, 48, 48, 50, "url(#crateT)", "url(#crateL)", "url(#crateR)")
    body.append(f'<g filter="url(#shade)">{box}</g>')
    front, rgt, lft, back = corners
    kx, ky = math.cos(math.radians(30)), math.sin(math.radians(30))
    # Steel straps round the crate, a hazard stripe and a stencil.
    for t in (0.28, 0.72):
        a = (front[0] + (rgt[0] - front[0]) * t, front[1] + (rgt[1] - front[1]) * t)
        b = (front[0] + (lft[0] - front[0]) * t, front[1] + (lft[1] - front[1]) * t)
        body.append(f'<polygon points="{pts([a, (a[0], a[1] - 50), (a[0] + 5 * kx, a[1] - 50 - 5 * ky), (a[0] + 5 * kx, a[1] - 5 * ky)])}" fill="#3B4458"/>')
        body.append(f'<polygon points="{pts([b, (b[0], b[1] - 50), (b[0] - 5 * kx, b[1] - 50 - 5 * ky), (b[0] - 5 * kx, b[1] - 5 * ky)])}" fill="#4C576F"/>')
    body.append(f'<polyline points="{pts([(front[0] - 30, front[1] - 32), (front[0], front[1] - 15), (front[0] + 30, front[1] - 32)])}" '
                'fill="none" stroke="#1B1D2B" stroke-width="2" opacity=".5"/>')
    # The beacon: an antenna on the lid with a light and its glow.
    top_c = (128 + (48 * kx - 48 * kx) / 2, 210 - 50 - 24)
    body.append(f'<rect x="{f(top_c[0] - 1.5)}" y="{f(top_c[1] - 34)}" width="3" height="36" fill="#6E7C9A"/>'
                f'<circle cx="{f(top_c[0])}" cy="{f(top_c[1] - 36)}" r="12" fill="#FF9A3D" opacity=".6" filter="url(#glow)"/>'
                f'<circle cx="{f(top_c[0])}" cy="{f(top_c[1] - 36)}" r="4.5" fill="#FFE2B8"/>')
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- relic

def relic():
    defs = COMMON + """
<linearGradient id="gemT" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FBEFFF"/><stop offset="1" stop-color="#D58CFF"/></linearGradient>
<linearGradient id="gemM" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#B46AF0"/><stop offset="1" stop-color="#6B2FA8"/></linearGradient>
<linearGradient id="gemD" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6E2FB0"/><stop offset="1" stop-color="#2E1050"/></linearGradient>
"""
    body = [plate(128, 202, 88, 32, "relic", pebbles=4)]
    # A broken ring of standing stones round a pedestal.
    for a in range(0, 360, 45):
        if a in (90,):
            continue
        x = 128 + math.cos(math.radians(a)) * 66
        y = 200 + math.sin(math.radians(a)) * 24
        h = 26 if a not in (180, 0) else 18
        body.append(f'<polygon points="{pts([(x - 6, y), (x - 5, y - h), (x + 1, y - h - 4), (x + 6, y - h + 2), (x + 6, y)])}" fill="url(#rockTop)" filter="url(#shade)"/>')
    body.append('<ellipse cx="128" cy="204" rx="30" ry="11" fill="#2E2946"/><ellipse cx="128" cy="198" rx="30" ry="11" fill="url(#rockTop)"/>'
                '<ellipse cx="128" cy="198" rx="18" ry="6" fill="#D58CFF" opacity=".5" filter="url(#glow)"/>')
    body.append('<ellipse cx="128" cy="112" rx="58" ry="66" fill="#D58CFF" opacity=".42" filter="url(#glow2)"/>')
    # The floating octahedron.
    top, left, right, front, bottom = (128, 42), (94, 110), (162, 110), (134, 124), (128, 176)
    body.append(f'<polygon points="{pts([top, left, front])}" fill="url(#gemT)"/>'
                f'<polygon points="{pts([top, front, right])}" fill="url(#gemM)"/>'
                f'<polygon points="{pts([left, bottom, front])}" fill="url(#gemM)"/>'
                f'<polygon points="{pts([front, bottom, right])}" fill="url(#gemD)"/>'
                f'<polyline points="{pts([left, top, right])}" fill="none" stroke="#FFFFFF" stroke-width="1.4" opacity=".8"/>'
                f'<polyline points="{pts([top, front, bottom])}" fill="none" stroke="#F4DDFF" stroke-width="1" opacity=".7"/>')
    body.append('<ellipse cx="128" cy="112" rx="72" ry="16" fill="none" stroke="#E9C8FF" stroke-width="2" opacity=".8" '
                'stroke-dasharray="120 40 30 40"/>')
    for x, y, s in ((96, 70, 6), (168, 88, 4.5), (122, 60, 3.5)):
        body.append(sparkle(x, y, s, "#FBEFFF"))
    for x, y, s in ((78, 132, 5), (182, 124, 4)):
        body.append(f'<polygon points="{pts([(x, y - s * 1.6), (x + s, y), (x, y + s * 1.6), (x - s, y)])}" fill="url(#gemM)" opacity=".9"/>')
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- survey beacon

def survey_beacon():
    defs = COMMON + """
<linearGradient id="scan" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FF3DD8" stop-opacity=".85"/><stop offset="1" stop-color="#FF3DD8" stop-opacity="0.05"/></linearGradient>
"""
    body = [plate(128, 204, 80, 30, "beacon", pebbles=4)]
    body.append('<ellipse cx="128" cy="200" rx="62" ry="22" fill="none" stroke="#FF3DD8" stroke-width="2.5" opacity=".9"/>'
                '<ellipse cx="128" cy="200" rx="40" ry="14" fill="none" stroke="#FF3DD8" stroke-width="1.5" opacity=".6"/>'
                '<ellipse cx="128" cy="200" rx="62" ry="22" fill="#FF3DD8" opacity=".25" filter="url(#glow)"/>')
    # The scan cone from the head down to the ring.
    body.append('<polygon points="128,92 70,200 186,200" fill="url(#scan)" opacity=".55"/>')
    # Tripod legs and the sensor head.
    for x in (92, 164, 128):
        body.append(f'<line x1="128" y1="104" x2="{x}" y2="{200 if x != 128 else 212}" stroke="#5B6782" stroke-width="5" stroke-linecap="round"/>'
                    f'<line x1="128" y1="104" x2="{x}" y2="{200 if x != 128 else 212}" stroke="#9AA8C6" stroke-width="1.5" stroke-linecap="round"/>')
    body.append('<rect x="110" y="78" width="36" height="24" rx="4" fill="url(#steelL)"/>'
                '<rect x="128" y="78" width="18" height="24" rx="4" fill="url(#steelR)"/>'
                '<ellipse cx="128" cy="78" rx="18" ry="6" fill="url(#steelTop)"/>'
                '<circle cx="128" cy="92" r="6" fill="#FF3DD8"/><circle cx="128" cy="92" r="14" fill="#FF3DD8" opacity=".6" filter="url(#glow)"/>'
                '<line x1="136" y1="74" x2="148" y2="44" stroke="#9AA8C6" stroke-width="2"/>'
                '<circle cx="148" cy="42" r="3.5" fill="#FFB8F0"/><circle cx="148" cy="42" r="9" fill="#FF3DD8" opacity=".5" filter="url(#glow)"/>')
    return doc(256, 256, defs, "".join(body))


# ---------------------------------------------------------------- harvester drone

def drone():
    defs = COMMON + """
<radialGradient id="rotor"><stop offset="0" stop-color="#E8F7FF" stop-opacity=".5"/><stop offset="1" stop-color="#9FD8FF" stop-opacity=".08"/></radialGradient>
<linearGradient id="hull" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#A7B4D0"/><stop offset=".5" stop-color="#5E6A86"/><stop offset="1" stop-color="#2E3548"/></linearGradient>
"""
    body = ['<ellipse cx="64" cy="70" rx="46" ry="30" fill="#FF9A3D" opacity=".22" filter="url(#glow)"/>']
    arms = [(24, 46), (104, 46), (34, 78), (94, 78)]
    for x, y in arms:
        body.append(f'<line x1="64" y1="62" x2="{x}" y2="{y}" stroke="#3A4459" stroke-width="6" stroke-linecap="round"/>'
                    f'<line x1="64" y1="61" x2="{x}" y2="{y - 1}" stroke="#8C99B6" stroke-width="2" stroke-linecap="round"/>')
    for x, y in arms:
        body.append(f'<ellipse cx="{x}" cy="{y - 4}" rx="18" ry="6" fill="url(#rotor)"/>'
                    f'<ellipse cx="{x}" cy="{y - 4}" rx="18" ry="6" fill="none" stroke="#DDF3FF" stroke-width=".8" opacity=".6"/>'
                    f'<rect x="{x - 3}" y="{y - 6}" width="6" height="8" rx="2" fill="#4B5670"/>')
    body.append('<ellipse cx="64" cy="64" rx="24" ry="13" fill="url(#hull)"/>'
                '<ellipse cx="64" cy="58" rx="16" ry="6" fill="#C9D5EE" opacity=".7"/>'
                '<rect x="54" y="72" width="20" height="14" rx="3" fill="#2A3040"/>'
                '<rect x="56" y="74" width="16" height="10" rx="2" fill="#3DF5FF" opacity=".85"/>'
                '<rect x="56" y="74" width="16" height="10" rx="2" fill="#3DF5FF" opacity=".6" filter="url(#glow)"/>'
                '<circle cx="46" cy="64" r="2.6" fill="#FF9A3D"/><circle cx="82" cy="64" r="2.6" fill="#FF9A3D"/>'
                '<circle cx="46" cy="64" r="6" fill="#FF9A3D" opacity=".6" filter="url(#glow)"/>'
                '<circle cx="82" cy="64" r="6" fill="#FF9A3D" opacity=".6" filter="url(#glow)"/>')
    return doc(128, 128, defs, "".join(body))


ART = {
    "CrystalField": crystal_field, "HeliumVent": helium_vent, "GoldSeam": gold_seam, "SupplyCache": supply_cache,
    "Relic": relic, "SurveyBeacon": survey_beacon, "Drone": drone,
}
SIZE = {"Drone": 128}

if __name__ == "__main__":
    names = [n for n in ART if not ONLY or n in ONLY]
    for n in names:
        open(os.path.join(OUT, f"{n}.svg"), "w").write(ART[n]())
    open(os.path.join(OUT, "sizes.txt"), "w").write("\n".join(f"{n} {SIZE.get(n, 256)}" for n in names) + "\n")
    print("wrote", len(names))
