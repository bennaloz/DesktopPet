"""Bretzel's ears from a second Tripo model of him (tripo_ears.glb, sitting up): there the lop ears hang clear of the
cheeks and grow only from the crown. In the standing model the rig is built on (tripo.glb) they are fused to the
cheeks and the neck along their whole edge, too wide, and every turn of the head fanned them out with the cheek.

transplant(mesh) swaps the top and the sides of the head, ears and all, for the second model's:
- the second head is fitted onto the first (a similarity: from the eyes and the nose, then trimmed ICP on the skull);
- a smooth field over the second head (zone) marks what is swapped: the crown from behind the brow, the sides behind
  the eyes down to the jowls; the face stays. The second model is cut along its middle line, and the first one along
  the same line carried over to it (each vertex takes the field at its nearest point on the second head), with the
  old ears besides: so the two cuts are one line on two skins;
- the new piece is drawn onto the old cut (its rim onto the old rim, the rest following smoothly: the old skull is
  longer behind the ears) and sewn on with a strip of triangles;
- by the seam the old texture fades into the new one's colours, the new piece is toned to the old skin, and the
  shading is made one across it.
The piece keeps its own material and texture; the point attribute 'ear' marks the ears (1 left, 2 right)."""
import bpy, bmesh, math
import numpy as np
from mathutils import Vector as V3, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from bretzel_source import import_tripo, drop_crumbs, weld, decimate, sstep

# landmarks: the old head's (straightened), as the rig has them; the new one's, raw, from the dark of its texture
OLD_EYES, OLD_NOSE = ((0.103, -0.376, 0.576), (-0.019, -0.419, 0.582)), (0.056, -0.478, 0.50)
NEW_EYES, NEW_NOSE = ((0.087, -0.354, 0.611), (-0.062, -0.379, 0.614)), (-0.016, -0.474, 0.508)
NEW_MID = 0.0125            # x of the middle of the new head (between its eyes)
OLD_MID = 0.03


def neighbours(me):
    adj = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices; adj[a].append(b); adj[b].append(a)
    return adj


def hanging(me, mid, lateral, nmin, cuts, ylim=-0.10, zlo=0.25):
    """Per side, the part of a lop ear that hangs clear of the head: the big piece out to that side that the mesh
    below a height falls into, at the highest such height (above it the ear has joined the head)."""
    co = [v.co.copy() for v in me.vertices]; adj = neighbours(me)
    found = {}
    for zc in cuts:
        keep = [zlo < c.z < zc and c.y < ylim for c in co]
        seen = [False] * len(co)
        for i in range(len(co)):
            if not keep[i] or seen[i]: continue
            stack = [i]; seen[i] = True; mem = []
            while stack:
                q = stack.pop(); mem.append(q)
                for o in adj[q]:
                    if keep[o] and not seen[o]: seen[o] = True; stack.append(o)
            mx = sum(co[k].x for k in mem) / len(mem) - mid
            if len(mem) >= nmin and abs(mx) > lateral: found['L' if mx > 0 else 'R'] = (round(float(zc), 3), set(mem))
    return found


def umeyama(a, b):
    """The similarity (scale, rotation, translation) taking points a onto points b, least squares."""
    ma, mb = a.mean(0), b.mean(0); A, B = a - ma, b - mb
    U, S, Vt = np.linalg.svd(B.T @ A / len(a))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt; s = np.trace(np.diag(S) @ D) / (A ** 2).sum(1).mean()
    return s, R, mb - s * R @ ma


def fit_head(new, old, ears):
    """The new head onto the old one. The landmarks alone are a few millimetres off (a dark spot is not an eye
    centre); ICP on the skull, keeping the nearer half of the points (the old ears are fused to the cheeks, the new
    cheeks under the ears are left out), settles it to ~2 mm."""
    s, R, t = umeyama(np.array(NEW_EYES + (NEW_NOSE,)), np.array(OLD_EYES + (OLD_NOSE,)))
    me = old.data
    bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    src = np.array([tuple(v.co) for v in new.data.vertices if v.index not in ears
                    and v.co.y < -0.18 and v.co.z > 0.44 and not (abs(v.co.x) > 0.075 and v.co.z < 0.60)])
    def icp(src, keep, s, R, t, n=40):
        for _ in range(n):
            p = (s * (R @ src.T)).T + t
            q = np.array([tuple(bvh.find_nearest(V3(x))[0]) for x in p])
            d = np.linalg.norm(p - q, axis=1); k = d <= np.quantile(d, keep)
            s, R, t = umeyama(src[k], q[k])
        return s, R, t, 1000 * math.sqrt((d[k] ** 2).mean()), 1000 * np.median(d)
    s, R, t, rms, med = icp(src, 0.5, s, R, t)
    print("head fit: scale %.3f, rms %.1f mm" % (s, rms))
    return Matrix.Translation(V3(t)) @ Matrix(R.tolist()).to_4x4() @ Matrix.Diagonal((s, s, s, 1))


