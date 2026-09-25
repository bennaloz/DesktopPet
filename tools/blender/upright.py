# Render the end of the Jump clip side-on, the whole rig pitched nose-up as the app does in a steep leap.
import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; pitch=float(argv[1]) if len(argv)>1 else 82
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=sc.render.resolution_y=420
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.6
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
co.location=(3,0,0.4); co.rotation_euler=(math.pi/2,0,math.pi/2)
act=bpy.data.actions["Jump"]; arm.animation_data.action=act
sc.frame_set(int(act.frame_range[1]))
from mathutils import Matrix
arm.matrix_world = Matrix.Rotation(math.radians(-pitch), 4, 'X') @ arm.matrix_world   # head at -Y: nose up
bpy.context.view_layer.update()
from mathutils import Vector
dg=bpy.context.evaluated_depsgraph_get()
pts=[]
for o in sc.objects:
    if o.type=='MESH' and o.parent==arm:
        e=o.evaluated_get(dg); pts+=[e.matrix_world@v.co for v in e.data.vertices]
ys=[p.y for p in pts]; zs=[p.z for p in pts]
co.location=(3,(min(ys)+max(ys))/2,(min(zs)+max(zs))/2); cam.ortho_scale=max(max(ys)-min(ys),max(zs)-min(zs))*1.15
print('SIZE', round(max(ys)-min(ys),3), round(max(zs)-min(zs),3))
sc.render.filepath=out; bpy.ops.render.render(write_still=True)
