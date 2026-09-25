"""Procedural animation library for the Zaira rig (Blender, head towards -Y, Z up).

Every bone's local X axis is world +X, so a rotation about local X is a bend in the side (YZ) plane.
Angles in that plane: alpha = atan2(dz, dy); a positive X rotation increases alpha.
Legs are posed with a planar two-bone IK so paws stay planted on the ground (no foot sliding).
"""
import bpy, math
from mathutils import Vector, Matrix

FPS = 30
D = math.radians

bpy.ops.wm.open_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/rig.blend")
scene = bpy.context.scene
scene.render.fps = FPS
arm = bpy.data.objects["Rig"]
bones = arm.data.bones
P = arm.pose.bones
for pb in P:
    # side-plane bend (X) first, then the sideways turn about the already bent bone (Z): intrinsic X then Z
    pb.rotation_mode = 'ZYX'

# ---------------------------------------------------------------- rest data in the side plane
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

class Pose:
    """Side-plane forward kinematics for a pose: rotations (deg) per bone about X, extra Y/Z, hips offset."""
    def __init__(self):
        self.x = {}       # bone -> degrees about local X (side-plane bend)
        self.yz = {}      # bone -> (deg about local Y, deg about local Z)
        self.hips = (0.0, 0.0)   # world (dy, dz) offset of the whole body

    def cum(self, name):
        a = 0.0
        while name:
            a += D(self.x.get(name, 0.0))
            name = REST[name]['parent']
        return a

    def point(self, name, p):
        """World position of rest point p carried by bone `name`."""
        chain = []
        n = name
        while n:
            chain.append(n); n = REST[n]['parent']
        q = p
        for n in chain:  # innermost first
            h = REST[n]['h']
            q = add(rot2(D(self.x.get(n, 0.0)), sub(q, h)), h)
        return add(q, self.hips)

    def leg(self, upper, lower, meta, toe, paw, meta_angle, knee_forward, toe_angle=None):
        """Place the paw (tip of `meta`) at `paw` with the metatarsus at world angle meta_angle (deg)."""
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
        # pick the bend by which side of the hip->paw line the joint sits on (stable even when the hip
        # is almost on the ground): hind knees bend forward (-Y), front elbows backward (+Y)
        a1 = K = None
        for s in (1, -1):
            c = base + s * k
            Kc = (H[0] + L1 * math.cos(c), H[1] + L1 * math.sin(c))
            side = v[0] * (Kc[1] - H[1]) - v[1] * (Kc[0] - H[0])
            if (side < 0) == knee_forward:
                a1, K = c, Kc
        if a1 is None:
            a1 = base; K = (H[0] + L1 * math.cos(a1), H[1] + L1 * math.sin(a1))
        a2 = math.atan2(A[1] - K[1], A[0] - K[0])
        t1 = a1 - REST[upper]['a'] - phi
        t2 = a2 - REST[lower]['a'] - (phi + t1)
        t3 = g - REST[meta]['a'] - (phi + t1 + t2)
        self.x[upper], self.x[lower], self.x[meta] = map(math.degrees, (t1, t2, t3))
        if toe:
            ta = REST[toe]['a'] if toe_angle is None else D(toe_angle)
            self.x[toe] = math.degrees(ta - REST[toe]['a'] - (phi + t1 + t2 + t3))

    def apply(self, frame):
        for pb in P:
            x = (self.x.get(pb.name, 0.0) + 180.0) % 360.0 - 180.0
            y, z = self.yz.get(pb.name, (0.0, 0.0))
            pb.rotation_euler = (D(x), D(y), D(z))
            pb.keyframe_insert("rotation_euler", frame=frame)
        # hips offset: world delta -> hips bone local space
        R = bones['Hips'].matrix_local.to_3x3()
        pb = P['Hips']
        pb.location = R.inverted() @ Vector((0, self.hips[0], self.hips[1]))
        pb.keyframe_insert("location", frame=frame)

