#!/usr/bin/env python3
"""The ship art (2026-09-29): every hull as an SVG illustration — lit metal
(SVG lighting filters), segmented hulls, detailed engines and turrets, the
role colour in the trim. Top-down, nose up, 512 x 512, transparent. Also
writes sheet.html, a contact sheet of them all.

render_ships.sh runs this and renders the SVGs to the game's
GalaxyRoyale/Assets/Resources/Ships/<Hull>.png (ShipArt.Photo loads them).

usage: ship_art.py <out dir> [hull ...]
"""
import math
import os
import random
import sys

OUT = sys.argv[1]
ONLY = set(sys.argv[2:])
os.makedirs(OUT, exist_ok=True)

# Len, Body, Span, WingAt, Sweep, Chord, Nose, Engines, extras
SHAPES = {
    "Fighter":   (0.60, 0.070, 0.30, 0.55, 0.16, 0.22, 0.30, 1, {"canopy"}),
    "Bomber":    (0.64, 0.100, 0.40, 0.40, 0.10, 0.34, 0.18, 2, {"canopy", "bombs"}),
    "Cruiser":   (0.80, 0.120, 0.24, 0.50, 0.06, 0.30, 0.20, 3, {"pods", "bridge"}),
    "Talon":     (0.72, 0.070, 0.36, 0.62, -0.16, 0.16, 0.34, 1, {"canopy"}),
    "Sentinel":  (0.62, 0.110, 0.26, 0.45, 0.04, 0.30, 0.16, 2, {"ring", "canopy"}),
    "Harrier":   (0.82, 0.060, 0.22, 0.62, 0.20, 0.14, 0.36, 2, {"canopy", "canards"}),
    "Vanguard":  (0.86, 0.160, 0.30, 0.45, 0.08, 0.36, 0.22, 3, {"pods", "turrets", "bridge", "sponsons"}),
    "Rampart":   (0.76, 0.200, 0.30, 0.38, 0.02, 0.44, 0.14, 3, {"ring", "turrets", "bridge", "sponsons"}),
    "Corsair":   (0.88, 0.130, 0.34, 0.50, 0.20, 0.26, 0.28, 2, {"turrets", "bridge", "canards"}),
    "Lancer":    (0.80, 0.080, 0.20, 0.30, 0.02, 0.50, 0.30, 1, {"pods", "missiles", "canopy"}),
    "Bulwark":   (0.70, 0.150, 0.26, 0.40, 0.04, 0.40, 0.14, 2, {"ring", "pd", "bridge"}),
    "Javelin":   (0.86, 0.070, 0.26, 0.58, 0.20, 0.18, 0.38, 2, {"missiles", "canopy", "canards"}),
    "Behemoth":  (0.90, 0.220, 0.36, 0.40, 0.06, 0.40, 0.12, 4, {"pods", "turrets", "big", "bridge", "sponsons"}),
    "Leviathan": (0.90, 0.240, 0.30, 0.30, 0.02, 0.56, 0.10, 4, {"ring", "turrets", "big", "bridge", "sponsons"}),
    "Nomad":     (0.92, 0.180, 0.38, 0.52, 0.20, 0.30, 0.24, 3, {"turrets", "big", "bridge", "canards"}),
    "Reaper":    (0.92, 0.140, 0.40, 0.55, 0.24, 0.26, 0.34, 3, {"pods", "turrets", "bridge"}),
    "Warden":    (0.86, 0.180, 0.32, 0.42, 0.06, 0.40, 0.18, 3, {"ring", "turrets", "bridge", "sponsons"}),
    "Wraith":    (0.94, 0.120, 0.42, 0.58, 0.28, 0.22, 0.40, 2, {"stealth", "canopy"}),
    "Hauler":    (0.66, 0.160, 0.20, 0.55, 0.00, 0.24, 0.10, 2, {"containers", "bridge"}),
    "Atlas":     (0.88, 0.200, 0.26, 0.60, 0.00, 0.22, 0.10, 3, {"containers", "big", "bridge"}),
    "Aegis":     (0.66, 0.120, 0.24, 0.48, 0.08, 0.28, 0.18, 2, {"ring", "field", "bridge"}),
    "Scavenger": (0.70, 0.130, 0.22, 0.55, 0.06, 0.24, 0.12, 2, {"claws", "bridge"}),
    "Probe":     (0.50, 0.000, 0.00, 0.00, 0.00, 0.00, 0.00, 0, {"orb"}),
}
CLASS = {  # hull silhouette family
    "Vanguard": "stepped", "Rampart": "stepped", "Corsair": "stepped",
    "Behemoth": "wedge", "Leviathan": "wedge", "Nomad": "wedge", "Reaper": "wedge", "Warden": "wedge", "Wraith": "wedge",
}
for _n in ("Fighter", "Bomber", "Cruiser", "Talon", "Sentinel", "Harrier", "Lancer", "Bulwark", "Javelin",
           "Hauler", "Atlas", "Aegis", "Scavenger", "Probe"):
    CLASS[_n] = "sleek"
