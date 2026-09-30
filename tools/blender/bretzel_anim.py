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
        self.length = 1.0         # the middle of the back (Spine) stretched out (> 1) or gathered up (< 1)

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
            d = sub(q, h)
            if n == 'Spine' and self.length != 1.0:
                a = REST[n]['a']; u = (math.cos(a), math.sin(a))
                # the Spine's own points stretch with it; everything beyond it (the chest, which undoes the
                # scale, and all it carries) just moves along by the extra length
                along = d[0] * u[0] + d[1] * u[1]
                extra = (self.length - 1) * (min(along, REST[n]['L']) if name == 'Spine' else REST[n]['L'])
                d = (d[0] + u[0] * extra, d[1] + u[1] * extra)
            q = add(rot2(D(self.x.get(n, 0.0)), d), h)
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
        # the stretched back: Spine longer along its length (local Y), Chest undoes it so the chest, the head and
        # the front legs keep their size; every other bone keys scale 1, or a clip would keep the last one's
        for pb in P:
            k = self.length if pb.name == 'Spine' else 1 / self.length if pb.name == 'Chest' else 1.0
            pb.scale = (1.0, k, 1.0)
            pb.keyframe_insert("scale", frame=frame)
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
    """Twitching nose: quick little up-downs, `rate` twitches over the clip (a whole number, or the loop jumps)."""
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
    # the nose twitches in bursts, fast, then rests a moment
    nose(p, t, rate=12, amp=9 * (0.35 + 0.65 * smooth(math.sin(TAU * t * 2) * 2)))   # 4 a second

def curve(t, keys):
    """A looping curve through (phase, value) keys, eased between them."""
    keys = sorted(keys)
    ext = [(k - 1, v) for k, v in keys] + keys + [(k + 1, v) for k, v in keys]
    for (a, va), (b, vb) in zip(ext, ext[1:]):
        if a <= t < b: return lerp(va, vb, smooth((t - a) / (b - a)))
    return keys[0][1]

def foot(t, land, duty, S, y_land, lift, heel_push, heel_swing, trail=0.0, trail_until=0.45):
    """A paw that lands at phase `land`, stays down for `duty` of the cycle sliding back under the body (the body
    goes on at S per cycle), then swings forward in an arc to land again at y_land. trail: a hind paw that has
    just pushed stays stretched out behind (m further back, for the first trail_until of the swing) before it
    swings forward under the belly. Returns (dy, dz, dmeta, down)."""
    k = (t - land) % 1.0
    if k < duty:
        u = k / duty
        # pushing off at the end of the stance: the heel comes up and the long foot rolls onto the toes
        return y_land + S * duty * u, 0.0, heel_push * smooth((u - 0.6) / 0.4), 0.06 < u < 0.94
    u = (k - duty) / (1 - duty)
    start = y_land + S * duty
    if trail:
        # out behind, then forward
        back = trail * math.sin(math.pi * min(1.0, u / trail_until) / 1.0) if u < trail_until else 0.0
        fwd = smooth((u - trail_until * 0.6) / (1 - trail_until * 0.6))
        y = lerp(start, y_land, fwd) + back
        z = lift * (0.55 * math.sin(math.pi * min(1.0, u / trail_until)) + math.sin(math.pi * u)) / 1.3
        m = heel_push + (heel_swing - heel_push) * smooth(u / trail_until) if u < trail_until else heel_swing * (1 - smooth((u - trail_until) / (1 - trail_until)))
        return y, z, m, False
    y = lerp(start, y_land, smooth(u))
    z = lift * math.sin(math.pi * u) ** 0.8
    return y, z, heel_push * (1 - smooth(u / 0.3)) + heel_swing * math.sin(math.pi * u), False

def ramp(t, keys):
    """A curve through (phase, value) keys over one cycle, eased between them, not looping (for progress)."""
    for (a, va), (b, vb) in zip(keys, keys[1:]):
        if a <= t <= b: return lerp(va, vb, smooth((t - a) / (b - a)) if b > a else 1.0)
    return keys[-1][1]

def spread(p, deg):
    """Hind legs out to the sides (they land either side of the front paws)."""
    p.yz['Thigh.L'] = (0.0, -deg)
    p.yz['Thigh.R'] = (0.0, deg)

