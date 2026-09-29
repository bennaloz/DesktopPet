import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import work_file
"""Procedural animation of Bretzel (Blender, head towards -Y, Z up), baked into work/anim.blend.

Same idea as the Zaira library (anim.py): every bone's local X is world +X, so a rotation about X bends it in the
side (YZ) plane; legs are posed with a planar two-bone IK plus a foot at a given angle, so paws stay planted.
A rabbit's rest pose here is already standing on all fours, hunched, the long hind feet flat on the floor.

Gaits: a rabbit does not walk; it hops. Slow hop: the front paws reach forward one just after the other, then the
hind legs push together and swing forward under the body to land just behind them. Running, the hind feet land
ahead of the front ones and the back stretches out and gathers up like a spring.
"""
import bpy, math
from mathutils import Vector

FPS = 30
D = math.radians
TAU = 2 * math.pi

bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
scene = bpy.context.scene
scene.render.fps = FPS
arm = bpy.data.objects["Rig"]
mesh = bpy.data.objects["Bretzel"]
bones = arm.data.bones
P = arm.pose.bones
for pb in P: pb.rotation_mode = 'ZYX'

REST = {}
for b in bones:
    h, t = b.head_local, b.tail_local
    REST[b.name] = dict(h=(h.y, h.z), t=(t.y, t.z), a=math.atan2(t.z - h.z, t.y - h.y),
                        L=math.hypot(t.y - h.y, t.z - h.z), parent=b.parent.name if b.parent else None)

def rot2(a, p):
    c, s = math.cos(a), math.sin(a)
    return (c * p[0] - s * p[1], s * p[0] + c * p[1])
def add(p, q): return (p[0] + q[0], p[1] + q[1])
def sub(p, q): return (p[0] - q[0], p[1] - q[1])
def smooth(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
def lerp(a, b, t): return a + (b - a) * t
def bump(t, a, b):
    """0 outside [a, b], a smooth hump inside."""
    if t <= a or t >= b: return 0.0
    return math.sin(math.pi * (t - a) / (b - a))

class Pose:
    def __init__(self):
        self.x = {}               # bone -> degrees about local X (side-plane bend)
        self.yz = {}              # bone -> (deg about local Y, deg about local Z)
        self.hips = (0.0, 0.0)    # world (dy, dz) offset of the whole body
        self.roll = 0.0           # degrees the whole body rolls onto its side (flop)

    def cum(self, name):
        a = 0.0
        while name:
            a += D(self.x.get(name, 0.0)); name = REST[name]['parent']
        return a

    def point(self, name, p):
        chain = []; n = name
        while n: chain.append(n); n = REST[n]['parent']
        q = p
        for n in chain:
            h = REST[n]['h']
            q = add(rot2(D(self.x.get(n, 0.0)), sub(q, h)), h)
        return add(q, self.hips)

    def leg(self, upper, lower, meta, paw, meta_angle, knee_forward):
        """Put the tip of `meta` at `paw` with `meta` at world angle meta_angle (deg); two-bone IK above it."""
        par = REST[upper]['parent']
        phi = self.cum(par)
        H = self.point(par, REST[upper]['h'])
        L1, L2, L3 = REST[upper]['L'], REST[lower]['L'], REST[meta]['L']
        g = D(meta_angle)
        A = (paw[0] - L3 * math.cos(g), paw[1] - L3 * math.sin(g))
        v = sub(A, H); d = math.hypot(*v)
        d = max(abs(L1 - L2) + 1e-4, min(L1 + L2 - 1e-4, d))
        base = math.atan2(v[1], v[0])
        k = math.acos(max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d))))
        a1 = None
        for s in (1, -1):
            c = base + s * k
            K = (H[0] + L1 * math.cos(c), H[1] + L1 * math.sin(c))
            side = v[0] * (K[1] - H[1]) - v[1] * (K[0] - H[0])
            if (side < 0) == knee_forward: a1, KK = c, K
        if a1 is None: a1 = base; KK = (H[0] + L1 * math.cos(a1), H[1] + L1 * math.sin(a1))
        a2 = math.atan2(A[1] - KK[1], A[0] - KK[0])
        t1 = a1 - REST[upper]['a'] - phi
        t2 = a2 - REST[lower]['a'] - (phi + t1)
        t3 = g - REST[meta]['a'] - (phi + t1 + t2)
        self.x[upper], self.x[lower], self.x[meta] = map(math.degrees, (t1, t2, t3))
        return phi + t1 + t2 + t3

    def apply(self, frame):
        for pb in P:
            x = (self.x.get(pb.name, 0.0) + 180.0) % 360.0 - 180.0
            y, z = self.yz.get(pb.name, (0.0, 0.0))
            pb.rotation_euler = (D(x), D(y), D(z))
            pb.keyframe_insert("rotation_euler", frame=frame)
        # the whole body: offset in the side plane, and the roll onto its side, both on the root (Hips)
        R = bones['Hips'].matrix_local.to_3x3()
        pb = P['Hips']
        pb.location = R.inverted() @ Vector((0, self.hips[0], self.hips[1]))
        pb.keyframe_insert("location", frame=frame)
        if self.roll:
            # roll about the body's long axis (world Y) through the hips: extra rotation on the root bone
            axis = R.inverted() @ Vector((0, 1, 0))
            import mathutils
            q = mathutils.Quaternion(axis, D(self.roll))
            e = pb.rotation_euler.to_quaternion()
            pb.rotation_euler = (q @ e).to_euler('ZYX')
            pb.keyframe_insert("rotation_euler", frame=frame)

