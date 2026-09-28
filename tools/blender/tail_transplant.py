# Step 2 of the tail transplant (after tail_align.py): the rigged model's tail was fused to the rump by the
# single-photo reconstruction, and lifting it tore a fringe at the base. Replace it with the tail of the Tripo model
# made with the tail up, which stands free of the body:
#   1. remove the old tail (and the skin detached from the rump for it) from the rigged mesh
#   2. take the new tail plus a collar of rump skin round its base (it covers where the old tail was)
#   3. lay the Tail1-5 bones along the new tail's centre line
#   4. weight the new part (collar to the hips, fading into Tail1; the tail along the chain) and join it
# Works on work/tail_up_aligned.blend and writes work/rig.blend (keep a copy of the old one).
import os, sys, math, heapq
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from paths import work_file
import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
from mathutils.bvhtree import BVHTree

TAIL = ['Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5']
OFF_BODY = 0.03         # new-model skin this far from the old body is tail (the new back is ~2 cm higher)
TAIL_FROM = 0.12        # ...and only from this far in front of the rump backwards (not the top of the back)
# The whole back of the cat is taken from the new model too (rump and back of the thighs, above the hocks): on the
# old one it was hidden under the hanging tail, with no real fur texture, and scraps of the fused tail stuck to it.
# Behind REAR_CUT (from the back of the thighs) the old skin goes and the new comes in, overlapping by OVERLAP.
REAR_CUT, REAR_ABOVE, OVERLAP = 0.09, 0.20, 0.012
# Below the cut the legs of the two models do not match; pale scraps of the old skin that lay under the tail are
# picked out by their colour instead and removed where new skin lies right over them.
PALE, PALE_NEAR = 0.62, 0.02
COLLAR = 0.045          # (geodesic distance from the tail's base, for how far Tail1 pulls the skin)
LIFT_COLLAR = 0.0015    # the new skin sits this much outside the old skin it overlaps
# Round the base the skin passes from the hips to the tail gradually, both ways from the base ring: the tail rests
# standing up, so hanging down it has turned ~150 degrees and a sharp change of weights tears the skin there.
TAIL1_INTO_COLLAR = 0.04   # how far out over the rump Tail1 still pulls the skin (fading out)
HIPS_UP_TAIL = 0.05        # how far up the tail the hips still hold some of it


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


bpy.ops.wm.open_mainfile(filepath=work_file("tail_up_aligned.blend"))
arm = bpy.data.objects["Rig"]
cat = bpy.data.objects["Zaira"]
new = bpy.data.objects["TailUp"]

# ---------------------------------------------------------------- 1. the old tail goes
gi = {g.index: g.name for g in cat.vertex_groups}
tail_idx = {cat.vertex_groups[n].index for n in TAIL}
hips = cat.vertex_groups['Hips']
# every old vertex's own weights (without the tail), for the new skin that replaces it
old_all = np.array([v.co[:] for v in cat.data.vertices])
old_w = []
for v in cat.data.vertices:
    w = {gi[g.group]: g.weight for g in v.groups if g.weight > 0 and gi[g.group] not in TAIL}
    t = sum(w.values())
    old_w.append({k: x / t for k, x in w.items()} if t > 0 else {'Hips': 1.0})
old_legal = [i for i, w in enumerate(old_w) if sum(g.weight for g in cat.data.vertices[i].groups
                                                  if gi[g.group] in TAIL) < 0.5]
kd_w = KDTree(len(old_legal))
for k, i in enumerate(old_legal): kd_w.insert(old_all[i], k)
kd_w.balance()
# the old body's surface (without the tail), to draw the new skin's edge onto
legal = set(old_legal)
bvh_old = BVHTree.FromPolygons([Vector(c) for c in old_all],
                               [list(pg.vertices) for pg in cat.data.polygons if all(i in legal for i in pg.vertices)])
body_pts = old_all[old_legal]
rump_y = body_pts[(body_pts[:, 2] < 0.3)][:, 1].max()
cut_y = rump_y - REAR_CUT
print("rear cut at y %.3f (rump %.3f)" % (cut_y, rump_y))

