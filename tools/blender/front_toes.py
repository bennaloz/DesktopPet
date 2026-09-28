# Front toes for the rig (run once after rig.py, before anim.py): the front paw was one bone from the wrist to
# the tips of the toes. Standing, that bone is nearly upright and the toes lie flat; lying down with the
# forearms on the floor it turns flat and the toes point at the ceiling. Split it: Hand ends at the ball of the
# paw, Finger carries the toes, so the toes can stay flat on the floor whatever the wrist does (like Toe on the
# hind legs). Works on the existing rig.blend (the tail was reworked after rig.py), and does nothing twice.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file
import bpy
from mathutils import Vector

# ball of the paw (new end of Hand) and tip of the toes, per side (metres, rest pose; the nose faces -Y)
PAWS = {'L': ((0.079, -0.312, 0.03), (0.082, -0.372, 0.012)),
        'R': ((-0.037, -0.312, 0.03), (-0.032, -0.372, 0.012))}
# how the skin passes from Hand to Finger along the paw (-y), only low down on the paw
BLEND_Y, TOES_Z = (0.300, 0.330), (0.06, 0.04)


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
arm = bpy.data.objects["Rig"]
cat = bpy.data.objects["Zaira"]
if "Finger.L" in arm.data.bones:
    print("front toes already there")
    sys.exit(0)

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for side, (ball, tip) in PAWS.items():
    hand = arm.data.edit_bones['Hand.' + side]
    hand.tail = Vector(ball)
    finger = arm.data.edit_bones.new('Finger.' + side)
    finger.head, finger.tail = Vector(ball), Vector(tip)
    finger.parent, finger.use_connect = hand, True
    for eb in (hand, finger):   # local X = world +X, as every bone of the rig (rotation about X bends in the side plane)
        eb.align_roll(Vector((1, 0, 0)).cross((eb.tail - eb.head).normalized()))
bpy.ops.object.mode_set(mode='OBJECT')

moved = 0
for side in PAWS:
    hand_g = cat.vertex_groups['Hand.' + side]
    finger_g = cat.vertex_groups.new(name='Finger.' + side)
    for v in cat.data.vertices:
        w = next((g.weight for g in v.groups if g.group == hand_g.index), 0.0)
        if w <= 0: continue
        share = smooth(*BLEND_Y, -v.co.y) * smooth(TOES_Z[0], TOES_Z[1], v.co.z)
        if share <= 0: continue
        hand_g.add([v.index], w * (1 - share), 'REPLACE')
        finger_g.add([v.index], w * share, 'REPLACE')
        moved += 1
print("front toes: weights moved on", moved, "vertices")
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
