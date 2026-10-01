import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from bretzel_paths import work_file
"""Procedural animation of Bretzel (Blender, head towards -Y, Z up), baked into work/anim.blend.

Same idea as the Zaira library (anim.py): every bone's local X is world +X, so a rotation about X bends it in the
side (YZ) plane; legs are posed with a planar two-bone IK plus a foot at a given angle, so paws stay planted.
The rest pose of this mesh is standing up on all fours, legs straight under it, the belly well clear of the
floor: every resting pose lets the body down onto bending legs (lower()).

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
        self.hay = 0.0            # how much of the blades of hay out of the mouth is still out (Eat; 0 = none)
        self.belly = 0.0          # the underside drawn up into the body (m): a stretched-out body's belly hangs

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
            pb.scale = (self.hay ** 0.5, self.hay, self.hay ** 0.5) if pb.name == 'Hay' else (1.0, k, 1.0)
            pb.keyframe_insert("scale", frame=frame)
        # the belly drawn up, in the body's own frame (it rides on the Spine)
        pb = P['Belly']
        pb.location = bones['Belly'].matrix_local.to_3x3().inverted() @ Vector((0, 0, self.belly))
        pb.keyframe_insert("location", frame=frame)
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
    """The trunk: hips offset, pitch (+ = nose down), back bends (+ = the front comes up and the back hollows,
    - = the back humps up into a ball and the front goes down), neck and head (+ = down)."""
    p.hips = (dy, dz)
    p.x['Hips'] = pitch
    p.x['Spine'] = -spine
    p.x['Chest'] = -chest
    p.x['Neck'] = neck
    p.x['Head'] = head

SHOULDER_REACH = REST['Hips']['h'][0] - REST['UpperArm.L']['h'][0]     # the shoulders this far ahead of the root
HEEL_DOWN = 180 + META['HL']            # how far the hind feet tip back to lie flat on the floor (heel down)

TUCK = 18.0      # how far the rump rolls under when the hips come all the way down to the crouch (deg)

def lower(p, rear, front, pitch=0.0, **kw):
    """The body let down onto bending legs: the hips `rear` and the shoulders `front` metres lower than standing
    (negative: higher), pitched to match, extra pitch on top; the rest as body().
    The lower the hips come, the more the rump rolls under (TUCK) and the back humps up to keep the shoulders and
    the head nearly where they were: a crouched rabbit is one round ball. Bending the thighs forward under a level rump
    instead folds the flank into a crease, a dog's tucked-up belly."""
    tilt = math.degrees(math.asin(max(-1.0, min(1.0, (front - rear) / SHOULDER_REACH))))
    body(p, dz=-rear, pitch=tilt + pitch, **kw)
    tuck = TUCK * max(0.0, min(1.2, rear / HUNCH_REAR))
    if tuck <= 0: return
    sh, head = shoulder(p, 'FL')[1], p.cum('Head')
    p.x['Hips'] -= tuck
    for _ in range(6):     # hump the back until the shoulders are back at their height
        p.x['Spine'] += math.degrees((shoulder(p, 'FL')[1] - sh) / 0.36)
    p.x['Neck'] -= 0.5 * math.degrees(p.cum('Head') - head)   # half: the head comes down a little with the chest

def crouch_feet(p, k=1.0, front=0.0):
    """Paws on their standing spots, the long hind feet let down flat on the floor (k: how far, 0..1), the front
    paws `front` m further back."""
    for key in ('HL', 'HR'): plant(p, key, dmeta=-HEEL_DOWN * k)
    for key in ('FL', 'FR'): plant(p, key, dy=front)

# A rabbit at rest is not a cat or a dog standing on its legs: it sits low, the belly almost on the floor, the hind
# legs folded right up under the haunches (the long feet flat, toes forward under the belly, the heel under the
# rump), so that they hardly show; only the short front legs stand, a little bent.
HUNCH_REAR, HUNCH_FRONT, HUNCH_FEET = 0.175, 0.14, -0.05

