import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy
bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
m=bpy.data.objects["Zaira"]; me=m.data
T={m.vertex_groups[n].index for n in ['Tail1','Tail2','Tail3','Tail4','Tail5']}
def tw(v): return sum(g.weight for g in v.groups if g.group in T)
w=[tw(v) for v in me.vertices]
bad=[]
for p in me.polygons:
    ws=[w[i] for i in p.vertices]
    if max(ws)>0.9 and min(ws)<0.1: bad.append(p)
import collections
print("mixed faces",len(bad))
for p in bad[:15]:
    c=p.center; L=max((me.vertices[a].co-me.vertices[b].co).length for a in p.vertices for b in p.vertices)
    print(round(c.x,3),round(c.y,3),round(c.z,3),"edge",round(L,4))
