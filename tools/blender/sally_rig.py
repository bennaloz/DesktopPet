import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__)))
os.environ["PET"]="sally"
from paths import source_file, work_file
"""The golden retriever: the Tripo model with the Zaira rig (same bones and names, so anim.py drives it too:
PET=sally blender -b --python anim.py). Blender, head towards -Y, Z up, feet on z=0, every bone's local X = world
+X. Also recolours the texture: the single-photo reconstruction left the parts the photo did not see (the back of
the tail and of the rump, the belly) grey; the fur is there, only without colour, so it gets the golden tones of
the rest of the coat at the same brightness."""
import bpy, mathutils, math, numpy as np
from mathutils import Vector as V3
from mathutils.kdtree import KDTree
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source_file("tripo.glb"))
mesh=[o for o in bpy.data.objects if o.type=='MESH' and len(o.data.vertices)>1000][0]
mw=mesh.matrix_world.copy(); mesh.parent=None; mesh.matrix_world=mw
for m in list(mesh.modifiers): mesh.modifiers.remove(m)
mesh.vertex_groups.clear()
for o in list(bpy.data.objects):
    if o!=mesh: bpy.data.objects.remove(o)       # Tripo's armature and a stray 2 m icosphere
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
mesh.data.transform(mesh.matrix_world); mesh.matrix_world=mathutils.Matrix.Identity(4)
# Tripo stood it along X, head at -X: turn it head towards -Y like the others, centred, feet on the floor
mesh.data.transform(mathutils.Matrix.Rotation(math.pi/2,4,'Z'))
V=np.array([v.co[:] for v in mesh.data.vertices])
mesh.data.transform(mathutils.Matrix.Translation((-(V[:,0].min()+V[:,0].max())/2, -(V[:,1].min()+V[:,1].max())/2, -V[:,2].min())))
mesh.name="Sally"
bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)
dec=mesh.modifiers.new("dec",'DECIMATE'); dec.ratio=20000/len(mesh.data.polygons)
bpy.ops.object.modifier_apply(modifier="dec")
print("polys after decimate", len(mesh.data.polygons))

# ---------------------------------------------------------------- texture: colour the grey fur
def recolour(img):
    if img.size[0]>2048: img.scale(2048,2048)
    w,h=img.size
    px=np.empty(w*h*4,np.float32); img.pixels.foreach_get(px); px=px.reshape(-1,4)
    rgb=px[:,:3]
    mx=rgb.max(1); mn=rgb.min(1); v=mx; s=np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
    # the coat's own colour at each brightness: median r,g,b ratios of the well-coloured fur, by brightness bin
    coloured=(s>0.28)&(v>0.25)&(rgb[:,0]>rgb[:,2])
    bins=np.clip((v*20).astype(int),0,19)
    lut=np.zeros((20,3),np.float32)
    for b in range(20):
        sel=coloured&(bins==b)
        if sel.sum()>200: lut[b]=np.median(rgb[sel]/np.maximum(v[sel,None],1e-6),axis=0)
    for b in range(20):   # fill empty bins from the nearest filled one
        if not lut[b].any():
            nz=[k for k in range(20) if lut[k].any()]; lut[b]=lut[min(nz,key=lambda k:abs(k-b))]
    # grey fur (little saturation, not the dark eyes and nose): the coat colour at the same brightness,
    # blending in over the edge of the grey patches
    wgt=(1-np.clip((s-0.13)/0.13,0,1))*np.clip((v-0.22)/0.1,0,1)
    tint=lut[bins]*v[:,None]
    rgb[:]=rgb*(1-wgt[:,None])+tint*wgt[:,None]
    img.pixels.foreach_set(px.ravel()); img.update()
    print("recoloured",img.name,"grey share %.2f"%float((wgt>0.5).mean()))
for img in bpy.data.images: recolour(img)

