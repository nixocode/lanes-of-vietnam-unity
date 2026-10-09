#!/bin/zsh
# Fetch the free sources the game's audio is cut from that are not Sonniss's
# (ASSETS.md, "Audio"), and check their hashes:
#
#   SourceArt/audio/commons/   three videos of US forces firing, public domain
#                              (works of the US government), from Wikimedia
#                              Commons: the launchers' and the mortar's reports
#   SourceArt/audio/music/     two pieces by Kevin MacLeod (incompetech.com),
#                              Creative Commons Attribution 4.0
#
# The videos' soundtracks are written beside them as WAV by ffmpeg. There is
# none on the Mac by default: `tools/.venv/bin/python -m pip install
# imageio-ffmpeg` puts one inside the tools' own environment, and this script
# finds it there. tools/audio/slice_shots.py builds the game's files from these.
set -eu
here="${0:A:h}"; art="${here:h:h}/SourceArt/audio"
UA="lanes-of-vietnam-asset-fetch/1.0 (github.com/nixocode/lanes-of-vietnam-unity)"
q() { python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$1"; }
fetch() { # folder, file as saved, url, sha256
  local dst="$art/$1"; mkdir -p "$dst"
  [[ -f "$dst/$2" ]] || curl -sfL --max-time 1800 -A "$UA" -o "$dst/$2" "$3"
  echo "$4  $dst/$2" | shasum -a 256 -c -
}
C="https://upload.wikimedia.org/wikipedia/commons"
fetch commons kw26_mortar_lfx.webm "$C/7/77/KW26_Mortar_LFX%2C_Baturaja_Combat_Training_Center%2C_Indonesia_2026_%28NO_GRAPHIC%29_%281022331%29.webm" 0e19a5343bd9717845096145e87607398a75cd21c1e4af4e6803f1eb07c63f60
fetch commons m203_ifs.ogv "$C/4/4b/M203_Grenade_Launcher_IFS.ogv" 04f4d7f1dfb0b1c54e95e6203ce9d7cef6c00f7bf469fc1f573a04cc38f11174
fetch commons paratroopers_rpg.ogv "$C/4/4d/U.S._Paratroopers_Fire_Polish_RPGs.ogv" 87f4730097a76df4b2ebf3e4f744a561bfe58f78fcfe5477b1cae8139a6aea34
M="https://incompetech.com/music/royalty-free/mp3-royaltyfree"
fetch music "Drums of the Deep.mp3" "$M/$(q "Drums of the Deep.mp3")" da4aa5740309886af08c0f474eb583c97ecac9e5c4ab624c3ba1aa970d0a50c9
fetch music "Crypto.mp3" "$M/Crypto.mp3" f09ba8bff00baca85fcdcde3b7f5d6b34a09c2c0b44b007a9123ee42f7d1bcf2

ff=$("${here:h}/.venv/bin/python" -c "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())" 2>/dev/null || command -v ffmpeg || true)
[[ -n "$ff" ]] || { echo "fetch_free.sh: no ffmpeg (tools/.venv/bin/python -m pip install imageio-ffmpeg)" >&2; exit 1; }
for f in kw26_mortar_lfx.webm m203_ifs.ogv paratroopers_rpg.ogv; do
  [[ -f "$art/commons/${f%.*}.wav" ]] || "$ff" -hide_banner -loglevel error -y -i "$art/commons/$f" -vn -ac 2 -ar 48000 "$art/commons/${f%.*}.wav"
done
echo "fetch_free.sh: sources in $art/commons and $art/music"
