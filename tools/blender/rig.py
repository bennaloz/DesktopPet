import bpy, mathutils, numpy as np
from mathutils import Vector as V3
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/mesh.blend")
mesh=bpy.data.objects["Zaira"]
# lighter mesh
bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)
dec=mesh.modifiers.new("dec",'DECIMATE'); dec.ratio=0.28
bpy.ops.object.modifier_apply(modifier="dec")
print("polys after decimate", len(mesh.data.polygons))

B={ # name: (head, tail, parent, connected)
 'Hips':((0,0.22,0.45),(0,0.08,0.47),None,False),
 'Spine':((0,0.08,0.47),(0,-0.08,0.47),'Hips',True),
 'Chest':((0,-0.08,0.47),(0,-0.24,0.47),'Spine',True),
 'Neck':((0,-0.24,0.47),(0,-0.36,0.56),'Chest',True),
 'Head':((0,-0.36,0.56),(0,-0.50,0.57),'Neck',True),
 'Tail1':((0,0.29,0.47),(0,0.335,0.40),'Hips',False),
 'Tail2':((0,0.335,0.40),(0.01,0.36,0.31),'Tail1',True),
 'Tail3':((0.01,0.36,0.31),(0.03,0.385,0.23),'Tail2',True),
 'Tail4':((0.03,0.385,0.23),(0.08,0.43,0.15),'Tail3',True),
 'Tail5':((0.08,0.43,0.15),(0.15,0.47,0.09),'Tail4',True),
 'Thigh.L':((0.075,0.24,0.40),(0.08,0.15,0.27),'Hips',False),
 'Shin.L':((0.08,0.15,0.27),(0.077,0.23,0.13),'Thigh.L',True),
 'Foot.L':((0.077,0.23,0.13),(0.085,0.205,0.035),'Shin.L',True),
 'Toe.L':((0.085,0.205,0.035),(0.095,0.15,0.015),'Foot.L',True),
 'Thigh.R':((-0.095,0.24,0.40),(-0.105,0.15,0.27),'Hips',False),
 'Shin.R':((-0.105,0.15,0.27),(-0.10,0.225,0.13),'Thigh.R',True),
 'Foot.R':((-0.10,0.225,0.13),(-0.115,0.205,0.035),'Shin.R',True),
 'Toe.R':((-0.115,0.205,0.035),(-0.135,0.15,0.015),'Foot.R',True),
 'UpperArm.L':((0.07,-0.22,0.43),(0.075,-0.27,0.27),'Chest',False),
 'Forearm.L':((0.075,-0.27,0.27),(0.077,-0.28,0.08),'UpperArm.L',True),
 'Hand.L':((0.077,-0.28,0.08),(0.08,-0.33,0.015),'Forearm.L',True),
 'UpperArm.R':((-0.06,-0.22,0.43),(-0.05,-0.27,0.27),'Chest',False),
 'Forearm.R':((-0.05,-0.27,0.27),(-0.045,-0.28,0.08),'UpperArm.R',True),
 'Hand.R':((-0.045,-0.28,0.08),(-0.03,-0.33,0.015),'Forearm.R',True),
}
ad=bpy.data.armatures.new("Rig"); arm=bpy.data.objects.new("Rig",ad)
bpy.context.scene.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for n,(h,t,p,c) in B.items():
    eb=ad.edit_bones.new(n); eb.head=h; eb.tail=t
    d=(V3(t)-V3(h)).normalized()
    # roll: local X = world +X, so rotation about X is the sagittal bend for every bone
    eb.align_roll(V3((1,0,0)).cross(d))
for n,(h,t,p,c) in B.items():
    if p: ad.edit_bones[n].parent=ad.edit_bones[p]; ad.edit_bones[n].use_connect=c
bpy.ops.object.mode_set(mode='OBJECT')
for b in arm.data.bones: print(b.name, [round(x,2) for x in b.matrix_local.col[0][:3]])
# automatic weights on a watertight voxel proxy, then transfer to the real mesh
proxy=mesh.copy(); proxy.data=mesh.data.copy(); proxy.name="Proxy"
bpy.context.scene.collection.objects.link(proxy)
for vg in list(proxy.vertex_groups): proxy.vertex_groups.remove(vg)
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=proxy; proxy.select_set(True)
rm=proxy.modifiers.new("vox",'REMESH'); rm.mode='VOXEL'; rm.voxel_size=0.012
bpy.ops.object.modifier_apply(modifier="vox")
pd=proxy.modifiers.new("pd",'DECIMATE'); pd.ratio=min(1.0, 12000/len(proxy.data.polygons))
bpy.ops.object.modifier_apply(modifier="pd")
print("proxy verts", len(proxy.data.vertices), flush=True)
bpy.ops.object.select_all(action='DESELECT')
proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active=arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print("proxy weighted", flush=True)
for n in B:
    if n not in mesh.vertex_groups: mesh.vertex_groups.new(name=n)
