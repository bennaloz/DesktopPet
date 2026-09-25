import bpy, math, mathutils, numpy as np
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"C:/develop/personal/_assets/zaira/domestic+cat+3d+model.glb")
mesh=[o for o in bpy.data.objects if o.type=='MESH' and len(o.data.vertices)>1000][0]
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
mw=mesh.matrix_world.copy()
mesh.parent=None; mesh.matrix_world=mw
for m in list(mesh.modifiers): mesh.modifiers.remove(m)
mesh.vertex_groups.clear()
for o in list(bpy.data.objects):
    if o!=mesh: bpy.data.objects.remove(o)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
mesh.data.transform(mesh.matrix_world); mesh.matrix_world=mathutils.Matrix.Identity(4)
V=np.array([v.co[:] for v in mesh.data.vertices])
# PCA on the torso (upper-middle band, excludes legs)
z0,z1=V[:,2].min(),V[:,2].max()
t=V[(V[:,2]>z0+0.45*(z1-z0))&(V[:,2]<z0+0.8*(z1-z0))][:,:2]
c=t.mean(0); u,s,vt=np.linalg.svd(t-c); ax=vt[0]
ang=math.atan2(ax[1],ax[0]); print("axis angle deg",math.degrees(ang))
# rotate so the axis lies on Y
R=mathutils.Matrix.Rotation(math.pi/2-ang,4,'Z')
mesh.data.transform(R)
V=np.array([v.co[:] for v in mesh.data.vertices])
# head is the high end: compare max z near each end
lo=V[V[:,1]<np.percentile(V[:,1],15)]; hi=V[V[:,1]>np.percentile(V[:,1],85)]
print("end -Y maxz",lo[:,2].max(),"end +Y maxz",hi[:,2].max())
if hi[:,2].max()>lo[:,2].max():
    mesh.data.transform(mathutils.Matrix.Rotation(math.pi,4,'Z'))
V=np.array([v.co[:] for v in mesh.data.vertices])
off=mathutils.Vector((-(V[:,0].min()+V[:,0].max())/2, -(V[:,1].min()+V[:,1].max())/2, -V[:,2].min()))
mesh.data.transform(mathutils.Matrix.Translation(off))
V=np.array([v.co[:] for v in mesh.data.vertices])
print("bbox",V.min(0),V.max(0))
mesh.name="Zaira"
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/mesh.blend")
