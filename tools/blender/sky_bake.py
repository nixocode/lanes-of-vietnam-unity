"""
Bake the sky from a Poly Haven HDRI into what the game ships (PLAN §12.10).

    Blender -b --python tools/blender/sky_bake.py -- --src SourceArt/polyhaven/sunflowers_puresky_16k.hdr \
        --preview captures/sky-candidates.png --suns -100,-125,-150,150,125
    Blender -b --python tools/blender/sky_bake.py -- --src ... --sun-az -125 --out Assets/_Project/Art/Sky

Why a bake and not the HDRI itself. The lens is 19 degrees, so the sky is
magnified: a 1080p frame wants ~58 pixels per degree, and a whole-sphere 16K
HDRI has 45. But this camera never rotates — it pans, which does not move a sky
at infinity — so only one window of sky is ever seen: about +-32 degrees of
azimuth, 0-24 of elevation, glasses included. That window is cut from the 16K
at native resolution (2560 x 1040, 40 px/degree) and shipped small and sharp;
the rest of the sphere ships at 1K for reflections and the fallback.

Outputs (all linear light scaled by one exposure k, recorded):
    sky_window.png   the visible window, 8-bit sRGB of (radiance / k)
    sky_full.png     the whole sphere at 1024 x 512, sun clamped, same encoding
    sky.json         k, the window, the rotation, the sun (direction, colour,
                     irradiance) and a 64 x 32 radiance grid with the sun
                     removed, from which the game builds its ambient light
                     with Unity's own spherical-harmonics code

Conventions: world azimuth 0 is +z (the camera's view), +90 is +x (screen
right); elevation 0 is the horizon. The HDRI is rotated about the vertical so
its sun lands at --sun-az: behind the camera, so the clouds in view are lit from
the front, as the reference's are.
"""
import bpy
import json
import math
import os
import sys
import zlib
import struct

import numpy as np

AZ0, AZ1, EL0, EL1 = -32.0, 32.0, -2.0, 24.0
WIN_W, WIN_H = 2560, 1040


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    out = {"--src": None, "--out": None, "--preview": None, "--suns": None, "--sun-az": "-125"}
    for i in range(0, len(a), 2):
        out[a[i]] = a[i + 1]
    return out


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    arr = px.reshape(h, w, 4)[::-1, :, :3].copy()      # row 0 = top
    bpy.data.images.remove(img)
    return arr


def lum(a):
    return a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def sample(arr, rot, az0, az1, el0, el1, W, H):
    """Bilinear sample of the rotated sphere over a world az/el window."""
    h, w, _ = arr.shape
    az = az0 + (np.arange(W) + 0.5) / W * (az1 - az0)
    el = el1 - (np.arange(H) + 0.5) / H * (el1 - el0)
    iaz = (az - rot + 180.0) % 360.0 - 180.0
    col = (iaz + 180.0) / 360.0 * w - 0.5
    row = (90.0 - el) / 180.0 * h - 0.5
    c0 = np.floor(col).astype(np.int64); fc = (col - c0)[None, :, None]
    r0 = np.clip(np.floor(row).astype(np.int64), 0, h - 2); fr = np.clip(row - r0, 0, 1)[:, None, None]
    c0m, c1m = c0 % w, (c0 + 1) % w
    top = arr[r0][:, c0m] * (1 - fc) + arr[r0][:, c1m] * fc
    bot = arr[r0 + 1][:, c0m] * (1 - fc) + arr[r0 + 1][:, c1m] * fc
    return (top * (1 - fr) + bot * fr).astype(np.float32)


def srgb8(lin):
    x = np.clip(lin, 0, 1)
    s = np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)
    return (np.clip(s, 0, 1) * 255 + 0.5).astype(np.uint8)


