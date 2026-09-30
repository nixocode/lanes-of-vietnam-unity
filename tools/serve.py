#!/usr/bin/env python3
"""
Serve a Unity WebGL build locally, with the headers a Brotli build needs.

    tools/serve.py                       Builds/web on http://127.0.0.1:8065
    tools/serve.py Builds/web --port 8080

The build ships Brotli-compressed with no decompression fallback (PLAN §6), so
the server must say so: every `*.br` file goes out with `Content-Encoding: br`
and the content type of what is inside it (`.wasm.br` is application/wasm,
which is also what lets the browser compile it while it streams). A server that
sends these as plain octet-streams gets a loader error, not a slow load.

Local only: it binds 127.0.0.1. Stop it with Ctrl-C (or kill the process) —
nothing here should be left running.
"""
import argparse
import functools
import http.server
import os
import sys

TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".json": "application/json",
    ".symbols.json": "application/json",
}


class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        # The content type comes from guess_type below; this adds the encoding.
        if self.translate_path(self.path).endswith(".br"):
            self.send_header("Content-Encoding", "br")
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def guess_type(self, path):
        if path.endswith(".br"):
            inner = path[:-3]
            for e in sorted(TYPES, key=len, reverse=True):
                if inner.endswith(e):
                    return TYPES[e]
            return "application/octet-stream"
        if path.endswith(".wasm"):
            return "application/wasm"
        return super().guess_type(path)

    def log_message(self, fmt, *args):
        sys.stderr.write("  %s\n" % (fmt % args))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir", nargs="?", default="Builds/web")
    ap.add_argument("--port", type=int, default=8065)
    a = ap.parse_args()
    if not os.path.isfile(os.path.join(a.dir, "index.html")):
        sys.exit(f"serve.py: no index.html in {a.dir} — build first (tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL)")
    handler = functools.partial(Handler, directory=a.dir)
    httpd = http.server.ThreadingHTTPServer(("127.0.0.1", a.port), handler)
    print(f"serving {a.dir} on http://127.0.0.1:{a.port}/  (Ctrl-C to stop)", flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
