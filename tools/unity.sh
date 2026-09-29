#!/bin/zsh
# Run Unity headless against this project and report what matters.
#
#   tools/unity.sh -executeMethod LanesOfVietnam.Tools.ProjectSetup.Apply
#   tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.EmptyFloor
#   UNITY_LOG=Logs/x.log tools/unity.sh ...
#
# Prints compile errors, exceptions and every line tagged [LOV] (the tag this
# project's editor tooling prints its results under), then Unity's exit code.
# Pass -nographics for anything that does not render; leave it off for frame
# capture, which needs the GPU.
#
# One Unity at a time: a second instance on the same project fails with "another
# Unity instance is running", so this refuses early rather than wasting a
# startup.
set -u
here="${0:A:h}"
proj="${here:h}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity}"
log="${UNITY_LOG:-$proj/Logs/batch.log}"
mkdir -p "${log:h}"

if [[ -f "$proj/Temp/UnityLockfile" ]] && lsof "$proj/Temp/UnityLockfile" >/dev/null 2>&1; then
  echo "unity.sh: the project is open in another Unity instance; close it first" >&2
  exit 3
fi

# -runTests quits by itself when the run finishes; with -quit as well, Unity
# exits before the test runner starts, returns 0 and writes no results — a
# green run that tested nothing.
quit=(-quit)
(( ${@[(Ie)-runTests]} )) && quit=()

start=$(date +%s)
"$UNITY" -batchmode "${quit[@]}" -projectPath "$proj" -logFile "$log" "$@"
code=$?
secs=$(( $(date +%s) - start ))

# grep exits 1 on no match; that is not a failure of this script.
grep -E "error CS[0-9]+|Exception:|\[LOV\]|Scripts have compiler errors|Build (succeeded|failed)|Aborting batchmode" "$log" \
  | grep -v "^UnityEngine\.\|^  at " | awk '!seen[$0]++' | head -150 || true
echo "unity.sh: exit $code in ${secs}s (log: $log)"
exit $code
