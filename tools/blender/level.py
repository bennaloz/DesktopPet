import bpy, sys
act=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]
bpy.context.scene.frame_set(0)
m=bpy.data.objects["Zaira"]; dg=bpy.context.evaluated_depsgraph_get(); em=m.evaluated_get(dg).to_mesh()
V=[m.matrix_world@v.co for v in em.vertices]
rest=bpy.data.objects["Zaira"].data.vertices
regions={'front paws':lambda r:r.y<-0.2 and r.z<0.06,'hind paws':lambda r:0.1<r.y<0.3 and r.z<0.06,
         'rump (rest y 0.1..0.3, z 0.25..0.45)':lambda r:0.1<r.y<0.3 and 0.25<r.z<0.45,'tail tip':lambda r:r.y>0.4 and r.z<0.2}
for name,f in regions.items():
    zs=[V[i].z for i,v in enumerate(rest) if f(v.co)]
    print(act,name,"min z",round(min(zs),3))
print(act,"overall min z",round(min(v.z for v in V),3))