ROLE = {
    "Fighter": "Skirmisher", "Bomber": "Striker", "Cruiser": "Guardian", "Talon": "Striker", "Sentinel": "Guardian",
    "Harrier": "Skirmisher", "Vanguard": "Striker", "Rampart": "Guardian", "Corsair": "Skirmisher",
    "Lancer": "Striker", "Bulwark": "Guardian", "Javelin": "Skirmisher", "Behemoth": "Striker",
    "Leviathan": "Guardian", "Nomad": "Skirmisher", "Reaper": "Striker", "Warden": "Guardian",
    "Wraith": "Skirmisher", "Hauler": "Freighter", "Atlas": "Freighter", "Aegis": "Shield Frigate",
    "Scavenger": "Salvage", "Probe": "Recon",
}
ACCENT = {"Striker": "#FF5A2E", "Guardian": "#3FA8FF", "Skirmisher": "#2EF59A", "Freighter": "#FFB23F",
          "Shield Frigate": "#3DF5FF", "Salvage": "#E08A45", "Recon": "#D58CFF"}
ENGINE = {"Striker": "#FF9A3D", "Guardian": "#6FD0FF", "Skirmisher": "#6FFFC4", "Freighter": "#FFCF7A",
          "Shield Frigate": "#7FF6FF", "Salvage": "#FFB26B", "Recon": "#E3B8FF"}

CX, CY = 256.0, 246.0


def dark(hex_color, k=0.55):
    """A darker, slightly desaturated tone of a colour: painted armour."""
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    grey = (r + g + b) / 3
    mix = lambda c: int((c * 0.75 + grey * 0.25) * k)
    return f"#{mix(r):02X}{mix(g):02X}{mix(b):02X}"


def f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def pts(p):
    return " ".join(f"{f(x)},{f(y)}" for x, y in p)


def mirror(p):
    return [(2 * CX - x, y) for x, y in p]


def smooth_path(poly):
    """A closed path through the points with slightly rounded corners."""
    n = len(poly)
    d = []
    for i in range(n):
        p0, p1, p2 = poly[i - 1], poly[i], poly[(i + 1) % n]
        ax, ay = p1[0] + (p0[0] - p1[0]) * 0.18, p1[1] + (p0[1] - p1[1]) * 0.18
        bx, by = p1[0] + (p2[0] - p1[0]) * 0.18, p1[1] + (p2[1] - p1[1]) * 0.18
        d.append(("M" if i == 0 else "L") + f"{f(ax)} {f(ay)} Q{f(p1[0])} {f(p1[1])} {f(bx)} {f(by)}")
    return " ".join(d) + "Z"


