using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class NeedsTests
{
    [Fact]
    public void Hunger_and_playfulness_grow_energy_drains_while_awake()
    {
        var n = new Needs { Hunger = 0, Energy = 1, Playfulness = 0 };
        n.Tick(3600, sleeping: false);
        Assert.InRange(n.Hunger, 0.2, 0.8);
        Assert.InRange(n.Energy, 0.2, 0.8);
        Assert.True(n.Playfulness > 0.5);
    }

    [Fact]
    public void Sleeping_restores_energy_and_values_stay_in_range()
    {
        var n = new Needs { Energy = 0.1 };
        n.Tick(100000, sleeping: true);
        Assert.Equal(1, n.Energy);
        Assert.Equal(1, n.Hunger);
        n.Eat(5);
        Assert.Equal(0, n.Hunger);
    }

    [Fact]
    public void Petting_raises_affection_and_play_discharges_playfulness()
    {
        var n = new Needs { Affection = 0, Playfulness = 1 };
        n.Pet(5);
        n.Play(0.7);
        Assert.True(n.Affection > 0.3);
        Assert.InRange(n.Playfulness, 0.29, 0.31);
    }
}

public class CatBodyTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };

    static SurfaceMap Map(params WindowInfo[] w) => SurfaceMap.Build(w, Work, System.Array.Empty<Platform>());

    static void Run(CatBody body, SurfaceMap map, double seconds, double walk = 0)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) body.Step(1 / 30.0, map, walk);
    }

    [Fact]
    public void Dropped_cat_falls_and_lands_on_the_floor()
    {
        var map = Map();
        var body = new CatBody(new Vec2(500, 100));
        Run(body, map, 3);
        Assert.Equal(BodyMode.Grounded, body.Mode);
        Assert.Equal(1040, body.Pos.Y);
        Assert.Equal(SurfaceKind.Floor, body.Support!.Kind);
    }

    [Fact]
    public void Walking_off_a_window_edge_falls_to_the_floor()
    {
        var map = Map(new WindowInfo(1, new RectI(100, 500, 400, 900)));
        var body = new CatBody(new Vec2(380, 400));
        Run(body, map, 2);
        Assert.Equal(500, body.Pos.Y);

        Run(body, map, 3, walk: 120);
        Assert.Equal(BodyMode.Grounded, body.Mode);
        Assert.Equal(1040, body.Pos.Y);
    }

    [Fact]
    public void Jump_lands_on_a_higher_window()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 700, 1200, 1000)));
        var body = new CatBody(new Vec2(600, 1040));
        Run(body, map, 1);
        body.JumpTo(new Vec2(900, 700), 80);
        Assert.Equal(BodyMode.Airborne, body.Mode);
        Run(body, map, 3);
        Assert.Equal(700, body.Pos.Y);
        Assert.InRange(body.Pos.X, 880, 920);
    }

    [Fact]
    public void Cat_follows_a_moved_window_and_falls_when_it_closes()
    {
        var body = new CatBody(new Vec2(300, 400));
        Run(body, Map(new WindowInfo(1, new RectI(100, 500, 800, 900))), 2);

        body.FollowSupport(Map(new WindowInfo(1, new RectI(200, 450, 900, 850))));
        Assert.Equal((400.0, 450.0), (body.Pos.X, body.Pos.Y));

        var empty = Map();
        body.FollowSupport(empty);
        Assert.Equal(BodyMode.Airborne, body.Mode);
        Run(body, empty, 3);
        Assert.Equal(1040, body.Pos.Y);
    }

    [Fact]
    public void Held_cat_is_thrown_on_release()
    {
        var map = Map();
        var body = new CatBody(new Vec2(500, 1040));
        body.Grab();
        body.MoveHeld(new Vec2(600, 300));
        body.Release(new Vec2(400, -200));
        Assert.Equal(BodyMode.Airborne, body.Mode);
        Run(body, map, 4);
        Assert.Equal(BodyMode.Grounded, body.Mode);
        Assert.True(body.Pos.X > 650);
    }

    [Fact]
    public void Cat_stays_inside_the_screens()
    {
        var map = Map();
        var body = new CatBody(new Vec2(1900, 1040));
        Run(body, map, 1);
        Run(body, map, 2, walk: 300);
        Assert.True(body.Pos.X < 1920);
        Assert.True(body.HitWall);
    }
}
