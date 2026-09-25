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
    pb.rotation_mode = 'XYZ'

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
        cands = []
        for s in (1, -1):
            a1 = base + s * k
            K = (H[0] + L1 * math.cos(a1), H[1] + L1 * math.sin(a1))
            cands.append((K[0], a1, K))
        cands.sort()
        _, a1, K = cands[0] if knee_forward else cands[-1]
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
            x = self.x.get(pb.name, 0.0)
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
        t = f / frames
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

def gait(p, t, phases, duty, reach, lift, bob, spine_flex=0.0, meta_roll=25.0, swing_curl=40.0):
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
        plant(p, key, dy, dz, dm, toe)

def walk(p, t, f):
    p.hips = (0.0, 0.006 * math.cos(2 * TAU * t))
    p.yz['Spine'] = (0.0, 2.5 * math.sin(TAU * t))
    p.yz['Chest'] = (0.0, -2.5 * math.sin(TAU * t))
    p.x['Neck'] = 2 * math.cos(2 * TAU * t)
    gait(p, t, {'HL': 0.0, 'FL': 0.25, 'HR': 0.5, 'FR': 0.75}, duty=0.62, reach=0.14, lift=0.05, bob=0.006)
    tail(p, lift=8, sway=8, t=TAU * t)

def run(p, t, f):
    c = math.cos(TAU * t)
    p.hips = (0.0, 0.025 * math.sin(TAU * t + 0.6))
    # the back flexes and extends with the stride
    p.x['Hips'] = 6 * c
    p.x['Spine'] = -5 * c
    p.x['Chest'] = -4 * c
    p.x['Neck'] = 6 * c
    gait(p, t, {'HL': 0.0, 'HR': 0.08, 'FL': 0.5, 'FR': 0.58}, duty=0.38, reach=0.18, lift=0.09, bob=0.02,
         meta_roll=30, swing_curl=55)
    tail(p, lift=10 + 4 * c, sway=5, t=TAU * t)

def jump(p, t, f):
    # crouch (t<0.3) then stretch out: hind legs push back, front legs reach forward
    c = smooth(t / 0.3) * (1 - smooth((t - 0.3) / 0.25))
    e = smooth((t - 0.3) / 0.35)
    p.hips = (0.0, -0.06 * c)
    p.x['Hips'] = -12 * e
    p.x['Neck'] = 10 * e
    plant(p, 'HL', 0.18 * e, 0.04 * e + 0.0, 55 * e)
    plant(p, 'HR', 0.19 * e, 0.05 * e, 55 * e)
    plant(p, 'FL', -0.16 * e, 0.16 * e, -40 * e)
    plant(p, 'FR', -0.14 * e, 0.18 * e, -40 * e)
    tail(p, lift=-10 * e + 5, sway=0, t=0)

def fall(p, t, f):
    e = smooth(t / 0.6)
    p.x['Hips'] = 8 * e
    p.x['Neck'] = -8 * e
    plant(p, 'HL', -0.06 * e, 0.07 * e, 20 * e)
    plant(p, 'HR', -0.05 * e, 0.09 * e, 20 * e)
    plant(p, 'FL', -0.10 * e, 0.05 * e, -30 * e)
    plant(p, 'FR', -0.08 * e, 0.07 * e, -30 * e)
    tail(p, lift=12 * e, sway=0, t=0)

def land(p, t, f):
    c = math.sin(math.pi * min(1, t / 0.8))
    p.hips = (0.0, -0.07 * c)
    p.x['Neck'] = -8 * c
    stand(p)
    tail(p, lift=10, sway=0, t=0)

def sit_pose(p, breathe=0.0, t=0.0):
    # body pitched up around the hips, rump on the ground, front legs straight, hind legs folded flat
    p.hips = (0.03, -0.24)
    p.x['Hips'] = -38
    p.x['Spine'] = -8
    p.x['Chest'] = -4 + breathe
    p.x['Neck'] = 26 - breathe
    p.x['Head'] = 22
    plant(p, 'FL', 0.07, 0.0, 10)
    plant(p, 'FR', 0.07, 0.0, 10)
    # hind: metatarsus flat on the ground pointing forward, hock behind
    plant(p, 'HL', -0.10, 0.0, -75, toe=180)
    plant(p, 'HR', -0.10, 0.0, -75, toe=180)
    # tail drops to the ground and lies along it, curling round to the side
    chain_world(p, TAILS, [-80, -70, -35, -2, 0], [0, 0, 20, 35, 35])

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

def loaf(p, t, f):
    loaf_pose(p, head_down=0.0, breathe=math.sin(TAU * t))

def sleep(p, t, f):
    loaf_pose(p, head_down=1.0, breathe=math.sin(TAU * t))

def eat(p, t, f):
    p.hips = (0.0, -0.01)
    p.x['Chest'] = 10
    p.x['Neck'] = 38
    p.x['Head'] = 28 + 5 * math.sin(TAU * 3 * t)
    stand(p)
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
bake("Jump", 12, jump, loop=False)
bake("Fall", 10, fall, loop=False)
bake("Land", 12, land, loop=False)
bake("Sit", 90, sit)
bake("Loaf", 90, loaf)
bake("Sleep", 120, sleep)
bake("Eat", 40, eat)
bake("Meow", 30, meow, loop=False)
bake("Held", 60, held)
arm.animation_data.action = bpy.data.actions["Idle"]
bpy.ops.wm.save_as_mainfile(filepath=r"C:/develop/personal/_assets/zaira/work/anim.blend")
