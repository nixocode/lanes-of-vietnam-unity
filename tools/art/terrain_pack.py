"""
Pack the ground's scanned layers into the two texture arrays the ground shader
samples (PLAN §12.10, "terrain materials: albedo measured, not guessed").

    tools/.venv/bin/python tools/art/terrain_pack.py            # 1K per layer
    tools/.venv/bin/python tools/art/terrain_pack.py --size 2048

Sources are the 2K PNG downloads in SourceArt/ (gitignored, re-fetchable; the
URLs and hashes are in ASSETS.md). Outputs, in Assets/_Project/Art/Terrain/,
one pair per layer (slice i, name n):

    ground_<i>_<n>_albedo.png   RGB albedo (sRGB), A height
    ground_<i>_<n>_normal.png   tangent-space normal (OpenGL, +Y)
    ground_layers.json          per layer: source, scan size, tile size,
                                measured albedo — what the shader's tiling is
                                set from

Separate textures, not a texture array: Unity's crunch compression, which cuts
the download of DXT data by about two thirds, does not take arrays (it falls
back to uncompressed RGBA), and eight textures sit well inside WebGL2's sixteen
fragment texture units.

Why 1K. At this camera the nearest ground is 23 m away and a pixel there
covers ~7.7 mm across; under the field glasses (7 degrees) ~2.8 mm. A 1K layer
tiled every 2.5 m is 2.4 mm a texel, so 1K already out-resolves the screen and
2K would be four times the download for detail no pixel can show.

One step is not a straight copy: each albedo is flattened at low frequency (its
luminance divided by a wrap-around blur of itself, scales above ~1/6 of the
tile). A scan's broad bright or dark patch is what makes a tiled texture read
as tiles from a distance; the shader puts large-scale variation back as noise
in world space, where it does not repeat.
"""
import argparse
import json
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Terrain")

# Slice order is the shader's: 0 grass floor, 1 verge, 2 track, 3 disturbed earth.
# Chosen by measured hue, chroma and albedo against TARGET.jpg's ground and by
# eye, tiled 2 x 2 (the survey is in ASSETS.md). The verge keeps half its broad
# variation on purpose: its grass clumps on dirt are the feature, not a flaw.
# scan_m is the real width the scan covers (Poly Haven's API "dimensions";
# ambientCG does not publish Ground103's, estimated from its grain against
# Ground109's 2.8 m). tile_m is how wide one repeat is laid in the world.
LAYERS = [
    dict(name="grass_floor", source="ambientcg/Grass003", scan_m=1.4, tile_m=1.6, flatten=0.8,
         diff="ambientcg/Grass003/Grass003_2K-PNG_Color.png",
         nor="ambientcg/Grass003/Grass003_2K-PNG_NormalGL.png",
         disp="ambientcg/Grass003/Grass003_2K-PNG_Displacement.png"),
    dict(name="verge", source="ambientcg/Ground047", scan_m=3.0, tile_m=3.0, flatten=0.5,
         diff="ambientcg/Ground047/Ground047_2K-PNG_Color.png",
         nor="ambientcg/Ground047/Ground047_2K-PNG_NormalGL.png",
         disp="ambientcg/Ground047/Ground047_2K-PNG_Displacement.png"),
    dict(name="track", source="ambientcg/Ground103", scan_m=2.0, tile_m=2.2, flatten=0.8,
         diff="ambientcg/Ground103/Ground103_2K-PNG_Color.png",
         nor="ambientcg/Ground103/Ground103_2K-PNG_NormalGL.png",
         disp="ambientcg/Ground103/Ground103_2K-PNG_Displacement.png"),
    dict(name="disturbed_earth", source="polyhaven/brown_mud_03", scan_m=1.3, tile_m=1.5, flatten=0.9,
         diff="polyhaven/brown_mud_03/brown_mud_03_diff_2k.png",
         nor="polyhaven/brown_mud_03/brown_mud_03_nor_gl_2k.png",
         disp="polyhaven/brown_mud_03/brown_mud_03_disp_2k.png"),
]


