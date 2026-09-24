using System;

namespace ZairaPet.Core;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>Screen rectangle in physical pixels, y down, Right/Bottom exclusive.</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public static class MathX
{
    /// <summary>Clamp that tolerates a range narrower than its margins (returns the middle).</summary>
    public static double SafeClamp(double v, double min, double max) => min > max ? (min + max) / 2 : Math.Clamp(v, min, max);
}
