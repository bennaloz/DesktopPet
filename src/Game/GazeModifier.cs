using Godot;

namespace ZairaPet.Game;

/// <summary>
/// Turns neck and head towards a point after the animation has posed the skeleton, so any clip (sitting,
/// walking, crouching to jump) can look at the viewer, the cursor or the landing spot.
/// The neck takes part of the turn, the head the rest; each is limited so the cat never breaks its neck.
/// </summary>
public partial class GazeModifier : SkeletonModifier3D
{
    public int Neck = -1;
    public int Head = -1;
    /// <summary>Optional: the chest turns a little too (the shoulders follow the look)...</summary>
    public int Chest = -1;
    /// <summary>...while these bones under it (the shoulder blades) stay where they were, so the legs do not move.</summary>
    public int[] Keep = System.Array.Empty<int>();
    /// <summary>Bones hanging off the head (lop ears): they turn with the head sideways, but not up or down.</summary>
    public int[] Hang = System.Array.Empty<int>();
    /// <summary>How far (radians) the head may be tipped up above where the clip holds it.</summary>
    public float LookUp = Mathf.Pi;
    /// <summary>How much of a look up the neck takes, of its usual share.</summary>
    public float NeckUpShare = 1f;
    /// <summary>How far (radians) the head may be turned round to the side from where the clip points it.</summary>
    public float MaxTurn = Mathf.Pi;
    /// <summary>World-space point to look at.</summary>
    public Vector3 Target;
    /// <summary>0 = the animation alone, 1 = fully turned towards <see cref="Target"/>.</summary>
    public float Weight;
    /// <summary>Where the head ended up this frame (world): the posed skeleton is reset after drawing, so
    /// reading it later shows the animation alone.</summary>
    public Vector3 HeadPos, HeadDir;
    /// <summary>Where the head was sent to look this frame (world direction from the head): the target, or lower
    /// or less far round if that was beyond <see cref="LookUp"/> or <see cref="MaxTurn"/>.</summary>
    public Vector3 AimDir;