def load(path, channels):
    im = Image.open(os.path.join(SRC, path))
    a = np.asarray(im)
    if a.dtype == np.uint16 or im.mode.startswith("I"):
        a = a.astype(np.float64) / 65535.0
    else:
        a = a.astype(np.float64) / 255.0
    if a.ndim == 2:
        a = a[..., None]
    return a[..., :channels]


def to_linear(s):
    return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


def wrap_blur(img, sigma):
    """Gaussian blur on a torus, so a tiling texture stays tiling."""
    h, w = img.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * (np.pi ** 2) * (sigma ** 2) * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(img) * k))


def resize(a, size):
    """Resample each channel in float, so linear data stays linear."""
    out = [np.asarray(Image.fromarray(a[..., c].astype(np.float32), mode="F").resize((size, size), Image.LANCZOS))
           for c in range(a.shape[2])]
    return np.stack(out, -1).astype(np.float64)


def lum(lin):
    return lin @ np.array([0.2126, 0.7152, 0.0722])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=1024)
    size = ap.parse_args().size
    os.makedirs(OUT, exist_ok=True)

    meta = []
    for i, L in enumerate(LAYERS):
        lin = to_linear(load(L["diff"], 3))
        before = lum(lin)
        Y = before.mean()
        # Flatten broad patches: divide by a blur of the luminance.
        blur = wrap_blur(before, lin.shape[0] / 12.0)
        ratio = np.clip(Y / np.maximum(blur, 1e-4), 0.25, 4.0) ** L["flatten"]
        lin = lin * ratio[..., None]
        lin *= Y / lum(lin).mean()                 # keep the measured mean albedo exactly
        lin = np.clip(resize(lin, size), 0, 1)

        h = load(L["disp"], 1)[..., 0]
        lo, hi = np.percentile(h, [1, 99])
        h = np.clip((h - lo) / max(hi - lo, 1e-6), 0, 1)
        h = np.clip(resize(h[..., None], size)[..., 0], 0, 1)

        n = load(L["nor"], 3) * 2 - 1
        n = resize(n, size)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)

        stem = os.path.join(OUT, f"ground_{i}_{L['name']}")
        rgba = np.concatenate([to_srgb(lin), h[..., None]], -1)
        Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA").save(stem + "_albedo.png", optimize=True)
        Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), "RGB").save(stem + "_normal.png", optimize=True)

        broad_before = wrap_blur(before, before.shape[0] / 12.0)
        m = dict(slice=i, name=L["name"], source=L["source"], scan_m=L["scan_m"], tile_m=L["tile_m"],
                 albedo_linear_rgb=[round(float(v), 4) for v in lin.reshape(-1, 3).mean(0)],
                 albedo_luminance=round(float(Y), 4),
                 broad_variation_before=round(float(broad_before.std() / Y), 4),
                 broad_variation_after=round(float(wrap_blur(lum(lin), size / 12.0).std() / lum(lin).mean()), 4))
        meta.append(m)
        print(f"[terrain] {i} {L['name']:16s} albedo Y {Y:.3f}  broad variation {m['broad_variation_before']:.3f} -> "
              f"{m['broad_variation_after']:.3f}  tile {L['tile_m']} m ({L['tile_m'] / size * 1000:.1f} mm/texel)")

    for stale in ("ground_albedo.png", "ground_normal.png"):          # the earlier strip layout
        for f in (stale, stale + ".meta"):
            if os.path.exists(os.path.join(OUT, f)):
                os.remove(os.path.join(OUT, f))
    with open(os.path.join(OUT, "ground_layers.json"), "w") as f:
        json.dump(dict(size=size, layers=meta), f, indent=1)
    print(f"[terrain] wrote {OUT} ({len(LAYERS)} layers at {size})")


main()
