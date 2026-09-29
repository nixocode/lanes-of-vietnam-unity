#!/usr/bin/env python3
"""
Flicker — frame-to-frame difference of a scene holding still (PLAN §7).

    tools/flicker.py captures/still.0.png captures/still.1.png [...]

Reports, per consecutive pair, in CIELAB L* over the play area:
    mean |dL*|, p99 |dL*|, % of pixels moving by more than 1 and 3 L*,
and the mean L* of the first frame beside it.

That last number is not decoration. PLAN §9 finding 1: an instrument with no
proven zero is not an instrument. The three.js build diagnosed flicker wrongly
three times running because a harness silently dropped "--wind 0". So the
zero is proven by rendering a frozen scene twice with nothing temporal on
(tools/capture.sh ...,aa=none,frames=2): it must read 0.000 — and at a
plausible mean L*, because a black frame also differences to zero.
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lookmeter import lstar, load, play_area  # noqa: E402


def main():
    paths = sys.argv[1:]
    if len(paths) < 2:
        print(__doc__)
        sys.exit(2)
    frames = [lstar(play_area(load(p), False)) for p in paths]
    worst = 0.0
    print(f"  first frame mean L* {frames[0].mean():.2f}  ({frames[0].shape[1]}x{frames[0].shape[0]} play area)")
    for i in range(1, len(frames)):
        d = np.abs(frames[i] - frames[i - 1])
        mean = float(d.mean())
        worst = max(worst, mean)
        print(f"  {Path(paths[i-1]).name} -> {Path(paths[i]).name}:  mean |dL*| {mean:.3f}   "
              f"p99 {float(np.percentile(d, 99)):.3f}   >1 L* {float((d > 1).mean() * 100):.2f}%   "
              f">3 L* {float((d > 3).mean() * 100):.2f}%   max {float(d.max()):.2f}")
    sane = 10 < frames[0].mean() < 90
    print(f"  worst pair mean |dL*| {worst:.3f}" + ("" if sane else "   (MEAN L* IMPLAUSIBLE — a broken render also reads zero)"))


if __name__ == "__main__":
    main()