def hunch(p, rear=0.0, front=0.0, pitch=0.0, paws=0.0, **kw):
    """The resting crouch, `rear`/`front` m lower (or higher, negative) than it, the front paws `paws` m further
    back; the rest as body()."""
    lower(p, rear=HUNCH_REAR + rear, front=HUNCH_FRONT + front, pitch=pitch, **kw)
    for key in ('HL', 'HR'): plant(p, key, dy=HUNCH_FEET, dmeta=-HEEL_DOWN)
    for key in ('FL', 'FR'): plant(p, key, dy=paws)

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
    """At ease, hunched: belly low, hind legs folded away under the haunches, the front held up a little,
    breathing; the nose twitching in bursts."""
    br = math.sin(TAU * t * 2)
    hunch(p, rear=0.003 * br, spine=5 + 1.0 * br, neck=3 + 2 * math.sin(TAU * t), head=2 - 1.5 * math.sin(TAU * t))
    ears(p, swing=3 * math.sin(TAU * t - 0.6))
    nose(p, t, rate=12, amp=9 * (0.35 + 0.65 * smooth(math.sin(TAU * t * 2) * 2)))   # 4 a second, in bursts

def curve(t, keys):
    """A looping curve through (phase, value) keys, eased between them."""
    keys = sorted(keys)
    ext = [(k - 1, v) for k, v in keys] + keys + [(k + 1, v) for k, v in keys]
    for (a, va), (b, vb) in zip(ext, ext[1:]):
        if a <= t < b: return lerp(va, vb, smooth((t - a) / (b - a)))
    return keys[0][1]

def foot(t, land, duty, S, y_land, lift, heel_push, heel_swing, trail=0.0, trail_until=0.45, flat=0.0):
    """A paw that lands at phase `land`, stays down for `duty` of the cycle sliding back under the body (the body
    goes on at S per cycle), then swings forward in an arc to land again at y_land. trail: a hind paw that has
    just pushed stays stretched out behind (m further back, for the first trail_until of the swing) before it
    swings forward under the belly. flat: a hind paw lands on the whole long foot, the heel this many degrees down
    from the standing pose (HEEL_DOWN: flat on the floor), bears the weight on it and rolls onto the toes only as it
    pushes off. Returns (dy, dz, dmeta, down)."""
    k = (t - land) % 1.0
    if k < duty:
        u = k / duty
        # pushing off at the end of the stance: the heel comes up and the long foot rolls onto the toes
        m = -flat * (1 - smooth((u - 0.45) / 0.35)) + heel_push * smooth((u - 0.6) / 0.4)
        return y_land + S * duty * u, 0.0, m, 0.06 < u < 0.94
    u = (k - duty) / (1 - duty)
    start = y_land + S * duty
    if trail:
        # out behind, then forward
        back = trail * math.sin(math.pi * min(1.0, u / trail_until) / 1.0) if u < trail_until else 0.0
        fwd = smooth((u - trail_until * 0.6) / (1 - trail_until * 0.6))
        y = lerp(start, y_land, fwd) + back
        z = lift * (0.55 * math.sin(math.pi * min(1.0, u / trail_until)) + math.sin(math.pi * u)) / 1.3
        m = heel_push + (heel_swing - heel_push) * smooth(u / trail_until) if u < trail_until else heel_swing * (1 - smooth((u - trail_until) / (1 - trail_until)))
        return y, z, m - flat * smooth((u - 0.7) / 0.3), False     # the heel comes down to land flat
    y = lerp(start, y_land, smooth(u))
    z = lift * math.sin(math.pi * u) ** 0.8
    return y, z, heel_push * (1 - smooth(u / 0.3)) + heel_swing * math.sin(math.pi * u) - flat * smooth((u - 0.7) / 0.3), False

