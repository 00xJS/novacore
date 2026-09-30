#!/usr/bin/env python3
"""The Frontier's last four buildings (2026-09-29): the Repair Dock, the Jump
Gate, the Clan Embassy and the Deep Space Observatory — isometric, lit, on the
concrete plinth the other buildings stand on, in the base's dark metal with
teal and orange light. 512 x 512 transparent SVGs; render_buildings.sh renders
them to GalaxyRoyale/Assets/Resources/Buildings/<BuildingId>.png (the name the
globe loads). Painted art dropped in under the same names replaces them.

usage: building_art.py <out dir> [name ...]
"""
import math
import os
import sys

OUT = sys.argv[1]
ONLY = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)

CX, CY = 256, 300          # screen point of the world origin (plinth top centre)
TEAL, ORANGE, MAGENTA, GOLD = "#3DE0FF", "#FF9A3D", "#FF3DD8", "#F6C445"
M1, M2, M3, M4, M5 = "#1C2029", "#2A303C", "#3B4252", "#566074", "#8A94A8"


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def P(x, y, z=0.0):
    """Dimetric (2:1) projection: +x runs down-right, +y down-left, +z up."""
    return CX + (x - y), CY + (x + y) / 2 - z


def pts(ps):
    return " ".join(f"{f(a)},{f(b)}" for a, b in ps)


class Scene:
    def __init__(self):
        self.parts = []

    def add(self, s):
        self.parts.append(s)

    def poly(self, world, fill, stroke="none", sw=0, extra=""):
        self.add(f'<polygon points="{pts([P(*w) for w in world])}" fill="{fill}" stroke="{stroke}" '
                 f'stroke-width="{sw}" stroke-linejoin="round"{extra}/>')

    def prism(self, base, z0, z1, top, light, dark, rim=None):
        """A vertical prism over a convex base polygon (world x,y, counter-clockwise
        seen from above); only the faces turned towards the viewer are drawn."""
        n = len(base)
        for i in range(n):
            (x1, y1), (x2, y2) = base[i], base[(i + 1) % n]
            nx, ny = (y2 - y1), -(x2 - x1)   # outward normal for a CCW polygon
            if nx + ny <= 0:
                continue
            shade = light if nx > ny else dark
            self.poly([(x1, y1, z0), (x2, y2, z0), (x2, y2, z1), (x1, y1, z1)], shade, "#0B0D12", 1)
        self.poly([(x, y, z1) for x, y in base], top, rim or "#0B0D12", 1.2)

    def box(self, x, y, w, d, z0, h, top, light, dark, rim=None):
        self.prism([(x, y), (x + w, y), (x + w, y + d), (x, y + d)][::-1], z0, z0 + h, top, light, dark, rim)

    def cyl(self, x, y, r, z0, z1, top, side, rim="#0B0D12"):
        rx, ry = r * math.sqrt(2), r * math.sqrt(2) / 2
        bx, by = P(x, y, z0)
        tx, ty = P(x, y, z1)
        self.add(f'<ellipse cx="{f(bx)}" cy="{f(by)}" rx="{f(rx)}" ry="{f(ry)}" fill="{side}" stroke="{rim}"/>')
        self.add(f'<rect x="{f(bx - rx)}" y="{f(ty)}" width="{f(2 * rx)}" height="{f(by - ty)}" fill="{side}"/>')
        self.add(f'<path d="M{f(bx - rx)},{f(ty)} L{f(bx - rx)},{f(by)} M{f(bx + rx)},{f(ty)} L{f(bx + rx)},{f(by)}" '
                 f'stroke="{rim}" stroke-width="1"/>')
        self.add(f'<ellipse cx="{f(tx)}" cy="{f(ty)}" rx="{f(rx)}" ry="{f(ry)}" fill="{top}" stroke="{rim}" stroke-width="1.2"/>')

    def glow_line(self, a, b, color, width=4):
        (x1, y1), (x2, y2) = P(*a), P(*b)
        self.add(f'<path d="M{f(x1)},{f(y1)} L{f(x2)},{f(y2)}" stroke="{color}" stroke-width="{width + 6}" '
                 f'stroke-opacity="0.25" stroke-linecap="round" filter="url(#blur)"/>')
        self.add(f'<path d="M{f(x1)},{f(y1)} L{f(x2)},{f(y2)}" stroke="{color}" stroke-width="{width}" stroke-linecap="round"/>')

    def svg(self):
        defs = f'''<defs>
<filter id="blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="5"/></filter>
<filter id="blur2" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="12"/></filter>
<linearGradient id="metal" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{M4}"/><stop offset="1" stop-color="{M2}"/></linearGradient>
<radialGradient id="portal"><stop offset="0" stop-color="#FFFFFF"/><stop offset="0.25" stop-color="#9FF3FF"/>
<stop offset="0.6" stop-color="{TEAL}" stop-opacity="0.85"/><stop offset="1" stop-color="{MAGENTA}" stop-opacity="0.55"/></radialGradient>
<radialGradient id="lens"><stop offset="0" stop-color="#FFFFFF"/><stop offset="0.4" stop-color="#8FEFFF"/><stop offset="1" stop-color="#0E4E66"/></radialGradient>
<linearGradient id="dome" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#9AA4B8"/><stop offset="0.5" stop-color="#5A6478"/><stop offset="1" stop-color="#2C3240"/></linearGradient>
</defs>'''
        return f'<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">{defs}{"".join(self.parts)}</svg>'


