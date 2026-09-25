using System;
using System.Collections.Generic;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class GazeTests
{
    static readonly Vec2 Head = new(1000, 950);

    static Gaze Run(Gaze g, GazeInput input, double seconds, List<GazeKind>? seen = null)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            g.Update(1 / 30.0, input);
            seen?.Add(g.Kind);
        }
        return g;
    }

    static GazeInput Resting(Vec2? mouse = null) =>
        new(CatState.Sit, Head, Facing: 1, Mouse: mouse, JumpTarget: null, Treat: null);

    [Fact]
    public void A_sleeping_cat_looks_nowhere()
    {
        var g = Run(new Gaze(new Random(1)), Resting() with { State = CatState.Sleep }, 20);
        Assert.Equal(GazeKind.Ahead, g.Kind);
    }

    [Fact]
    public void A_resting_cat_looks_at_the_viewer_looks_around_and_looks_ahead()
    {
        var seen = new List<GazeKind>();
        Run(new Gaze(new Random(2)), Resting(), 90, seen);
        Assert.Contains(GazeKind.Viewer, seen);
        Assert.Contains(GazeKind.Point, seen);
        Assert.Contains(GazeKind.Ahead, seen);
    }

    [Fact]
    public void A_cursor_nearby_is_watched()
    {
        var mouse = new Vec2(1150, 850);
        var g = Run(new Gaze(new Random(3)), Resting(mouse), 1);
        Assert.Equal(GazeKind.Point, g.Kind);
        Assert.Equal(mouse, g.Point);
    }

    [Fact]
    public void A_cursor_far_away_is_ignored()
    {
        var mouse = new Vec2(1900, 100);
        var seen = new List<GazeKind>();
        var g = new Gaze(new Random(4));
        for (double t = 0; t < 30; t += 1 / 30.0)
        {
            g.Update(1 / 30.0, Resting(mouse));
            Assert.False(g.Kind == GazeKind.Point && g.Point == mouse);
        }
    }

    [Fact]
    public void Before_and_during_a_jump_it_looks_where_it_will_land()
    {
        var landing = new Vec2(1300, 600);
        var g = Run(new Gaze(new Random(5)), Resting(new Vec2(1100, 900)) with { State = CatState.Travel, JumpTarget = landing }, 0.2);
        Assert.Equal(GazeKind.Point, g.Kind);
        Assert.Equal(landing, g.Point);
    }

    [Fact]
    public void Chasing_a_treat_it_keeps_its_eyes_on_the_treat()
    {
        var treat = new Vec2(1500, 950);
        var g = Run(new Gaze(new Random(6)), Resting() with { State = CatState.ChaseTreat, Treat = treat }, 0.5);
        Assert.Equal(GazeKind.Point, g.Kind);
        Assert.Equal(treat, g.Point);
    }

    [Fact]
    public void Walking_it_mostly_looks_ahead_with_the_odd_glance_at_the_viewer()
    {
        var seen = new List<GazeKind>();
        Run(new Gaze(new Random(7)), Resting() with { State = CatState.Travel }, 120, seen);
        int ahead = seen.FindAll(k => k == GazeKind.Ahead).Count;
        Assert.True(ahead > seen.Count * 0.7);
        Assert.Contains(GazeKind.Viewer, seen);
    }
}