def carried(plans, rise, lifts):
    """Paws in the air go up with the body (rise, m) instead of hanging down to the floor: fully at the top of
    their swing, not at all as they leave or touch the floor. lifts: each key's swing height."""
    return [(k, y, z if d else z + max(0.0, rise) * min(1.0, 2 * z / lifts[k]), m, d) for k, y, z, m, d in plans]

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
    """The pet rabbit's hop, a little low leap: sitting crouched, it pushes off with both hind legs, the front
    lifting first; a moment in the air, front legs reaching; the front paws land together and take the body, the
    hind feet swing forward and land just behind them, and it sits a moment in its crouch before the next hop.
    The body goes forward in the leap and waits while it sits (prog), the paws stay put on the floor."""
    S = HOP_S
    # before the push the weight goes back onto the hind legs (the body rocks back and the rump sinks), then the
    # push; on landing the hind legs take the weight again and the rump settles down onto them
    prog = ramp(t, [(0.0, 0.0), (0.18, 0.0), (0.28, -0.03), (0.40, 0.36), (0.58, 0.78), (0.80, 0.96), (1.0, 1.0)])
    up = bump(t, 0.36, 0.68)
    rear = ramp(t, [(0.0, HUNCH_REAR), (0.18, HUNCH_REAR), (0.29, HUNCH_REAR + 0.03), (0.40, 0.04), (0.62, 0.05),
                    (0.80, HUNCH_REAR * 0.75), (0.88, HUNCH_REAR + 0.02), (1.0, HUNCH_REAR)])
    front = ramp(t, [(0.0, HUNCH_FRONT), (0.28, HUNCH_FRONT + 0.01), (0.38, 0.02), (0.56, 0.0), (0.68, HUNCH_FRONT * 0.85),
                     (0.85, HUNCH_FRONT), (1.0, HUNCH_FRONT)])
    lower(p, rear=rear, front=front, dy=-S * (prog - t),
          pitch=-5 * bump(t, 0.16, 0.34) - 6 * bump(t, 0.28, 0.48) + 6 * bump(t, 0.54, 0.70) - 5 * bump(t, 0.66, 0.96),
          spine=4 + 4 * bump(t, 0.36, 0.62) - 10 * bump(t, 0.64, 0.96),
          neck=curve(t, [(0.0, 3.0), (0.4, -5.0), (0.62, -3.0), (0.85, 3.0)]),
          head=curve(t, [(0.0, 2.0), (0.4, 3.0), (0.62, 2.0), (0.85, 2.0)]))
    p.hips = (p.hips[0], p.hips[1] + 0.08 * up)
    p.length = 1 + 0.10 * bump(t, 0.36, 0.64) - 0.06 * bump(t, 0.66, 0.94)
    plans = []
    for key, land in (('FL', 0.56), ('FR', 0.59)):
        y, z, m, down = foot(t, land, 0.72, S, -0.19, 0.10, 10, -45)
        plans.append((key, y, z, m, down))
    for key in ('HL', 'HR'):
        y, z, m, down = foot(t, 0.80, 0.58, S, HUNCH_FEET - 0.08, 0.04, 50, 15, trail=0.08, trail_until=0.45,
                             flat=HEEL_DOWN)
        if down:
            # the long feet land flat and stay flat while it sits, carrying its weight; the heels come up only at
            # the end of the push, the foot rolling onto the toes as the body goes forward over them
            m = -HEEL_DOWN if t > 0.5 else lerp(-HEEL_DOWN, 50.0, smooth((t - 0.31) / 0.08))
        plans.append((key, y, z, m, down))
    plans = carried(plans, 0.08 * up, {'FL': 0.10, 'FR': 0.10, 'HL': 0.04, 'HR': 0.04})
    lower_to_reach(p, [q[:4] for q in plans if q[4]])
    for q in plans: plant(p, q[0], dy=q[1], dz=q[2], dmeta=q[3])
    spread(p, 5 * up)
    p.belly = 0.025 * up            # the belly drawn up in the leap, not hanging
    ears(p, swing=curve(t, [(0.0, 0.0), (0.4, -4.0), (0.62, 5.0), (0.8, 1.0)]))
    nose(p, t, rate=2, amp=3)

