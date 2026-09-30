#!/usr/bin/env python3
"""App Store screenshots with captions (2026-09-29): each raw 6.9" capture
(1320 x 2868, from scripts/screenshots.sh) set in the Neon Hologram style —
a headline and a line under it in the game's own fonts, the screen below in a
glowing orange frame — and saved as JPEG (App Store Connect takes no alpha).

usage: scripts/store_frames.py <out dir> <capture.png>=<caption key> ...
  caption keys: colony, wilds, replay, port, galaxy, report, core, galaxyfull, frontier
  e.g. scripts/store_frames.py store/screenshots raw/base.png=colony raw/wilds.png=wilds

Needs python3, Google Chrome and sips (macOS).
"""
import base64
import os
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, "..", "GalaxyRoyale", "Assets", "Resources", "Fonts")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
W, H = 1320, 2868

CAPTIONS = {
    "colony": ("BUILD YOUR COLONY", "Every building on a living globe you can spin"),
    "wilds": ("CHART THE WILDS", "Survey the fog for deposits, caches and relics"),
    "replay": ("WATCH EVERY BATTLE", "Your real fleet, round by round"),
    "port": ("COMMAND YOUR FLEET", "23 hulls, docked at your Spaceport"),
    "galaxy": ("249 RIVALS. ONE GALAXY.", "Raid, ally and seize the Galactic Core"),
    "report": ("WIN THE RAID", "Every ship lost and every crate of plunder"),
    # The map redesign and the Frontier (2026-09-30).
    "core": ("SEIZE THE CORE", "Hold the sun at the galaxy's heart"),
    "galaxyfull": ("A GALAXY OF WORLDS", "Thousands of worlds, 249 rival commanders"),
    "frontier": ("EXPAND YOUR FRONTIER", "Jump gates, repair docks and observatories"),
}


def font(name):
    return base64.b64encode(open(os.path.join(FONTS, name), "rb").read()).decode()


def page(capture, headline, subline):
    # One line, whatever its length: Orbitron at 84 px fits about 16 characters.
    size = min(84, int(84 * 16.5 / max(1, len(headline))))
    return f"""<!doctype html><html><head><meta charset="utf-8"><style>
@font-face{{font-family:Orbitron;src:url(data:font/ttf;base64,{font("Orbitron.ttf")}) format("truetype");font-weight:400 900}}
@font-face{{font-family:Exo2;src:url(data:font/ttf;base64,{font("Exo2.ttf")}) format("truetype");font-weight:100 900}}
html,body{{margin:0;width:{W}px;height:{H}px;overflow:hidden;background:#05030E}}
.bg{{position:absolute;inset:0;background:radial-gradient(ellipse at 50% 12%,#3b1d72 0%,#1a0c3c 38%,#05030E 72%)}}
.flare{{position:absolute;left:-200px;right:-200px;top:-120px;height:760px;
 background:radial-gradient(ellipse at 50% 60%,rgba(255,154,61,.28),transparent 60%)}}
.stars{{position:absolute;inset:0;background-image:radial-gradient(2px 2px at 12% 8%,#fff8,transparent),
 radial-gradient(1.6px 1.6px at 83% 5%,#fff9,transparent),radial-gradient(2px 2px at 91% 14%,#fff6,transparent),
 radial-gradient(1.4px 1.4px at 6% 19%,#fff7,transparent),radial-gradient(1.8px 1.8px at 70% 3%,#fffa,transparent),
 radial-gradient(1.2px 1.2px at 30% 2%,#fff8,transparent)}}
h1{{position:absolute;top:150px;left:40px;right:40px;height:110px;margin:0;display:flex;align-items:center;justify-content:center;
 white-space:nowrap;text-align:center;font:900 {size}px Orbitron;letter-spacing:5px;
 color:#FF9A3D;text-shadow:0 0 28px rgba(255,154,61,.75),0 0 70px rgba(255,154,61,.35)}}
p{{position:absolute;top:276px;left:60px;right:60px;margin:0;text-align:center;font:500 46px Exo2;color:#E8F7FF;opacity:.9}}
.rule{{position:absolute;top:374px;left:50%;width:360px;height:3px;transform:translateX(-50%);
 background:linear-gradient(90deg,transparent,#FF3DD8,transparent)}}
.shot{{position:absolute;top:470px;left:50%;width:1030px;height:2238px;transform:translateX(-50%);border-radius:74px;overflow:hidden;
 box-shadow:0 0 0 5px #FF9A3D,0 0 50px 8px rgba(255,154,61,.45),0 30px 90px rgba(0,0,0,.6)}}
.shot img{{display:block;width:100%;height:100%}}
</style></head><body><div class="bg"></div><div class="flare"></div><div class="stars"></div>
<h1>{headline}</h1><p>{subline}</p><div class="rule"></div>
<div class="shot"><img src="file://{os.path.abspath(capture)}"></div></body></html>"""


def render(html_path, png_path):
    profile = tempfile.mkdtemp()
    chrome = subprocess.Popen([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                               f"--user-data-dir={profile}", f"--window-size={W},{H}", "--force-device-scale-factor=1",
                               "--virtual-time-budget=6000", f"--screenshot={png_path}", f"file://{html_path}"],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # Headless Chrome sometimes lingers after writing the file: give it a minute at most.
    for _ in range(60):
        if chrome.poll() is not None:
            break
        if os.path.exists(png_path) and os.path.getsize(png_path) > 0:
            time.sleep(1)
            break
        time.sleep(1)
    if chrome.poll() is None:
        chrome.kill()
    subprocess.run(["pkill", "-f", profile], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if not os.path.exists(png_path):
        raise SystemExit(f"Chrome made no screenshot of {html_path}")


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    tmp = tempfile.mkdtemp()
    for n, arg in enumerate(sys.argv[2:], start=1):
        capture, key = arg.rsplit("=", 1)
        headline, subline = CAPTIONS[key]
        html = os.path.join(tmp, f"{key}.html")
        open(html, "w", encoding="utf-8").write(page(capture, headline, subline))
        png = os.path.join(tmp, f"{key}.png")
        render(html, png)
        jpg = os.path.join(out, f"{n:02d}-{key}.jpg")
        subprocess.run(["sips", "-s", "format", "jpeg", "-s", "formatOptions", "90", png, "--out", jpg],
                       stdout=subprocess.DEVNULL, check=True)
        print("framed", jpg)


if __name__ == "__main__":
    main()
