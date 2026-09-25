import bpy, sys
act=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]; bpy.context.scene.frame_set(0)
m=bpy.data.objects["Zaira"]; em=m.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
names={g.index:g.name for g in m.vertex_groups}
V=[(v.co.z,i) for i,v in enumerate(em.vertices)]; V.sort()
import collections
c=collections.Counter()
for z,i in V[:300]:
    if z<-0.005:
        g=max(m.data.vertices[i].groups,key=lambda g:g.weight); c[names[g.group]]+=1
print(act,"below floor by bone:",c.most_common(6),"lowest z",round(V[0][0],3))