RUN_S, RUN_FRAMES = 1.56, 12      # bounding: 1.56 m per leap at 2.5 leaps a second (3.9 m/s)
def run(p, t, f):
    """Running is a string of leaps (a bound), not a dog's gallop. t = 0 the hind feet land together, ahead of
    where the front paws came down, the back curled right up; they drive the body up and out (0-0.2): the back
    unrolls and stretches, and it sails in a high arc, front legs reaching ahead, hind legs stretched out behind.
    The front paws land almost together (0.52, 0.55), the nose dips, the body rolls over them and the back curls
    up again as the hind legs swing forward past them; the front paws push off and for a moment it flies gathered
    up before the hind feet come down."""
    S = RUN_S
    # the hind feet land flat and take the weight: the rump sinks down onto the folding hind legs (0-0.06) before
    # they drive it up and out, rolling onto the toes at the very end
    body(p,
         dz=curve(t, [(0.0, -0.08), (0.06, -0.11), (0.15, -0.01), (0.35, 0.22), (0.52, 0.06), (0.66, -0.02), (0.86, 0.04)]),
         pitch=curve(t, [(0.0, -12.0), (0.06, -15.0), (0.15, -8.0), (0.35, -2.0), (0.52, 6.0), (0.7, -2.0), (0.9, -10.0)]),
         spine=curve(t, [(0.0, -11.0), (0.15, -4.0), (0.35, 0.0), (0.52, -1.0), (0.7, -9.0), (0.9, -13.0)]),
         chest=curve(t, [(0.0, 2.0), (0.35, -6.0), (0.52, 0.0), (0.8, 3.0)]),
         neck=curve(t, [(0.0, -2.0), (0.35, -8.0), (0.52, -4.0), (0.75, 0.0)]),
         head=curve(t, [(0.0, 2.0), (0.35, 4.0), (0.52, 4.0), (0.75, 2.0)]))
    p.length = curve(t, [(0.0, 0.86), (0.2, 1.0), (0.38, 1.05), (0.54, 1.0), (0.74, 0.90), (0.9, 0.86)])
    plans = []
    for key, land in (('FL', 0.52), ('FR', 0.55)):
        y, z, m, down = foot(t, land, 0.26, S, -0.18, 0.18, 25, -60)
        plans.append((key, y, z, m, down))
    for key in ('HL', 'HR'):
        y, z, m, down = foot(t, 0.0, 0.18, S, -0.10, 0.18, 55, 10, trail=0.10, trail_until=0.40, flat=HEEL_DOWN)
        plans.append((key, y, z, m, down))
    rise = curve(t, [(0.0, -0.08), (0.06, -0.11), (0.15, -0.01), (0.35, 0.22), (0.52, 0.06), (0.66, -0.02), (0.86, 0.04)])
    plans = carried(plans, rise, {'FL': 0.18, 'FR': 0.18, 'HL': 0.18, 'HR': 0.18})
    lower_to_reach(p, [q[:4] for q in plans if q[4]])
    for q in plans: plant(p, q[0], dy=q[1], dz=q[2], dmeta=q[3])
    # the front legs in the air are swung by angle, not put at a spot on the floor that the bobbing body comes down
    # onto: off the floor they fold up under the chest (the upper arm stays as it is, the forearm comes forward, the
    # paw hangs from the wrist), then they reach out ahead to land. Swinging the upper arm back folds the armpit.
    for key, land in (('FL', 0.52), ('FR', 0.55)):
        u = ((t - land) % 1.0 - 0.26) / 0.74
        if u <= 0: continue
        w = smooth(u / 0.2) * (1 - smooth((u - 0.8) / 0.2))
        swing_leg(p, key, w, ramp(u, [(0.0, -40.0), (0.25, -60.0), (0.55, -95.0), (0.85, -112.0), (1.0, -112.0)]),
                  ramp(u, [(0.0, -90.0), (0.25, -150.0), (0.55, -130.0), (0.85, -125.0), (1.0, -125.0)]),
                  ramp(u, [(0.0, -140.0), (0.25, -95.0), (0.55, -150.0), (0.85, -165.0), (1.0, -165.0)]))
    # the hind legs likewise: after the push they follow through behind only so far (the thighs are the haunches:
    # stretched right out behind they drag the skin of the belly out into a sheet), then fold up under the belly
    # and reach forward to land
    for key in ('HL', 'HR'):
        u = (t - 0.18) / 0.82
        if u <= 0: continue
        w = smooth(u / 0.12) * (1 - smooth((u - 0.85) / 0.15))
        swing_leg(p, key, w, ramp(u, [(0.0, -95.0), (0.15, -110.0), (0.35, -120.0), (0.6, -138.0), (0.8, -155.0), (1.0, -160.0)]),
                  ramp(u, [(0.0, -20.0), (0.15, -25.0), (0.35, -35.0), (0.6, -40.0), (0.8, -45.0), (1.0, -55.0)]),
                  ramp(u, [(0.0, -80.0), (0.15, -25.0), (0.35, -55.0), (0.6, -150.0), (0.8, -165.0), (1.0, -155.0)]))
    spread(p, curve(t, [(0.0, 10.0), (0.25, 3.0), (0.6, 2.0), (0.85, 12.0)]))
    p.belly = 0.035 * smooth((t - 0.10) / 0.10) * (1 - smooth((t - 0.55) / 0.15))   # drawn up in the long leap
    ears(p, swing=curve(t, [(0.0, 3.0), (0.3, -8.0), (0.52, 8.0), (0.8, 2.0)]))

def swing_leg(p, key, w, *angles):
    """A leg in the air posed by the world angles of its bones (deg, side plane, from the top down; a hind
    leg's toes in line with the foot), blended by w over the pose it already has."""
    names = [n for n in LEGS[key][:4] if n]
    old = {n: p.x.get(n, 0.0) for n in names}
    phi = p.cum(REST[names[0]]['parent'])
    for n, a in zip(names, list(angles) + [None]):
        rel = 0.0 if a is None else math.degrees(D(a) - REST[n]['a'] - phi)
        phi += D(rel)
        d = (rel - old[n] + 180.0) % 360.0 - 180.0
        p.x[n] = old[n] + d * w

