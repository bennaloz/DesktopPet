using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Ways of moving: a walk; a happy trot (tail up) when pleased; a lope to get somewhere fast when starving;
/// a sprint for zoomies and treats.
/// </summary>
public class GaitChoiceTests
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

    static HashSet<string> ActionsWhile(World world, Needs needs, CatState state, int frames = 30 * 3,
                                         Action<CatBrain, CatBody>? before = null)
    {
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var floor = map.Platforms.First();
        world.BowlPlatform ??= floor;
        var body = new CatBody(new Vec2(200, 1040));
        body.PlaceOn(floor, 200);
        var brain = new CatBrain(new Random(1));
        before?.Invoke(brain, body);
        var seen = new HashSet<string>();
        for (int i = 0; i < frames; i++)
        {
            brain.Update(1 / 30.0, body, needs, map, world);
            if (brain.State == state && Math.Abs(body.Vel.X) > 1) seen.Add(brain.Action);
        }
        return seen;
    }

    [Fact]
    public void A_starving_cat_lopes_to_the_bowl()
    {
        var seen = ActionsWhile(new World { BowlX = 1700 }, new Needs { Hunger = 0.9, Energy = 1, Playfulness = 0 }, CatState.Travel);
        Assert.Contains("lope", seen);
        Assert.DoesNotContain("run", seen);
    }

    [Fact]
    public void A_hungry_cat_trots_happily_to_a_full_bowl()
    {
        var seen = ActionsWhile(new World { BowlX = 1700, BowlFood = 1 }, new Needs { Hunger = 0.7, Energy = 1, Playfulness = 0 }, CatState.Travel);
        Assert.Contains("trot", seen);
        Assert.DoesNotContain("walk", seen);
    }

    [Fact]
    public void After_being_petted_it_trots_about_happily()
    {
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 0 };
        var seen = ActionsWhile(new World { BowlX = 100 }, needs, CatState.Wander, 30 * 40,
            (brain, body) => brain.Cheer(20));
        Assert.Contains("trot", seen);
        Assert.DoesNotContain("walk", seen);
    }

    [Fact]
    public void Petting_cheers_the_cat_up()
    {
        var brain = new CatBrain(new Random(1));
        Assert.Equal(0, brain.Happy);
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var body = new CatBody(new Vec2(500, 1040));
        body.PlaceOn(map.Platforms.First(), 500);
        var needs = new Needs();
        var world = new World { BowlPlatform = map.Platforms.First(), BowlX = 100 };
        for (int i = 0; i < 30 * 3; i++) { brain.OnPetting(1 / 30.0); brain.Update(1 / 30.0, body, needs, map, world); }
        Assert.True(brain.Happy > 10);
    }

    [Fact]
    public void A_treat_is_chased_at_a_sprint()
    {
        var world = new World { BowlX = 100, TreatPos = new Vec2(1700, 1040), TreatLanded = true };
        var seen = ActionsWhile(world, new Needs { Hunger = 0.3, Energy = 1, Playfulness = 0 }, CatState.ChaseTreat);
        Assert.Contains("run", seen);
        Assert.DoesNotContain("trot", seen);
    }
}