HOP_S, HOP_FRAMES = 0.36, 15      # m per hop, 0.5 s: 0.72 m/s, about 80 px/s on screen
def hop(p, t, f):
    """The slow hop of a pet rabbit (no moment in the air): the front paws step forward one after the other and
    the back stretches a little; then both hind feet hop up together, the rump rising and the back bunching as
    they land under the belly, beside and just behind the front paws. The body goes forward mostly in the hop of
    the hind legs, not evenly: it surges, then waits for the front paws."""
    S = HOP_S
    # the body's progress over the hop (0..1): a little while the front paws reach, most of it in the hind hop
    prog = ramp(t, [(0.0, 0.0), (0.32, 0.18), (0.42, 0.26), (0.72, 0.94), (1.0, 1.0)])
    lift = bump(t, 0.40, 0.80)
    body(p, dy=-S * (prog - t), dz=0.05 * lift,
         pitch=curve(t, [(0.0, 1.0), (0.3, 3.0), (0.55, 9.0), (0.75, 4.0), (0.9, 0.0)]),
         spine=curve(t, [(0.0, 0.0), (0.3, -4.0), (0.62, 8.0), (0.8, 10.0), (1.0, 0.0)]),
         neck=curve(t, [(0.0, 0.0), (0.3, -5.0), (0.6, -4.0), (0.85, 2.0)]),
         head=curve(t, [(0.0, 0.0), (0.3, 3.0), (0.6, 2.0), (0.85, 0.0)]))
    p.length = curve(t, [(0.0, 1.0), (0.3, 1.12), (0.45, 1.14), (0.72, 0.9), (0.9, 0.96)])
    plans = []
    for key, land in (('FL', 0.20), ('FR', 0.30)):
        duty = 0.80
        y, z, m, down = foot(t, land, duty, S, -0.14, 0.05, 15, -20)
        plans.append((key, y, z, m, down))
    for key in ('HL', 'HR'):
        y, z, m, down = foot(t, 0.74, 0.62, S, -0.06, 0.07, 45, 15)
        plans.append((key, y, z, m, down))
    lower_to_reach(p, [q[:4] for q in plans if q[4]])
    for q in plans: plant(p, q[0], dy=q[1], dz=q[2], dmeta=q[3])
    spread(p, 6 * lift)
    ears(p, swing=curve(t, [(0.0, 0.0), (0.45, -3.0), (0.75, 5.0), (0.9, 1.0)]))
    nose(p, t, rate=2, amp=3)      # 4 a second

RUN_S, RUN_FRAMES = 1.30, 10      # the half-bound: 1.3 m per stride at 3 strides a second (3.9 m/s)
def run(p, t, f):
    """Running: the half-bound. t = 0 the hind feet land together, either side of and ahead of where the front
    paws were, the back curled up (gathered). They drive the body out (0-0.25): the back straightens and
    lengthens, launching it into a long stretched-out flight, front legs reaching ahead, hind legs trailing.
    The front paws land one just after the other (0.45, 0.52), the nose dips, and the back curls up again as the
    hind legs swing forward past the front paws, which lift before the hind feet come down."""
    S = RUN_S
    body(p,
         dz=curve(t, [(0.0, -0.02), (0.12, 0.0), (0.33, 0.19), (0.47, 0.04), (0.62, 0.0), (0.85, 0.06)]),
         pitch=curve(t, [(0.0, 12.0), (0.18, -4.0), (0.33, 0.0), (0.5, 12.0), (0.68, 14.0), (0.9, 13.0)]),
         spine=curve(t, [(0.0, 10.0), (0.2, 0.0), (0.35, -10.0), (0.5, -4.0), (0.8, 12.0)]),
         chest=curve(t, [(0.0, 2.0), (0.35, -6.0), (0.5, 0.0), (0.8, 2.0)]),
         neck=curve(t, [(0.0, -6.0), (0.33, -8.0), (0.5, -10.0), (0.75, -8.0)]),
         head=curve(t, [(0.0, 2.0), (0.33, 4.0), (0.5, 6.0), (0.75, 2.0)]))
    p.length = curve(t, [(0.0, 0.88), (0.2, 1.18), (0.36, 1.35), (0.52, 1.18), (0.72, 0.92), (0.9, 0.86)])
    plans = []
    for key, land in (('FL', 0.45), ('FR', 0.52)):
        y, z, m, down = foot(t, land, 0.24, S, -0.22, 0.14, 25, -40)
        plans.append((key, y, z, m, down))
    for key in ('HL', 'HR'):
        y, z, m, down = foot(t, 0.0, 0.26, S, -0.14, 0.12, 45, 10, trail=0.08, trail_until=0.35)
        plans.append((key, y, z, m, down))
    lower_to_reach(p, [q[:4] for q in plans if q[4]])
    for q in plans: plant(p, q[0], dy=q[1], dz=q[2], dmeta=q[3])
    spread(p, curve(t, [(0.0, 10.0), (0.25, 3.0), (0.6, 2.0), (0.85, 12.0)]))
    ears(p, swing=curve(t, [(0.0, 2.0), (0.33, -5.0), (0.5, 5.0), (0.8, 1.0)]))

