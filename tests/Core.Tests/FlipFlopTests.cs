using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;
using Xunit.Abstractions;

namespace ZairaPet.Tests;

/// <summary>A cat never gets stuck turning left, right, left on the spot.</summary>
public class FlipFlopTests
{
    readonly ITestOutputHelper _out;
    public FlipFlopTests(ITestOutputHelper output) => _out = output;

    sealed class World : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX { get; set; } = 300;
        public double BowlFood { get; set; } = 1;
        public Platform? PerchTop => null;
        public double PerchX => 0;
        public Vec2? TreatPos => null;
        public bool TreatLanded => false;
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() { }
    }

    public static IEnumerable<object[]> Cases() =>
        Enumerable.Range(1, 12).Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Never_flips_back_and_forth_on_the_spot(int seed)
    {
        var rng = new Random(seed);
        var windows = new[] { new WindowInfo(1, new RectI(300, 700, 900, 1040)), new WindowInfo(2, new RectI(1200, 500, 1700, 1040)) };
        var map = SurfaceMap.Build(windows, new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var body = new CatBody(new Vec2(60, 1040));   // start near the left screen edge
        body.PlaceOn(floor, 60);
        var brain = new CatBrain(new Random(seed));
        var needs = new Needs { Hunger = 0.2, Energy = 1, Playfulness = 1 };
        var world = new World { BowlPlatform = floor };

        var flips = new List<(double t, double x)>();
        int facing = brain.Facing;
        double t = 0;
        Vec2? cursor = null;
        var trace = new Queue<string>();
        for (int i = 0; i < 30 * 600; i++, t += 1 / 30.0)
        {
            if (i % (30 * 40) == 0) needs.Playfulness = rng.NextDouble();
            if (i % (30 * 25) == 0) cursor = rng.NextDouble() < 0.5 ? null : body.Pos + new Vec2(rng.Next(-250, 250), -rng.Next(0, 120));
            brain.Cursor = cursor is { } c ? c + new Vec2(3 * Math.Sin(t * 8), 0) : null;
            brain.Update(1 / 30.0, body, needs, map, world);
            trace.Enqueue($"t={t:0.00} x={body.Pos.X:0.0} vx={body.Vel.X:0} f={brain.Facing} {brain.State}/{brain.Action} on={body.Support?.Kind}:{body.Support?.X0}-{body.Support?.X1} hitwall={body.HitWall}");
            if (trace.Count > 45) trace.Dequeue();
            if (brain.Facing != facing)
            {
                facing = brain.Facing;
                flips.Add((t, body.Pos.X));
                // four turns within two seconds, without getting anywhere
                var recent = flips.Where(f => t - f.t < 2).ToList();
                if (recent.Count >= 4 && recent.Max(f => f.x) - recent.Min(f => f.x) < 30)
                {
                    foreach (var line in trace) _out.WriteLine(line);
                    _out.WriteLine($"seed {seed} t={t:0.0} x={body.Pos.X:0} state={brain.State}/{brain.Action} goal={brain.Goal}");
                    Assert.Fail($"flip-flop at t={t:0.0}s, state {brain.State}/{brain.Action}, goal {brain.Goal}, x {body.Pos.X:0}");
                }
            }
        }
    }
}