def octagon(r, rot=22.5):
    return [(r * math.cos(math.radians(rot + 45 * k)), r * math.sin(math.radians(rot + 45 * k))) for k in range(8)][::-1]


def plinth(sc):
    """The concrete plinth every building stands on, with a lit trim."""
    sc.prism(octagon(172), -30, 0, "#4A4E57", "#5C606A", "#393C44")
    sc.prism(octagon(150), 0, 4, "#3A3E47", "#4A4E57", "#30333A")
    # panel seams and a glowing edge strip on the front faces
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        sc.poly([(0, 0, 4.2), (150 * math.cos(a), 150 * math.sin(a), 4.2)], "none", "#2A2D34", 1)
    for k in (0, 1, 2):
        a1, a2 = math.radians(22.5 + 45 * k), math.radians(22.5 + 45 * (k + 1))
        p1 = (172 * math.cos(a1), 172 * math.sin(a1), -8)
        p2 = (172 * math.cos(a2), 172 * math.sin(a2), -8)
        sc.glow_line(p1, p2, TEAL if k != 1 else ORANGE, 2.5)


def write(name, sc):
    if ONLY and name not in ONLY:
        return
    with open(os.path.join(OUT, name + ".svg"), "w") as fh:
        fh.write(sc.svg())


# ---------------------------------------------------------------- Jump Gate