EYE_MID = (np.array(NEW_EYES[0]) + np.array(NEW_EYES[1])) / 2
YAW = math.atan2(NEW_EYES[0][1] - NEW_EYES[1][1], NEW_EYES[0][0] - NEW_EYES[1][0])     # the new head is turned too


def head(r):
    """A point of the new model (raw) in its head's own frame: from between the eyes, x across to the left eye."""
    d = V3(r) - V3(EYE_MID); c, s = math.cos(-YAW), math.sin(-YAW)
    return V3((c * d.x - s * d.y, s * d.x + c * d.y, d.z))


ZONE = dict(side=(0.050, 0.080), eye=(0.020, 0.030), front=(-0.050, -0.030), back=(0.175, 0.210), low=(-0.175, -0.150))


def zone(r):
    """How much a point (in the new model's raw coordinates) is of the head that is swapped: the ears' roots and the
    cheeks they hang over, and the crown from behind the brow (in the old model it is the white of the ears' fused
    roots); not the face (the eyes stay with it, joined on in front: an island of old skin in the new would need a
    second seam), the neck behind or the jowls below."""
    h = head(r); Z = ZONE
    eye = min((h - V3((sx * 0.075, 0, 0))).length for sx in (1, -1))
    face = max(1 - sstep(eye, *Z['eye']), (1 - sstep(h.y, -0.005, 0.01)) * (1 - sstep(abs(h.x), 0.085, 0.10)))
    crown = sstep(h.z, -0.01, 0.02) * sstep(h.y, 0.02, 0.05)
    return (max(sstep(abs(h.x), *Z['side']), crown) * (1 - face) * sstep(h.y, *Z['front'])
            * (1 - sstep(h.y, *Z['back'])) * sstep(h.z, *Z['low']))


class Surface:
    """The new head without its ears: the colour of its texture at the point nearest to p."""
    def __init__(self, mesh, skip):
        me = mesh.data; me.calc_loop_triangles()
        uv = me.uv_layers.active.data
        tris = [t for t in me.loop_triangles if not any(v in skip for v in t.vertices)]
        self.co = np.array([[tuple(me.vertices[v].co) for v in t.vertices] for t in tris])
        self.uv = np.array([[tuple(uv[l].uv) for l in t.loops] for t in tris])
        self.bvh = BVHTree.FromPolygons([V3(c) for c in self.co.reshape(-1, 3)], [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tris))])
        img = image_of(mesh)
        self.W, self.H = img.size
        px = np.empty(self.W * self.H * 4, np.float32); img.pixels.foreach_get(px)
        self.px = px.reshape(self.H, self.W, 4)

    def colour(self, p):
        loc, _, i, _ = self.bvh.find_nearest(V3(p))
        a, b, c = self.co[i]; v0, v1, v2 = b - a, c - a, np.array(loc) - a
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01 or 1e-12
        w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den
        u, v = (1 - w1 - w2) * self.uv[i][0] + w1 * self.uv[i][1] + w2 * self.uv[i][2]
        x = min(max(u * self.W - 0.5, 0), self.W - 1.001); y = min(max(v * self.H - 0.5, 0), self.H - 1.001)
        x0, y0 = int(x), int(y); fx, fy = x - x0, y - y0
        q = self.px
        return ((q[y0, x0] * (1 - fx) + q[y0, x0 + 1] * fx) * (1 - fy) + (q[y0 + 1, x0] * (1 - fx) + q[y0 + 1, x0 + 1] * fx) * fy)


def image_of(mesh):
    return image_of_material(mesh.data.materials[0])


