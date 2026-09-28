# Shoulder blades for the rig (run once after rig.py, before anim.py): a cat's front leg does not hang from a
# fixed shoulder, the shoulder blade swings with every stride and carries the shoulder joint forward and back
# (and its top rises past the line of the back at the end of the stance). Without it the front legs swing from
# the chest like a table's. Scapula.L/R run from the top of the blade, beside the spine, down to the shoulder
# joint; UpperArm hangs from it. Works on the existing rig.blend, once.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file
import bpy, math
from mathutils import Vector

TOP_Y, TOP_Z, TOP_X = -0.14, 0.54, 0.06     # top of the blade (x: out from the middle line)
# the skin that moves with the blade: around the middle of the bone, on its own side of the body only
REACH = (0.07, 0.08, 0.08)                  # radii across (x), along (y), up (z)
SHARE = 0.8                                  # how much of the chest's hold on that skin the blade takes


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
arm = bpy.data.objects["Rig"]
cat = bpy.data.objects["Zaira"]
if "Scapula.L" in arm.data.bones:
    print("shoulder blades already there")
    sys.exit(0)

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
mids = {}
for side, sx in (('L', 1), ('R', -1)):
    upper = eb['UpperArm.' + side]
    blade = eb.new('Scapula.' + side)
    blade.head = Vector((TOP_X * sx, TOP_Y, TOP_Z))
    blade.tail = upper.head.copy()
    blade.parent = eb['Chest']
    blade.align_roll(Vector((1, 0, 0)).cross((blade.tail - blade.head).normalized()))   # local X = world X
    upper.use_connect = False
    upper.parent = blade
    mids[side] = (blade.head + blade.tail) / 2
bpy.ops.object.mode_set(mode='OBJECT')

chest = cat.vertex_groups['Chest']
moved = 0
for side, sx in (('L', 1), ('R', -1)):
    g = cat.vertex_groups.new(name='Scapula.' + side)
    m = mids[side]
    for v in cat.data.vertices:
        if v.co.x * sx < 0.02: continue   # never the other side, nor the middle of the back
        d = v.co - m
        r = math.sqrt((d.x / REACH[0]) ** 2 + (d.y / REACH[1]) ** 2 + (d.z / REACH[2]) ** 2)
        w = SHARE * (1 - smooth(0.6, 1.2, r))
        if w <= 0.001: continue
        cw = next((x.weight for x in v.groups if x.group == chest.index), 0.0)
        if cw <= 0: continue
        chest.add([v.index], cw * (1 - w), 'REPLACE')
        g.add([v.index], cw * w, 'REPLACE')
        moved += 1
print("shoulder blades: weights moved on", moved, "vertices")
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