# ---------------------------------------------------------------- legs
LEGS = {  # key: (upper, lower, meta, toe, knee forward)
    'HL': ('Thigh.L', 'Shin.L', 'Foot.L', 'Toe.L', True),
    'HR': ('Thigh.R', 'Shin.R', 'Foot.R', 'Toe.R', True),
    'FL': ('UpperArm.L', 'Forearm.L', 'Hand.L', None, False),
    'FR': ('UpperArm.R', 'Forearm.R', 'Hand.R', None, False),
}
PAW = {k: REST[v[2]]['t'] for k, v in LEGS.items()}
META = {k: math.degrees(REST[v[2]]['a']) for k, v in LEGS.items()}
TOE = math.degrees(REST['Toe.L']['a'])

def plant(p, key, dy=0.0, dz=0.0, dmeta=0.0, toe=None):
    """A paw at its rest spot moved by (dy, dz); dmeta tilts the foot (+ = heel up); the toes stay flat unless told."""
    u, l, m, t, fwd = LEGS[key]
    paw = (PAW[key][0] + dy, PAW[key][1] + dz)
    ang = p.leg(u, l, m, paw, META[key] + dmeta, fwd)
    if t:   # the toes at a world angle (flat on the floor unless told), whatever the foot does
        p.x[t] = (TOE if toe is None else toe) - math.degrees(REST[t]['a'] + ang)

def reach(p, key, target, dmeta=0.0):
    """A front paw at a world point (after the body is posed), e.g. up at the muzzle."""
    u, l, m, t, fwd = LEGS[key]
    p.leg(u, l, m, target, META[key] + dmeta, fwd)

def shoulder(p, key): u = LEGS[key][0]; return p.point(REST[u]['parent'], REST[u]['h'])
def muzzle(p): return p.point('Head', REST['Head']['t'])

SHOULDER_REST = None
def settle_front(p, slack=0.0):
    """The front legs are straight at rest: if the body pose lifted the shoulders, lower the whole body so the
    front paws still reach the floor."""
    up = max(shoulder(p, k)[1] - REST[LEGS[k][0]]['h'][1] for k in ('FL', 'FR')) - slack
    if up > 0: p.hips = (p.hips[0], p.hips[1] - up)

def lower_to_reach(p, plans, margin=0.004):
    """Lower the whole body just enough that every planted paw in plans [(key, dy, dz, dmeta)] can be reached
    (a leg cannot stretch longer than its bones)."""
    drop = 0.0
    for key, dy, dz, dm in plans:
        u, l, m, t, fwd = LEGS[key]
        R = REST[u]['L'] + REST[l]['L'] - margin
        g = D(META[key] + dm)
        paw = (PAW[key][0] + dy, PAW[key][1] + dz)
        A = (paw[0] - REST[m]['L'] * math.cos(g), paw[1] - REST[m]['L'] * math.sin(g))
        H = shoulder(p, key)
        dx = H[0] - A[0]
        if abs(dx) >= R: continue
        drop = max(drop, (H[1] - A[1]) - math.sqrt(R * R - dx * dx))
    if drop > 0: p.hips = (p.hips[0], p.hips[1] - drop)

def stand(p):
    for k in LEGS: plant(p, k)

def body(p, dz=0.0, dy=0.0, pitch=0.0, spine=0.0, chest=0.0, neck=0.0, head=0.0):
    """The trunk: hips offset, pitch (+ = nose down), back bends (+ = arches the back up), neck and head (+ = down)."""
    p.hips = (dy, dz)
    p.x['Hips'] = pitch
    p.x['Spine'] = -spine
    p.x['Chest'] = -chest
    p.x['Neck'] = neck
    p.x['Head'] = head