BINKY_UP = 0.40                    # how high the leap takes the body (m, at the top)

BINKY_TRAVEL = 1.6          # how far the leap carries it (m): it binkies on the run, the momentum carries it on
BINKY_FRAMES = 23

def binky(p, tt, f, side=1):
    """The happy leap, on the run: the hind legs fire it up off the floor, the front rearing up, and it flies on.
    The clip goes forward at an even BINKY_TRAVEL per clip (the game moves the pet that much); the body runs ahead of
    that in the air and falls behind as it lands and brakes (prog), the paws on the floor stay where they are put."""
    t = 0.16 + 0.84 * tt                 # the timing of the leap as it was from a standstill, without the coil
    prog = ramp(tt, [(0.0, 0.0), (0.17, 0.15), (0.76, 0.92), (0.83, 0.97), (1.0, 1.0)])
    _binky_at(p, t, side, BINKY_TRAVEL, -BINKY_TRAVEL * (prog - tt), tt)

def _binky_at(p, t, side, D, dyb, tt):
    """The leap itself (t: 0.16 crouched, 0.30 off the floor, 0.80 the front paws down, 0.86 the hind ones), the
    body dyb ahead of the evenly moving clip, the floor going by under it at D per clip (tt).
    From its crouch the hind legs fire it up off the floor, the front rearing up.
    In the air it is a ball: the back arched, the front paws folded up under the chest, the hind legs gathered; the
    rump twists round and rolls over to one side and the hind feet kick out sideways and back, while the head turns
    the other way; it straightens out, the front paws reach down to land first and it settles back into its crouch.
    The kick comes from the knees down (the thighs turning about their own length), not from the hips: the thighs
    are the haunches, and swinging them out would drag the skin of the flanks out with them.
    side: where the rump and the kick go (+1 towards +X, the model's left): each facing gets the clip that kicks
    towards the viewer."""
    S_ = side
    H = BINKY_UP * math.sin(math.pi * min(1.0, max(0.0, (t - 0.28) / 0.52)))
    rear = ramp(t, [(0.0, HUNCH_REAR), (0.12, HUNCH_REAR + 0.02), (0.18, HUNCH_REAR + 0.02), (0.30, 0.02),
                    (0.40, 0.05), (0.72, 0.05), (0.80, 0.03), (0.90, HUNCH_REAR - 0.03), (1.0, HUNCH_REAR)])
    front = ramp(t, [(0.0, HUNCH_FRONT), (0.12, HUNCH_FRONT + 0.02), (0.16, HUNCH_FRONT), (0.28, -0.03),
                     (0.40, 0.05), (0.66, 0.06), (0.78, 0.02), (0.86, HUNCH_FRONT + 0.03), (1.0, HUNCH_FRONT)])
    ball = smooth((t - 0.28) / 0.12) * (1 - smooth((t - 0.68) / 0.12))     # rolled up in the air
    tw = bump(t, 0.38, 0.76)                                    # the twist, there and back
    out = ramp(t, [(0.0, 0.0), (0.44, 0.0), (0.52, 1.0), (0.62, 1.0), (0.74, 0.0)])   # the kick out sideways
    lower(p, rear=rear, front=front, pitch=-6 * ball, spine=6 * bump(t, 0.0, 0.3) - 14 * ball + 4 * bump(t, 0.8, 1.0),
          chest=-4 * ball, neck=-8 * bump(t, 0.2, 0.5) - 4 * ball, head=4 * bump(t, 0.3, 0.7))
    p.length = 1.0 - 0.10 * ball
    p.hips = (p.hips[0] + dyb, p.hips[1] + H)
    T_FRONT, T_HIND = (0.80 - 0.16) / 0.84, (0.86 - 0.16) / 0.84      # when the paws come down (clip time)
    front_spot = lambda: -D * (1 - T_FRONT) + D * (tt - T_FRONT)      # where they land, at rest by the end
    hind_spot = lambda: HUNCH_FEET - D * (1 - T_HIND) + D * (tt - T_HIND)
    # the rump swings round (yaw) and rolls over (its legs go out with it), the back and the chest turn the front
    # straight again, and the head looks the other way. (roll, yaw) about each bone's own length and its down axis
    p.yz['Hips'] = (22 * S_ * tw, 26 * S_ * tw)
    p.yz['Spine'] = (-9 * S_ * tw, -14 * S_ * tw)
    p.yz['Chest'] = (-10 * S_ * tw, -12 * S_ * tw)
    p.yz['Neck'] = (0.0, 16 * S_ * tw)
    p.yz['Head'] = (0.0, 10 * S_ * tw)
    # front legs: on the floor as the front rears up, then folded up under the chest (elbows back, the paws hanging
    # from bent wrists), and reaching down to the floor to land first
    fold = smooth((t - 0.20) / 0.10) * (1 - smooth((t - 0.64) / 0.14))
    for key in ('FL', 'FR'):
        # on the floor: where they stood at the start, then where they are going to land
        down = (PAW[key][0] + (D * tt if t < 0.5 else front_spot()), PAW[key][1] + H)
        sh = shoulder(p, key)
        up = (sh[0] - 0.075, sh[1] - 0.235)
        reach(p, key, (lerp(down[0], up[0], fold), lerp(down[1], up[1], fold)), dmeta=55 * fold)
    # hind legs: pushing off (the heels come up as they straighten); in the air they are swung by angle, not put at
    # a spot: the thighs stay close to where they are (they are the haunches), the legs fold up under the belly and
    # then kick out from the knee, the long feet flung back; then they reach down under it to land flat in the crouch
    air = smooth((t - 0.30) / 0.08) * (1 - smooth((t - 0.70) / 0.10))
    for key in ('HL', 'HR'):
        if t < 0.30:
            push = smooth((t - 0.16) / 0.14)
            plant(p, key, dy=HUNCH_FEET + D * tt, dmeta=-HEEL_DOWN * (1 - push) + 50 * push)
            continue
        # carried along under the body, then reaching for the spot on the floor where they land
        plant(p, key, dy=lerp(dyb, hind_spot(), smooth((t - 0.72) / 0.14)),
              dz=H + ramp(t, [(0.30, 0.0), (0.70, 0.05), (0.80, 0.02), (0.86, 0.0), (1.0, 0.0)]),
              dmeta=ramp(t, [(0.30, 50.0), (0.70, 0.0), (0.80, -60.0), (0.86, -HEEL_DOWN), (1.0, -HEEL_DOWN)]))
        if air > 0:
            swing_leg(p, key, air,
                      ramp(t, [(0.30, -120.0), (0.38, -118.0), (0.46, -155.0), (0.53, -128.0), (0.64, -130.0), (0.74, -150.0)]),
                      ramp(t, [(0.30, -30.0), (0.38, -20.0), (0.46, -30.0), (0.53, -15.0), (0.64, -22.0), (0.74, -40.0)]),
                      ramp(t, [(0.30, -60.0), (0.38, -40.0), (0.46, -175.0), (0.53, -45.0), (0.64, -55.0), (0.74, -165.0)]))
    # the kick sideways: each thigh turns about its own length, so the bent leg below the knee swings out while the
    # haunch stays where it is; only a little out at the hip, the outer leg further than the one under the belly
    for s_, sg in (('L', 1), ('R', -1)):
        outer = sg == S_
        p.yz[f'Thigh.{s_}'] = (S_ * (38 if outer else 26) * out, -S_ * (10 if outer else 4) * out)
    # the ears flop about with the jolts, only a little: their upper half is part of the head
    ears(p, swing=6 * math.sin(TAU * t * 1.5) - 4 * bump(t, 0.3, 0.8), out=3 * bump(t, 0.3, 0.8))

