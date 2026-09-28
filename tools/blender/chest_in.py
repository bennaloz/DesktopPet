# Flatter chest (run once after rig.py, before anim.py): the single-photo model pushes the chest out under the
# chin as far forward as the base of the neck; rig.py already pulls it in a little, this goes further. Moves the
# front of the chest back in the rest pose (the skinning follows), most at its fullest point, fading out towards
# the neck, the front legs and the sides. Works on the existing rig.blend, once (marked on the mesh).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file
import bpy

BACK = 0.02                  # how far the fullest point goes back (m)
PEAK_Z, SPAN_Z = 0.43, 0.09  # height of the fullest point, and how far up and down the pull fades out
FRONT_Y = (-0.36, -0.41)     # depth: nothing behind the first, all of it past the second
HALF_X = (0.05, 0.10)        # across: all of it near the middle line, nothing past the second


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
cat = bpy.data.objects["Zaira"]
if cat.get("chest_in"):
    print("chest already pulled in")
    sys.exit(0)
moved = 0
for v in cat.data.vertices:
    x, y, z = v.co
    w = (1 - smooth(0, SPAN_Z, abs(z - PEAK_Z))) * smooth(-FRONT_Y[0], -FRONT_Y[1], -y) * (1 - smooth(*HALF_X, abs(x)))
    if w <= 0.001: continue
    v.co.y += BACK * w
    moved += 1
cat["chest_in"] = BACK
print("chest: moved", moved, "vertices back")
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