    // The neck takes most of a turn, as a cat's does: a head turning alone on a still neck squashed the cheek on
    // the side it turned to against the neck.
    const float NeckShare = 0.6f;
    const float ChestShare = 0.2f;
    /// <summary>How much of a downward look the neck takes: pointing the rising neck straight at something at
    /// head height sank the head into the shoulders; the head does the looking down.</summary>
    const float NeckDown = 0.25f;
    static readonly float ChestLimit = Mathf.DegToRad(15);
    // Together about 115 degrees: enough to look straight up; further round she turns her body (CatBrain).
    static readonly float NeckLimit = Mathf.DegToRad(70);
    static readonly float HeadLimit = Mathf.DegToRad(70);

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Head < 0 || Weight <= 0.001f) return;
        var target = sk.GlobalTransform.AffineInverse() * Target;
        var up = Up(sk);
        var head0 = sk.GetBoneGlobalPose(Head);
        target = LimitUp(target, head0.Origin, head0.Basis.Y.Normalized(), up, LookUp);
        target = LimitTurn(target, head0.Origin, head0.Basis.Y.Normalized(), up, MaxTurn);
        AimDir = (sk.GlobalTransform.Basis * (target - head0.Origin)).Normalized();
        var hung = new Basis[Hang.Length];
        for (int i = 0; i < Hang.Length; i++) hung[i] = sk.GetBoneGlobalPose(Hang[i]).Basis.Orthonormalized();
        if (Chest >= 0)
        {
            var kept = new Transform3D[Keep.Length];
            for (int i = 0; i < Keep.Length; i++) kept[i] = sk.GetBoneGlobalPose(Keep[i]);
            TurnAround(sk, Chest, target, ChestShare * Weight, ChestLimit, Up(sk), 0);
            for (int i = 0; i < Keep.Length; i++)
            {
                var parent = sk.GetBoneGlobalPose(sk.GetBoneParent(Keep[i]));
                var local = parent.AffineInverse() * kept[i];
                sk.SetBonePoseRotation(Keep[i], local.Basis.GetRotationQuaternion());
                sk.SetBonePosePosition(Keep[i], local.Origin);
            }
        }
        if (Neck >= 0) TurnAround(sk, Neck, target, NeckShare * Weight, NeckLimit, up, NeckDown, NeckUpShare);
        // the head keeps its eyes level: turning the shortest way to a point up and to the side would tilt it
        Turn(sk, Head, target, Weight, HeadLimit, up);
        var g = sk.GetBoneGlobalPose(Head);
        if (Hang.Length > 0)
        {
            // the hanging bones as the clip had them, turned only as far round the vertical as the head turned
            var fh = head0.Basis.Y - up * head0.Basis.Y.Dot(up);
            var th = g.Basis.Y - up * g.Basis.Y.Dot(up);
            float yaw = fh.LengthSquared() > 1e-6f && th.LengthSquared() > 1e-6f
                ? Mathf.Atan2(up.Dot(fh.Cross(th)), fh.Dot(th)) : 0f;
            for (int i = 0; i < Hang.Length; i++)
            {
                var world = new Basis(up, yaw) * hung[i];
                var parentBasis = sk.GetBoneGlobalPose(sk.GetBoneParent(Hang[i])).Basis.Orthonormalized();
                sk.SetBonePoseRotation(Hang[i], (parentBasis.Inverse() * world).GetRotationQuaternion());
            }
        }
        HeadPos = sk.GlobalTransform * g.Origin;
        HeadDir = (sk.GlobalTransform.Basis * g.Basis.Y).Normalized();
    }

    /// <summary>Rotate a bone so its axis (it points along +Y, towards the nose) swings towards the target.</summary>
    static Vector3 Up(Skeleton3D sk) => (sk.GlobalTransform.Basis.Inverse() * Vector3.Up).Normalized();

    /// <summary>The target brought down, if need be, so that looking at it from <paramref name="from"/> tips the
    /// direction <paramref name="dir"/> up by at most <paramref name="maxUp"/> radians (to the side it stays).</summary>
    static Vector3 LimitUp(Vector3 target, Vector3 from, Vector3 dir, Vector3 up, float maxUp)
    {
        var to = target - from;
        float dist = to.Length();
        if (dist < 1e-6f) return target;
        to /= dist;
        float now = Mathf.Asin(Mathf.Clamp(dir.Dot(up), -1, 1));
        float want = Mathf.Asin(Mathf.Clamp(to.Dot(up), -1, 1));
        if (want - now <= maxUp) return target;
        // which way round: the target's, unless it is nearly overhead, where that is chance (a point just behind
        // would turn the head round to look backwards): then the way the head already faces
        var ft = to - up * to.Dot(up);
        var fd = dir - up * dir.Dot(up);
        var flat = (ft.LengthSquared() > 1e-8f ? ft.Normalized() * Mathf.Cos(want) : Vector3.Zero)
                 + (fd.LengthSquared() > 1e-8f ? fd.Normalized() * Mathf.Sin(want) * 0.5f : Vector3.Zero);
        if (flat.LengthSquared() < 1e-8f) return target;
        float e = Mathf.Min(now + maxUp, Mathf.Pi / 2);
        return from + (flat.Normalized() * Mathf.Cos(e) + up * Mathf.Sin(e)) * dist;
    }

    /// <summary>The target swung back round the vertical, if need be, so that looking at it from
    /// <paramref name="from"/> turns the direction <paramref name="dir"/> by at most <paramref name="maxTurn"/>
    /// radians to the side (as high up, as far away).</summary>
    static Vector3 LimitTurn(Vector3 target, Vector3 from, Vector3 dir, Vector3 up, float maxTurn)
    {
        var to = target - from;
        var ft = to - up * to.Dot(up);
        var fd = dir - up * dir.Dot(up);
        if (ft.LengthSquared() < 1e-8f || fd.LengthSquared() < 1e-8f) return target;
        float side = Mathf.Atan2(up.Dot(fd.Cross(ft)), fd.Dot(ft));
        if (Mathf.Abs(side) <= maxTurn) return target;
        return from + new Basis(up, (Mathf.Sign(side) * maxTurn) - side) * to;
    }

    /// <summary>
    /// Turn a bone the way a neck or a chest turns: round the vertical first (to the side), then up (by
    /// <paramref name="upShare"/> of what it would take); down only by <paramref name="downShare"/>. Each part
    /// limited and scaled by <paramref name="amount"/>.
    /// </summary>
    static void TurnAround(Skeleton3D sk, int bone, Vector3 target, float amount, float limit, Vector3 up, float downShare,
                           float upShare = 1f)
    {
        var g = sk.GetBoneGlobalPose(bone);
        var from = g.Basis.Y.Normalized();
        var to = (target - g.Origin).Normalized();
        var basis = g.Basis;
        var fh = from - up * from.Dot(up);
        var th = to - up * to.Dot(up);
        if (fh.LengthSquared() > 1e-6f && th.LengthSquared() > 1e-6f)
        {
            float yaw = Mathf.Atan2(up.Dot(fh.Cross(th)), fh.Dot(th));
            basis = new Basis(up, Mathf.Clamp(yaw, -limit, limit) * amount) * basis;
        }
        var f2 = basis.Y.Normalized();
        float pitch = Mathf.Asin(Mathf.Clamp(to.Dot(up), -1, 1)) - Mathf.Asin(Mathf.Clamp(f2.Dot(up), -1, 1));
        pitch *= pitch < 0 ? downShare : upShare;
        var side = f2.Cross(up);
        if (side.LengthSquared() > 1e-6f)   // turning about f2 x up lifts f2
            basis = new Basis(side.Normalized(), Mathf.Clamp(pitch, -limit, limit) * amount) * basis;
        int parent = sk.GetBoneParent(bone);
        var parentBasis = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        sk.SetBonePoseRotation(bone, (parentBasis.Inverse() * basis.Orthonormalized()).GetRotationQuaternion());
    }

    /// <param name="levelUp">If given (skeleton space), the bone's side axis (its X: across the eyes) is brought back
    /// level with the floor after the turn, as much as <paramref name="amount"/>.</param>
    static void Turn(Skeleton3D sk, int bone, Vector3 target, float amount, float limit, Vector3? levelUp = null)
    {
        var g = sk.GetBoneGlobalPose(bone);
        var from = g.Basis.Y.Normalized();
        var to = (target - g.Origin).Normalized();
        var axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-10f) return;
        float angle = Mathf.Min(from.AngleTo(to), limit) * amount;
        var turned = new Basis(axis.Normalized(), angle) * g.Basis;
        if (levelUp is { } upv)
        {
            var y = turned.Y.Normalized();
            var x = y.Cross(upv);
            if (x.LengthSquared() > 1e-6f)
            {
                x = x.Normalized();
                if (x.Dot(turned.X) < 0) x = -x;
                var level = new Basis(x, y, x.Cross(y)).Orthonormalized();
                var q = turned.Orthonormalized().GetRotationQuaternion().Slerp(level.GetRotationQuaternion(), Mathf.Clamp(amount, 0, 1));
                turned = new Basis(q);
            }
        }
        int parent = sk.GetBoneParent(bone);
        var parentBasis = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        sk.SetBonePoseRotation(bone, (parentBasis.Inverse() * turned).GetRotationQuaternion());
    }
}
