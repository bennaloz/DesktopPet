using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Bretzel, the rabbit: hops about the floor and never climbs onto windows; no meowing, no hunting the cursor;
/// its zoomies are dashes with binkies (leaps with a twist) in between; relaxed and tired it flops onto its side
/// to sleep; food it cannot get it thumps about.
/// </summary>
public class BunnyBrainTests
{
    static readonly RectI[] Work = { new(0, 0, 1920, 1040) };

    sealed class FakeWorld : IWorld
    {
        public Platform? BowlPlatform { get; set; }
        public double BowlX { get; set; } = 1500;
        public double BowlFood { get; set; } = 1;
        public Platform? PerchTop { get; set; }
        public double PerchX { get; set; }
        public Vec2? TreatPos { get; set; }
        public bool TreatLanded { get; set; }
        public void EatFromBowl(double amount) => BowlFood = Math.Max(0, BowlFood - amount);
        public void ConsumeTreat() => TreatPos = null;
    }

    sealed class Sim
    {
        public readonly SurfaceMap Map;
        public readonly FakeWorld World = new();
        public readonly Needs Needs = new() { Hunger = 0.1, Energy = 0.9, Playfulness = 0.1 };
        public readonly CatBody Body;
        public readonly BunnyBrain Brain;
        public readonly Platform Floor;
        public readonly HashSet<PetState> States = new();
        public readonly HashSet<string> Actions = new();
        public readonly HashSet<SurfaceKind> Supports = new();

        public Sim(int seed = 1, params WindowInfo[] windows)
        {
            Brain = new BunnyBrain(new Random(seed));
            Map = SurfaceMap.Build(windows, Work, Array.Empty<Platform>());
            Floor = Map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
            World.BowlPlatform = Floor;
            Body = new CatBody(new Vec2(200, 1040));
            Body.PlaceOn(Floor, 200);
        }

        public bool RunUntil(Func<bool> done, double maxSeconds)
        {
            for (double t = 0; t < maxSeconds; t += 1 / 30.0)
            {
                Brain.Update(1 / 30.0, Body, Needs, Map, World);
                States.Add(Brain.State);
                Actions.Add(Brain.Action);
                if (Body.Support is { } s) Supports.Add(s.Kind);
                if (done()) return true;
            }
            return false;
        }
    }

    [Fact]
    public void Stays_on_the_floor_and_thumps_when_the_bowl_is_up_on_a_window()
    {
        var sim = new Sim(1, new WindowInfo(1, new RectI(600, 600, 1200, 1040)));
        sim.World.BowlPlatform = sim.Map.Platforms.First(p => p.Kind == SurfaceKind.WindowTop);
        sim.World.BowlX = 900;
        sim.Needs.Hunger = 0.9;

        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Thump, 30), "thumps: it cannot get up there");
        sim.RunUntil(() => false, 300);
        Assert.Equal(new[] { SurfaceKind.Floor }, sim.Supports.ToArray());
    }

    [Fact]
    public void Never_does_what_only_cats_do()
    {
        var sim = new Sim(2, new WindowInfo(1, new RectI(600, 600, 1200, 1040)));
        sim.Needs.Playfulness = 0.6;
        // a cursor wiggling close by: a cat would hunt it
        for (int i = 0; i < 20 * 30; i++)
        {
            sim.Brain.Cursor = new Vec2(sim.Body.Pos.X + 120 + 30 * Math.Sin(i * 0.3), 1000);
            sim.RunUntil(() => true, 1 / 30.0);
        }
        sim.RunUntil(() => false, 600);
        Assert.DoesNotContain(PetState.Meow, sim.States);
        Assert.DoesNotContain(PetState.Hunt, sim.States);
        Assert.DoesNotContain(PetState.Climb, sim.States);
        Assert.Equal(new[] { SurfaceKind.Floor }, sim.Supports.ToArray());
    }

    [Fact]
    public void Zoomies_are_dashes_with_binkies_in_between()
    {
        var sim = new Sim(3);
        sim.Needs.Playfulness = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Zoomies, 20));
        Assert.True(sim.RunUntil(() => sim.Brain.Action == "binky", 15), "a binky during the zoomies");
        Assert.Contains("run", sim.Actions);
    }

    [Fact]
    public void Relaxed_and_tired_it_flops_over_and_sleeps_on_its_side()
    {
        var sim = new Sim(4);
        sim.Needs.Energy = 0.2;
        sim.Needs.Affection = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Flop, 20), "flops over first");
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Sleep, 5));
        sim.RunUntil(() => false, 2);
        Assert.Equal("flopsleep", sim.Brain.Action);
    }

    [Fact]
    public void Tired_but_not_relaxed_it_sleeps_as_a_loaf()
    {
        var sim = new Sim(5);
        sim.Needs.Energy = 0.2;
        sim.Needs.Affection = 0.3;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Sleep, 20));
        Assert.Equal("sleep", sim.Brain.Action);
        Assert.DoesNotContain(PetState.Flop, sim.States);
    }

    [Fact]
    public void Hungry_it_hops_to_the_bowl_and_eats()
    {
        var sim = new Sim(6);
        sim.Needs.Hunger = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Eat, 60));
        Assert.True(sim.Actions.Contains("hop") || sim.Actions.Contains("run"));
    }

    [Fact]
    public void Resting_it_loafs_and_sometimes_washes_its_face()
    {
        var sim = new Sim(7);
        sim.RunUntil(() => false, 900);
        Assert.Contains("loaf", sim.Actions);
        Assert.Contains("groom", sim.Actions);
    }

    [Theory]
    [InlineData(BunnyBrain.HopSpeed, "hop")]
    [InlineData(BunnyBrain.RunSpeed, "run")]
    public void Gait_follows_the_speed(double speed, string action) =>
        Assert.Equal(action, new BunnyBrain(new Random(1)).GaitFor(speed));

    [Fact]
    public void The_rabbit_species_gets_the_rabbit_brain() =>
        Assert.IsType<BunnyBrain>(Brains.For("rabbit", new Random(1)));
}
