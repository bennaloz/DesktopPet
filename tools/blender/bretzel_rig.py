import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import source_file, work_file
"""Bretzel: the Tripo rabbit with a rig of our own (Tripo's weights move the whole rump with the tail).
Blender, head towards -Y, Z up, feet on z=0; every bone's local X is world +X (rotation about X = bend in the
side plane), as in the Zaira rig, so the same planar kinematics drive it."""
import bpy, mathutils
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
mesh.name="Bretzel"
bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)
dec=mesh.modifiers.new("dec",'DECIMATE'); dec.ratio=16000/len(mesh.data.polygons)
bpy.ops.object.modifier_apply(modifier="dec")
print("polys after decimate", len(mesh.data.polygons))

B={ # name: (head, tail, parent, connected)
 'Hips':((0,0.34,0.30),(0,0.14,0.43),None,False),
 'Spine':((0,0.14,0.43),(0,-0.05,0.47),'Hips',True),
 'Chest':((0,-0.05,0.47),(0,-0.19,0.49),'Spine',True),
 'Neck':((0,-0.19,0.49),(0,-0.27,0.62),'Chest',True),
 'Head':((0,-0.27,0.62),(0,-0.45,0.58),'Neck',True),
 'Nose':((0,-0.43,0.58),(0,-0.49,0.555),'Head',False),
 'Ear1.L':((0.11,-0.26,0.71),(0.155,-0.24,0.61),'Head',False),
 'Ear2.L':((0.155,-0.24,0.61),(0.20,-0.225,0.52),'Ear1.L',True),
 'Ear3.L':((0.20,-0.225,0.52),(0.225,-0.225,0.44),'Ear2.L',True),
 'Ear4.L':((0.225,-0.225,0.44),(0.245,-0.23,0.36),'Ear3.L',True),
 'Tail':((0,0.42,0.13),(0,0.49,0.08),'Hips',False),
 'Thigh.L':((0.13,0.26,0.26),(0.17,0.10,0.18),'Hips',False),
 'Shin.L':((0.17,0.10,0.18),(0.175,0.21,0.04),'Thigh.L',True),
 'Foot.L':((0.175,0.21,0.04),(0.18,0.03,0.02),'Shin.L',True),
 'Toe.L':((0.18,0.03,0.02),(0.19,-0.08,0.015),'Foot.L',True),
 'UpperArm.L':((0.085,-0.19,0.37),(0.085,-0.17,0.18),'Chest',False),
 'Forearm.L':((0.085,-0.17,0.18),(0.085,-0.19,0.04),'UpperArm.L',True),
 'Hand.L':((0.085,-0.19,0.04),(0.085,-0.25,0.012),'Forearm.L',True),
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
EAR=groups(lambda n: n.startswith('Ear'))
LEG=groups(lambda n: n.split('.')[0] in ('Thigh','Shin','Foot','Toe','UpperArm','Forearm','Hand'))
TAIL={gi['Tail']}
def ear_outline(y,z):
    """The lop ear seen from the side: a flap from the crown down to below the jaw, wider in the middle."""
    if not 0.35<z<0.73: return False
    k=(z-0.35)/0.38                                   # 0 at the tip, 1 at the crown
    half=0.075*(1-abs(k-0.45)*1.1)+0.02               # half width along the body
    return abs(y-(-0.225-0.03*k))<half
# the ear lies over the cheek: at each spot of its outline only the outermost layer (the flap) is ear
import collections
outer=collections.defaultdict(float)
CELL=0.012
for v in me.vertices:
    x,y,z=v.co
    if ear_outline(y,z): key=(x>0,int(y/CELL),int(z/CELL)); outer[key]=max(outer[key],abs(x))
EAR_THICK=0.03
def in_ear(x,y,z):
    return ear_outline(y,z) and abs(x)>0.12 and abs(x)>outer[(x>0,int(y/CELL),int(z/CELL))]-EAR_THICK
def in_scut(x,y,z): return y>0.40 and z<0.22
fixed=0
for v in me.vertices:
    x,y,z=v.co
    if in_ear(x,y,z): kill={g.group for g in v.groups if g.group not in EAR}      # ear: ear bones only
    else:
        kill=set(EAR)                                                              # nothing else follows the ears
        if z<0.30: kill|=(R if x>0.0 else L)                                       # legs: strict left/right split
        if not in_scut(x,y,z): kill|=TAIL                                          # only the scut follows the tail
        else: kill|=LEG
    for g in v.groups:
        if g.group in kill and g.weight>0: g.weight=0; fixed+=1
    tot=sum(g.weight for g in v.groups)
    if tot>1e-6:
        for g in v.groups: g.weight/=tot
print("fixes",fixed)
# the rump behind the heel sits on the folded hind leg at rest, and the automatic weights hand it to the shin
# and the foot: a hind leg swinging forward then drags it out into a fin. Behind the heel it follows the hips.
def sstep(x,a,b):
    t=max(0.0,min(1.0,(x-a)/(b-a))); return t*t*(3-2*t)
LOWER=groups(lambda n: n.split('.')[0] in ('Shin','Foot','Toe'))
THIGH=groups(lambda n: n.startswith('Thigh'))
hips=gi['Hips']; moved=0
for v in me.vertices:
    x,y,z=v.co
    if z>0.30: continue
    k_low=1-sstep(y,0.22,0.32)          # shin and foot let go behind the heel
    k_thigh=1-sstep(y,0.34,0.44)        # the thigh a little further back
    lost=0.0
    for g in v.groups:
        k=k_low if g.group in LOWER else k_thigh if g.group in THIGH else 1.0
        if k<1.0 and g.weight>0: lost+=g.weight*(1-k); g.weight*=k
    if lost>0:
        mesh.vertex_groups['Hips'].add([v.index],lost,'ADD'); moved+=1
print("rump verts handed to the hips",moved)
# ears: weighted procedurally along their chain (the proxy fuses them to the cheeks)
for side in ('L','R'):
    names=[f'Ear{i}.{side}' for i in range(1,5)]
    seg=[(V3(B[n][0]),V3(B[n][1])) for n in names]
    for v in me.vertices:
        p=V3(v.co)
        if not in_ear(*p) or (p.x>0)!=(side=='L'): continue
        best=None
        for i,(h,t) in enumerate(seg):
            d=t-h; u=max(0,min(1,(p-h).dot(d)/d.length_squared)); dist=(p-(h+d*u)).length
            if best is None or dist<best[0]: best=(dist,i,u)
        _,i,u=best
        w={names[i]:1.0}
        if u>0.7 and i<3: a=(u-0.7)/0.6; w={names[i]:1-a, names[i+1]:a}
        elif u<0.3 and i>0: a=(0.3-u)/0.6; w={names[i]:1-a, names[i-1]:a}
        elif u<0.3 and i==0: a=(0.3-u)/0.6; w={names[0]:1-a, 'Head':a}
        # the lower ear lies against the neck: it follows the neck a little, or stretching the back opens a gap
        b=0.45*(1-max(0.0,min(1.0,(p.z-0.36)/0.22)))
        w={n:x*(1-b) for n,x in w.items()}
        if b>0: w['Neck']=w.get('Neck',0)+b
        for g in v.groups: g.weight=0
        for n,x in w.items(): mesh.vertex_groups[n].add([v.index],x,'REPLACE')
# soften every seam the rules above cut (ear edge against the neck, legs against the belly): hard steps in the
# weights tear the skin into shards when the body bends. By position, not along the edges: the mesh is split along
# its UV seams, and copies of one point smoothed apart would crack open.
def soften(radius=0.028, passes=3, keep=4):
    ng=len(mesh.vertex_groups)
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
print("weights softened")
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
# (the ears are the sides of the head in this mesh: there is no cheek under them, so they can only sway a
# little, never swing out, or the side of the face opens)
for img in bpy.data.images:
    if img.size[0]>2048: img.scale(2048,2048)
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
