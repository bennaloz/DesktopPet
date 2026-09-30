"""Test del server dell'editor di revisione: python -m unittest tools/review/test_serve.py"""
import base64
import json
import shutil
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import serve  # noqa: E402

PNG = "data:image/png;base64," + base64.b64encode(b"\x89PNG\r\n\x1a\nfinto").decode()


class ServeTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        pet = self.root / "cats" / "bretzel"
        pet.mkdir(parents=True)
        (pet / "profile.json").write_text(json.dumps({"name": "Bretzel", "model": "bretzel.glb", "length_px": 110}))
        (pet / "bretzel.glb").write_bytes(b"glTF-finto")
        (self.root / "cats" / "senza-profilo").mkdir()
        (self.root / "tools" / "review").mkdir(parents=True)
        (self.root / "tools" / "review" / "index.html").write_text("<p>ciao</p>")
        (self.root / "segreto.txt").write_text("no")
        self.server = serve.make_server(self.root, 0)
        self.base = f"http://127.0.0.1:{self.server.server_address[1]}"
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        shutil.rmtree(self.root)

    def call(self, method, path, body=None, raw=None):
        data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
        req = urllib.request.Request(self.base + path, data=data, method=method)
        try:
            with urllib.request.urlopen(req) as r:
                return r.status, r.read(), r.headers.get("Content-Type")
        except urllib.error.HTTPError as e:
            return e.code, e.read(), e.headers.get("Content-Type")

    def test_lists_only_pets_with_a_profile(self):
        code, data, _ = self.call("GET", "/api/pets")
        self.assertEqual(200, code)
        pets = json.loads(data)
        self.assertEqual(["bretzel"], [p["id"] for p in pets])
        self.assertEqual(110, pets[0]["length_px"])

    def test_serves_the_page_and_the_model(self):
        code, data, ctype = self.call("GET", "/cats/bretzel/bretzel.glb")
        self.assertEqual((200, b"glTF-finto", "model/gltf-binary"), (code, data, ctype))
        code, data, ctype = self.call("GET", "/tools/review/index.html")
        self.assertEqual(200, code)
        self.assertTrue(ctype.startswith("text/html"))

    def test_refuses_paths_outside(self):
        for path in ("/tools/review/../../segreto.txt", "/tools/review/%2e%2e/%2e%2e/segreto.txt",
                     "/cats/..%2F/segreto.txt", "/cats/bretzel/..%2F..%2Fsegreto.txt"):
            code, _, _ = self.call("GET", path)
            self.assertIn(code, (400, 403, 404), path)

    def test_refuses_bad_names(self):
        self.assertEqual(400, self.call("GET", "/api/state/..")[0])
        self.assertEqual(400, self.call("POST", "/api/feedback/bretzel/a.b", {"json": {}})[0])
        self.assertEqual(404, self.call("GET", "/api/state/senza-profilo")[0])

    def test_state_round_trip(self):
        self.assertEqual({}, json.loads(self.call("GET", "/api/state/bretzel")[1]))
        state = {"Run": {"stato": "da_rifare", "nota": "piu' allungato", "impronta": "a1b2c3d4"}}
        self.assertEqual(200, self.call("PUT", "/api/state/bretzel", state)[0])
        self.assertEqual(state, json.loads(self.call("GET", "/api/state/bretzel")[1]))
        saved = json.loads((self.root / "review" / "bretzel" / "stato.json").read_text(encoding="utf-8"))
        self.assertEqual(state, saved)

    def test_bad_json_is_400(self):
        self.assertEqual(400, self.call("PUT", "/api/state/bretzel", raw=b"{non json")[0])
        self.assertEqual(400, self.call("PUT", "/api/state/bretzel", ["lista"])[0])

    def test_feedback_writes_json_and_screenshots(self):
        body = {"json": {"clip": "Run", "time": 0.2, "note": "zampe dietro avanti"}, "prima": PNG, "dopo": PNG}
        code, data, _ = self.call("POST", "/api/feedback/bretzel/Run", body)
        self.assertEqual(200, code)
        base = json.loads(data)["base"]
        d = self.root / "review" / "bretzel" / "Run"
        self.assertEqual(body["json"], json.loads((d / f"{base}.json").read_text(encoding="utf-8")))
        self.assertTrue((d / f"{base}-prima.png").read_bytes().startswith(b"\x89PNG"))
        self.assertTrue((d / f"{base}-dopo.png").is_file())
        # a second one in the same second does not overwrite the first
        base2 = json.loads(self.call("POST", "/api/feedback/bretzel/Run", body)[1])["base"]
        self.assertNotEqual(base, base2)
        listed = json.loads(self.call("GET", "/api/feedback/bretzel")[1])
        self.assertEqual({base, base2}, {f["base"] for f in listed})
        self.assertEqual("zampe dietro avanti", listed[0]["note"])
        code, png, ctype = self.call("GET", f"/review/bretzel/Run/{base}-dopo.png")
        self.assertEqual((200, "image/png"), (code, ctype))

    def test_resolved_feedback_is_not_listed(self):
        d = self.root / "review" / "bretzel" / "Run" / "risolti"
        d.mkdir(parents=True)
        (d / "20260930-100000.json").write_text("{}")
        self.assertEqual([], json.loads(self.call("GET", "/api/feedback/bretzel")[1]))

    def test_feedback_needs_png_data_urls(self):
        body = {"json": {}, "prima": "data:text/html;base64,PGI+"}
        self.assertEqual(400, self.call("POST", "/api/feedback/bretzel/Run", body)[0])


if __name__ == "__main__":
    unittest.main()