def image_of_material(m):
    return [n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'][0]


def repaint(mesh, w, surf, mat=0):
    """Paint the old texture (the faces of material mat) with the new head's colours there, blended by w per vertex.
    Each triangle is filled in the texture by its UVs, with 1.5 pixels round it (the gutter the mipmaps read)."""
    me = mesh.data; me.calc_loop_triangles()
    img = image_of(mesh); W, H = img.size
    px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
    uv = me.uv_layers.active.data
    done = 0
    for t in me.loop_triangles:
        if me.polygons[t.polygon_index].material_index != mat or t.area < 1e-8: continue
        ws = np.array([w[v] for v in t.vertices])
        if ws.max() <= 0.0: continue
        P = np.array([tuple(me.vertices[v].co) for v in t.vertices])
        T = np.array([tuple(uv[l].uv) for l in t.loops]) * (W, H)
        lo = np.floor(T.min(0) - 2).astype(int); hi = np.ceil(T.max(0) + 2).astype(int)
        if (hi - lo).max() > 300: continue          # (not a triangle of the surface: one across UV islands)
        xs, ys = np.meshgrid(np.arange(max(lo[0], 0), min(hi[0], W)), np.arange(max(lo[1], 0), min(hi[1], H)))
        c = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
        v0, v1 = T[1] - T[0], T[2] - T[0]; den = v0[0] * v1[1] - v0[1] * v1[0]
        if abs(den) < 1e-9: continue
        d = c - T[0]
        b1 = (d[:, 0] * v1[1] - d[:, 1] * v1[0]) / den; b2 = (v0[0] * d[:, 1] - v0[1] * d[:, 0]) / den
        bc = np.stack([1 - b1 - b2, b1, b2], 1)
        # distance outside the triangle, in pixels, from the most negative barycentric and that side's height
        heights = np.array([abs(den) / np.linalg.norm(T[(k + 2) % 3] - T[(k + 1) % 3]) for k in range(3)])
        out = np.max(-bc * heights, axis=1)
        sel = out <= 1.5
        bc = np.clip(bc[sel], 0, None); bc /= bc.sum(1, keepdims=True)
        for (x, y), b in zip(c[sel].astype(int), bc):
            k = float(b @ ws)
            if k <= 0: continue
            px[y, x] = px[y, x] * (1 - k) + surf.colour(b @ P) * k
            done += 1
    img.pixels.foreach_set(px.ravel()); img.update(); img.pack()
    print("repainted texels", done)


def face_neighbours(me):
    by_edge = {}
    for p in me.polygons:
        for k in p.edge_keys: by_edge.setdefault(k, []).append(p.index)
    nb = [[] for _ in me.polygons]
    for fs in by_edge.values():
        for a in fs: nb[a].extend(b for b in fs if b != a)
    return nb


def pieces(sel, nb):
    sel = set(sel); out = []
    while sel:
        f = sel.pop(); comp = {f}; stack = [f]
        while stack:
            q = stack.pop()
            for o in nb[q]:
                if o in sel: sel.discard(o); comp.add(o); stack.append(o)
        out.append(comp)
    return out


def tidy(sel, nb, speck=40, enclosed=80):
    """A face selection without specks and without small islands of other faces inside it: one clean patch."""
    sel = set().union(*(c for c in pieces(sel, nb) if len(c) >= speck))
    for c in pieces(set(range(len(nb))) - sel, nb):
        if len(c) < enclosed: sel |= c
    return sel


def rims(bm):
    """The open edges of a bmesh as closed loops of vertices, in order."""
    nxt = {}
    for e in bm.edges:
        if e.is_boundary:
            a, b = e.verts; nxt.setdefault(a, []).append(b); nxt.setdefault(b, []).append(a)
    odd = [v for v, n in nxt.items() if len(n) != 2]
    if odd: print("  rim vertices with", sorted({len(nxt[v]) for v in odd}), "open edges:", len(odd))
    loops, seen = [], set()
    for v0 in nxt:
        if v0 in seen: continue
        loop, prev, cur = [v0], None, v0; seen.add(v0)
        while True:
            nx = [w for w in nxt[cur] if w is not prev and w not in seen]
            if not nx: break
            prev, cur = cur, nx[0]; loop.append(cur); seen.add(cur)
        loops.append(loop)
    return loops


def unpinch(sel, me, nb):
    """No two parts of the selection touching at a single vertex (its edge would cross itself there): the faces
    round such a vertex all go in."""
    vf = [[] for _ in me.vertices]
    for p in me.polygons:
        for v in p.vertices: vf[v].append(p.index)
    faces_of = {}
    for p in me.polygons:
        for k in p.edge_keys: faces_of.setdefault(k, []).append(p.index)
    sel = set(sel)
    for _ in range(20):
        cnt = {}
        for (a, b), fs in faces_of.items():          # edges between the selection and the rest of the skin
            if len(fs) == 2 and (fs[0] in sel) != (fs[1] in sel): cnt[a] = cnt.get(a, 0) + 1; cnt[b] = cnt.get(b, 0) + 1
        bad = [v for v, n in cnt.items() if n > 2]
        if not bad: break
        for v in bad: sel.update(vf[v])
    return sel


def along(A):
    """A turned so that the old faces run along it backwards: a strip sewn on its open side then faces out too."""
    e = next(e for e in A[0].link_edges if e.other_vert(A[0]) is A[1])
    f = e.link_faces[0]; ls = [l.vert for l in f.loops]; k = ls.index(A[0])
    return [A[0]] + A[:0:-1] if ls[(k + 1) % len(ls)] is A[1] else A


class Rim:
    """A closed loop of vertices as a polyline: distance along it, nearest points."""
    def __init__(self, A):
        self.A = A
        self.p = np.array([tuple(v.co) for v in A]); self.d = np.roll(self.p, -1, 0) - self.p
        self.L = np.linalg.norm(self.d, axis=1); self.s = np.concatenate([[0], np.cumsum(self.L)[:-1]]); self.total = self.L.sum()

    def at(self, s):
        s = s % self.total; k = min(int(np.searchsorted(self.s, s, side='right')) - 1, len(self.s) - 1)
        return self.p[k] + self.d[k] * ((s - self.s[k]) / max(self.L[k], 1e-12))

    def nearest(self, q):
        q = np.array(tuple(q))
        t = np.clip(((q - self.p) * self.d).sum(1) / np.maximum(self.L ** 2, 1e-12), 0, 1)
        x = self.p + self.d * t[:, None]; k = int(np.argmin(np.linalg.norm(x - q, axis=1)))
        return self.s[k] + t[k] * self.L[k], x[k]


def follow(rim, B):
    """B (another loop near the rim) in the rim's direction, from its vertex nearest the rim's start, with each
    vertex's place along the rim (unwrapped, never going back)."""
    s = np.array([rim.nearest(v.co)[0] for v in B])
    ang = np.unwrap(2 * math.pi * s / rim.total)
    if ang[-1] - ang[0] < 0 and np.mean(np.diff(ang)) < 0: B, s = B[::-1], s[::-1]
    k = int(np.argmin(s)); B = B[k:] + B[:k]; s = np.concatenate([s[k:], s[:k]])
    u = np.unwrap(2 * math.pi * s / rim.total) * rim.total / (2 * math.pi)
    u = np.maximum.accumulate(u - u[0]) + s[0]
    u = np.minimum(u, s[0] + rim.total - 1e-6)
    # where the old rim runs out into a bay the new one has no vertex to follow it, and the nearest places jump
    # across its mouth: spread the jumps over the neighbours, so the new rim is drawn along the bay
    m = len(u); w = np.exp(-0.5 * (np.arange(-6, 7) / 3.0) ** 2); w /= w.sum()
    ext = np.concatenate([u[-6:] - rim.total, u, u[:6] + rim.total])
    sm = np.convolve(ext, w, mode='valid')
    sm = np.maximum.accumulate(np.clip(sm, u[0], u[0] + rim.total - 1e-6))
    return B, sm


def sew(bm, rim, B, sB, uvl, mat, mat_new):
    """Triangles from the old rim to the new one (B, at its places sB along the rim), in order along the rim; each
    takes one spot of the old texture (the strip is a hair wide, and its corners may lie on two UV islands)."""
    A, sA = rim.A, rim.s
    i0 = int(np.searchsorted(sA, sB[0])) % len(A)
    A = A[i0:] + A[:i0]; sA = np.concatenate([sA[i0:], sA[:i0] + rim.total])
    def spot_of(v):          # (the middle of an old face there: a vertex's own UV may lie on its island's dark edge)
        fs = [l.face for l in v.link_loops if l.face.material_index != mat_new and l.face.calc_area() > 1e-8]
        return sum((l[uvl].uv for l in fs[0].loops), V3((0, 0, 0)).to_2d()) / len(fs[0].loops) if fs else None
    spot = {v: spot_of(v) for v in A}
    n, m = len(A), len(B); i = j = 0; made = []
    while i < n or j < m:
        ta = sA[i + 1] if i + 1 < n else sA[0] + rim.total
        tb = sB[j + 1] if j + 1 < m else sB[0] + rim.total
        a0, b0 = A[i % n], B[j % m]
        if j >= m or (i < n and ta <= tb): tri = (a0, A[(i + 1) % n], b0); i += 1
        else: tri = (a0, B[(j + 1) % m], b0); j += 1
        if len(set(tri)) < 3 or bm.faces.get(tri): continue
        f = bm.faces.new(tri); f.material_index = mat; made.append(f)
        if spot[a0] is not None:
            for l in f.loops: l[uvl].uv = spot[a0]
    return made


def pixels(img):
    W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px)
    return px.reshape(H, W, 4)


