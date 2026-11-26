#!/usr/bin/env python3
"""A stand-in for the mod sites during the UI smoke test, so it runs offline and the same way
every time: a Thunderstore community with two mods, their details, icons and a download.
Nexus GraphQL answers 503 (shows the error path). Usage: fake_sites.py <port>"""
import io
import json
import struct
import sys
import zipfile
import zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1])
BASE = f"http://127.0.0.1:{PORT}"


def png(r, g, b, size=64):
    row = b"\x00" + bytes([r, g, b]) * size
    raw = zlib.compress(row * size)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) + chunk(b"IDAT", raw) + chunk(b"IEND", b"")


def mod_zip():
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("manifest.json", json.dumps({"name": "MoreCompany", "version_number": "1.14.0"}))
        z.writestr("BepInEx/plugins/MoreCompany.dll", b"\0" * 2048)
    return buf.getvalue()


MODS = [
    ("notnotnotswipez", "MoreCompany", "A stable lobby player count expansion mod.", 1117467, "1.14.0", (90, 140, 230)),
    ("BepInEx", "BepInExPack", "BepInEx pack for Mono Unity games.", 34514741, "5.4.2100", (230, 120, 80)),
]


def listing():
    return {"count": len(MODS), "next": None, "previous": None, "results": [
        {"categories": [{"id": "1", "name": "Mods", "slug": "mods"}], "community_identifier": "lethal-company",
         "description": d, "download_count": n, "icon_url": f"{BASE}/icons/{name}.png", "is_nsfw": False,
         "last_updated": "2026-04-25T01:28:42Z", "datetime_created": "2023-11-16T04:46:27Z", "name": name,
         "namespace": ns, "rating_count": 500} for ns, name, d, n, _, _ in MODS]}


def package(ns, name):
    for m in MODS:
        if m[0] == ns and m[1] == name:
            deps = ["BepInEx-BepInExPack-5.4.2100"] if name == "MoreCompany" else []
            return {"namespace": ns, "name": name, "full_name": f"{ns}-{name}", "owner": ns,
                    "date_updated": "2026-04-25T01:28:42Z", "latest": {
                        "version_number": m[4], "description": m[2], "icon": f"{BASE}/icons/{name}.png",
                        "dependencies": deps, "download_url": f"{BASE}/download/{ns}-{name}.zip", "downloads": m[3]},
                    "community_listings": [{"categories": ["Mods"], "community": "lethal-company"}]}
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, code, body, kind="application/json"):
        if isinstance(body, (dict, list)):
            body = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?")[0]
        parts = [p for p in path.split("/") if p]
        if path.startswith("/api/cyberstorm/listing/"):
            return self.send(200, listing())
        if path.startswith("/api/cyberstorm/community/"):
            return self.send(200, {"package_categories": [{"id": "1", "name": "Mods", "slug": "mods"}, {"id": "2", "name": "Cosmetics", "slug": "cosmetics"}]})
        if path.startswith("/api/experimental/package/") and path.endswith("/readme/"):
            return self.send(200, {"markdown": f"# {parts[4]}\n\nA mod used by Luma's UI smoke test.\n\n* Works offline\n* Has a dependency"})
        if path.startswith("/api/experimental/package/") and len(parts) == 5:
            p = package(parts[3], parts[4])
            return self.send(200, p) if p else self.send(404, {"detail": "Not found."})
        if path.startswith("/icons/"):
            name = parts[-1][:-4]
            color = next((m[5] for m in MODS if m[1] == name), (128, 128, 128))
            return self.send(200, png(*color), "image/png")
        if path.startswith("/download/"):
            return self.send(200, mod_zip(), "application/zip")
        self.send(404, {"detail": "Not found."})

    def do_POST(self):
        self.send(503, {"errors": [{"message": "Smoke test: no Nexus here."}]})


ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
