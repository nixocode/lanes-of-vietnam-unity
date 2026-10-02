#!/bin/zsh
# A strip of frames of one moment of a match, as one image: what the men do
# over a few seconds, to look at before the owner does.
#
#   tools/strip.sh seed=3,tick=900,x=0                 eight frames, half a second apart
#   tools/strip.sh seed=3,tick=900,x=0,every=1,n=6     six frames, a second apart
#   tools/strip.sh seed=3,tick=900,x=0,dolly=0.6,out=captures/contact.png
#
# Any other key is passed to tools/capture.sh. The sheet is written to out=
# (default captures/strip.png); the frames it is made of are removed.
set -u
here="${0:A:h}"
proj="${here:h}"
spec="${1:-seed=3,tick=900,x=0}"
every=0.5; n=8; out="captures/strip.png"; rest=()
for kv in ${(s:,:)spec}; do
  case "$kv" in
    every=*) every="${kv#every=}" ;;
    n=*) n="${kv#n=}" ;;
    out=*) out="${kv#out=}" ;;
    *) rest+=("$kv") ;;
  esac
done
movie=$(python3 -c "print(1/float('$every'))")
tmp="captures/.strip/f.png"
mkdir -p "$proj/captures/.strip"; rm -f "$proj"/captures/.strip/*(N)
"$here/capture.sh" "${(j:,:)rest},frames=$n,movie=$movie,w=960,h=520,ui=0,out=$tmp" >/dev/null 2>&1
PY="${LOV_PYTHON:-$here/.venv/bin/python}"; [[ -x "$PY" ]] || PY=python3
"$PY" - "$proj/captures/.strip" "$proj/$out" "$every" <<'PYEOF'
import sys, glob, os, re
from PIL import Image, ImageDraw
src, out, every = sys.argv[1], sys.argv[2], float(sys.argv[3])
files = sorted(glob.glob(os.path.join(src, "*.png")), key=lambda f: [int(x) for x in re.findall(r"\d+", os.path.basename(f))] or [0])
if not files: sys.exit("strip.sh: no frames were written (see Logs/capture.log)")
ims = [Image.open(f).convert("RGB") for f in files]
w, h = ims[0].size
cols = 2 if len(ims) <= 8 else 3
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h))
d = ImageDraw.Draw(sheet)
for i, im in enumerate(ims):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im, (x, y))
    d.rectangle([x + 6, y + 6, x + 86, y + 26], fill=(0, 0, 0))
    d.text((x + 10, y + 10), f"+{i * every:.1f} s", fill=(255, 255, 255))
sheet.save(out)
print(f"strip.sh: {len(ims)} frames, {every} s apart -> {out} ({sheet.size[0]}x{sheet.size[1]})")
PYEOF
code=$?
rm -rf "$proj/captures/.strip"
exit $code