def blurred(px, n=512):
    """The texture's tone without the fur: averaged down to n x n."""
    f = px.shape[1] // n
    return px[:n * f, :n * f, :3].reshape(n, f, n, f, 3).mean((1, 3))


def tone_at(small, uv):
    n = small.shape[0]
    x = min(max(uv[0] * n - 0.5, 0), n - 1.001); y = min(max(uv[1] * n - 0.5, 0), n - 1.001)
    x0, y0 = int(x), int(y); fx, fy = x - x0, y - y0
    return ((small[y0, x0] * (1 - fx) + small[y0, x0 + 1] * fx) * (1 - fy)
            + (small[y0 + 1, x0] * (1 - fx) + small[y0 + 1, x0 + 1] * fx) * fy)


def texels(faces, uvl, W, H, value):
    """For each triangle of the faces, the texture's pixels it covers (with 1.5 pixels round it, the gutter the
    mipmaps read) and value (per vertex, an array) interpolated there: (rows, columns, values)."""
    for f in faces:
        cs = [l[uvl].uv for l in f.loops]; vs = [value(l.vert) for l in f.loops]
        for t in range(1, len(cs) - 1):
            T = np.array([tuple(cs[0]), tuple(cs[t]), tuple(cs[t + 1])]) * (W, H)
            D = np.array([vs[0], vs[t], vs[t + 1]])
            lo = np.floor(T.min(0) - 2).astype(int); hi = np.ceil(T.max(0) + 2).astype(int)
            if (hi - lo).max() > 400: continue
            xs, ys = np.meshgrid(np.arange(max(lo[0], 0), min(hi[0], W)), np.arange(max(lo[1], 0), min(hi[1], H)))
            c = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
            v0, v1 = T[1] - T[0], T[2] - T[0]; den = v0[0] * v1[1] - v0[1] * v1[0]
            if abs(den) < 1e-9: continue
            q = c - T[0]
            b1 = (q[:, 0] * v1[1] - q[:, 1] * v1[0]) / den; b2 = (v0[0] * q[:, 1] - v0[1] * q[:, 0]) / den
            bc = np.stack([1 - b1 - b2, b1, b2], 1)
            heights = np.array([abs(den) / max(np.linalg.norm(T[(k + 2) % 3] - T[(k + 1) % 3]), 1e-9) for k in range(3)])
            sel = np.max(-bc * heights, axis=1) <= 1.5
            bc = np.clip(bc[sel], 0, None); bc /= bc.sum(1, keepdims=True)
            px_ = c[sel].astype(int)
            yield px_[:, 1], px_[:, 0], bc @ D


