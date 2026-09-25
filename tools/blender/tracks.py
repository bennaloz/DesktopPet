import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import repo_file
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=repo_file("cats/zaira/zaira.glb"))
for a in sorted(bpy.data.actions, key=lambda a:a.name):
    paths=set()
    for layer in getattr(a,'layers',[]):
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves: paths.add(fc.data_path.split('"')[1] if '"' in fc.data_path else fc.data_path)
    if not paths:
        for fc in getattr(a,'fcurves',[]): paths.add(fc.data_path.split('"')[1] if '"' in fc.data_path else fc.data_path)
    print("TRACKS", a.name, len(paths), "Thigh.L" in paths)
