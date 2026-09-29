#!/bin/zsh
# Run a Photoshop JSX script headlessly-ish through AppleScript's "do javascript".
#   tools/photoshop/run.sh script.jsx [arg ...]
# Arguments reach the script as `arguments[]`. Photoshop opens if it is not
# running; the script's return value is printed.
set -eu
script="${1:A}"; shift
PS_APP="${PS_APP:-Adobe Photoshop 2026}"
args=""
for a in "$@"; do args="$args, \"${a//\"/\\\"}\""; done
osascript -e "tell application \"$PS_APP\" to do javascript (read POSIX file \"$script\" as «class utf8») with arguments {${args#, }}"
