import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
import bpy, numpy as np
bpy.ops.wm.open_mainfile(filepath=work_file("mesh.blend"))
V=np.array([v.co[:] for v in bpy.data.objects["Zaira"].data.vertices])
def clusters(P,k=4,it=30):
    rng=np.random.default_rng(0); C=P[rng.choice(len(P),k,replace=False)]
    for _ in range(it):
        L=((P[:,None,:]-C[None])**2).sum(-1).argmin(1)
        C=np.array([P[L==j].mean(0) if (L==j).any() else C[j] for j in range(k)])
    return C,L
for z in [0.02,0.08,0.15,0.22,0.28]:
    S=V[(V[:,2]>z-0.01)&(V[:,2]<z+0.01)]
    S=S[S[:,1]>-0.45] # skip head region
    S=S[~((S[:,1]>0.28))] if z>0.12 else S  # skip tail at mid heights
    C,L=clusters(S[:,:2])
    print("z",z, sorted([tuple(np.round(c,3)) for c in C], key=lambda c:(c[1],c[0])), [int((L==j).sum()) for j in range(4)])
print("--- torso profile (|x|<0.04): y, zmin, zmax")
for y in np.arange(-0.50,0.51,0.04):
    S=V[(abs(V[:,1]-y)<0.02)&(abs(V[:,0])<0.04)]
    if len(S): print(round(y,2), round(S[:,2].min(),3), round(S[:,2].max(),3), "xspan", round(V[(abs(V[:,1]-y)<0.02)][:,0].min(),3), round(V[(abs(V[:,1]-y)<0.02)][:,0].max(),3))
print("--- tail: verts y>0.3 by z band")
T=V[V[:,1]>0.3]
for z in np.arange(0.05,0.5,0.05):
    S=T[(abs(T[:,2]-z)<0.025)]
    if len(S): print(round(z,2), np.round(S.mean(0),3), len(S))