# ---------------------------------------------------------------- bones (the Zaira set)
B={ # name: (head, tail, parent, connected)
 'Hips':((0,0.30,0.44),(0,0.14,0.45),None,False),
 'Spine':((0,0.14,0.45),(0,-0.05,0.45),'Hips',True),
 'Chest':((0,-0.05,0.45),(0,-0.22,0.47),'Spine',True),
 'Neck':((0,-0.22,0.47),(0,-0.31,0.62),'Chest',True),
 'Head':((0,-0.31,0.62),(0,-0.47,0.60),'Neck',True),
 'Belly':((0,0.02,0.40),(0,0.02,0.25),'Spine',False),
 'Tail1':((0,0.28,0.47),(0,0.33,0.53),'Hips',False),
 'Tail2':((0,0.33,0.53),(0,0.37,0.59),'Tail1',True),
 'Tail3':((0,0.37,0.59),(0,0.40,0.66),'Tail2',True),
 'Tail4':((0,0.40,0.66),(0,0.425,0.72),'Tail3',True),
 'Tail5':((0,0.425,0.72),(0,0.435,0.79),'Tail4',True),
 'Scapula.L':((0.07,-0.17,0.53),(0.09,-0.25,0.42),'Chest',False),
 'UpperArm.L':((0.09,-0.25,0.42),(0.10,-0.20,0.28),'Scapula.L',True),
 'Forearm.L':((0.10,-0.20,0.28),(0.10,-0.235,0.09),'UpperArm.L',True),
 'Hand.L':((0.10,-0.235,0.09),(0.10,-0.25,0.03),'Forearm.L',True),
 'Finger.L':((0.10,-0.25,0.03),(0.10,-0.29,0.01),'Hand.L',True),
 'Thigh.L':((0.10,0.22,0.43),(0.13,0.17,0.28),'Hips',False),
 'Shin.L':((0.13,0.17,0.28),(0.15,0.30,0.14),'Thigh.L',True),
 'Foot.L':((0.15,0.30,0.14),(0.15,0.285,0.035),'Shin.L',True),
 'Toe.L':((0.15,0.285,0.035),(0.15,0.25,0.01),'Foot.L',True),
}
for n in [k for k in B if k.endswith('.L')]:
    h,t,p,c=B[n]
    B[n[:-2]+'.R']=((-h[0],h[1],h[2]),(-t[0],t[1],t[2]),p[:-2]+'.R' if p.endswith('.L') else p,c)

ad=bpy.data.armatures.new("Rig"); arm=bpy.data.objects.new("Rig",ad)
bpy.context.scene.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for n,(h,t,p,c) in B.items():
    eb=ad.edit_bones.new(n); eb.head=h; eb.tail=t
    d=(V3(t)-V3(h)).normalized()
    eb.align_roll(V3((1,0,0)).cross(d))     # local X = world +X
for n,(h,t,p,c) in B.items():
    if p: ad.edit_bones[n].parent=ad.edit_bones[p]; ad.edit_bones[n].use_connect=c
bpy.ops.object.mode_set(mode='OBJECT')

# automatic weights on a watertight voxel proxy, then transfer to the real mesh
proxy=mesh.copy(); proxy.data=mesh.data.copy(); proxy.name="Proxy"
bpy.context.scene.collection.objects.link(proxy)
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=proxy; proxy.select_set(True)
rm=proxy.modifiers.new("vox",'REMESH'); rm.mode='VOXEL'; rm.voxel_size=0.01
bpy.ops.object.modifier_apply(modifier="vox")
pd=proxy.modifiers.new("pd",'DECIMATE'); pd.ratio=min(1.0, 14000/len(proxy.data.polygons))
bpy.ops.object.modifier_apply(modifier="pd")
bpy.ops.object.select_all(action='DESELECT')
proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active=arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
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

