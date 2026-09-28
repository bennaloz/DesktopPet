import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import source_file
import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source_file("tripo.glb"))
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, "parent=", o.parent.name if o.parent else None, "dims=", tuple(round(d,3) for d in o.dimensions), "loc=", tuple(round(v,3) for v in o.location), "rot=", tuple(round(v,3) for v in o.rotation_euler), "scale=", tuple(round(v,3) for v in o.scale))
    if o.type=='MESH':
        me=o.data; print("  verts",len(me.vertices),"polys",len(me.polygons),"groups",len(o.vertex_groups), "mats",[m.name for m in me.materials])
        import mathutils
        ws=[o.matrix_world@v.co for v in me.vertices]
        mn=[min(w[i] for w in ws) for i in range(3)]; mx=[max(w[i] for w in ws) for i in range(3)]
        print("  world bbox", [round(x,3) for x in mn], [round(x,3) for x in mx])
    if o.type=='ARMATURE':
        for b in o.data.bones:
            h=o.matrix_world@b.head_local; t=o.matrix_world@b.tail_local
            print("  BONE", b.name, "parent=", b.parent.name if b.parent else None, "head", [round(x,3) for x in h], "tail", [round(x,3) for x in t])
for a in bpy.data.actions: print("ACTION", a.name, a.frame_range)
for i in bpy.data.images: print("IMG", i.name, i.size[:])
