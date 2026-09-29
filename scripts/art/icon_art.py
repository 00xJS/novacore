#!/usr/bin/env python3
"""The app icon and the launch logo (2026-09-29), in the Neon Hologram style:
the home planet with its orange hologram grid and neon rim, an orbit ring
running round it, and a Vanguard (the ship art in Resources/Ships) riding the
ring on an engine trail.

  icon.svg    1024 x 1024, opaque: the App Store icon (iOS rounds the corners)
  launch.svg  1200 x 1200, transparent: the emblem over "GALAXY ROYALE" for the
              launch screen, which puts it on the game's own dark background

render_icon.sh runs this and renders them into GalaxyRoyale/Assets.

usage: icon_art.py <out dir>
"""
import base64
import math
import os
import random
import sys

OUT = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "GalaxyRoyale", "Assets")
os.makedirs(OUT, exist_ok=True)


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def b64(path):
    return base64.b64encode(open(path, "rb").read()).decode()


SHIP = b64(os.path.join(ASSETS, "Resources", "Ships", "Vanguard.png"))
ORBITRON = b64(os.path.join(ASSETS, "Resources", "Fonts", "Orbitron.ttf"))

ORANGE, MAGENTA, INK = "#FF9A3D", "#FF3DD8", "#05030E"


def defs(uid):
    return f"""
<radialGradient id="{uid}space" cx="50%" cy="40%" r="75%"><stop offset="0" stop-color="#2B1556"/><stop offset=".55" stop-color="#12082E"/><stop offset="1" stop-color="{INK}"/></radialGradient>
<radialGradient id="{uid}planet" cx="36%" cy="30%" r="80%"><stop offset="0" stop-color="#5A3A9A"/><stop offset=".45" stop-color="#2A1760"/><stop offset=".85" stop-color="#120830"/><stop offset="1" stop-color="#0A0520"/></radialGradient>
<radialGradient id="{uid}shade" cx="70%" cy="78%" r="70%"><stop offset="0" stop-color="#000" stop-opacity=".55"/><stop offset="1" stop-color="#000" stop-opacity="0"/></radialGradient>
<linearGradient id="{uid}ring" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="{MAGENTA}"/><stop offset=".5" stop-color="{ORANGE}"/><stop offset="1" stop-color="#FFD29A"/></linearGradient>
<filter id="{uid}glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="16"/></filter>
<filter id="{uid}glow2" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="40"/></filter>
<filter id="{uid}soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="3"/></filter>
<filter id="{uid}grain" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">
 <feTurbulence type="fractalNoise" baseFrequency=".012" numOctaves="3" seed="11" result="n"/>
 <feColorMatrix in="n" type="matrix" values="0 0 0 0 .55  0 0 0 0 .3  0 0 0 0 .9  0 0 0 .5 -.12" result="tint"/>
 <feComposite in="tint" in2="SourceAlpha" operator="in"/>
</filter>
"""