def binky(p, t, f):
    """The happy leap: a quick crouch, the hind legs drive it up stretched out long, legs straight; at the top the
    rump flips sideways and the hind feet kick out to the side while the head turns the other way; it straightens
    out, the front paws come down first and it lands."""
    crouch = bump(t, 0.0, 0.26)
    push = bump(t, 0.18, 0.40)
    air = smooth((t - 0.24) / 0.12) * (1 - smooth((t - 0.74) / 0.12))
    height = math.sin(math.pi * max(0.0, min(1.0, (t - 0.24) / 0.58))) if 0.24 < t < 0.82 else 0.0
    twist = bump(t, 0.40, 0.76)
    land = bump(t, 0.78, 1.0)
    body(p, dz=-0.05 * crouch + 0.42 * height - 0.03 * land,
         pitch=8 * crouch - 14 * push + 6 * land + 4 * bump(t, 0.62, 0.84),
         spine=5 * crouch - 8 * air, chest=-4 * air, neck=-12 * air + 6 * crouch, head=4 * air)
    p.length = 1 + 0.16 * push + 0.06 * air
    # the twist: rump one way, head the other, the body rolling a little with it
    # spread along the whole back, or the skin folds where one bone does all the turning
    p.yz['Hips'] = (-3 * twist, -12 * twist)
    p.yz['Spine'] = (-2 * twist, -7 * twist)
    p.yz['Chest'] = (2 * twist, 6 * twist)
    p.yz['Neck'] = (0.0, 24 * twist)
    p.yz['Head'] = (0.0, 10 * twist)
    up = max(0.0, p.hips[1])
    # legs: straight and long in the air (front reaching down-forward, hind stretched down-back), then the kick
    kick = bump(t, 0.44, 0.72)
    for key in ('FL', 'FR'):
        plant(p, key, dy=-0.07 * air + 0.03 * crouch, dz=up * 0.9 - 0.05 * air, dmeta=-35 * air)
    for key in ('HL', 'HR'):
        # stretched down and back a little under the body (the long feet do it, the thigh stays near its place)
        plant(p, key, dy=0.10 * push + 0.04 * air - 0.03 * kick, dz=up - 0.05 * air + 0.05 * kick,
              dmeta=40 * max(push, air))
    for s_, sgn in (('L', 1), ('R', -1)):
        # both hind feet flick out to the same side (the rump's twist), one a little more than the other
        p.yz[f'Thigh.{s_}'] = (0.0, -(10 + 4 * sgn) * kick)
    ears(p, swing=12 * math.sin(TAU * t * 1.5) - 6 * air, out=6 * air)

def tuck_front(p, back=0.12):
    """Front paws folded in under the chest (the way a loafing rabbit hides them): the forearms lie back along
    the floor, the paws flat just in front of the knees."""
    for key in ('FL', 'FR'): plant(p, key, dy=back, dz=0.006, dmeta=25)

def loaf(p, t, f):
    """The loaf: the round body settled on the floor (the model already sits on its belly), the chest down onto the
    folded front paws, head drawn in, ears lying back, breathing."""
    br = math.sin(TAU * t * 2)
    body(p, dz=-0.004 + 0.003 * br, pitch=10, spine=3 + 0.8 * br, neck=1, head=2)
    for key in ('HL', 'HR'): plant(p, key)
    tuck_front(p)
    ears(p, swing=-6)
    nose(p, t, rate=9, amp=4)      # 3 a second, calm

def sleep(p, t, f):
    """Asleep: loafed and gone limp, leaning a little, the head sunk until the chin rests on the floor, ears
    back, slow deep breaths and the nose still."""
    br = math.sin(TAU * t * 2)
    p.roll = 7
    body(p, dz=-0.006 + 0.005 * br, pitch=11, spine=5 + 1.4 * br, neck=20, head=14)
    for key in ('HL', 'HR'): plant(p, key)
    tuck_front(p, back=0.11)
    ears(p, swing=-9)

