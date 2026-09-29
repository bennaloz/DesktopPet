import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import work_file
"""Extreme test poses of the Bretzel rig, side and three-quarter: python bretzel_poses.py -- <out dir>"""
import bpy, math
out=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; P=arm.pose.bones
for pb in P: pb.rotation_mode='ZYX'
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=400; sc.render.resolution_y=360; sc.render.film_transparent=True
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.5
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
D=math.radians
POSES={
 'rest':{},
 'stretch':{'Hips':(20,0,0),'Spine':(-12,0,0),'Chest':(-8,0,0),'Neck':(-20,0,0),
            'Thigh.L':(-45,0,0),'Shin.L':(30,0,0),'Foot.L':(20,0,0),'Thigh.R':(-45,0,0),'Shin.R':(30,0,0),'Foot.R':(20,0,0),
            'UpperArm.L':(-45,0,0),'UpperArm.R':(-45,0,0)},
 'tuck':{'Hips':(-15,0,0),'Spine':(15,0,0),'Thigh.L':(40,0,0),'Shin.L':(-30,0,0),'Thigh.R':(40,0,0),'Shin.R':(-30,0,0),
         'UpperArm.L':(40,0,0),'Forearm.L':(-60,0,0),'UpperArm.R':(40,0,0),'Forearm.R':(-60,0,0)},
 'headdown':{'Neck':(30,0,0),'Head':(15,0,0)},
 'ears':{'Ear1.L':(10,0,-8),'Ear2.L':(6,0,-4),'Ear1.R':(-10,0,8),'Ear2.R':(-6,0,4)},
 'twist':{'Hips':(0,25,0),'Chest':(0,-25,0),'Neck':(0,0,30)},
}
views={'side':((3,0,0.4),(math.pi/2,0,math.pi/2)),'tq':((2.4,-1.4,0.8),(math.radians(78),0,math.radians(60)))}
for name,pose in POSES.items():
    for pb in P: pb.rotation_euler=(0,0,0)
    for b,(x,y,z) in pose.items(): P[b].rotation_euler=(D(x),D(y),D(z))
    bpy.context.view_layer.update()
    for v,(loc,rot) in views.items():
        co.location=loc; co.rotation_euler=rot
        sc.render.filepath=f"{out}/pose_{name}_{v}.png"; bpy.ops.render.render(write_still=True)