def jump_gate():
    sc = Scene()
    plinth(sc)
    # the launch pad the ring stands over
    sc.prism(octagon(96), 4, 14, "#23303C", "#2E3B49", "#1C2630")
    sc.add(f'<ellipse cx="{f(P(0, 0, 14.5)[0])}" cy="{f(P(0, 0, 14.5)[1])}" rx="118" ry="59" fill="none" '
           f'stroke="{TEAL}" stroke-width="3" stroke-opacity="0.8"/>')
    # two pylons holding the ring
    for s in (-1, 1):
        sc.box(-14 + s * 0, s * 104 - 14, 28, 28, 14, 150, M3, M4, M2)
        sc.glow_line((s * 0 - 14, s * 104 + 14, 40), (s * 0 - 14, s * 104 + 14, 150), ORANGE, 2)
    # the ring: an upright annulus on the x = 0 plane (seen edge-on at 45°)
    ring_c = (0, 0, 120)
    R, t = 100, 18
    outer = [(0, R * math.cos(math.radians(a)), ring_c[2] + R * math.sin(math.radians(a))) for a in range(0, 361, 6)]
    inner = [(0, (R - t) * math.cos(math.radians(a)), ring_c[2] + (R - t) * math.sin(math.radians(a))) for a in range(0, 361, 6)]
    # portal glow behind the membrane
    ox, oy = P(*ring_c)
    sc.add(f'<ellipse cx="{f(ox)}" cy="{f(oy)}" rx="95" ry="120" fill="{TEAL}" opacity="0.35" filter="url(#blur2)"/>')
    sc.add(f'<polygon points="{pts([P(*p) for p in inner])}" fill="url(#portal)" opacity="0.92"/>')
    # swirl in the membrane
    for k in range(4):
        rr = (R - t) * (0.25 + 0.18 * k)
        arc = [(0, rr * math.cos(math.radians(a + 40 * k)), ring_c[2] + rr * math.sin(math.radians(a + 40 * k)))
               for a in range(0, 250, 10)]
        sc.add(f'<polyline points="{pts([P(*p) for p in arc])}" fill="none" stroke="#FFFFFF" stroke-opacity="{0.55 - 0.1 * k}" '
               f'stroke-width="{3 - 0.5 * k}" stroke-linecap="round"/>')
    # the ring body with its segments
    sc.add(f'<path d="M{pts([P(*p) for p in outer])} Z M{pts([P(*p) for p in inner][::-1])} Z" fill="url(#metal)" '
           f'fill-rule="evenodd" stroke="#0B0D12" stroke-width="1.5"/>')
    for a in range(0, 360, 30):
        p1 = P(0, (R - t) * math.cos(math.radians(a)), ring_c[2] + (R - t) * math.sin(math.radians(a)))
        p2 = P(0, R * math.cos(math.radians(a)), ring_c[2] + R * math.sin(math.radians(a)))
        sc.add(f'<path d="M{f(p1[0])},{f(p1[1])} L{f(p2[0])},{f(p2[1])}" stroke="{ORANGE if a % 90 == 0 else "#0B0D12"}" '
               f'stroke-width="{4 if a % 90 == 0 else 1.5}"/>')
    sc.add(f'<polygon points="{pts([P(*p) for p in inner])}" fill="none" stroke="{TEAL}" stroke-width="3"/>')
    # control consoles on the pad
    for (x, y) in ((70, -40), (60, 50)):
        sc.box(x, y, 24, 18, 14, 16, M2, M3, M1)
        sc.poly([(x + 2, y + 2, 30.5), (x + 22, y + 2, 30.5), (x + 22, y + 16, 30.5), (x + 2, y + 16, 30.5)], TEAL, extra=' opacity="0.7"')
    write("JumpGate", sc)


# ---------------------------------------------------------------- Repair Dock

def repair_dock():
    sc = Scene()
    plinth(sc)
    # the dock's cradle
    sc.box(-90, -60, 180, 120, 4, 10, "#2B313C", "#363D4A", "#222731")
    for k in range(5):
        x = -80 + k * 40
        sc.box(x, -50, 10, 100, 14, 8, M3, M4, M2)
    # a damaged frigate in the cradle: a wedge hull
    hull = [(-80, 0), (-40, -34), (70, -26), (95, 0), (70, 26), (-40, 34)]
    sc.prism(hull, 22, 48, "#4E5566", "#5E6678", "#3A404D")
    sc.prism([(-30, -20), (40, -16), (55, 0), (40, 16), (-30, 20)], 48, 62, "#5A6274", "#6A7386", "#434A58")
    # hull breaches, scorch and exposed ribs
    for (x, y, z) in ((10, -30, 38), (-20, 33, 35), (50, 20, 40)):
        px, py = P(x, y, z)
        sc.add(f'<ellipse cx="{f(px)}" cy="{f(py)}" rx="10" ry="6" fill="#141820"/>')
        sc.add(f'<ellipse cx="{f(px)}" cy="{f(py)}" rx="15" ry="9" fill="#2A1A10" opacity="0.5"/>')
    # engine nozzles
    for y in (-14, 14):
        sc.cyl(-86, y, 9, 26, 40, "#3A1E10", M2)
        ex, ey = P(-86, y, 33)
        sc.add(f'<circle cx="{f(ex)}" cy="{f(ey)}" r="4" fill="{ORANGE}" opacity="0.8"/>')
    # the gantry crane over it
    for (x, y) in ((-100, -75), (-100, 60), (90, -75), (90, 60)):
        sc.box(x, y, 12, 12, 14, 120, M3, M4, M2)
    sc.box(-100, -75, 202, 12, 134, 12, M4, M5, M3)
    sc.box(-100, 60, 202, 12, 134, 12, M4, M5, M3)
    sc.box(-10, -75, 18, 147, 146, 10, M3, M4, M2)
    # the trolley and its arm reaching down to the hull, sparks at the weld
    sc.box(-12, -8, 22, 22, 128, 18, M2, M3, M1)
    sc.glow_line((0, 3, 128), (8, 0, 66), "#9AA4B8", 3)
    wx, wy = P(8, 0, 64)
    sc.add(f'<circle cx="{f(wx)}" cy="{f(wy)}" r="16" fill="{ORANGE}" opacity="0.55" filter="url(#blur)"/>')
    sc.add(f'<circle cx="{f(wx)}" cy="{f(wy)}" r="5" fill="#FFF3C4"/>')
    for k in range(12):
        a = math.radians(200 + k * 14)
        l = 14 + (k * 7) % 18
        sc.add(f'<path d="M{f(wx)},{f(wy)} l{f(math.cos(a) * l)},{f(math.sin(a) * l * 0.8 + 6)}" stroke="#FFD27A" '
               f'stroke-width="1.6" stroke-linecap="round"/>')
    # warning stripes and a tool rack
    for k in range(6):
        x = -90 + k * 30
        sc.poly([(x, 60, 14.5), (x + 14, 60, 14.5), (x + 18, 56, 14.5), (x + 4, 56, 14.5)], GOLD)
    sc.box(110, -30, 20, 60, 4, 34, M2, M3, M1)
    sc.glow_line((130, -26, 34), (130, 26, 34), TEAL, 2)
    write("RepairDock", sc)


