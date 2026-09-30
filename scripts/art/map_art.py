#!/usr/bin/env python3
"""The galaxy map's art (map redesign, 2026-09-29): every point of interest is a
small world, the Galactic Core is a sun with a clear zone around it.

  map-planets    768 x 768 atlas, 3 x 3 cells of 256: gold asteroid (a cratered
                 gold moon), quartz nebula (a ringed crystal ice world), helium
                 cloud (a banded gas giant) / derelict (a dead moon in a broken
                 debris ring), pirate camp (a volcanic world in a warning halo),
                 dark matter field (a glowing violet world, bright ring) /
                 comet (an icy nucleus with a long glowing tail), trade
                 caravan (three freighters in an escort ring) — the map events,
                 2026-09-30. The map's galaxy field draws every world from this
                 one sheet.
  map-badge-*    64 x 64 type badges shown beside a world when zoomed in.
  core-sun       1024 x 1024: the sun, its corona and the station ring.
  core-zone      1024 x 1024: the Core Zone's dashed boundary.
  zone-storm     1024 x 1024: the Ion Storm: blue-violet cloud bands wound in a
                 slow spiral, forked lightning, a ragged edge (2026-09-30).
  zone-nova      1024 x 1024: the Supernova's doomed sector: a swollen, unstable
                 star in shock rings, inside a red hazard boundary.

render_map.sh runs this and renders them to GalaxyRoyale/Assets/Resources/Map.

usage: map_art.py <out dir> [name ...]
"""
import math
import os
import random
import sys

OUT = sys.argv[1]
ONLY = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)
sizes = []


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def write(name, size, body):
    if ONLY and name not in ONLY:
        return
    with open(os.path.join(OUT, name + ".svg"), "w") as fh:
        fh.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" '
                 f'viewBox="0 0 {size} {size}">{body}</svg>')
    sizes.append(f"{name} {size}")


# ---------------------------------------------------------------- worlds

KINDS = [
    # name, rim colour, highlight, mid, shade, deep
    ("asteroid", "#F6C445", "#FFEAA8", "#E0A838", "#7A5212", "#3A2606"),
    ("nebula", "#3DE0FF", "#EFFDFF", "#7FEAFF", "#1A8FB0", "#06303F"),
    ("heliumcloud", "#4DFFA6", "#DAFFEC", "#5CF0A8", "#1C8C58", "#07301D"),
    ("derelict", "#B8C2D9", "#EEF1F7", "#A2AABD", "#474E62", "#1C2030"),
    ("camp", "#FF4D6A", "#FFC9B0", "#FF5A5A", "#8A1426", "#36060F"),
    ("dmfield", "#B57BFF", "#F4E8FF", "#BE8BFF", "#5A25A8", "#14062C"),
]