def defs_for(uid, acc, eng, stealth):
    base = "#5B6680" if not stealth else "#262A38"
    return f"""
<filter id="{uid}metal" x="-10%" y="-10%" width="120%" height="120%" color-interpolation-filters="sRGB">
 <feGaussianBlur in="SourceAlpha" stdDeviation="2.6" result="bump"/>
 <feDiffuseLighting in="bump" surfaceScale="5" diffuseConstant="1.05" lighting-color="#FFFFFF" result="diff">
  <feDistantLight azimuth="235" elevation="52"/></feDiffuseLighting>
 <feSpecularLighting in="bump" surfaceScale="5" specularConstant="1.1" specularExponent="26" lighting-color="#FFE7D2" result="spec">
  <feDistantLight azimuth="235" elevation="40"/></feSpecularLighting>
 <feComposite in="spec" in2="SourceAlpha" operator="in" result="specIn"/>
 <feComposite in="SourceGraphic" in2="diff" operator="arithmetic" k1="1.25" k2="0" k3="0" k4="0" result="lit"/>
 <feTurbulence type="fractalNoise" baseFrequency=".9" numOctaves="2" seed="7" result="grain"/>
 <feColorMatrix in="grain" type="saturate" values="0" result="grainG"/>
 <feComposite in="grainG" in2="SourceAlpha" operator="in" result="grainIn"/>
 <feComposite in="lit" in2="grainIn" operator="arithmetic" k1="0" k2="1" k3=".07" k4="-.035" result="grainy"/>
 <feComposite in="grainy" in2="specIn" operator="arithmetic" k1="0" k2="1" k3=".55" k4="0" result="shiny"/>
 <feComposite in="shiny" in2="SourceAlpha" operator="in"/>
</filter>
<filter id="{uid}glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="5"/></filter>
<filter id="{uid}glow2" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="12"/></filter>
<filter id="{uid}soft" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="10"/></filter>
<filter id="{uid}ao" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="3.2"/></filter>
<linearGradient id="{uid}glass" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#B8F6FF"/><stop offset=".3" stop-color="#2A89AE"/><stop offset="1" stop-color="#061A28"/></linearGradient>
<radialGradient id="{uid}core"><stop offset="0" stop-color="#FFFFFF"/><stop offset=".35" stop-color="{eng}"/><stop offset="1" stop-color="{eng}" stop-opacity="0"/></radialGradient>
<linearGradient id="{uid}plume" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF" stop-opacity=".95"/><stop offset=".18" stop-color="{eng}" stop-opacity=".9"/><stop offset="1" stop-color="{eng}" stop-opacity="0"/></linearGradient>
<linearGradient id="{uid}trim" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="{acc}"/><stop offset="1" stop-color="{acc}" stop-opacity=".55"/></linearGradient>
""", base


