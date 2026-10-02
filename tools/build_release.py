"""Builds the desktop pet as a Windows program to give away: a folder with the .exe and the .NET files next to it,
zipped.

    python tools/build_release.py --pets bretzel,sally,zaira --default bretzel --name DesktopPet

--pets: the pets in the build (folders under cats/); the tray menu offers them. --default: the one that shows up the
first time (after that, the last one picked from the menu). Needs Godot's export templates for its version
(%APPDATA%\\Godot\\export_templates\\4.7.2.stable.mono). The export preset is written here each time
(export_presets.cfg, not in git): what goes in is decided by these arguments, not by an editor setting."""
import argparse, os, shutil, subprocess, sys, zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
GODOT = r"C:\develop\personal\tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"

PRESET = """[preset.0]

name="Windows Desktop"
platform="Windows Desktop"
runnable=true
advanced_options=false
dedicated_server=false
custom_features="{features}"
export_filter="all_resources"
include_filter="cats/*/profile.json"
exclude_filter="{exclude}"
export_path="{exe}"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.0.options]

custom_template/debug=""
custom_template/release=""
debug/export_console_wrapper=0
binary_format/embed_pck=true
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
binary_format/architecture="x86_64"
codesign/enable=false
application/modify_resources=false
application/icon=""
application/file_version=""
application/product_version=""
application/company_name=""
application/product_name="{name}"
application/file_description="{name}"
application/copyright=""
application/trademarks=""
application/export_angle=0
application/export_d3d12=0
ssh_remote_deploy/enabled=false
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pets", required=True, help="comma separated, e.g. bretzel,sally,zaira")
    ap.add_argument("--default", required=True, help="the pet shown the first time")
    ap.add_argument("--name", default="DesktopPet", help="name of the program and of the zip")
    a = ap.parse_args()
    pets = [p.strip().lower() for p in a.pets.split(",") if p.strip()]
    every = sorted(d for d in os.listdir(os.path.join(ROOT, "cats"))
                   if os.path.isfile(os.path.join(ROOT, "cats", d, "profile.json")))
    missing = [p for p in pets + [a.default] if p not in every]
    if missing: sys.exit(f"non ci sono in cats/: {', '.join(missing)} (ci sono: {', '.join(every)})")
    if a.default not in pets: sys.exit("--default deve essere fra i --pets")
    out = os.path.join(ROOT, "export", a.name)
    exe = os.path.join(out, a.name + ".exe")
    shutil.rmtree(out, ignore_errors=True); os.makedirs(out)
    # everything that is not the game: pipeline, review, tests, and the pets left out (their extracted textures too)
    exclude = ["assets/*", "tools/*", "review/*", "tests/*", "docs/*", "export/*"] + \
              [f"cats/{d}/*" for d in every if d not in pets] + [f"cats/{p}/*.jpg" for p in pets]
    with open(os.path.join(ROOT, "export_presets.cfg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(PRESET.format(features=f"default_pet_{a.default}", exclude=", ".join(exclude),
                              exe=exe.replace("\\", "/"), name=a.name))
    subprocess.run(["dotnet", "build", "ZairaDesktopPet.csproj", "-c", "ExportRelease", "-v", "q", "-nologo"],
                   cwd=ROOT, check=True)
    subprocess.run([GODOT, "--headless", "--path", ROOT, "--export-release", "Windows Desktop", exe], cwd=ROOT, check=True)
    if not os.path.isfile(exe): sys.exit("export fallito: manca " + exe)
    z = os.path.join(ROOT, "export", a.name + ".zip")
    with zipfile.ZipFile(z, "w", zipfile.ZIP_DEFLATED) as zf:
        for base, _, files in os.walk(out):
            for fn in files:
                p = os.path.join(base, fn)
                zf.write(p, os.path.join(a.name, os.path.relpath(p, out)))
    mb = os.path.getsize(z) / 1e6
    print(f"fatto: {z} ({mb:.0f} MB) con {', '.join(pets)}; al primo avvio {a.default}")


if __name__ == "__main__":
    main()