# the body's fawn, as the old texture has it on the back (red/green 1.23, green/blue 1.21); the new model's crown and
# ears are a near-neutral cream (1.11, 1.08) that some lights turn to silver
WARM = np.array([1.08, 1.0, 0.915])
EAR_SHADE = 0.85              # the ears a shade darker than the head


def match_tone(bm, placed, newmat, old_img, new_img):
    """The new texture made to sit with the old one, all of it alike (the fade by the seam reads it outside the piece
    as well): its pale near-neutral hairs warmed to the body's fawn (the reddish ones are left nearly as they are),
    then scaled to the old skin's tone along the seam; and the ears a shade darker, fading in over their roots."""
    uvl = bm.loops.layers.uv.active
    piece = [f for f in bm.faces if f.material_index == newmat]
    px = pixels(new_img); c = px[:, :, :3]
    sat = (c.max(2) - c.min(2)) / np.maximum(c.max(2), 1e-6)
    k = np.clip(1 - sat / 0.25, 0, 1)[:, :, None]
    c *= 1 + (WARM - 1) * k
    so, sn = blurred(pixels(old_img)), blurred(px)
    ratios = []
    for rim, B, sB in placed:
        for b, t in zip(B, sB):
            a = rim.A[int(np.argmin(np.abs((rim.s - t + rim.total / 2) % rim.total - rim.total / 2)))]
            # (each skin's tone in the middle of a face by the seam: a vertex's own UV is on an island's edge)
            fo = [l.face for l in a.link_loops if l.face.material_index != newmat and l.face.calc_area() > 1e-7]
            fn = [l.face for l in b.link_loops if l.face.material_index == newmat]
            if not fo or not fn: continue
            ou = sum((l[uvl].uv for l in fo[0].loops), V3((0, 0)).to_2d()) / len(fo[0].loops)
            nu = sum((l[uvl].uv for l in fn[0].loops), V3((0, 0)).to_2d()) / len(fn[0].loops)
            ratios.append(tone_at(so, ou) / np.maximum(tone_at(sn, nu), 1e-3))
    gain = np.clip(np.median(ratios, axis=0), 0.8, 1.2)
    c *= gain
    print("new texture warmed and scaled by", np.round(gain, 3), "to the old skin at the seam")
    # the ears a shade darker: per vertex of the piece, how much ear it is (spread a little over the root)
    lay = bm.verts.layers.int.get('ear')
    pv = sorted({v for f in piece for v in f.verts}, key=lambda v: v.index); ix = {v: k for k, v in enumerate(pv)}
    e = np.array([1.0 if v[lay] else 0.0 for v in pv])
    ea = np.array([(ix[x.verts[0]], ix[x.verts[1]]) for x in bm.edges if x.verts[0] in ix and x.verts[1] in ix])
    deg = np.bincount(ea.ravel(), minlength=len(pv)).astype(float)
    for _ in range(6):
        acc = np.zeros_like(e); np.add.at(acc, ea[:, 0], e[ea[:, 1]]); np.add.at(acc, ea[:, 1], e[ea[:, 0]])
        e = 0.5 * e + 0.5 * acc / np.maximum(deg, 1)
    H, W = px.shape[:2]
    shade = np.ones((H, W), np.float32)
    ears = [f for f in piece if any(e[ix[v]] > 0 for v in f.verts)]
    for ys, xs, val in texels(ears, uvl, W, H, lambda v: np.array([1.0 + (EAR_SHADE - 1.0) * e[ix[v]]])):
        shade[ys, xs] = val[:, 0]
    c *= shade[:, :, None]
    px[:, :, :3] = np.clip(c, 0, 1)
    new_img.pixels.foreach_set(px.ravel()); new_img.update(); new_img.pack()
    return px


