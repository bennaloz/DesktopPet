using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

public class JumpTargetTests
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
    public void The_brain_tells_where_the_jump_lands_while_crouching_and_flying_then_forgets_it()
    {
        var map = SurfaceMap.Build(new[] { new WindowInfo(1, new RectI(700, 800, 1200, 1040)) },
                                   new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);
        var body = new CatBody(new Vec2(300, 1040));
        body.PlaceOn(floor, 300);
        var brain = new CatBrain(new Random(1));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 0 };
        brain.ExploreTo(top, 950);

        bool sawCrouch = false, sawFlight = false;
        for (int i = 0; i < 30 * 20 && !(body.Mode == BodyMode.Grounded && body.Support == top && brain.Action != "prejump"); i++)
        {
            brain.Update(1 / 30.0, body, needs, map, new World { BowlPlatform = floor });
            if (brain.Action == "prejump") { Assert.NotNull(brain.JumpTarget); Assert.Equal(800, brain.JumpTarget!.Value.Y); sawCrouch = true; }
            if (body.Mode == BodyMode.Airborne) { Assert.NotNull(brain.JumpTarget); sawFlight = true; }
        }
        Assert.True(sawCrouch && sawFlight);
        brain.Update(1 / 30.0, body, needs, map, new World { BowlPlatform = floor });
        Assert.Null(brain.JumpTarget);
    }
}
