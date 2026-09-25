import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]
blend,out=argv[0],argv[1]; view=argv[2] if len(argv)>2 else 'side'
bpy.ops.wm.open_mainfile(filepath=blend)
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=300
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.25
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
if view=='side': co.location=(3,0,0.4); co.rotation_euler=(math.pi/2,0,math.pi/2)
else: co.location=(2.2,-2.2,0.9); co.rotation_euler=(math.radians(75),0,math.radians(45))
# ground line
bpy.ops.mesh.primitive_plane_add(size=3,location=(0,0,-0.002))
import os
rows=[]
for act in bpy.data.actions:
    arm.animation_data.action=act
    fr=act.frame_range; n=6
    for i in range(n):
        f=int(fr[0]+(fr[1]-fr[0])*i/(n-1 if act.name in('Jump','Fall','Land','Meow') else n))
        sc.frame_set(f); sc.render.filepath=f"{out}/_f_{act.name}_{i}.png"; bpy.ops.render.render(write_still=True)
    rows.append((act.name,n))
open(f"{out}/_rows.txt","w").write("\n".join(f"{a},{n}" for a,n in rows))
