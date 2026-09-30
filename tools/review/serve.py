"""Server locale dell'editor di revisione delle animazioni (tools/review/).

    python tools/review/serve.py [porta]

Serve la pagina, i GLB veri dei `cats/` e i profili; scrive le decisioni e i feedback in `review/<animale>/`.
Solo libreria standard, solo 127.0.0.1. Vedi docs/superpowers/specs/2026-09-30-review-editor-design.md.
"""
import base64
import json
import os
import re
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse

ROOT = Path(__file__).resolve().parents[2]
NAME = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
MAX_BODY = 20 * 1024 * 1024
TYPES = {
    ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
    ".mjs": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8", ".glb": "model/gltf-binary",
    ".png": "image/png", ".jpg": "image/jpeg",
}
PNG_DATA_URL = "data:image/png;base64,"


class Refused(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def inside(root: Path, *parts: str) -> Path:
    """The path under root, refusing anything that climbs out of it."""
    p = root.joinpath(*parts).resolve()
    if p != root and root not in p.parents:
        raise Refused(403, "fuori dalla cartella")
    return p


def check_name(s: str) -> str:
    if not NAME.match(s):
        raise Refused(400, f"nome non valido: {s!r}")
    return s


class Review:
    """What the API does, separate from HTTP so the tests can drive it through a real server."""

    def __init__(self, root: Path):
        self.root = root.resolve()
        self.cats = self.root / "cats"
        self.review = self.root / "review"

    def pets(self):
        out = []
        for d in sorted(self.cats.iterdir()):
            f = d / "profile.json"
            if d.is_dir() and NAME.match(d.name) and f.is_file():
                profile = json.loads(f.read_text(encoding="utf-8"))
                out.append({"id": d.name, **profile})
        return out

    def pet_dir(self, pet: str) -> Path:
        d = inside(self.cats, check_name(pet))
        if not (d / "profile.json").is_file():
            raise Refused(404, f"animale sconosciuto: {pet}")
        return d

    def state_file(self, pet: str) -> Path:
        self.pet_dir(pet)
        return inside(self.review, pet, "stato.json")

    def read_state(self, pet: str):
        f = self.state_file(pet)
        return json.loads(f.read_text(encoding="utf-8")) if f.is_file() else {}

    def write_state(self, pet: str, state):
        if not isinstance(state, dict) or not all(isinstance(v, dict) and NAME.match(k) for k, v in state.items()):
            raise Refused(400, "lo stato e' un oggetto { clip: { ... } }")
        f = self.state_file(pet)
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text(json.dumps(state, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return state

    def save_feedback(self, pet: str, clip: str, body):
        self.pet_dir(pet)
        check_name(clip)
        if not isinstance(body, dict) or not isinstance(body.get("json"), dict):
            raise Refused(400, "serve { json: {...}, prima: png, dopo: png }")
        images = {}
        for k in ("prima", "dopo"):
            v = body.get(k)
            if v is None:
                continue
            if not isinstance(v, str) or not v.startswith(PNG_DATA_URL):
                raise Refused(400, f"{k}: serve un data URL PNG")
            images[k] = base64.b64decode(v[len(PNG_DATA_URL):], validate=True)
        d = inside(self.review, pet, clip)
        d.mkdir(parents=True, exist_ok=True)
        base = time.strftime("%Y%m%d-%H%M%S")
        n, name = 1, base
        while (d / f"{name}.json").exists():
            n += 1
            name = f"{base}-{n}"
        (d / f"{name}.json").write_text(json.dumps(body["json"], ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        for k, data in images.items():
            (d / f"{name}-{k}.png").write_bytes(data)
        return {"base": name}

    def open_feedback(self, pet: str):
        self.pet_dir(pet)
        d = inside(self.review, pet)
        out = []
        if not d.is_dir():
            return out
        for clip_dir in sorted(p for p in d.iterdir() if p.is_dir()):
            for f in sorted(clip_dir.glob("*.json")):
                data = json.loads(f.read_text(encoding="utf-8"))
                out.append({
                    "clip": clip_dir.name, "base": f.stem, "time": data.get("time"), "note": data.get("note", ""),
                    "summary": data.get("summary", []), "risposta": data.get("risposta", ""),
                    "prima": (clip_dir / f"{f.stem}-prima.png").is_file(),
                    "dopo": (clip_dir / f"{f.stem}-dopo.png").is_file(),
                })
        return out


def make_handler(review: Review):
    page = review.root / "tools" / "review"

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *args):
            sys.stderr.write("review: " + fmt % args + "\n")

        def send(self, code, data: bytes, ctype):
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(data)

        def send_json(self, obj, code=200):
            self.send(code, json.dumps(obj, ensure_ascii=False).encode("utf-8"), TYPES[".json"])

        def body(self):
            n = int(self.headers.get("Content-Length") or 0)
            if n > MAX_BODY:
                raise Refused(413, "troppo grande")
            try:
                return json.loads(self.rfile.read(n) or b"null")
            except (ValueError, UnicodeDecodeError):
                raise Refused(400, "JSON non valido")

        def file(self, path: Path):
            if not path.is_file():
                raise Refused(404, "non trovato")
            self.send(200, path.read_bytes(), TYPES.get(path.suffix.lower(), "application/octet-stream"))

        def route(self, method):
            parts = [unquote(p) for p in urlparse(self.path).path.split("/") if p]
            if method == "GET" and not parts:
                self.send_response(302)
                self.send_header("Location", "/tools/review/index.html")
                self.end_headers()
                return
            if parts[:2] == ["tools", "review"] and method == "GET":
                return self.file(inside(page, *parts[2:]))
            if parts[:1] == ["cats"] and method == "GET" and len(parts) == 3:
                return self.file(inside(review.pet_dir(parts[1]), parts[2]))
            if parts[:1] == ["review"] and method == "GET" and len(parts) == 4 and parts[3].endswith(".png"):
                check_name(parts[1]); check_name(parts[2])
                return self.file(inside(review.review, *parts[1:]))
            if parts[:1] != ["api"]:
                raise Refused(404, "non trovato")
            api = parts[1:]
            if method == "GET" and api == ["pets"]:
                return self.send_json(review.pets())
            if api[:1] == ["state"] and len(api) == 2:
                if method == "GET":
                    return self.send_json(review.read_state(api[1]))
                if method == "PUT":
                    return self.send_json(review.write_state(api[1], self.body()))
            if api[:1] == ["feedback"]:
                if method == "GET" and len(api) == 2:
                    return self.send_json(review.open_feedback(api[1]))
                if method == "POST" and len(api) == 3:
                    return self.send_json(review.save_feedback(api[1], api[2], self.body()))
            raise Refused(404, "non trovato")

        def handle_method(self, method):
            try:
                self.route(method)
            except Refused as e:
                self.send_json({"errore": str(e)}, e.code)

        def do_GET(self):
            self.handle_method("GET")

        def do_PUT(self):
            self.handle_method("PUT")

        def do_POST(self):
            self.handle_method("POST")

    return Handler


def make_server(root: Path = ROOT, port: int = 8765) -> ThreadingHTTPServer:
    return ThreadingHTTPServer(("127.0.0.1", port), make_handler(Review(root)))


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    server = make_server(ROOT, port)
    print(f"Editor di revisione: http://127.0.0.1:{port}/  (Ctrl+C per chiudere)")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
