using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class RegionTests
{
    [Fact]
    public void Overlapping_rects_merge_and_far_ones_stay_apart()
    {
        var merged = Region.Merge(new[]
        {
            new RectI(100, 100, 200, 200), new RectI(180, 150, 260, 230),   // overlap
            new RectI(900, 500, 950, 540),
        }, snap: 1);

        Assert.Equal(2, merged.Count);
        Assert.Contains(new RectI(100, 100, 260, 230), merged);
    }

    [Fact]
    public void Polygon_covers_exactly_the_union_of_the_rects()
    {
        var rects = Region.Merge(new[]
        {
            new RectI(100, 800, 250, 900),     // cat
            new RectI(600, 870, 680, 900),     // bowl
            new RectI(1200, 600, 1330, 900),   // perch
            new RectI(400, 100, 420, 120),     // treat falling, above everything
        }, snap: 1);
        var poly = Region.Polygon(rects);
        var rng = new Random(5);

        for (int i = 0; i < 20000; i++)
        {
            double x = rng.NextDouble() * 1500, y = rng.NextDouble() * 1000;
            // Skip points exactly on the bridge lines, which have no area anyway.
            bool inUnion = rects.Any(r => r.Contains(x, y));
            Assert.Equal(inUnion, Region.Contains(poly, x, y));
        }
    }

    [Fact]
    public void Nothing_visible_gives_an_empty_polygon()
    {
        Assert.Empty(Region.Polygon(Region.Merge(Array.Empty<RectI>())));
    }
}
