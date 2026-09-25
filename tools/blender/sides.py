import bpy, sys
act=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]; bpy.context.scene.frame_set(0)
m=bpy.data.objects["Zaira"]; em=m.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
rest=m.data.vertices; V=[m.matrix_world@v.co for v in em.vertices]
for side,f in (("L (+x)",lambda r:r.x>0.03),("R (-x)",lambda r:r.x<-0.03)):
    idx=[i for i,v in enumerate(rest) if 0.05<v.co.y<0.32 and 0.15<v.co.z<0.5 and f(v.co)]
    zs=[V[i].z for i in idx]; ys=[V[i].y for i in idx]; xs=[V[i].x for i in idx]
    print(act,side,"haunch min z",round(min(zs),3),"max z",round(max(zs),3),"mean y",round(sum(ys)/len(ys),3),"x range",round(min(xs),3),round(max(xs),3))
P=arm.pose.bones
for s in ('L','R'):
    k=arm.matrix_world@P['Shin.'+s].head; h=arm.matrix_world@P['Thigh.'+s].head
    print(act,"knee",s,[round(c,3) for c in k],"hip",[round(c,3) for c in h])
