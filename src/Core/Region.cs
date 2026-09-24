using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

/// <summary>
/// Turns the rectangles of everything visible into the single polygon Godot accepts as window region.
/// On Windows that region both clips drawing and decides which clicks the overlay takes, so it must cover
/// every visible object and nothing else.
/// </summary>
public static class Region
{
    /// <summary>Merge overlapping or touching rectangles into their bounding boxes until all are disjoint.</summary>
    public static List<RectI> Merge(IEnumerable<RectI> rects, int snap = 8)
    {
        var list = rects.Where(r => r.Width > 0 && r.Height > 0).Select(r => Snap(r, snap)).ToList();
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < list.Count && !merged; i++)
                for (int j = i + 1; j < list.Count && !merged; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    if (a.Left <= b.Right && b.Left <= a.Right && a.Top <= b.Bottom && b.Top <= a.Bottom)
                    {
                        list[i] = new RectI(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top),
                                            Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
                        list.RemoveAt(j);
                        merged = true;
                    }
                }
        }
        return list.OrderBy(r => r.Left).ThenBy(r => r.Top).ToList();
    }

    /// <summary>
    /// One polygon whose even-odd fill equals the union of disjoint rectangles: every rectangle is walked
    /// from a common anchor and back along the same line, so the bridges enclose no area.
    /// </summary>
    public static List<(int x, int y)> Polygon(IReadOnlyList<RectI> disjoint)
    {
        var pts = new List<(int, int)>();
        if (disjoint.Count == 0) return pts;
        var anchor = (disjoint[0].Left, disjoint[0].Top);
        foreach (var r in disjoint)
        {
            pts.Add(anchor);
            pts.Add((r.Left, r.Top));
            pts.Add((r.Right, r.Top));
            pts.Add((r.Right, r.Bottom));
            pts.Add((r.Left, r.Bottom));
            pts.Add((r.Left, r.Top));
        }
        pts.Add(anchor);
        return pts;
    }

    /// <summary>Even-odd point-in-polygon, the rule Windows uses for ALTERNATE regions.</summary>
    public static bool Contains(IReadOnlyList<(int x, int y)> poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > y) != (yj > y) && x < (double)(xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    static RectI Snap(RectI r, int s) => s <= 1 ? r : new RectI(
        (int)Math.Floor(r.Left / (double)s) * s, (int)Math.Floor(r.Top / (double)s) * s,
        (int)Math.Ceiling(r.Right / (double)s) * s, (int)Math.Ceiling(r.Bottom / (double)s) * s);
}
