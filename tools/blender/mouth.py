import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
arm=bpy.data.objects["Rig"]; m=bpy.data.objects["Zaira"]
ys=[v.co.y for v in m.data.vertices]; xs=[v.co.x for v in m.data.vertices]; zs=[v.co.z for v in m.data.vertices]
cy=(min(ys)+max(ys))/2; length=max(max(ys)-min(ys), max(xs)-min(xs))
arm.animation_data.action=bpy.data.actions["Eat"]; bpy.context.scene.frame_set(0)
em=m.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
head=[v.co for v in em.vertices]
# mouth: the most forward vertices of the head, lowest ones
front=sorted(head,key=lambda c:c.y)[:40]
my=sum(c.y for c in front)/len(front); mz=min(c.z for c in front)
px=150/length
print("MOUTH rest-bbox-center y",round(cy,3),"eat mouth y",round(my,3),"z",round(mz,3),"reach units",round(cy-my,3),"reach px",round((cy-my)*px,1),"mouth height px",round(mz*px,1))
