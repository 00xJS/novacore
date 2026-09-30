#!/usr/bin/env python3
"""The galaxy map's art (map redesign, 2026-09-29): every point of interest is a
small world, the Galactic Core is a sun with a clear zone around it.

  map-planets    768 x 768 atlas, 3 x 3 cells of 256: gold asteroid (a cratered
                 gold moon), quartz nebula (a ringed crystal ice world), helium
                 cloud (a banded gas giant) / derelict (a dead moon in a broken
                 debris ring), pirate camp (a volcanic world in a warning halo),
                 dark matter field (a glowing violet world, bright ring). The
                 map's galaxy field draws every world from this one sheet.
  map-badge-*    64 x 64 type badges shown beside a world when zoomed in.
  core-sun       1024 x 1024: the sun, its corona and the station ring.
  core-zone      1024 x 1024: the Core Zone's dashed boundary.

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
write("map-planets", 768, f'<defs>{"".join(defs_all)}</defs>{"".join(body_all)}')

# ---------------------------------------------------------------- badges

GLYPHS = {
    "asteroid": '<path d="M7 5l6-2 5 4 2 6-3 6-7 1-5-4-1-6z"/>',
    "nebula": '<path d="M12 2l5 6-5 14-5-14z"/><path d="M7 8h10"/>',
    "heliumcloud": '<path d="M12 3c4 5 6 8 6 11a6 6 0 0 1-12 0c0-3 2-6 6-11z"/><path d="M9 14h6"/>',
    "derelict": '<path d="M4 16l6-10h4l6 10-4 4H8z"/><path d="M9 12h6"/>',
    "camp": '<circle cx="12" cy="12" r="7"/><path d="M12 2v5M12 17v5M2 12h5M17 12h5"/>',
    "dmfield": '<path d="M12 3l7 9-7 9-7-9z"/>',
}
for kind, rim, *_ in KINDS:
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

with open(os.path.join(OUT, "sizes.txt"), "w") as fh:
    fh.write("\n".join(sizes) + "\n")
