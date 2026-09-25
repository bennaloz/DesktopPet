"""Replace the tail: the AI mesh had it fused to the rump, so lifting it always tore the skin.

1. weld the UV-seam splits so real holes can be told from seams;
2. delete the old tail and close the rump where it was attached;
3. build a new tail: a tapered tube along the tail bones, root buried in the rump, radius measured on the old
   tail, coloured by borrowing the old tail's texture coordinates point by point;
4. weight it along the bone chain.
Runs on rig.blend (after rig.py) and saves it back.
"""
import bpy, bmesh, math
from mathutils import Vector
from mathutils.kdtree import KDTree

bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/rig.blend")
mesh = bpy.data.objects["Zaira"]
arm = bpy.data.objects["Rig"]
me = mesh.data
TAILS = ['Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5']
gi = {g.name: g.index for g in mesh.vertex_groups}
T = {gi[n] for n in TAILS}

bm = bmesh.new()
bm.from_mesh(me)
dl = bm.verts.layers.deform.active
uv = bm.loops.layers.uv.active
bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)

def tw(v):
    return sum(w for g, w in v[dl].items() if g in T)

# ---- 1. remember the old tail's surface (position -> texture coordinate), then delete it
tail_all = [f for f in bm.faces if min(tw(v) for v in f.verts) > 0.5]
# The part of the old tail glued to the rump stays, as rump skin; only the free part goes.
body_pts = [v.co.copy() for v in bm.verts if tw(v) < 0.2]
kd_body = KDTree(len(body_pts))
for i, co in enumerate(body_pts): kd_body.insert(co, i)
kd_body.balance()
def glued(f):
    return kd_body.find(f.calc_center_median())[2] < GLUE
GLUE = 0.03
tail_faces = [f for f in tail_all if not glued(f)]
kept = [f for f in tail_all if glued(f)]
Hips = gi['Hips']
for f in kept:
    for v in f.verts:
        for g in list(v[dl].keys()): del v[dl][g]
        v[dl][Hips] = 1.0
print("glued tail faces kept as rump skin", len(kept), flush=True)
samples = []
for f in tail_faces:
    for l in f.loops:
        samples.append((l.vert.co.copy(), l[uv].uv.copy()))
kd_old = KDTree(len(samples))
for i, (co, _) in enumerate(samples):
    kd_old.insert(co, i)
kd_old.balance()
mat_index = tail_faces[0].material_index if tail_faces else 0
old = bmesh.new()
vmap = {}
for f in tail_faces:
    for v in f.verts:
        if v not in vmap: vmap[v] = old.verts.new(v.co)
old_uv = old.loops.layers.uv.new(me.uv_layers.active.name)
for f in tail_faces:
    nf = old.faces.new([vmap[v] for v in f.verts])
    nf.material_index = 0
    for l, ol in zip(f.loops, nf.loops): ol[old_uv].uv = l[uv].uv
