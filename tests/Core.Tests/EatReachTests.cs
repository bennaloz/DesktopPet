using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>The cat stops where its mouth, not its belly, is over the food.</summary>
public class EatReachTests
{
    sealed class World : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX { get; set; }
        public double BowlFood { get; set; } = 1;
        public Platform? PerchTop => null;
        public double PerchX => 0;
        public Vec2? TreatPos { get; set; }
        public bool TreatLanded { get; set; }
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() => TreatPos = null;
    }

    static (CatBody body, CatBrain brain, SurfaceMap map, Platform floor) Setup(double catX, double reach)
    {
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First();
        var body = new CatBody(new Vec2(catX, 1040));
        body.PlaceOn(floor, catX);
        return (body, new CatBrain(new Random(3)) { EatReach = reach }, map, floor);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1500)]
    public void Stops_with_the_mouth_over_the_bowl(double from)
    {
        var (body, brain, map, floor) = Setup(from, 81);
        var world = new World { BowlPlatform = floor, BowlX = 900 };
        var needs = new Needs { Hunger = 0.9, Energy = 1, Playfulness = 0 };

        for (int i = 0; i < 30 * 20 && brain.State != CatState.Eat; i++)
            brain.Update(1 / 30.0, body, needs, map, world);

        Assert.Equal(CatState.Eat, brain.State);
        Assert.InRange(Math.Abs(body.Pos.X - 900), 76, 86);
        Assert.Equal(Math.Sign(900 - body.Pos.X), brain.Facing);
    }

    [Fact]
    public void Stops_with_the_mouth_over_a_treat()
    {
        var (body, brain, map, _) = Setup(300, 81);
        var world = new World { BowlX = 1800, TreatPos = new Vec2(900, 1040), TreatLanded = true };
        var needs = new Needs { Hunger = 0.3, Energy = 1, Playfulness = 0 };

        double stoppedAt = double.NaN;
        for (int i = 0; i < 30 * 20 && world.TreatPos != null; i++)
        {
            brain.Update(1 / 30.0, body, needs, map, world);
            if (world.TreatPos == null) stoppedAt = body.Pos.X;
        }

        Assert.InRange(Math.Abs(stoppedAt - 900), 76, 86);
    }
}
