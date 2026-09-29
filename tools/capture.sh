#!/bin/zsh
# FrameCapture (PLAN §7): render the real game at a frozen moment and measure it.
#
#   tools/capture.sh                                   seed 3, tick 700, camera at x 0
#   tools/capture.sh seed=3,tick=700,x=-30,out=captures/firebase.png
#   tools/capture.sh frames=2,out=captures/still.png   two consecutive frames, for flicker
#
# Renders through Unity's play mode with the GPU (never -nographics), writes the
# PNG and a .json of render counters beside it, then runs LookMeter on it.
set -u
here="${0:A:h}"
proj="${here:h}"
spec="${1:-seed=3,tick=700,x=0,out=captures/frame.png}"
[[ "$spec" == *out=* ]] || spec="$spec,out=captures/frame.png"
out="${spec##*out=}"; out="${out%%,*}"

UNITY_LOG="${UNITY_LOG:-$proj/Logs/capture.log}" "$here/unity.sh" \
  -runTests -testPlatform PlayMode -testCategory Capture \
  -testResults "$proj/Logs/capture-results.xml" -lovCapture "$spec"
code=$?
[[ -f "$proj/$out" || -f "$out" ]] || { echo "capture.sh: no image written ($out)" >&2; exit 1; }

PY="${LOV_PYTHON:-python3}"
if "$PY" -c "import numpy, PIL" 2>/dev/null; then
  "$PY" "$here/lookmeter.py" "$proj/$out"
else
  echo "capture.sh: LookMeter needs numpy and Pillow (pip install -r tools/requirements.txt); set LOV_PYTHON to a python that has them"
fi
exit $code
