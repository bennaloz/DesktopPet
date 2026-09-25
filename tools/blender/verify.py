import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import repo_file
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=repo_file("cats/zaira/zaira.glb"))
print("ACTIONS", sorted(a.name for a in bpy.data.actions))
for o in bpy.data.objects: print("OBJ",o.type,o.name, [round(x,3) for x in o.dimensions])
print("IMG",[ (i.name,i.size[:]) for i in bpy.data.images])