def ship_svg(name):
    L, B, SP, WA, SW, CH, NO, ENG, EX = SHAPES[name]
    role = ROLE[name]
    acc, eng = ACCENT[role], ENGINE[role]
    uid = name.lower()
    rnd = random.Random(name + "v2")
    stealth = "stealth" in EX
    defs, base = defs_for(uid, acc, eng, stealth)
    if "orb" in EX:
        return probe_svg(uid, defs, acc, eng)

    # Every ship fills the frame: scale so the longest dimension is ~430 px.
    span_w = max(SP, B + 0.02) * 2 + (0.1 if "ring" in EX else 0)
    S = 430.0 / max(L, span_w * 0.95)
    top = CY - L * S / 2
    bot = CY + L * S / 2
    W = max(B * S, 16)
    nose_end = top + NO * L * S
    big = "big" in EX

    under, parts, over = [], [], []

    # --- hull profile: nose, shoulder, waist and engine block (per class) ---
    cls = CLASS[name]
    if cls == "wedge":      # destroyers, dreadnoughts: a long armoured arrowhead
        prof = [(0.0, 0.0), (0.18, 0.32), (0.4, 0.62), (0.62, 0.9), (0.8, 1.08), (0.9, 1.12), (0.96, 1.0), (1.0, 0.86)]
    elif cls == "stepped":  # battleships: a narrow prow, a wide gun deck, a stepped stern
        prof = [(0.0, 0.0), (NO * 0.5, 0.34), (NO, 0.62), (NO + 0.04, 0.8), (0.34, 0.84), (0.38, 1.04),
                (0.7, 1.04), (0.74, 0.9), (0.9, 0.9), (1.0, 0.78)]
    else:
        prof = [(0.0, 0.0), (NO * 0.45, 0.42), (NO, 0.86), (NO + 0.08, 1.0), (0.46, 1.06 if big else 1.0),
                (0.62, 0.96), (0.8, 0.9), (0.92, 0.94), (1.0, 0.8)]
    right = [(CX + W * w, top + t * L * S) for t, w in prof]
    hull = right + list(reversed(mirror(right)))[1:-1]

    # --- wings ---
    has_wings = SP > B + 0.01
    wroot = top + WA * L * S
    wchord = CH * L * S
    tip_x, tip_y = CX + SP * S, wroot + SW * L * S
    tip_chord = max(wchord * 0.3, 12)
    wing = [(CX + W * 0.8, wroot), (tip_x - 4, tip_y), (tip_x, tip_y + 6), (tip_x, tip_y + tip_chord),
            (CX + W * 0.8, wroot + wchord)]
    inner = [(CX + W * 0.9 + 6, wroot + wchord * 0.2), (tip_x - 14, tip_y + tip_chord * 0.25),
             (tip_x - 14, tip_y + tip_chord * 0.75), (CX + W * 0.9 + 6, wroot + wchord * 0.8)]

    # --- drop shadow ---
    shade_parts = [smooth_path(hull)]
    if has_wings:
        shade_parts += [smooth_path(wing), smooth_path(mirror(wing))]
    under.append("".join(f'<path d="{d}" transform="translate(16 22)" fill="#000" opacity=".5" filter="url(#{uid}soft)"/>'
                         for d in shade_parts))

    # --- engine plumes ---
    nozzles = []
    nr = max(8, min(20, W * 0.36 / max(1, ENG ** 0.45)))
    if ENG:
        width_e = W * 1.5 if ENG > 1 else 0
        for i in range(ENG):
            nozzles.append(CX + (i - (ENG - 1) / 2) * (width_e / max(1, ENG - 1)) if ENG > 1 else CX)
        for ex in nozzles:
            under.append(f'<ellipse cx="{f(ex)}" cy="{f(bot + nr * 5)}" rx="{f(nr * 1.9)}" ry="{f(nr * 6.5)}" fill="{eng}" opacity=".35" filter="url(#{uid}glow2)"/>'
                         f'<path d="M{f(ex - nr * 0.95)} {f(bot)} Q{f(ex)} {f(bot + nr * 11)} {f(ex + nr * 0.95)} {f(bot)}Z" fill="url(#{uid}plume)" filter="url(#{uid}glow)"/>')

    # --- wings (lit metal) ---
    if has_wings:
        for w, iw, side in ((wing, inner, 1), (mirror(wing), mirror(inner), -1)):
            parts.append(f'<path d="{smooth_path(w)}" fill="{base}" filter="url(#{uid}metal)"/>')
            parts.append(f'<path d="{smooth_path(iw)}" fill="#000" opacity=".22"/>')
            # trim band near the tip + panel seams + hardpoint lights
            k0, k1 = 0.7, 0.82
            def lerp(a, b, t):
                return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
            r0, t0, t1, r1 = w[0], w[1], w[3], w[4]
            band = [lerp(r0, t0, k0), lerp(r0, t0, k1), lerp(r1, t1, k1), lerp(r1, t1, k0)]
            parts.append(f'<polygon points="{pts(band)}" fill="{acc}" opacity=".95"/>')
            for kk in (0.28, 0.5):
                a, b = lerp(r0, t0, kk), lerp(r1, t1, kk)
                parts.append(f'<path d="M{f(a[0])} {f(a[1])} L{f(b[0])} {f(b[1])}" stroke="#0A0C12" stroke-width="1.3" opacity=".6"/>'
                             f'<path d="M{f(a[0] + side)} {f(a[1] + 1)} L{f(b[0] + side)} {f(b[1] + 1)}" stroke="#FFFFFF" stroke-width=".8" opacity=".25"/>')
            light = "#3DFFA0" if side == 1 else "#FF4D6D"
            tipc = (w[3][0] - side * 3, (w[2][1] + w[3][1]) / 2)
            parts.append(f'<circle cx="{f(tipc[0])}" cy="{f(tipc[1])}" r="10" fill="{light}" opacity=".45" filter="url(#{uid}glow)"/>'
                         f'<circle cx="{f(tipc[0])}" cy="{f(tipc[1])}" r="3.4" fill="#FFFFFF"/>')
        if "canards" in EX:
            cr = top + (NO + 0.06) * L * S
            can = [(CX + W * 0.8, cr), (CX + W * 0.8 + SP * S * 0.35, cr + 14), (CX + W * 0.8 + SP * S * 0.33, cr + 26), (CX + W * 0.8, cr + 22)]
            for c in (can, mirror(can)):
                parts.append(f'<path d="{smooth_path(c)}" fill="{base}" filter="url(#{uid}metal)"/>')

    # --- weapon pods ---
    if "pods" in EX and has_wings:
        for side in (1, -1):
            px = CX + side * (W + (SP * S - W) * 0.5)
            py = wroot + (tip_y - wroot) * 0.5
            pl, pw = max(wchord * 1.05, 46), max(11, W * 0.26)
            parts.append(f'<rect x="{f(px - pw / 2)}" y="{f(py - pl * 0.4)}" width="{f(pw)}" height="{f(pl)}" rx="{f(pw / 2)}" fill="{base}" filter="url(#{uid}metal)"/>'
                         f'<rect x="{f(px - 2)}" y="{f(py - pl * 0.4 - 18)}" width="4" height="20" rx="1" fill="#1B1F2B"/>'
                         f'<rect x="{f(px - pw / 2 + 2)}" y="{f(py + pl * 0.25)}" width="{f(pw - 4)}" height="4" fill="{acc}"/>')

    # --- sponsons (side armour blocks) ---
    if "sponsons" in EX:
        for side in (1, -1):
            sx = CX + side * W * 1.0
            sy0, sy1 = top + L * S * 0.5, top + L * S * 0.8
            blk = [(sx, sy0), (sx + side * W * 0.34, sy0 + 12), (sx + side * W * 0.34, sy1 - 8), (sx, sy1)]
            parts.append(f'<path d="{smooth_path(blk)}" fill="{base}" filter="url(#{uid}metal)"/>')

    # --- the hull (lit metal), ambient occlusion where the wings meet it ---
    if has_wings:
        parts.append(f'<rect x="{f(CX - W * 1.05)}" y="{f(wroot)}" width="{f(W * 2.1)}" height="{f(wchord)}" fill="#000" opacity=".35" filter="url(#{uid}ao)"/>')
    parts.append(f'<path d="{smooth_path(hull)}" fill="{base}" filter="url(#{uid}metal)"/>')

    # --- paint: a role-tinted armour band and a dark nose cap ---
    band_y0, band_y1 = top + L * S * 0.64, top + L * S * 0.82
    clip_open = f'<clipPath id="{uid}hc"><path d="{smooth_path(hull)}"/></clipPath><g clip-path="url(#{uid}hc)">'
    over.append(clip_open +
                f'<rect x="{f(CX - W * 1.3)}" y="{f(band_y0)}" width="{f(W * 2.6)}" height="{f(band_y1 - band_y0)}" fill="{dark(acc)}" filter="url(#{uid}metal)"/>'
                f'<rect x="{f(CX - W * 1.3)}" y="{f(band_y0 + 3)}" width="{f(W * 2.6)}" height="2" fill="{acc}"/>'
                f'<rect x="{f(CX - W * 1.3)}" y="{f(top - 2)}" width="{f(W * 2.6)}" height="{f((nose_end - top) * 0.42)}" fill="#0A0C12" opacity=".35"/>')
    over_clip_start = len(over)
    # --- raised superstructure deck for capital ships ---
    if cls in ("wedge", "stepped") or big:
        deck = [(CX - W * 0.46, top + L * S * 0.42), (CX + W * 0.46, top + L * S * 0.42),
                (CX + W * 0.56, top + L * S * 0.78), (CX - W * 0.56, top + L * S * 0.78)]
        over.append(f'<path d="{smooth_path([(x + 4, y + 6) for x, y in deck])}" fill="#000" opacity=".45" filter="url(#{uid}ao)"/>'
                    f'<path d="{smooth_path(deck)}" fill="{base}" filter="url(#{uid}metal)"/>')
        # hangar bay: a dark mouth with landing lights
        hb_y = top + L * S * 0.86
        over.append(f'<rect x="{f(CX - W * 0.34)}" y="{f(hb_y)}" width="{f(W * 0.68)}" height="{f(L * S * 0.05)}" rx="3" fill="#07080D"/>'
                    + "".join(f'<rect x="{f(CX - W * 0.3 + i * W * 0.12)}" y="{f(hb_y + 2)}" width="3" height="2" fill="{eng}"/>' for i in range(6)))
        # rows of lit windows along the deck edges
        for i in range(10):
            y = top + L * S * (0.45 + i * 0.03)
            for side in (-1, 1):
                over.append(f'<rect x="{f(CX + side * W * 0.5 - 1.5)}" y="{f(y)}" width="3" height="1.8" fill="#FFE9B0" opacity=".85"/>')

    # --- hull detail: plates, seams, vents, spine, lights ---
    segs = [top + L * S * t for t in (NO + 0.1, 0.36, 0.52, 0.68, 0.84)]
    for y in segs:
        if y < nose_end + 4 or y > bot - 12:
            continue
        over.append(f'<path d="M{f(CX - W * 0.9)} {f(y)} L{f(CX + W * 0.9)} {f(y)}" stroke="#0A0C12" stroke-width="1.6" opacity=".55"/>'
                    f'<path d="M{f(CX - W * 0.9)} {f(y + 1.5)} L{f(CX + W * 0.9)} {f(y + 1.5)}" stroke="#FFFFFF" stroke-width=".9" opacity=".22"/>')
    # inset plates on the flanks
    for i in range(len(segs) - 1):
        y0, y1 = segs[i] + 5, segs[i + 1] - 5
        if y1 - y0 < 12 or y0 < nose_end:
            continue
        for side in (-1, 1):
            x0 = CX + side * W * 0.34
            w_ = W * 0.42
            over.append(f'<rect x="{f(x0 - w_ / 2 if side > 0 else x0 - w_ / 2)}" y="{f(y0)}" width="{f(w_)}" height="{f(y1 - y0)}" rx="2" '
                        f'fill="#000" opacity="{rnd.choice((".10", ".16", ".05"))}"/>')
    # vents
    for _ in range(int(4 + L * 10)):
        gy = rnd.uniform(nose_end + 12, bot - 26)
        side = rnd.choice((-1, 1))
        gx = CX + side * rnd.uniform(W * 0.45, W * 0.75)
        for k in range(3):
            over.append(f'<rect x="{f(gx - 5)}" y="{f(gy + k * 3.2)}" width="10" height="1.6" fill="#0A0C12" opacity=".6"/>')
    # spine
    over.append(f'<rect x="{f(CX - W * 0.1)}" y="{f(nose_end + 6)}" width="{f(W * 0.2)}" height="{f(bot - nose_end - 26)}" rx="{f(W * 0.1)}" fill="#000" opacity=".18"/>')
    # accent chevron + hull number
    cv = nose_end + (bot - nose_end) * 0.18
    over.append(f'<path d="M{f(CX - W * 0.85)} {f(cv + W * 0.28)} L{f(CX)} {f(cv)} L{f(CX + W * 0.85)} {f(cv + W * 0.28)}" '
                f'stroke="url(#{uid}trim)" stroke-width="{f(max(3.5, W * 0.13))}" fill="none" stroke-linejoin="round"/>')
    # running lights down the flanks for big hulls
    if big or L > 0.8:
        for i in range(5):
            y = nose_end + 20 + i * (bot - nose_end - 50) / 4
            for side in (-1, 1):
                over.append(f'<circle cx="{f(CX + side * W * 0.86)}" cy="{f(y)}" r="1.8" fill="#FFE9B0"/>')

    over.append('</g>')  # end of the hull-clipped details

    # --- cargo containers ---
    if "containers" in EX:
        rows = int((bot - nose_end - 50) / 28)
        for r in range(rows):
            y = nose_end + 26 + r * 28
            for side in (-1, 1):
                x = CX + side * W * 0.5
                over.append(f'<rect x="{f(x - W * 0.38)}" y="{f(y)}" width="{f(W * 0.76)}" height="22" rx="2.5" fill="{acc}" filter="url(#{uid}metal)"/>'
                            f'<path d="M{f(x - W * 0.3)} {f(y + 7)}h{f(W * 0.6)}M{f(x - W * 0.3)} {f(y + 14)}h{f(W * 0.6)}" stroke="#3A2408" stroke-width="1.1" opacity=".55"/>')

    # --- missiles / point defence ---
    if "missiles" in EX:
        for side in (-1, 1):
            for i in range(5):
                y = nose_end + 14 + i * 13
                over.append(f'<rect x="{f(CX + side * W * 0.58 - 3.5)}" y="{f(y)}" width="7" height="11" rx="3" fill="#E8EEF8"/>'
                            f'<rect x="{f(CX + side * W * 0.58 - 3.5)}" y="{f(y)}" width="7" height="4" rx="2" fill="{acc}"/>')
    if "pd" in EX:
        for side in (-1, 1):
            for y in (nose_end + 26, bot - 50):
                over.append(f'<circle cx="{f(CX + side * W * 0.62)}" cy="{f(y)}" r="7" fill="#20242F"/>'
                            f'<circle cx="{f(CX + side * W * 0.62)}" cy="{f(y)}" r="7" fill="none" stroke="{acc}" stroke-width="2"/>'
                            f'<rect x="{f(CX + side * W * 0.62 - 1.2)}" y="{f(y - 13)}" width="2.4" height="9" fill="#20242F"/>')

    # --- turrets ---
    if "turrets" in EX:
        count = 3 if big else 2
        for i in range(count):
            ty = nose_end + (bot - nose_end) * (0.34 + i * 0.19)
            tr = max(10, W * 0.27)
            over.append(f'<circle cx="{f(CX + 3)}" cy="{f(ty + 4)}" r="{f(tr * 1.08)}" fill="#000" opacity=".4" filter="url(#{uid}ao)"/>'
                        f'<rect x="{f(CX - tr * 0.45 - 2.3)}" y="{f(ty - tr * 2.2)}" width="4.6" height="{f(tr * 1.9)}" rx="1.5" fill="#1B1F2B"/>'
                        f'<rect x="{f(CX + tr * 0.45 - 2.3)}" y="{f(ty - tr * 2.2)}" width="4.6" height="{f(tr * 1.9)}" rx="1.5" fill="#1B1F2B"/>'
                        f'<circle cx="{f(CX)}" cy="{f(ty)}" r="{f(tr)}" fill="{base}" filter="url(#{uid}metal)"/>'
                        f'<circle cx="{f(CX)}" cy="{f(ty)}" r="{f(tr * 0.36)}" fill="{acc}"/>')

    # --- shield ring / field ---
    if "ring" in EX:
        ry = top + L * S * 0.5
        rx = max(W * 1.9, SP * S * 0.84)
        over.append(f'<ellipse cx="{f(CX)}" cy="{f(ry)}" rx="{f(rx)}" ry="{f(rx * 0.28)}" fill="none" stroke="{acc}" stroke-width="12" opacity=".22" filter="url(#{uid}glow)"/>'
                    f'<ellipse cx="{f(CX)}" cy="{f(ry)}" rx="{f(rx)}" ry="{f(rx * 0.28)}" fill="none" stroke="{acc}" stroke-width="3" opacity=".9"/>'
                    f'<ellipse cx="{f(CX)}" cy="{f(ry)}" rx="{f(rx)}" ry="{f(rx * 0.28)}" fill="none" stroke="#FFFFFF" stroke-width="1" opacity=".6" stroke-dasharray="30 18"/>')
    if "field" in EX:
        under.insert(0, f'<circle cx="{f(CX)}" cy="{f(CY)}" r="226" fill="{acc}" opacity=".07"/>'
                        f'<circle cx="{f(CX)}" cy="{f(CY)}" r="226" fill="none" stroke="{acc}" stroke-width="2" stroke-dasharray="8 8" opacity=".55"/>')

    # --- salvage claws ---
    if "claws" in EX:
        for side in (-1, 1):
            x0 = CX + side * W * 0.72
            d = f"M{f(x0)} {f(nose_end + 8)} Q{f(x0 + side * 26)} {f(top - 6)} {f(x0 - side * 8)} {f(top - 24)}"
            over.append(f'<path d="{d}" stroke="#262B38" stroke-width="10" fill="none" stroke-linecap="round"/>'
                        f'<path d="{d}" stroke="{acc}" stroke-width="3" fill="none" stroke-linecap="round"/>')

    # --- cockpit or bridge ---
    if "canopy" in EX:
        ck_y = top + (nose_end - top) * 0.66
        ck_w, ck_h = max(7, W * 0.42), max(18, (nose_end - top) * 0.6)
        over.append(f'<ellipse cx="{f(CX)}" cy="{f(ck_y)}" rx="{f(ck_w)}" ry="{f(ck_h / 2)}" fill="url(#{uid}glass)" stroke="#0A0C12" stroke-width="2"/>'
                    f'<ellipse cx="{f(CX - ck_w * 0.32)}" cy="{f(ck_y - ck_h * 0.2)}" rx="{f(ck_w * 0.2)}" ry="{f(ck_h * 0.22)}" fill="#FFFFFF" opacity=".8"/>')
    else:
        by = nose_end + (bot - nose_end) * 0.06
        bw, bh = max(12, W * 0.62), max(16, W * 0.55)
        over.append(f'<rect x="{f(CX - bw / 2 + 3)}" y="{f(by + 4)}" width="{f(bw)}" height="{f(bh)}" rx="4" fill="#000" opacity=".45" filter="url(#{uid}ao)"/>'
                    f'<rect x="{f(CX - bw / 2)}" y="{f(by)}" width="{f(bw)}" height="{f(bh)}" rx="4" fill="{base}" filter="url(#{uid}metal)"/>'
                    f'<rect x="{f(CX - bw * 0.36)}" y="{f(by + 3)}" width="{f(bw * 0.72)}" height="{f(max(4, bh * 0.22))}" rx="2" fill="url(#{uid}glass)"/>')
        for i in range(max(2, int(bw / 7))):
            over.append(f'<rect x="{f(CX - bw * 0.36 + 3 + i * 6.5)}" y="{f(by + bh * 0.62)}" width="3" height="2" fill="#FFE9B0" opacity=".9"/>')

    # --- engine housings and cores ---
    for ex in nozzles:
        over.append(f'<rect x="{f(ex - nr * 1.05)}" y="{f(bot - nr * 1.2)}" width="{f(nr * 2.1)}" height="{f(nr * 1.5)}" rx="{f(nr * 0.45)}" fill="#3B4254" filter="url(#{uid}metal)"/>'
                    f'<ellipse cx="{f(ex)}" cy="{f(bot + nr * 0.3)}" rx="{f(nr * 1.9)}" ry="{f(nr * 1.3)}" fill="url(#{uid}core)"/>'
                    f'<ellipse cx="{f(ex)}" cy="{f(bot + nr * 0.12)}" rx="{f(nr * 0.7)}" ry="{f(nr * 0.4)}" fill="#FFFFFF"/>')

    # --- rim lights: orange from the right, cool cyan from the left (the hologram look) ---
    over.append(f'<path d="M{pts(right[1:-1])}" fill="none" stroke="#FF9A3D" stroke-width="2.4" opacity=".7" stroke-linecap="round"/>'
                .replace("M", "M", 1).replace('d="M', 'd="M', 1))
    left = mirror(right)
    over.append(f'<polyline points="{pts(left[1:-1])}" fill="none" stroke="#6FE3FF" stroke-width="1.6" opacity=".45"/>')
    if has_wings:
        over.append(f'<polyline points="{pts([wing[1], wing[2], wing[3]])}" fill="none" stroke="#FF9A3D" stroke-width="2.2" opacity=".65"/>')

    return doc(defs, under + parts + over)


