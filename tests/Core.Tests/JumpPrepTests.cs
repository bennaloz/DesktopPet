using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// How she gets ready to jump varies: sometimes she just goes, sometimes she crouches first, sometimes she
/// takes aim two or three times before leaping.
/// </summary>
public class JumpPrepTests
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
    public void Some_jumps_are_straight_away_some_after_a_crouch_some_after_taking_aim()
    {
        var map = SurfaceMap.Build(new[] { new WindowInfo(1, new RectI(700, 850, 1200, 1040)) },
                                   new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
        var top = map.Platforms.First(p => p.Owner == 1);
        var body = new CatBody(new Vec2(300, 1040));
        body.PlaceOn(floor, 300);
        var brain = new CatBrain(new Random(5));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 0 };
        var world = new World { BowlPlatform = floor };

        var preps = new List<double>();
        bool aimed = false;
        double prep = 0;
        for (int trip = 0; trip < 24; trip++)
        {
            bool up = trip % 2 == 0;
            brain.ExploreTo(up ? top : floor, up ? 950 : (trip % 4 == 1 ? 300 : 1600));
            for (int i = 0; i < 30 * 20; i++)
            {
                var before = body.Mode;
                brain.Update(1 / 30.0, body, needs, map, world);
                if (brain.Action is "prejump" or "aim") prep += 1 / 30.0;
                if (brain.Action == "aim") aimed = true;
                if (before == BodyMode.Grounded && body.Mode == BodyMode.Airborne) { preps.Add(prep); prep = 0; }
                if (brain.State is CatState.Idle or CatState.Sit) break;
            }
        }
        Assert.True(preps.Count >= 20, $"{preps.Count} jumps");
        Assert.Contains(preps, p => p < 0.15);                 // straight away
        Assert.Contains(preps, p => p > 0.3 && p < 0.7);       // a crouch
        Assert.Contains(preps, p => p > 0.9);                  // taking aim two or three times
        Assert.True(aimed);
    }
}