dt=mesh.modifiers.new("dt",'DATA_TRANSFER'); dt.object=proxy
dt.use_vert_data=True; dt.data_types_verts={'VGROUP_WEIGHTS'}; dt.vert_mapping='POLYINTERP_NEAREST'
dt.layers_vgroup_select_src='ALL'; dt.layers_vgroup_select_dst='NAME'
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)
bpy.ops.object.modifier_apply(modifier="dt")
bpy.data.objects.remove(proxy)
mesh.parent=arm
am=mesh.modifiers.new("Armature",'ARMATURE'); am.object=arm
# clean left/right bleeding between the close legs
me=mesh.data; gi={g.name:g.index for g in mesh.vertex_groups}
def side_groups(s): return [gi[n] for n in gi if n.endswith('.'+s)]
L,R=side_groups('L'),side_groups('R')
fixed=0; empty=0
T=[gi[n] for n in gi if n.startswith('Tail')]
LEG=[gi[n] for n in gi if n.split('.')[0] in ('Thigh','Shin','Foot','Toe','UpperArm','Forearm','Hand')]
for v in me.vertices:
    x,y,z=v.co
    kill=[]
    if y>0.33 and z<0.42: kill=[g.group for g in v.groups if g.group not in T]          # hanging tail: tail bones only
    elif z<0.33:
        kill=list(T)                                                                     # legs never follow the tail
        if y<-0.1: kill+= R if x>0.02 else L                                             # front legs: strict split
        elif y>0.0: kill+= R if x>-0.01 else L                                           # hind legs
    for g in v.groups:
        if g.group in kill and g.weight>0: g.weight=0; fixed+=1
    tot=sum(g.weight for g in v.groups)
    if tot<1e-6: empty+=1
    else:
        for g in v.groups: g.weight/=tot
print("side fixes",fixed,"verts with no weight",empty)
# the tail hangs against the rump: weight it procedurally along the tail chain
tails=['Tail1','Tail2','Tail3','Tail4','Tail5']
seg=[(V3(B[n][0]),V3(B[n][1])) for n in tails]
def near_seg(p):
    best=None
    for i,(h,t) in enumerate(seg):
        d=t-h; u=max(0,min(1,(p-h).dot(d)/d.length_squared)); q=h+d*u; dist=(p-q).length
        if best is None or dist<best[0]: best=(dist,i,u)
    return best
ntail=0
for v in me.vertices:
    p=V3(v.co)
    dist,i,u=near_seg(p)
    if not (p.y>0.305 and p.z<0.46 and dist<0.05): continue
    w={tails[i]:1.0}
    if u>0.75 and i<4: a_=(u-0.75)/0.5; w={tails[i]:1-a_, tails[i+1]:a_}
    elif u<0.25 and i>0: a_=(0.25-u)/0.5; w={tails[i]:1-a_, tails[i-1]:a_}
    for g in v.groups: g.weight=0
    for n,x in w.items(): mesh.vertex_groups[n].add([v.index],x,'REPLACE')
    ntail+=1
print("tail verts",ntail)
# fill unweighted verts from the nearest weighted neighbour
from mathutils.kdtree import KDTree
ok=[v for v in me.vertices if sum(g.weight for g in v.groups)>1e-6]
kd=KDTree(len(ok))
for j,v in enumerate(ok): kd.insert(v.co,j)
kd.balance()
filled=0
for v in me.vertices:
    if sum(g.weight for g in v.groups)>1e-6: continue
    _,j,_=kd.find(v.co); src=ok[j]
    for g in src.groups:
        if g.weight>0: mesh.vertex_groups[g.group].add([v.index],g.weight,'REPLACE')
    filled+=1
print("filled",filled)
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/rig.blend")
# cut the faces that glue the hanging tail to the rump (AI mesh fused them where they touched)
import bmesh
bm=bmesh.new(); bm.from_mesh(me)
dl=bm.verts.layers.deform.verify()
Tset={mesh.vertex_groups[n].index for n in tails}
def twb(v): return sum(w for g,w in v[dl].items() if g in Tset)
# the rump never follows the hanging tail
for v in bm.verts:
    if v.co.y<0.305 and v.co.z<0.44:
        d=v[dl]
        for g in list(d.keys()):
            if g in Tset: del d[g]
        tot=sum(d.values())
        if tot>1e-6:
            for g in list(d.keys()): d[g]/=tot
        else: d[bpy.data.objects["Zaira"].vertex_groups['Hips'].index]=1.0
kill=[f for f in bm.faces if f.calc_center_median().z<0.44 and max(twb(v) for v in f.verts)-min(twb(v) for v in f.verts)>0.6]
# detach instead of deleting: the glue faces get their own tail-weighted copies of the rump vertices
uv=bm.loops.layers.uv.active
made=0
for f in kill:
    best=max(f.verts,key=twb)
    wbest=dict(best[dl])
    newverts=[]; uvs=[l[uv].uv.copy() for l in f.loops] if uv else None
    for v in f.verts:
        if twb(v)<0.5:
            nv=bm.verts.new(v.co); nv.normal=v.normal
            d=nv[dl]
            for g,w in wbest.items(): d[g]=w
            newverts.append(nv); made+=1
        else: newverts.append(v)
    nf=bm.faces.new(newverts); nf.material_index=f.material_index; nf.smooth=f.smooth
    if uv:
        for l,u in zip(nf.loops,uvs): l[uv].uv=u
bmesh.ops.delete(bm,geom=kill,context='FACES_ONLY')
print("detached verts",made)
bm.to_mesh(me); bm.free(); print("cut faces",len(kill))
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/rig.blend")
