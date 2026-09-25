import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, sys, math
out=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions["Trot"]; sc.frame_set(0)
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=500
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=0.45
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
for n,(loc,rot) in {'side':((3,0.3,0.45),(math.pi/2,0,math.pi/2)),'otherside':((-3,0.3,0.45),(math.pi/2,0,-math.pi/2)),'back':((0,3,0.45),(math.pi/2,0,math.pi))}.items():
    co.location=loc; co.rotation_euler=rot; sc.render.filepath=f"{out}/_tz_{n}.png"; bpy.ops.render.render(write_still=True)
