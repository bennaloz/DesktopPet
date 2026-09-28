import os,sys; sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); from paths import work_file
"""Procedural animation library for the Zaira rig (Blender, head towards -Y, Z up).

Every bone's local X axis is world +X, so a rotation about local X is a bend in the side (YZ) plane.
Angles in that plane: alpha = atan2(dz, dy); a positive X rotation increases alpha.
Legs are posed with a planar two-bone IK so paws stay planted on the ground (no foot sliding).
"""
import bpy, math
from mathutils import Vector, Matrix

FPS = 30
D = math.radians

bpy.ops.wm.open_mainfile(filepath=work_file("rig.blend"))
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
        self.belly = (0.0, 1.0)  # Belly bone: how far the belly skin drops (m, towards the floor), how wide it spreads
        self.squash = 1.0        # length of the middle of the back (Spine) as a fraction: a loafing cat draws in
        self.shrink = {}         # bone -> uniform scale: paws tucked out of sight under a lean body

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
            d = sub(q, h)
            if n == 'Spine' and self.squash != 1.0:
                # shortened along its own rest direction (points carried by it or its children come closer)
                a = REST[n]['a']; u = (math.cos(a), math.sin(a))
                along = d[0] * u[0] + d[1] * u[1]
                d = (d[0] - along * u[0] * (1 - self.squash), d[1] - along * u[1] * (1 - self.squash))
            q = add(rot2(D(self.x.get(n, 0.0)), d), h)
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
        if toe and toe_angle is None and toe.startswith('Finger'):
            # front toes follow the paw unless a pose lays them down (walking they roll with it, as before
            # the rig had them: kept flat they looked stiff and seemed to twist outwards)
            self.x[toe] = 0.0
        elif toe:
            ta = REST[toe]['a'] if toe_angle is None else D(toe_angle)
            self.x[toe] = math.degrees(ta - REST[toe]['a'] - (phi + t1 + t2 + t3))

    def apply(self, frame):
        for pb in P:
            x = (self.x.get(pb.name, 0.0) + 180.0) % 360.0 - 180.0
            y, z = self.yz.get(pb.name, (0.0, 0.0))
            pb.rotation_euler = (D(x), D(y), D(z))
            pb.keyframe_insert("rotation_euler", frame=frame)
        # the belly: the bone points at the floor (local +Y) and its local X is world X (the width)
        pb = P['Belly']
        pb.location = (0.0, self.belly[0], 0.0)
        # (its local Z runs along the body, which the squashed Spine above it shortens: undo that here)
        pb.scale = (self.belly[1], 1.0, self.belly[1] / self.squash)
        pb.keyframe_insert("location", frame=frame)
        pb.keyframe_insert("scale", frame=frame)
        # the squashed back: Spine shorter along its length (local Y), Chest undoes it so the chest, the head
        # and the front legs keep their size (their axes line up with the Spine's: every bone's X is world X)
        for name, k in (('Spine', self.squash), ('Chest', 1 / self.squash)):
            P[name].scale = (1.0, k, 1.0)
            P[name].keyframe_insert("scale", frame=frame)
        # every other bone keys its scale too (1 unless shrunk), or a clip would keep the last clip's shrunk paws
        for pb in P:
            if pb.name in ('Belly', 'Spine', 'Chest'): continue
            k = self.shrink.get(pb.name, 1.0)
            pb.scale = (k, k, k)
            pb.keyframe_insert("scale", frame=frame)
        # hips offset: world delta -> hips bone local space
        R = bones['Hips'].matrix_local.to_3x3()
        pb = P['Hips']
        pb.location = R.inverted() @ Vector((0, self.hips[0], self.hips[1]))
        pb.keyframe_insert("location", frame=frame)

