"""
Pack the fighting's sprite sheets into the two atlases the effect shaders sample
(PLAN §12.42: "the actual shots being fired need some work").

    tools/.venv/bin/python -I tools/art/fx_atlas.py
    tools/.venv/bin/python -I tools/art/fx_atlas.py --out Logs/fx/out   # not into the project

Each run also leaves both atlases in Logs/fx/ as they would be seen, to look at.

Sources are Unity Labs' "VFX image sequences and flipbooks" (CC0), unpacked in
SourceArt/unitylabs/ (gitignored, re-fetchable; the URLs and hashes are in
ASSETS.md). Outputs, in Assets/_Project/Art/FX/:

    fx_smoke.png   2048 x 2048, colour times alpha with alpha beside it: what is
                   drawn over the scene (LOV/FX Smoke)
    fx_glow.png    2048 x 1024, colour on black: what is added to it as light
                   (LOV/FX Glow)

Where each sheet sits in them (SHEETS below, in pixels from the top left) is
written out again in View/FxSheets.cs: change one, change the other.

What is in them:

    smoke  dust    Explosion01-light-nofire, 25 frames: a burst of smoke with no
                   fire in it. Dirt kicked up by a round, a grenade's dust, the
                   earth a shell throws.
           blast   Explosion00, 25 frames: a ball of fire going to black smoke.
           wisp    WispySmoke01, 64 frames that loop: smoke that hangs. Gun
                   smoke, a shell's column, a smoke screen.
           curl    CandleSmoke01, 80 frames that loop: a thin thread of it, off
                   a hot barrel.
           speck   made here: clods of earth and drops, eight of them.
           brass   made here: a spent case, side on.
    glow   fire    the fire of blast, without its smoke.
           flame   FireBall01, every other frame of 64: a ball of flame, looping.
           tongue  made here from flame's frames: the gas out of a muzzle seen
                   from the side, eight of them.
           star    the same seen down the barrel, or out through the slots of a
                   flash hider: three to six spikes.

Three things are not a straight copy.

The smoke is stored as colour times alpha. Stored apart, the colour under a
clear texel is whatever the artist left there, and a filtered or mip-mapped
sample at a puff's edge takes some of it: a dark rim round every puff.

The grey sheets (dust, wisp, curl) keep their light and shade but not their
level: each is stretched so that its dark side is half and its lit side all of
what the shader's tint asks for, so one tint gives the same colour whichever
sheet it is put on.

A frame of a five-by-five sheet is 204.8 pixels on a side. Each is resampled to
200 and set in a cell of 204, with two clear pixels round it, so that no frame's
edge is its neighbour's.
"""
import argparse
import os

import numpy as np
from PIL import Image

SRC = "SourceArt/unitylabs"
OUT = "Assets/_Project/Art/FX"
SEEN = "Logs/fx"

SMOKE = (2048, 2048)
GLOW = (2048, 1024)

# Where each sheet sits: x, y of its top left, a frame's width and height, the
# pitch from one frame to the next, columns and frames. (FxSheets.cs has the same.)
SHEETS = {
    "smoke": {
        "dust": dict(x=2, y=2, w=200, h=200, px=204, py=204, cols=5, frames=25),
        "blast": dict(x=1026, y=2, w=200, h=200, px=204, py=204, cols=5, frames=25),
        "wisp": dict(x=0, y=1024, w=128, h=128, px=128, py=128, cols=8, frames=64),
        "curl": dict(x=1024, y=1024, w=51, h=128, px=51, py=128, cols=20, frames=80),
        "speck": dict(x=1024, y=1536, w=64, h=64, px=64, py=64, cols=8, frames=8),
        "brass": dict(x=1536, y=1536, w=64, h=64, px=64, py=64, cols=2, frames=2),
    },
    "glow": {
        "fire": dict(x=2, y=2, w=200, h=200, px=204, py=204, cols=5, frames=25),
        "flame": dict(x=1024, y=0, w=128, h=128, px=128, py=128, cols=8, frames=32),
        "tongue": dict(x=1024, y=512, w=128, h=64, px=128, py=64, cols=8, frames=8),
        "star": dict(x=1024, y=576, w=128, h=128, px=128, py=128, cols=8, frames=8),
    },
}


def lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def load(folder, name):
    """A sheet as floats: linear colour, and alpha (1 everywhere if it has none)."""
    im = Image.open(os.path.join(SRC, folder, name))
    a = np.asarray(im.convert("RGBA")).astype(np.float32) / 255
    return lin(a[..., :3]), a[..., 3]


def resize(a, w, h):
    """An array of floats (H x W x C) to a new size, each channel on its own."""
    if a.ndim == 2:
        return np.asarray(Image.fromarray(a.astype(np.float32), "F").resize((w, h), Image.LANCZOS))
    return np.stack([resize(a[..., k], w, h) for k in range(a.shape[-1])], -1)


def frames(a, cols, rows):
    """The frames of a sheet, left to right and top to bottom."""
    hh, ww = a.shape[0] / rows, a.shape[1] / cols
    for r in range(rows):
        for c in range(cols):
            yield a[int(round(r * hh)):int(round((r + 1) * hh)), int(round(c * ww)):int(round((c + 1) * ww))]


def shade(rgb, alpha):
    """A grey sheet's light and shade, stretched: its dark side a half, its lit side one."""
    lum = rgb.mean(-1)
    body = lum[alpha > 0.3]
    lo, hi = np.percentile(body, 5), np.percentile(body, 98)
    s = np.clip((lum - lo) / max(hi - lo, 1e-4), 0, 1)
    return np.repeat((0.5 + 0.5 * s)[..., None], 3, -1)


