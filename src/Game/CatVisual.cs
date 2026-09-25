using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ZairaPet.Game;

/// <summary>
/// The 3D cat: loads the GLB of a <see cref="CatProfile"/> at runtime, scales it to its on-screen length,
/// recolours it and plays logical actions ("walk", "sleep"...) through the profile's mapping.
/// The node origin sits between the feet; 1 world unit = 1 screen pixel.
/// </summary>
public partial class CatVisual : Node3D
{
    CatProfile _profile = null!;
    Node3D _pivot = null!;       // yaw / held spin / procedural wobble
    Node3D _model = null!;
    AnimationPlayer? _player;
    readonly Dictionary<string, string> _resolved = new(); // lower-case → real clip name
    readonly Random _rng = new();

    string _action = "";
    ActionClip? _clip;
    double _yaw;
    double _roll;
    double _time;

    public const double HeldRollDeg = 60;
    /// <summary>Body angle while going up a window side: nearly vertical, head up.</summary>
    public const double ClimbRollDeg = 78;

    /// <summary>Where the scruff ends up above the feet once the cat hangs, so the cursor can hold it there.</summary>
    public float ScruffHeight => (float)(SizePx.X * 0.45 * Math.Sin(Mathf.DegToRad((float)HeldRollDeg))
                                        + SizePx.Y * 0.8 * Math.Cos(Mathf.DegToRad((float)HeldRollDeg)));

    /// <summary>Model size on screen after scaling, in pixels.</summary>
    public Vector2 SizePx { get; private set; }

    /// <summary>Extra yaw set by the player while holding the cat.</summary>
    public double HeldSpin { get; set; }

    public void Load(CatProfile profile)
    {
        _profile = profile;
        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);

        var doc = new GltfDocument();
        var state = new GltfState();
        string path = System.IO.Path.Combine(profile.Folder, profile.Model);
        var err = doc.AppendFromFile(path, state);
        if (err != Error.Ok) throw new InvalidOperationException($"GLB non caricato ({err}): {path}");
        _model = (Node3D)doc.GenerateScene(state);
        _pivot.AddChild(_model);

        _player = FindAll<AnimationPlayer>(_model).FirstOrDefault();
        if (_player != null)
            foreach (var name in _player.GetAnimationList())
            {
                _resolved.TryAdd(name.ToString().ToLowerInvariant(), name);
                FillRestTracks(_player.GetAnimation(name));
            }

