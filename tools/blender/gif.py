import bpy, sys, math
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]; act=argv[1]; n=int(argv[2])
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]; arm.animation_data.action=bpy.data.actions[act]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=360; sc.render.resolution_y=300
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.25
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
bpy.ops.mesh.primitive_plane_add(size=4,location=(0,0,-0.002))
fr=act and bpy.data.actions[act].frame_range
views={'side':((3,0,0.35),(math.pi/2,0,math.pi/2)),'tq':((2.4,-1.4,0.8),(math.radians(78),0,math.radians(60)))}
for v,(loc,rot) in views.items():
    co.location=loc; co.rotation_euler=rot
    for i in range(n):
        sc.frame_set(int(round(fr[0]+(fr[1]-fr[0])*i/n)))
        sc.render.filepath=f"{out}/_g_{v}_{i:03d}.png"; bpy.ops.render.render(write_still=True)
