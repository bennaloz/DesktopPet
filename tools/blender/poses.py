import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; acts=argv[1].split(',')
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=420
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.2
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
bpy.ops.mesh.primitive_plane_add(size=3,location=(0,0,-0.002))
views={'side':((3,0,0.35),(math.pi/2,0,math.pi/2)),'tq':((2.6,-1.5,0.9),(math.radians(78),0,math.radians(60)))}
for a in acts:
    arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(0)
    for v,(loc,rot) in views.items():
        co.location=loc; co.rotation_euler=rot
        sc.render.filepath=f"{out}/_p_{a}_{v}.png"; bpy.ops.render.render(write_still=True)
