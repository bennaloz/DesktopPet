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
    public void Too_high_window_is_unreachable_from_the_floor()
    {
        var map = Map(new WindowInfo(1, new RectI(800, 200, 1200, 1000)));
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
