using System;
using Godot;
using ZairaPet.Core;

namespace ZairaPet.Game;

/// <summary>
/// Poses the tail after the animation, from the mood (<see cref="TailMoods"/>): each tail bone is pointed at an
/// angle measured from the body (straight back along the hips, up, or bent over forwards) and waved sideways, so
/// the tail keeps its meaning whatever the gait and however the body pitches. Blended over the clip's own tail by
/// <see cref="Weight"/>: on while she walks or runs, off at rest, where the poses curl the tail themselves.
/// The tail is not a stick: each bone follows its angle with a little lag, more towards the tip, and dips as the
/// body springs up and flicks up as it comes down, so the tip trails the bounds of a gallop like a whip.
/// </summary>
public partial class TailModifier : SkeletonModifier3D
{
    public int Hips = -1;
    public int[] Tail = Array.Empty<int>();
    /// <summary>0 = the clip's tail, 1 = the mood's.</summary>
    public float Weight;
    /// <summary>The mood's shape being shown: eased towards the mood's own over a second or so.</summary>
    public double[] Lift = new double[5];
    public double WaveDeg, WaveHz, TipDeg, TipHz;
    public bool TipJerky;
    public double Time;

    Vector3 _upInHips, _sideInHips;
    bool _ready;
    // follow-through: per bone how fast (1/s) the shown direction catches up with the wanted one, root to tip
    static readonly float[] Catch = { 30f, 20f, 14f, 10f, 8f };
    const float BounceDeg = 14f;          // degrees per m/s of the hips going up (skeleton units), at the tip
    Vector3[] _shown = Array.Empty<Vector3>();
    Vector3 _lastHips;
    float _bounce;
    bool _running;

    /// <summary>Remember which way "up" and "to her left" are in the hips' own frame, from the rest pose.</summary>
    public void Setup(Skeleton3D sk)
    {
        var rest = sk.GetBoneGlobalRest(Hips).Basis.Orthonormalized().Inverse();
        _upInHips = (rest * Vector3.Up).Normalized();
        _sideInHips = (rest * Vector3.Right).Normalized();
        _ready = true;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || !_ready || Tail.Length == 0) return;
        if (Weight <= 0.001f) { _running = false; return; }
        var hips = sk.GetBoneGlobalPose(Hips).Basis.Orthonormalized();
        var back = -hips.Y.Normalized();
        var side = (hips * _sideInHips).Normalized();
        var up = side.Cross(back).Normalized();
        if (up.Dot(hips * _upInHips) < 0) up = -up;

        // the body's bounce: going up the tail is left behind (dips), coming down it flicks up
        var hipsAt = sk.GetBoneGlobalPose(Hips).Origin;
        bool fresh = !_running || _shown.Length != Tail.Length;
        float dt = Mathf.Max((float)delta, 1e-4f);
        float rise = fresh ? 0f : (hipsAt - _lastHips).Dot(Vector3.Up) / dt;
        _lastHips = hipsAt;
        _bounce = Mathf.Lerp(_bounce, Mathf.Clamp(-rise * BounceDeg, -20f, 20f), 1 - Mathf.Exp(-dt * 20f));
        if (fresh) { _shown = new Vector3[Tail.Length]; _bounce = 0f; }
        _running = true;

        var sideways = TailMoods.Sideways(new TailShape(Lift, WaveDeg, WaveHz, TipDeg, TipHz, TipJerky), Time);
        for (int i = 0; i < Tail.Length && i < Lift.Length; i++)
        {
            float a = Mathf.DegToRad((float)Lift[i] + _bounce * (0.2f + 0.2f * i));
            var dir = (back * Mathf.Cos(a) + up * Mathf.Sin(a)).Normalized();
            // swing towards her left (+) or right (-) about the axis that moves the bone sideways
            var axis = dir.Cross(side);
            if (axis.LengthSquared() > 1e-6f) dir = dir.Rotated(axis.Normalized(), Mathf.DegToRad((float)sideways[i]));
            // follow through: catch up with the wanted direction a little late, the tip later than the root
            if (fresh) _shown[i] = dir;
            else _shown[i] = _shown[i].Slerp(dir, 1 - Mathf.Exp(-dt * Catch[Math.Min(i, Catch.Length - 1)])).Normalized();
            dir = _shown[i];

            int bone = Tail[i];
            var g = sk.GetBoneGlobalPose(bone).Basis.Orthonormalized();
            var from = g.Y.Normalized();
            if (from.Dot(dir) > 0.99999f) continue;
            var target = new Quaternion(from, dir) * g.GetRotationQuaternion();
            var shown = g.GetRotationQuaternion().Slerp(target.Normalized(), Weight);
            int parent = sk.GetBoneParent(bone);
            var parentRot = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Orthonormalized().GetRotationQuaternion() : Quaternion.Identity;
            sk.SetBonePoseRotation(bone, (parentRot.Inverse() * shown).Normalized());
        }
    }
}
