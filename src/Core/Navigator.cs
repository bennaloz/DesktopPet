using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

public enum NavStepKind { Walk, Jump, Drop }

/// <summary>
/// Walk: go to X on the current platform. Jump/Drop: take off from the current position (the X of
/// the previous Walk) and land at LandX on Target.
/// </summary>
public sealed record NavStep(NavStepKind Kind, double X, double LandX, Platform Target);

public static class Navigator
{
    const double EdgeMargin = 25;
    const double JumpCost = 250;

    public static List<NavStep>? FindPath(SurfaceMap map, Platform from, double fromX, Platform to, double toX,
                                          double maxJumpUp, double maxJumpGap)
    {
        toX = Math.Clamp(toX, to.X0 + 1, to.X1 - 1);
        if (from.Id == to.Id)
            return new List<NavStep> { new(NavStepKind.Walk, toX, toX, to) };

        // Dijkstra over platforms; the state keeps where the cat arrives on each one.
        var cost = new Dictionary<int, double> { [from.Id] = 0 };
        var arrival = new Dictionary<int, double> { [from.Id] = fromX };
        var back = new Dictionary<int, (Platform prev, double takeoff, double land, NavStepKind kind)>();
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
                if (hop == null) continue;
                var (takeoff, land, kind) = hop.Value;
                double c = cost[a.Id] + Math.Abs(ax - takeoff) + JumpCost + Math.Abs(a.Y - b.Y) * 0.3;
                if (cost.TryGetValue(b.Id, out var old) && old <= c) continue;
                cost[b.Id] = c;
                arrival[b.Id] = land;
                back[b.Id] = (a, takeoff, land, kind);
                open.Enqueue(b, c);
            }
        }

        if (!back.ContainsKey(to.Id)) return null;

        var steps = new List<NavStep> { new(NavStepKind.Walk, toX, toX, to) };
        var cur = to;
        while (cur.Id != from.Id)
        {
            var (prev, takeoff, land, kind) = back[cur.Id];
            steps.Add(new NavStep(kind, takeoff, land, cur));
            steps.Add(new NavStep(NavStepKind.Walk, takeoff, takeoff, prev));
            cur = prev;
        }
        steps.Reverse();
        return steps;
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
            // Overlapping spans: land right next to where we are, a short hop sideways.
            land = Math.Clamp(ax, Math.Max(a.X0, b.X0) + EdgeMargin, Math.Min(a.X1, b.X1) - EdgeMargin);
            if (Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) < 2 * EdgeMargin)
                land = (Math.Max(a.X0, b.X0) + Math.Min(a.X1, b.X1)) / 2;
            // Jumping straight up/down looks odd: offset the takeoff a little.
            takeoff = Math.Clamp(land - 60 * Math.Sign(land - a.Center + 0.1), a.X0 + EdgeMargin, a.X1 - EdgeMargin);
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