EARS = [f'Ear{i}.{s}' for s in 'LR' for i in range(1, 5)]

def ears(p, swing=0.0, out=0.0, t=0.0, lag=0.35):
    """The lop ears: swing (deg, + = backwards) travelling down the ear a little late, and out (deg, away from
    the cheeks). Kept small: in this mesh the ears are the sides of the head."""
    for s, sgn in (('L', 1), ('R', -1)):
        for i in range(1, 5):
            w = [0.5, 0.3, 0.15, 0.05][i - 1]
            p.x[f'Ear{i}.{s}'] = p.x.get(f'Ear{i}.{s}', 0.0) + swing * w
            p.yz[f'Ear{i}.{s}'] = (0.0, -sgn * out * w)

def nose(p, t, rate=7.0, amp=4.0):
    """Twitching nose: quick little up-downs."""
    p.x['Nose'] = amp * max(0.0, math.sin(TAU * rate * t)) ** 3

# ---------------------------------------------------------------- baking
def new_action(name):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    return act

def lowest():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    z = min(v.co.z for v in me.vertices)
    ev.to_mesh_clear()
    return z

def bake(name, frames, fn, loop=True, ground=False):
    """ground: rest the body on the floor, frame by frame (lying poses, where no paw is planted to hold it)."""
    new_action(name)
    for f in range(frames + (1 if loop else 0)):
        t = f / frames if loop else f / (frames - 1)
        p = Pose(); fn(p, t, f); p.apply(f)
        if ground:
            scene.frame_set(f)
            z = lowest()
            p.hips = (p.hips[0], p.hips[1] - z); p.apply(f)
    print("action", name, frames, flush=True)

# ---------------------------------------------------------------- clips
def idle(p, t, f):
    br = math.sin(TAU * t * 2)
    body(p, dz=0.004 * br, spine=1.0 * br, neck=2 * math.sin(TAU * t), head=-1.5 * math.sin(TAU * t))
    settle_front(p)
    stand(p)
    ears(p, swing=3 * math.sin(TAU * t - 0.6))
    nose(p, t * 3)

HOP_S = 0.30         # m per hop (about a third of the body)
def hop_cycle(p, t, S, lift_h, lift_f, rise, pitch, flex, stretch, land_ahead=0.0):
    """One hop at phase t. Hind feet planted t in [0, 0.45), swinging after; front paws swinging t in [0, 0.3)
    (the left leading a little), planted after. S: ground covered per hop (m). land_ahead: how far past the
    rest spot the hind feet land (running, they land in front of the front paws)."""
    # hind: slide back while planted, then swing forward
    th = 0.45
    if t < th:
        k = t / th
        hy = lerp(-S * th / 2, S * th / 2, k) - land_ahead
        hz = 0.0
        heel = 35 * smooth((k - 0.4) / 0.6)      # the push: heels come up, the long feet roll onto the toes
    else:
        k = (t - th) / (1 - th)
        hy = lerp(S * th / 2, -S * th / 2, smooth(k)) - land_ahead
        hz = lift_h * math.sin(math.pi * k)
        heel = 35 * (1 - smooth(k / 0.4)) + 10 * bump(k, 0.3, 1.0)
    # trunk: the push lifts the rump and pitches the nose down; the swing gathers the back up (arched)
    push = bump(t, 0.15, 0.6)
    gather = bump(t, 0.5, 1.0)
    # (the rump stays up while the hind legs swing under it: nose still down, the back curling over them)
    tilt = pitch * (push + 0.8 * gather)
    body(p, dz=rise * (push + 0.9 * gather), pitch=tilt,
         spine=flex * gather - stretch * push, chest=-0.5 * stretch * push,
         neck=-0.7 * tilt + 3 * gather, head=0.2 * tilt)
    # front: swing forward, then planted and sliding back
    plans = []
    for key, lead in (('FL', 0.0), ('FR', 0.06)):
        tf = (t - lead) % 1.0
        tsw = 0.3
        if tf < tsw:
            k = tf / tsw
            fy = lerp(S * (1 - tsw) / 2, -S * (1 - tsw) / 2, smooth(k))
            fz = lift_f * math.sin(math.pi * k)
            fm = -30 * math.sin(math.pi * k)
            down = False
        else:
            k = (tf - tsw) / (1 - tsw)
            fy = lerp(-S * (1 - tsw) / 2, S * (1 - tsw) / 2, k)
            fz = 0.0; fm = 0.0
            down = 0.05 < k < 0.95     # (a paw about to lift or just landing may reach a little)
        plans.append((key, fy, fz, fm, down))
    plans += [(key, hy, hz, heel, t < th and 0.05 < t / th < 0.95) for key in ('HL', 'HR')]
    lower_to_reach(p, [q[:4] for q in plans if q[4]])
    for q in plans: plant(p, q[0], dy=q[1], dz=q[2], dmeta=q[3])
    return push, gather

