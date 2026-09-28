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
    /// <summary>World-space point to look at.</summary>
    public Vector3 Target;
    /// <summary>0 = the animation alone, 1 = fully turned towards <see cref="Target"/>.</summary>
    public float Weight;
    /// <summary>Where the head ended up this frame (world): the posed skeleton is reset after drawing, so
    /// reading it later shows the animation alone.</summary>
    public Vector3 HeadPos, HeadDir;

    // The neck takes most of a turn, as a cat's does: a head turning alone on a still neck squashed the cheek on
    // the side it turned to against the neck.
    const float NeckShare = 0.6f;
    const float ChestShare = 0.2f;
    static readonly float ChestLimit = Mathf.DegToRad(15);
    static readonly float NeckLimit = Mathf.DegToRad(45);
    static readonly float HeadLimit = Mathf.DegToRad(50);

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Head < 0 || Weight <= 0.001f) return;
        var target = sk.GlobalTransform.AffineInverse() * Target;
        if (Chest >= 0)
        {
            var kept = new Transform3D[Keep.Length];
            for (int i = 0; i < Keep.Length; i++) kept[i] = sk.GetBoneGlobalPose(Keep[i]);
            Turn(sk, Chest, target, ChestShare * Weight, ChestLimit);
            for (int i = 0; i < Keep.Length; i++)
            {
                var parent = sk.GetBoneGlobalPose(sk.GetBoneParent(Keep[i]));
                var local = parent.AffineInverse() * kept[i];
                sk.SetBonePoseRotation(Keep[i], local.Basis.GetRotationQuaternion());
                sk.SetBonePosePosition(Keep[i], local.Origin);
            }
        }
        if (Neck >= 0) Turn(sk, Neck, target, NeckShare * Weight, NeckLimit);
        // the head keeps its eyes level: turning the shortest way to a point up and to the side would tilt it
        var up = (sk.GlobalTransform.Basis.Inverse() * Vector3.Up).Normalized();
        Turn(sk, Head, target, Weight, HeadLimit, up);
        var g = sk.GetBoneGlobalPose(Head);
        HeadPos = sk.GlobalTransform * g.Origin;
        HeadDir = (sk.GlobalTransform.Basis * g.Basis.Y).Normalized();
    }

    /// <summary>Rotate a bone so its axis (it points along +Y, towards the nose) swings towards the target.</summary>
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
