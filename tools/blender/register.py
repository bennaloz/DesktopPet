# Check direct register numerically: ground positions of front and hind footfalls, same side.
import bpy, math
src=open(r"C:/develop/personal/_assets/zaira/work/anim.py",encoding='utf-8').read()
exec(src[:src.index('bake("Idle"')])
T=1.0; S=2*WALK_REACH/0.64
def touchdown(key, ph):
    # stance starts at u=0: body-frame y = PAW - reach + centre; body has moved -S*ph
    c = -WALK_HIND_SHIFT if key.startswith('H') else 0.0
    return PAW[key][0] - WALK_REACH + c - S*ph
fl=touchdown('FL',0.25); hl=touchdown('HL',1.0)
print("REGISTER front print",round(fl,3),"hind lands",round(hl,3),"gap",round(hl-fl,3),"stride",round(S,3))