# rest paw (toe-base) positions and metatarsus angles
LEGS = {
    'HL': ('Thigh.L', 'Shin.L', 'Foot.L', 'Toe.L', True),
    'HR': ('Thigh.R', 'Shin.R', 'Foot.R', 'Toe.R', True),
    'FL': ('UpperArm.L', 'Forearm.L', 'Hand.L', None, False),
    'FR': ('UpperArm.R', 'Forearm.R', 'Hand.R', None, False),
}
PAW = {k: REST[v[2]]['t'] for k, v in LEGS.items()}
META = {k: math.degrees(REST[v[2]]['a']) for k, v in LEGS.items()}

def plant(p, key, dy=0.0, dz=0.0, dmeta=0.0, toe=None):
    u, l, m, t, fwd = LEGS[key]
    paw = (PAW[key][0] + dy, PAW[key][1] + dz)
    p.leg(u, l, m, t, paw, META[key] + dmeta, fwd, toe)

def plant_under_shoulder(p, key, dmeta=0.0, ahead=0.0):
    """Front leg straight down: the paw goes right under the shoulder, wherever the body pose put it."""
    u, l, m, t, fwd = LEGS[key]
    par = REST[u]['parent']
    H = p.point(par, REST[u]['h'])
    p.leg(u, l, m, t, (H[0] - ahead, PAW[key][1]), META[key] + dmeta, fwd, None)

def fold_hind(p, key, back=0.02, hock_z=0.03):
    """Sitting hind leg: hock just behind the hip on the ground, foot flat forward under the thigh, so the leg
    folds completely and the thigh rests on the floor with the knee forward."""
    u, l, m, t, fwd = LEGS[key]
    H = p.point(REST[u]['parent'], REST[u]['h'])
    L3 = REST[m]['L']
    hock = (H[0] + back, hock_z)
    p.leg(u, l, m, t, (hock[0] - L3, PAW[key][1]), 180, fwd, 180)

def stand(p):
    for k in LEGS: plant(p, k)

def tail(p, lift=0.0, sway=0.0, t=0.0, curl=0.0):
    """Gentle tail: lift (deg, up) spread over the chain, sway (deg, sideways) as a wave."""
    for i, n in enumerate(['Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5']):
        w = [0.45, 0.25, 0.15, 0.1, 0.05][i]
        p.x[n] = p.x.get(n, 0.0) + lift * w + curl * (i / 4)
        p.yz[n] = (0.0, sway * (0.4 + 0.3 * i) * math.sin(t - i * 0.6))

def chain_world(p, names, alphas, lateral=None):
    """Point each bone of a chain at a world side-plane angle (deg); optional sideways curl per bone."""
    for i, n in enumerate(names):
        par = REST[n]['parent']
        phi = p.cum(par) if par else 0.0
        p.x[n] = alphas[i] - math.degrees(REST[n]['a'] + phi)
        if lateral: p.yz[n] = (0.0, lateral[i])

TAILS = ['Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5']