def sit(p, t, f):
    """Alert: up on all four legs, front raised a little, head up, still; only the nose and the ears are busy."""
    br = math.sin(TAU * t * 2)
    body(p, dz=0.015, pitch=-5 + 0.3 * br, spine=-3, neck=-10, head=-4)
    settle_front(p)
    stand(p)
    ears(p, swing=-4 + 1.5 * math.sin(TAU * t), out=2)
    nose(p, t, rate=10, amp=9)     # 5 a second

def groom(p, t, f):
    """Washing the face, crouched (sitting up would show the belly, which the model has only rough and dark): the
    head bows and turns towards one front paw, the paw comes up to the cheek, is licked, and wipes down over the
    eye to the nose; then the other paw. Two wipes, one per paw."""
    half = 0 if t < 0.5 else 1
    u = (t * 2) % 1.0
    key, other, sgn = (('FL', 'FR', 1), ('FR', 'FL', -1))[half]
    up = bump(u, 0.05, 0.95)
    wipe = smooth((u - 0.45) / 0.35)                      # 0 at the cheek, 1 down at the nose
    lick = bump(u, 0.22, 0.45)
    body(p, dz=0.012 * up, pitch=-6 * up, spine=-2 * up, neck=16 + 12 * up + 6 * wipe, head=8 + 4 * lick)
    p.yz['Neck'] = (0.0, 10 * sgn * up)
    p.yz['Head'] = (0.0, 6 * sgn * up)
    for k in ('HL', 'HR'): plant(p, k)
    plant(p, other)
    mz = muzzle(p)
    # from the floor up to the cheek, a little lick, then down the face to the nose, and back to the floor
    cheek = (mz[0] + 0.10, mz[1] + 0.09 - 0.02 * lick)
    nosept = (mz[0] + 0.04, mz[1] - 0.03)
    tgt = (lerp(cheek[0], nosept[0], wipe), lerp(cheek[1], nosept[1], wipe))
    rest = PAW[key]                                        # its spot on the floor
    lift = smooth(u / 0.25) * (1 - smooth((u - 0.82) / 0.18))
    reach(p, key, (lerp(rest[0], tgt[0], lift), lerp(rest[1], tgt[1], lift)), dmeta=-80 * lift)
    ears(p, swing=3 * up - 2)
    p.x['Nose'] = 7 * lick * max(0.0, math.sin(TAU * u * 8))

def eat(p, t, f):
    chew = math.sin(TAU * t * 6)
    body(p, dz=-0.01, pitch=6, spine=4, neck=28 + 2 * chew, head=14)
    stand(p)
    p.x['Nose'] = 4 * max(0.0, chew)
    ears(p, swing=2 * chew)

def flop_pose(p, k, br=0.0):
    """Lying on its side (k = how far over, 0..1), stretched out long: hind legs out straight behind, front paws
    forward, the back lengthened, the head laid down."""
    p.roll = 85 * smooth(k)      # onto its right side, the back towards +X (the underside of the mesh is dark and rough)
    body(p, dz=-0.05 * smooth(k), pitch=0, spine=-6 * k + br, neck=6 * k, head=2 * k)
    p.length = 1 + 0.12 * smooth(k)
    # hind legs straight out behind, the feet (soles back) beyond the rump; front paws reaching forward
    for key in ('HL', 'HR'): plant(p, key, dy=0.40 * k, dz=0.07 * k, dmeta=150 * k)
    for key in ('FL', 'FR'): plant(p, key, dy=-0.10 * k, dz=0.04 * k, dmeta=-40 * k)
    ears(p, swing=-5 * k)

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
    """Dropping: all four legs straight down reaching for the floor, the back hollow, head up, ears flying up."""
    s = math.sin(TAU * t * 2)
    body(p, pitch=-6, spine=-8, neck=-14, head=-4)
    for key in ('HL', 'HR'): plant(p, key, dy=0.08, dz=-0.13, dmeta=60 + 4 * s)
    for key in ('FL', 'FR'): plant(p, key, dy=0.06, dz=-0.09, dmeta=-70)     # straight down under the shoulders
    ears(p, swing=14, out=9 + 2 * s)

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
    bake("Hop", HOP_FRAMES, hop)
    bake("Run", RUN_FRAMES, run)
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
