using System;

namespace ZairaPet.Core;

/// <summary>Where the head points: along the body, at whoever sits in front of the screen, or at a spot on it.</summary>
public enum GazeKind { Ahead, Viewer, Point }

/// <summary>What the gaze needs to know each frame. Positions are screen pixels.</summary>
public readonly record struct GazeInput(CatState State, Vec2 Head, int Facing, Vec2? Mouse, Vec2? JumpTarget, Vec2? Treat);

/// <summary>
/// Decides what the cat looks at. The animation keeps playing; the visual turns neck and head on top of it.
/// Priorities: where it is about to land, the treat it chases, a cursor close by; otherwise, at rest it
/// looks at the viewer, looks around, or just ahead, and while walking it now and then glances at the viewer.
/// </summary>
public sealed class Gaze
{
    public const double MouseRange = 320;

    readonly Random _rng;
    double _left;          // seconds left for the current idle choice
    GazeKind _idleKind = GazeKind.Ahead;
    Vec2 _idlePoint;
    bool _walking;
    (GazeKind kind, Vec2 point, double left)? _forced;

    public GazeKind Kind { get; private set; } = GazeKind.Ahead;
    public Vec2 Point { get; private set; }

    public Gaze(Random rng) => _rng = rng;

    /// <summary>Look at something regardless of the rules for a while (self test, debugging).</summary>
    public void Force(GazeKind kind, Vec2 point, double seconds) => _forced = (kind, point, seconds);

    public void Update(double dt, GazeInput i)
    {
        _left -= dt;
        if (_forced is { } f)
        {
            _forced = f.left > dt ? (f.kind, f.point, f.left - dt) : null;
            Set(f.kind, f.point);
            return;
        }

        if (i.State is CatState.Sleep or CatState.Eat or CatState.Held or CatState.Climb or CatState.Zoomies)
        {
            Set(GazeKind.Ahead);
            _left = 0;
            return;
        }
        if (i.JumpTarget is { } landing) { Set(GazeKind.Point, landing); return; }
        if (i.State == CatState.Hunt && i.Mouse is { } prey) { Set(GazeKind.Point, prey); return; }
        if (i.State == CatState.ChaseTreat && i.Treat is { } treat) { Set(GazeKind.Point, treat); return; }

        bool resting = i.State is CatState.Idle or CatState.Sit or CatState.Petted or CatState.Meow or CatState.Landing;
        if (resting && i.Mouse is { } m && (m - i.Head).Length < MouseRange) { Set(GazeKind.Point, m); return; }

        bool walking = !resting;
        if (walking != _walking) { _walking = walking; _left = 0; }
        if (_left <= 0) Choose(i, walking);
        Set(_idleKind, _idlePoint);
    }

    void Choose(GazeInput i, bool walking)
    {
        double r = _rng.NextDouble();
        if (walking)
        {
            // Mostly eyes on the path; a short glance at the viewer from time to time.
            bool glance = _idleKind == GazeKind.Ahead && r < 0.25;
            _idleKind = glance ? GazeKind.Viewer : GazeKind.Ahead;
            _left = glance ? 0.8 + _rng.NextDouble() * 0.7 : 4 + _rng.NextDouble() * 6;
            return;
        }
        if (r < 0.45)
        {
            _idleKind = GazeKind.Viewer;
            _left = 2.5 + _rng.NextDouble() * 4;
        }
        else if (r < 0.8)
        {
            // Something caught its eye: a spot on the screen around it, mostly in front and a bit above.
            _idleKind = GazeKind.Point;
            double dx = i.Facing * (80 + _rng.NextDouble() * 420) * (_rng.NextDouble() < 0.8 ? 1 : -0.5);
            double dy = -40 - _rng.NextDouble() * 260 + (_rng.NextDouble() < 0.25 ? 300 : 0);
            _idlePoint = i.Head + new Vec2(dx, dy);
            _left = 1.5 + _rng.NextDouble() * 2.5;
        }
        else
        {
            _idleKind = GazeKind.Ahead;
            _left = 2 + _rng.NextDouble() * 4;
        }
    }

    void Set(GazeKind kind, Vec2 point = default)
    {
        Kind = kind;
        Point = point;
    }
}
