import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; acts=argv[1].split(','); tag=argv[2] if len(argv)>2 else ''
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=480; sc.render.resolution_y=360
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.0
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
# the app: horizontal orthographic camera, cat turned 65 degrees from facing the viewer (three-quarter)
a=math.radians(65)
co.location=(3*math.sin(a), -3*math.cos(a), 0.3); co.rotation_euler=(math.pi/2, 0, a)
# floor line like the taskbar edge
bpy.ops.mesh.primitive_plane_add(size=4, location=(0,0,-0.001))
for act in acts:
    arm.animation_data.action=bpy.data.actions[act]; sc.frame_set(0)
    sc.render.filepath=f"{out}/_app_{act}{tag}.png"; bpy.ops.render.render(write_still=True)