old_me = bpy.data.meshes.new("OldTail"); old.to_mesh(old_me); old.free()
old_me.materials.append(me.materials[mat_index])
old_obj = bpy.data.objects.new("OldTail", old_me); bpy.context.scene.collection.objects.link(old_obj)
bmesh.ops.delete(bm, geom=tail_faces, context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
print("old tail faces removed", len(tail_faces), flush=True)

# ---- 2. close the holes left on the rump
region = lambda co: co.y > 0.18 and 0.12 < co.z < 0.6
hole_edges = [e for e in bm.edges if e.is_boundary and region(e.verts[0].co)]
before = set(bm.faces)
if hole_edges:
    bmesh.ops.holes_fill(bm, edges=hole_edges, sides=60)
new_faces = [f for f in bm.faces if f not in before]
if new_faces:
    bmesh.ops.triangulate(bm, faces=new_faces)
    new_faces = [f for f in bm.faces if f not in before]
# texture the patches from the nearest surviving skin
kd_skin_pts = [(l.vert.co.copy(), l[uv].uv.copy()) for f in bm.faces if f not in new_faces for l in f.loops if region(l.vert.co)]
kd_skin = KDTree(len(kd_skin_pts))
for i, (co, _) in enumerate(kd_skin_pts):
    kd_skin.insert(co, i)
kd_skin.balance()
for f in new_faces:
    f.material_index = mat_index
    c = f.calc_center_median()
    _, i, _ = kd_skin.find(c)
    for l in f.loops:
        l[uv].uv = kd_skin_pts[i][1]
    for v in f.verts:          # patch vertices ride with the hips
        if not v[dl]:
            v[dl][gi['Hips']] = 1.0
if new_faces:
    bmesh.ops.recalc_face_normals(bm, faces=new_faces)
    for f in new_faces: f.smooth = True
print("rump patches", len(new_faces), flush=True)

# ---- 3. the new tail: centreline along the bones, root pushed into the rump
bones = arm.data.bones
pts = [bones[TAILS[0]].head_local.copy()] + [bones[n].tail_local.copy() for n in TAILS]
root_dir = (pts[0] - pts[1]).normalized()
pts.insert(0, pts[0] + root_dir * 0.045)            # buried root

def catmull(p0, p1, p2, p3, t):
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)

ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
centre = []
for s in range(len(pts) - 1):
    for k in range(8):
        centre.append(catmull(ext[s], ext[s + 1], ext[s + 2], ext[s + 3], k / 8))
centre.append(pts[-1])
# arc length parameter
L = [0.0]
for a, b in zip(centre, centre[1:]):
    L.append(L[-1] + (b - a).length)
total = L[-1]
root_len = (pts[1] - pts[0]).length

