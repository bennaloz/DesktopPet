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
/// OwnerRight (when known) tells a move (both edges shift) from a resize (one edge shifts).
/// </summary>
public sealed record Platform(int Id, SurfaceKind Kind, int Y, int X0, int X1, long Owner, int OwnerX, int OwnerY,
                              int OwnerRight = int.MinValue)
{
    public bool SpansX(double x) => x >= X0 && x < X1;
    public double Center => (X0 + X1) / 2.0;
}

/// <summary>
/// A climbable window side at x = X, from its top Y0 down to Y1. Side -1 is the left edge (the cat hangs
/// on its left), +1 the right edge. Only sides whose top corner is visible are kept: climbing must end on the top.
/// </summary>
public sealed record Wall(int X, int Y0, int Y1, int Side, long Owner, int OwnerX, int OwnerY)
{
    public bool SpansY(double y) => y >= Y0 && y <= Y1;
}

public sealed class SurfaceMap
{
    /// <summary>Window tops closer than this to the top of their screen are useless: the cat would be off-screen.</summary>
    public const int Headroom = 60;

    public const int MinWall = 60;

    public IReadOnlyList<Platform> Platforms { get; }
    public IReadOnlyList<Wall> Walls { get; }
    public IReadOnlyList<RectI> WorkAreas { get; }

    SurfaceMap(IReadOnlyList<Platform> platforms, IReadOnlyList<Wall> walls, IReadOnlyList<RectI> workAreas)
    {
        Platforms = platforms;
        Walls = walls;
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
                        result.Add(new Platform(id++, SurfaceKind.WindowTop, y, a, b, w.Handle, w.Bounds.Left, w.Bounds.Top,
                                                w.Bounds.Right));
                }
        }

        foreach (var p in extra)
            result.Add(p with { Id = id++ });

        var walls = new List<Wall>();
        for (int i = 0; i < windowsTopToBottom.Count; i++)
        {
            var w = windowsTopToBottom[i];
            if (!result.Any(p => p.Owner == w.Handle)) continue;   // no reachable top: nothing to climb to
            foreach (int side in new[] { -1, 1 })
            {
                int x = side < 0 ? w.Bounds.Left : w.Bounds.Right;
                int probe = side < 0 ? x - 1 : x;     // the column the cat's body occupies
                var wa = workAreas.FirstOrDefault(a => probe >= a.Left + 20 && probe < a.Right - 20
                                                        && w.Bounds.Top >= a.Top + Headroom && w.Bounds.Top < a.Bottom);
                if (wa == default) continue;

                var spans = new List<(int, int)> { (w.Bounds.Top, Math.Min(w.Bounds.Bottom, wa.Bottom)) };
                for (int j = 0; j < i && spans.Count > 0; j++)
                {
                    var o = windowsTopToBottom[j].Bounds;
                    if (probe >= o.Left && probe < o.Right) spans = Subtract(spans, o.Top, o.Bottom);
                }
                // Only the stretch that reaches the top corner is useful.
                var top = spans.FirstOrDefault(sp => sp.Item1 == w.Bounds.Top);
                if (top == default || top.Item2 - top.Item1 < MinWall) continue;
                walls.Add(new Wall(x, top.Item1, top.Item2, side, w.Handle, w.Bounds.Left, w.Bounds.Top));
            }
        }

        return new SurfaceMap(result, walls, workAreas);
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

    /// <summary>The same window side after the windows moved, and how far it moved; null if it is gone.</summary>
    public (Wall wall, int dx, int dy)? RelocateWall(Wall old)
    {
        var w = Walls.FirstOrDefault(v => v.Owner == old.Owner && v.Side == old.Side);
        return w == null ? null : (w, w.OwnerX - old.OwnerX, w.OwnerY - old.OwnerY);
    }

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
            int dx = p.OwnerX - old.OwnerX;
            // Resized from one edge (the other stayed): the cat keeps its place.
            bool resized = old.OwnerRight != int.MinValue && p.OwnerRight - old.OwnerRight != dx;
            double nx = x + (resized ? 0 : dx);
            if (p.OwnerY - old.OwnerY == p.Y - old.Y && p.SpansX(nx))
                return (p, nx);
        }
        return null;
    }
}