# new rear skin, to know where an old pale scrap has something to take its place
nm = new.data
new_rear = np.array([v.co[:] for v in nm.vertices if v.co.y > cut_y - OVERLAP and v.co.z > REAR_ABOVE * 0.5])
kd_rear = KDTree(len(new_rear))
for i, c in enumerate(new_rear): kd_rear.insert(c, i)
kd_rear.balance()
img = next(n.image for n in cat.active_material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
W_, H_ = img.size
pix = np.array(img.pixels[:], dtype=np.float32).reshape(H_, W_, 4)
uvl = cat.data.uv_layers.active.data
bright = np.zeros(len(cat.data.vertices)); count = np.zeros(len(cat.data.vertices))
for poly in cat.data.polygons:
    for li in poly.loop_indices:
        u, v = uvl[li].uv
        c = pix[min(H_ - 1, max(0, int(v * H_))), min(W_ - 1, max(0, int(u * W_)))]
        vi = cat.data.loops[li].vertex_index
        bright[vi] += (c[0] + c[1] + c[2]) / 3; count[vi] += 1
bright /= np.maximum(count, 1)
pale = {i for i, v in enumerate(cat.data.vertices)
        if v.co.z > REAR_ABOVE * 0.75 and v.co.y > cut_y - 0.08 and bright[i] > PALE
        and kd_rear.find(v.co)[2] < PALE_NEAR}
print("pale scraps:", len(pale), "vertices")

bm = bmesh.new(); bm.from_mesh(cat.data)
dl = bm.verts.layers.deform.verify()
kill = [v for v in bm.verts if sum(w for g, w in v[dl].items() if g in tail_idx) > 0.5
        or (v.co.y > cut_y + OVERLAP and v.co.z > REAR_ABOVE + OVERLAP) or v.index in pale]
print("old tail: removing", len(kill), "vertices")
bmesh.ops.delete(bm, geom=kill, context='VERTS')
for v in bm.verts:                       # what is left of it belongs to the hips now
    d = v[dl]
    moved = sum(w for g, w in d.items() if g in tail_idx)
    if moved <= 0: continue
    for g in list(d.keys()):
        if g in tail_idx: del d[g]
    d[hips.index] = d.get(hips.index, 0.0) + moved
bm.to_mesh(cat.data); bm.free()
old_pts = np.array([v.co[:] for v in cat.data.vertices])
kd_old = KDTree(len(old_pts))
for i, c in enumerate(old_pts): kd_old.insert(c, i)
kd_old.balance()

# ---------------------------------------------------------------- 2. the new tail and its collar
bm = bmesh.new(); bm.from_mesh(new.data)
bm.verts.ensure_lookup_table()
# The mesh is split along its UV seams: vertices at the same place are one point of the surface. Walk the surface
# over "points" (all the vertices at one place), not over vertices, or a UV island looks like a loose piece.
point = {}
first = {}
for v in bm.verts:
    k = (round(v.co.x, 6), round(v.co.y, 6), round(v.co.z, 6))
    point[v.index] = first.setdefault(k, v.index)
nbr = {}
for e in bm.edges:
    a, b = point[e.verts[0].index], point[e.verts[1].index]
    if a == b: continue
    l = e.calc_length()
    nbr.setdefault(a, {})[b] = l
    nbr.setdefault(b, {})[a] = l
members = {}
for i, p in point.items(): members.setdefault(p, []).append(i)
far = {point[v.index] for v in bm.verts
       if v.co.y > rump_y - TAIL_FROM and v.co.z > 0.3 and kd_w.find(v.co)[2] > OFF_BODY}   # (old body, whole)
# the tail is the biggest connected piece of that
comps, seen = [], set()
for i in far:
    if i in seen: continue
    stack, comp = [i], []
    seen.add(i)
    while stack:
        x = stack.pop(); comp.append(x)
        for y in nbr.get(x, {}):
            if y in far and y not in seen: seen.add(y); stack.append(y)
    comps.append(comp)
core = set(max(comps, key=len))
print("new tail:", len(core), "points")


def dijkstra(sources, allowed, limit=1e9):
    dist = {s: 0.0 for s in sources}
    heap = [(0.0, s) for s in sources]
    while heap:
        d, i = heapq.heappop(heap)
        if d > dist.get(i, 1e9) or d > limit: continue
        for j, l in nbr.get(i, {}).items():
            if not allowed(j): continue
            nd = d + l
            if nd < dist.get(j, 1e9) and nd <= limit:
                dist[j] = nd; heapq.heappush(heap, (nd, j))
    return dist


base = {i for i in core if any(j not in core for j in nbr.get(i, {}))}
collar = dijkstra(base, lambda j: j not in core, COLLAR)          # how far from the tail's base
rear = {point[v.index] for v in bm.verts if v.co.y > cut_y - OVERLAP and v.co.z > REAR_ABOVE - OVERLAP}
# and the new skin over the pale scraps that went
pale_pts = old_all[list(pale)] if pale else np.zeros((0, 3))
if len(pale_pts):
    kd_p = KDTree(len(pale_pts))
    for i, c in enumerate(pale_pts): kd_p.insert(c, i)
    kd_p.balance()
    rear |= {point[v.index] for v in bm.verts if v.co.z > REAR_ABOVE * 0.5 and kd_p.find(v.co)[2] < PALE_NEAR * 1.5}
along = dijkstra(base, lambda j: j in core)                        # how far up the tail each point is
keep_points = core | rear
keep = {i for p in keep_points for i in members[p]}
print("rear skin:", len(rear - core), "points; tail length over the surface %.3f" % max(along.values()))

# ---------------------------------------------------------------- 3. bones along the centre line
L = max(along.values())
bins = 14
cent = []
for k in range(bins):
    sel = [bm.verts[i].co for i in core if k * L / bins <= along[i] < (k + 1) * L / bins]
    if sel: cent.append(sum(sel, Vector()) / len(sel))
base_c = sum((bm.verts[i].co for i in base), Vector()) / len(base)
line = [base_c] + cent
seg = [(line[i + 1] - line[i]).length for i in range(len(line) - 1)]
total = sum(seg)
joints = []
for k in range(6):
    target, acc = total * k / 5, 0.0
    for i, s in enumerate(seg):
        if acc + s >= target or i == len(seg) - 1:
            u = 0 if s == 0 else (target - acc) / s
            joints.append(line[i].lerp(line[i + 1], min(1.0, u)))
            break
        acc += s
for j in joints: j.x = 0.0            # keep the chain in the body's middle plane
print("tail joints:", [tuple(round(c, 3) for c in j) for j in joints])

# ---------------------------------------------------------------- 4. cut out the new part, weight it, join
drop = [v for v in bm.verts if v.index not in keep]
normals = {v.index: v.normal.copy() for v in bm.verts}
old_index = {v: v.index for v in bm.verts}
# Near its edge the new skin is drawn onto the old surface (the two bodies differ by a centimetre or two), so the
# join shows no step: fully at the cut, not at all SEAM_BLEND further in.
SEAM_BLEND = 0.04
for p in rear - core:
    v0 = bm.verts[p].co.copy()
    edge = max(v0.y - (cut_y - OVERLAP), 0.0)
    if v0.z > REAR_ABOVE - OVERLAP: edge = min(edge, max(v0.z - (REAR_ABOVE - OVERLAP), 0.0) + 1.0)
    k = 1 - smooth(0, SEAM_BLEND, edge)
    hit = bvh_old.find_nearest(v0)
    target = hit[0] if hit[0] is not None else v0
    n = sum((normals[i] for i in members[p]), Vector()).normalized()
    moved = v0.lerp(target, k) + n * LIFT_COLLAR
    for i in members[p]:
        bm.verts[i].co = moved
dl = bm.verts.layers.deform.verify()
names = sorted(set(['Hips'] + TAIL + [g.name for g in cat.vertex_groups]))
for n in names:
    if n not in new.vertex_groups: new.vertex_groups.new(name=n)
gidx = {n: new.vertex_groups[n].index for n in names}
seglen = [(joints[i + 1] - joints[i]).length for i in range(5)]
cum = [0.0]
for s in seglen: cum.append(cum[-1] + s)
for v in bm.verts:
    i = old_index[v]
    if i not in keep: continue
    p = point[i]
    d = v[dl]
    d.clear()
    if p in core:
        # position along the chain (same measure as the joints: along the centre line, from the base)
        a = along[p] / L * cum[-1]
        k = min(4, max(0, next((b for b in range(5) if a < cum[b + 1]), 4)))
        u = (a - cum[k]) / seglen[k]
        w = {TAIL[k]: 1.0}
        if u > 0.75 and k < 4: t = (u - 0.75) / 0.5; w = {TAIL[k]: 1 - t, TAIL[k + 1]: t}
        elif u < 0.25 and k > 0: t = (0.25 - u) / 0.5; w = {TAIL[k]: 1 - t, TAIL[k - 1]: t}
        h = 0.5 * (1 - smooth(0, HIPS_UP_TAIL, along[p]))   # half the hips at the base ring, none further up
        for n, x in w.items(): d[gidx[n]] = x * (1 - h)
        if h > 0: d[gidx['Hips']] = h
    else:
        # the weights of the old skin that was here, and near the tail's base a share of Tail1
        _, k, _ = kd_w.find(v.co)
        t1 = 0.5 * (1 - smooth(0, TAIL1_INTO_COLLAR, collar.get(p, 1.0)))
        for n, x in old_w[old_legal[k]].items(): d[gidx[n]] = x * (1 - t1)
        if t1 > 0: d[gidx['Tail1']] = d.get(gidx['Tail1'], 0.0) + t1
bmesh.ops.delete(bm, geom=drop, context='VERTS')
bm.to_mesh(new.data); bm.free()
print("new part:", len(new.data.vertices), "vertices")

bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for k, n in enumerate(TAIL):
    eb = arm.data.edit_bones[n]
    eb.use_connect = False
    eb.head, eb.tail = joints[k], joints[k + 1]
    eb.align_roll(Vector((1, 0, 0)).cross((eb.tail - eb.head).normalized()))   # local X = world X, as every bone
for k, n in enumerate(TAIL[1:], 1):
    arm.data.edit_bones[n].use_connect = True
bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='DESELECT')
new.select_set(True); cat.select_set(True)
bpy.context.view_layer.objects.active = cat
bpy.ops.object.join()
cat["tail_transplant"] = 1
print("joined:", len(cat.data.vertices), "vertices,", len(cat.data.materials), "materials")
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
