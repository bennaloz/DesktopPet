import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import source_file, work_file
"""Bretzel: the Tripo rabbit (standing on all fours, legs clear of the body) with a rig of our own.
Blender, head towards -Y, Z up, feet on z=0; every bone's local X is world +X (rotation about X = bend in the
side plane), as in the Zaira rig, so the same planar kinematics drive it. The mesh comes straightened by
bretzel_source; its legs are not in step (left paws ahead of the right ones), so each leg's bones follow it, from
hip and shoulder joints shared by both sides and with the same bone lengths left and right. Once skinned, the right
legs are posed onto the left ones' places and that pose becomes the rest: both sides start alike."""
import bpy, mathutils, math
from mathutils import Vector as V3
from mathutils.kdtree import KDTree
from bretzel_source import load
mesh=load(source_file("tripo.glb"))
mesh.name="Bretzel"
bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)

def joint(a, c, l1, l2, forward):
    """The middle joint of a two-bone limb from a (y,z) to c (y,z): knee forward (-y) or elbow back (+y)."""
    dy,dz=c[0]-a[0],c[1]-a[1]; d=math.hypot(dy,dz); d=min(d,l1+l2-1e-4)
    k=(l1*l1-l2*l2+d*d)/(2*d); h=math.sqrt(max(0.0,l1*l1-k*k))
    uy,uz=dy/d,dz/d; py,pz=a[0]+uy*k,a[1]+uz*k
    ny,nz=(uz,-uy)
    if (ny<0)!=forward: ny,nz=-ny,-nz
    return (py+ny*h,pz+nz*h)
def along(a, b, l):
    """From a towards b, l long."""
    dy,dz=b[0]-a[0],b[1]-a[1]; d=math.hypot(dy,dz); return (a[0]+dy/d*l,a[1]+dz/d*l)

HIP,THIGH,SHIN,FOOT=(0.27,0.33),0.17,0.17,0.142
SHOULDER,UPPER,FORE=(-0.22,0.34),0.155,0.16
# measured on the mesh (y, z): the hock and the ball of each hind foot, the toe tips; the wrists and the paw tips
HIND={'L':dict(x=(0.11,0.12,0.12,0.134,0.134),hock=(0.265,0.09),ball=(0.14,0.022),toe=(0.09,0.012)),
      'R':dict(x=(-0.09,-0.08,-0.065,-0.11,-0.11),hock=(0.365,0.095),ball=(0.26,0.022),toe=(0.215,0.010))}
FRONT={'L':dict(x=(0.07,0.08,0.073,0.07),wrist=(-0.19,0.055),tip=(-0.275,0.012)),
       'R':dict(x=(-0.075,-0.085,-0.08,-0.088),wrist=(-0.145,0.055),tip=(-0.225,0.012))}
def p3(x,yz): return (x,yz[0],yz[1])
B={ # name: (head, tail, parent, connected)
 'Hips':((0,0.36,0.37),(0,0.14,0.45),None,False),
 'Spine':((0,0.14,0.45),(0,-0.06,0.465),'Hips',True),
 'Chest':((0,-0.06,0.465),(0,-0.19,0.47),'Spine',True),
 'Neck':((0,-0.19,0.47),(0,-0.27,0.60),'Chest',True),        # up into the skull: the crown goes with the head
 'Head':((0,-0.27,0.60),(0,-0.46,0.55),'Neck',True),
 'Nose':((0.035,-0.43,0.52),(0.035,-0.488,0.505),'Head',False),   # the nostrils (the head is turned a little)
 'Tail':((0.03,0.40,0.29),(0.05,0.47,0.27),'Hips',False),
}
for s,sg in (('L',1),('R',-1)):
    e=[(0.118,-0.28,0.66),(0.142,-0.275,0.58),(0.16,-0.27,0.50),(0.17,-0.265,0.43),(0.175,-0.26,0.365)]
    dy=0.0 if s=='L' else -0.06                   # the head is turned a little: the right ear hangs further forward
    for i in range(4):
        h,t=(e[i][0],e[i][1]+dy,e[i][2]),(e[i+1][0],e[i+1][1]+dy,e[i+1][2])
        B[f'Ear{i+1}.{s}']=((sg*h[0],h[1],h[2]),(sg*t[0],t[1],t[2]),'Head' if i==0 else f'Ear{i}.{s}',i>0)
    m=HIND[s]; x=m['x']
    hock=m['hock']; ball=along(hock,m['ball'],FOOT); knee=joint(HIP,hock,THIGH,SHIN,True)
    B[f'Thigh.{s}']=(p3(x[0],HIP),p3(x[1],knee),'Hips',False)
    B[f'Shin.{s}']=(p3(x[1],knee),p3(x[2],hock),f'Thigh.{s}',True)
    B[f'Foot.{s}']=(p3(x[2],hock),p3(x[3],ball),f'Shin.{s}',True)
    B[f'Toe.{s}']=(p3(x[3],ball),p3(x[4],m['toe']),f'Foot.{s}',True)
    m=FRONT[s]; x=m['x']
    elbow=joint(SHOULDER,m['wrist'],UPPER,FORE,False)
    B[f'UpperArm.{s}']=(p3(x[0],SHOULDER),p3(x[1],elbow),'Chest',False)
    B[f'Forearm.{s}']=(p3(x[1],elbow),p3(x[2],m['wrist']),f'UpperArm.{s}',True)
    B[f'Hand.{s}']=(p3(x[2],m['wrist']),p3(x[3],m['tip']),f'Forearm.{s}',True)
