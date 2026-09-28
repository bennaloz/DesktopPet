import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; act=argv[1]; tag=argv[2]; fr=int(argv[3]) if len(argv)>3 else 0
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]; sc.frame_set(fr)
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.film_transparent=True
# real desktop size: 1.058 model units = 150 px; render 2x for a crisp look at 1:1 and 2:1
px=150/1.058*2; sc.render.resolution_x=int(1.4*px); sc.render.resolution_y=int(1.0*px)
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.4
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
a=math.radians(65); co.location=(3*math.sin(a),-3*math.cos(a),0.42); co.rotation_euler=(math.pi/2,0,a)
sc.render.filepath=f"{out}/_as_{act}_{tag}.png"; bpy.ops.render.render(write_still=True)
