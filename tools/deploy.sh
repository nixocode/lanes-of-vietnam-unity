#!/bin/zsh
# Stage the WebGL build for a static host and, asked to, deploy it to Vercel.
#
#   tools/deploy.sh            stage and check only: Builds/deploy is what would go up
#   tools/deploy.sh preview    a preview deployment (Vercel keeps previews private to the account)
#   tools/deploy.sh prod       the public one
#
# Build first (tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL).
# The Vercel CLI has to be signed in once, by a person: `vercel login`.
#
# Only the build goes up, never the project folder: the repository's licensed
# sources (Mixamo's FBX files, Sonniss's WAVs) are not in Builds/web and must
# not be on a public host. What is staged is checked against a list of what a
# build is made of, and anything else stops the deploy.
#
# vercel.json is written here, not kept: the page's one inline script is
# allowed by its hash, which changes whenever the WebGL template does.
set -eu
here="${0:A:h}"; proj="${here:h}"
src="$proj/Builds/web"; stage="$proj/Builds/deploy"
PROJECT="${LOV_VERCEL_PROJECT:-lanes-of-vietnam-65}"
[[ -f "$src/index.html" ]] || { echo "deploy.sh: no build in Builds/web" >&2; exit 1; }

mkdir -p "$stage"
# size.json is the build's own report (file list, editor version): nobody playing needs it.
rsync -a --delete --exclude ".vercel" --exclude "size.json" --exclude ".DS_Store" --exclude "*.meta" "$src/" "$stage/"

python3 - "$stage" "$PROJECT" <<'PY'
import base64, hashlib, json, os, re, sys
stage, project = sys.argv[1], sys.argv[2]
html = open(os.path.join(stage, "index.html"), encoding="utf-8").read()
inline = [m.group(1) for m in re.finditer(r"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", html, re.S)]
hashes = " ".join("'sha256-" + base64.b64encode(hashlib.sha256(s.encode("utf-8")).digest()).decode() + "'" for s in inline)
csp = "; ".join([
    "default-src 'self'",
    # The engine compiles WebAssembly; nothing may eval strings. The page's own inline script, by its hash.
    f"script-src 'self' 'wasm-unsafe-eval' {hashes}".strip(),
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "media-src 'self' blob:",
    "font-src 'self' data:",
    # The game talks to nobody but the host it came from.
    "connect-src 'self' blob: data:",
    "worker-src 'self' blob:",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'none'",
    "frame-ancestors 'self'",
    "upgrade-insecure-requests",
])
everywhere = [
    ("Content-Security-Policy", csp),
    ("X-Content-Type-Options", "nosniff"),
    ("X-Frame-Options", "SAMEORIGIN"),
    ("Referrer-Policy", "no-referrer"),
    ("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=(), browsing-topics=()"),
    ("Cross-Origin-Opener-Policy", "same-origin"),
    # Other sites may not pull the build's files (its sounds are licensed for the game, not for them).
    # The host answers every file with "any site may read this" unless it is told otherwise; the game
    # reads its own files from its own address, which needs no such leave.
    ("Cross-Origin-Resource-Policy", "same-origin"),
    ("Access-Control-Allow-Origin", f"https://{project}.vercel.app"),
    ("Vary", "Origin"),
    ("Strict-Transport-Security", "max-age=63072000; includeSubDomains"),
    # A demo handed out by its link: search engines are asked to leave it out. Take this line out to be found.
    ("X-Robots-Tag", "noindex, nofollow"),
]
def h(pairs): return [dict(key=k, value=v) for k, v in pairs]
# The build is Brotli with no fallback (PLAN §6): the host has to say so, with the type of what is inside.
# Its file names do not change from build to build, so nothing may be kept without asking again.
fresh = ("Cache-Control", "public, max-age=0, must-revalidate")
config = {
    "$schema": "https://openapi.vercel.sh/vercel.json",
    "cleanUrls": False,
    "trailingSlash": False,
    "headers": [
        dict(source="/(.*)", headers=h(everywhere)),
        dict(source="/Build/(.*)", headers=h([fresh])),
        dict(source="/StreamingAssets/(.*)", headers=h([fresh])),
        dict(source="/Build/(.*)\\.wasm\\.br", headers=h([("Content-Encoding", "br"), ("Content-Type", "application/wasm")])),
        dict(source="/Build/(.*)\\.js\\.br", headers=h([("Content-Encoding", "br"), ("Content-Type", "application/javascript")])),
        dict(source="/Build/(.*)\\.data\\.br", headers=h([("Content-Encoding", "br"), ("Content-Type", "application/octet-stream")])),
    ],
}
json.dump(config, open(os.path.join(stage, "vercel.json"), "w"), indent=2)

# What a build is made of. Anything else in the staged folder is a mistake, and stops here.
allowed = [r"index\.html", r"vercel\.json", r"Build/web\.(loader\.js|framework\.js\.br|wasm\.br|data\.br)",
           r"StreamingAssets/Audio/audio\.json", r"StreamingAssets/Audio/(licensed/)?[a-z0-9]+_\d+\.m4a"]
bad, total, n = [], 0, 0
for root, dirs, files in os.walk(stage):
    dirs[:] = [d for d in dirs if d != ".vercel"]
    for f in files:
        rel = os.path.relpath(os.path.join(root, f), stage)
        size = os.path.getsize(os.path.join(root, f)); total += size; n += 1
        if not any(re.fullmatch(p, rel) for p in allowed): bad.append(rel)
        if size > 95 * 1024 * 1024: bad.append(rel + " (over 95 MB)")
# (Unity's own loader names localhost in an error message about file:// pages; a home folder or an address it does not.)
for f, local in (("index.html", True), ("StreamingAssets/Audio/audio.json", True), ("Build/web.loader.js", False)):
    text = open(os.path.join(stage, f), encoding="utf-8", errors="ignore").read()
    if re.search(r"/Users/|[A-Za-z]:\\\\Users|@gmail\.com" + (r"|localhost|127\.0\.0\.1" if local else ""), text): bad.append(f + " (a local path, address or host in it)")
if bad:
    print("deploy.sh: not deploying; unexpected in the staged folder:\n  " + "\n  ".join(bad), file=sys.stderr); sys.exit(1)
print(f"deploy.sh: staged {n} files, {total / 1048576:.1f} MB; {len(inline)} inline script(s) allowed by hash")
PY

case "${1:-}" in
  "") echo "deploy.sh: staged only ($stage). 'tools/deploy.sh preview' or 'prod' deploys it." ;;
  preview|prod)
    command -v vercel >/dev/null || { echo "deploy.sh: no vercel CLI (npm i -g vercel)" >&2; exit 1; }
    cd "$stage"
    [[ -f .vercel/project.json ]] || vercel link --yes --project "$PROJECT"
    if [[ "$1" == prod ]]; then vercel deploy --prod --yes; else vercel deploy --yes; fi ;;
  *) echo "usage: tools/deploy.sh [preview|prod]" >&2; exit 2 ;;
esac
