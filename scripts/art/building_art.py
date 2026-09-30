#!/usr/bin/env python3
"""The Frontier's last four buildings (2026-09-29): the Repair Dock, the Jump
Gate, the Clan Embassy and the Deep Space Observatory — and the Citadel's five
(2026-09-30): the Academy, the Relic Vault, the Missile Silo, the Trade
Consulate and the Terraformer — isometric, lit, on the
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


# ================================================================ the Citadel (2026-09-30)

# ---------------------------------------------------------------- Academy

def academy():
    sc = Scene()
    plinth(sc)
    # a broad lecture hall with a colonnade, crowned by a spire and a star emblem
    sc.box(-100, -80, 150, 160, 4, 56, M3, M4, M2)
    sc.box(-90, -70, 130, 140, 60, 12, M4, M5, M3)
    for k in range(6):
        y = -70 + k * 28
        sc.box(52, y, 8, 8, 4, 56, M5, "#A7B0C2", M4)
    for k in range(5):
        y = -60 + k * 28
        sc.poly([(52.5, y, 12), (52.5, y + 16, 12), (52.5, y + 16, 48), (52.5, y, 48)], ORANGE, extra=' opacity="0.5"')
    # the spire
    sc.prism(octagon(26), 72, 150, M4, M5, M3)
    sc.prism(octagon(16), 150, 200, M5, "#A7B0C2", M4)
    tip = P(0, 0, 238)
    b1, b2 = P(-14, 0, 200), P(0, 14, 200)
    b3 = P(14, 0, 200)
    sc.add(f'<polygon points="{f(b1[0])},{f(b1[1])} {f(tip[0])},{f(tip[1])} {f(b3[0])},{f(b3[1])} {f(b2[0])},{f(b2[1])}" '
           f'fill="{M5}" stroke="#0B0D12" stroke-width="1.2"/>')
    # the star emblem glowing over the hall
    ex, ey = P(-10, -10, 250)
    sc.add(f'<circle cx="{f(ex)}" cy="{f(ey)}" r="40" fill="{GOLD}" opacity="0.25" filter="url(#blur2)"/>')
    star = []
    for k in range(10):
        r = 26 if k % 2 == 0 else 11
        a = math.radians(-90 + 36 * k)
        star.append(f"{f(ex + r * math.cos(a))},{f(ey + r * math.sin(a))}")
    sc.add(f'<polygon points="{" ".join(star)}" fill="{GOLD}" stroke="#FFF1C8" stroke-width="2"/>')
    # lit windows on the side face
    for k in range(4):
        x = -86 + k * 32
        sc.poly([(x, 80.5, 16), (x + 18, 80.5, 16), (x + 18, 80.5, 44), (x, 80.5, 44)], TEAL, extra=' opacity="0.55"')
    write("Academy", sc)


# ---------------------------------------------------------------- Relic Vault

def relic_vault():
    sc = Scene()
    plinth(sc)
    # a stepped ziggurat vault
    for k, (r, z0, h) in enumerate(((110, 4, 26), (86, 30, 26), (62, 56, 26))):
        sc.box(-r, -r, 2 * r, 2 * r, z0, h, [M3, M4, M5][k], [M4, M5, "#A7B0C2"][k], [M2, M3, M4][k])
    # glowing seams on each step
    for (r, z) in ((110, 16), (86, 42), (62, 68)):
        sc.glow_line((r, -r + 8, z), (r, r - 8, z), GOLD, 2)
        sc.glow_line((-r + 8, r, z), (r - 8, r, z), GOLD, 2)
    # the display case on top: a glass cube with a relic floating inside
    sc.box(-30, -30, 60, 60, 82, 8, M2, M3, M1)
    cx, cy = P(0, 0, 122)
    sc.add(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="58" fill="{MAGENTA}" opacity="0.22" filter="url(#blur2)"/>')
    glass = [P(-26, -26, 90), P(26, -26, 90), P(26, 26, 90), P(-26, 26, 90)]
    top = [P(-26, -26, 150), P(26, -26, 150), P(26, 26, 150), P(-26, 26, 150)]
    sc.add(f'<polygon points="{pts([glass[1], glass[2], top[2], top[1]])}" fill="#9FF3FF" opacity="0.16" stroke="#B7F6FF" stroke-width="1.5"/>')
    sc.add(f'<polygon points="{pts([glass[2], glass[3], top[3], top[2]])}" fill="#9FF3FF" opacity="0.1" stroke="#B7F6FF" stroke-width="1.5"/>')
    # the relic: a faceted violet crystal
    k1, k2, k3, k4 = P(0, 0, 146), P(-12, 0, 120), P(0, 12, 102), P(12, 0, 120)
    k5 = P(0, -12, 120)
    sc.add(f'<polygon points="{pts([k1, k2, k3])}" fill="#D9A8FF" stroke="#2A0E4A" stroke-width="1"/>')
    sc.add(f'<polygon points="{pts([k1, k4, k3])}" fill="#8A4DE0" stroke="#2A0E4A" stroke-width="1"/>')
    sc.add(f'<polygon points="{pts([k1, k5, k2])}" fill="#F1DCFF" stroke="#2A0E4A" stroke-width="1" opacity="0.9"/>')
    sc.add(f'<polygon points="{pts([top[0], top[1], top[2], top[3]])}" fill="#9FF3FF" opacity="0.12" stroke="#B7F6FF" stroke-width="1.5"/>')
    write("RelicVault", sc)


# ---------------------------------------------------------------- Missile Silo

def missile_silo():
    sc = Scene()
    plinth(sc)
    # a low armoured bunker with three silo hatches; two missiles raised
    sc.box(-110, -90, 200, 180, 4, 24, M2, M3, M1)
    sc.box(-100, -80, 180, 160, 28, 6, M3, M4, M2)
    for (x, y, open_) in ((-60, -45, True), (10, -45, False), (-25, 40, True)):
        cx, cy = P(x, y, 34.5)
        sc.add(f'<ellipse cx="{f(cx)}" cy="{f(cy)}" rx="40" ry="20" fill="#0E1118" stroke="{ORANGE if open_ else M4}" stroke-width="3"/>')
        if not open_:
            sc.add(f'<ellipse cx="{f(cx)}" cy="{f(cy)}" rx="34" ry="17" fill="{M4}" stroke="#0B0D12"/>')
            sc.add(f'<path d="M{f(cx - 34)},{f(cy)} L{f(cx + 34)},{f(cy)}" stroke="#0B0D12" stroke-width="2"/>')
            continue
        # the missile, standing out of its hatch
        bx, by = P(x, y, 34)
        tx, ty = P(x, y, 190)
        sc.add(f'<rect x="{f(bx - 13)}" y="{f(ty + 26)}" width="26" height="{f(by - ty - 26)}" fill="#D7DCE6" stroke="#0B0D12" stroke-width="1.5"/>')
        sc.add(f'<rect x="{f(bx - 13)}" y="{f(ty + 70)}" width="26" height="10" fill="{ORANGE}"/>')
        sc.add(f'<path d="M{f(bx - 13)},{f(ty + 27)} Q{f(bx)},{f(ty - 10)} {f(bx + 13)},{f(ty + 27)} Z" fill="#FF4D6A" stroke="#0B0D12" stroke-width="1.5"/>')
        for s in (-1, 1):
            sc.add(f'<path d="M{f(bx + s * 13)},{f(by - 40)} l{f(s * 14)},20 l0,16 l{f(-s * 14)},-8 z" fill="{M4}" stroke="#0B0D12" stroke-width="1"/>')
    # warning lights on the bunker's corners
    for (x, y) in ((90, -90), (90, 90), (-110, 90)):
        lx, ly = P(x, y, 30)
        sc.add(f'<circle cx="{f(lx)}" cy="{f(ly)}" r="9" fill="#FF4D6A" opacity="0.6" filter="url(#blur)"/>')
        sc.add(f'<circle cx="{f(lx)}" cy="{f(ly)}" r="4" fill="#FFD2D9"/>')
    write("MissileSilo", sc)


# ---------------------------------------------------------------- Trade Consulate

def trade_consulate():
    sc = Scene()
    plinth(sc)
    # a landing pad for the traders' ships
    lx, ly = P(20, 70, 4.5)
    sc.add(f'<ellipse cx="{f(lx)}" cy="{f(ly)}" rx="62" ry="31" fill="#23303C" stroke="{GOLD}" stroke-width="3"/>')
    sc.add(f'<ellipse cx="{f(lx)}" cy="{f(ly)}" rx="40" ry="20" fill="none" stroke="{GOLD}" stroke-width="2" stroke-dasharray="8 6"/>')
    # the consulate tower: a slim hexagonal tower with a gold crown and a trade-beacon ring
    tower = [(24 * math.cos(math.radians(30 + 60 * k)) - 50, 24 * math.sin(math.radians(30 + 60 * k)) - 50) for k in range(6)][::-1]
    sc.prism(tower, 4, 150, M3, M4, M2)
    crown = [(34 * math.cos(math.radians(30 + 60 * k)) - 50, 34 * math.sin(math.radians(30 + 60 * k)) - 50) for k in range(6)][::-1]
    sc.prism(crown, 150, 170, GOLD, "#FFE39A", "#B7791F")
    for z in (50, 90, 130):
        sc.glow_line((-26, -60, z), (-26, -40, z), GOLD, 2)
    rx, ry = P(-50, -50, 195)
    sc.add(f'<ellipse cx="{f(rx)}" cy="{f(ry)}" rx="46" ry="16" fill="none" stroke="{GOLD}" stroke-width="5" opacity="0.4" filter="url(#blur)"/>')
    sc.add(f'<ellipse cx="{f(rx)}" cy="{f(ry)}" rx="46" ry="16" fill="none" stroke="#FFE7A8" stroke-width="3"/>')
    sc.add(f'<circle cx="{f(rx)}" cy="{f(ry - 4)}" r="9" fill="#FFF3C4"/>')
    # cargo crates stacked by the pad (drawn last: they stand in front)
    for (x, y, z, col) in ((70, -30, 4, GOLD), (100, -30, 4, ORANGE), (70, 0, 4, TEAL), (85, -15, 30, GOLD)):
        sc.box(x - 14, y - 14, 28, 28, z, 26, col, M4, M2)
    write("TradeConsulate", sc)


# ---------------------------------------------------------------- Terraformer

def terraformer():
    sc = Scene()
    plinth(sc)
    # four intake pylons around a tall atmospheric processor venting green-teal mist
    for (x, y) in ((-90, -20), (-20, -90), (60, 0), (0, 60)):
        sc.box(x - 10, y - 10, 20, 20, 4, 70, M3, M4, M2)
        sc.glow_line((x, y + 10, 20), (x, y + 10, 64), "#4DFFA6", 2)
    sc.cyl(0, 0, 62, 4, 50, M3, M2)
    sc.cyl(0, 0, 44, 50, 140, M4, M3)
    for z in (72, 98, 124):
        sc.cyl(0, 0, 50, z, z + 6, "#4DFFA6", "#1C8C58")
    sc.cyl(0, 0, 30, 140, 164, M5, M4)
    mx, my = P(0, 0, 164)
    for k, (dx, dy, r, op) in enumerate(((0, -34, 40, 0.45), (-26, -66, 34, 0.3), (22, -88, 30, 0.25), (-6, -112, 26, 0.18))):
        sc.add(f'<circle cx="{f(mx + dx)}" cy="{f(my + dy)}" r="{r}" fill="#9FFFD0" opacity="{op}" filter="url(#blur2)"/>')
    sc.add(f'<ellipse cx="{f(mx)}" cy="{f(my)}" rx="{f(30 * math.sqrt(2))}" ry="{f(15 * math.sqrt(2))}" fill="#4DFFA6" opacity="0.8"/>')
    write("Terraformer", sc)


academy()
relic_vault()
missile_silo()
trade_consulate()
terraformer()