# ---------------------------------------------------------------- Clan Embassy

def clan_embassy():
    sc = Scene()
    plinth(sc)
    # steps up to the hall
    for k in range(3):
        sc.box(40 + k * 12, -50, 30, 100, 4 + k * 6, 6, "#4A4E57", "#5C606A", "#393C44")
    # the hall: a wide block with a stepped roof
    sc.box(-90, -70, 140, 140, 4, 70, M3, M4, M2)
    sc.box(-80, -60, 120, 120, 74, 16, M4, M5, M3)
    sc.box(-60, -40, 80, 80, 90, 14, M3, M4, M2)
    # columns across the front face (x = 50 side)
    for k in range(5):
        y = -56 + k * 28
        sc.box(50, y, 8, 8, 4, 70, M5, "#A7B0C2", M4)
    # tall lit windows between them
    for k in range(4):
        y = -46 + k * 28
        sc.poly([(50.5, y, 14), (50.5, y + 14, 14), (50.5, y + 14, 60), (50.5, y, 60)], TEAL, extra=' opacity="0.55"')
    # the clan banner poles either side
    for y in (-100, 100):
        sc.box(20, y - 3, 6, 6, 4, 150, M4, M5, M3)
        bx, by = P(23, y, 150)
        col = TEAL if y < 0 else ORANGE
        flag = [P(23, y, 148), P(23, y + (26 if y < 0 else -26), 142), P(23, y + (24 if y < 0 else -24), 108), P(23, y, 112)]
        sc.add(f'<polygon points="{pts(flag)}" fill="{col}" stroke="#0B0D12" stroke-width="1"/>')
    # the clan emblem: two linked rings floating over the roof
    ex, ey = P(-20, 0, 150)
    sc.add(f'<ellipse cx="{f(ex)}" cy="{f(ey)}" rx="70" ry="44" fill="{TEAL}" opacity="0.18" filter="url(#blur2)"/>')
    for dx in (-18, 18):
        sc.add(f'<circle cx="{f(ex + dx)}" cy="{f(ey)}" r="26" fill="none" stroke="{TEAL}" stroke-width="7" opacity="0.35" filter="url(#blur)"/>')
        sc.add(f'<circle cx="{f(ex + dx)}" cy="{f(ey)}" r="26" fill="none" stroke="#B7F6FF" stroke-width="4"/>')
    bx, by = P(-20, 0, 104)
    sc.add(f'<path d="M{f(bx)},{f(by)} L{f(ex)},{f(ey + 26)}" stroke="{TEAL}" stroke-width="2" stroke-dasharray="4 4" opacity="0.8"/>')
    write("ClanEmbassy", sc)


# ---------------------------------------------------------------- Observatory