def emblem(uid, cx, cy, r, ship=True):
    """The planet with its grid, rim and orbit ring (and the ship on the ring)."""
    rnd = random.Random(7)
    out = []
    tilt = -16
    rx, ry = r * 1.42, r * 0.36
    ring_d = f"M{f(cx - rx)} {f(cy)} A{f(rx)} {f(ry)} 0 0 1 {f(cx + rx)} {f(cy)}"   # the back (upper) half
    ring_f = f"M{f(cx + rx)} {f(cy)} A{f(rx)} {f(ry)} 0 0 1 {f(cx - rx)} {f(cy)}"   # the front (lower) half
    rot = f'transform="rotate({tilt} {f(cx)} {f(cy)})"'
    # Behind the planet: a glow, and the back half of the ring.
    out.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r * 1.08)}" fill="{ORANGE}" opacity=".35" filter="url(#{uid}glow2)"/>')
    out.append(f'<path d="{ring_d}" {rot} fill="none" stroke="url(#{uid}ring)" stroke-width="{f(r * 0.035)}" opacity=".55"/>')
    # The planet.
    out.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="url(#{uid}planet)"/>')
    out.append(f'<clipPath id="{uid}clip"><circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}"/></clipPath>')
    g = [f'<g clip-path="url(#{uid}clip)">']
    g.append(f'<rect x="{f(cx - r)}" y="{f(cy - r)}" width="{f(2 * r)}" height="{f(2 * r)}" filter="url(#{uid}grain)" opacity=".7"/>')
    # The hologram grid: parallels as flattened ellipses, meridians as ellipse arcs.
    view = 22  # the camera looks down on the north a little
    k = math.sin(math.radians(view))
    for lat in range(-60, 90, 20):
        y = cy - r * math.sin(math.radians(lat)) * math.cos(math.radians(view))
        w = r * math.cos(math.radians(lat))
        h = w * k
        bright = lat == 0
        g.append(f'<ellipse cx="{f(cx)}" cy="{f(y)}" rx="{f(w)}" ry="{f(h)}" fill="none" stroke="{ORANGE}" '
                 f'stroke-width="{f(r * (0.012 if bright else 0.007))}" opacity="{0.75 if bright else 0.38}"/>')
    for lon in range(-75, 90, 25):
        w = r * math.sin(math.radians(lon))
        g.append(f'<ellipse cx="{f(cx)}" cy="{f(cy)}" rx="{f(abs(w))}" ry="{f(r)}" fill="none" stroke="{ORANGE}" '
                 f'stroke-width="{f(r * 0.007)}" opacity=".32"/>')
    # Glowing pads on the northern band: the colony.
    for lon, lat in ((-50, 30), (-22, 34), (5, 36), (32, 34), (58, 29), (-10, 16), (22, 17)):
        x = cx + r * math.cos(math.radians(lat)) * math.sin(math.radians(lon))
        y = cy - r * math.sin(math.radians(lat)) * math.cos(math.radians(view)) + r * math.cos(math.radians(lat)) * math.cos(math.radians(lon)) * k
        s = r * 0.016 * math.cos(math.radians(lon)) + r * 0.008
        g.append(f'<circle cx="{f(x)}" cy="{f(y)}" r="{f(s * 2.6)}" fill="{ORANGE}" opacity=".45" filter="url(#{uid}soft)"/>'
                 f'<circle cx="{f(x)}" cy="{f(y)}" r="{f(s)}" fill="#FFE9CF"/>')
    g.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="url(#{uid}shade)"/>')
    g.append("</g>")
    out.extend(g)
    # The neon rim.
    out.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="none" stroke="{ORANGE}" stroke-width="{f(r * 0.05)}" opacity=".6" filter="url(#{uid}glow)"/>')
    out.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="none" stroke="{ORANGE}" stroke-width="{f(r * 0.016)}"/>')
    # In front: the ring's front half, glowing.
    out.append(f'<path d="{ring_f}" {rot} fill="none" stroke="url(#{uid}ring)" stroke-width="{f(r * 0.07)}" opacity=".6" filter="url(#{uid}glow)"/>')
    out.append(f'<path d="{ring_f}" {rot} fill="none" stroke="url(#{uid}ring)" stroke-width="{f(r * 0.03)}"/>')
    def on_ring(deg):
        """A point on the ring (0 = its right end, 90 = the bottom), tilted like the ring."""
        t = math.radians(deg)
        px, py = cx + rx * math.cos(t), cy + ry * math.sin(t)
        a = math.radians(tilt)
        return (cx + (px - cx) * math.cos(a) - (py - cy) * math.sin(a),
                cy + (px - cx) * math.sin(a) + (py - cy) * math.cos(a))

    if ship:
        at = 36  # degrees round the ring's front half
        sx, sy = on_ring(at)
        # The trail: the stretch of ring behind the ship, glowing and fading away.
        pts_trail = [on_ring(at + d) for d in range(0, 97, 3)]
        # One soft glow along the whole stretch, fading with distance from the ship...
        (tx0, ty0), (tx1, ty1) = pts_trail[0], pts_trail[-1]
        out.append(f'<linearGradient id="{uid}trailglow" gradientUnits="userSpaceOnUse" x1="{f(tx0)}" y1="{f(ty0)}" '
                   f'x2="{f(tx1)}" y2="{f(ty1)}"><stop offset="0" stop-color="{ORANGE}" stop-opacity=".7"/>'
                   f'<stop offset="1" stop-color="{ORANGE}" stop-opacity="0"/></linearGradient>')
        path = "M" + " L".join(f"{f(x)} {f(y)}" for x, y in pts_trail)
        out.append(f'<path d="{path}" fill="none" stroke="url(#{uid}trailglow)" stroke-width="{f(r * 0.13)}" '
                   f'stroke-linecap="round" stroke-linejoin="round" filter="url(#{uid}glow)"/>')
        # ...and a white-hot core that thins and fades behind it.
        for i in range(len(pts_trail) - 1):
            (x0, y0), (x1, y1) = pts_trail[i], pts_trail[i + 1]
            fade = 1 - i / (len(pts_trail) - 1)
            out.append(f'<line x1="{f(x0)}" y1="{f(y0)}" x2="{f(x1)}" y2="{f(y1)}" stroke="#FFF1DC" '
                       f'stroke-width="{f(r * (0.008 + 0.03 * fade))}" stroke-linecap="round" opacity="{0.95 * fade:.2f}"/>')
        # Heading: along the ring towards its right end.
        ax, ay = on_ring(at - 2)
        heading = math.degrees(math.atan2(ay - sy, ax - sx)) + 90  # the art's nose points up
        size = r * 0.7
        out.append(f'<g transform="translate({f(sx)} {f(sy)}) rotate({f(heading)})">'
                   f'<ellipse cx="0" cy="{f(size * 0.36)}" rx="{f(size * 0.22)}" ry="{f(size * 0.12)}" fill="{ORANGE}" opacity=".75" filter="url(#{uid}glow)"/>'
                   f'<image href="data:image/png;base64,{SHIP}" x="{f(-size / 2)}" y="{f(-size / 2)}" width="{f(size)}" height="{f(size)}"/>'
                   f'</g>')
    # Sparkles on the ring.
    for tt, sz in ((200, 0.05), (250, 0.03)):
        sx, sy = on_ring(tt)
        z = r * sz
        out.append(f'<path d="M{f(sx)} {f(sy - z)} L{f(sx + z * .2)} {f(sy - z * .2)} L{f(sx + z)} {f(sy)} L{f(sx + z * .2)} {f(sy + z * .2)} '
                   f'L{f(sx)} {f(sy + z)} L{f(sx - z * .2)} {f(sy + z * .2)} L{f(sx - z)} {f(sy)} L{f(sx - z * .2)} {f(sy - z * .2)}Z" fill="#FFF3E0"/>')
    return "".join(out)


def stars(uid, w, h, n, seed):
    rnd = random.Random(seed)
    out = []
    for _ in range(n):
        x, y = rnd.uniform(0, w), rnd.uniform(0, h)
        s = rnd.choice((0.8, 1.1, 1.4, 2.0, 2.8))
        o = rnd.uniform(0.25, 0.9)
        out.append(f'<circle cx="{f(x)}" cy="{f(y)}" r="{f(s)}" fill="#FFFFFF" opacity="{o:.2f}"/>')
    return "".join(out)


def trail_gradient(uid):
    return (f'<linearGradient id="{uid}trailv" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFF1DC" stop-opacity=".95"/>'
            f'<stop offset=".25" stop-color="{ORANGE}" stop-opacity=".8"/><stop offset="1" stop-color="{MAGENTA}" stop-opacity="0"/></linearGradient>')


def icon():
    uid = "i"
    body = [f'<rect width="1024" height="1024" fill="url(#{uid}space)"/>',
            f'<ellipse cx="820" cy="170" rx="300" ry="190" fill="{MAGENTA}" opacity=".16" filter="url(#{uid}glow2)"/>',
            f'<ellipse cx="160" cy="860" rx="320" ry="200" fill="{ORANGE}" opacity=".12" filter="url(#{uid}glow2)"/>',
            stars(uid, 1024, 1024, 90, 3),
            emblem(uid, 512, 540, 318)]
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">'
            f'<defs>{defs(uid)}{trail_gradient(uid)}</defs>{"".join(body)}</svg>')


