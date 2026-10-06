using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;

namespace ZairaPet.Web;

/// <summary>
/// The phone's room: what Main.cs does on the desktop, without Godot and without windows. One floor across the screen
/// (the room is the phone's screen, in world units), a shelf halfway up to jump onto, the bowl, the cat's perch and a
/// treat. The pet is the desktop one: same brain, same body physics, same needs; the page draws it.
/// </summary>
public sealed class Sim : IWorld
{
    public const double PerchHeight = 230, PerchTopWidth = 124;
    /// <summary>The shelf: a plank on the wall, a window top for the brain (it jumps up onto it), at most ShelfUp above
    /// the floor (a phone in portrait is tall: halfway up it was out of reach).</summary>
    public const double ShelfWidth = 0.42, ShelfUp = 170;
    /// <summary>The fastest it is thrown when let go (room units/s).</summary>
    public const double MaxToss = 700;

    readonly Random _rng = new();
    public PetBrain Brain { get; }
    public Needs Needs { get; } = new();
    public CatBody Body { get; private set; }
    public CatBody Bowl { get; private set; }
    public double BowlFood { get; private set; } = 1;
    public CatBody Perch { get; private set; }
    public CatBody? Treat { get; private set; }
    public readonly Exertion Exertion = new();
    public bool HasPerch { get; }
    public string Species { get; }
    public double LengthPx { get; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    public RectI Shelf { get; private set; }
    SurfaceMap _map = null!;
    Platform? _perchPlatform;

    // the pet's size on screen (from the model) for touching it
    public double PetW = 150, PetH = 100;

    enum Drag { None, Pet, Bowl, Perch }
    Drag _drag;
    Vec2 _grabOffset;

    public Sim(string species, double width, double height, double lengthPx, string? saveJson)
    {
        Species = species;
        LengthPx = lengthPx;
        HasPerch = species == "cat";
        Brain = Brains.For(species, _rng);
        Width = width; Height = height;
        BuildMap(initial: true);
        var save = string.IsNullOrEmpty(saveJson) ? new SaveData() : SaveData.FromJson(saveJson);
        var restored = save.RestoreNeeds(DateTime.UtcNow);
        Needs.Hunger = restored.Hunger; Needs.Energy = restored.Energy;
        Needs.Playfulness = restored.Playfulness; Needs.Affection = restored.Affection;
        var floor = _map.NearestFloor(width / 2);
        double X(double saved, double fraction) =>
            double.IsNaN(saved) || saved < 20 || saved > width - 20 ? floor.X0 + (floor.X1 - floor.X0) * fraction : saved;
        Bowl = new CatBody(new Vec2(X(save.BowlX, 0.72), floor.Y));
        BowlFood = save.BowlFood;
        Perch = new CatBody(new Vec2(X(save.PerchX, 0.12), floor.Y));
        Bowl.PlaceOn(floor, Bowl.Pos.X);
        Perch.PlaceOn(floor, Perch.Pos.X);
        double x = X(save.CatX, 0.45);
        Body = new CatBody(new Vec2(x, floor.Y));
        Body.PlaceOn(floor, x);
        BuildMap();
    }

    /// <summary>The phone turned or the page resized: a new room, everything put back on its floor.</summary>
    public void Resize(double width, double height)
    {
        Width = width; Height = height;
        BuildMap();
        var floor = _map.NearestFloor(width / 2);
        foreach (var b in new[] { Bowl, Perch, Treat })
            if (b != null && b.Mode != BodyMode.Held) b.PlaceOn(floor, Math.Clamp(b.Pos.X, 20, width - 20));
        if (Body.Mode != BodyMode.Held) Body.PlaceOn(floor, Math.Clamp(Body.Pos.X, 20, width - 20));
        BuildMap();
    }

    void BuildMap(bool initial = false)
    {
        var work = new List<RectI> { new(0, 0, (int)Width, (int)Height) };
        int sw = (int)(Width * ShelfWidth), sy = (int)Math.Max(Height * 0.5, Height - ShelfUp);
        int sx = (int)(Width * 0.52);
        Shelf = new RectI(sx, sy, Math.Min((int)Width - 10, sx + sw), sy + 18);
        var windows = new List<WindowInfo> { new(1, Shelf) };
        var extra = new List<Platform>();
        if (!initial && HasPerch && Perch.Mode == BodyMode.Grounded)
        {
            int y = (int)(Perch.Pos.Y - PerchHeight), x = (int)Perch.Pos.X;
            extra.Add(new Platform(0, SurfaceKind.Perch, y, x - (int)PerchTopWidth / 2, x + (int)PerchTopWidth / 2, -1, x, y));
        }
        _map = SurfaceMap.Build(windows, work, extra);
        _perchPlatform = _map.Platforms.FirstOrDefault(p => p.Kind == SurfaceKind.Perch);
        if (initial) return;
        Body.FollowSupport(_map);
        Bowl.FollowSupport(_map);
        Treat?.FollowSupport(_map);
    }

    // ------------------------------------------------------------------ IWorld
    public Platform? BowlPlatform => Bowl.Mode == BodyMode.Grounded ? Bowl.Support : null;
    public double BowlX => Bowl.Pos.X;
    double IWorld.BowlFood => BowlFood;
    public Platform? PerchTop => HasPerch && Perch.Mode == BodyMode.Grounded ? _perchPlatform : null;
    public double PerchX => Perch.Pos.X;
    public Vec2? TreatPos => Treat?.Pos;
    public bool TreatLanded => Treat?.Mode == BodyMode.Grounded;
    public void EatFromBowl(double amount) => BowlFood = Math.Max(0, BowlFood - amount);
    public void ConsumeTreat() => Treat = null;

    // ------------------------------------------------------------------ frame
    public void Tick(double dt)
    {
        dt = Math.Min(dt, 0.1);
        foreach (var b in new[] { Bowl, Perch, Treat })
        {
            if (b == null) continue;
            if (b.Mode == BodyMode.Held) b.TickHeld(dt); else b.Step(dt, _map, 0);
        }
        Body.TickHeld(dt);
        if (_drag == Drag.Perch || Perch.Mode != BodyMode.Grounded) BuildMap();
        Brain.Update(dt, Body, Needs, _map, this);
        Exertion.Update(dt, Body.Mode == BodyMode.Held ? 0 : Math.Abs(Body.Vel.X), LengthPx);
    }

    public TailMood TailMood => TailMoods.For(Brain.State, Needs, Brain.Happy);

    // ------------------------------------------------------------------ touch
    bool OnPet(Vec2 p) => Math.Abs(p.X - Body.Pos.X) < PetW / 2 && p.Y < Body.Pos.Y + 6 && p.Y > Body.Pos.Y - PetH;
    static bool On(CatBody b, Vec2 p, double w, double h) => Math.Abs(p.X - b.Pos.X) < w / 2 && p.Y < b.Pos.Y + 6 && p.Y > b.Pos.Y - h;

    /// <summary>A finger down: the bowl first (small, often under the pet), then the pet, then the perch. 0 nothing,
    /// 1 the pet, 2 the bowl, 3 the perch.</summary>
    public int Press(double x, double y)
    {
        var p = new Vec2(x, y);
        if (On(Bowl, p, 70, 44)) { _drag = Drag.Bowl; Bowl.Grab(); _grabOffset = Bowl.Pos + new Vec2(-x, -y); return 2; }
        if (OnPet(p))
        {
            _drag = Drag.Pet; Brain.OnGrab(Body);
            _grabOffset = new Vec2(0, PetH * 0.55);   // held by the scruff: the finger on its back
            Body.MoveHeld(p + _grabOffset);
            return 1;
        }
        if (HasPerch && On(Perch, p, PerchTopWidth, PerchHeight + 16))
        { _drag = Drag.Perch; Perch.Grab(); _grabOffset = Perch.Pos + new Vec2(-x, -y); return 3; }
        return 0;
    }

    public void Move(double x, double y, double dt)
    {
        var p = new Vec2(x, y) + _grabOffset;
        switch (_drag)
        {
            case Drag.Pet: Body.MoveHeld(p, dt); break;
            case Drag.Bowl: Bowl.MoveHeld(p, dt); break;
            case Drag.Perch: Perch.MoveHeld(p, dt); break;
        }
    }

    public void Release()
    {
        switch (_drag)
        {
            case Drag.Pet:
            {
                // a finger flicks much faster than a mouse drags: a gentle toss at most
                var v = Body.HeldVelocity * 0.8;
                double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                if (len > MaxToss) v = new Vec2(v.X * MaxToss / len, v.Y * MaxToss / len);
                Brain.OnRelease(Body, v, _map);
                break;
            }
            case Drag.Bowl: Bowl.Release(new Vec2(0, 0), _map); break;
            case Drag.Perch: Perch.Release(new Vec2(0, 0), _map); BuildMap(); break;
        }
        _drag = Drag.None;
    }

    /// <summary>A finger stroking the pet (the length of the stroke, in world units).</summary>
    public bool Stroke(double x, double y, double length)
    {
        if (_drag != Drag.None || !OnPet(new Vec2(x, y)) || length < 1.5) return false;
        Brain.OnPetting(Math.Min(0.06, length / 300));
        return true;
    }

    public void FillBowl() { BowlFood = 1; Brain.Notice(); }

    public void ThrowTreat(double x)
    {
        Treat = new CatBody(new Vec2(Math.Clamp(x, 20, Width - 20), 40));
        Brain.Notice();
    }

    public string Save() =>
        SaveData.Capture(Needs, BowlFood, Bowl.Pos.X, Perch.Pos.X, Body.Pos.X).ToJson();
}
