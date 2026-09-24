using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>Regressions for the issues found in the code review of 2026-09-24.</summary>
public class ReviewFixTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };
    static SurfaceMap Map(params WindowInfo[] w) => SurfaceMap.Build(w, Work, Array.Empty<Platform>());

    static void Run(CatBody body, SurfaceMap map, double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) body.Step(1 / 30.0, map, 0);
    }

    [Fact]
    public void A_click_without_moving_does_not_drop_the_cat_through_its_window()
    {
        var map = Map(new WindowInfo(1, new RectI(300, 500, 1200, 900)));
        var body = new CatBody(new Vec2(700, 500));
        body.PlaceOn(map.Platforms.First(p => p.Owner == 1), 700);

        body.Grab();
        body.MoveHeld(new Vec2(700, 586));          // held by the scruff: the feet hang below the cursor
        body.Release(body.HeldVelocity, map);
        Run(body, map, 3);

        Assert.Equal(500, body.Pos.Y);
        Assert.Equal(1L, body.Support!.Owner);
    }

    [Fact]
    public void Releasing_on_the_floor_does_not_sink_below_it()
    {
        var map = Map();
        var body = new CatBody(new Vec2(700, 1040));
        body.PlaceOn(map.Platforms[0], 700);

        body.Grab();
        body.MoveHeld(new Vec2(700, 1120));
        body.Release(new Vec2(0, 0), map);
        Run(body, map, 1);

        Assert.Equal(BodyMode.Grounded, body.Mode);
        Assert.Equal(1040, body.Pos.Y);
    }

    [Fact]
    public void Hand_speed_fades_when_the_mouse_stops()
    {
        var body = new CatBody(new Vec2(500, 500));
        body.Grab();
        body.MoveHeld(new Vec2(500, 500));
        for (int i = 1; i <= 5; i++) body.MoveHeld(new Vec2(500 + 60 * i, 500));
        Assert.True(body.HeldVelocity.Length > 500);

        for (int i = 0; i < 30; i++) body.TickHeld(1 / 30.0);
        Assert.True(body.HeldVelocity.Length < 20);
    }

    [Fact]
    public void Resizing_a_window_from_its_left_edge_does_not_drag_the_cat()
    {
        var before = Map(new WindowInfo(1, new RectI(300, 500, 1200, 900)));
        var body = new CatBody(new Vec2(900, 500));
        body.PlaceOn(before.Platforms.First(p => p.Owner == 1), 900);

        body.FollowSupport(Map(new WindowInfo(1, new RectI(500, 500, 1200, 900))));
        Assert.Equal(900, body.Pos.X);

        body.FollowSupport(Map(new WindowInfo(1, new RectI(600, 480, 1300, 880))));   // a real move
        Assert.Equal((1000.0, 480.0), (body.Pos.X, body.Pos.Y));
    }

    sealed class World : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX { get; set; }
        public double BowlFood { get; set; } = 1;
        public Platform? PerchTop { get; set; }
        public double PerchX { get; set; }
        public Vec2? TreatPos { get; set; }
        public bool TreatLanded { get; set; }
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() => TreatPos = null;
    }

    static (SurfaceMap map, Platform high) Unreachable()
    {
        // A window top high up whose sides end far above the floor: no jump, no climb.
        var map = Map(new WindowInfo(1, new RectI(800, 100, 1200, 400)));
        return (map, map.Platforms.First(p => p.Owner == 1));
    }

    [Fact]
    public void An_unreachable_treat_does_not_stop_the_cat_from_sleeping()
    {
        var (map, high) = Unreachable();
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var world = new World { BowlPlatform = floor, BowlX = 1500, TreatPos = new Vec2(1000, high.Y), TreatLanded = true };
        var body = new CatBody(new Vec2(300, 1040));
        body.PlaceOn(floor, 300);
        var brain = new CatBrain(new Random(2));
        var needs = new Needs { Hunger = 0, Energy = 0.1, Playfulness = 0 };

        bool slept = false;
        for (int i = 0; i < 30 * 120 && !slept; i++)
        {
            brain.Update(1 / 30.0, body, needs, map, world);
            slept = brain.State == CatState.Sleep;
        }
        Assert.True(slept);
    }

    [Fact]
    public void An_unreachable_bowl_does_not_keep_the_cat_meowing_forever()
    {
        var (map, high) = Unreachable();
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var world = new World { BowlPlatform = high, BowlX = 1000 };
        var body = new CatBody(new Vec2(300, 1040));
        body.PlaceOn(floor, 300);
        var brain = new CatBrain(new Random(2));
        var needs = new Needs { Hunger = 0.9, Energy = 0.1, Playfulness = 0 };

        bool slept = false;
        for (int i = 0; i < 30 * 200 && !slept; i++)
        {
            brain.Update(1 / 30.0, body, needs, map, world);
            slept = brain.State == CatState.Sleep;
        }
        Assert.True(slept);
    }

    [Fact]
    public void A_climbing_cat_steps_off_at_the_same_spot_when_the_window_is_resized()
    {
        var map = Map(new WindowInfo(1, new RectI(600, 300, 1200, 1040)));
        var body = new CatBody(new Vec2(580, 1040));
        body.PlaceOn(map.Platforms.First(p => p.Kind == SurfaceKind.Floor), 580);
        body.StartClimb(map.Walls.Single(w => w.Side == -1), map.Platforms.First(p => p.Owner == 1), 650);

        var resized = Map(new WindowInfo(1, new RectI(600, 300, 1500, 1040)));   // right edge dragged
        body.FollowSupport(resized);
        Run(body, resized, 6);

        Assert.Equal(300, body.Pos.Y);
        Assert.Equal(650, body.Pos.X);
    }
}

public class ZoomiesAtTheEdgeTests
{
    sealed class World : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX => 100;
        public double BowlFood => 1;
        public Platform? PerchTop => null;
        public double PerchX => 0;
        public Vec2? TreatPos => null;
        public bool TreatLanded => false;
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() { }
    }

    [Fact]
    public void Zoomies_on_a_narrow_screen_keep_bumping_into_the_edges_without_crashing()
    {
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 260, 700) }, Array.Empty<Platform>());
        var floor = map.Platforms[0];
        var body = new CatBody(new Vec2(130, 700));
        body.PlaceOn(floor, 130);
        var brain = new CatBrain(new Random(4));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 1 };
        var world = new World { BowlPlatform = floor };

        for (int i = 0; i < 30 * 120; i++)
        {
            if (i % 300 == 0) needs.Playfulness = 1;
            brain.Update(1 / 30.0, body, needs, map, world);
            Assert.False(double.IsNaN(body.Pos.X));
        }
    }
}