def probe_svg(uid, defs, acc, eng):
    items = [f'<circle cx="{CX + 14}" cy="{CY + 20}" r="80" fill="#000" opacity=".45" filter="url(#{uid}soft)"/>']
    for a in (35, 145, 270):
        r = math.radians(a)
        x1, y1 = CX + math.cos(r) * 175, CY + math.sin(r) * 175
        items.append(f'<path d="M{CX} {CY} L{f(x1)} {f(y1)}" stroke="#5B6680" stroke-width="9" stroke-linecap="round" filter="url(#{uid}metal)"/>'
                     f'<circle cx="{f(x1)}" cy="{f(y1)}" r="30" fill="{acc}" opacity=".35" filter="url(#{uid}glow)"/>'
                     f'<circle cx="{f(x1)}" cy="{f(y1)}" r="11" fill="{acc}"/><circle cx="{f(x1)}" cy="{f(y1)}" r="4" fill="#fff"/>')
    items.append(f'<circle cx="{CX}" cy="{CY}" r="76" fill="#5B6680" filter="url(#{uid}metal)"/>'
                 f'<circle cx="{CX}" cy="{CY}" r="34" fill="url(#{uid}glass)" stroke="#0A0C12" stroke-width="3"/>'
                 f'<circle cx="{CX}" cy="{CY}" r="13" fill="{acc}"/><circle cx="{CX - 10}" cy="{CY - 12}" r="7" fill="#fff" opacity=".85"/>'
                 f'<circle cx="{CX}" cy="{CY}" r="76" fill="none" stroke="#FF9A3D" stroke-width="2.4" stroke-dasharray="60 420" opacity=".8"/>')
    return doc(defs, items)


def doc(defs, items):
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">'
            f'<defs>{defs}</defs>{"".join(items)}</svg>')


names = [n for n in SHAPES if not ONLY or n in ONLY]
for n in names:
    open(os.path.join(OUT, f"{n}.svg"), "w").write(ship_svg(n))
cells = "".join(f'<div class="c"><img src="{n}.svg"><b>{n}</b><i>{ROLE[n]}</i></div>' for n in names)
open(os.path.join(OUT, "sheet.html"), "w").write(f"""<!doctype html><html><head><meta charset="utf-8"><style>
body{{margin:0;background:radial-gradient(ellipse at 50% 0%,#1a0e3d,#05030E 70%);font-family:system-ui;color:#E8F7FF}}
.g{{display:grid;grid-template-columns:repeat(6,1fr);gap:10px;padding:16px}}
.c{{display:flex;flex-direction:column;align-items:center;background:rgba(30,16,70,.35);border:1px solid rgba(255,154,61,.3);padding:6px}}
.c img{{width:100%;aspect-ratio:1}}.c b{{font-size:13px;margin-top:2px}}.c i{{font-size:11px;color:#9FB3D9;font-style:normal}}
</style></head><body><div class="g">{cells}</div></body></html>""")
print("wrote", len(names))