def cut_along(mesh, f, level=0.5):
    """The mesh split along the line where the vertex field f crosses level: a vertex where each edge crosses it (its
    UVs in between), and each face it crosses split there. Returns f for the vertices now (level on the line)."""
    bm = bmesh.new(); bm.from_mesh(mesh.data)
    uvl = bm.loops.layers.uv.active
    lay = bm.verts.layers.float.new('cut_f')
    bm.verts.ensure_lookup_table()
    on = set()
    for v in bm.verts:
        v[lay] = f[v.index]
        if abs(f[v.index] - level) < 1e-3: v[lay] = level; on.add(v)
    for e in list(bm.edges):
        a, b = e.verts; fa, fb = a[lay] - level, b[lay] - level
        if fa * fb >= 0: continue
        t = fa / (fa - fb); pa, pb = a.co.copy(), b.co.copy()
        uv = {fc: ([l for l in fc.loops if l.vert is a][0][uvl].uv.copy(), [l for l in fc.loops if l.vert is b][0][uvl].uv.copy())
              for fc in e.link_faces}
        ne, nv = bmesh.utils.edge_split(e, a, t)
        nv.co = pa.lerp(pb, t); nv[lay] = level; on.add(nv)
        for l in nv.link_loops:
            if l.face in uv: l[uvl].uv = uv[l.face][0].lerp(uv[l.face][1], t)
    for fc in list(bm.faces):
        vs = [l.vert for l in fc.loops if l.vert in on]
        if len(vs) != 2: continue
        a, b = vs
        if any(e.other_vert(a) is b for e in a.link_edges): continue
        bmesh.utils.face_split(fc, a, b)
    bm.verts.index_update()
    out = [v[lay] for v in bm.verts]
    bm.to_mesh(mesh.data); bm.free()
    return out


def inside(me, f, level=0.5):
    return {p.index for p in me.polygons if sum(f[v] for v in p.vertices) / len(p.vertices) > level}


def seam_normals(me, seam, inner=0.01, outer=0.03, radius=0.015):
    """One shading across the seam: the old skin keeps Tripo's normals, the new one has its own, and the two met in a
    hard line. Near the seam each corner takes the area-weighted normal of the faces round its point, whichever skin
    they belong to, fading back to its own normal 1 to 3 cm away."""
    fk = KDTree(len(me.polygons))
    for p in me.polygons: fk.insert(p.center, p.index)
    fk.balance()
    own = [V3(n.vector) for n in me.corner_normals]
    out = []
    for l in me.loops:
        v = me.vertices[l.vertex_index]
        k = 1 - sstep(seam.find(v.co)[2], inner, outer)
        if k <= 0: out.append(own[l.index]); continue
        n = sum((me.polygons[i].normal * me.polygons[i].area for _, i, _ in fk.find_range(v.co, radius)), V3())
        out.append(own[l.index].lerp(n.normalized(), k).normalized() if n.length > 1e-12 else own[l.index])
    me.normals_split_custom_set(out)


