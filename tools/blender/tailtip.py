import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
arm=bpy.data.objects["Rig"]; m=bpy.data.objects["Zaira"]
for a in ("Trot","Walk"):
    arm.animation_data.action=bpy.data.actions[a]
    mx=0
    for f in range(0,18,3):
        bpy.context.scene.frame_set(f)
        em=m.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        mx=max(mx,max(v.co.z for v in em.vertices))
    print("TOP",a,round(mx,3))