def hop(p, t, f):
    push, gather = hop_cycle(p, t, HOP_S, lift_h=0.05, lift_f=0.035, rise=0.05, pitch=10, flex=10, stretch=6)
    ears(p, swing=6 * math.sin(TAU * t - 1.2), out=2 * push)
    nose(p, t * 2, amp=2)

RUN_S = 0.62
def run(p, t, f):
    push, gather = hop_cycle(p, t, RUN_S, lift_h=0.08, lift_f=0.06, rise=0.09, pitch=14, flex=18, stretch=14,
                             land_ahead=0.06)
    ears(p, swing=10 * math.sin(TAU * t - 1.4), out=3 * push)

def binky(p, t, f):
    """Crouch, leap up twisting the head one way and the rump the other, flick the hind feet, land."""
    crouch = smooth(t / 0.2) * (1 - smooth((t - 0.2) / 0.1))
    air = bump(t, 0.25, 0.8)
    land = bump(t, 0.78, 1.0)
    twist = math.sin(math.pi * min(1, max(0, (t - 0.28) / 0.5)))
    body(p, dz=-0.05 * crouch + 0.36 * air - 0.03 * land, pitch=6 * crouch - 12 * air * (1 - t) + 4 * land,
         spine=-6 * air + 4 * crouch, neck=-8 * air, head=4 * air)
    p.yz['Hips'] = (0.0, -22 * twist)
    p.yz['Neck'] = (0.0, 30 * twist)
    p.yz['Chest'] = (0.0, 8 * twist)
    # legs: gathered in the air, the hind feet flicked out and back at the top
    kick = bump(t, 0.4, 0.7)
    up = max(0.0, p.hips[1])
    for key in ('FL', 'FR'): plant(p, key, dy=-0.02 * air, dz=up * 0.85 + 0.02 * air, dmeta=-25 * air)
    for key in ('HL', 'HR'): plant(p, key, dy=0.10 * kick, dz=up * 0.8 + 0.05 * kick, dmeta=40 * air)
    for s, sgn in (('L', 1), ('R', -1)): p.yz[f'Thigh.{s}'] = (0.0, sgn * 18 * kick)
    ears(p, swing=10 * math.sin(TAU * t * 1.5), out=5 * air)

def loaf(p, t, f):
    """Hunkered down: belly on the floor, front paws tucked under the chest, ears relaxed, breathing."""
    br = math.sin(TAU * t * 2)
    body(p, dz=-0.04 + 0.004 * br, pitch=2, spine=2 + 1.0 * br, neck=6, head=4)
    for key in ('HL', 'HR'): plant(p, key)
    for key in ('FL', 'FR'): plant(p, key, dy=0.03)
    ears(p, swing=-2)
    nose(p, t * 2, amp=1.5)

def sleep(p, t, f):
    br = math.sin(TAU * t * 1)
    body(p, dz=-0.05 + 0.005 * br, pitch=4, spine=3 + 1.2 * br, neck=14, head=8)
    for key in ('HL', 'HR'): plant(p, key)
    for key in ('FL', 'FR'): plant(p, key, dy=0.03)
    ears(p, swing=-4)

def sit(p, t, f):
    """Sitting up on the haunches, front paws lifted off the floor, looking about."""
    br = math.sin(TAU * t * 2)
    body(p, dz=0.02, dy=0.03, pitch=-22 + 0.8 * br, spine=-8, chest=-4, neck=12 + 3 * math.sin(TAU * t), head=8)
    for key in ('HL', 'HR'): plant(p, key)
    for key in ('FL', 'FR'):
        sh = shoulder(p, key); reach(p, key, (sh[0] - 0.05, sh[1] - 0.26), dmeta=-30)
    ears(p, swing=2 * math.sin(TAU * t))
    nose(p, t * 3)

def groom(p, t, f):
    """Sitting up, washing the face: both front paws rub up the muzzle, the head dips into them."""
    rub = math.sin(TAU * t * 3)
    body(p, dz=0.02, dy=0.03, pitch=-24, spine=-8, chest=-4, neck=26 + 6 * rub, head=14 + 4 * rub)
    for key in ('HL', 'HR'): plant(p, key)
    mz = muzzle(p)
    for key, ph in (('FL', 0.0), ('FR', 0.5)):
        r = math.sin(TAU * (t * 3 + ph))
        reach(p, key, (mz[0] + 0.07, mz[1] - 0.10 + 0.05 * r), dmeta=-100)
    ears(p, swing=3 * rub)

