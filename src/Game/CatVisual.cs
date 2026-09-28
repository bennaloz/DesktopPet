using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZairaPet.Core;

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
    int _variant;            // which of the clip's anims is playing
    int _side;               // the facing the playing anim was picked for (see ActionClip.AnimsRight)
    double _yaw;
    double _roll;
    double _time;

    GazeModifier? _gaze;
    bool _gazeActive;
    TailModifier? _tail;

    /// <summary>What the tail shows while she walks or runs (set by the game every frame).</summary>
    public TailMood TailMood { get; set; } = TailMood.Calm;
    /// <summary>Gaits whose tail follows the mood; at rest the poses place the tail themselves.</summary>
    static readonly HashSet<string> MoodTailActions = new() { "walk", "trot", "lope", "run" };
    /// <summary>Self-test: the tail shape being shown and how much of it.</summary>
    internal (double[] lift, float weight)? TailShown => _tail == null ? null : (_tail.Lift, _tail.Weight);

    /// <summary>
    /// Look at a world point (null: let the animation lead). Eyes move fast, the head follows smoothly and
    /// turns back to the animation's pose when there is nothing to look at.
    /// </summary>
    public void Look(Vector3? target, double dt)
    {
        if (_gaze == null) return;
        float want = target == null ? 0 : 1;
        _gaze.Weight = Mathf.MoveToward(_gaze.Weight, want, (float)dt * 3.5f);
        if (target is { } t)
        {
            // Snap when starting from rest, then follow the target smoothly.
            _gaze.Target = _gazeActive ? _gaze.Target.Lerp(t, Math.Min(1f, (float)dt * 7)) : t;
            _gazeActive = true;
        }
        else if (_gaze.Weight <= 0) _gazeActive = false;
    }

    /// <summary>Self-test: head position and direction after the gaze turned it (null if not looking).</summary>
    internal (Vector3 pos, Vector3 dir)? GazeHead => _gaze is { Weight: > 0.001f } g ? (g.HeadPos, g.HeadDir) : null;

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

        var skeleton = FindAll<Skeleton3D>(_model).FirstOrDefault();
        if (skeleton != null && skeleton.FindBone(profile.GazeBones.Head) >= 0)
        {
            _gaze = new GazeModifier
            {
                Name = "Gaze",
                Neck = skeleton.FindBone(profile.GazeBones.Neck),
                Head = skeleton.FindBone(profile.GazeBones.Head),
                Chest = profile.GazeBones.Chest.Length > 0 ? skeleton.FindBone(profile.GazeBones.Chest) : -1,
                Keep = profile.GazeBones.Keep.Select(skeleton.FindBone).Where(b => b >= 0).ToArray(),
            };
            skeleton.AddChild(_gaze);
        }
        if (skeleton != null) _tail = AddTail(skeleton);

        Recolor();
        UseFloorClipMaterials();
        FitToLength();
        Play("idle");
    }

    TailModifier? AddTail(Skeleton3D skeleton)
    {
        int root = skeleton.FindBone(_profile.TailBones.Root);
        var bones = _profile.TailBones.Bones.Select(skeleton.FindBone).ToArray();
        if (root < 0 || bones.Length == 0 || bones.Any(b => b < 0)) return null;
        var tail = new TailModifier { Name = "Tail", Hips = root, Tail = bones };
        tail.Lift = (double[])TailMoods.Shape(TailMood.Calm).Lift.Clone();
        skeleton.AddChild(tail);
        tail.Setup(skeleton);
        return tail;
    }

    /// <summary>Ease the tail towards the mood's shape (a second or so), on in the gaits, off at rest.</summary>
    void UpdateTail(double dt)
    {
        if (_tail == null) return;
        var want = TailMoods.Shape(TailMood);
        if (_action is "lope" or "run") want = TailMoods.Running(want);
        double k = Math.Min(1, dt * 2.5);
        for (int i = 0; i < _tail.Lift.Length && i < want.Lift.Length; i++) _tail.Lift[i] += (want.Lift[i] - _tail.Lift[i]) * k;
        _tail.WaveDeg += (want.WaveDeg - _tail.WaveDeg) * k;
        _tail.WaveHz += (want.WaveHz - _tail.WaveHz) * k;
        _tail.TipDeg += (want.TipDeg - _tail.TipDeg) * k;
        _tail.TipHz += (want.TipHz - _tail.TipHz) * k;
        _tail.TipJerky = want.TipJerky;
        _tail.Time += dt;
        float on = MoodTailActions.Contains(_action) ? 1 : 0;
        _tail.Weight = Mathf.MoveToward(_tail.Weight, on, (float)dt * 3);
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

    /// <summary>
    /// While she rests on a surface nothing of her is drawn below it: a body lying on the floor flattens against
    /// it, and the rig can only bend her, so a pose that sits a little into the floor (a round belly, a thigh)
    /// looks planted instead of hovering or poking through the window edge. Off in the air and when held.
    /// </summary>
    public bool FloorClip { get; set; }

    const string FloorClipShader = @"
shader_type spatial;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float roughness = 0.9;
uniform float metallic = 0.0;
uniform float floor_y = -1e9;
varying float world_y;
void vertex() { world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y; }
void fragment() {
    if (world_y < floor_y) discard;
    vec4 c = texture(albedo_tex, UV) * albedo_color;
    ALBEDO = c.rgb;
    ROUGHNESS = roughness;
    METALLIC = metallic;
}";
    static Shader? _floorClipShader;
    readonly List<ShaderMaterial> _clipMaterials = new();

    /// <summary>The model's materials (a colour texture, roughness, metallic) as the same look plus the floor clip.</summary>
    void UseFloorClipMaterials()
    {
        _floorClipShader ??= new Shader { Code = FloorClipShader };
        foreach (var mi in FindAll<MeshInstance3D>(_model))
        {
            var mesh = mi.Mesh;
            if (mesh == null) continue;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if ((mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not StandardMaterial3D std) continue;
                var clip = new ShaderMaterial { Shader = _floorClipShader };
                clip.SetShaderParameter("albedo_tex", std.AlbedoTexture);
                clip.SetShaderParameter("albedo_color", std.AlbedoColor);
                clip.SetShaderParameter("roughness", std.Roughness);
                clip.SetShaderParameter("metallic", std.Metallic);
                mi.SetSurfaceOverrideMaterial(s, clip);
                _clipMaterials.Add(clip);
            }
        }
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

    /// <summary>
    /// Switch the logical action; the clip keeps playing if it is already the current one. Facing picks the
    /// variant with the tail on the side towards the viewer, and turning round swaps it without restarting.
    /// </summary>
    public void Play(string action, int facing = 1)
    {
        if (_player == null) return;
        int side = facing >= 0 ? 1 : -1;
        if (action == _action)
        {
            if (_clip is { AnimsRight.Count: > 0 } c && side != _side) Start(c, _variant, side, keepTime: true);
            return;
        }
        var clip = _profile.Resolve(action);
        if (clip == null || clip.Anims.Count == 0) return;
        _action = action;
        _clip = clip;
        Start(clip, _rng.Next(clip.Anims.Count), side, keepTime: false);
    }

    void Start(ActionClip clip, int variant, int side, bool keepTime)
    {
        _variant = variant;
        _side = side;
        var list = side > 0 && clip.AnimsRight.Count == clip.Anims.Count ? clip.AnimsRight : clip.Anims;
        string? name = _resolved.GetValueOrDefault(list[variant].ToLowerInvariant())
                       ?? _resolved.GetValueOrDefault(clip.Anims[variant].ToLowerInvariant());
        if (name == null) return;
        double at = keepTime ? _player!.CurrentAnimationPosition : 0;
        var anim = _player!.GetAnimation(name);
        anim.LoopMode = clip.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        _player.Play(name, customBlend: keepTime ? 0.3f : (float)clip.Blend, customSpeed: (float)clip.Speed);
        if (keepTime) _player.Seek(Math.Min(at, anim.Length), update: true);
    }

    /// <summary>Per-frame pose: facing, ground speed for locomotion clips, procedural touches.</summary>
    public void Animate(double dt, int facing, double groundSpeed, bool held, bool climbing = false, ZairaPet.Core.Vec2? airVel = null)
    {
        _time += dt;
        // Leaping steeply she turns side-on, so her stretched-out length shows instead of being foreshortened.
        double sideOn = airVel is { } av ? ZairaPet.Core.Flight.SideOn(av) : 0;
        if (_clip?.ShowSide is 1 or -1) facing = _clip.ShowSide;
        double target = held ? HeldSpin : facing * (90 - _profile.ThreeQuarterDeg * (1 - sideOn));
        target += _profile.YawOffsetDeg;
        // Turn quickly but not instantly, through the viewer side (the cat never shows its back while turning).
        _yaw += (target - _yaw) * Math.Min(1, dt * (held ? 20 : 10));
        // Held by the scruff: the body hangs head-up and sways; in the air it follows the trajectory
        // (nearly upright leaping up a window); otherwise upright.
        double pitch = held ? HeldRollDeg : climbing ? ClimbRollDeg
                     : airVel is { } v ? ZairaPet.Core.Flight.PitchDeg(v) : 0;
        _roll += (pitch - _roll) * Math.Min(1, dt * 12);
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
        UpdateTail(dt);

        // the feet are on the node origin: clip just under it (half a pixel, so paws standing on it stay whole)
        float floorY = FloorClip ? GlobalPosition.Y - 0.5f : -1e9f;
        foreach (var m in _clipMaterials) m.SetShaderParameter("floor_y", floorY);
    }

    static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        if (root is T t) yield return t;
        foreach (var child in root.GetChildren())
            foreach (var x in FindAll<T>(child))
                yield return x;
    }
}
