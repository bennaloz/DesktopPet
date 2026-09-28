using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// A cat can turn its head only so far: when the cursor stays behind her while she sits or stands about, she
/// turns round to face it. Lying down (loaf, paws out) she does not get up for it.
/// </summary>
public class TurnToCursorTests
{
    sealed class World : IWorld
    {
        public Platform? BowlPlatform => null;
        public double BowlX => 0;
        public double BowlFood => 0;
        public Platform? PerchTop => null;
        public double PerchX => 0;
        public Vec2? TreatPos => null;
        public bool TreatLanded => false;
        public void EatFromBowl(double amount) { }
        public void ConsumeTreat() { }
    }

    readonly SurfaceMap _map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
    readonly Needs _needs = new() { Hunger = 0.1, Energy = 0.9, Playfulness = 0.0 };

    (CatBrain brain, CatBody body) Sitting(Action<CatBrain> start)
    {
        var brain = new CatBrain(new Random(5));
        var body = new CatBody(new Vec2(900, 1040));
        body.PlaceOn(_map.Platforms.First(), 900);
        start(brain);
        return (brain, body);
    }

    void Run(CatBrain brain, CatBody body, double seconds, Vec2? cursor)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            brain.Cursor = cursor;
            brain.Update(1 / 30.0, body, _needs, _map, new World());
        }
    }

    [Fact]
    public void Cursor_staying_behind_her_makes_her_turn_round()
    {
        var (brain, body) = Sitting(b => b.SitFor(60));
        int facing = brain.Facing;
        var behind = body.Pos + new Vec2(-facing * 150, -120);
        Run(brain, body, CatBrain.TurnToCursorAfter + 0.3, behind);
        Assert.Equal(-facing, brain.Facing);
        Assert.Equal(CatState.Sit, brain.State);   // still sitting, only turned
    }

    [Fact]
    public void A_moment_behind_her_is_not_enough()
    {
        var (brain, body) = Sitting(b => b.SitFor(60));
        int facing = brain.Facing;
        Run(brain, body, CatBrain.TurnToCursorAfter * 0.5, body.Pos + new Vec2(-facing * 150, -120));
        Run(brain, body, 1, null);
        Assert.Equal(facing, brain.Facing);
    }

    [Fact]
    public void Straight_above_or_in_front_she_just_looks()
    {
        var (brain, body) = Sitting(b => b.SitFor(60));
        int facing = brain.Facing;
        Run(brain, body, 3, body.Pos + new Vec2(-facing * 20, -250));   // above her head
        Assert.Equal(facing, brain.Facing);
        Run(brain, body, 3, body.Pos + new Vec2(facing * 200, -100));   // in front
        Assert.Equal(facing, brain.Facing);
    }

    [Fact]
    public void Lying_as_a_loaf_she_does_not_get_up_for_it()
    {
        var (brain, body) = Sitting(b => b.LoafFor(60));
        Run(brain, body, CatBrain.LoafAfter.min + CatBrain.LieDownTime + 0.2, null);
        int facing = brain.Facing;
        Run(brain, body, 3, body.Pos + new Vec2(-facing * 150, -120));
        Assert.Equal(facing, brain.Facing);
    }
}
