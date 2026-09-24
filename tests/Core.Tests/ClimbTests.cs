using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class ClimbTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };
    static SurfaceMap Map(params WindowInfo[] w) => SurfaceMap.Build(w, Work, Array.Empty<Platform>());
    const double Up = 460, Gap = 340;

    [Fact]
    public void A_window_has_climbable_sides_from_its_top_down()
    {
        var map = Map(new WindowInfo(1, new RectI(600, 300, 1200, 1040)));

        var left = map.Walls.Single(w => w.Owner == 1 && w.Side == -1);
        var right = map.Walls.Single(w => w.Owner == 1 && w.Side == 1);
        Assert.Equal((600, 300, 1040), (left.X, left.Y0, left.Y1));
        Assert.Equal((1200, 300, 1040), (right.X, right.Y0, right.Y1));
    }

    [Fact]
    public void A_window_in_front_cuts_the_side_below_it()
    {
        var map = Map(
            new WindowInfo(2, new RectI(400, 700, 900, 1000)),     // covers the left side from y=700
            new WindowInfo(1, new RectI(600, 300, 1200, 1040)));

        var left = map.Walls.Single(w => w.Owner == 1 && w.Side == -1);
        Assert.Equal((300, 700), (left.Y0, left.Y1));
    }

    [Fact]
    public void Side_hidden_at_the_top_is_not_climbable()
    {
        var map = Map(
            new WindowInfo(2, new RectI(400, 200, 900, 500)),
            new WindowInfo(1, new RectI(600, 300, 1200, 1040)));

        Assert.DoesNotContain(map.Walls, w => w.Owner == 1 && w.Side == -1);
    }

    [Fact]
    public void Navigator_climbs_to_a_window_too_high_to_jump()
    {
        var map = Map(new WindowInfo(1, new RectI(600, 300, 1200, 1040)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);

        var path = Navigator.FindPath(map, floor, 200, top, 900, Up, Gap)!;

        var climb = Assert.Single(path, s => s.Kind == NavStepKind.Climb);
        Assert.NotNull(climb.Via);
        Assert.Equal(-1, climb.Via!.Side);
        Assert.InRange(path[0].X, 560, 600);   // walks up to the left side first
    }

    [Fact]
    public void Body_climbs_and_mounts_the_top()
    {
        var map = Map(new WindowInfo(1, new RectI(600, 300, 1200, 1040)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);
        var wall = map.Walls.Single(w => w.Side == -1);
        var body = new CatBody(new Vec2(580, 1040));
        body.PlaceOn(floor, 580);

        body.StartClimb(wall, top, 630);
        Assert.Equal(BodyMode.Climbing, body.Mode);
        for (int i = 0; i < 30 * 10 && body.Mode == BodyMode.Climbing; i++) body.Step(1 / 30.0, map, 0);

        Assert.Equal(BodyMode.Grounded, body.Mode);
        Assert.Equal(300, body.Pos.Y);
        Assert.Equal(top.Owner, body.Support!.Owner);
    }

    [Fact]
    public void Climbing_cat_falls_when_the_window_closes_and_follows_when_it_moves()
    {
        var map = Map(new WindowInfo(1, new RectI(600, 300, 1200, 1040)));
        var body = new CatBody(new Vec2(580, 1040));
        body.PlaceOn(map.Platforms.First(p => p.Kind == SurfaceKind.Floor), 580);
        body.StartClimb(map.Walls.Single(w => w.Side == -1), map.Platforms.First(p => p.Owner == 1), 630);
        for (int i = 0; i < 30; i++) body.Step(1 / 30.0, map, 0);
        double y = body.Pos.Y;

        body.FollowSupport(Map(new WindowInfo(1, new RectI(650, 280, 1250, 1020))));
        Assert.Equal(BodyMode.Climbing, body.Mode);
        Assert.Equal(y - 20, body.Pos.Y, 3);

        body.FollowSupport(Map());
        Assert.Equal(BodyMode.Airborne, body.Mode);
    }
}

public class ClimbBrainTests
{
    sealed class World : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX => 1700;
        public double BowlFood => 1;
        public Platform? PerchTop => null;
        public double PerchX => 0;
        public Vec2? TreatPos => null;
        public bool TreatLanded => false;
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() { }
    }

    [Fact]
    public void Brain_climbs_a_tall_window_and_ends_up_on_top()
    {
        var map = SurfaceMap.Build(new[] { new WindowInfo(1, new RectI(600, 250, 1200, 1040)) },
                                   new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);
        var body = new CatBody(new Vec2(200, 1040));
        body.PlaceOn(floor, 200);
        var brain = new CatBrain(new Random(1));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 0 };
        var world = new World { BowlPlatform = floor };
        brain.ExploreTo(top, 900);

        bool climbed = false;
        for (int i = 0; i < 30 * 30 && body.Support?.Owner != 1; i++)
        {
            brain.Update(1 / 30.0, body, needs, map, world);
            climbed |= brain.State == CatState.Climb && brain.Action == "climb" && brain.Facing == 1;
        }

        Assert.True(climbed);
        Assert.Equal(250, body.Pos.Y);
    }
}

public class DropThroughTests
{
    [Fact]
    public void A_targeted_drop_passes_in_front_of_the_surface_it_left()
    {
        // The perch stands behind a window, entirely within its width: the cat hops down in front of it.
        var perch = new Platform(0, SurfaceKind.Perch, 490, 859, 983, -1, 921, 490);
        var map = SurfaceMap.Build(new[] { new WindowInfo(1, new RectI(707, 200, 1300, 720)) },
                                   new[] { new RectI(0, 0, 1920, 1040) }, new[] { perch });
        var top = map.Platforms.First(p => p.Owner == 1);
        var body = new CatBody(new Vec2(930, 200));
        body.PlaceOn(top, 930);

        body.JumpTo(new Vec2(900, 490), 15);
        for (int i = 0; i < 90 && body.Mode != BodyMode.Grounded; i++) body.Step(1 / 30.0, map, 0);

        Assert.Equal(SurfaceKind.Perch, body.Support!.Kind);
        Assert.Equal(490, body.Pos.Y);
    }
}
