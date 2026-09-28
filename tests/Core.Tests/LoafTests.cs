using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// The loaf: settling in for a long sit, she first sits up for a few seconds, then lies down (the front paws
/// stepping forward, at a cat's pace), stays a moment on her belly with the paws out, and tucks them in: a loaf
/// for the rest of it. Short sits stay sits; a sit asked for on purpose (self test) stays a sit.
/// </summary>
public class LoafTests
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

    sealed class Scene
    {
        public readonly SurfaceMap Map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        public readonly CatBody Body;
        public readonly CatBrain Brain;
        public readonly Needs Needs = new() { Hunger = 0.1, Energy = 0.9, Playfulness = 0.1 };
        public readonly World World = new();
        public readonly List<(string action, CatState state, double time)> Log = new();

        public Scene(int seed)
        {
            Brain = new CatBrain(new Random(seed));
            Body = new CatBody(new Vec2(900, 1040));
            Body.PlaceOn(Map.Platforms.First(), 900);
        }

        public void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 30.0)
            {
                Needs.Playfulness = 0.1;   // no zoomies: the test is about resting
                Needs.Hunger = 0.1;
                Needs.Energy = 0.9;
                Brain.Update(1 / 30.0, Body, Needs, Map, World);
                Log.Add((Brain.Action, Brain.State, t));
            }
        }
    }

    [Fact]
    public void Left_alone_she_sometimes_settles_into_a_loaf()
    {
        var s = new Scene(7);
        s.Run(600);
        Assert.Contains(s.Log, e => e.action == "loaf");
    }

    [Fact]
    public void Sit_then_paws_out_then_paws_tucked_in()
    {
        var s = new Scene(7);
        s.Run(600);
        for (int i = 1; i < s.Log.Count; i++)
        {
            var (action, state, _) = s.Log[i];
            string before = s.Log[i - 1].action;
            if (action is "liedown" or "crouch" or "tuck" or "loaf") Assert.Equal(CatState.Sit, state);
            if (action == before) continue;
            // always in this order: lies down from sitting (never from walking), tucks in the paws of a cat already lying
            string expected = action switch { "liedown" => "sit", "crouch" => "liedown", "tuck" => "crouch", "loaf" => "tuck", _ => before };
            Assert.Equal(expected, before);
        }
    }

    [Fact]
    public void She_sits_up_for_a_few_seconds_and_lies_with_paws_out_a_moment()
    {
        var s = new Scene(7);
        s.Run(600);
        int lying = s.Log.FindIndex(e => e.action == "liedown");
        int down = s.Log.FindIndex(e => e.action == "crouch");
        int tucking = s.Log.FindIndex(e => e.action == "tuck");
        int loaf = s.Log.FindIndex(e => e.action == "loaf");
        Assert.True(lying > 0 && down > lying && tucking > down && loaf > tucking);
        int sitStart = lying;
        while (sitStart > 0 && s.Log[sitStart - 1].state == CatState.Sit) sitStart--;
        double Took(int from, int to) => s.Log[to].time - s.Log[from].time;
        Assert.InRange(Took(sitStart, lying), CatBrain.LoafAfter.min - 0.05, CatBrain.LoafAfter.max + 0.05);
        Assert.InRange(Took(lying, down), CatBrain.LieDownTime - 0.05, CatBrain.LieDownTime + 0.05);
        Assert.InRange(Took(down, tucking), CatBrain.TuckAfter.min - 0.05, CatBrain.TuckAfter.max + 0.05);
        Assert.InRange(Took(tucking, loaf), CatBrain.TuckTime - 0.05, CatBrain.TuckTime + 0.05);
    }

    [Fact]
    public void A_sit_asked_for_stays_a_sit()
    {
        var s = new Scene(7);
        s.Brain.SitFor(60);
        s.Run(50);
        Assert.DoesNotContain(s.Log, e => e.action != "sit");
    }

    [Fact]
    public void LoafFor_goes_through_the_shortest_sit_and_paws_out()
    {
        var s = new Scene(7);
        s.Brain.LoafFor(60);
        s.Run(CatBrain.LoafAfter.min + 0.5);
        Assert.Equal("liedown", s.Brain.Action);
        s.Run(CatBrain.LieDownTime);
        Assert.Equal("crouch", s.Brain.Action);
        s.Run(CatBrain.TuckAfter.min);
        Assert.Equal("tuck", s.Brain.Action);
        s.Run(CatBrain.TuckTime);
        Assert.Equal("loaf", s.Brain.Action);
        Assert.Equal(CatState.Sit, s.Brain.State);
    }
}
