using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// The model takes a moment to turn round (it turns through the side facing the viewer): a cat that reverses
/// at full speed would be seen running straight at the viewer. It stops, turns, then goes.
/// </summary>
public class TurnAroundTests
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
    public void Zoomies_never_run_fast_right_after_reversing()
    {
        // A narrow floor forces many reversals.
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 500, 700) }, Array.Empty<Platform>());
        var floor = map.Platforms[0];
        var body = new CatBody(new Vec2(250, 700));
        body.PlaceOn(floor, 250);
        var brain = new CatBrain(new Random(7));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 1 };
        var world = new World { BowlPlatform = floor };

        int facing = brain.Facing, reversals = 0;
        double sinceTurn = 99;
        for (int i = 0; i < 30 * 60; i++)
        {
            if (i % 300 == 0) needs.Playfulness = 1;
            brain.Update(1 / 30.0, body, needs, map, world);
            if (brain.Facing != facing) { facing = brain.Facing; sinceTurn = 0; reversals++; }
            else sinceTurn += 1 / 30.0;
            if (Math.Abs(body.Vel.X) > CatBrain.WalkSpeed + 1)
                Assert.True(sinceTurn >= CatBrain.TurnTime - 1e-9, $"running at {body.Vel.X:0} px/s {sinceTurn:0.00} s after turning round");
        }
        Assert.True(reversals > 5, $"only {reversals} reversals: the test did not exercise turning");
    }
}