me=mesh.data; gi={g.name:g.index for g in mesh.vertex_groups}
def groups(pred): return {gi[n] for n in gi if pred(n)}
L=groups(lambda n: n.endswith('.L')); R=groups(lambda n: n.endswith('.R'))
TAIL=groups(lambda n: n.startswith('Tail'))
LEGS=groups(lambda n: n.split('.')[0] in ('Thigh','Shin','Foot','Toe','UpperArm','Forearm','Hand','Finger'))
tails=[f'Tail{i}' for i in range(1,6)]
seg=[(V3(B[n][0]),V3(B[n][1])) for n in tails]
def near_tail(p):
    best=None
    for i,(h,t) in enumerate(seg):
        d=t-h; u=max(0,min(1,(p-h).dot(d)/d.length_squared)); dist=(p-(h+d*u)).length
        if best is None or dist<best[0]: best=(dist,i,u)
    return best
def in_tail(p):
    # the plume stands free above and behind the rump
    dist,i,u=near_tail(p)
    return (i>0 or u>0.3) and dist<0.09 and p.z>0.47 and p.y>0.27
fixed=0
for v in me.vertices:
    p=V3(v.co); x,y,z=v.co
    kill=set()
    if in_tail(p): kill={g.group for g in v.groups if g.group not in TAIL}      # the tail: tail bones only
    else:
        if not (y>0.24 and z>0.42): kill|=TAIL                                   # the body never follows the tail
        if z<0.32: kill|=(R if x>0.0 else L)                                     # legs: strict left/right split
    for g in v.groups:
        if g.group in kill and g.weight>0: g.weight=0; fixed+=1
    tot=sum(g.weight for g in v.groups)
    if tot>1e-6:
        for g in v.groups: g.weight/=tot
print("fixes",fixed)
# the tail along its chain, blending from bone to bone
for v in me.vertices:
    p=V3(v.co)
    if not in_tail(p): continue
    dist,i,u=near_tail(p)
    w={tails[i]:1.0}
    if u>0.7 and i<4: a=(u-0.7)/0.6; w={tails[i]:1-a, tails[i+1]:a}
    elif u<0.3 and i>0: a=(0.3-u)/0.6; w={tails[i]:1-a, tails[i-1]:a}
    for g in v.groups: g.weight=0
    for n,x in w.items(): mesh.vertex_groups[n].add([v.index],x,'REPLACE')

# soften every seam the rules above cut, by position (the mesh is split along its UV seams)
def soften(radius=0.028, passes=3, keep=4):
    W=[{g.group:g.weight for g in v.groups if g.weight>0} for v in me.vertices]
    kd=KDTree(len(me.vertices))
    for v in me.vertices: kd.insert(v.co,v.index)
    kd.balance()
    near=[[(i,1-d/radius) for _,i,d in kd.find_range(v.co,radius)] for v in me.vertices]
    for _ in range(passes):
        out=[]
        for vi in range(len(W)):
            acc={}; tot=0.0
            for i,w in near[vi]:
                tot+=w
                for g,x in W[i].items(): acc[g]=acc.get(g,0.0)+x*w
            out.append({g:x/tot for g,x in acc.items()} if tot>0 else W[vi])
        W=out
    for v,w in zip(me.vertices,W):
        top=sorted(w.items(),key=lambda kv:-kv[1])[:keep]
        s_=sum(x for _,x in top) or 1.0
        for g in list(v.groups): g.weight=0.0
        for g,x in top: mesh.vertex_groups[g].add([v.index],x/s_,'REPLACE')
soften()
# fill unweighted verts from the nearest weighted neighbour
ok=[v for v in me.vertices if sum(g.weight for g in v.groups)>1e-6]
kd=KDTree(len(ok))
for j,v in enumerate(ok): kd.insert(v.co,j)
kd.balance(); filled=0
for v in me.vertices:
    if sum(g.weight for g in v.groups)>1e-6: continue
    _,j,_=kd.find(v.co)
    for g in ok[j].groups:
        if g.weight>0: mesh.vertex_groups[g.group].add([v.index],g.weight,'REPLACE')
    filled+=1
print("filled",filled)
for img in bpy.data.images: img.pack()
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
