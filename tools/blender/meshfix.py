"""Mesh repairs shared by the pets' pipelines."""
import bmesh

def weld(mesh):
    """One vertex per point: Tripo splits the mesh along its UV seams (the UVs stay on the face corners). Decimated
    split, the two sides of every seam were thinned apart and the skin had hairline cracks along all of them. Weld
    before decimating."""
    bm = bmesh.new(); bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(mesh.data); bm.free()

def keep_largest(mesh):
    """Drop every loose part but the biggest. A voxel remesh of the welded body also closes small pockets inside it
    into shells of their own, and with one of those in an auto-weight proxy bone heat finds no solution for any bone."""
    bm = bmesh.new(); bm.from_mesh(mesh.data)
    seen, parts = set(), []
    for v in bm.verts:
        if v in seen: continue
        part, stack = [v], [v]; seen.add(v)
        while stack:
            for e in stack.pop().link_edges:
                for w in e.verts:
                    if w not in seen: seen.add(w); part.append(w); stack.append(w)
        parts.append(part)
    parts.sort(key=len, reverse=True)
    drop = [v for p in parts[1:] for v in p]
    if drop: bmesh.ops.delete(bm, geom=drop, context='VERTS')
    print("loose parts dropped", [len(p) for p in parts[1:]])
    bm.to_mesh(mesh.data); bm.free()
