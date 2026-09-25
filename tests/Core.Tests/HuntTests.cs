using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Hunting the mouse cursor. How keen she is depends on her playfulness: a little → she stops and watches it;
/// some → she crouches and follows it; a lot → she crouches, wiggles her rump and pounces. A cursor within
/// paw reach gets swatted. Afterwards she leaves the cursor alone for a while.
/// </summary>
public class HuntTests
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

    sealed class Scene
    {
        public readonly SurfaceMap Map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        public readonly CatBody Body;
        public readonly CatBrain Brain = new(new Random(3));
        public readonly Needs Needs;
        public readonly World World = new();
        public readonly List<string> Actions = new();
        public readonly List<CatState> States = new();
        double _t;

        public Scene(double playfulness, CatState start = CatState.Sit)
        {
            var floor = Map.Platforms.First();
            World.BowlPlatform = floor;
            Body = new CatBody(new Vec2(800, 1040));
            Body.PlaceOn(floor, 800);
            Needs = new Needs { Hunger = 0.1, Energy = 0.9, Playfulness = playfulness };
            if (start == CatState.Sit) Brain.SitFor(120);
        }

        /// <summary>Run with the cursor around a point, jiggling a few pixels (a hand on the mouse) unless still.</summary>
        public void Run(double seconds, Vec2? cursor, bool moving = true)
        {
            for (double s = 0; s < seconds; s += 1 / 30.0)
            {
                _t += 1 / 30.0;
                Brain.Cursor = cursor is { } c && moving ? c + new Vec2(4 * Math.Sin(_t * 9), 3 * Math.Cos(_t * 7)) : cursor;
                Brain.Update(1 / 30.0, Body, Needs, Map, World);
                Actions.Add(Brain.Action);
                States.Add(Brain.State);
            }
        }
    }

    [Fact]
    public void Not_very_playful_she_stops_and_watches()
    {
        var s = new Scene(0.1);
        s.Run(3, new Vec2(950, 980));
        Assert.Equal(CatState.Hunt, s.Brain.State);
        Assert.DoesNotContain("stalk", s.Actions);
        Assert.Equal(800, s.Body.Pos.X);
    }

    [Fact]
    public void Somewhat_playful_she_crouches_and_follows_it()
    {
        var s = new Scene(0.5);
        s.Run(3, new Vec2(1000, 980));
        Assert.Equal(CatState.Hunt, s.Brain.State);
        Assert.Equal("stalk", s.Brain.Action);
        Assert.DoesNotContain("wiggle", s.Actions);
    }

    [Fact]
    public void She_turns_to_face_a_cursor_behind_her()
    {
        var s = new Scene(0.5);
        s.Run(0.5, null);
        int before = s.Brain.Facing;
        s.Run(2, new Vec2(800 - before * 180, 990));
        Assert.Equal(-before, s.Brain.Facing);
    }

    [Fact]
    public void Very_playful_she_wiggles_and_pounces_on_it()
    {
        var s = new Scene(0.9);
        var cursor = new Vec2(1050, 1000);
        s.Run(0.5, null);
        s.Run(6, cursor);
        Assert.Contains("wiggle", s.Actions);
        Assert.Contains(CatState.Airborne, s.States);
        s.Run(2, null);
        Assert.Equal(BodyMode.Grounded, s.Body.Mode);
        // Lands with its head, not its body, on the cursor.
        Assert.InRange(s.Body.Pos.X, 1050 - s.Brain.EatReach - 30, 1050 - s.Brain.EatReach + 30);
        Assert.True(s.Needs.Playfulness < 0.6);
    }

    [Fact]
    public void A_cursor_within_paw_reach_gets_swatted()
    {
        var s = new Scene(0.5);
        s.Run(0.5, null);
        s.Run(4, new Vec2(800 + s.Brain.Facing * 70, 1000));
        Assert.Contains("swat", s.Actions);
    }

    [Fact]
    public void A_cursor_that_never_moves_is_not_prey()
    {
        var s = new Scene(0.9);
        s.Run(5, new Vec2(950, 990), moving: false);
        Assert.DoesNotContain(CatState.Hunt, s.States);
    }

    [Fact]
    public void A_sleeping_cat_ignores_the_cursor()
    {
        var s = new Scene(0.9, start: CatState.Idle);
        s.Needs.Energy = 0.1;
        s.Needs.Playfulness = 0;
        s.Run(3, null);          // goes to sleep (no perch: on the spot)
        Assert.Equal(CatState.Sleep, s.Brain.State);
        s.Needs.Playfulness = 0.9;
        s.Run(3, new Vec2(900, 990));
        Assert.Equal(CatState.Sleep, s.Brain.State);
    }

    [Fact]
    public void After_a_hunt_she_leaves_the_cursor_alone_for_a_while()
    {
        var s = new Scene(0.5);
        s.Run(3, new Vec2(1000, 980));
        Assert.Equal(CatState.Hunt, s.Brain.State);
        s.Run(3, new Vec2(1900, 100));            // the cursor goes away: the hunt ends
        Assert.NotEqual(CatState.Hunt, s.Brain.State);
        s.States.Clear();
        s.Run(15, new Vec2(1000, 980));           // and comes back straight away
        Assert.DoesNotContain(CatState.Hunt, s.States);
    }
}
