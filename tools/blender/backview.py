import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; a=argv[1]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(0)
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=420
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.0
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
for name,loc,rot in [('back',(0,3,0.35),(math.pi/2,0,math.pi)),('front',(0,-3,0.35),(math.pi/2,0,0))]:
    co.location=loc; co.rotation_euler=rot; sc.render.filepath=f"{out}/_b_{a}_{name}.png"; bpy.ops.render.render(write_still=True)
