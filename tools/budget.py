#!/usr/bin/env python3
"""
Budget — PLAN §2's numbers, checked, with a non-zero exit when one is missed.

    tools/budget.py                                   latest build + captures/frame.json
    tools/budget.py --build Builds/web --capture captures/frame.json

Checks what can be checked offline:
    build size       initial <= 45 MB, total <= 80 MB   (Builds/<name>/size.json)
    draw calls       <= 400                             (a capture's render counters)
    triangles        <= 3 M visible                     (same)

Frame time and WebGL heap can only be measured in a browser on real hardware
(brief §9 finding 11: an automation browser throttles and read-back
de-accelerates the canvas), so they are reported as not measured here rather
than assumed to pass.
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MB = 1024 * 1024

BUDGET = {
    "initial_mb": 45.0,
    "total_mb": 80.0,
    "draw_calls": 400,
    "triangles": 3_000_000,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", default=None)
    ap.add_argument("--capture", default=str(ROOT / "captures" / "frame.json"))
    a = ap.parse_args()

    rows, failed = [], False

    build = Path(a.build) if a.build else None
    if build is None:
        sizes = sorted((ROOT / "Builds").glob("*/size.json"), key=lambda p: p.stat().st_mtime)
        build = sizes[-1].parent if sizes else None
    if build and (build / "size.json").exists():
        s = json.loads((build / "size.json").read_text())
        for key, label, value in [("initial_mb", "initial download", s["initialBytes"] / MB),
                                  ("total_mb", "total download", s["totalBytes"] / MB)]:
            ok = value <= BUDGET[key]
            failed |= not ok
            rows.append((label, f"{value:.2f} MB", f"<= {BUDGET[key]:.0f} MB", ok, build.name))
    else:
        rows.append(("build size", "no build found", "", None, ""))

    cap = Path(a.capture)
    if cap.exists():
        c = json.loads(cap.read_text())
        if not c.get("renderCounters"):
            rows.append(("draw calls", "counters unavailable", "", None, cap.name))
        else:
            for key, label, value in [("draw_calls", "draw calls", c["drawCalls"]),
                                      ("triangles", "triangles", c["triangles"])]:
                ok = value <= BUDGET[key]
                failed |= not ok
                rows.append((label, f"{value:,}", f"<= {BUDGET[key]:,}", ok, cap.name))
    else:
        rows.append(("render counters", "no capture found", "", None, ""))

    rows.append(("frame time", "not measured", "60 fps @1080p, 60 men", None, "browser, owner's machine"))
    rows.append(("WebGL heap", "not measured", "<= 512 MB", None, "browser"))

    for label, value, limit, ok, src in rows:
        mark = "ok  " if ok else ("FAIL" if ok is False else " -- ")
        print(f"  {mark}  {label:<17} {value:>18}   {limit:<22} {src}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
