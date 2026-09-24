using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class CatBrainTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };

    sealed class FakeWorld : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX { get; set; } = 1500;
        public double BowlFood { get; set; } = 1;
        public Platform? PerchTop { get; set; }
        public double PerchX { get; set; }
        public Vec2? TreatPos { get; set; }
        public bool TreatLanded { get; set; }
        public int TreatsEaten;

        public void EatFromBowl(double amount) => BowlFood = Math.Max(0, BowlFood - amount);
        public void ConsumeTreat() { TreatPos = null; TreatsEaten++; }
    }

    sealed class Sim
    {
        public readonly SurfaceMap Map;
        public readonly FakeWorld World = new();
        public readonly Needs Needs = new() { Hunger = 0.1, Energy = 0.9, Playfulness = 0.1 };
        public readonly CatBody Body;
        public readonly CatBrain Brain = new(new Random(1));

        public Sim(double x = 200, params Platform[] extra)
        {
            Map = SurfaceMap.Build(Array.Empty<WindowInfo>(), Work, extra);
            World.BowlPlatform = Map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
            Body = new CatBody(new Vec2(x, 1040));
            Body.PlaceOn(World.BowlPlatform, x);
        }

        public bool RunUntil(Func<bool> done, double maxSeconds)
        {
            for (double t = 0; t < maxSeconds; t += 1 / 30.0)
            {
                Brain.Update(1 / 30.0, Body, Needs, Map, World);
                if (done()) return true;
            }
            return false;
        }
    }

    [Fact]
    public void Hungry_cat_walks_to_a_full_bowl_and_eats()
    {
        var sim = new Sim();
        sim.Needs.Hunger = 0.9;

        Assert.True(sim.RunUntil(() => sim.Brain.State == CatState.Eat, 30));
        Assert.InRange(Math.Abs(sim.Body.Pos.X - sim.World.BowlX), 0, 80);

        Assert.True(sim.RunUntil(() => sim.Needs.Hunger < 0.1, 30));
        Assert.True(sim.World.BowlFood < 1);
    }

    [Fact]
    public void Hungry_cat_meows_at_an_empty_bowl()
    {
        var sim = new Sim();
        sim.Needs.Hunger = 0.9;
        sim.World.BowlFood = 0;

        Assert.True(sim.RunUntil(() => sim.Brain.State == CatState.Meow, 30));
    }

    [Fact]
    public void Tired_cat_climbs_the_perch_and_sleeps()
    {
        var perch = new Platform(0, SurfaceKind.Perch, 800, 1000, 1110, -1, 1000, 800);
        var sim = new Sim(200, perch);
        sim.World.PerchTop = sim.Map.Platforms.First(p => p.Kind == SurfaceKind.Perch);
        sim.World.PerchX = 1055;
        sim.Needs.Energy = 0.1;

        Assert.True(sim.RunUntil(() => sim.Brain.State == CatState.Sleep, 40));
        Assert.Equal(800, sim.Body.Pos.Y);
        Assert.Equal("sleep", sim.Brain.Action);

        Assert.True(sim.RunUntil(() => sim.Brain.State != CatState.Sleep, 4000));
        Assert.True(sim.Needs.Energy > 0.9);
    }

    [Fact]
    public void Cat_chases_and_eats_a_treat()
    {
        var sim = new Sim();
        sim.World.TreatPos = new Vec2(1200, 1040);
        sim.World.TreatLanded = true;

        Assert.True(sim.RunUntil(() => sim.World.TreatsEaten == 1, 30));
    }

    [Fact]
    public void Restless_cat_gets_the_zoomies_and_calms_down()
    {
        var sim = new Sim();
        sim.Needs.Playfulness = 1;

        Assert.True(sim.RunUntil(() => sim.Brain.State == CatState.Zoomies, 10));
        Assert.Equal("run", sim.Brain.Action);
        Assert.True(sim.RunUntil(() => sim.Brain.State != CatState.Zoomies, 30));
        Assert.True(sim.Needs.Playfulness < 0.5);
    }

    [Fact]
    public void Petting_a_calm_cat_makes_it_purr()
    {
        var sim = new Sim();
        sim.RunUntil(() => false, 1);

        for (int i = 0; i < 60; i++)
        {
            sim.Brain.OnPetting(1 / 30.0);
            sim.Brain.Update(1 / 30.0, sim.Body, sim.Needs, sim.Map, sim.World);
        }

        Assert.Equal(CatState.Petted, sim.Brain.State);
        Assert.Equal("purr", sim.Brain.Action);
    }

    [Fact]
    public void Grabbed_cat_is_held_then_falls_and_lands()
    {
        var sim = new Sim();
        sim.Brain.OnGrab(sim.Body);
        sim.Body.MoveHeld(new Vec2(600, 400));
        sim.Brain.Update(1 / 30.0, sim.Body, sim.Needs, sim.Map, sim.World);
        Assert.Equal(("held", CatState.Held), (sim.Brain.Action, sim.Brain.State));

        sim.Brain.OnRelease(sim.Body, new Vec2(0, 0));
        Assert.True(sim.RunUntil(() => sim.Body.Mode == BodyMode.Grounded, 5));
        Assert.Equal(1040, sim.Body.Pos.Y);
    }

    [Fact]
    public void Half_an_hour_among_moving_windows_stays_sane_and_explores()
    {
        var rng = new Random(7);
        var world = new FakeWorld { BowlFood = 1 };
        var needs = new Needs();
        var brain = new CatBrain(new Random(3));
        var windows = new[]
        {
            new WindowInfo(1, new RectI(200, 600, 900, 1000)),
            new WindowInfo(2, new RectI(1000, 750, 1700, 1040)),
            new WindowInfo(3, new RectI(700, 350, 1300, 700)),
        };
        // A perch standing right under window 3: overlapping surfaces used to make the cat dither forever.
        var perch = new[] { new Platform(0, SurfaceKind.Perch, 810, 1000, 1124, -1, 1062, 810) };
        SurfaceMap Build() => SurfaceMap.Build(windows, Work, perch);
        var map = Build();
        world.BowlPlatform = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var body = new CatBody(new Vec2(100, 1040));
        body.PlaceOn(world.BowlPlatform, 100);
        bool visitedWindow = false;
        double travelling = 0, longestTrip = 0;

        for (int frame = 0; frame < 30 * 1800; frame++)
        {
            if (frame % 4 == 0)
            {
                // Every few seconds a window moves; now and then one closes or reopens.
                if (rng.NextDouble() < 0.02)
                {
                    int i = rng.Next(windows.Length);
                    var b = windows[i].Bounds;
                    int dx = rng.Next(-60, 61), dy = rng.Next(-40, 41);
                    windows[i] = windows[i] with { Bounds = new RectI(b.Left + dx, Math.Max(100, b.Top + dy), b.Right + dx, b.Bottom + dy) };
                }
                if (rng.NextDouble() < 0.002)
                    windows = windows.Length == 3 ? windows.Take(2).ToArray()
                                                  : windows.Append(new WindowInfo(3, new RectI(700, 350, 1300, 700))).ToArray();
                map = Build();
                world.BowlPlatform = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
                body.FollowSupport(map);
            }
            brain.Update(1 / 30.0, body, needs, map, world);
            if (body.Support?.Kind == SurfaceKind.WindowTop) visitedWindow = true;
            travelling = brain.State is CatState.Travel or CatState.ChaseTreat ? travelling + 1 / 30.0 : 0;
            longestTrip = Math.Max(longestTrip, travelling);
            Assert.InRange(body.Pos.X, 0, 1920);
            Assert.True(body.Pos.Y <= 1041, $"y={body.Pos.Y} at frame {frame}");
        }

        Assert.True(visitedWindow);
        Assert.True(longestTrip < 30, $"a trip lasted {longestTrip:0} s");
    }

    [Fact]
    public void Wandering_cat_never_leaves_the_screen_in_ten_minutes()
    {
        var sim = new Sim();
        sim.World.BowlFood = 1;
        for (double t = 0; t < 600; t += 1 / 30.0)
        {
            sim.Brain.Update(1 / 30.0, sim.Body, sim.Needs, sim.Map, sim.World);
            Assert.InRange(sim.Body.Pos.X, 0, 1920);
            Assert.InRange(sim.Body.Pos.Y, -2000, 1041);
        }
    }
}
