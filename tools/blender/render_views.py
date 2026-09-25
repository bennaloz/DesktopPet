import bpy, math, sys, mathutils
argv=sys.argv[sys.argv.index("--")+1:]
blend, outdir = argv[0], argv[1]
bpy.ops.wm.open_mainfile(filepath=blend)
sc=bpy.context.scene
sc.render.engine='BLENDER_WORKBENCH'
sc.display.shading.light='FLAT'; sc.display.shading.color_type='TEXTURE'
sc.display.shading.show_xray = ('xray' in argv)
sc.display.shading.xray_alpha = 0.5
sc.render.resolution_x=sc.render.resolution_y=1200
sc.render.film_transparent=False
S=1.2
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=S
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
# show armature bones in front if present
for o in sc.objects:
    if o.type=='ARMATURE': o.show_in_front=True; o.data.display_type='STICK'
views={'side':((3,0,0.37),(math.pi/2,0,math.pi/2)),   # looking -X: image right = -Y? 
       'top':((0,0,3),(0,0,0)),
       'front':((0,-3,0.37),(math.pi/2,0,0))}
for name,(loc,rot) in views.items():
    co.location=loc; co.rotation_euler=rot
    sc.render.filepath=f"{outdir}/{name}.png"
    bpy.ops.render.render(write_still=True)
    # print mapping: world point -> pixel
    from bpy_extras.object_utils import world_to_camera_view
    for p in [(0,0,0),(0,0.1,0),(0.1,0,0),(0,0,0.1)]:
        v=world_to_camera_view(sc,co,mathutils.Vector(p)); print("MAP",name,p,round(v.x*1200,1),round((1-v.y)*1200,1))
