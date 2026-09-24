using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

/// <summary>A top-level window as seen by the tracker (physical pixels).</summary>
public readonly record struct WindowInfo(long Handle, RectI Bounds);

public enum SurfaceKind { Floor, WindowTop, Perch }

/// <summary>
/// A horizontal segment the cat can stand on: [X0, X1) at height Y.
/// Owner is the window handle (0 for the floor, negative for props); OwnerX/OwnerY is the owner's
/// top-left corner when the map was built, so a standing cat can follow its owner when it moves.
/// </summary>
public sealed record Platform(int Id, SurfaceKind Kind, int Y, int X0, int X1, long Owner, int OwnerX, int OwnerY)
{
    public bool SpansX(double x) => x >= X0 && x < X1;
    public double Center => (X0 + X1) / 2.0;
}

public sealed class SurfaceMap
{
    /// <summary>Window tops closer than this to the top of their screen are useless: the cat would be off-screen.</summary>
    public const int Headroom = 60;

    public IReadOnlyList<Platform> Platforms { get; }
    public IReadOnlyList<RectI> WorkAreas { get; }

    SurfaceMap(IReadOnlyList<Platform> platforms, IReadOnlyList<RectI> workAreas)
    {
        Platforms = platforms;
        WorkAreas = workAreas;
    }

    public static SurfaceMap Build(IReadOnlyList<WindowInfo> windowsTopToBottom, IReadOnlyList<RectI> workAreas,
                                   IReadOnlyList<Platform> extra, int minWidth = 40)
    {
        var result = new List<Platform>();
        int id = 0;

        foreach (var wa in workAreas)
            result.Add(new Platform(id++, SurfaceKind.Floor, wa.Bottom, wa.Left, wa.Right, 0, wa.Left, wa.Bottom));

        for (int i = 0; i < windowsTopToBottom.Count; i++)
        {
            var w = windowsTopToBottom[i];
            int y = w.Bounds.Top;

            // A window is hiding the edge if it covers the pixel row just above it.
            var visible = new List<(int, int)> { (w.Bounds.Left, w.Bounds.Right) };
            for (int j = 0; j < i && visible.Count > 0; j++)
            {
                var o = windowsTopToBottom[j].Bounds;
                if (o.Top < y && o.Bottom >= y)
                    visible = Subtract(visible, o.Left, o.Right);
            }

            foreach (var (x0, x1) in visible)
                foreach (var wa in workAreas)
                {
                    if (y < wa.Top + Headroom || y >= wa.Bottom) continue;
                    int a = Math.Max(x0, wa.Left), b = Math.Min(x1, wa.Right);
                    if (b - a >= minWidth)
                        result.Add(new Platform(id++, SurfaceKind.WindowTop, y, a, b, w.Handle, w.Bounds.Left, w.Bounds.Top));
                }
        }

        foreach (var p in extra)
            result.Add(p with { Id = id++ });

        return new SurfaceMap(result, workAreas);
    }

    static List<(int, int)> Subtract(List<(int, int)> spans, int cutFrom, int cutTo)
    {
        var output = new List<(int, int)>();
        foreach (var (a, b) in spans)
        {
            if (cutTo <= a || cutFrom >= b) { output.Add((a, b)); continue; }
            if (cutFrom > a) output.Add((a, cutFrom));
            if (cutTo < b) output.Add((cutTo, b));
        }
        return output;
    }

    /// <summary>The platform under the cat's feet, if the feet are within <paramref name="tolerance"/> px of it.</summary>
    public Platform? SupportAt(double x, double y, double tolerance) =>
        Platforms.Where(p => p.SpansX(x) && Math.Abs(p.Y - y) <= tolerance)
                 .OrderBy(p => Math.Abs(p.Y - y)).FirstOrDefault();

    /// <summary>The highest platform at or below <paramref name="y"/> under x: where a falling cat lands.</summary>
    public Platform? FirstBelow(double x, double y) =>
        Platforms.Where(p => p.SpansX(x) && p.Y >= y).OrderBy(p => p.Y).FirstOrDefault();

    /// <summary>The floor of the screen containing x (or the nearest screen).</summary>
    public Platform NearestFloor(double x) =>
        Platforms.Where(p => p.Kind == SurfaceKind.Floor)
                 .OrderBy(p => p.SpansX(x) ? 0 : Math.Min(Math.Abs(p.X0 - x), Math.Abs(p.X1 - x)))
                 .First();

    /// <summary>
    /// Where a cat that stood on <paramref name="old"/> at x stands now: same owner, moved by the owner's offset.
    /// Null when the owner disappeared or that stretch of its edge is no longer reachable.
    /// </summary>
    public (Platform platform, double x)? Relocate(Platform old, double x)
    {
        foreach (var p in Platforms)
        {
            if (p.Owner != old.Owner || p.Kind != old.Kind) continue;
            // Floors never move; several share Owner 0, so only the same screen's floor matches.
            if (p.Kind == SurfaceKind.Floor && (p.X0 != old.X0 || p.Y != old.Y)) continue;
            double nx = x + (p.OwnerX - old.OwnerX);
            if (p.OwnerY - old.OwnerY == p.Y - old.Y && p.SpansX(nx))
                return (p, nx);
        }
        return null;
    }
}