def eat(p, t, f):
    chew = math.sin(TAU * t * 6)
    body(p, dz=-0.01, pitch=6, spine=4, neck=28 + 2 * chew, head=14)
    stand(p)
    p.x['Nose'] = 4 * max(0.0, chew)
    ears(p, swing=2 * chew)

def flop_pose(p, k, br=0.0):
    """Lying on its side (k = how far over, 0..1), the hind legs stretched out behind."""
    p.roll = 85 * smooth(k)      # onto its right side, the back towards +X (the underside of the mesh is dark and rough)
    body(p, dz=-0.05 * smooth(k), pitch=0, spine=-4 * k + br, neck=10 * k, head=4 * k)
    for key in ('HL', 'HR'): plant(p, key, dy=0.10 * k, dz=0.03 * k, dmeta=25 * k)
    for key in ('FL', 'FR'): plant(p, key, dy=-0.03 * k, dz=0.02 * k, dmeta=-20 * k)
    ears(p, swing=-4 * k)

def flop(p, t, f):
    # a moment gathering, then over it goes in a quarter of a second, with a little bounce
    k = smooth((t - 0.25) / 0.35)
    flop_pose(p, k + 0.06 * bump(t, 0.6, 0.85))

def flop_sleep(p, t, f):
    flop_pose(p, 1.0, br=1.2 * math.sin(TAU * t))

def thump(p, t, f):
    """Hind feet slammed down together: the rump jolts up and down, ears flick."""
    hit = bump(t, 0.3, 0.5)
    body(p, dz=0.03 * hit, pitch=-4 * hit, spine=-3 * hit, neck=-6)
    for key in ('HL', 'HR'): plant(p, key, dz=0.04 * hit, dmeta=-20 * hit)
    for key in ('FL', 'FR'): plant(p, key)
    ears(p, swing=8 * bump(t, 0.45, 0.8), out=4 * bump(t, 0.45, 0.8))

def held(p, t, f):
    """Held up: legs hang loosely, swaying a little."""
    s = math.sin(TAU * t)
    body(p, pitch=-6, spine=-4 + 2 * s, neck=-4)
    for key in ('HL', 'HR'): plant(p, key, dy=0.06, dz=-0.10 + 0.01 * s, dmeta=30)
    for key in ('FL', 'FR'): plant(p, key, dy=0.02, dz=-0.05 + 0.01 * s, dmeta=10)
    ears(p, swing=4 * s)

def fall(p, t, f):
    s = math.sin(TAU * t * 2)
    body(p, pitch=-4, spine=-6, neck=-8)
    for key in ('HL', 'HR'): plant(p, key, dy=0.05, dz=-0.06, dmeta=25 + 5 * s)
    for key in ('FL', 'FR'): plant(p, key, dy=-0.05, dz=-0.03, dmeta=-20)
    ears(p, swing=10, out=6 + 2 * s)

def land(p, t, f):
    a = bump(t, 0.0, 1.0)
    body(p, dz=-0.05 * a, pitch=5 * a, spine=5 * a, neck=6 * a)
    stand(p)
    ears(p, swing=-8 * a)

def petted(p, t, f):
    """Stroked: it flattens a little into the floor, head down, ears back, breathing slow."""
    br = math.sin(TAU * t)
    body(p, dz=-0.05 + 0.004 * br, pitch=4, spine=2 + br, neck=16, head=10)
    for key in ('HL', 'HR'): plant(p, key)
    for key in ('FL', 'FR'): plant(p, key, dy=0.03)
    ears(p, swing=-6)

if __name__ == "__main__":
    bake("Idle", 90, idle)
    bake("Hop", 16, hop)
    bake("Run", 12, run)
    bake("Binky", 26, binky, loop=False)
    bake("Loaf", 90, loaf)
    bake("Sleep", 120, sleep)
    bake("Sit", 60, sit)
    bake("Groom", 60, groom)
    bake("Eat", 30, eat)
    bake("Flop", 30, flop, loop=False, ground=True)
    bake("FlopSleep", 120, flop_sleep, ground=True)
    bake("Thump", 24, thump, loop=False)
    bake("Held", 40, held)
    bake("Fall", 20, fall)
    bake("Land", 12, land, loop=False)
    bake("Petted", 60, petted)
    arm.animation_data.action = bpy.data.actions["Idle"]
    bpy.ops.wm.save_as_mainfile(filepath=work_file("anim.blend"))
