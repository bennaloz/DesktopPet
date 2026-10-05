import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import work_file
"""Frames of Bretzel's clips, side and three-quarter: blender -b --python bretzel_gifs.py -- <out> [names]
then makegifs.py <out> builds the GIFs."""
import bpy, math, json
argv=sys.argv[sys.argv.index("--")+1:]; out=argv[0]
ONLY=set(argv[1].split(",")) if len(argv)>1 else None
SETS=[('ferma',['Idle'],3),('saltelli',['Hop'],1),('corsa',['Run'],1),('binky',['Binky'],1),('pagnotta',['Loaf'],3),
      ('dorme',['Sleep'],4),('all_erta',['Sit'],2),('si_lava',['Groom'],1),('mangia',['Eat'],1),('flop',['Flop','FlopSleep'],2),
      ('thump',['Thump'],1),('in_braccio',['Held'],2),('cade',['Fall','Land'],1),('coccole',['Petted'],2)]
bpy.ops.wm.open_mainfile(filepath=work_file("anim.blend"))
sc=bpy.context.scene; arm=bpy.data.objects["Rig"]
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=360; sc.render.resolution_y=300; sc.render.film_transparent=True
cam=bpy.data.cameras.new("c"); cam.type='ORTHO'; cam.ortho_scale=1.6
co=bpy.data.objects.new("cam",cam); sc.collection.objects.link(co); sc.camera=co
views={'side':((3,0,0.45),(math.pi/2,0,math.pi/2)),'tq':((2.4,-1.4,0.9),(math.radians(78),0,math.radians(60)))}
index={}
for name,acts,step in [x for x in SETS if ONLY is None or x[0] in ONLY]:
    frames=[]
    for a in acts:
        fr=bpy.data.actions[a].frame_range
        frames+=[(a,f) for f in range(int(fr[0]),int(fr[1])+1,step)]
    index[name]=(len(frames),step)
    for v,(loc,rot) in views.items():
        co.location=loc; co.rotation_euler=rot
        for i,(a,f) in enumerate(frames):
            arm.animation_data.action=bpy.data.actions[a]; sc.frame_set(f)
            sc.render.filepath=f"{out}/_{name}_{v}_{i:03d}.png"; bpy.ops.render.render(write_still=True)
json.dump(index,open(f"{out}/_index.json","w"))
