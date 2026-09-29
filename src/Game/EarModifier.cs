using System;
using Godot;

namespace ZairaPet.Game;

/// <summary>
/// Floppy ears after the animation: each ear bone points where the clip says, but through a damped spring, so the
/// ears lag behind the head and bounce: a hop tosses them up, landing drops them, a turn swings them. The root
/// follows closely, the tips loosely. The swing is kept small (<see cref="MaxDeg"/>): in the rabbit's mesh the
/// lop ears are the sides of the head, and a big swing would open the face.
/// </summary>
public partial class EarModifier : SkeletonModifier3D
{
    /// <summary>Ear chains, root first (one per ear).</summary>
    public int[][] Ears = Array.Empty<int[]>();
    /// <summary>The bone whose motion tosses the ears (the head).</summary>
    public int Head = -1;
    public float MaxDeg = 12f;
    /// <summary>Spring stiffness (1/s²) root to tip, and damping (1/s).</summary>
    static readonly float[] Stiff = { 260f, 170f, 120f, 90f };
    const float Damp = 9f;
    /// <summary>How much the head's acceleration (model metres/s²) pushes the ears: a hop's bob of a few
    /// centimetres, twice a second, swings the tips by several degrees.</summary>
    const float Toss = 4f;

    Vector3[][] _dir = Array.Empty<Vector3[]>();
    Vector3[][] _vel = Array.Empty<Vector3[]>();
    Vector3 _lastHead, _lastHeadVel;
    bool _started;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Ears.Length == 0 || Head < 0) return;
        float dt = Mathf.Clamp((float)delta, 1e-4f, 0.05f);
        var head = sk.GetBoneGlobalPose(Head).Origin;
        var headVel = _started ? (head - _lastHead) / dt : Vector3.Zero;
        var headAcc = _started ? (headVel - _lastHeadVel) / dt : Vector3.Zero;
        _lastHead = head;
        _lastHeadVel = headVel;
        if (!_started || _dir.Length != Ears.Length)
        {
            _dir = new Vector3[Ears.Length][];
            _vel = new Vector3[Ears.Length][];
            for (int e = 0; e < Ears.Length; e++) { _dir[e] = new Vector3[Ears[e].Length]; _vel[e] = new Vector3[Ears[e].Length]; }
        }

        for (int e = 0; e < Ears.Length; e++)
            for (int i = 0; i < Ears[e].Length; i++)
            {
                int bone = Ears[e][i];
                var g = sk.GetBoneGlobalPose(bone).Basis.Orthonormalized();
                var want = g.Y.Normalized();
                if (!_started) { _dir[e][i] = want; _vel[e][i] = Vector3.Zero; continue; }
                // spring towards the animated direction, pushed back by the head speeding up (inertia)
                float k = Stiff[Math.Min(i, Stiff.Length - 1)];
                var acc = (want - _dir[e][i]) * k - _vel[e][i] * Damp - headAcc * Toss * (1 + 0.5f * i);
                _vel[e][i] += acc * dt;
                var d = (_dir[e][i] + _vel[e][i] * dt).Normalized();
                // never further than MaxDeg from where the clip puts it
                float off = want.AngleTo(d);
                float max = Mathf.DegToRad(MaxDeg);
                if (off > max) d = want.Slerp(d, max / off).Normalized();
                _dir[e][i] = d;
                if (want.Dot(d) > 0.99999f) continue;
                var shown = (new Quaternion(want, d) * g.GetRotationQuaternion()).Normalized();
                int parent = sk.GetBoneParent(bone);
                var parentRot = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Orthonormalized().GetRotationQuaternion() : Quaternion.Identity;
                sk.SetBonePoseRotation(bone, (parentRot.Inverse() * shown).Normalized());
            }
        _started = true;
    }
}
