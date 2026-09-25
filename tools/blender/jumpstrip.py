import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, sys, math
out=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=360
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.3
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
co.location=(3,0,0.4); co.rotation_euler=(math.pi/2,0,math.pi/2)
shots=[('Prejump',7,'carica'),('Jump',4,'spinta'),('Jump',9,'volo'),('Fall',4,'discesa'),('Fall',8,'pronta'),('Land',3,'ammortizza'),('Land',8,'in piedi')]
for i,(a,f,label) in enumerate(shots):
    arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(f)
    sc.render.filepath=f"{out}/_j_{i}.png"; bpy.ops.render.render(write_still=True)
open(f"{out}/_j.txt","w").write("\n".join(s[2] for s in shots))
