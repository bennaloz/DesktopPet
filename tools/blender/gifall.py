import bpy, sys, math, json
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]
ONLY=set(argv[1].split(",")) if len(argv)>1 else None
SETS=[('camminata',['Walk'],1),('trotto',['Trot'],1),('corsa',['Run'],1),('scatto',['Sprint'],1),('salto',['Prejump','Jump','Fall','Land'],1),('ferma',['Idle'],3),
      ('guarda',['Idle_Look'],4),('seduta',['Sit'],3),('dorme',['Sleep'],4),('mangia',['Eat'],1),('miagola',['Meow'],1),('in_braccio',['Held'],2)]
bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=360; sc.render.resolution_y=300; sc.render.film_transparent=True
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.25
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
views={'side':((3,0,0.35),(math.pi/2,0,math.pi/2)),'tq':((2.4,-1.4,0.8),(math.radians(78),0,math.radians(60)))}
index={}
# a taller frame for clips that reach higher (tail up)
FRAME={'trotto':(1.5,0.12)}
for name,acts,step in [x for x in SETS if ONLY is None or x[0] in ONLY]:
    frames=[]
    for a in acts:
        fr=bpy.data.actions[a].frame_range
        frames+= [(a,f) for f in range(int(fr[0]), int(fr[1])+1, step)]
    index[name]=(len(frames), step)
    scale,lift=FRAME.get(name,(1.25,0.0)); cam.ortho_scale=scale
    for v,(loc,rot) in views.items():
        co.location=(loc[0],loc[1],loc[2]+lift); co.rotation_euler=rot
        for i,(a,f) in enumerate(frames):
            arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(f)
            sc.render.filepath=f"{out}/_{name}_{v}_{i:03d}.png"; bpy.ops.render.render(write_still=True)
json.dump(index,open(f"{out}/_index.json","w"))
