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
    /// <summary>World-space point to look at.</summary>
    public Vector3 Target;
    /// <summary>0 = the animation alone, 1 = fully turned towards <see cref="Target"/>.</summary>
    public float Weight;
    /// <summary>Where the head ended up this frame (world): the posed skeleton is reset after drawing, so
    /// reading it later shows the animation alone.</summary>
    public Vector3 HeadPos, HeadDir;

    const float NeckShare = 0.4f;
    static readonly float NeckLimit = Mathf.DegToRad(35);
    static readonly float HeadLimit = Mathf.DegToRad(60);

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Head < 0 || Weight <= 0.001f) return;
        var target = sk.GlobalTransform.AffineInverse() * Target;
        if (Neck >= 0) Turn(sk, Neck, target, NeckShare * Weight, NeckLimit);
        Turn(sk, Head, target, Weight, HeadLimit);
        var g = sk.GetBoneGlobalPose(Head);
        HeadPos = sk.GlobalTransform * g.Origin;
        HeadDir = (sk.GlobalTransform.Basis * g.Basis.Y).Normalized();
    }

    /// <summary>Rotate a bone so its axis (it points along +Y, towards the nose) swings towards the target.</summary>
    static void Turn(Skeleton3D sk, int bone, Vector3 target, float amount, float limit)
    {
        var g = sk.GetBoneGlobalPose(bone);
        var from = g.Basis.Y.Normalized();
        var to = (target - g.Origin).Normalized();
        var axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-10f) return;
        float angle = Mathf.Min(from.AngleTo(to), limit) * amount;
        var turned = new Basis(axis.Normalized(), angle) * g.Basis;
        int parent = sk.GetBoneParent(bone);
        var parentBasis = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        sk.SetBonePoseRotation(bone, (parentBasis.Inverse() * turned).GetRotationQuaternion());
    }
}
