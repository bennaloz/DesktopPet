"""The phone version (a PWA): export/web/, a folder to put on any web server, plus export/<name>-web.zip of it.

    python tools/build_web.py --pets bretzel,sally,zaira --default bretzel

The pets' brains (src/Core) are compiled to WebAssembly (web/brain, `dotnet publish`, no workload needed); the page
(web/) draws them with three.js from the same GLBs and profiles as the desktop game. The service worker keeps
everything on the phone after the first visit; each build gets a new version, so the phone picks it up on the next
opening. iPhone: open the address in Safari, Share, "Aggiungi alla schermata Home". The offline part (service
worker) needs https (or localhost); without it the page still works while online.
"""
import argparse, datetime, json, os, shutil, subprocess, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEB = os.path.join(ROOT, "web")
PAGE = ["index.html", "main.js", "pet.js", "props.js", "manifest.webmanifest"]


def icons(out):
    """The program's icon at the sizes a phone wants."""
    from PIL import Image
    src = Image.open(os.path.join(ROOT, "icon.png")).convert("RGBA")
    d = os.path.join(out, "icons"); os.makedirs(d, exist_ok=True)
    for name, size in (("icon-192.png", 192), ("icon-512.png", 512), ("apple-touch-icon.png", 180)):
        img = src.resize((size, size), Image.LANCZOS)
        if name.startswith("apple"):     # iOS shows no transparency: on the page's background
            bg = Image.new("RGBA", img.size, (239, 230, 216, 255)); bg.alpha_composite(img); img = bg.convert("RGB")
        img.save(os.path.join(d, name))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pets", default="bretzel,sally,zaira", help="comma-separated pets to include")
    ap.add_argument("--default", default=None, help="the pet first shown (default: the first of --pets)")
    ap.add_argument("--name", default="DesktopPet")
    a = ap.parse_args()
    pets = [p.strip() for p in a.pets.split(",") if p.strip()]
    default = a.default or pets[0]
    if default not in pets: sys.exit(f"--default {default} non e' fra --pets")
    for p in pets:
        if not os.path.isfile(os.path.join(ROOT, "cats", p, "profile.json")): sys.exit(f"animale sconosciuto: {p}")

    out = os.path.join(ROOT, "export", "web")
    # emptied rather than removed: a local web server may be serving from it
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        p = os.path.join(out, f)
        shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)

    # the brain
    pub = os.path.join(ROOT, "export", "web-brain")
    shutil.rmtree(pub, ignore_errors=True)
    subprocess.run(["dotnet", "publish", os.path.join(WEB, "brain", "PetBrain.Wasm.csproj"), "-c", "Release",
                    "-o", pub, "-nologo", "-v", "q"], check=True, cwd=ROOT)
    fw = os.path.join(pub, "wwwroot", "_framework")
    os.makedirs(os.path.join(out, "_framework"))
    for f in os.listdir(fw):
        if not f.endswith((".gz", ".br")):     # a plain web server does not negotiate the compressed copies
            shutil.copy2(os.path.join(fw, f), os.path.join(out, "_framework", f))
    shutil.rmtree(pub, ignore_errors=True)

    # the page and the pets
    for f in PAGE: shutil.copy2(os.path.join(WEB, f), os.path.join(out, f))
    icons(out)
    listed = []
    for p in [default] + [p for p in pets if p != default]:
        src = os.path.join(ROOT, "cats", p)
        prof = json.load(open(os.path.join(src, "profile.json"), encoding="utf-8"))
        os.makedirs(os.path.join(out, "cats", p))
        for f in ("profile.json", prof["model"]): shutil.copy2(os.path.join(src, f), os.path.join(out, "cats", p, f))
        listed.append({"id": p, "name": prof.get("name") or p.capitalize()})
    json.dump(listed, open(os.path.join(out, "pets.json"), "w", encoding="utf-8"), ensure_ascii=False)

    # the service worker: this build's version and everything to keep
    files = ["./"] + sorted(os.path.relpath(os.path.join(d, f), out).replace("\\", "/")
                            for d, _, fs in os.walk(out) for f in fs)
    version = datetime.datetime.now().strftime("%Y%m%d%H%M%S")
    sw = open(os.path.join(WEB, "sw.js"), encoding="utf-8").read()
    sw = sw.replace("__VERSION__", version).replace("__FILES__", json.dumps(files + ["sw.js"]))
    open(os.path.join(out, "sw.js"), "w", encoding="utf-8", newline="\n").write(sw)

    zpath = os.path.join(ROOT, "export", f"{a.name}-web.zip")
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for d, _, fs in os.walk(out):
            for f in fs: z.write(os.path.join(d, f), os.path.join("web", os.path.relpath(os.path.join(d, f), out)))
    size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(out) for f in fs) / 1e6
    print(f"fatto: {out} ({size:.0f} MB) e {zpath}, con {', '.join(pets)}; prima {default}; versione {version}")


if __name__ == "__main__":
    main()