def launch():
    uid = "l"
    font = (f'<style>@font-face{{font-family:"Orbitron";src:url(data:font/ttf;base64,{ORBITRON}) format("truetype");'
            f'font-weight:400 900}}</style>')
    body = [emblem(uid, 600, 470, 250),
            f'<text x="600" y="930" text-anchor="middle" font-family="Orbitron" font-weight="800" font-size="92" '
            f'letter-spacing="14" fill="{ORANGE}" filter="url(#{uid}glow)" opacity=".7">GALAXY ROYALE</text>',
            f'<text x="600" y="930" text-anchor="middle" font-family="Orbitron" font-weight="800" font-size="92" '
            f'letter-spacing="14" fill="{ORANGE}">GALAXY ROYALE</text>',
            f'<line x1="330" y1="985" x2="870" y2="985" stroke="{ORANGE}" stroke-width="3" opacity=".6"/>',
            f'<text x="600" y="1045" text-anchor="middle" font-family="Orbitron" font-weight="600" font-size="34" '
            f'letter-spacing="10" fill="{MAGENTA}">A LIVING GALAXY OF 249 RIVALS</text>']
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="1200" viewBox="0 0 1200 1200">'
            f'<defs>{font}{defs(uid)}{trail_gradient(uid)}</defs>{"".join(body)}</svg>')


open(os.path.join(OUT, "icon.svg"), "w").write(icon())
open(os.path.join(OUT, "launch.svg"), "w").write(launch())
open(os.path.join(OUT, "sizes.txt"), "w").write("icon 1024\nlaunch 1200\n")
print("wrote icon.svg, launch.svg")
