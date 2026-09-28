import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, math
bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
arm=bpy.data.objects["Rig"]
P=arm.pose.bones
for pb in P: pb.rotation_mode='XYZ'
def r(n,x=0,y=0,z=0): P[n].rotation_euler=(math.radians(x),math.radians(y),math.radians(z))
r('Thigh.R',35); r('Shin.R',-30); r('Thigh.L',-25)
r('UpperArm.L',-35); r('Forearm.L',20); r('UpperArm.R',30); r('Forearm.R',-40)
r('Head',30); r('Neck',25)
r('Tail1',8,z=8); r('Tail2',4,z=4); r('Tail3',0,z=6)
bpy.ops.wm.save_as_mainfile(filepath=work_file("testpose.blend"))