def write_png(path, rgb8):
    h, w, _ = rgb8.shape
    raw = b"".join(b"\x00" + rgb8[y].tobytes() for y in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def find_sun(arr):
    """The sun: brightest pixel, its disk, and its irradiance above the sky around it."""
    h, w, _ = arr.shape
    L = lum(arr)
    y, x = np.unravel_index(np.argmax(L), L.shape)
    az = (x + 0.5) / w * 360 - 180
    el = 90 - (y + 0.5) / h * 180
    # Disk: within 3 degrees of the peak.
    ry = int(3 / 180 * h) + 1
    rx = int(3 / 360 * w / max(0.2, math.cos(math.radians(el)))) + 1
    ys = slice(max(0, y - ry), min(h, y + ry + 1))
    xs = np.arange(x - rx, x + rx + 1) % w
    patch = arr[ys][:, xs]
    pl = lum(patch)
    bg = float(np.median(pl))
    mask = pl > max(bg * 20, 50)
    rows = np.arange(ys.start, ys.stop)
    elr = np.radians(90 - (rows + 0.5) / h * 180)
    domega = (2 * math.pi / w) * (math.pi / h) * np.cos(elr)[:, None]
    excess = np.clip(patch - np.median(patch.reshape(-1, 3), axis=0), 0, None)
    E = (excess * mask[..., None] * domega[..., None]).reshape(-1, 3).sum(axis=0)
    return az, el, E, (ys, xs, mask)


def rot_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return [math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)]


def main():
    a = args()
    src = a["--src"]
    print(f"[sky] loading {src}", flush=True)
    arr = load(src)
    h, w, _ = arr.shape
    sun_az_img, sun_el, E_sun, (ys, xs, mask) = find_sun(arr)
    print(f"[sky] {w}x{h}; sun at image azimuth {sun_az_img:.1f}, elevation {sun_el:.1f}; irradiance rgb {E_sun.round(2).tolist()}", flush=True)

    if a["--preview"]:
        suns = [float(s) for s in a["--suns"].split(",")]
        tiles = []
        for s in suns:
            rot = s - sun_az_img
            win = sample(arr, rot, AZ0, AZ1, EL0, EL1, 1024, 416)
            k = float(np.percentile(lum(win), 99.8)) / 0.95
            tile = srgb8(win / k)
            tile[:6, :, :] = [255, 220, 0]              # a separator
            tiles.append(tile)
            print(f"[sky] candidate sun azimuth {s}: rotation {rot:.1f}", flush=True)
        write_png(a["--preview"], np.concatenate(tiles, axis=0))
        print(f"[sky] preview {a['--preview']}", flush=True)
        return

    out = a["--out"]
    os.makedirs(out, exist_ok=True)
    sun_az = float(a["--sun-az"])
    rot = sun_az - sun_az_img

    win = sample(arr, rot, AZ0, AZ1, EL0, EL1, WIN_W, WIN_H)
    k = float(np.percentile(lum(win), 99.8)) / 0.95
    write_png(os.path.join(out, "sky_window.png"), srgb8(win / k))

    # The whole sphere, sun clamped to the sky around it.
    clamped = arr.copy()
    patch = clamped[ys][:, xs]
    bgc = np.median(patch.reshape(-1, 3), axis=0)
    patch[mask] = bgc
    clamped[ys.start:ys.stop, xs] = patch
    full = sample(clamped, rot, -180, 180, -90, 90, 1024, 512)
    write_png(os.path.join(out, "sky_full.png"), srgb8(full / k))

    grid = sample(clamped, rot, -180, 180, -90, 90, 64, 32)
    sun_dir = rot_dir(sun_az, sun_el)
    info = {
        "source": os.path.basename(src),
        "exposure_k": k,
        "window": {"az0": AZ0, "az1": AZ1, "el0": EL0, "el1": EL1, "width": WIN_W, "height": WIN_H},
        "rotation_deg": rot,
        "sun": {"azimuth": sun_az, "elevation": sun_el, "dir_to_sun": sun_dir,
                "irradiance_rgb": [float(v) for v in E_sun]},
        "radiance_grid": {"width": 64, "height": 32, "rgb": [round(float(v), 5) for v in grid.reshape(-1)]},
    }
    with open(os.path.join(out, "sky.json"), "w") as f:
        json.dump(info, f)
    print(f"[sky] baked to {out}: k={k:.3f}, rotation {rot:.1f}, sun dir {np.round(sun_dir, 3).tolist()}", flush=True)


main()