def observatory():
    sc = Scene()
    plinth(sc)
    # a small radar dish off to one side
    sc.box(80, 50, 16, 16, 4, 40, M3, M4, M2)
    dx, dy = P(88, 58, 58)
    sc.add(f'<ellipse cx="{f(dx)}" cy="{f(dy)}" rx="26" ry="15" fill="{M5}" stroke="#0B0D12" transform="rotate(-25 {f(dx)} {f(dy)})"/>')
    sc.add(f'<ellipse cx="{f(dx)}" cy="{f(dy)}" rx="18" ry="10" fill="{M3}" transform="rotate(-25 {f(dx)} {f(dy)})"/>')
    # the drum and its lit ring
    sc.cyl(-10, -10, 92, 4, 70, M3, M2)
    sc.cyl(-10, -10, 96, 70, 78, M4, M3)
    for k in range(9):
        a = math.radians(-10 + k * 22)
        x, y = -10 + 92 * math.cos(a), -10 + 92 * math.sin(a)
        if math.cos(a) + math.sin(a) > 0.2:
            sc.poly([(x, y, 24), (x, y, 50)], "none", TEAL, 3)
    # the dome
    tx, ty = P(-10, -10, 78)
    rx, ry = 92 * math.sqrt(2) * 0.96, 92 * math.sqrt(2) / 2 * 0.96
    sc.add(f'<path d="M{f(tx - rx)},{f(ty)} A{f(rx)},{f(rx * 0.95)} 0 0 1 {f(tx + rx)},{f(ty)} A{f(rx)},{f(ry)} 0 0 1 {f(tx - rx)},{f(ty)} Z" '
           f'fill="url(#dome)" stroke="#0B0D12" stroke-width="1.5"/>')
    for k in (-2, -1, 1, 2):
        sc.add(f'<path d="M{f(tx + k * rx / 3)},{f(ty + ry * 0.94 * math.sqrt(1 - (k / 3) ** 2))} '
               f'Q{f(tx + k * rx / 3.2)},{f(ty - rx * 0.6)} {f(tx)},{f(ty - rx * 0.95)}" stroke="#2C3240" stroke-width="1.2" fill="none"/>')
    # the slit, open towards the sky, and the telescope rising through it
    sc.add(f'<path d="M{f(tx - 16)},{f(ty + 20)} L{f(tx - 10)},{f(ty - rx * 0.93)} L{f(tx + 26)},{f(ty - rx * 0.9)} L{f(tx + 18)},{f(ty + 22)} Z" fill="#0E1118"/>')
    base = (tx + 4, ty - 40)
    end = (tx + 110, ty - 150)
    ang = math.degrees(math.atan2(end[1] - base[1], end[0] - base[0]))
    length = math.hypot(end[0] - base[0], end[1] - base[1])
    sc.add(f'<g transform="translate({f(base[0])} {f(base[1])}) rotate({f(ang)})">'
           f'<rect x="-10" y="-22" width="{f(length)}" height="44" rx="6" fill="url(#metal)" stroke="#0B0D12" stroke-width="1.5"/>'
           f'<rect x="{f(length * 0.3)}" y="-25" width="14" height="50" rx="3" fill="{M2}"/>'
           f'<rect x="{f(length * 0.62)}" y="-25" width="10" height="50" rx="3" fill="{M2}"/>'
           f'<rect x="{f(length - 16)}" y="-27" width="22" height="54" rx="5" fill="{M4}" stroke="#0B0D12"/>'
           f'<ellipse cx="{f(length + 6)}" cy="0" rx="8" ry="24" fill="url(#lens)"/>'
           f'<path d="M4,-8 L{f(length * 0.95)},-8" stroke="{ORANGE}" stroke-width="2.5"/></g>')
    lx, ly = base[0] + math.cos(math.radians(ang)) * (length + 6), base[1] + math.sin(math.radians(ang)) * (length + 6)
    sc.add(f'<circle cx="{f(lx)}" cy="{f(ly)}" r="26" fill="{TEAL}" opacity="0.45" filter="url(#blur)"/>')
    # stars it's looking at
    for (sx, sy, r) in ((lx + 24, ly - 30, 2.5), (lx - 30, ly - 44, 2), (lx + 50, ly + 6, 1.6)):
        sc.add(f'<circle cx="{f(sx)}" cy="{f(sy)}" r="{r}" fill="#FFFFFF" opacity="0.9"/>')
    write("Observatory", sc)


jump_gate()
repair_dock()
clan_embassy()
observatory()
