using System;
using Godot;
using ZairaPet.Core;

namespace ZairaPet.Game;

/// <summary>A desktop object with simple physics (it falls onto surfaces and rides moving windows).</summary>
public abstract partial class Prop : Node3D
{
    public CatBody Body { get; protected set; } = null!;
    /// <summary>Screen footprint: width and height above the support point.</summary>
    public Vector2 SizePx { get; protected set; }

    public RectI ScreenRect => new(
        (int)(Body.Pos.X - SizePx.X / 2), (int)(Body.Pos.Y - SizePx.Y),
        (int)(Body.Pos.X + SizePx.X / 2), (int)Body.Pos.Y + 2);

    protected static StandardMaterial3D Mat(Color c, float rough = 0.8f) =>
        new() { AlbedoColor = c, Roughness = rough };

    protected MeshInstance3D Add(Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        AddChild(mi);
        return mi;
    }
}

public partial class Bowl : Prop
{
    MeshInstance3D _food = null!;

    public double Food { get; set; } = 1;

    public Bowl() : this(new Vec2(0, 0)) { }

    public Bowl(Vec2 pos)
    {
        Body = new CatBody(pos);
        SizePx = new Vector2(72, 24);
        Add(new CylinderMesh { TopRadius = 36, BottomRadius = 27, Height = 22, RadialSegments = 32 },
            Mat(new Color(0.78f, 0.22f, 0.25f), 0.4f), new Vector3(0, 11, 0));
        Add(new CylinderMesh { TopRadius = 30, BottomRadius = 30, Height = 3, RadialSegments = 32 },
            Mat(new Color(0.95f, 0.93f, 0.9f)), new Vector3(0, 21, 0));
        _food = Add(new SphereMesh { Radius = 28, Height = 14, RadialSegments = 24, Rings = 8 },
            Mat(new Color(0.45f, 0.28f, 0.14f), 1f), new Vector3(0, 20, 0));
    }

    public void UpdateLook() => _food.Visible = Food > 0.02;

    public override void _Process(double delta)
    {
        UpdateLook();
        float h = (float)Math.Clamp(Food, 0, 1);
        _food.Scale = new Vector3(0.6f + 0.4f * h, 0.3f + 0.7f * h, 0.6f + 0.4f * h);
    }
}

public partial class Perch : Prop
{
    public const float Height = 230;
    public const float TopWidth = 124;

    public Perch() : this(new Vec2(0, 0)) { }

    public Perch(Vec2 pos)
    {
        Body = new CatBody(pos);
        SizePx = new Vector2(TopWidth, Height + 16);
        var wood = Mat(new Color(0.62f, 0.47f, 0.33f));
        var rope = Mat(new Color(0.83f, 0.74f, 0.55f), 1f);
        var cushion = Mat(new Color(0.42f, 0.55f, 0.72f), 1f);
        Add(new BoxMesh { Size = new Vector3(110, 12, 70) }, wood, new Vector3(0, 6, 0));
        Add(new CylinderMesh { TopRadius = 11, BottomRadius = 11, Height = Height - 12, RadialSegments = 16 }, rope,
            new Vector3(0, 12 + (Height - 12) / 2, 0));
        Add(new BoxMesh { Size = new Vector3(TopWidth, 12, 80) }, wood, new Vector3(0, Height - 6, 0));
        Add(new CylinderMesh { TopRadius = 44, BottomRadius = 48, Height = 10, RadialSegments = 24 }, cushion,
            new Vector3(0, Height + 5, 0));
    }

    /// <summary>The top surface as an extra platform for the surface map.</summary>
    public Platform TopPlatform()
    {
        int y = (int)(Body.Pos.Y - Height);
        int x = (int)Body.Pos.X;
        return new Platform(0, SurfaceKind.Perch, y, x - (int)TopWidth / 2, x + (int)TopWidth / 2, -1, x, y);
    }
}

public partial class Treat : Prop
{
    public Treat() : this(new Vec2(0, 0)) { }

    public Treat(Vec2 pos)
    {
        Body = new CatBody(pos);
        SizePx = new Vector2(18, 14);
        Add(new SphereMesh { Radius = 7, Height = 11 }, Mat(new Color(0.55f, 0.33f, 0.16f), 0.9f), new Vector3(0, 5, 0));
        Add(new SphereMesh { Radius = 5, Height = 8 }, Mat(new Color(0.66f, 0.42f, 0.2f), 0.9f), new Vector3(6, 4, 2));
    }
}

/// <summary>What the brain sees of the props.</summary>
public sealed class GameWorld : IWorld
{
    public Bowl Bowl = null!;
    public Perch Perch = null!;
    public Treat? Treat;
    public Platform? PerchPlatform;
    public Action? TreatEaten;

    public Platform? BowlPlatform => Bowl.Body.Mode == BodyMode.Grounded ? Bowl.Body.Support : null;
    public double BowlX => Bowl.Body.Pos.X;
    public double BowlFood => Bowl.Food;
    public Platform? PerchTop => Perch.Body.Mode == BodyMode.Grounded ? PerchPlatform : null;
    public double PerchX => Perch.Body.Pos.X;
    public Vec2? TreatPos => Treat?.Body.Pos;
    public bool TreatLanded => Treat?.Body.Mode == BodyMode.Grounded;

    public void EatFromBowl(double amount) => Bowl.Food = Math.Max(0, Bowl.Food - amount);
    public void ConsumeTreat() => TreatEaten?.Invoke();
}
