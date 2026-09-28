# A belly for the rig (run once after rig.py, before anim.py): the underside of the body is skin hung from the
# spine, so lying down it stays up where the standing cat carries it and leaves a gap over the floor. A Belly
# bone under the spine, pointing down, carries that skin: the resting poses move it down onto the floor and
# widen it (a lying cat spreads), and the leap can hold it in. Works on the existing rig.blend, once.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file
import bpy
from mathutils import Vector

HEAD, TAIL = (0.0, -0.02, 0.42), (0.0, -0.02, 0.30)   # under the middle of the spine, pointing at the floor
# where the skin belongs to the belly: low on the body (z), between the front and hind legs (y), near the middle
# line (x); each as (all belly, no belly) and a smooth fade between
LOW_Z = (0.30, 0.40)
ALONG_Y = ((-0.12, 0.08), (-0.20, 0.16))
ACROSS_X = (0.07, 0.13)
BODY = ('Hips', 'Spine', 'Chest')


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
arm = bpy.data.objects["Rig"]
cat = bpy.data.objects["Zaira"]
if "Belly" in arm.data.bones:
    print("belly already there")
    sys.exit(0)

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones.new("Belly")
eb.head, eb.tail = Vector(HEAD), Vector(TAIL)
eb.parent = arm.data.edit_bones["Spine"]
eb.align_roll(Vector((1, 0, 0)).cross((eb.tail - eb.head).normalized()))   # local X = world +X, as every bone
bpy.ops.object.mode_set(mode='OBJECT')

belly = cat.vertex_groups.new(name="Belly")
body = {cat.vertex_groups[n].index for n in BODY}
moved = 0
for v in cat.data.vertices:
    x, y, z = v.co
    (y0, y1), (y00, y11) = ALONG_Y
    w = (1 - smooth(*LOW_Z, z)) * smooth(y00, y0, y) * (1 - smooth(y1, y11, y)) * (1 - smooth(*ACROSS_X, abs(x)))
    if w <= 0.001: continue
    # the belly takes its share only from the body's own weights, never from the legs
    for g in v.groups:
        if g.group in body and g.weight > 0:
            share = g.weight * w
            cat.vertex_groups[g.group].add([v.index], g.weight - share, 'REPLACE')
            belly.add([v.index], share, 'ADD')
            moved += 1
print("belly: weights moved on", moved, "vertex groups")
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