def transplant(mesh, path):
    """Graft the ears of the Tripo model at path onto mesh (the straightened, decimated standing model)."""
    new = import_tripo(path); new.name = "EarSource"
    drop_crumbs(new); weld(new); decimate(new, 17000)
    new.data.normals_split_custom_set([(0.0, 0.0, 0.0)] * len(new.data.loops))     # (its own shading, welded)
    found = hanging(new.data, NEW_MID, 0.12, 150, np.arange(0.55, 0.66, 0.005))
    ears = {s: v for s, (zc, v) in found.items()}
    for s, (zc, v) in sorted(found.items()): print(f"new ear {s}: cut at z {zc}, {len(v)} verts")
    allear = ears['L'] | ears['R']
    M = fit_head(new, mesh, allear); Minv = M.inverted()
    nm = new.data
    side_of = {i: 1 for i in ears['L']}; side_of.update({i: 2 for i in ears['R']})
    # the new piece: the ears and the head they grow from (crown, sides), cut out along the zone's middle line
    fn = cut_along(new, [1.0 if v.index in allear else zone(v.co) for v in nm.vertices])
    nb = face_neighbours(nm)
    earf = {p.index for p in nm.polygons if all(v in allear for v in p.vertices)}
    keep = unpinch(tidy(inside(nm, fn) | earf, nb), nm, nb)
    keep = set().union(*(c for c in pieces(keep, nb) if c & earf))
    nm.transform(M)
    surf = Surface(new, allear)                                     # (the whole new head, for its colours)
    me = mesh.data
    # the old head: what lies over the new piece goes (each vertex takes the zone of its nearest point on the new
    # head, ears left out: they hang by the old shoulder), so the two cuts are one line on two skins; and the old
    # ear with it: what hung free, and what of it stands out of the new head next to that (low behind the ear, where
    # it was fused to the neck)
    nm.calc_loop_triangles()
    tris = [t for t in nm.loop_triangles if t.polygon_index not in earf]
    bvh = BVHTree.FromPolygons([v.co.copy() for v in nm.vertices], [tuple(t.vertices) for t in tris])
    fo, out = [], []
    for v in me.vertices:
        loc, nor, i, d = bvh.find_nearest(v.co)
        fo.append(zone(Minv @ loc) if v.co.y < -0.10 else 0.0); out.append((v.co - loc).dot(nor))
    old = hanging(me, OLD_MID, 0.10, 100, np.arange(0.40, 0.60, 0.005), ylim=-0.12)
    for s, (zc, v) in sorted(old.items()): print(f"old ear {s}: hangs free below z {zc}, {len(v)} verts")
    flap = set().union(*(v for zc, v in old.values()))
    onb = face_neighbours(me)
    ear_out = {p.index for p in me.polygons if p.center.y < -0.12 and p.center.z > 0.33
               and abs(head(Minv @ p.center).x) > 0.05 and min(out[v] for v in p.vertices) > 0.006}
    grown = inside(me, fo) | {p.index for p in me.polygons if any(v in flap for v in p.vertices)}
    stack = list(grown)
    while stack:
        f = stack.pop()
        for o in onb[f]:
            if o in ear_out and o not in grown: grown.add(o); stack.append(o)
    for f in grown - inside(me, fo):
        for v in me.polygons[f].vertices: fo[v] = 1.0
    fo = cut_along(mesh, fo)
    onb = face_neighbours(me)
    hole = unpinch(tidy(inside(me, fo), onb), me, onb)
    body = max(pieces(set(range(len(me.polygons))) - hole, onb), key=len)      # (and any crumbs left behind)
    print("old head: faces out", len(me.polygons) - len(body))
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in range(len(me.polygons)) if i not in body], context='FACES')
    bm.to_mesh(me); bm.free()
    bm = bmesh.new(); bm.from_mesh(nm); bm.faces.ensure_lookup_table()
    lay = bm.verts.layers.int.new('ear')
    for v in bm.verts: v[lay] = side_of.get(v.index, 0)
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in range(len(nm.polygons)) if i not in keep], context='FACES')
    bm.to_mesh(nm); bm.free()
    print("new piece: faces", len(nm.polygons))
    # join, then sew: the new piece's rim drawn onto the old rim, the rest of the piece following smoothly
    if 'ear' not in me.attributes: me.attributes.new('ear', 'INT', 'POINT')
    bpy.ops.object.select_all(action='DESELECT')
    new.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.join()
    me = mesh.data
    newmat = len(me.materials) - 1
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    isnew = {v for f in bm.faces if f.material_index == newmat for v in f.verts}
    loops = rims(bm)
    old_r = [l for l in loops if not any(v in isnew for v in l)]
    new_r = [l for l in loops if all(v in isnew for v in l)]
    print("rims: old", sorted(len(l) for l in old_r)[::-1][:4], "new", sorted(len(l) for l in new_r)[::-1])
    uvl = bm.loops.layers.uv.active
    def centre(l): return sum((v.co for v in l), V3()) / len(l)
    pairs = []
    for B in [l for l in new_r if len(l) > 20]:
        A = min((l for l in old_r if len(l) > 20), key=lambda l: (centre(l) - centre(B)).length)
        pairs.append((Rim(along(A)), B))
    # the piece's rim onto the old rim (each vertex to its nearest point there), and the shift carried smoothly over
    # the piece (harmonic, by its edges: the ear goes with its root)
    nv = sorted(isnew, key=lambda v: v.index); ix = {v: k for k, v in enumerate(nv)}
    fixed = np.zeros(len(nv), bool); shift = np.zeros((len(nv), 3))
    placed = []
    for rim, B in pairs:
        B, sB = follow(rim, B); placed.append((rim, B, sB))
        for v, t in zip(B, sB):
            shift[ix[v]] = rim.at(t) - np.array(tuple(v.co)); fixed[ix[v]] = True
    ea = np.array([(ix[e.verts[0]], ix[e.verts[1]]) for e in bm.edges if e.verts[0] in ix and e.verts[1] in ix])
    deg = np.bincount(ea.ravel(), minlength=len(nv)).astype(float)
    for _ in range(1500):
        acc = np.zeros_like(shift); np.add.at(acc, ea[:, 0], shift[ea[:, 1]]); np.add.at(acc, ea[:, 1], shift[ea[:, 0]])
        shift = np.where(fixed[:, None], shift, acc / np.maximum(deg, 1)[:, None])
    print("piece moved onto the old head: rim by up to %.1f mm (mean %.1f), inside by up to %.1f mm" % (
        1000 * np.linalg.norm(shift[fixed], axis=1).max(), 1000 * np.linalg.norm(shift[fixed], axis=1).mean(),
        1000 * np.linalg.norm(shift[~fixed], axis=1).max()))
    for rim, B, sB in placed: print("  sewn with", len(sew(bm, rim, B, sB, uvl, 0, newmat)), "triangles")
    for v in nv: v.co += V3(shift[ix[v]])
    # the first rings of the piece inside its rim evened out: the rim slid along the old one and folded them
    rimv = {v for rim, B, sB in placed for v in B}
    ring, near_rim = set(rimv), set()
    for _ in range(3):
        ring = {o for v in ring for e in v.link_edges for o in [e.other_vert(v)] if o in ix and o not in rimv} - near_rim
        near_rim |= ring
    for _ in range(10):
        co = {v: sum((e.other_vert(v).co for e in v.link_edges), V3()) / len(v.link_edges) for v in near_rim}
        for v, c in co.items(): v.co = v.co.lerp(c, 0.5)
    seam = [tuple(v.co) for rim, B, sB in placed for v in rim.A]
    bm.verts.index_update()
    placed = [([v.index for v in rim.A], [v.index for v in B], sB) for rim, B, sB in placed]
    bm.to_mesh(me); bm.free()
    # the new piece toned to the old skin, then the old skin by the seam fades into the new one's colours over 2.5 cm
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    placed = [(Rim([bm.verts[i] for i in A]), [bm.verts[i] for i in B], sB) for A, B, sB in placed]
    surf.px = match_tone(bm, placed, newmat, image_of_material(me.materials[0]), image_of_material(me.materials[newmat]))
    bm.free()
    kd = KDTree(len(seam))
    for k, c in enumerate(seam): kd.insert(c, k)
    kd.balance()
    w = [1 - sstep(kd.find(v.co)[2], 0.0, 0.025) for v in me.vertices]
    repaint(mesh, w, surf)
    seam_normals(me, kd)
    bm = bmesh.new(); bm.from_mesh(me)
    print("open edges left:", sum(1 for e in bm.edges if e.is_boundary)); bm.free()
    print("ears grafted:", sum(1 for a in me.attributes['ear'].data if a.value), "verts;", len(me.vertices), "verts in all")
    return mesh
