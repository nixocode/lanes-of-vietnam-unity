"""
The mountains behind the treeline: the real Chu Pong massif, from real
elevation data (PLAN §12.10: "real terrain: SRTM elevation data ... The Chu
Pong massif is a real shape").

    tools/.venv/bin/python tools/art/mountains.py

Source: the AWS Open Data "Terrain Tiles" (terrarium encoding, zoom 13, about
18.6 m a sample at this latitude), which for this region are SRTM — public
domain, NASA/USGS — fetched once into SourceArt/terrain/ (gitignored; see
ASSETS.md for the tile range and how to re-fetch).

The view is taken from LZ X-Ray, in the Ia Drang valley at the eastern foot
of the massif — the landing zone of November 1965 — looking along a heading
of 262 degrees, into the massif. Rendered skylines from nine candidate
viewpoints and headings (captures/, not shipped) put the massif at 6-8 degrees
above the horizon from here, where the reference's peaks stand at 5-9; from
4-8 km east it sinks to 2-3. So no vertical exaggeration: the real heights.

The camera never rotates, so the backdrop is a fan-shaped grid around it:
columns of azimuth, rings of distance growing geometrically, which keeps the
grid about as fine on screen near as far. What ships is the heights only
(float16, rings x columns) and the grid's description; the game builds the
mesh at load (MountainView).

Below the DEM's own resolution the height gets fine noise — a forested
ridge is not smooth at 20 m — scaled so it never shows as noise from here.
"""
import json
import math
import os
import re
import glob

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt", "terrain")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Mountains")

VIEW = dict(name="LZ X-Ray, Ia Drang valley", lat=13.567, lon=107.717, heading=262.0)
AZ_HALF = 25.0               # degrees either side of the heading: the 19 degree lens, the +-150 m pan (4 deg at 2 km), the glasses
COLUMNS = 720
RINGS = 240
NEAR, FAR = 900.0, 22000.0   # metres
ZOOM = 13


def load_dem():
    tiles = {}
    for f in glob.glob(os.path.join(SRC, f"terrarium_{ZOOM}_*.png")):
        tx, ty = map(int, re.findall(r"_(\d+)_(\d+)\.png$", f)[0])
        tiles[(tx, ty)] = f
    xs = sorted({k[0] for k in tiles}); ys = sorted({k[1] for k in tiles})
    dem = np.zeros((len(ys) * 256, len(xs) * 256), np.float64)
    for (tx, ty), f in tiles.items():
        a = np.asarray(Image.open(f).convert("RGB")).astype(np.float64)
        dem[(ty - ys[0]) * 256:(ty - ys[0] + 1) * 256, (tx - xs[0]) * 256:(tx - xs[0] + 1) * 256] = \
            a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768
    return dem, xs[0], ys[0], len(tiles)


def to_pixel(lat, lon, x0, y0):
    n = 2 ** ZOOM
    x = (lon + 180) / 360 * n
    y = (1 - math.log(math.tan(math.radians(lat)) + 1 / math.cos(math.radians(lat))) / math.pi) / 2 * n
    return (x - x0) * 256, (y - y0) * 256


def bilinear(dem, x, y):
    H, W = dem.shape
    x = np.clip(x, 0, W - 2); y = np.clip(y, 0, H - 2)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
    fx, fy = x - x0, y - y0
    return (dem[y0, x0] * (1 - fx) * (1 - fy) + dem[y0, x0 + 1] * fx * (1 - fy)
            + dem[y0 + 1, x0] * (1 - fx) * fy + dem[y0 + 1, x0 + 1] * fx * fy)


def value_noise(x, y, seed):
    """Smooth lattice noise, -1..1, deterministic."""
    xi, yi = np.floor(x), np.floor(y)
    fx, fy = x - xi, y - yi
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)

    def h(i, j):
        v = np.sin(i * 127.1 + j * 311.7 + seed * 74.7) * 43758.5453
        return (v - np.floor(v)) * 2 - 1
    return (h(xi, yi) * (1 - ux) * (1 - uy) + h(xi + 1, yi) * ux * (1 - uy)
            + h(xi, yi + 1) * (1 - ux) * uy + h(xi + 1, yi + 1) * ux * uy)


def main():
    dem, tx0, ty0, ntiles = load_dem()
    metres_per_px = 156543.03392 / 2 ** ZOOM * math.cos(math.radians(VIEW["lat"]))
    cx, cy = to_pixel(VIEW["lat"], VIEW["lon"], tx0, ty0)
    ground = float(bilinear(dem, np.array([cx]), np.array([cy]))[0])

    az = np.radians(VIEW["heading"] + np.linspace(-AZ_HALF, AZ_HALF, COLUMNS))
    dist = np.geomspace(NEAR, FAR, RINGS)
    A, D = np.meshgrid(az, dist)                                  # rings x columns
    east, north = D * np.sin(A), D * np.cos(A)
    px, py = cx + east / metres_per_px, cy - north / metres_per_px
    h = bilinear(dem, px, py) - ground
    # Detail below the data's resolution: two octaves at 90 m and 30 m,
    # a few metres, in real ground coordinates so it stands still.
    h += 7.0 * value_noise(east / 90.0, north / 90.0, 1) + 2.5 * value_noise(east / 30.0, north / 30.0, 2)
    # The earth's curvature, with standard refraction.
    h -= D ** 2 / (2 * 6.371e6 * 1.15)

    # Where it stands against the sky from the lens, for the record.
    lens = 5.1
    ang = np.degrees(np.arctan2(h - lens, D))
    sky = ang.max(0)
    print(f"[mountains] {ntiles} tiles, {metres_per_px:.1f} m/sample; view ground {ground:.0f} m; "
          f"skyline {sky.min():.1f}..{sky.max():.1f} deg (median {np.median(sky):.1f}); "
          f"grid {RINGS} x {COLUMNS}, {NEAR:.0f}..{FAR:.0f} m")

    os.makedirs(OUT, exist_ok=True)
    h.astype(np.float16).tofile(os.path.join(OUT, "mountains.bytes"))
    with open(os.path.join(OUT, "mountains.json"), "w") as f:
        json.dump(dict(view=VIEW, ground_m=round(ground, 1), rings=RINGS, columns=COLUMNS, near_m=NEAR, far_m=FAR,
                       az_half_deg=AZ_HALF, source="AWS Terrain Tiles (terrarium, SRTM), zoom 13",
                       tiles=[tx0, ty0], skyline_deg=[round(float(sky.min()), 2), round(float(sky.max()), 2)]),
                  f, indent=1)
    print(f"[mountains] wrote {OUT}")


main()
