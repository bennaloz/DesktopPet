import bpy
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
for img in bpy.data.images:
    if img.size[0]>2048:
        img.scale(2048,2048); print("scaled",img.name)
arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions["Idle"]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=r"C:/develop/personal/ZairaDesktopPet/cats/zaira/zaira.glb", export_format='GLB',
    export_animation_mode='ACTIONS', export_anim_single_armature=True, export_skins=True, export_force_sampling=True,
    export_image_format='JPEG', export_jpeg_quality=90, export_yup=True, export_apply=False)
