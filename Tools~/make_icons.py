"""Download Material Symbols SVGs and rasterize them into white-on-transparent PNGs.

usage: python make_icons.py <svg_cache_dir> <png_out_dir> [size]
"""
import math
import os
import re
import sys
import urllib.request

from PIL import Image, ImageChops, ImageDraw

ICONS = [
    "person", "face", "face_2", "face_3", "face_4", "face_5", "face_6", "accessibility_new",
    "checkroom", "apparel", "styler", "content_cut", "diamond", "redeem", "eyeglasses",
    "footprint", "pets", "palette", "texture", "gradient", "brush", "animation", "movie",
    "music_note", "photo_camera", "landscape", "light_mode", "auto_awesome", "build", "code",
    "extension", "deployed_code", "view_in_ar", "science", "settings", "star", "favorite",
    "bookmark", "construction", "check_circle", "inventory_2", "archive", "delete", "warning",
    "shopping_bag", "category", "folder_special",
]
URL = "https://fonts.gstatic.com/s/i/short-term/release/materialsymbolsoutlined/{name}/{style}/48px.svg"

SUPER = 1024  # supersampled render size
NUM_RE = re.compile(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")


def fetch(name, cache):
    path = os.path.join(cache, name + ".svg")
    if os.path.exists(path):
        return open(path, encoding="utf-8").read()
    for style in ("fill1", "default"):
        try:
            with urllib.request.urlopen(URL.format(name=name, style=style), timeout=20) as r:
                svg = r.read().decode("utf-8")
            with open(path, "w", encoding="utf-8") as f:
                f.write(svg)
            return svg
        except Exception:
            continue
    return None


class Scanner:
    def __init__(self, d):
        self.d, self.i = d, 0

    def skip(self):
        while self.i < len(self.d) and self.d[self.i] in " ,\t\r\n":
            self.i += 1

    def peek_cmd(self):
        self.skip()
        if self.i < len(self.d) and self.d[self.i].isalpha():
            return self.d[self.i]
        return None

    def at_end(self):
        self.skip()
        return self.i >= len(self.d)

    def num(self):
        self.skip()
        m = NUM_RE.match(self.d, self.i)
        if not m:
            raise ValueError("number expected at %d: %r" % (self.i, self.d[self.i:self.i + 20]))
        self.i = m.end()
        return float(m.group())

    def flag(self):
        self.skip()
        c = self.d[self.i]
        self.i += 1
        return c == "1"


def arc_points(p0, rx, ry, phi, large, sweep, p1, steps=24):
    # SVG arc endpoint -> center parameterization (SVG spec F.6.5)
    if rx == 0 or ry == 0:
        return [p1]
    rx, ry = abs(rx), abs(ry)
    cp, sp = math.cos(math.radians(phi)), math.sin(math.radians(phi))
    dx, dy = (p0[0] - p1[0]) / 2, (p0[1] - p1[1]) / 2
    x1, y1 = cp * dx + sp * dy, -sp * dx + cp * dy
    lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    co = math.sqrt(max(0.0, num / den)) if den else 0.0
    if large == sweep:
        co = -co
    cx1, cy1 = co * rx * y1 / ry, -co * ry * x1 / rx
    cx = cp * cx1 - sp * cy1 + (p0[0] + p1[0]) / 2
    cy = sp * cx1 + cp * cy1 + (p0[1] + p1[1]) / 2

    def ang(ux, uy, vx, vy):
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a

    t1 = ang(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
    dt = ang((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    pts = []
    for k in range(1, steps + 1):
        t = t1 + dt * k / steps
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cp * x - sp * y + cx, sp * x + cp * y + cy))
    return pts


def parse_path(d):
    """Return a list of polygons (lists of points) in SVG user units."""
    s = Scanner(d)
    polys, cur = [], []
    x = y = sx = sy = 0.0
    last_ctrl, last_cmd = None, None
    cmd = None
    seg = 16
    while not s.at_end():
        c = s.peek_cmd()
        if c:
            s.i += 1
            cmd = c
        elif cmd is None:
            raise ValueError("path must start with a command")
        rel = cmd.islower()
        C = cmd.upper()
        ox, oy = (x, y) if rel else (0.0, 0.0)
        if C == "M":
            if cur:
                polys.append(cur)
            x, y = s.num() + ox, s.num() + oy
            sx, sy = x, y
            cur = [(x, y)]
            cmd = "l" if rel else "L"  # subsequent pairs are implicit lineto
            last_ctrl = None
        elif C == "Z":
            if cur:
                polys.append(cur)
            cur = []
            x, y = sx, sy
            last_ctrl = None
            cmd = None
            # a new subpath may continue without M; start it at the current point
            if s.peek_cmd() not in (None, "M", "m") and not s.at_end():
                cur = [(x, y)]
        elif C == "L":
            x, y = s.num() + ox, s.num() + oy
            cur.append((x, y))
            last_ctrl = None
        elif C == "H":
            x = s.num() + ox
            cur.append((x, y))
            last_ctrl = None
        elif C == "V":
            y = s.num() + oy
            cur.append((x, y))
            last_ctrl = None
        elif C in ("Q", "T"):
            if C == "Q":
                qx, qy = s.num() + ox, s.num() + oy
            else:
                if last_ctrl and last_ctrl[0] == "Q":
                    qx, qy = 2 * x - last_ctrl[1], 2 * y - last_ctrl[2]
                else:
                    qx, qy = x, y
            ex, ey = s.num() + ox, s.num() + oy
            for k in range(1, seg + 1):
                t = k / seg
                mt = 1 - t
                cur.append((mt * mt * x + 2 * mt * t * qx + t * t * ex,
                            mt * mt * y + 2 * mt * t * qy + t * t * ey))
            last_ctrl = ("Q", qx, qy)
            x, y = ex, ey
        elif C in ("C", "S"):
            if C == "C":
                c1x, c1y = s.num() + ox, s.num() + oy
            else:
                if last_ctrl and last_ctrl[0] == "C":
                    c1x, c1y = 2 * x - last_ctrl[1], 2 * y - last_ctrl[2]
                else:
                    c1x, c1y = x, y
            c2x, c2y = s.num() + ox, s.num() + oy
            ex, ey = s.num() + ox, s.num() + oy
            for k in range(1, seg + 1):
                t = k / seg
                mt = 1 - t
                cur.append((mt ** 3 * x + 3 * mt * mt * t * c1x + 3 * mt * t * t * c2x + t ** 3 * ex,
                            mt ** 3 * y + 3 * mt * mt * t * c1y + 3 * mt * t * t * c2y + t ** 3 * ey))
            last_ctrl = ("C", c2x, c2y)
            x, y = ex, ey
        elif C == "A":
            rx, ry, phi = s.num(), s.num(), s.num()
            large, sweep = s.flag(), s.flag()
            ex, ey = s.num() + ox, s.num() + oy
            cur.extend(arc_points((x, y), rx, ry, phi, large, sweep, (ex, ey)))
            x, y = ex, ey
            last_ctrl = None
        else:
            raise ValueError("unsupported command " + cmd)
        last_cmd = C
    if cur:
        polys.append(cur)
    return polys


def rasterize(svg, size):
    vb = [float(v) for v in re.search(r'viewBox="([^"]+)"', svg).group(1).split()]
    vx, vy, vw, vh = vb
    mask = Image.new("1", (SUPER, SUPER), 0)
    for d in re.findall(r'\sd="([^"]+)"', svg):
        for poly in parse_path(d):
            if len(poly) < 3:
                continue
            layer = Image.new("1", (SUPER, SUPER), 0)
            pts = [((px - vx) / vw * SUPER, (py - vy) / vh * SUPER) for px, py in poly]
            ImageDraw.Draw(layer).polygon(pts, fill=1)
            mask = ImageChops.logical_xor(mask, layer)  # even-odd fill
    alpha = mask.convert("L").resize((size, size), Image.LANCZOS)
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    img.putalpha(alpha)
    return img


def main():
    cache, out = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 64
    os.makedirs(cache, exist_ok=True)
    os.makedirs(out, exist_ok=True)
    for name in ICONS:
        svg = fetch(name, cache)
        if svg is None:
            print("MISSING", name)
            continue
        rasterize(svg, size).save(os.path.join(out, name + ".png"))
        print("ok", name)


if __name__ == "__main__":
    main()
