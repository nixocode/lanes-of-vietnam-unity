#!/usr/bin/env python3
"""
LookMeter — brief §4's acceptance test for the look, as numbers.

    tools/lookmeter.py --reference            self-check: must reproduce the brief's table
    tools/lookmeter.py captures/frame.png     measure a capture (full frame; the play area is cropped)
    tools/lookmeter.py shot.png --play        the image is already just the play area
    tools/lookmeter.py a.png --json           machine-readable

Metric definitions are the three.js build's tools/measure.ts, which recovered
them by reproducing the brief's own table off reference/TARGET.jpg:

    L*          mean CIELAB L* (D65) of the final 8-bit sRGB pixels
    saturation  mean HSV S x 100 over the same pixels

Bands are fractions of play-area height (0 / .2759 / .4914 / .6897 / 1), so a
render of any size is measured on the same terms as the reference.

The whole-frame histogram is reported with equal weight to the bands (PLAN §7).
The three.js build reached a band table within a point of the reference while
the frame read flat and lifeless: a band mean cannot see flatness, so the
percentiles, spread and the 10-L* histogram sit right beside it.

Needs numpy and Pillow (tools/requirements.txt).
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
REFERENCE = ROOT / "reference" / "TARGET.jpg"

# The reference's play area, in its own 2772 x 1504 pixels: inside the frame,
# between the command strip and the deck. A capture at any size is cropped by
# the same fractions, so both are measured over the same part of the picture.
REF_W, REF_H = 2772, 1504
PLAY = (70, 247, 2757, 1291)            # x0, y0, x1, y1 (exclusive)

BANDS = [
    ("sky + mountains", 0.0, 0.2759),
    ("treeline", 0.2759, 0.4914),
    ("far lane", 0.4914, 0.6897),
    ("near lane", 0.6897, 1.0),
]

TARGET_BANDS = {
    "sky + mountains": (69.7, 11.1),
    "treeline": (40.2, 16.7),
    "far lane": (32.8, 21.8),
    "near lane": (31.2, 33.8),
}
TARGET_FRAME = dict(p2=4, p50=41, p98=93, range=89, sd=24.7, dark=17.8, light=15.8)


def srgb_to_linear(c):
    v = c / 255.0
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)


def lstar(rgb):
    """CIELAB L* (D65) of an HxWx3 uint8 array."""
    lin = srgb_to_linear(rgb.astype(np.float64))
    y = 0.2126729 * lin[..., 0] + 0.7151522 * lin[..., 1] + 0.0721750 * lin[..., 2]
    d = 6 / 29
    f = np.where(y > d ** 3, np.cbrt(y), y / (3 * d * d) + 4 / 29)
    return 116 * f - 16


def hsv_sat(rgb):
    x = rgb.astype(np.float64)
    mx = x.max(axis=-1)
    mn = x.min(axis=-1)
    with np.errstate(invalid="ignore", divide="ignore"):
        s = np.where(mx > 0, (mx - mn) / mx, 0.0)
    return s * 100


def play_area(img, already_play):
    if already_play:
        return img
    h, w = img.shape[:2]
    x0 = round(PLAY[0] / REF_W * w)
    y0 = round(PLAY[1] / REF_H * h)
    x1 = round(PLAY[2] / REF_W * w)
    y1 = round(PLAY[3] / REF_H * h)
    return img[y0:y1, x0:x1]


def measure(rgb):
    L = lstar(rgb)
    S = hsv_sat(rgb)
    h = rgb.shape[0]
    flat = np.sort(L.ravel())

    def pct(p):
        i = p / 100 * (flat.size - 1)
        lo, hi = int(np.floor(i)), int(np.ceil(i))
        return float(flat[lo] + (flat[hi] - flat[lo]) * (i - lo))

    bands = []
    for name, a, b in BANDS:
        y0 = round(a * h)
        y1 = max(y0 + 1, round(b * h))
        bl, bs = L[y0:y1], S[y0:y1]
        bands.append(dict(name=name, lstar=float(bl.mean()), sat=float(bs.mean()),
                          sd=float(bl.std()), pixels=int(bl.size)))
    hist, _ = np.histogram(L, bins=10, range=(0, 100))
    p2, p98 = pct(2), pct(98)
    return dict(
        width=int(rgb.shape[1]), height=int(h),
        mean=float(L.mean()), p2=p2, p50=pct(50), p98=p98, range=p98 - p2,
        sd=float(L.std()), dark=float((L < 20).mean() * 100), light=float((L > 75).mean() * 100),
        sat=float(S.mean()), bands=bands,
        hist=[float(x) for x in hist / L.size * 100],
    )


def monotonic(st):
    notes = []
    b = st["bands"]
    for i in range(1, len(b)):
        if b[i]["lstar"] > b[i - 1]["lstar"]:
            notes.append(f'L* rises from "{b[i-1]["name"]}" to "{b[i]["name"]}" '
                         f'({b[i-1]["lstar"]:.1f} -> {b[i]["lstar"]:.1f}); distance must lighten')
        if b[i]["sat"] < b[i - 1]["sat"]:
            notes.append(f'saturation falls from "{b[i-1]["name"]}" to "{b[i]["name"]}" '
                         f'({b[i-1]["sat"]:.1f} -> {b[i]["sat"]:.1f}); the near ground must be the most saturated')
    return notes


def report(st, label, ref=None):
    out = [f"{label}  {st['width']}x{st['height']} (play area)", ""]
    out.append("  band               L*   target   delta |     S   target   delta")
    out.append("  " + "-" * 66)
    for b in st["bands"]:
        tl, ts = TARGET_BANDS[b["name"]]
        out.append(f"  {b['name']:<16}{b['lstar']:6.1f}   {tl:6.1f}  {b['lstar']-tl:+6.1f} |"
                   f"{b['sat']:6.1f}   {ts:6.1f}  {b['sat']-ts:+6.1f}")
    out.append("")
    # Two targets for the whole frame. The brief's figures were taken over a
    # slightly different crop than its bands (this ruler reads the reference's
    # p2 as 8.0 where the brief says 4), so the fair comparison is against
    # the reference measured by this same ruler; the brief's column stays for
    # the record.
    out.append("  whole frame        value      ref   delta   (brief)")
    rows = [("L* p2", "p2"), ("L* p50", "p50"), ("L* p98", "p98"), ("L* range", "range"),
            ("L* std dev", "sd"), ("dark % (<20)", "dark"), ("light % (>75)", "light")]
    for name, k in rows:
        t = TARGET_FRAME[k]
        r = ref[k] if ref else t
        out.append(f"  {name:<16}{st[k]:7.1f}   {r:6.1f}  {st[k]-r:+6.1f}   ({t:.1f})")
    out.append(f"  {'L* mean':<16}{st['mean']:7.1f}")
    out.append(f"  {'saturation':<16}{st['sat']:7.1f}")
    out.append("")
    # The histogram, beside the reference's if we have it. Equal weight to the
    # bands: flatness shows here and nowhere else.
    out.append("  L* histogram (% of pixels)" + ("      frame  reference" if ref else ""))
    for i in range(10):
        bar = "#" * int(round(st["hist"][i] / 1.0))
        line = f"    {i*10:3d}-{i*10+9:<3d} {st['hist'][i]:5.1f} {bar:<30}"
        if ref:
            line += f" {ref['hist'][i]:5.1f}"
        out.append(line)
    notes = monotonic(st)
    out.append("")
    out.append("  monotonic across bands: " + ("yes" if not notes else "NO\n    " + "\n    ".join(notes)))
    return "\n".join(out)


def load(path):
    return np.asarray(Image.open(path).convert("RGB"))


def reference_stats():
    img = load(REFERENCE)
    assert img.shape[1] == REF_W and img.shape[0] == REF_H, img.shape
    return measure(play_area(img, False))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image", nargs="?")
    ap.add_argument("--reference", action="store_true")
    ap.add_argument("--play", action="store_true", help="the image is already only the play area")
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()

    ref = reference_stats()
    if a.reference:
        print(report(ref, "reference/TARGET.jpg"))
        # The instrument's zero: it must reproduce the brief's table from the
        # image the table was taken from. The three.js harness did so within
        # 1.2 L* and 1.1 saturation; anything worse means the ruler moved.
        worst_l = max(abs(b["lstar"] - TARGET_BANDS[b["name"]][0]) for b in ref["bands"])
        worst_s = max(abs(b["sat"] - TARGET_BANDS[b["name"]][1]) for b in ref["bands"])
        ok = worst_l <= 1.5 and worst_s <= 1.5
        print(f"\n  self-check: worst band error {worst_l:.2f} L*, {worst_s:.2f} S — {'ok' if ok else 'FAILED'}")
        sys.exit(0 if ok else 1)
    if not a.image:
        ap.error("an image, or --reference")
    st = measure(play_area(load(a.image), a.play))
    if a.json:
        print(json.dumps(st, indent=2))
    else:
        print(report(st, a.image, ref))


if __name__ == "__main__":
    main()
