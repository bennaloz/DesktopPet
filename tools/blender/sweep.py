import bpy, math
src=open(r"C:/develop/personal/_assets/zaira/work/anim.py",encoding='utf-8').read()
exec(src[:src.index('bake("Idle"')])
for drop in [-0.25,-0.27,-0.29,-0.30,-0.31,-0.315,-0.33]:
    globals()['LOAF_DROP']=drop
    p=Pose(); loaf(p,0.0,0)
    H=p.point('Hips', REST['Thigh.L']['h'])
    print("DROP",drop,"hip z",round(H[1],3),{k:round(p.x[k],1) for k in ('Thigh.L','Shin.L','Foot.L','Toe.L')})