def world(i, kind, rim, hi, mid, shade, deep, cx, cy):
    """One world centred in its 256 cell. Sphere radius 64 (rings reach ±118)."""
    r = 64
    uid = f"{kind}{i}"
    g = []
    defs = [
        f'<radialGradient id="s{uid}" cx="0.36" cy="0.32" r="0.78">'
        f'<stop offset="0" stop-color="{hi}"/><stop offset="0.34" stop-color="{mid}"/>'
        f'<stop offset="0.74" stop-color="{shade}"/><stop offset="1" stop-color="{deep}"/></radialGradient>',
        f'<radialGradient id="g{uid}"><stop offset="0.45" stop-color="{rim}" stop-opacity="0.55"/>'
        f'<stop offset="1" stop-color="{rim}" stop-opacity="0"/></radialGradient>',
        f'<radialGradient id="t{uid}" cx="0.7" cy="0.72" r="0.75"><stop offset="0.5" stop-color="#000" stop-opacity="0"/>'
        f'<stop offset="1" stop-color="#000" stop-opacity="0.55"/></radialGradient>',
        f'<clipPath id="c{uid}"><circle cx="{cx}" cy="{cy}" r="{r}"/></clipPath>',
    ]
    glow_r = 104 if kind in ("dmfield", "camp") else 92
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{glow_r}" fill="url(#g{uid})"/>')

    ring = kind in ("nebula", "derelict", "dmfield")
    ring_col = {"nebula": "#7FEAFF", "derelict": "#C4CCDD", "dmfield": "#E2C4FF"}.get(kind, rim)
    rx, ry, tilt = 116, 26, -18
    if ring:
        dash = ' stroke-dasharray="14 9"' if kind == "derelict" else ""
        width = 7 if kind == "dmfield" else 5
        # the far half of the ring, behind the world
        g.append(f'<path d="M{f(cx - rx)},{cy} A{rx},{ry} 0 0 1 {f(cx + rx)},{cy}" fill="none" stroke="{ring_col}" '
                 f'stroke-width="{width}" stroke-opacity="0.75"{dash} transform="rotate({tilt} {cx} {cy})"/>')

    g.append(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#s{uid})"/>')
    surf = []
    rnd = random.Random(kind)
    if kind == "asteroid":
        for (dx, dy, cr) in [(18, 14, 13), (-20, 26, 9), (12, -26, 7), (-28, -6, 6), (30, -4, 5), (-4, 40, 6)]:
            surf.append(f'<circle cx="{cx + dx}" cy="{cy + dy}" r="{cr}" fill="#462D08" fill-opacity="0.6"/>'
                        f'<circle cx="{cx + dx - cr * 0.25}" cy="{cy + dy - cr * 0.25}" r="{cr * 0.7}" fill="#FFE39A" fill-opacity="0.18"/>')
    elif kind == "nebula":
        for (x1, y1, x2, y2) in [(-40, -30, 10, 40), (-10, -55, 40, 10), (-55, 5, -5, 55)]:
            surf.append(f'<path d="M{cx + x1},{cy + y1} L{cx + x2},{cy + y2}" stroke="#FFFFFF" stroke-opacity="0.35" stroke-width="3"/>')
        surf.append(f'<path d="M{cx - 20},{cy - 40} l14,-12 l10,14 l-10,20 z" fill="#FFFFFF" fill-opacity="0.25"/>')
    elif kind == "heliumcloud":
        for k in range(-5, 6):
            y = cy + k * 12
            op = 0.22 if k % 2 else 0.12
            col = "#FFFFFF" if k % 2 else "#063A22"
            surf.append(f'<path d="M{cx - 80},{y - 8} Q{cx},{y + 6} {cx + 80},{y - 4}" stroke="{col}" '
                        f'stroke-opacity="{op}" stroke-width="7" fill="none"/>')
        surf.append(f'<ellipse cx="{cx + 20}" cy="{cy + 18}" rx="14" ry="7" fill="#0E5A36" fill-opacity="0.55"/>')
    elif kind == "derelict":
        for (dx, dy, cr) in [(14, 18, 10), (-22, -14, 8), (24, -22, 5)]:
            surf.append(f'<circle cx="{cx + dx}" cy="{cy + dy}" r="{cr}" fill="#141824" fill-opacity="0.6"/>')
        surf.append(f'<path d="M{cx - 50},{cy + 6} L{cx - 10},{cy - 2} L{cx + 8},{cy + 16} L{cx + 52},{cy + 8}" '
                    f'stroke="#141824" stroke-opacity="0.55" stroke-width="3" fill="none"/>')
    elif kind == "camp":
        for _ in range(9):
            a = rnd.uniform(0, math.tau)
            d = rnd.uniform(8, 50)
            x, y = cx + math.cos(a) * d, cy + math.sin(a) * d
            surf.append(f'<circle cx="{f(x)}" cy="{f(y)}" r="{f(rnd.uniform(2.5, 5.5))}" fill="#FFC46A" fill-opacity="0.9"/>')
        surf.append(f'<path d="M{cx - 40},{cy - 10} Q{cx - 5},{cy + 14} {cx + 34},{cy - 22}" stroke="#FFB05A" '
                    f'stroke-opacity="0.55" stroke-width="3" fill="none"/>')
    elif kind == "dmfield":
        surf.append(f'<circle cx="{cx + 6}" cy="{cy + 4}" r="26" fill="#F1E0FF" fill-opacity="0.18"/>')
        surf.append(f'<path d="M{cx - 60},{cy + 12} Q{cx},{cy - 18} {cx + 60},{cy + 6}" stroke="#E7CCFF" '
                    f'stroke-opacity="0.3" stroke-width="5" fill="none"/>')
    g.append(f'<g clip-path="url(#c{uid})">{"".join(surf)}'
             f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="url(#t{uid})"/></g>')
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{r - 1}" fill="none" stroke="{hi}" stroke-opacity="0.35" stroke-width="2"/>')

    if ring:
        dash = ' stroke-dasharray="14 9"' if kind == "derelict" else ""
        width = 7 if kind == "dmfield" else 5
        # the near half, in front of the world
        g.append(f'<path d="M{f(cx - rx)},{cy} A{rx},{ry} 0 0 0 {f(cx + rx)},{cy}" fill="none" stroke="{ring_col}" '
                 f'stroke-width="{width}"{dash} transform="rotate({tilt} {cx} {cy})"/>')
    if kind == "camp":
        g.append(f'<circle cx="{cx}" cy="{cy}" r="{r + 16}" fill="none" stroke="#FF4D6A" stroke-width="5" stroke-opacity="0.85"/>')
        for a in (0, 90, 180, 270):
            x1 = cx + math.cos(math.radians(a)) * (r + 10)
            y1 = cy + math.sin(math.radians(a)) * (r + 10)
            x2 = cx + math.cos(math.radians(a)) * (r + 26)
            y2 = cy + math.sin(math.radians(a)) * (r + 26)
            g.append(f'<path d="M{f(x1)},{f(y1)} L{f(x2)},{f(y2)}" stroke="#FF4D6A" stroke-width="6" stroke-linecap="round"/>')
    return "".join(defs), "".join(g)


defs_all, body_all = [], []
for i, (kind, rim, hi, mid, shade, deep) in enumerate(KINDS):
    col, row = i % 3, i // 3
    d, b = world(i, kind, rim, hi, mid, shade, deep, col * 256 + 128, row * 256 + 128)
    defs_all.append(d)
    body_all.append(b)


def comet(cx, cy):
    """The Comet Pass's comet: a bright icy nucleus, its tail streaming up-right."""
    d = ('<radialGradient id="cmN" cx="0.4" cy="0.38" r="0.7"><stop offset="0" stop-color="#FFFFFF"/>'
         '<stop offset="0.4" stop-color="#CFF8FF"/><stop offset="1" stop-color="#2A7FA8"/></radialGradient>'
         '<linearGradient id="cmT" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#8FF4FF" stop-opacity="0.95"/>'
         '<stop offset="1" stop-color="#8FF4FF" stop-opacity="0"/></linearGradient>'
         '<radialGradient id="cmG"><stop offset="0.3" stop-color="#8FF4FF" stop-opacity="0.6"/>'
         '<stop offset="1" stop-color="#8FF4FF" stop-opacity="0"/></radialGradient>')
    x0, y0 = cx - 34, cy + 34
    g = [f'<circle cx="{x0}" cy="{y0}" r="86" fill="url(#cmG)"/>',
         f'<path d="M{x0 - 30},{y0 + 8} Q{cx + 20},{cy - 10} {cx + 120},{cy - 118} Q{cx + 10},{cy + 30} {x0 + 12},{y0 + 30} Z" fill="url(#cmT)"/>',
         f'<path d="M{x0 - 10},{y0 - 20} Q{cx + 30},{cy - 60} {cx + 110},{cy - 122}" stroke="#E4FCFF" stroke-opacity="0.55" stroke-width="5" fill="none"/>',
         f'<path d="M{x0 + 20},{y0 + 6} Q{cx + 60},{cy - 20} {cx + 122},{cy - 96}" stroke="#B388FF" stroke-opacity="0.5" stroke-width="4" fill="none"/>',
         f'<circle cx="{x0}" cy="{y0}" r="40" fill="url(#cmN)"/>',
         f'<circle cx="{x0 - 10}" cy="{y0 - 12}" r="9" fill="#FFFFFF" fill-opacity="0.8"/>']
    rnd = random.Random(11)
    for _ in range(14):
        t = rnd.uniform(0.2, 1)
        g.append(f'<circle cx="{f(x0 + t * 150 + rnd.uniform(-14, 14))}" cy="{f(y0 - t * 150 + rnd.uniform(-14, 14))}" '
                 f'r="{f(rnd.uniform(1.5, 4))}" fill="#FFFFFF" fill-opacity="{f(0.9 - t * 0.6)}"/>')
    return d, "".join(g)


def caravan(cx, cy):
    """The Trade Caravan: three gold freighters in line inside a dashed escort ring."""
    d = ('<radialGradient id="cvG"><stop offset="0.35" stop-color="#FFC857" stop-opacity="0.55"/>'
         '<stop offset="1" stop-color="#FFC857" stop-opacity="0"/></radialGradient>'
         '<linearGradient id="cvH" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE7A8"/>'
         '<stop offset="1" stop-color="#B7791F"/></linearGradient>')
    g = [f'<circle cx="{cx}" cy="{cy}" r="110" fill="url(#cvG)"/>',
         f'<circle cx="{cx}" cy="{cy}" r="92" fill="none" stroke="#FFC857" stroke-width="5" stroke-dasharray="16 10" stroke-opacity="0.9"/>']
    for k, (dx, dy, sc) in enumerate([(-40, 44, 0.72), (6, -4, 0.9), (48, -48, 0.72)]):
        x, y = cx + dx, cy + dy
        w, h = 58 * sc, 24 * sc
        g.append(f'<g transform="rotate(-20 {f(x)} {f(y)})">'
                 f'<rect x="{f(x - w / 2)}" y="{f(y - h / 2)}" width="{f(w)}" height="{f(h)}" rx="{f(6 * sc)}" '
                 f'fill="url(#cvH)" stroke="#3A2606" stroke-width="3"/>'
                 f'<rect x="{f(x - w * 0.3)}" y="{f(y - h * 0.3)}" width="{f(w * 0.18)}" height="{f(h * 0.6)}" fill="#3A2606" fill-opacity="0.5"/>'
                 f'<rect x="{f(x - w * 0.05)}" y="{f(y - h * 0.3)}" width="{f(w * 0.18)}" height="{f(h * 0.6)}" fill="#3A2606" fill-opacity="0.5"/>'
                 f'<path d="M{f(x + w / 2)},{f(y - h * 0.3)} l{f(12 * sc)},{f(h * 0.3)} l{f(-12 * sc)},{f(h * 0.3)} z" fill="#FFE7A8"/>'
                 f'<circle cx="{f(x - w / 2 - 6 * sc)}" cy="{f(y)}" r="{f(6 * sc)}" fill="#7FEAFF" fill-opacity="0.9"/></g>')
    return d, "".join(g)


for fn, cell in ((comet, 6), (caravan, 7)):
    d, b = fn((cell % 3) * 256 + 128, (cell // 3) * 256 + 128)
    defs_all.append(d)
    body_all.append(b)
write("map-planets", 768, f'<defs>{"".join(defs_all)}</defs>{"".join(body_all)}')

# ---------------------------------------------------------------- badges

GLYPHS = {
    "asteroid": '<path d="M7 5l6-2 5 4 2 6-3 6-7 1-5-4-1-6z"/>',
    "nebula": '<path d="M12 2l5 6-5 14-5-14z"/><path d="M7 8h10"/>',
    "heliumcloud": '<path d="M12 3c4 5 6 8 6 11a6 6 0 0 1-12 0c0-3 2-6 6-11z"/><path d="M9 14h6"/>',
    "derelict": '<path d="M4 16l6-10h4l6 10-4 4H8z"/><path d="M9 12h6"/>',
    "camp": '<circle cx="12" cy="12" r="7"/><path d="M12 2v5M12 17v5M2 12h5M17 12h5"/>',
    "dmfield": '<path d="M12 3l7 9-7 9-7-9z"/>',
    "comet": '<circle cx="8" cy="16" r="4"/><path d="M11 13l9-9M12 17l8-5M7 12l6-8"/>',
    "caravan": '<path d="M3 9h11l3 3-3 3H3z"/><path d="M8 17h9l3 2-3 2H8z"/><path d="M8 5h8"/>',
}
EVENT_BADGES = [("comet", "#8FF4FF"), ("caravan", "#FFC857")]
for kind, rim in [(k[0], k[1]) for k in KINDS] + EVENT_BADGES:
    write(f"map-badge-{kind}", 64,
          f'<circle cx="32" cy="32" r="28" fill="#0D0820"/>'
          f'<circle cx="32" cy="32" r="27" fill="none" stroke="{rim}" stroke-width="3"/>'
          f'<g transform="translate(14 14) scale(1.5)" fill="none" stroke="{rim}" stroke-width="2.6" '
          f'stroke-linejoin="round" stroke-linecap="round">{GLYPHS[kind]}</g>')

# ---------------------------------------------------------------- the Core

C = 512
sun = f'''<defs>
<radialGradient id="corona"><stop offset="0.3" stop-color="#FFD27A" stop-opacity="0.75"/>
<stop offset="0.55" stop-color="#FF9A3D" stop-opacity="0.32"/><stop offset="0.8" stop-color="#FF3DD8" stop-opacity="0.08"/>
<stop offset="1" stop-color="#FF3DD8" stop-opacity="0"/></radialGradient>
<radialGradient id="disc" cx="0.4" cy="0.36" r="0.72"><stop offset="0" stop-color="#FFF8E0"/>
<stop offset="0.3" stop-color="#FFD98A"/><stop offset="0.66" stop-color="#FF9A3D"/><stop offset="1" stop-color="#C2410C"/></radialGradient>
<radialGradient id="limb"><stop offset="0.8" stop-color="#FFFFFF" stop-opacity="0"/>
<stop offset="1" stop-color="#FFF1C8" stop-opacity="0.55"/></radialGradient>
<filter id="blur" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="10"/></filter>
</defs>
<circle cx="{C}" cy="{C}" r="500" fill="url(#corona)"/>'''
rnd = random.Random(7)
flares = []
for k in range(24):
    a = k / 24 * math.tau + rnd.uniform(-0.08, 0.08)
    r1, r2 = 238, 238 + rnd.uniform(40, 120)
    flares.append(f'<path d="M{f(C + math.cos(a) * r1)},{f(C + math.sin(a) * r1)} '
                  f'L{f(C + math.cos(a) * r2)},{f(C + math.sin(a) * r2)}" stroke="#FFD27A" '
                  f'stroke-opacity="{f(rnd.uniform(0.15, 0.4))}" stroke-width="{f(rnd.uniform(6, 16))}" stroke-linecap="round"/>')
sun += f'<g filter="url(#blur)">{"".join(flares)}</g>'
rx, ry, tilt = 390, 88, -14
sun += (f'<path d="M{C - rx},{C} A{rx},{ry} 0 0 1 {C + rx},{C}" fill="none" stroke="#3DE0FF" stroke-width="12" '
        f'stroke-opacity="0.6" transform="rotate({tilt} {C} {C})"/>')
sun += f'<circle cx="{C}" cy="{C}" r="230" fill="url(#disc)"/>'
cells = []
for _ in range(70):
    a = rnd.uniform(0, math.tau)
    d = math.sqrt(rnd.uniform(0, 1)) * 205
    cells.append(f'<circle cx="{f(C + math.cos(a) * d)}" cy="{f(C + math.sin(a) * d)}" r="{f(rnd.uniform(8, 22))}" '
                 f'fill="#FFF3C4" fill-opacity="{f(rnd.uniform(0.06, 0.16))}"/>')
sun += f'<g>{"".join(cells)}</g><circle cx="{C}" cy="{C}" r="230" fill="url(#limb)"/>'
sun += (f'<path d="M{C - rx},{C} A{rx},{ry} 0 0 0 {C + rx},{C}" fill="none" stroke="#3DE0FF" stroke-width="12" '
        f'transform="rotate({tilt} {C} {C})"/>')
for k in range(6):
    a = math.radians(22 + k * 27)
    x, y = C + math.cos(a) * rx, C + math.sin(a) * ry
    # station modules riding the near arc
    sun += (f'<g transform="rotate({tilt} {C} {C})"><rect x="{f(x - 12)}" y="{f(y - 7)}" width="24" height="14" rx="3" '
            f'fill="#0D0820" stroke="#3DE0FF" stroke-width="3"/></g>')
write("core-sun", 1024, sun)

dashes = []
for k in range(72):
    a0 = k / 72 * math.tau
    a1 = a0 + math.tau / 72 * 0.55
    dashes.append(f'<path d="M{f(C + math.cos(a0) * 500)},{f(C + math.sin(a0) * 500)} '
                  f'A500,500 0 0 1 {f(C + math.cos(a1) * 500)},{f(C + math.sin(a1) * 500)}"/>')
write("core-zone", 1024,
      f'<defs><radialGradient id="z"><stop offset="0.6" stop-color="#FF9A3D" stop-opacity="0"/>'
      f'<stop offset="1" stop-color="#FF9A3D" stop-opacity="0.07"/></radialGradient></defs>'
      f'<circle cx="{C}" cy="{C}" r="500" fill="url(#z)"/>'
      f'<g fill="none" stroke="#FF9A3D" stroke-opacity="0.7" stroke-width="7" stroke-linecap="round">{"".join(dashes)}</g>')

# ---------------------------------------------------------------- event zones

def bolt(rnd, x, y, a, length, width):
    """A jagged lightning path from (x, y) heading at angle a, with one fork."""
    pts, forks = [(x, y)], []
    steps = 7
    for k in range(steps):
        a += rnd.uniform(-0.55, 0.55)
        x += math.cos(a) * length / steps
        y += math.sin(a) * length / steps
        pts.append((x, y))
        if k == 3:
            fa = a + rnd.choice((-1, 1)) * rnd.uniform(0.5, 0.9)
            forks.append([(x, y), (x + math.cos(fa) * length * 0.25, y + math.sin(fa) * length * 0.25)])
    path = lambda ps: "M" + " L".join(f"{f(px)},{f(py)}" for px, py in ps)
    out = (f'<path d="{path(pts)}" stroke="#9FB8FF" stroke-width="{f(width * 3)}" stroke-opacity="0.35" filter="url(#sg)"/>'
           f'<path d="{path(pts)}" stroke="#F2F6FF" stroke-width="{f(width)}"/>')
    for fp in forks:
        out += f'<path d="{path(fp)}" stroke="#DDE6FF" stroke-width="{f(width * 0.6)}"/>'
    return out


rnd = random.Random(19)
storm = ('<defs><radialGradient id="sf"><stop offset="0" stop-color="#6C7CFF" stop-opacity="0.05"/>'
         '<stop offset="0.7" stop-color="#5A6BFF" stop-opacity="0.16"/><stop offset="0.95" stop-color="#8A5BFF" stop-opacity="0.26"/>'
         '<stop offset="1" stop-color="#8A5BFF" stop-opacity="0"/></radialGradient>'
         '<filter id="sb" x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="14"/></filter>'
         '<filter id="sg" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="6"/></filter></defs>'
         f'<circle cx="{C}" cy="{C}" r="500" fill="url(#sf)"/>')
# Cloud bands: arcs on a slow logarithmic spiral, blurred into soft streaks.
bands = []
for k in range(26):
    r = 120 + k * 14 + rnd.uniform(-8, 8)
    a0 = rnd.uniform(0, math.tau)
    span = rnd.uniform(0.9, 2.2)
    x0, y0 = C + math.cos(a0) * r, C + math.sin(a0) * r
    r1 = r * 1.12
    x1, y1 = C + math.cos(a0 + span) * r1, C + math.sin(a0 + span) * r1
    col = rnd.choice(("#7F8CFF", "#9C7BFF", "#6FB6FF", "#B7C2FF"))
    bands.append(f'<path d="M{f(x0)},{f(y0)} A{f(r)},{f(r)} 0 0 1 {f(x1)},{f(y1)}" stroke="{col}" '
                 f'stroke-opacity="{f(rnd.uniform(0.18, 0.4))}" stroke-width="{f(rnd.uniform(14, 34))}" stroke-linecap="round"/>')
storm += f'<g fill="none" filter="url(#sb)">{"".join(bands)}</g>'
# Crisp inner streaks over the soft bands.
streaks = []
for k in range(18):
    r = rnd.uniform(160, 470)
    a0 = rnd.uniform(0, math.tau)
    span = rnd.uniform(0.25, 0.7)
    streaks.append(f'<path d="M{f(C + math.cos(a0) * r)},{f(C + math.sin(a0) * r)} A{f(r)},{f(r)} 0 0 1 '
                   f'{f(C + math.cos(a0 + span) * r)},{f(C + math.sin(a0 + span) * r)}" stroke="#DCE3FF" '
                   f'stroke-opacity="{f(rnd.uniform(0.2, 0.45))}" stroke-width="{f(rnd.uniform(2, 4))}" stroke-linecap="round"/>')
storm += f'<g fill="none">{"".join(streaks)}</g>'
bolts = []
for k in range(5):
    a = k / 5 * math.tau + rnd.uniform(-0.3, 0.3)
    r = rnd.uniform(190, 330)
    bolts.append(bolt(rnd, C + math.cos(a) * r, C + math.sin(a) * r, a + rnd.uniform(1.2, 1.9), rnd.uniform(140, 210), 4.5))
storm += f'<g fill="none" stroke-linecap="round" stroke-linejoin="round">{"".join(bolts)}</g>'
# A ragged edge: short dashes whose radius wanders.
edge = []
for k in range(96):
    a0 = k / 96 * math.tau
    a1 = a0 + math.tau / 96 * 0.6
    r = 488 + rnd.uniform(-10, 8)
    edge.append(f'<path d="M{f(C + math.cos(a0) * r)},{f(C + math.sin(a0) * r)} A{f(r)},{f(r)} 0 0 1 '
                f'{f(C + math.cos(a1) * r)},{f(C + math.sin(a1) * r)}"/>')
storm += (f'<g fill="none" stroke="#9FB0FF" stroke-opacity="0.75" stroke-width="6" stroke-linecap="round">'
          f'{"".join(edge)}</g>')
write("zone-storm", 1024, storm)

rnd = random.Random(29)
nova = ('<defs><radialGradient id="nf"><stop offset="0" stop-color="#FF7A2E" stop-opacity="0.22"/>'
        '<stop offset="0.5" stop-color="#FF4D2E" stop-opacity="0.08"/><stop offset="0.93" stop-color="#FF2E4D" stop-opacity="0.16"/>'
        '<stop offset="1" stop-color="#FF2E4D" stop-opacity="0"/></radialGradient>'
        '<radialGradient id="ns" cx="0.45" cy="0.42" r="0.7"><stop offset="0" stop-color="#FFFFFF"/>'
        '<stop offset="0.25" stop-color="#FFF1B8"/><stop offset="0.6" stop-color="#FF9A3D"/><stop offset="1" stop-color="#D9261C"/></radialGradient>'
        '<radialGradient id="nc"><stop offset="0.25" stop-color="#FFB15A" stop-opacity="0.8"/>'
        '<stop offset="0.6" stop-color="#FF4D2E" stop-opacity="0.25"/><stop offset="1" stop-color="#FF2E4D" stop-opacity="0"/></radialGradient>'
        '<filter id="nb" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="9"/></filter></defs>'
        f'<circle cx="{C}" cy="{C}" r="500" fill="url(#nf)"/>')
# Shock rings, fainter as they spread.
for k, r in enumerate((150, 235, 320, 405)):
    nova += (f'<circle cx="{C}" cy="{C}" r="{r}" fill="none" stroke="#FFB15A" '
             f'stroke-opacity="{f(0.55 - k * 0.11)}" stroke-width="{f(9 - k * 1.5)}"/>')
# The swollen star: a corona of uneven flares, then the disc and its hot spots.
flares = []
for k in range(30):
    a = k / 30 * math.tau + rnd.uniform(-0.06, 0.06)
    r1, r2 = 92, 92 + rnd.uniform(25, 85)
    flares.append(f'<path d="M{f(C + math.cos(a) * r1)},{f(C + math.sin(a) * r1)} L{f(C + math.cos(a) * r2)},'
                  f'{f(C + math.sin(a) * r2)}" stroke="#FFC46B" stroke-opacity="{f(rnd.uniform(0.3, 0.7))}" '
                  f'stroke-width="{f(rnd.uniform(6, 14))}" stroke-linecap="round"/>')
nova += f'<circle cx="{C}" cy="{C}" r="190" fill="url(#nc)"/><g filter="url(#nb)">{"".join(flares)}</g>'
nova += f'<circle cx="{C}" cy="{C}" r="96" fill="url(#ns)"/>'
for _ in range(16):
    a = rnd.uniform(0, math.tau)
    d = math.sqrt(rnd.uniform(0, 1)) * 80
    nova += (f'<circle cx="{f(C + math.cos(a) * d)}" cy="{f(C + math.sin(a) * d)}" r="{f(rnd.uniform(5, 13))}" '
             f'fill="#FFFFFF" fill-opacity="{f(rnd.uniform(0.15, 0.4))}"/>')
# Ejecta: specks flung outward.
for _ in range(60):
    a = rnd.uniform(0, math.tau)
    d = rnd.uniform(120, 460)
    nova += (f'<circle cx="{f(C + math.cos(a) * d)}" cy="{f(C + math.sin(a) * d)}" r="{f(rnd.uniform(1.5, 4))}" '
             f'fill="#FFD9A0" fill-opacity="{f(rnd.uniform(0.3, 0.8))}"/>')
# The hazard boundary: long red dashes broken by warning chevrons.
haz = []
for k in range(8):
    a0 = k / 8 * math.tau + 0.12
    a1 = a0 + math.tau / 8 - 0.24
    haz.append(f'<path d="M{f(C + math.cos(a0) * 492)},{f(C + math.sin(a0) * 492)} A492,492 0 0 1 '
               f'{f(C + math.cos(a1) * 492)},{f(C + math.sin(a1) * 492)}"/>')
chev = []
for k in range(8):
    a = k / 8 * math.tau
    x, y = C + math.cos(a) * 492, C + math.sin(a) * 492
    deg = math.degrees(a) + 90
    chev.append(f'<g transform="translate({f(x)} {f(y)}) rotate({f(deg)})">'
                f'<path d="M-22,14 L0,-16 L22,14 Z" fill="#FF2E4D" stroke="#0D0820" stroke-width="3"/>'
                f'<rect x="-2.5" y="-6" width="5" height="11" fill="#0D0820"/><rect x="-2.5" y="7" width="5" height="4" fill="#0D0820"/></g>')
nova += (f'<g fill="none" stroke="#FF2E4D" stroke-opacity="0.85" stroke-width="8" stroke-linecap="round" '
         f'stroke-dasharray="26 14">{"".join(haz)}</g>{"".join(chev)}')
write("zone-nova", 1024, nova)

with open(os.path.join(OUT, "sizes.txt"), "w") as fh:
    fh.write("\n".join(sizes) + "\n")
