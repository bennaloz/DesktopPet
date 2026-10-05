using Godot;

namespace ZairaPet.Game;

/// <summary>
/// Paws that stay where they were put down. The clips move a planted paw back under the body at the speed the clip
/// was made for; the game plays them faster or slower with her real speed, but not exactly (the playback is clamped,
/// she speeds up and slows down, one clip blends into the next), and the difference showed as paws skating on the
/// floor. After the animation, every paw the clip has on the floor is held on the spot where it landed (across the
/// screen; depth does not show) by bending the leg: a two-bone IK of the upper and lower leg moves the ankle by as
/// much as the paw has to go back, and the paw keeps the clip's angle. As the clip lifts it the hold fades out and
/// the paw catches up during the swing. It never holds a paw more than <see cref="MaxSlip"/> away from the clip's
/// own place (a turn on the spot, a fast stop): beyond that it lets it slide.
/// </summary>
public partial class FootLockModifier : SkeletonModifier3D
{
    /// <summary>Each leg: upper, lower, paw, and the bone whose head touches the floor (the toes; the paw if none).</summary>
    public int[][] Legs = System.Array.Empty<int[]>();
    /// <summary>World height of the floor under her (the feet are on the node's origin); set by the game.</summary>
    public float FloorY;
    /// <summary>She stands on a surface (set by the game): only then are paws on the floor.</summary>
    public bool OnFloor;
    /// <summary>Hold the planted paws (off: only measure them, for the self test).</summary>
    public bool Hold = true;
    /// <summary>How far above its height in the standing rest pose a paw still counts as on the floor (world px).</summary>
    public float Tolerance = 4;
    /// <summary>The farthest a paw is held from where the clip puts it (world px).</summary>
    public float MaxSlip = 15;
    /// <summary>Her speed across the screen (px/s; set by the game): a paw the clip moves across the screen much
    /// slower than she goes is planted, one that moves along with her or faster is swinging, however low it glides.</summary>
    public float BodySpeed;

    sealed class Leg
    {
        public bool Planted;
        public float LockX, Shift, Weight;
        public float LastX, ClipX; public bool WasPlanted;
    }
    Leg[] _state = System.Array.Empty<Leg>();

    /// <summary>For the self test: how far planted paws moved across the screen, and for how long they were down.</summary>
    public float SlipPx { get; private set; }
    public float PlantedSeconds { get; private set; }
    /// <summary>For the self test: frames a paw was further from its spot than <see cref="MaxSlip"/>.</summary>
    public int Slipped { get; private set; }
    public void ResetStats() { SlipPx = 0; PlantedSeconds = 0; Slipped = 0; }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null) return;
        float dt = (float)delta;
        if (_state.Length != Legs.Length) { _state = new Leg[Legs.Length]; for (int i = 0; i < Legs.Length; i++) _state[i] = new Leg(); }
        var toWorld = sk.GlobalTransform;
        for (int i = 0; i < Legs.Length; i++)
        {
            var leg = Legs[i]; var s = _state[i];
            int contact = leg[^1];
            var q = toWorld * sk.GetBoneGlobalPose(contact).Origin;
            float restH = (toWorld * sk.GetBoneGlobalRest(contact).Origin).Y - FloorY;
            float clipV = dt > 0 ? Mathf.Abs(q.X - s.ClipX) / dt : 0;
            s.ClipX = q.X;
            bool down = OnFloor && q.Y - FloorY < restH + Tolerance && clipV < 0.6f * BodySpeed + 20;
            if (down && !s.Planted) s.LockX = q.X;
            s.Planted = down;
            if (down)
            {
                float want = s.LockX - q.X;
                if (Mathf.Abs(want) > MaxSlip) Slipped++;
                s.Shift = Mathf.Clamp(want, -MaxSlip, MaxSlip);
            }
            // grip at once, let go over the start of the swing
            s.Weight = down ? 1 : Mathf.MoveToward(s.Weight, 0, dt * 8);
            float shift = Hold ? s.Shift * s.Weight : 0;
            if (Mathf.Abs(shift) > 0.01f) Reach(sk, leg, toWorld.AffineInverse().Basis * new Vector3(shift, 0, 0));

            float x = (toWorld * sk.GetBoneGlobalPose(contact).Origin).X;
            if (down && s.WasPlanted) { SlipPx += Mathf.Abs(x - s.LastX); PlantedSeconds += dt; }
            s.LastX = x; s.WasPlanted = down;
        }
    }

    /// <summary>Bend upper and lower leg so the paw moves by <paramref name="by"/> (skeleton space), the knee staying in
    /// the plane it bends in; the paw keeps its angle.</summary>
    static void Reach(Skeleton3D sk, int[] leg, Vector3 by)
    {
        int upper = leg[0], lower = leg[1], paw = leg[2];
        var gu = sk.GetBoneGlobalPose(upper); var gl = sk.GetBoneGlobalPose(lower); var gp = sk.GetBoneGlobalPose(paw);
        Vector3 a = gu.Origin, b = gl.Origin, c = gp.Origin, t = c + by;
        float l1 = a.DistanceTo(b), l2 = b.DistanceTo(c);
        if (l1 < 1e-5f || l2 < 1e-5f) return;
        var d = t - a; float dist = d.Length();
        if (dist < 1e-5f) return;
        var dir = d / dist;
        dist = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 1e-4f, (l1 + l2) * 0.999f);
        var bend = (b - a) - dir * (b - a).Dot(dir);
        if (bend.LengthSquared() < 1e-10f) return;
        bend = bend.Normalized();
        float cos = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1);
        var b2 = a + l1 * (cos * dir + Mathf.Sqrt(1 - cos * cos) * bend);
        var c2 = a + dir * dist;

        var r1 = Turn(b - a, b2 - a);
        var gu2 = new Transform3D(new Basis(r1) * gu.Basis, a);
        var r2 = Turn(new Basis(r1) * (c - b), c2 - b2);
        var gl2 = new Transform3D(new Basis(r2 * r1) * gl.Basis, b2);

        int parent = sk.GetBoneParent(upper);
        var gparent = parent >= 0 ? sk.GetBoneGlobalPose(parent) : Transform3D.Identity;
        SetGlobalBasis(sk, upper, gparent, gu2);
        SetGlobalBasis(sk, lower, gu2, gl2);
        SetGlobalBasis(sk, paw, gl2, new Transform3D(gp.Basis, c2));
    }

    static Quaternion Turn(Vector3 from, Vector3 to)
    {
        from = from.Normalized(); to = to.Normalized();
        var axis = from.Cross(to);
        float s = axis.Length(), c = from.Dot(to);
        if (s < 1e-7f) return Quaternion.Identity;
        return new Quaternion(axis / s, Mathf.Atan2(s, c));
    }

    static void SetGlobalBasis(Skeleton3D sk, int bone, Transform3D parentGlobal, Transform3D global)
    {
        var local = parentGlobal.Basis.Inverse() * global.Basis;
        sk.SetBonePoseRotation(bone, local.GetRotationQuaternion());
    }
}
