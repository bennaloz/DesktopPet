import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, collections
bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
m=bpy.data.objects["Zaira"]; names={g.index:g.name for g in m.vertex_groups}
for label,cond in [("head y<-0.40",lambda c:c.y<-0.40),("muzzle y<-0.46",lambda c:c.y<-0.46),("tail y>0.36 z<0.4",lambda c:c.y>0.36 and c.z<0.4),("front paws z<0.06 y<0",lambda c:c.z<0.06 and c.y<0)]:
    cnt=collections.Counter()
    for v in m.data.vertices:
        if cond(v.co) and v.groups:
            g=max(v.groups,key=lambda g:g.weight); cnt[names[g.group]]+=1
    print(label, cnt.most_common(6))