# radius measured on the old tail around each centreline point
radii = []
for c in centre:
    near = [(co - c).length for co, _ in samples if (co - c).length < 0.06]
    near.sort()
    radii.append(near[len(near) * 3 // 4] if len(near) > 6 else None)
# fill gaps, keep a sensible taper, and a rounded tip
known = [r for r in radii if r]
base_r = sorted(known)[len(known) // 2] if known else 0.03
for i, r in enumerate(radii):
    if r is None or r > base_r * 1.6:
        radii[i] = None
for i in range(len(radii)):
    if radii[i] is None:
        prev = next((radii[j] for j in range(i - 1, -1, -1) if radii[j]), None)
        nxt = next((radii[j] for j in range(i + 1, len(radii)) if radii[j]), None)
        radii[i] = prev or nxt or base_r
sm = radii[:]
for _ in range(3):
    sm = [sm[0]] + [(sm[i - 1] + 2 * sm[i] + sm[i + 1]) / 4 for i in range(1, len(sm) - 1)] + [sm[-1]]
for i in range(len(sm)):
    u = L[i] / total
    sm[i] *= min(1.0, (1 - u) * 6 + 0.35)            # round off the last sixth
    if L[i] < root_len:
        sm[i] = max(sm[i], base_r * 1.1)             # the buried root is a little thicker
print("tail length", round(total, 3), "base radius", round(base_r, 3), flush=True)

# the rump is done: write it back, then build the tail as its own object
bm.to_mesh(me); bm.free(); me.update()

tb = bmesh.new()
tuv = tb.loops.layers.uv.new(me.uv_layers.active.name)
tdl = tb.verts.layers.deform.verify()
SEG = 14
tangents = [(centre[min(i + 1, len(centre) - 1)] - centre[max(i - 1, 0)]).normalized() for i in range(len(centre))]
n = tangents[0].cross(Vector((1, 0, 0))).normalized()
rings = []
for i, c in enumerate(centre):
    t = tangents[i]
    n = (n - t * n.dot(t)).normalized()
    b = t.cross(n)
    rings.append([tb.verts.new(c + (n * math.cos(2 * math.pi * k / SEG) + b * math.sin(2 * math.pi * k / SEG)) * sm[i])
                  for k in range(SEG)])
tip = tb.verts.new(centre[-1] + tangents[-1] * sm[-1] * 0.6)
for i, (r0, r1) in enumerate(zip(rings, rings[1:])):
    v0, v1 = L[i] / total, L[i + 1] / total
    for k in range(SEG):
        f = tb.faces.new((r0[k], r0[(k + 1) % SEG], r1[(k + 1) % SEG], r1[k]))
        u0, u1 = k / SEG, (k + 1) / SEG
        for l, (u, v) in zip(f.loops, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]):
            l[tuv].uv = (u, v)
        f.smooth = True
for k in range(SEG):
    f = tb.faces.new((rings[-1][k], rings[-1][(k + 1) % SEG], tip))
    for l, (u, v) in zip(f.loops, [(k / SEG, 0.99), ((k + 1) / SEG, 0.99), ((k + 0.5) / SEG, 1.0)]):
        l[tuv].uv = (u, v)
    f.smooth = True
tb.normal_update()
for f in tb.faces:
    c = f.calc_center_median()
    i = min(range(len(centre)), key=lambda j: (c - centre[j]).length)
    if f.normal.dot(c - centre[i]) < 0:
        f.normal_flip()

# weights along the chain; the buried root rides with the hips
seg_ends = [0.0]
for a_, b_ in zip(pts, pts[1:]):
    seg_ends.append(seg_ends[-1] + (b_ - a_).length)
def weights_at(s_):
    if s_ < root_len:
        return {'Hips': 1.0}
    for i in range(1, len(pts) - 1):
        if s_ <= seg_ends[i + 1] or i == len(pts) - 2:
            u = (s_ - seg_ends[i]) / max(1e-6, seg_ends[i + 1] - seg_ends[i])
            bone = TAILS[i - 1]
            if u < 0.25:
                a_ = (0.25 - u) / (0.5 if i > 1 else 0.5)
                return {bone: 1 - a_, (TAILS[i - 2] if i > 1 else 'Hips'): a_}
            if u > 0.75 and i < len(TAILS):
                a_ = (u - 0.75) / 0.5
                return {bone: 1 - a_, TAILS[i]: a_}
            return {bone: 1.0}
    return {TAILS[-1]: 1.0}
names = [g.name for g in mesh.vertex_groups]
for i, ring in enumerate(rings):
    for v in ring:
        for g, w in weights_at(L[i]).items(): v[tdl][names.index(g)] = w
for g, w in weights_at(total).items(): tip[tdl][names.index(g)] = w

tail_me = bpy.data.meshes.new("NewTail"); tb.to_mesh(tail_me); tb.free()
tail_obj = bpy.data.objects.new("NewTail", tail_me); bpy.context.scene.collection.objects.link(tail_obj)
for nm in names: tail_obj.vertex_groups.new(name=nm)

# bake the old tail's colours onto a texture of its own
img = bpy.data.images.new("TailColor", 256, 1024)
mat = bpy.data.materials.new("TailMat"); mat.use_nodes = True
nodes = mat.node_tree.nodes; bsdf = nodes.get("Principled BSDF")
tex = nodes.new("ShaderNodeTexImage"); tex.image = img
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
nodes.active = tex
tail_me.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
bpy.ops.object.select_all(action='DESELECT')
old_obj.select_set(True); tail_obj.select_set(True); bpy.context.view_layer.objects.active = tail_obj
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=True,
                    cage_extrusion=0.03, max_ray_distance=0.08, margin=8)
img.pack()
print("baked tail colours", flush=True)
bpy.data.objects.remove(old_obj)

# join the tail into the cat: same UV layer name and group names, so everything merges
bpy.ops.object.select_all(action='DESELECT')
tail_obj.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = mesh
bpy.ops.object.join()
print("new tail verts", len(rings) * SEG + 1, flush=True)
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/rig.blend")