LOAF_REAR, LOAF_FRONT = 0.18, 0.22       # how far the body comes down to lie on the floor, the chest on it

def tuck_front(p, ahead=0.06):
    """Front legs folded down under the chest (a loafing rabbit): elbows back, forearms along the floor, the paws
    flat with only the toes showing, `ahead` m in front of the shoulders. Pose the body first. (Nearer than about
    6 cm the upper arm swings back so far that the armpit creases.)"""
    for key in ('FL', 'FR'):
        sh = shoulder(p, key)
        reach(p, key, (sh[0] - ahead, 0.012), dmeta=-30)

def loaf(p, t, f):
    """The loaf: the whole body let down onto the floor, hind feet flat beside it, front paws folded away under the
    chest, head drawn in, ears lying back, breathing."""
    br = math.sin(TAU * t * 2)
    lower(p, rear=LOAF_REAR - 0.003 * br, front=LOAF_FRONT, spine=4 + 0.8 * br, neck=8, head=4)
    for key in ('HL', 'HR'): plant(p, key, dy=HUNCH_FEET, dmeta=-HEEL_DOWN)
    tuck_front(p)
    ears(p, swing=-6)
    nose(p, t, rate=9, amp=4)      # 3 a second, calm

def sleep(p, t, f):
    """Asleep: loafed and gone limp, leaning a little, the head sunk until the chin rests on the floor, ears
    back, slow deep breaths and the nose still."""
    br = math.sin(TAU * t * 2)
    p.roll = 6
    lower(p, rear=LOAF_REAR + 0.005 - 0.005 * br, front=LOAF_FRONT + 0.01, spine=5 + 1.4 * br, neck=22, head=16)
    for key in ('HL', 'HR'): plant(p, key, dy=HUNCH_FEET, dmeta=-HEEL_DOWN)
    tuck_front(p, ahead=0.05)
    ears(p, swing=-9)