# rest paw (toe-base) positions and metatarsus angles
LEGS = {
    'HL': ('Thigh.L', 'Shin.L', 'Foot.L', 'Toe.L', True),
    'HR': ('Thigh.R', 'Shin.R', 'Foot.R', 'Toe.R', True),
    'FL': ('UpperArm.L', 'Forearm.L', 'Hand.L', 'Finger.L', False),
    'FR': ('UpperArm.R', 'Forearm.R', 'Hand.R', 'Finger.R', False),
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

# Which way sideways curls go (the tail wrapped round her while resting): 1 = to her left (+X), the side seen
# when she faces left on screen; -1 mirrors them for the "_R" clips shown when she faces right.
TAIL_SIDE = 1

def chain_world(p, names, alphas, lateral=None):
    """Point each bone of a chain at a world side-plane angle (deg); optional sideways curl per bone."""
    for i, n in enumerate(names):
        par = REST[n]['parent']
        phi = p.cum(par) if par else 0.0
        p.x[n] = alphas[i] - math.degrees(REST[n]['a'] + phi)
        if lateral: p.yz[n] = (0.0, TAIL_SIDE * lateral[i])

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

def bake_both_sides(name, frames, fn, loop=True):
    """A resting clip twice: tail curled to her left (name) and to her right (name_R), so the game can always
    keep the tail on the side facing the viewer."""
    global TAIL_SIDE
    bake(name, frames, fn, loop)
    TAIL_SIDE = -1
    bake(name + "_R", frames, fn, loop)
    TAIL_SIDE = 1

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

SCAPULA = {'FL': 'Scapula.L', 'FR': 'Scapula.R'}
SCAPULA_SWING = 18   # degrees each way at the ends of the stride (running: SCAPULA_RUN)
SCAPULA_RUN = 24

def gait(p, t, phases, duty, reach, lift, bob, spine_flex=0.0, meta_roll=25.0, swing_curl=40.0, centre=None,
         blade=None, track=(0.0, 0.0), hind_lift=None, extend=(0.0, 0.0)):
    """extend: (front, hind) how far (m) each paw reaches out forward past its landing spot late in the swing,
    the way a galloping cat throws its legs out before they land.
    track: (front, hind) degrees each leg leans in under the body from shoulder/hip: a walking cat puts its
    paws almost in one line under its middle, not at the corners like a table's legs."""
    centre = centre or {}
    blade = SCAPULA_SWING if blade is None else blade
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
            ext = extend[0] if key[0] == 'F' else extend[1]
            if ext: dy -= ext * math.sin(math.pi * min(1.0, k ** 1.6))   # peaks about 2/3 into the swing
            dz = (lift if key[0] == 'F' or hind_lift is None else hind_lift) * math.sin(math.pi * k)
            # rolls from the push-off angle back to the touch-down angle (the stance start), curling on the way:
            # no jump at either end, or the paw snaps round and seems to twist
            # the curl peaks early and is gone before touch-down: the paw reaches out flat to land
            dm = lerp(meta_roll, -meta_roll, smooth(k)) + swing_curl * math.sin(math.pi * min(1.0, k / 0.8))
            toe = None
        upper = LEGS[key][0]
        lean = track[0] if key[0] == 'F' else track[1]
        p.yz[upper] = (0.0, lean if key[1] == 'L' else -lean)
        if key in SCAPULA:
            # the shoulder blade swings with its leg: shoulder joint forward with the paw reaching out, back
            # (the top of the blade rising) as the paw pushes off behind
            p.x[SCAPULA[key]] = blade * dy / reach
        plant(p, key, dy + centre.get(key, 0.0), dz, dm, toe)

WALK_REACH, WALK_HIND_SHIFT = 0.185, 0.10
# Running strides at a cat's pace (a gallop is 3+ strides a second; 14 frames looked like slow motion). The reach
# goes with it so the paws still keep pace with the ground: 2 * reach / (duty * stride time) is the speed the
# profile's ref_speed_px was set for (lope 288 px/s, sprint 380 px/s at ~118 px per metre of rig).
RUN_FRAMES, RUN_REACH = 11, 0.167
SPRINT_FRAMES, SPRINT_REACH = 9, 0.155
# A cat's gallop: the front legs are thrown out well ahead of the shoulders before they land and the stance
# happens ahead of them; the hind legs come forward under the belly (the gathered phase) and push back behind.
RUN_EXTEND, RUN_CENTRE = (0.10, 0.06), {'FL': -0.05, 'FR': -0.05, 'HL': -0.04, 'HR': -0.04}
SPRINT_EXTEND, SPRINT_CENTRE = (0.13, 0.08), {'FL': -0.06, 'FR': -0.06, 'HL': -0.05, 'HR': -0.05}
WALK_DROP, TROT_DROP, RUN_DROP = -0.035, -0.02, -0.03
# the swinging leg folds at the elbow/knee and a little at the wrist (straighter, the legs looked in plaster)
WALK_LIFT, WALK_CURL = 0.1, 40
WALK_HIND_LIFT = 0.055   # the hind paws barely clear the ground
WALK_TRACK = (14, 6)   # legs lean in: the paws land nearly in line under the body
WALK_ROLL, WALK_YAW = 3.0, 4.0   # degrees of sway of hips and shoulders

def walk(p, t, f):
    """A cat's walk: lateral-sequence gait, head low and steady, shoulders and hips rolling, back weaving."""
    w = TAU * t
    # weight shifts: the body dips a little twice per stride, rolls towards the leg that carries it
    # a walking cat carries itself lower than standing still, knees and elbows bent (legs straight like
    # pillars look stiff)
    p.hips = (0.0, WALK_DROP + 0.005 * math.cos(2 * w))
    # hips and shoulders sway with the legs: the hip of the leg in the air drops and swings forward with it
    # (the hind legs swing around 82% of the stride, the front ones around 7%), the back in between winds like
    # a snake. Kept small: the leg IK works in the side plane, so a big roll would slide the planted paws sideways.
    hr = -WALK_ROLL * math.cos(TAU * (t - 0.82))          # + raises the left hip
    hy = WALK_YAW * math.cos(TAU * (t - 0.82))            # + brings the left hip forward
    sr = -WALK_ROLL * math.cos(TAU * (t - 0.07))
    sy = WALK_YAW * math.cos(TAU * (t - 0.07))
    p.yz['Hips'] = (hr, hy)
    p.yz['Spine'] = (-hr, -0.5 * hy)
    p.yz['Chest'] = (sr, sy - 0.5 * hy)
    # head low, nose level, steady: the neck soaks up the body's bob
    p.x['Neck'] = 16 - 1.5 * math.cos(2 * w)
    p.x['Head'] = -14 + 1.5 * math.cos(2 * w)
    p.yz['Neck'] = (0.0, 2.0 * math.sin(w + 2.4))
    # lateral sequence (LH, LF, RH, RF), with direct register: the hind paw lands in the print the front paw
    # of the same side left. Print spacing 0.535 = 0.75 * stride + hind stance shift: stride 0.58, shift 0.10.
    # the paw lifts from the elbow/knee (lift) more than it curls at the wrist (swing_curl): a big curl shows the
    # pad sideways to a three-quarter view and reads as the foot twisting outwards
    gait(p, t, {'HL': 0.0, 'FL': 0.25, 'HR': 0.5, 'FR': 0.75}, duty=0.64, reach=WALK_REACH, lift=WALK_LIFT, bob=0.0,
         meta_roll=20, swing_curl=WALK_CURL, centre={'HL': -WALK_HIND_SHIFT, 'HR': -WALK_HIND_SHIFT}, track=WALK_TRACK,
         hind_lift=WALK_HIND_LIFT)
    # the legs keep their own lean and twist while the hips and shoulders sway above them: take back what each
    # one inherits (hind legs from the hips, front legs from hips + back + chest), or the planted paws slide
    for key, roll, yaw in (('HL', hr, hy), ('HR', hr, hy), ('FL', sr, sy), ('FR', sr, sy)):
        upper = LEGS[key][0]
        y, z = p.yz.get(upper, (0.0, 0.0))
        # (measured: the front legs, hanging from the shoulder blades, take their twist back the other way)
        p.yz[upper] = (y + (-1 if key[0] == 'F' else 1) * yaw, z + roll)
    tail(p, lift=6, sway=9, t=w)

def run(p, t, f):
    c = math.cos(TAU * t)
    p.hips = (0.0, RUN_DROP + 0.025 * math.sin(TAU * t + 0.6))
    # the back flexes and extends with the stride
    # the back bends and stretches with every stride, a little less than in the sprint
    p.x['Hips'] = 6 * c
    p.x['Spine'] = -5 * c
    p.x['Chest'] = -4 * c
    p.x['Neck'] = 8 + 5 * c
    p.x['Head'] = -8
    # rotary gallop, as cats run: LH, RH, then RF, LF (the footfalls go round the body)
    gait(p, t, {'HL': 0.0, 'HR': 0.08, 'FR': 0.5, 'FL': 0.58}, duty=0.38, reach=RUN_REACH, lift=0.09, bob=0.02, blade=SCAPULA_RUN,
         extend=RUN_EXTEND, centre=RUN_CENTRE,
         meta_roll=30, swing_curl=55)
    tail(p, lift=10 + 4 * c, sway=5, t=TAU * t)

TROT_TAIL = [55, 80, 95, 125, 155]

def trot(p, t, f):
    """The happy trot: diagonal pairs (left hind with right fore), a bouncy step, head up,
    tail straight up with the tip hooked forward, the way a pleased cat comes over."""
    w = TAU * t
    p.hips = (0.0, TROT_DROP + 0.011 * math.cos(2 * w))     # a bounce at every diagonal push
    p.x['Neck'] = -6 + 2 * math.cos(2 * w)
    p.x['Head'] = 10 - 2 * math.cos(2 * w)
    gait(p, t, {'HL': 0.0, 'FR': 0.02, 'HR': 0.5, 'FL': 0.52}, duty=0.45, reach=0.143, lift=0.1, bob=0.0,
         meta_roll=20, swing_curl=40, track=(10, 5))   # folding legs, paws towards the middle, as in the walk
    sway = 4 * math.sin(w)
    chain_world(p, TAILS, TROT_TAIL, [0, sway * 0.5, sway, sway * 1.5, 6 + 10 * math.sin(2 * w)])

# ---- hunting the cursor
STALK_DROP = -0.14

def stalk_pose(p, t, lash=1.0, drop=STALK_DROP):
    """Hunting crouch: belly close to the ground, shoulder blades up, head low and dead level on the prey,
    hind legs gathered under, tail low along the ground with only the tip twitching."""
    w = TAU * t
    p.hips = (0.0, drop + 0.002 * math.sin(w))
    p.x['Hips'] = 3
    p.x['Spine'] = -2
    p.x['Chest'] = 4
    p.x['Neck'] = 10
    p.x['Head'] = -14
    plant(p, 'FL', 0.03, 0.0, 22)
    plant(p, 'FR', 0.03, 0.0, 22)
    plant(p, 'HL', 0.04, 0.0, -30)
    plant(p, 'HR', 0.04, 0.0, -30)
    tw = lash * 12 * math.sin(3 * w)
    chain_world(p, TAILS, [-18, -10, -5, 2, 6], [0, 0, 0, tw * 0.6, tw])

def stalk(p, t, f):
    stalk_pose(p, t)

def wiggle(p, t, f):
    """The rump wiggle before a pounce: hips shimmy side to side, hind paws tread, tail tip lashes."""
    w = TAU * t
    stalk_pose(p, t * 0.5, lash=1.8)
    p.hips = (0.0, STALK_DROP + 0.012 + 0.006 * math.sin(2 * w))   # rump a touch higher, ready
    p.yz['Hips'] = (5.0 * math.sin(w), 4.0 * math.sin(w))
    plant(p, 'HL', 0.04, 0.012 * max(0.0, math.sin(w)), -30)
    plant(p, 'HR', 0.04, 0.012 * max(0.0, -math.sin(w)), -30)

def swat(p, t, f):
    """A quick swat from a half crouch: one fore paw comes up, strikes forward and down, and is set back."""
    stalk_pose(p, 0.0, lash=0.5, drop=-0.07)
    up = smooth(t / 0.3) * (1 - smooth((t - 0.3) / 0.25))      # raise, then strike
    hit = smooth((t - 0.3) / 0.25) * (1 - smooth((t - 0.6) / 0.4))
    p.x['Hips'] = 3 - 6 * up + 4 * hit                        # rock back to raise, forward to strike
    p.x['Neck'] = 10 - 6 * up + 4 * hit
    plant(p, 'FL', 0.03 - 0.10 * up - 0.24 * hit, 0.26 * up + 0.02 * hit, 22 - 110 * up - 95 * hit)

def sprint(p, t, f):
    """Zoomies: a flat-out rotary gallop. The back flexes hard and stretches out at every stride (that spring
    is where a cat's speed comes from), body low, head pushed forward and steady, tail straight back."""
    w = TAU * t
    c = math.cos(w)
    p.hips = (0.0, -0.03 + 0.035 * math.sin(w + 0.6))
    p.x['Hips'] = 8 * c
    p.x['Spine'] = -6 * c
    p.x['Chest'] = -5 * c
    p.x['Neck'] = 12 + 5 * c        # soaks up the back's swing: the head stays level
    p.x['Head'] = -10
    gait(p, t, {'HL': 0.0, 'HR': 0.1, 'FR': 0.45, 'FL': 0.55}, duty=0.32, reach=SPRINT_REACH, lift=0.11, bob=0.0,
         blade=SCAPULA_RUN, extend=SPRINT_EXTEND, centre=SPRINT_CENTRE,
         meta_roll=35, swing_curl=70)
    tail(p, lift=5 + 3 * c, sway=3, t=w)

# ---- jump: parametric key poses blended over time (a real cat's take-off, flight and landing)
STAND = dict(dz=0.0, pitch=0.0, spine=0.0, chest=0.0, neck=0.0, head=0.0, tail=4.0,
             legs={k: (0.0, 0.0, 0.0) for k in ('FL', 'FR', 'HL', 'HR')})
# loading: rump sinks on the hind legs, front stays up, head points at the target
LOAD = dict(dz=-0.12, pitch=-10.0, spine=3.0, chest=2.0, neck=10.0, head=-6.0, tail=-4.0,
            legs={'FL': (0.02, 0.0, 5.0), 'FR': (0.02, 0.0, 5.0), 'HL': (0.03, 0.0, -18.0), 'HR': (0.03, 0.0, -18.0)})
# push-off: hind legs straighten against the ground, front paws already folded up under the chest
PUSH = dict(dz=0.03, pitch=4.0, spine=-3.0, chest=-2.0, neck=-2.0, head=2.0, tail=6.0,
            legs={'FL': (0.03, 0.15, 70.0), 'FR': (0.05, 0.14, 70.0), 'HL': (0.24, 0.02, 70.0), 'HR': (0.25, 0.02, 70.0)})
# flight: stretched out by the push - back extended, front legs reaching forward and up for the ledge, hind legs
# extended back after the push, tail out behind and a little raised (the counterweight)
FLY = dict(dz=0.0, pitch=10.0, spine=-5.0, chest=-4.0, neck=-6.0, head=4.0, tail=65.0,
           legs={'FL': (-0.32, 0.28, -90.0), 'FR': (-0.30, 0.30, -90.0), 'HL': (0.27, 0.12, 75.0), 'HR': (0.29, 0.13, 75.0)})
# coming down: back arched, front legs stretched forward and down for the ground, hind legs brought forward
# under the belly, tail out behind for balance
DOWN = dict(dz=0.0, pitch=10.0, spine=12.0, chest=6.0, neck=-6.0, head=2.0, tail=50.0,
            legs={'FL': (-0.13, 0.0, -25.0), 'FR': (-0.11, 0.02, -25.0), 'HL': (-0.08, 0.14, 40.0), 'HR': (-0.06, 0.15, 40.0)})
# touch-down: the front legs take the weight and give, chest dips, back still curved, hind paws come down
ABSORB = dict(dz=-0.07, pitch=6.0, spine=10.0, chest=10.0, neck=-8.0, head=4.0, tail=20.0,
              legs={'FL': (0.0, 0.0, 0.0), 'FR': (0.0, 0.0, 0.0), 'HL': (-0.04, 0.0, 0.0), 'HR': (-0.04, 0.0, 0.0)})

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

def aim(p, t, f):
    """Taking aim from the crouch: she rises a little and looks up at the landing spot, sinks back,
    shifts her weight, hind paws treading."""
    w = TAU * t
    up = max(0.0, math.sin(w))
    q = dict(LOAD)
    q['dz'] = LOAD['dz'] + 0.035 * up
    q['neck'] = LOAD['neck'] - 12 * up
    q['head'] = LOAD['head'] + 6 * up
    pose_from(p, q)
    plant(p, 'HL', 0.03, 0.012 * max(0.0, math.sin(2 * w)), -18)
    plant(p, 'HR', 0.03, 0.012 * max(0.0, -math.sin(2 * w)), -18)

def jump(p, t, f):
    pose_from(p, keys(t, [(0, LOAD), (0.2, PUSH), (0.6, FLY), (1, FLY)]))

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

CROUCH_DROP = -0.31   # hips and thighs down on the floor

def crouch(p, t, f):
    """Crouched like a sphinx: belly and hips on the floor, chest held up on the forearms (elbows under the
    shoulders, forearms flat, paws just in front of the chest, toes flat), neck up, head level, hind legs folded
    under the thighs with the feet forward, tail wrapped along the flank."""
    breathe = math.sin(TAU * t)
    p.hips = (0.0, CROUCH_DROP + 0.003 * breathe)
    p.x['Hips'] = CROUCH_PITCH
    p.x['Spine'] = CROUCH_SPINE
    p.x['Chest'] = CROUCH_CHEST + breathe
    p.x['Neck'] = CROUCH_NECK
    p.x['Head'] = -(CROUCH_PITCH + CROUCH_SPINE + CROUCH_CHEST + CROUCH_NECK) + CROUCH_HEAD
    for key in ('FL', 'FR'):
        u, l, m, t_, fwd = LEGS[key]
        H = p.point(REST[u]['parent'], REST[u]['h'])
        # the upper arm slopes down and back from the shoulder, the elbow rests on the floor behind it and the
        # forearm lies flat forward from there: the paws end just in front of the chest
        L1, L2 = REST[u]['L'], REST[l]['L']
        behind = math.sqrt(max(0.0, L1 * L1 - (H[1] - CROUCH_WRIST_Z) ** 2))
        wrist = (H[0] + behind - L2 * CROUCH_ARM_OUT, CROUCH_WRIST_Z)
        g = D(CROUCH_META)
        paw = (wrist[0] + REST[m]['L'] * math.cos(g), wrist[1] + REST[m]['L'] * math.sin(g))
        p.leg(u, l, m, t_, paw, CROUCH_META, False, CROUCH_TOES)
    plant(p, 'HL', CROUCH_FOOT[0], CROUCH_FOOT[1], CROUCH_FOOT[2], toe=180)
    plant(p, 'HR', CROUCH_FOOT[0], CROUCH_FOOT[1], CROUCH_FOOT[2], toe=180)
    chain_world(p, TAILS, CROUCH_TAIL, CROUCH_TAIL_WRAP)
    p.belly = CROUCH_BELLY

# the back sinks in the middle and the chest curves up onto the forearms (a Tripo model of Zaira crouching
# was the reference: compact, paws just in front of the chest, head close to the shoulders)
CROUCH_PITCH, CROUCH_SPINE, CROUCH_CHEST, CROUCH_NECK, CROUCH_HEAD = 0, 6, -14, -2, 6
CROUCH_FOOT = (-0.13, 0.0, -80)
# hips this low: the tail leaves them less steeply than sitting, or it goes through the floor
CROUCH_TAIL = [-45, -32, -4, 3, 3]
CROUCH_BELLY = (0.05, 1.15)   # lying on it, the belly skin comes down onto the floor and spreads a little
CROUCH_TAIL_WRAP = [0, 0, -45, -45, -35]
CROUCH_WRIST_Z, CROUCH_META, CROUCH_TOES = 0.05, 190, 185
CROUCH_ARM_OUT = 0.98   # the forearm a hair short of its length, so the elbow stays bent the right way
LOAF_DROP = -0.21

def loaf(p, t, f):
    """The 'loaf': belly on the ground, every paw hidden underneath, head up, tail wrapped along the flank; now and
    then the tip of the tail flicks (the only thing a loafing cat moves, besides the head)."""
    breathe = math.sin(TAU * LOAF_BREATHS * t)
    p.hips = (0.0, LOAF_DROP - LOAF_SINK + 0.003 * breathe)
    # a loafing cat is shorter than a standing one: the back rounds up and the rump tucks in under it
    p.x['Hips'] = LOAF_PITCH
    p.x['Spine'] = LOAF_SPINE
    p.x['Chest'] = LOAF_CHEST + breathe
    p.x['Neck'] = LOAF_NECK
    p.x['Head'] = LOAF_HEAD
    for key in ('FL', 'FR'):
        u, l, m, t_, fwd = LEGS[key]
        H = p.point(REST[u]['parent'], REST[u]['h'])
        # paws folded back inside the chest (wrist bent, paw pointing backwards, toes curled under), on the floor
        p.leg(u, l, m, t_, (H[0] + LOAF_PAW_BACK, LOAF_PAW_Z), 5, False, LOAF_TOES)
    # hind feet folded forward under the belly and drawn in towards the middle
    plant(p, 'HL', LOAF_FOOT_Y, LOAF_FOOT_Z, -80, toe=180)
    plant(p, 'HR', LOAF_FOOT_Y, LOAF_FOOT_Z, -80, toe=180)
    # tail drops to the ground and wraps forward along the flank; the tip flicks out, away from the body
    wrap = list(LOAF_TAIL_WRAP)
    for start, length, d4, d5 in LOAF_FLICKS:
        k = math.sin(math.pi * min(1.0, max(0.0, (t - start) / length))) ** 2
        wrap[3] += d4 * k
        wrap[4] += d5 * k
    chain_world(p, TAILS, LOAF_TAIL, wrap)
    p.belly = LOAF_BELLY
    p.squash = LOAF_SQUASH
    # the rig's body is too lean to hide tucked paws: they fold away small under the chest and thighs
    for name in ('Hand.L', 'Hand.R'): p.shrink[name] = LOAF_PAW_SHRINK
    for name in ('Foot.L', 'Foot.R'): p.shrink[name] = LOAF_FOOT_SHRINK

LOAF_NECK, LOAF_HEAD = -25, 10   # head up above the shoulders (the highest point of a loaf), nose level
# front paws folded back under the chest, hind feet tucked back into the thighs
LOAF_PAW_Z, LOAF_TOES, LOAF_FOOT_Z = 0.02, -70, -0.015   # sunk with the body (the game clips under the floor)
LOAF_PAW_BACK = 0.09
LOAF_BELLY = (0.05, 1.15)   # the loaf spreads sideways
LOAF_PITCH, LOAF_SPINE, LOAF_CHEST, LOAF_FOOT_Y = 0, 3, 10, 0.03
# (kept mild: shrinking more, together with the body drawing in, looked like the cat morphing)
LOAF_PAW_SHRINK, LOAF_FOOT_SHRINK = 0.5, 0.6
LOAF_SQUASH = 0.62   # the middle of the back draws in: a loaf is compact, much shorter than the cat lying out
# the whole loaf sits low, chest front on the floor over the paws: tilting the chest down instead dropped the
# head below the back like a tortoise's; the game clips whatever goes under the floor
LOAF_SINK = 0.08
# the tail drops straight down behind the rump and turns at once, forward along the flank: nothing sticks out
# behind her (the length of a loaf is nose to rump)
LOAF_TAIL = [-70, -85, -3, 2, 2]
LOAF_TAIL_WRAP = [0, 0, -80, -60, -25]
LOAF_FRAMES, LOAF_BREATHS = 240, 3    # an 8 s loop, so the flicks come now and then
# (start, length as fractions of the loop, degrees for Tail4, Tail5): one lazy flick, then a small double twitch
LOAF_FLICKS = [(0.19, 0.11, 20, 40), (0.62, 0.045, 0, 18), (0.68, 0.045, 0, 15)]

# curled up asleep: the back bends sideways into a ring (turns about each bone's own axis, the side-plane IK
# stays valid for the tucked legs), she rolls a little onto her side, head tucked in towards the hind legs
CURL_BEND, CURL_HEAD, CURL_ROLL = 60, 50, 15
# the tail drops to the ground first, then only the bones lying flat wrap round towards the nose
# (turning a steep bone sideways would lift the tail off the ground)
CURL_TAIL = [-66, -58, -4, 3, 3]
CURL_TAIL_WRAP = [0, 0, 60, 70, 70]

def sleep(p, t, f):
    """Curled up in a ball: belly down, back round, head on the hind legs, tail wrapped round to the nose."""
    breathe = math.sin(TAU * t)
    p.hips = (0.0, LOAF_DROP + 0.003 * breathe)
    p.x['Chest'] = breathe
    p.x['Neck'] = 30
    p.x['Head'] = 25
    p.yz['Hips'] = (CURL_ROLL, -CURL_BEND * 0.6)     # turned so the ring sits over her spot
    p.yz['Spine'] = (0.0, CURL_BEND)
    p.yz['Chest'] = (0.0, CURL_BEND)
    p.yz['Neck'] = (0.0, CURL_HEAD)
    p.yz['Head'] = (0.0, CURL_HEAD)
    for key in ('FL', 'FR'):
        u, l, m, t_, fwd = LEGS[key]
        H = p.point(REST[u]['parent'], REST[u]['h'])
        p.leg(u, l, m, t_, (H[0] + 0.03, 0.07), 5, False, None)      # paws folded back under the chest
    plant(p, 'HL', 0.02, 0.05, -80, toe=180)
    plant(p, 'HR', 0.02, 0.05, -80, toe=180)
    wrap = list(CURL_TAIL_WRAP)
    wrap[-1] += 4 * math.sin(TAU * t + 1.0)          # the tip stirs with the breath
    chain_world(p, TAILS, CURL_TAIL, wrap)

# ---------------------------------------------------------------- transitions between resting poses
# One-shot clips from the exact end pose of one clip to the start pose of the next, at a cat's pace: each part
# of the body moves in its own stretch of the clip, and the paws step (lifted a little, one after the other)
# instead of sliding all together. Lengths must match CatBrain.LieDownTime / TuckTime.
LIE_DOWN_S, TUCK_S = 2.0, 1.6
LEG_BONES = {b for leg in LEGS.values() for b in leg[:4] if b}

def snapshot(fn):
    """The pose a clip starts with (frame 0, no breathing)."""
    q = Pose()
    fn(q, 0.0, 0)
    return q

def world_leg(q, key):
    """Where a pose puts a paw (side plane), the world angle of its metatarsus and of its toe."""
    u, l, m, toe, fwd = LEGS[key]
    paw = q.point(m, REST[m]['t'])
    meta = math.degrees(REST[m]['a'] + q.cum(m))
    toe_a = math.degrees(REST[toe]['a'] + q.cum(toe)) if toe else None
    return paw, meta, toe_a

def near(a, b):
    """b turned by whole turns to lie within half a turn of a (angles lerp the short way)."""
    return b + 360 * round((a - b) / 360)

def stage(t, t0, t1):
    """0 before t0, 1 after t1, smooth in between: when a part of the body moves."""
    return smooth((t - t0) / (t1 - t0))

def blend_body(p, a, b, parts):
    """Every bone but the legs from pose a to b, each group at its own time: parts = [(bones, weight)];
    bones not listed follow 'rest' (the last entry with bones None)."""
    default = next(w for bones_, w in parts if bones_ is None)
    for name in REST:
        if name in LEG_BONES: continue
        w = next((w for bones_, w in parts if bones_ and name in bones_), default)
        p.x[name] = lerp(a.x.get(name, 0.0), b.x.get(name, 0.0), w)
        ya, za = a.yz.get(name, (0.0, 0.0)); yb, zb = b.yz.get(name, (0.0, 0.0))
        p.yz[name] = (lerp(ya, yb, w), lerp(za, zb, w))
    hips_w = next((w for bones_, w in parts if bones_ and 'hips' in bones_), default)
    p.hips = (lerp(a.hips[0], b.hips[0], hips_w), lerp(a.hips[1], b.hips[1], hips_w))
    p.belly = (lerp(a.belly[0], b.belly[0], hips_w), lerp(a.belly[1], b.belly[1], hips_w))
    p.squash = lerp(a.squash, b.squash, hips_w)
    for name in set(a.shrink) | set(b.shrink):   # paws shrink as they go under: with the legs, not the body
        p.shrink[name] = lerp(a.shrink.get(name, 1.0), b.shrink.get(name, 1.0), default)
    for name in LEG_BONES:   # sideways turns of the legs (knees apart when sitting) blend with the body
        ya, za = a.yz.get(name, (0.0, 0.0)); yb, zb = b.yz.get(name, (0.0, 0.0))
        p.yz[name] = (lerp(ya, yb, default), lerp(za, zb, default))

def step_leg(p, key, a, b, k, lift, meta_to=None):
    """A paw going from where pose a has it to where pose b has it as k goes 0 -> 1, lifted `lift` on the way.
    meta_to: the end angle of the metatarsus when it must turn the long way round (a paw curling under)."""
    u, l, m, toe, fwd = LEGS[key]
    (pa, ma, ta), (pb, mb, tb) = world_leg(a, key), world_leg(b, key)
    mb = meta_to if meta_to is not None else near(ma, mb)
    paw = (lerp(pa[0], pb[0], k), lerp(pa[1], pb[1], k) + lift * math.sin(math.pi * k))
    toe_a = lerp(ta, near(ta, tb), k) if toe else None
    p.leg(u, l, m, toe, paw, lerp(ma, mb, k), fwd, toe_a)

BODY = {'Hips', 'Spine', 'Chest', 'hips'}
HEAD = {'Neck', 'Head'}

def lie_down(p, t, f):
    """Sitting -> lying with the paws out: the front paws walk forward one after the other while the chest
    comes down, the elbows fold, then the hind legs settle alongside and the tail follows."""
    a, b = snapshot(sit), snapshot(crouch)
    blend_body(p, a, b, [(BODY, stage(t, 0.15, 0.85)), (HEAD, stage(t, 0.1, 0.9)),
                         (set(TAILS), stage(t, 0.55, 1.0)), (None, stage(t, 0.5, 1.0))])
    step_leg(p, 'FL', a, b, stage(t, 0.05, 0.45), 0.03)
    step_leg(p, 'FR', a, b, stage(t, 0.3, 0.7), 0.03)
    step_leg(p, 'HL', a, b, stage(t, 0.55, 1.0), 0.01)
    step_leg(p, 'HR', a, b, stage(t, 0.6, 1.0), 0.01)

TUCK_CHEST, TUCK_BODY = (0.1, 0.95), (0.1, 0.95)   # when the chest comes down, when the body settles

def tuck_paws(front_times):
    """Lying with the paws out -> the loaf: each front paw lifts, curls under at the wrist (the long way
    round, pointing down on the way) and slides back under the chest; the body rises onto them and settles."""
    def fn(p, t, f):
        a, b = snapshot(crouch), snapshot(loaf)
        # the chest comes down over the paws as they go under it, and only then does the body settle to the
        # loaf's height (rising first would lift her front off the floor on half-folded paws)
        blend_body(p, a, b, [({'Chest'}, stage(t, TUCK_CHEST[0], TUCK_CHEST[1])), ({'Hips', 'Spine', 'hips'}, stage(t, TUCK_BODY[0], TUCK_BODY[1])),
                             (HEAD, stage(t, 0.2, 0.8)), (set(TAILS), stage(t, 0.3, 1.0)), (None, stage(t, 0.4, 1.0))])
        for key, (t0, t1) in front_times.items():
            ma = world_leg(a, key)[1]
            mb = world_leg(b, key)[1]
            # curl downwards: the end angle past the start one going clockwise in the side plane
            curl = mb + 360 * math.ceil((ma - mb) / 360) if mb < ma else mb
            # slid under, not lifted: a paw raised under the chest reads as the cat standing on it
            step_leg(p, key, a, b, stage(t, t0, t1), 0.0, meta_to=curl)
            # the paw goes out of sight as it slides under the chest, in the second half of its move
            hand = LEGS[key][2]
            p.shrink[hand] = lerp(1.0, b.shrink.get(hand, 1.0), stage(t, (t0 + t1) / 2, t1))
        step_leg(p, 'HL', a, b, stage(t, 0.45, 1.0), 0.0)
        step_leg(p, 'HR', a, b, stage(t, 0.5, 1.0), 0.0)
    return fn

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
bake("Run", RUN_FRAMES, run)
bake("Sprint", SPRINT_FRAMES, sprint)
bake("Trot", 18, trot)
bake("Stalk", 60, stalk)
bake("Wiggle", 15, wiggle)
bake("Swat", 15, swat, loop=False)
bake("Prejump", 13, prejump, loop=False)
bake("Aim", 15, aim)
bake("Jump", 10, jump, loop=False)
bake("Fall", 9, fall, loop=False)
bake("Land", 9, land, loop=False)
bake_both_sides("Sit", 90, sit)
bake_both_sides("Crouch", 90, crouch)
bake_both_sides("Loaf", LOAF_FRAMES, loaf)
bake_both_sides("LieDown", round(LIE_DOWN_S * FPS), lie_down, loop=False)
# one paw after the other (folding both at once lifted her front off the floor on the half-curled arms)
bake_both_sides("Tuck", round(TUCK_S * FPS), tuck_paws({'FL': (0.05, 0.45), 'FR': (0.5, 0.9)}), loop=False)
bake("Sleep", 120, sleep)
bake("Eat", 40, eat)
bake("Meow", 30, meow, loop=False)
bake("Held", 60, held)
arm.animation_data.action = bpy.data.actions["Idle"]
bpy.ops.wm.save_as_mainfile(filepath=work_file("anim.blend"))