def put(atlas, sheet, index, frame):
    """A frame (premultiplied colour with alpha, or colour alone) into its cell."""
    s = sheet
    x = s["x"] + (index % s["cols"]) * s["px"]
    y = s["y"] + (index // s["cols"]) * s["py"]
    if frame.shape[0] != s["h"] or frame.shape[1] != s["w"]:
        frame = resize(frame, s["w"], s["h"])
    atlas[y:y + s["h"], x:x + s["w"]] = np.clip(frame, 0, 1)


def premultiplied(rgb, alpha):
    return np.concatenate([rgb * alpha[..., None], alpha[..., None]], -1)


def smooth(lo, hi, x):
    t = np.clip((x - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def bilinear(img, u, v):
    """An image sampled at u, v in 0..1 (clamped)."""
    h, w = img.shape[:2]
    x = np.clip(u, 0, 1) * (w - 1)
    y = np.clip(v, 0, 1) * (h - 1)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    x1, y1 = np.minimum(x0 + 1, w - 1), np.minimum(y0 + 1, h - 1)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    return (img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) + (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy


def noise(rng, n, size):
    """Smooth noise in 0..1: a coarse grid of random values, drawn up."""
    g = rng.random((n, n)).astype(np.float32)
    a = np.asarray(Image.fromarray(g, "F").resize((size, size), Image.BICUBIC))
    return np.clip((a - a.min()) / max(a.max() - a.min(), 1e-4), 0, 1)


def specks(rng):
    """Clods and drops: four of each. A clod is a lump with a broken edge; a drop is round, or drawn out."""
    out = []
    y, x = np.mgrid[-1:1:64j, -1:1:64j].astype(np.float32)
    for k in range(4):
        n = noise(rng, 5, 64)
        r = np.hypot(x * (1.0 + 0.25 * (k % 2)), y)
        a = smooth(0.0, 0.14, 0.60 + 0.55 * (n - 0.5) - r)
        m = np.clip(0.62 + 0.3 * (0.5 - y * 0.5) + 0.25 * (noise(rng, 9, 64) - 0.5), 0.4, 1.0)
        out.append(premultiplied(np.repeat(m[..., None], 3, -1), a))
    for k in range(4):
        long = 1.0 if k < 2 else 2.3
        r = np.hypot(x * long, y * (1.0 if k < 2 else 0.9))
        a = smooth(0.78, 0.30 if k % 2 == 0 else 0.5, r)
        m = np.clip(0.8 + 0.2 * (0.5 - y * 0.5), 0, 1)
        out.append(premultiplied(np.repeat(m[..., None], 3, -1), a))
    return out


def brass():
    """A spent case from the side, and one a little end on: brass, with the light along it."""
    out = []
    y, x = np.mgrid[-1:1:64j, -1:1:64j].astype(np.float32)
    for half in (0.62, 0.36):
        wide = 0.17
        d = np.hypot(np.maximum(np.abs(x) - (half - wide), 0), y) - wide
        a = smooth(0.035, -0.035, d)
        shine = np.exp(-((y + 0.05) / 0.06) ** 2)
        base = np.array([0.56, 0.38, 0.10], np.float32) * (0.55 + 0.25 * (0.5 - y * 2.5).clip(0, 1))[..., None]
        rgb = base + np.array([1.0, 0.9, 0.62], np.float32) * (0.85 * shine)[..., None]
        # The mouth of it: dark.
        rgb = rgb * (1 - 0.6 * smooth(half - 0.16, half - 0.04, x))[..., None]
        out.append(premultiplied(np.clip(rgb, 0, 1), a))
    return out


def fire_only(rgb, alpha):
    """What of a frame of fire and smoke is fire: the colour of flame, and the white of its heart."""
    hot = smooth(0.06, 0.30, rgb[..., 0] - rgb[..., 2]) + smooth(0.55, 0.9, rgb.min(-1))
    return rgb * (alpha * np.clip(hot, 0, 1))[..., None]


def ramp(heat):
    """The colour of burning gas by how hot it is, 0 to 1: nothing, deep orange, orange, pale yellow, white."""
    stops = np.array([0.0, 0.22, 0.5, 0.8, 1.0], np.float32)
    cols = np.array([[0, 0, 0], [0.55, 0.10, 0.01], [1.0, 0.42, 0.06], [1.0, 0.84, 0.42], [1.0, 0.97, 0.86]], np.float32)
    heat = np.clip(heat, 0, 1)
    return np.stack([np.interp(heat, stops, cols[:, k]) for k in range(3)], -1).astype(np.float32)


def tongues(flame):
    """
    The gas out of a muzzle, from the side: narrow where it leaves, widest a
    fifth of the way out, drawn to a point, hottest along its middle and near
    the muzzle; its edge broken by a frame of flame pulled out along it. Eight:
    four long and thin (a rifle's, a machine gun's), four short and fat (a
    submachine gun's, a carbine's).
    """
    out = []
    v, u = np.mgrid[-1:1:64j, 0:1:128j].astype(np.float32)
    for k in range(8):
        fat = 0.42 if k < 4 else 0.7
        peak = 0.2 if k < 4 else 0.32
        f = flame[(k * 7 + 3) % len(flame)]
        n = bilinear(f, 0.2 + 0.6 * u, 0.5 + 0.36 * v + 0.07 * np.sin(u * 11 + k)).mean(-1)
        width = np.where(u < peak, (u / peak) ** 0.6, np.clip(1 - (u - peak) / (1 - peak), 0, 1) ** 1.4) * fat * (0.55 + 0.9 * n)
        across = np.exp(-(v / np.maximum(width, 1e-3)) ** 2 * 2.2)
        along = smooth(0.0, 0.04, u) * (1 - 0.55 * u)
        out.append(ramp(across * along * (0.75 + 0.5 * n)))
    return out


def stars(flame):
    """The same from in front, or out through a flash hider's slots: a ball with three to six spikes of flame, each drawn to a point."""
    out = []
    y, x = np.mgrid[-1:1:128j, -1:1:128j].astype(np.float32)
    r = np.hypot(x, y)
    for k in range(8):
        spikes = 3 + k % 4
        f = flame[(k * 5 + 11) % len(flame)]
        n = bilinear(f, 0.5 + 0.45 * x, 0.5 + 0.45 * y).mean(-1)
        heat = np.exp(-r * r * (14 if k < 4 else 22)) * (0.8 + 0.4 * n)
        for q in range(spikes):
            ang = 0.7 * k + q * 2 * np.pi / spikes + 0.25 * np.sin(k * 3.1 + q)
            reach = 0.62 + 0.32 * np.sin(k * 1.7 + q * 2.3)
            al = x * np.cos(ang) + y * np.sin(ang)
            ac = -x * np.sin(ang) + y * np.cos(ang)
            wide = 0.11 * np.clip(1 - al / reach, 0, 1) ** 0.8 * (0.6 + 0.8 * n) + 1e-3
            spike = np.exp(-(ac / wide) ** 2 * 2.0) * smooth(-0.02, 0.06, al) * np.clip(1 - al / reach, 0, 1) ** 0.7
            heat = np.maximum(heat, spike * (0.7 + 0.5 * n))
        out.append(ramp(heat))
    return out


def sheet_image(atlas_rgba):
    rgb = srgb(atlas_rgba[..., :3])
    if atlas_rgba.shape[-1] == 4:
        rgb = np.concatenate([rgb, atlas_rgba[..., 3:]], -1)
    return Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    rng = np.random.default_rng(1965)
    sm, gl = SHEETS["smoke"], SHEETS["glow"]

    smoke = np.zeros((SMOKE[1], SMOKE[0], 4), np.float32)
    rgb, a = load("Explosion01-light-nofire", "Explosion01-light-nofire_5x5.tga")
    for i, f in enumerate(frames(premultiplied(shade(rgb, a), a), 5, 5)):
        put(smoke, sm["dust"], i, f)
    brgb, ba = load("Explosion00", "Explosion00_5x5.tga")
    for i, f in enumerate(frames(premultiplied(brgb, ba), 5, 5)):
        put(smoke, sm["blast"], i, f)
    rgb, a = load("WispySmoke01", "WispySmoke01_8x8.tga")
    for i, f in enumerate(frames(premultiplied(shade(rgb, a), a), 8, 8)):
        put(smoke, sm["wisp"], i, f)
    rgb, a = load("CandleSmoke01", "CandleSmoke01_20x4.tga")
    curl = resize(premultiplied(shade(rgb, a), a), 20 * sm["curl"]["w"], 4 * sm["curl"]["h"])
    for i, f in enumerate(frames(curl, 20, 4)):
        put(smoke, sm["curl"], i, f)
    for i, f in enumerate(specks(rng)):
        put(smoke, sm["speck"], i, f)
    for i, f in enumerate(brass()):
        put(smoke, sm["brass"], i, f)

    glow = np.zeros((GLOW[1], GLOW[0], 3), np.float32)
    for i, f in enumerate(frames(fire_only(brgb, ba), 5, 5)):
        put(glow, gl["fire"], i, f)
    frgb, _ = load("FireBall01", "FireBall01_8x8.tga")
    flame = list(frames(frgb, 8, 8))
    for i in range(32):
        put(glow, gl["flame"], i, flame[i * 2])
    for i, f in enumerate(tongues(flame)):
        put(glow, gl["tongue"], i, f)
    for i, f in enumerate(stars(flame)):
        put(glow, gl["star"], i, f)

    sheet_image(smoke).save(os.path.join(args.out, "fx_smoke.png"), optimize=True)
    sheet_image(glow).save(os.path.join(args.out, "fx_glow.png"), optimize=True)

    # To look at (Logs/fx/): the smoke over the green of the ground, the glow as it is.
    os.makedirs(SEEN, exist_ok=True)
    over = np.array([0.10, 0.14, 0.06], np.float32)
    seen = smoke[..., :3] + over * (1 - smoke[..., 3:])
    sheet_image(seen).resize((1024, 1024), Image.LANCZOS).save(os.path.join(SEEN, "fx_smoke_seen.jpg"), quality=88)
    sheet_image(glow).save(os.path.join(SEEN, "fx_glow_seen.jpg"), quality=88)
    for group, size in (("smoke", SMOKE), ("glow", GLOW)):
        for name, s in SHEETS[group].items():
            rows = (s["frames"] + s["cols"] - 1) // s["cols"]
            assert s["x"] + s["cols"] * s["px"] <= size[0] + s["px"] - s["w"] and s["y"] + rows * s["py"] <= size[1] + s["py"] - s["h"], name
            print(f"{group:5} {name:6} {s['frames']:3} frames of {s['w']} x {s['h']} at {s['x']}, {s['y']}")


if __name__ == "__main__":
    main()