def sit(p, t, f):
    """Alert: still hunched behind, the front up on straight legs, head up; only the nose and the ears are busy."""
    br = math.sin(TAU * t * 2)
    hunch(p, front=-0.07, pitch=0.3 * br, spine=-3, neck=-10, head=-4)
    settle_front(p)
    for key in ('FL', 'FR'): plant(p, key)
    ears(p, swing=-4 + 1.5 * math.sin(TAU * t), out=2)
    nose(p, t, rate=10, amp=9)     # 5 a second

def groom(p, t, f):
    """Washing the face, crouched: the head bows and turns towards one front paw, the paw comes up to the cheek, is
    licked, and wipes down over the eye to the nose; then the other paw. Two wipes, one per paw."""
    half = 0 if t < 0.5 else 1
    u = (t * 2) % 1.0
    key, other, sgn = (('FL', 'FR', 1), ('FR', 'FL', -1))[half]
    up = bump(u, 0.05, 0.95)
    wipe = smooth((u - 0.45) / 0.35)                      # 0 at the cheek, 1 down at the nose
    lick = bump(u, 0.22, 0.45)
    lower(p, rear=HUNCH_REAR, front=HUNCH_FRONT - 0.03 * up, spine=4 - 2 * up, neck=6 + 8 * up + 4 * wipe, head=6 + 4 * lick)
    p.yz['Neck'] = (0.0, 6 * sgn * up)
    p.yz['Head'] = (0.0, 4 * sgn * up)
    for k in ('HL', 'HR'): plant(p, k, dy=HUNCH_FEET, dmeta=-HEEL_DOWN)
    plant(p, other)
    mz = muzzle(p)
    # from the floor up to the cheek, a little lick, then down the face to the nose, and back to the floor
    cheek = (mz[0] + 0.07, mz[1] + 0.04 - 0.02 * lick)      # (any higher, the upper arm swings so far up that the
    nosept = (mz[0] + 0.03, mz[1] - 0.03)                   # skin over the shoulder tears)
    tgt = (lerp(cheek[0], nosept[0], wipe), lerp(cheek[1], nosept[1], wipe))
    rest = PAW[key]                                        # its spot on the floor
    lift = smooth(u / 0.25) * (1 - smooth((u - 0.82) / 0.18))
    reach(p, key, (lerp(rest[0], tgt[0], lift), lerp(rest[1], tgt[1], lift)), dmeta=-80 * lift)
    # out to the side as it comes up: the cheek is wider than the shoulders, and straight up the paw would go into it
    arm_ = 'UpperArm.L' if key == 'FL' else 'UpperArm.R'
    p.yz[arm_] = (0.0, -sgn * 14 * lift)
    ears(p, swing=3 * up - 2)
    p.x['Nose'] = 7 * lick * max(0.0, math.sin(TAU * u * 8))

def eat(p, t, f):
    """Hunched at the bowl, belly low. Its head dips into the hay, nibbling, and comes up a little with a few blades
    across its mouth; it chews them in, a bite at a time, the nose going, until they are gone; then down for more.
    A mouthful every three seconds, a little over two chews a second."""
    dip = 1 - smooth((t - 0.10) / 0.12) + smooth((t - 0.88) / 0.12)          # head down in the bowl (and loops)
    chewing = smooth((t - 0.18) / 0.06) * (1 - smooth((t - 0.86) / 0.06))
    chew = math.sin(TAU * t * 7) * chewing
    nibble = math.sin(TAU * t * 12) * dip
    hunch(p, front=0.02 + 0.02 * dip, spine=4, neck=12 + 16 * dip + 1.5 * chew, head=8 + 6 * dip + 1.0 * chew + 2 * nibble)
    # the blades: picked up in the bowl, then pulled in with each bite, in six pulls
    k = 6 * min(1.0, max(0.0, (t - 0.22) / 0.62))
    eaten = (math.floor(k) + smooth((k - math.floor(k)) / 0.5)) / 6
    p.hay = smooth((t - 0.12) / 0.08) * (1 - eaten)
    p.x['Nose'] = 5 * max(0.0, chew) + 3 * max(0.0, nibble)
    ears(p, swing=1.5 * chew)

