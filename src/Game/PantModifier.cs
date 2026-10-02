using Godot;

namespace ZairaPet.Game;

/// <summary>
/// Panting after the animation: the clips keep the jaw shut, and this brings it back towards the jaw's rest pose,
/// which in Sally's model is her mouth open with the tongue out (the photo was of her panting), by <see cref="Pant"/>
/// (0..1, how winded she is), with quick breaths. The mouth opens and shuts over a moment, not at once.
/// </summary>
public partial class PantModifier : SkeletonModifier3D
{
    public int Jaw = -1;
    /// <summary>0 shut (as the clip has it), 1 open as the model is; set by the game.</summary>
    public float Pant;
    public float BreathHz = 3f;
    /// <summary>How much the jaw moves with each breath (share of the open mouth).</summary>
    public float BreathDepth = 0.18f;
    /// <summary>For the self test: how far (deg) the jaw was from the open mouth after the last frame.</summary>
    public float ShutDeg { get; private set; }

    float _t, _shown;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton();
        if (sk == null || Jaw < 0) return;
        float dt = (float)delta;
        _t += dt;
        _shown = Mathf.MoveToward(_shown, Pant, dt * 1.5f);
        var rest = sk.GetBoneRest(Jaw).Basis.GetRotationQuaternion();
        var pose = sk.GetBonePoseRotation(Jaw);
        if (_shown > 0.001f)
        {
            float w = _shown * (1 - BreathDepth * (0.5f + 0.5f * Mathf.Sin(Mathf.Tau * BreathHz * _t)));
            pose = pose.Slerp(rest, w).Normalized();
            sk.SetBonePoseRotation(Jaw, pose);
        }
        ShutDeg = Mathf.RadToDeg(pose.AngleTo(rest));
    }
}