for n,(h,t,p,c) in B.items(): print(f"bone {n:11s} {tuple(round(v,3) for v in h)} -> {tuple(round(v,3) for v in t)}")

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
EAR_Y={'L':-0.265,'R':-0.33}     # the head is turned a little: the right ear hangs 6 cm further forward
def ear_outline(y,z,side):
    """The lop ear seen from the side: a flap from the crown down to the jaw, narrower at the tip."""
    if not 0.35<z<0.70: return False
    t=max(0.0,min(1.0,(z-0.35)/0.11)); half=0.035+0.035*t*t*(3-2*t)
    return abs(y-EAR_Y[side])<half
# the ear hangs over the cheek and the neck, a flap with air between. A ray from outside in along x through a spot
# of its outline meets the flap's outer side (facing out), its inner side (facing in, towards the cheek), then the
# cheek (facing out again): the ear is everything down to that inner side. Only the lower half of each ear hangs
# free like that: higher up it is fused to the side of the head, and there its outermost layer is ear. Low down,
# where no surface faces in, there is no ear (the tip hangs in front of the shoulder, which the outline also covers).
from mathutils.bvhtree import BVHTree
bvh=BVHTree.FromPolygons([v.co.copy() for v in me.vertices],[tuple(p.vertices) for p in me.polygons])
def ear_depth(sg,y,z):
    """How far in (|x|) the ear reaches along the ray at (y, z) on side sg, or None where there is no ear."""
    o=V3((sg*0.5,y,z)); d=V3((-sg,0,0)); hits=[]
    while len(hits)<12:
        loc,nor,idx,dist=bvh.ray_cast(o,d,1.0)
        if loc is None or sg*loc.x<0.05: break
        hits.append((sg*loc.x,sg*nor.x)); o=loc+d*1e-4
    if not hits: return None
    for ax,nx in hits:
        if nx<-0.2: return ax if hits[0][0]-ax<0.07 else None
    return hits[0][0]-0.03 if z>0.46 else None
EARC={}
def in_ear(x,y,z):
    if not (ear_outline(y,z,'L' if x>0 else 'R') and abs(x)>0.11): return False
    k=(x>0,round(y,4),round(z,4))
    if k not in EARC: EARC[k]=ear_depth(1 if x>0 else -1,y,z)
    return EARC[k] is not None and abs(x)>=EARC[k]-0.004
def in_scut(x,y,z): return y>0.415 and 0.19<z<0.42     # (below it, the right heel reaches as far back)
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
        # the lower ear hangs by the neck: it follows the neck a little, or bending the neck pushes it through
        b=0.3*(1-max(0.0,min(1.0,(p.z-0.36)/0.22)))
        w={n:x*(1-b) for n,x in w.items()}
        if b>0: w['Neck']=w.get('Neck',0)+b
        for g in v.groups: g.weight=0
        for n,x in w.items(): mesh.vertex_groups[n].add([v.index],x,'REPLACE')
