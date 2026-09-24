using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class NavigatorTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };
    static SurfaceMap Map(params WindowInfo[] w) => SurfaceMap.Build(w, Work, System.Array.Empty<Platform>());
    const double Up = 450, Gap = 350;

    [Fact]
    public void Same_platform_is_a_single_walk()
    {
        var map = Map();
        var floor = map.Platforms[0];
        var path = Navigator.FindPath(map, floor, 100, floor, 900, Up, Gap);
        var step = Assert.Single(path!);
        Assert.Equal((NavStepKind.Walk, 900.0), (step.Kind, step.X));
    }

    [Fact]
    public void Reachable_window_top_needs_a_walk_and_a_jump()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 700, 1200, 1000)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);

        var path = Navigator.FindPath(map, floor, 100, top, 1000, Up, Gap)!;

        Assert.Equal(new[] { NavStepKind.Walk, NavStepKind.Jump, NavStepKind.Walk }, path.Select(s => s.Kind));
        Assert.Equal(top, path[1].Target);
        Assert.InRange(path[1].LandX, top.X0, top.X1);
    }

    [Fact]
    public void Too_high_to_jump_means_climbing_a_side()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 200, 1200, 1000)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);

        var path = Navigator.FindPath(map, floor, 100, top, 1000, Up, Gap)!;

        Assert.Contains(path, s => s.Kind == NavStepKind.Climb);
        Assert.DoesNotContain(path, s => s.Kind == NavStepKind.Jump);
    }

    [Fact]
    public void High_window_whose_sides_end_far_above_the_floor_is_unreachable()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 100, 1200, 400)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);

        Assert.Null(Navigator.FindPath(map, floor, 100, top, 1000, Up, Gap));
    }

    [Fact]
    public void Chains_jumps_through_an_intermediate_window()
    {
        var map = Map(
            new WindowInfo(1, new RectI(1300, 250, 1700, 500)),
            new WindowInfo(2, new RectI(800, 650, 1250, 1000)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var high = map.Platforms.First(p => p.Owner == 1);

        var path = Navigator.FindPath(map, floor, 100, high, 1500, Up, Gap)!;

        Assert.Equal(2, path.Count(s => s.Kind == NavStepKind.Jump));
        Assert.Equal(high, path.Last().Target);
    }

    [Fact]
    public void Going_down_is_a_drop()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 300, 1200, 1000)));
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);

        var path = Navigator.FindPath(map, top, 1000, floor, 200, Up, Gap)!;

        Assert.Contains(path, s => s.Kind == NavStepKind.Drop && s.Target == floor);
        Assert.Equal(200, path.Last().X);
    }
}

public class NavigatorOverlapTests
{
    [Fact]
    public void Dropping_onto_a_surface_right_below_takes_off_where_the_cat_already_is()
    {
        // A window top right above the perch: the spans overlap, the drop must start on the spot,
        // wherever the cat stands (otherwise the takeoff point runs away as the cat walks to it).
        var perch = new Platform(0, SurfaceKind.Perch, 490, 859, 983, -1, 921, 490);
        var map = SurfaceMap.Build(new[] { new WindowInfo(1, new RectI(857, 150, 1600, 700)) },
                                   new[] { new RectI(0, 0, 1920, 1040) }, new[] { perch });
        var top = map.Platforms.First(p => p.Owner == 1);
        var p2 = map.Platforms.First(p => p.Kind == SurfaceKind.Perch);

        foreach (double x in new[] { 900.0, 930, 940, 941, 950, 960 })
        {
            var path = Navigator.FindPath(map, top, x, p2, 920, 460, 340)!;
            Assert.Equal(NavStepKind.Drop, path[1].Kind);
            Assert.Equal(x, path[0].X, 1);
        }
    }
}
