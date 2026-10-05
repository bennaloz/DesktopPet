"""The Tripo rabbit as the rig wants it: one mesh, no Tripo armature, Z up, head towards -Y, feet on z=0, the
body straight and the loose crumbs gone.

The Tripo model stands on all fours but bent sideways: chest and rump are each straight along Y, the rump 15 cm to
one side of the chest. The side-plane rig needs one straight back, so the mesh is sheared back into line (x moved by
a function of y only: the legs, upright columns, stay upright). The two sides still differ in the legs (the left
hind foot half a stride ahead of the right): the skeleton follows each leg where it is. (Mirroring one half would
make them equal but loses the scut, which sits on the left, and smears the texture.)"""
import bpy, bmesh, math
from meshfix import weld

FRONT_X, BACK_X = -0.072, 0.097       # the midline of the chest and of the rump, in the Tripo mesh
BEND = (-0.20, 0.32)                  # where along the body one turns into the other

def sstep(x, a, b):
    t = max(0.0, min(1.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)

def midline(y): return FRONT_X + (BACK_X - FRONT_X) * sstep(y, *BEND)

def import_tripo(path):
    """A Tripo GLB's mesh into the scene as it is, without Tripo's armature and the stray icosphere."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]
    mesh = [o for o in added if o.type == 'MESH' and len(o.data.vertices) > 1000][0]
    mw = mesh.matrix_world.copy(); mesh.parent = None; mesh.matrix_world = mw
    for m in list(mesh.modifiers): mesh.modifiers.remove(m)
    mesh.vertex_groups.clear()
    for o in added:
        if o != mesh: bpy.data.objects.remove(o)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    mesh.data.transform(mesh.matrix_world); mesh.matrix_world.identity()
    return mesh

def drop_crumbs(mesh, crumbs=300):
    """Loose crumbs (whiskers floating by the muzzle): islands of a few hundred vertices; the mesh is split along its
    UV seams, so islands are taken by position (welded copy), not by the raw edges."""
    me = mesh.data
    bm = bmesh.new(); bm.from_mesh(me)
    tmp = bm.copy(); bmesh.ops.remove_doubles(tmp, verts=tmp.verts, dist=1e-5)
    tmp.verts.ensure_lookup_table()
    island = {}; sizes = []
    for v in tmp.verts:
        if v.index in island: continue
        k = len(sizes); stack = [v]; island[v.index] = k; n = 0
        while stack:
            q = stack.pop(); n += 1
            for e in q.link_edges:
                o = e.other_vert(q)
                if o.index not in island: island[o.index] = k; stack.append(o)
        sizes.append(n)
    from mathutils.kdtree import KDTree
    kd = KDTree(len(tmp.verts))
    for v in tmp.verts: kd.insert(v.co, v.index)
    kd.balance()
    bm.verts.ensure_lookup_table()
    drop = [v for v in bm.verts if sizes[island[kd.find(v.co)[1]]] < crumbs]
    bmesh.ops.delete(bm, geom=drop, context='VERTS')
    print("islands", sorted(sizes, reverse=True)[:8], "dropped verts", len(drop))
    bm.to_mesh(me); bm.free(); tmp.free()

def decimate(mesh, polys):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = mesh; mesh.select_set(True)
    dec = mesh.modifiers.new("dec", 'DECIMATE'); dec.ratio = polys / len(mesh.data.polygons)
    bpy.ops.object.modifier_apply(modifier="dec")
    print("polys", len(mesh.data.polygons))

def load(path, crumbs=300, polys=16000):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = import_tripo(path)
    for v in mesh.data.vertices: v.co.x -= midline(v.co.y)
    drop_crumbs(mesh, crumbs)
    weld(mesh)
    decimate(mesh, polys)
    return mesh