def flop_pose(p, k, br=0.0):
    """Lying on its side (k = how far over, 0..1), gone limp: all four legs laid out on the floor, the front ones
    reaching out past the chin and the hind ones past the tail, the near and the far one of each pair crossed over
    each other; the body curled a little round, not a log; the head laid down."""
    s = smooth(k)
    p.roll = 85 * s              # onto its right side, the back towards +X (the underside of the mesh is dark and rough)
    # from its crouch: the body down at the floor, rolled over
    lower(p, rear=lerp(HUNCH_REAR, 0.05, s), front=lerp(HUNCH_FRONT, 0.05, s), spine=-6 * k + br, neck=10 * k,
          head=6 * k)
    p.length = 1 + 0.06 * s
    plant(p, 'HL', dy=lerp(HUNCH_FEET, 0.47, s), dz=0.10 * s, dmeta=lerp(-HEEL_DOWN, 150.0, s))
    plant(p, 'HR', dy=lerp(HUNCH_FEET, 0.39, s), dz=0.03 * s, dmeta=lerp(-HEEL_DOWN, 135.0, s))
    plant(p, 'FL', dy=-0.26 * s, dz=0.08 * s, dmeta=-60 * s)
    plant(p, 'FR', dy=-0.18 * s, dz=0.02 * s, dmeta=-35 * s)
    c = 10 * s
    p.yz['Spine'] = (0.0, c); p.yz['Chest'] = (0.0, 0.6 * c); p.yz['Neck'] = (0.0, 0.8 * c); p.yz['Hips'] = (0.0, -0.6 * c)
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
    """Touching down: the legs give, folding up under it, and it settles into its crouch."""
    a = smooth(t / 0.4)
    lower(p, rear=lerp(0.0, HUNCH_REAR + 0.01, a), front=lerp(0.0, HUNCH_FRONT + 0.03 * bump(t, 0.2, 0.8), a),
          spine=5 * bump(t, 0.0, 1.0), neck=6 * bump(t, 0.0, 1.0))
    for key in ('HL', 'HR'): plant(p, key, dy=HUNCH_FEET * a, dmeta=-HEEL_DOWN * a)
    for key in ('FL', 'FR'): plant(p, key)
    ears(p, swing=-8 * bump(t, 0.0, 1.0))

def petted(p, t, f):
    """Stroked: it sinks down flat into the floor, head lowered, ears back, breathing slow."""
    br = math.sin(TAU * t)
    hunch(p, rear=0.015 - 0.004 * br, front=0.04, spine=3 + br, neck=12, head=8, paws=0.03)
    ears(p, swing=-6)

CLIPS = [  # name, frames, function, bake options
    ("Idle", 90, idle, {}), ("Hop", HOP_FRAMES, hop, {}), ("Run", RUN_FRAMES, run, {}),
    ("Binky", BINKY_FRAMES, binky, dict(loop=False)),
    ("Binky_R", BINKY_FRAMES, lambda p, t, f: binky(p, t, f, side=-1), dict(loop=False)), ("Loaf", 90, loaf, {}), ("Sleep", 120, sleep, {}), ("Sit", 60, sit, {}),
    ("Groom", 60, groom, {}), ("Eat", 90, eat, {}), ("Flop", 30, flop, dict(loop=False, ground=True)),
    ("FlopSleep", 120, flop_sleep, dict(ground=True)), ("Thump", 24, thump, dict(loop=False)), ("Held", 40, held, {}),
    ("Fall", 20, fall, {}), ("Land", 12, land, dict(loop=False)), ("Petted", 60, petted, {}),
]

if __name__ == "__main__":
    # CLIPS=Hop,Run OUT=<file.blend>: only those, saved elsewhere (for trying a change without the whole set)
    only = [c for c in os.environ.get("CLIPS", "").split(",") if c]
    for name, frames, fn, kw in CLIPS:
        if not only or name in only: bake(name, frames, fn, **kw)
    arm.animation_data.action = bpy.data.actions["Idle" if not only else only[0]]
    bpy.ops.wm.save_as_mainfile(filepath=os.environ.get("OUT") or work_file("anim.blend"))