        Recolor();
        FitToLength();
        Play("idle");
    }

    /// <summary>
    /// Exporters drop tracks for bones that stay at rest for a whole clip. Without them a bone keeps the pose of
    /// the previous clip (legs frozen mid-stride after walking), so give every clip every bone, at rest if unanimated.
    /// </summary>
    void FillRestTracks(Animation anim)
    {
        var skeleton = FindAll<Skeleton3D>(_model).FirstOrDefault();
        if (skeleton == null || _player == null) return;
        var root = _player.GetNode(_player.RootNode);
        string skelPath = root.GetPathTo(skeleton).ToString();

        var present = new HashSet<(string, Animation.TrackType)>();
        for (int t = 0; t < anim.GetTrackCount(); t++)
            present.Add((anim.TrackGetPath(t).GetConcatenatedSubNames(), anim.TrackGetType(t)));

        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            string bone = skeleton.GetBoneName(b);
            var rest = skeleton.GetBoneRest(b);
            if (!present.Contains((bone, Animation.TrackType.Rotation3D)))
            {
                int t = anim.AddTrack(Animation.TrackType.Rotation3D);
                anim.TrackSetPath(t, $"{skelPath}:{bone}");
                anim.RotationTrackInsertKey(t, 0, rest.Basis.GetRotationQuaternion());
            }
            if (!present.Contains((bone, Animation.TrackType.Position3D)))
            {
                int t = anim.AddTrack(Animation.TrackType.Position3D);
                anim.TrackSetPath(t, $"{skelPath}:{bone}");
                anim.PositionTrackInsertKey(t, 0, rest.Origin);
            }
        }
    }

    /// <summary>
    /// Self-test: world position of a point `along` model units down a bone's axis in its current pose
    /// (e.g. the tip of the nose past the head bone), or null if the model has no such bone.
    /// </summary>
    internal Vector3? BonePoint(string bone, float along)
    {
        var skeleton = FindAll<Skeleton3D>(_model).FirstOrDefault();
        int i = skeleton?.FindBone(bone) ?? -1;
        if (skeleton == null || i < 0) return null;
        var pose = skeleton.GetBoneGlobalPose(i);
        return skeleton.GlobalTransform * (pose.Origin + pose.Basis.Y.Normalized() * along);
    }

    /// <summary>Self-test: every clip drives every bone (see <see cref="FillRestTracks"/>).</summary>
    internal bool EveryClipDrivesEveryBone()
    {
        var skeleton = FindAll<Skeleton3D>(_model).FirstOrDefault();
        if (skeleton == null || _player == null) return true;
        foreach (var name in _player.GetAnimationList())
        {
            var anim = _player.GetAnimation(name);
            var bones = Enumerable.Range(0, anim.GetTrackCount())
                .Where(t => anim.TrackGetType(t) == Animation.TrackType.Rotation3D)
                .Select(t => anim.TrackGetPath(t).GetConcatenatedSubNames().ToString()).ToHashSet();
            for (int b = 0; b < skeleton.GetBoneCount(); b++)
                if (!bones.Contains(skeleton.GetBoneName(b))) return false;
        }
        return true;
    }

    void Recolor()
    {
        if (_profile.MaterialColors.Count == 0) return;
        foreach (var mi in FindAll<MeshInstance3D>(_model))
        {
            var mesh = mi.Mesh;
            if (mesh == null) continue;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetMaterial(s) is not StandardMaterial3D mat) continue;
                if (!_profile.MaterialColors.TryGetValue(mat.ResourceName, out var c) || c.Length < 3) continue;
                var copy = (StandardMaterial3D)mat.Duplicate();
                copy.AlbedoColor = new Color(c[0], c[1], c[2], 1);
                copy.Metallic = 0;
                copy.Roughness = 0.85f;
                mi.SetSurfaceOverrideMaterial(s, copy);
            }
        }
    }

    void FitToLength()
    {
        var box = ModelAabb();
        double length = Math.Max(box.Size.X, box.Size.Z);
        if (length <= 0.0001) length = 1;
        float scale = (float)(_profile.LengthPx / length);
        _model.Scale *= scale;
        // Put the feet on the node origin.
        box = ModelAabb();
        _model.Position -= new Vector3(box.GetCenter().X, box.Position.Y, box.GetCenter().Z);
        SizePx = new Vector2((float)_profile.LengthPx, box.Size.Y);
    }

    Aabb ModelAabb()
    {
        Aabb? total = null;
        var toPivot = _pivot.GlobalTransform.AffineInverse();
        foreach (var mi in FindAll<MeshInstance3D>(_model))
        {
            var box = (toPivot * mi.GlobalTransform) * mi.GetAabb();
            total = total?.Merge(box) ?? box;
        }
        return total ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    /// <summary>Switch the logical action; the clip keeps playing if it is already the current one.</summary>
    public void Play(string action)
    {
        if (action == _action || _player == null) return;
        var clip = _profile.Resolve(action);
        if (clip == null) return;
        _action = action;
        _clip = clip;

        var names = clip.Anims.Select(a => _resolved.GetValueOrDefault(a.ToLowerInvariant())).Where(n => n != null).ToList();
        if (names.Count == 0) return;
        string name = names[_rng.Next(names.Count)]!;
        var anim = _player.GetAnimation(name);
        anim.LoopMode = clip.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        _player.Play(name, customBlend: 0.18, customSpeed: (float)clip.Speed);
    }

    /// <summary>Per-frame pose: facing, ground speed for locomotion clips, procedural touches.</summary>
    public void Animate(double dt, int facing, double groundSpeed, bool held, bool climbing = false)
    {
        _time += dt;
        double target = held ? HeldSpin : facing * (90 - _profile.ThreeQuarterDeg);
        target += _profile.YawOffsetDeg;
        // Turn quickly but not instantly, through the viewer side (the cat never shows its back while turning).
        _yaw += (target - _yaw) * Math.Min(1, dt * (held ? 20 : 10));
        // Held by the scruff: the body hangs head-up and sways; otherwise upright.
        _roll += ((held ? HeldRollDeg : climbing ? ClimbRollDeg : 0) - _roll) * Math.Min(1, dt * 12);
        double sway = held ? 6 * Math.Sin(_time * 2.2) : 0;
        // Roll so the head (forward = +Z turned by yaw) points up, whichever side it faces.
        double headSide = Math.Sin(Mathf.DegToRad((float)_yaw)) >= 0 ? 1 : -1;
        var basis = new Basis(Vector3.Back, Mathf.DegToRad((float)((_roll + sway) * headSide)))
                    * new Basis(Vector3.Up, Mathf.DegToRad((float)_yaw));
        _pivot.Transform = new Transform3D(basis, _pivot.Position);

        if (_player != null && _clip != null)
        {
            double speed = _clip.Speed;
            if (_clip.RefSpeedPx > 0 && groundSpeed > 1) speed *= Math.Clamp(groundSpeed / _clip.RefSpeedPx, 0.5, 2.2);
            _player.SpeedScale = (float)(speed / _clip.Speed);
        }

        var s = Vector3.One;
        var offset = Vector3.Zero;
        if (_clip?.Breathe == true) s = new Vector3(1, 1 + 0.03f * (float)Math.Sin(_time * 2.4), 1);
        if (_clip?.Vibrate == true) offset = new Vector3(0.6f * (float)Math.Sin(_time * 90), 0, 0);
        _pivot.Scale = s;
        _pivot.Position = offset;
    }

    static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        if (root is T t) yield return t;
        foreach (var child in root.GetChildren())
            foreach (var x in FindAll<T>(child))
                yield return x;
    }
}
