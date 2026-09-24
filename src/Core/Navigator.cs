using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

public enum NavStepKind { Walk, Jump, Drop, Climb }

/// <summary>
/// Walk: go to X on the current platform. Jump/Drop: take off from the current position (the X of
/// the previous Walk) and land at LandX on Target. Climb: go up the window side Via and step onto Target at LandX.
/// </summary>
public sealed record NavStep(NavStepKind Kind, double X, double LandX, Platform Target, Wall? Via = null);

public static class Navigator
{
    const double EdgeMargin = 25;
    const double JumpCost = 250;
    /// <summary>How high above its feet the cat can grab a wall.</summary>
    const double WallReach = 200;
    const double WallOffset = 22;

    public static List<NavStep>? FindPath(SurfaceMap map, Platform from, double fromX, Platform to, double toX,
                                          double maxJumpUp, double maxJumpGap)
    {
        toX = MathX.SafeClamp(toX, to.X0 + 1, to.X1 - 1);
        if (from.Id == to.Id)
            return new List<NavStep> { new(NavStepKind.Walk, toX, toX, to) };

        // Dijkstra over platforms; the state keeps where the cat arrives on each one.
        var cost = new Dictionary<int, double> { [from.Id] = 0 };
        var arrival = new Dictionary<int, double> { [from.Id] = fromX };
        var back = new Dictionary<int, (Platform prev, double takeoff, double land, NavStepKind kind, Wall? via)>();
        var open = new PriorityQueue<Platform, double>();
        open.Enqueue(from, 0);
        var done = new HashSet<int>();

        while (open.TryDequeue(out var a, out _))
        {
            if (!done.Add(a.Id)) continue;
            if (a.Id == to.Id) break;
            double ax = arrival[a.Id];

            foreach (var b in map.Platforms)
            {
                if (b.Id == a.Id || done.Contains(b.Id)) continue;
                var hop = Hop(a, ax, b, maxJumpUp, maxJumpGap);
                Wall? via = null;
                if (hop == null)
                {
                    var climb = Climb(map, a, b);
                    if (climb == null) continue;
                    hop = (climb.Value.takeoff, climb.Value.land, NavStepKind.Climb);
                    via = climb.Value.wall;
                }
                var (takeoff, land, kind) = hop.Value;
                double c = cost[a.Id] + Math.Abs(ax - takeoff) + JumpCost + Math.Abs(a.Y - b.Y) * (via != null ? 0.8 : 0.3);
                if (cost.TryGetValue(b.Id, out var old) && old <= c) continue;
                cost[b.Id] = c;
                arrival[b.Id] = land;
                back[b.Id] = (a, takeoff, land, kind, via);
                open.Enqueue(b, c);
            }
        }

        if (!back.ContainsKey(to.Id)) return null;

        var steps = new List<NavStep> { new(NavStepKind.Walk, toX, toX, to) };
        var cur = to;
        while (cur.Id != from.Id)
        {
            var (prev, takeoff, land, kind, via) = back[cur.Id];
            steps.Add(new NavStep(kind, takeoff, land, cur, via));
            steps.Add(new NavStep(NavStepKind.Walk, takeoff, takeoff, prev));
            cur = prev;
        }
        steps.Reverse();
        return steps;
    }

    /// <summary>A window side that leads from platform a up onto window top b, if the cat can grab it from a.</summary>
    static (double takeoff, double land, Wall wall)? Climb(SurfaceMap map, Platform a, Platform b)
    {
        if (b.Kind != SurfaceKind.WindowTop || b.Y >= a.Y) return null;
        foreach (var w in map.Walls)
        {
            if (w.Owner != b.Owner) continue;
            bool corner = w.Side < 0 ? b.X0 == w.X : b.X1 == w.X;
            if (!corner || w.Y0 != b.Y) continue;
            if (w.Y1 < a.Y - WallReach) continue;          // the side ends too high above a
            double takeoff = w.X + w.Side * WallOffset;
            if (!a.SpansX(takeoff)) continue;
            double land = w.Side < 0 ? b.X0 + 2 * EdgeMargin : b.X1 - 2 * EdgeMargin;
            return (takeoff, MathX.SafeClamp(land, b.X0 + 1, b.X1 - 1), w);
        }
        return null;
    }

    /// <summary>Takeoff point on a, landing point on b and kind, or null if the cat cannot make it.</summary>
    static (double takeoff, double land, NavStepKind kind)? Hop(Platform a, double ax, Platform b,
                                                                double maxJumpUp, double maxJumpGap)
    {
        double dy = a.Y - b.Y;   // > 0: b is higher
        if (dy > maxJumpUp) return null;

        double gap = Math.Max(0, Math.Max(b.X0 - a.X1, a.X0 - b.X1));
        double allowedGap = dy >= 0 ? maxJumpGap : maxJumpGap + Math.Min(-dy, 800) * 0.4;
        if (gap > allowedGap) return null;

        double land, takeoff;
        if (gap == 0)
        {
            // Overlapping spans: take off where we already are (inside the shared stretch), and land a fixed
            // hop towards the middle of b. The takeoff must not depend on anything that moves while the cat
            // walks to it, or it keeps running away.
            double lo = Math.Max(a.X0, b.X0), hi = Math.Min(a.X1, b.X1);
            takeoff = MathX.SafeClamp(ax, Math.Max(lo, a.X0 + EdgeMargin), Math.Min(hi, a.X1 - EdgeMargin));
            double dir = b.Center >= takeoff ? 1 : -1;
            land = MathX.SafeClamp(takeoff + dir * 40, b.X0 + EdgeMargin, b.X1 - EdgeMargin);
        }
        else if (b.X0 >= a.X1)
        {
            takeoff = a.X1 - EdgeMargin;
            land = b.X0 + EdgeMargin * 2;
        }
        else
        {
            takeoff = a.X0 + EdgeMargin;
            land = b.X1 - EdgeMargin * 2;
        }

        if (b.X1 - b.X0 < 2 * EdgeMargin) land = b.Center;
        return (takeoff, land, dy >= 0 ? NavStepKind.Jump : NavStepKind.Drop);
    }
}
