# Step 1 of the tail transplant: bring the Tripo model of Zaira with her tail up (assets/zaira/tail_up/) into the
# rig's frame, fitted to the rigged body (tail left out on both sides), and save it to work/tail_up_aligned.blend.
#
# Rough fit first (nose, floor, length), then a similarity ICP (scale, rotation, shift) on the two bodies.
import os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file, source_file
import bpy, numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

TAIL = ('Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5')

bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
cat = bpy.data.objects["Zaira"]
gi = {g.index: g.name for g in cat.vertex_groups}
old = np.array([cat.matrix_world @ v.co for v in cat.data.vertices])
tailw = np.array([sum(g.weight for g in v.groups if gi[g.group] in TAIL) for v in cat.data.vertices])
old_body = old[tailw < 0.05]

bpy.ops.import_scene.gltf(filepath=source_file("tail_up/tripo_tail_up.glb"))
new = [o for o in bpy.context.selected_objects if o.type == 'MESH'][0]
for o in list(bpy.context.selected_objects):
    if o is not new: bpy.data.objects.remove(o)
new.parent = None
bpy.context.view_layer.objects.active = new
new.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
new.data.transform(Matrix.Rotation(math.radians(90), 4, 'Z'))   # nose from -X to -Y, as the rig
pts = np.array([v.co[:] for v in new.data.vertices])

# rough: floor at 0, centred across, nose where the rig's nose is, length nose -> back of the thighs (low points only,
# the raised tail is high up)
def nose_rump(p, low):
    h = p[:, 2].max() - p[:, 2].min()
    lowp = p[p[:, 2] < p[:, 2].min() + low * h]
    return p[:, 1].min(), lowp[:, 1].max()
on, orr = nose_rump(old_body, 0.5)
nn, nr = nose_rump(pts, 0.5)
s = (orr - on) / (nr - nn)
pts = pts * s
pts[:, 2] -= pts[:, 2].min()
pts[:, 0] -= (pts[:, 0].min() + pts[:, 0].max()) / 2
pts[:, 1] += on - pts[:, 1].min()
print("rough scale %.4f" % s)

def body_of_new(p):
    # leave out the raised tail: behind the rump's front and above the top of the back there
    rump_top = old_body[old_body[:, 1] > orr - 0.12][:, 2].max()
    return p[~((p[:, 1] > orr - 0.10) & (p[:, 2] > rump_top - 0.03))]

kd = KDTree(len(old_body))
for i, c in enumerate(old_body): kd.insert(c, i)
kd.balance()
rng = np.random.default_rng(1)
T = np.eye(4)
for it in range(30):
    src = body_of_new(pts)
    src = src[rng.choice(len(src), min(4000, len(src)), replace=False)]
    dst = np.array([kd.find(c)[0][:] for c in src])
    d = np.linalg.norm(src - dst, axis=1)
    keep = d < np.percentile(d, 80)
    a, b = src[keep], dst[keep]
    ma, mb = a.mean(0), b.mean(0)
    A, B = a - ma, b - mb
    U, S, Vt = np.linalg.svd(A.T @ B)
    D = np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))])
    R = Vt.T @ D @ U.T
    sc = (S * np.diag(D)).sum() / (A ** 2).sum()
    t = mb - sc * R @ ma
    pts = (sc * (R @ pts.T)).T + t
    if it % 5 == 0 or it == 29:
        print("icp %2d: mean %.4f  scale step %.4f  rot %.2f deg" % (it, d[keep].mean(), sc,
              math.degrees(math.acos(max(-1, min(1, (np.trace(R) - 1) / 2))))))

for v, c in zip(new.data.vertices, pts): v.co = c
new.data.update()
new.name = "TailUp"
for img in bpy.data.images:
    if img.size[0] > 2048: img.scale(2048, 2048)
bpy.ops.wm.save_as_mainfile(filepath=work_file("tail_up_aligned.blend"))
print("saved; new model height %.3f, old %.3f" % (pts[:, 2].max(), old[:, 2].max()))
