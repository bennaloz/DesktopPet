import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; a=argv[1]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(0)
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=420
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.2
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
co.location=(0,0,3); co.rotation_euler=(0,0,0); sc.render.filepath=f"{out}/_p_{a}_top.png"; bpy.ops.render.render(write_still=True)