# soften every seam the rules above cut (ear edge against the neck, legs against the belly): hard steps in the
# weights tear the skin into shards when the body bends. By position, not along the edges: the mesh is split along
# its UV seams, and copies of one point smoothed apart would crack open.
EARV=[in_ear(*v.co) for v in me.vertices]
def soften(radius=0.028, passes=3, keep=4, only=None):
    ng=len(mesh.vertex_groups)
    W=[{g.group:g.weight for g in v.groups if g.weight>0} for v in me.vertices]
    kd=KDTree(len(me.vertices))
    for v in me.vertices: kd.insert(v.co,v.index)
    kd.balance()
    # the ear flaps hang clear of the cheeks: ear and head skin never average across the gap
    near=[[(i,1-d/radius) for _,i,d in kd.find_range(v.co,radius) if EARV[i]==EARV[v.index]] for v in me.vertices]
    for _ in range(passes):
        out=[]
        for vi in range(len(W)):
            if only and not only[vi]: out.append(W[vi]); continue
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
# the back half again, wider: the haunch has to stretch from the flank, not fold against it
soften(radius=0.055, passes=2, only=[v.co.y>-0.02 and not in_ear(*v.co) for v in me.vertices])
# and the shoulders, where the upper arm meets the side of the chest
soften(radius=0.045, passes=2, only=[-0.26<v.co.y<=-0.02 and v.co.z<0.42 and not in_ear(*v.co) for v in me.vertices])
print("weights softened")
def sstep(x,a,b):
    t=max(0.0,min(1.0,(x-a)/(b-a))); return t*t*(3-2*t)
def regroup(v, w):
    for g in v.groups: g.weight=0.0
    for n,x in w.items():
        if x>1e-4: mesh.vertex_groups[n].add([v.index],x,'REPLACE')
# the front legs come out from under the chest at the elbow: above it the skin is the chest's and the shoulder's,
# or reaching forward the forearm drags a web of chest skin down with it
for v in me.vertices:
    x,y,z=v.co
    if y>-0.02 or z<0.17 or in_ear(x,y,z): continue
    f=sstep(z,0.17,0.23); w={mesh.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0}
    for s_ in 'LR':
        moved=0.0
        for n in (f'Forearm.{s_}',f'Hand.{s_}'):
            if n in w: moved+=w[n]*f; w[n]*=1-f
        if moved: w[f'UpperArm.{s_}']=w.get(f'UpperArm.{s_}',0.0)+moved
    regroup(v,w)
# the nose: only the nostrils over the mouth twitch (the bone heat spread it over the whole muzzle and chin)
NOSE=V3((0.035,-0.478,0.505))
for v in me.vertices:
    w={mesh.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0}
    k=(1-sstep((V3(v.co)-NOSE).length,0.015,0.045))*sstep(v.co.z,0.486,0.496)
    if k<=0 and 'Nose' not in w: continue
    rest=w.pop('Nose',0.0)
    if k>0:
        tot=sum(w.values()) or 1.0
        w={n:x/tot*(1-k) for n,x in w.items()} if w else {'Head':1-k}
        w['Nose']=k
    elif rest: w['Head']=w.get('Head',0.0)+rest
    regroup(v,w)
print("elbows and nose regrouped")
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
# the right legs onto the left ones' places (in the side plane; the bones turn about X), then that pose becomes the
# rest pose, the mesh with it: both sides start alike, and no pose has to drag one leg half a stride further
def side_angle(n): h,t=B[n][0],B[n][1]; return math.atan2(t[2]-h[2],t[1]-h[1])
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
for chain in (('Thigh','Shin','Foot','Toe'),('UpperArm','Forearm','Hand')):
    done=0.0
    for n in chain:
        turn=side_angle(n+'.L')-side_angle(n+'.R')
        pb=arm.pose.bones[n+'.R']; pb.rotation_mode='XYZ'; pb.rotation_euler=(turn-done,0,0); done=turn
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=mesh; mesh.select_set(True)
bpy.ops.object.modifier_apply(modifier="Armature")
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')
am=mesh.modifiers.new("Armature",'ARMATURE'); am.object=arm
for b in arm.data.bones:
    if b.name.endswith('.R') and b.name.split('.')[0] in ('Thigh','Shin','Foot','Toe','UpperArm','Forearm','Hand'):
        L=arm.data.bones[b.name[:-2]+'.L']
        print(f"rest {b.name:11s} y {b.head_local.y:+.3f} z {b.head_local.z:+.3f} -> y {b.tail_local.y:+.3f} z {b.tail_local.z:+.3f}"
              f"   (L y {L.head_local.y:+.3f} z {L.head_local.z:+.3f} -> y {L.tail_local.y:+.3f} z {L.tail_local.z:+.3f})")
for img in bpy.data.images:
    if img.size[0]>2048: img.scale(2048,2048)
bpy.ops.wm.save_as_mainfile(filepath=work_file("rig.blend"))
