import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, sys
act=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]
bpy.context.scene.frame_set(0)
m=bpy.data.objects["Zaira"]; dg=bpy.context.evaluated_depsgraph_get(); em=m.evaluated_get(dg).to_mesh()
V=[m.matrix_world@v.co for v in em.vertices]
rest=bpy.data.objects["Zaira"].data.vertices
regions={'front paws':lambda r:r.y<-0.2 and r.z<0.06,'hind paws':lambda r:0.1<r.y<0.3 and r.z<0.06,
         'rump (rest y 0.1..0.3, z 0.25..0.45)':lambda r:0.1<r.y<0.3 and 0.25<r.z<0.45,'tail tip':lambda r:r.y>0.4 and r.z<0.2,'belly':lambda r:-0.15<r.y<0.08 and 0.24<r.z<0.29 and abs(r.x)<0.06}
for name,f in regions.items():
    zs=[V[i].z for i,v in enumerate(rest) if f(v.co)]
    print(act,name,"min z",round(min(zs),3))
print(act,"overall min z",round(min(v.z for v in V),3))
P=arm.pose.bones
for s in ('L','R'):
    sh=arm.matrix_world@P['UpperArm.'+s].head; el=arm.matrix_world@P['Forearm.'+s].head; wr=arm.matrix_world@P['Hand.'+s].head
    import math
    tilt=lambda a,b: round(math.degrees(math.atan2(b.y-a.y, a.z-b.z)),1)
    print(act,"front",s,"shoulder->elbow tilt",tilt(sh,el),"elbow->wrist tilt",tilt(el,wr),"(0 = vertical, + = paw behind)")
for s in ('L','R'):
    hock=arm.matrix_world@P['Foot.'+s].head; toe=arm.matrix_world@P['Toe.'+s].head
    print(act,"hind",s,"hock z",round(hock.z,3),"toe-base z",round(toe.z,3),"(flat foot: hock close to the ground)")