def new_action(name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    return act

def bake(name, frames, fn, loop=True):
    new_action(name)
    for f in range(frames + (1 if loop else 0)):
        t = f / frames if loop else f / (frames - 1)   # one-shot clips end exactly on their last pose
        p = Pose()
        fn(p, t, f)
        p.apply(f)
    print("action", name, frames, flush=True)

def smooth(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
def lerp(a, b, t): return a + (b - a) * t
TAU = 2 * math.pi

# ---------------------------------------------------------------- actions
def idle(p, t, f):
    s = math.sin(TAU * t)
    p.hips = (0.0, 0.002 * s)
    p.x['Chest'] = 1.2 * s
    p.x['Neck'] = -1.2 * s
    p.yz['Head'] = (0.0, 8 * math.sin(TAU * t + 1.0))
    stand(p)
    tail(p, lift=4, sway=8, t=TAU * t)

def idle_look(p, t, f):
    stand(p)
    look = 28 * (smooth(t / 0.25) - smooth((t - 0.45) / 0.2)) - 22 * (smooth((t - 0.6) / 0.15) - smooth((t - 0.85) / 0.15))
    p.yz['Neck'] = (0.0, look * 0.4)
    p.yz['Head'] = (0.0, look * 0.6)
    p.x['Head'] = -6 * math.sin(math.pi * t)
    tail(p, lift=4, sway=6, t=TAU * t)

def gait(p, t, phases, duty, reach, lift, bob, spine_flex=0.0, meta_roll=25.0, swing_curl=40.0, centre=None):
    centre = centre or {}
    for key, ph in phases.items():
        u = (t - ph) % 1.0
        if u < duty:                       # stance: paw planted, sliding back under the body
            k = u / duty
            dy = lerp(-reach, reach, k)
            dz = 0.0
            dm = meta_roll * dy / reach
            toe = None
        else:                              # swing: lift and bring forward
            k = (u - duty) / (1 - duty)
            dy = lerp(reach, -reach, smooth(k))
            dz = lift * math.sin(math.pi * k)
            dm = meta_roll + swing_curl * math.sin(math.pi * k)
            toe = None
        plant(p, key, dy + centre.get(key, 0.0), dz, dm, toe)

WALK_REACH, WALK_HIND_SHIFT = 0.185, 0.10

def walk(p, t, f):
    """A cat's walk: lateral-sequence gait, head low and steady, shoulders and hips rolling, back weaving."""
    w = TAU * t
    # weight shifts: the body dips a little twice per stride, rolls towards the leg that carries it
    p.hips = (0.0, -0.012 + 0.005 * math.cos(2 * w))
    # only a light weave of the back: rolling the hips or shoulders would swing the planted legs sideways
    # (the leg IK works in the side plane only)
    p.yz['Spine'] = (0.0, -1.2 * math.sin(w + 1.0))
    # head low, nose level, steady: the neck soaks up the body's bob
    p.x['Neck'] = 16 - 1.5 * math.cos(2 * w)
    p.x['Head'] = -14 + 1.5 * math.cos(2 * w)
    p.yz['Neck'] = (0.0, 2.0 * math.sin(w + 2.4))
    # lateral sequence (LH, LF, RH, RF), with direct register: the hind paw lands in the print the front paw
    # of the same side left. Print spacing 0.535 = 0.75 * stride + hind stance shift: stride 0.58, shift 0.10.
    gait(p, t, {'HL': 0.0, 'FL': 0.25, 'HR': 0.5, 'FR': 0.75}, duty=0.64, reach=WALK_REACH, lift=0.035, bob=0.0,
         meta_roll=28, swing_curl=55, centre={'HL': -WALK_HIND_SHIFT, 'HR': -WALK_HIND_SHIFT})
    tail(p, lift=6, sway=9, t=w)

def run(p, t, f):
    c = math.cos(TAU * t)
    p.hips = (0.0, 0.025 * math.sin(TAU * t + 0.6))
    # the back flexes and extends with the stride
    # the back bends and stretches with every stride, a little less than in the sprint
    p.x['Hips'] = 9 * c
    p.x['Spine'] = -8 * c
    p.x['Chest'] = -6 * c
    p.x['Neck'] = 8 + 7 * c
    p.x['Head'] = -8
    # rotary gallop, as cats run: LH, RH, then RF, LF (the footfalls go round the body)
    gait(p, t, {'HL': 0.0, 'HR': 0.08, 'FR': 0.5, 'FL': 0.58}, duty=0.38, reach=0.18, lift=0.09, bob=0.02,
         meta_roll=30, swing_curl=55)
    tail(p, lift=10 + 4 * c, sway=5, t=TAU * t)

def sprint(p, t, f):
    """Zoomies: a flat-out rotary gallop. The back flexes hard and stretches out at every stride (that spring
    is where a cat's speed comes from), body low, head pushed forward and steady, tail straight back."""
    w = TAU * t
    c = math.cos(w)
    p.hips = (0.0, -0.03 + 0.035 * math.sin(w + 0.6))
    p.x['Hips'] = 12 * c
    p.x['Spine'] = -10 * c
    p.x['Chest'] = -8 * c
    p.x['Neck'] = 12 + 8 * c        # soaks up the back's swing: the head stays level
    p.x['Head'] = -10
    gait(p, t, {'HL': 0.0, 'HR': 0.1, 'FR': 0.45, 'FL': 0.55}, duty=0.32, reach=0.2, lift=0.11, bob=0.0,
         meta_roll=35, swing_curl=70)
    tail(p, lift=5 + 3 * c, sway=3, t=w)

# ---- jump: parametric key poses blended over time (a real cat's take-off, flight and landing)
STAND = dict(dz=0.0, pitch=0.0, spine=0.0, chest=0.0, neck=0.0, head=0.0, tail=4.0,
             legs={k: (0.0, 0.0, 0.0) for k in ('FL', 'FR', 'HL', 'HR')})
# loading: rump sinks on the hind legs, front stays up, head points at the target
LOAD = dict(dz=-0.12, pitch=-10.0, spine=3.0, chest=2.0, neck=10.0, head=-6.0, tail=-4.0,
            legs={'FL': (0.02, 0.0, 5.0), 'FR': (0.02, 0.0, 5.0), 'HL': (0.03, 0.0, -18.0), 'HR': (0.03, 0.0, -18.0)})
# push-off: hind legs straighten against the ground, front paws already folded up under the chest
PUSH = dict(dz=0.03, pitch=-16.0, spine=-3.0, chest=-2.0, neck=-2.0, head=2.0, tail=6.0,
            legs={'FL': (0.03, 0.15, 70.0), 'FR': (0.05, 0.14, 70.0), 'HL': (0.24, 0.02, 70.0), 'HR': (0.25, 0.02, 70.0)})
# flight: body long, front legs reaching forward, hind legs trailing, tail straight back
FLY = dict(dz=0.0, pitch=-5.0, spine=-4.0, chest=-3.0, neck=-6.0, head=4.0, tail=12.0,
           legs={'FL': (-0.25, 0.22, -75.0), 'FR': (-0.23, 0.24, -75.0), 'HL': (0.27, 0.13, 85.0), 'HR': (0.29, 0.14, 85.0)})
# coming down: nose down, front legs reaching for the ground, hind legs drawn in under the belly
DOWN = dict(dz=0.0, pitch=10.0, spine=2.0, chest=2.0, neck=4.0, head=-6.0, tail=10.0,
            legs={'FL': (-0.09, 0.02, -15.0), 'FR': (-0.07, 0.04, -15.0), 'HL': (-0.03, 0.17, 30.0), 'HR': (-0.01, 0.18, 30.0)})
# touch-down: chest dips as the front legs take the weight
ABSORB = dict(dz=-0.05, pitch=6.0, spine=2.0, chest=6.0, neck=-6.0, head=4.0, tail=6.0,
              legs={'FL': (0.0, 0.0, 0.0), 'FR': (0.0, 0.0, 0.0), 'HL': (0.0, 0.0, 0.0), 'HR': (0.0, 0.0, 0.0)})

def mix(a, b, t):
    out = {k: lerp(a[k], b[k], t) for k in a if k != 'legs'}
    out['legs'] = {k: tuple(lerp(x, y, t) for x, y in zip(a['legs'][k], b['legs'][k])) for k in a['legs']}
    return out

def pose_from(p, q):
    p.hips = (0.0, q['dz'])
    p.x['Hips'] = q['pitch']; p.x['Spine'] = q['spine']; p.x['Chest'] = q['chest']
    p.x['Neck'] = q['neck']; p.x['Head'] = q['head']
    for k, (dy, dz, dm) in q['legs'].items():
        plant(p, k, dy, dz, dm)
    tail(p, lift=q['tail'], sway=0, t=0)

def keys(t, seq):
    """seq: [(time, pose), ...] with times 0..1; smooth blend between neighbours."""
    for (t0, a), (t1, b) in zip(seq, seq[1:]):
        if t <= t1:
            return mix(a, b, smooth((t - t0) / (t1 - t0)))
    return seq[-1][1]

def prejump(p, t, f):
    pose_from(p, keys(t, [(0, STAND), (1, LOAD)]))

def jump(p, t, f):
    pose_from(p, keys(t, [(0, LOAD), (0.4, PUSH), (1, FLY)]))

def fall(p, t, f):
    pose_from(p, keys(t, [(0, FLY), (1, DOWN)]))

def land(p, t, f):
    pose_from(p, keys(t, [(0, DOWN), (0.3, ABSORB), (1, STAND)]))

def sit_pose(p, breathe=0.0, t=0.0):
    # body pitched up around the hips, rump on the ground, front legs straight, hind legs folded flat
    p.hips = (0.03, SIT_DROP)
    p.x['Hips'] = SIT_PITCH
    p.x['Spine'] = SIT_SPINE
    p.x['Chest'] = SIT_CHEST + breathe
    p.x['Neck'] = 20 - breathe
    p.x['Head'] = -(SIT_PITCH + SIT_SPINE + SIT_CHEST) - 12
    plant_under_shoulder(p, 'FL', 12, ahead=0.015)
    plant_under_shoulder(p, 'FR', 12, ahead=0.015)
    # hind: metatarsus flat on the ground pointing forward, hock behind
    fold_hind(p, 'HL', SIT_HOCK_BACK)
    fold_hind(p, 'HR', SIT_HOCK_BACK)
    # thighs turned a few degrees outwards, knees apart, as a cat sits
    p.yz['Thigh.L'] = (0.0, SIT_THIGH_OUT)
    p.yz['Thigh.R'] = (0.0, -SIT_THIGH_OUT)
    # tail drops to the ground and lies along it, curling round to the side
    chain_world(p, TAILS, SIT_TAIL, SIT_TAIL_CURL)

SIT_DROP, SIT_PITCH, SIT_SPINE, SIT_CHEST = -0.245, -30, -12, 4
SIT_HOCK_BACK = -0.04
SIT_THIGH_OUT = -10   # negative = knees outwards
SIT_TAIL_CURL = [0, 0, -35, -60, -55]
SIT_TAIL = [-66, -58, -4, 3, 3]

def sit(p, t, f):
    sit_pose(p, breathe=1.2 * math.sin(TAU * t), t=t)
    y, z = p.yz['Tail5']; p.yz['Tail5'] = (y, z + 12 * math.sin(TAU * 2 * t))

def loaf_pose(p, head_down=0.0, breathe=0.0):
    # lying on the belly, legs tucked under
    p.hips = (0.0, -0.20 + breathe * 0.004)
    p.x['Chest'] = breathe
    p.x['Neck'] = 8 + 30 * head_down
    p.x['Head'] = 10 + 25 * head_down
    p.yz['Head'] = (0.0, 25 * head_down)
    plant(p, 'FL', 0.02, 0.0, 128)
    plant(p, 'FR', 0.02, 0.0, 128)
    plant(p, 'HL', -0.14, 0.0, -80, toe=180)
    plant(p, 'HR', -0.14, 0.0, -80, toe=180)
    chain_world(p, TAILS, [-60, -35, -5, 0, 0], [0, 10, 25, 35, 35])

CROUCH_DROP = -0.25

def crouch(p, t, f):
    """Crouched: belly on the ground, forearms flat with the paws showing in front of the chest,
    hind legs folded alongside, head up."""
    breathe = math.sin(TAU * t)
    p.hips = (0.0, CROUCH_DROP + 0.003 * breathe)
    p.x['Spine'] = -3
    p.x['Chest'] = 2 + breathe
    p.x['Neck'] = 2
    p.x['Head'] = 0
    for key in ('FL', 'FR'):
        u, l, m, t_, fwd = LEGS[key]
        H = p.point(REST[u]['parent'], REST[u]['h'])
        p.leg(u, l, m, t_, (H[0] - CROUCH_REACH, 0.025), 180, False, None)
    plant(p, 'HL', -0.13, 0.0, -80, toe=180)
    plant(p, 'HR', -0.13, 0.0, -80, toe=180)
    chain_world(p, TAILS, [-70, -40, -5, 0, 0], [0, 15, 35, 45, 40])

CROUCH_REACH = 0.20
LOAF_DROP = -0.21

def loaf(p, t, f):
    """The 'loaf': belly on the ground, every paw hidden underneath, head sunk into the shoulders."""
    breathe = math.sin(TAU * t)
    p.hips = (0.0, LOAF_DROP + 0.003 * breathe)
    p.x['Spine'] = 3
    p.x['Chest'] = 4 + breathe
    p.x['Neck'] = 16
    p.x['Head'] = -20
    for key in ('FL', 'FR'):
        u, l, m, t_, fwd = LEGS[key]
        H = p.point(REST[u]['parent'], REST[u]['h'])
        # paws folded back inside the chest (wrist bent, paw pointing backwards)
        p.leg(u, l, m, t_, (H[0] + 0.03, 0.07), 5, False, None)
    # hind feet folded forward under the belly and drawn in towards the middle
    plant(p, 'HL', 0.02, 0.05, -80, toe=180)
    plant(p, 'HR', 0.02, 0.05, -80, toe=180)
    # tail lies on the ground along the flank
    chain_world(p, TAILS, [-60, -25, -3, 0, 0], [0, 0, 10, 15, 15])

def sleep(p, t, f):
    loaf_pose(p, head_down=1.0, breathe=math.sin(TAU * t))

EAT = dict(drop=-0.03, pitch=8, spine=4, chest=12, neck=48, head=32, paws_back=0.05)

def eat(p, t, f):
    """Head down in the bowl: chest lowered on slightly bent front legs, paws kept behind the bowl, chewing."""
    e = EAT
    p.hips = (0.0, e['drop'])
    p.x['Hips'] = e['pitch']
    p.x['Spine'] = e['spine']
    p.x['Chest'] = e['chest']
    p.x['Neck'] = e['neck']
    p.x['Head'] = e['head'] + 4 * math.sin(TAU * 3 * t)
    plant(p, 'FL', e['paws_back'], 0.0)
    plant(p, 'FR', e['paws_back'], 0.0)
    plant(p, 'HL'); plant(p, 'HR')
    tail(p, lift=4, sway=8, t=TAU * t)

def meow(p, t, f):
    a = math.sin(math.pi * t)
    stand(p)
    p.x['Neck'] = -14 * a
    p.x['Head'] = -22 * a
    tail(p, lift=4 + 6 * a, sway=0, t=0)

def held(p, t, f):
    s = math.sin(TAU * t)
    # dangling: legs relaxed and straightened, hanging a little forward and down
    plant(p, 'FL', 0.02, -0.03 + 0.01 * s, -10)
    plant(p, 'FR', 0.05, -0.02 - 0.01 * s, -10)
    plant(p, 'HL', 0.06, -0.05 - 0.01 * s, 25)
    plant(p, 'HR', 0.08, -0.04 + 0.01 * s, 25)
    for i, n in enumerate(['Tail1', 'Tail2', 'Tail3', 'Tail4', 'Tail5']):
        p.x[n] = [-15, -5, 0, 5, 5][i]
        p.yz[n] = (0.0, 6 * math.sin(TAU * t - i * 0.6))

bake("Idle", 90, idle)
bake("Idle_Look", 120, idle_look)
bake("Walk", 24, walk)
bake("Run", 14, run)
bake("Sprint", 14, sprint)
bake("Prejump", 13, prejump, loop=False)
bake("Jump", 10, jump, loop=False)
bake("Fall", 9, fall, loop=False)
bake("Land", 9, land, loop=False)
bake("Sit", 90, sit)
bake("Crouch", 90, crouch)
bake("Loaf", 90, loaf)
bake("Sleep", 120, sleep)
bake("Eat", 40, eat)
bake("Meow", 30, meow, loop=False)
bake("Held", 60, held)
arm.animation_data.action = bpy.data.actions["Idle"]
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
