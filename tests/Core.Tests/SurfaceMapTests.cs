using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class SurfaceMapTests
{
    // One 1920x1080 monitor with a 40 px taskbar at the bottom.
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };

    static SurfaceMap Build(params WindowInfo[] windowsTopToBottom) =>
        SurfaceMap.Build(windowsTopToBottom, Work, System.Array.Empty<Platform>());

    [Fact]
    public void WorkArea_bottom_is_the_floor()
    {
        var map = Build();

        var floor = Assert.Single(map.Platforms);
        Assert.Equal(SurfaceKind.Floor, floor.Kind);
        Assert.Equal(1040, floor.Y);
        Assert.Equal((0, 1920), (floor.X0, floor.X1));
    }

    [Fact]
    public void Window_top_edge_becomes_a_platform_owned_by_the_window()
    {
        var map = Build(new WindowInfo(42, new RectI(100, 300, 900, 800)));

        var top = map.Platforms.Single(p => p.Kind == SurfaceKind.WindowTop);
        Assert.Equal((300, 100, 900, 42L), (top.Y, top.X0, top.X1, top.Owner));
        Assert.Equal((100, 300), (top.OwnerX, top.OwnerY));
    }

    [Fact]
    public void Window_above_splits_the_edge_of_a_window_below()
    {
        var map = Build(
            new WindowInfo(1, new RectI(400, 200, 600, 700)),   // on top, crosses the edge at y=300
            new WindowInfo(2, new RectI(100, 300, 900, 800)));

        var segs = map.Platforms.Where(p => p.Owner == 2).OrderBy(p => p.X0).ToList();
        Assert.Equal(2, segs.Count);
        Assert.Equal((100, 400), (segs[0].X0, segs[0].X1));
        Assert.Equal((600, 900), (segs[1].X0, segs[1].X1));
    }

    [Fact]
    public void Window_starting_below_the_edge_does_not_hide_it()
    {
        var map = Build(
            new WindowInfo(1, new RectI(400, 350, 600, 700)),
            new WindowInfo(2, new RectI(100, 300, 900, 800)));

        var seg = Assert.Single(map.Platforms, p => p.Owner == 2);
        Assert.Equal((100, 900), (seg.X0, seg.X1));
    }

    [Fact]
    public void Fully_covered_edge_and_too_narrow_leftovers_are_dropped()
    {
        var map = Build(
            new WindowInfo(1, new RectI(0, 100, 1900, 1000)),
            new WindowInfo(2, new RectI(100, 300, 1910, 800)));   // only 10 px stick out

        Assert.DoesNotContain(map.Platforms, p => p.Owner == 2);
    }

    [Fact]
    public void Edge_too_close_to_the_top_of_the_screen_is_ignored()
    {
        var map = Build(new WindowInfo(7, new RectI(0, 0, 1920, 1040)));   // maximized

        Assert.DoesNotContain(map.Platforms, p => p.Owner == 7);
    }

    [Fact]
    public void Edge_is_clipped_to_the_screen()
    {
        var map = Build(new WindowInfo(3, new RectI(-200, 400, 300, 900)));

        var seg = Assert.Single(map.Platforms, p => p.Owner == 3);
        Assert.Equal((0, 300), (seg.X0, seg.X1));
    }

    [Fact]
    public void Extra_platforms_are_kept()
    {
        var perch = new Platform(0, SurfaceKind.Perch, 800, 1500, 1600, -1, 1500, 800);
        var map = SurfaceMap.Build(System.Array.Empty<WindowInfo>(), Work, new[] { perch });

        Assert.Contains(map.Platforms, p => p.Kind == SurfaceKind.Perch && p.Y == 800);
    }

    [Fact]
    public void SupportAt_and_FirstBelow_find_the_right_platform()
    {
        var map = Build(new WindowInfo(9, new RectI(100, 300, 900, 800)));

        Assert.Equal(SurfaceKind.WindowTop, map.SupportAt(500, 301, 3)!.Kind);
        Assert.Null(map.SupportAt(500, 320, 3));
        Assert.Equal(SurfaceKind.WindowTop, map.FirstBelow(500, 100)!.Kind);
        Assert.Equal(SurfaceKind.Floor, map.FirstBelow(500, 310)!.Kind);
        Assert.Equal(SurfaceKind.Floor, map.FirstBelow(50, 100)!.Kind);
    }

    [Fact]
    public void Relocate_follows_a_moved_window_and_fails_when_it_is_gone()
    {
        var before = Build(new WindowInfo(5, new RectI(100, 300, 900, 800)));
        var standing = before.Platforms.Single(p => p.Owner == 5);

        var moved = Build(new WindowInfo(5, new RectI(150, 250, 950, 750)));
        var r = moved.Relocate(standing, 400);
        Assert.NotNull(r);
        Assert.Equal(250, r!.Value.platform.Y);
        Assert.Equal(450, r.Value.x);

        var gone = Build();
        Assert.Null(gone.Relocate(standing, 400));
    }

    [Fact]
    public void Relocate_on_the_floor_keeps_the_same_screen()
    {
        var two = new[] { new RectI(0, 0, 1920, 1040), new RectI(1920, 0, 3840, 1080) };
        var map = SurfaceMap.Build(System.Array.Empty<WindowInfo>(), two, System.Array.Empty<Platform>());
        var floor2 = map.Platforms.Single(p => p.Kind == SurfaceKind.Floor && p.X0 == 1920);

        var r = map.Relocate(floor2, 2500);
        Assert.Equal((1080, 2500.0), (r!.Value.platform.Y, r.Value.x));
    }

    [Fact]
    public void Two_monitors_give_two_floors()
    {
        var map = SurfaceMap.Build(System.Array.Empty<WindowInfo>(),
            new[] { new RectI(0, 0, 1920, 1040), new RectI(1920, 0, 3840, 1080) },
            System.Array.Empty<Platform>());

        Assert.Equal(2, map.Platforms.Count(p => p.Kind == SurfaceKind.Floor));
        Assert.Equal(1080, map.FirstBelow(2500, 0)!.Y);
    }
}
