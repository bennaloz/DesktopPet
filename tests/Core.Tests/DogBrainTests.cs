using System;
using System.Collections.Generic;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// The golden retriever: walks, trots when pleased and gallops in its zoomies, all on the floor (a dog does not
/// climb onto windows); sits, then lies down with its front legs out; sleeps where it is; barks for food it cannot
/// get. No meowing, hunting the cursor, binkies or flops.
/// </summary>
public class DogBrainTests
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
        public readonly DogBrain Brain;
        public readonly HashSet<PetState> States = new();
        public readonly HashSet<string> Actions = new();
        public readonly HashSet<SurfaceKind> Supports = new();

        public Sim(int seed = 1, params WindowInfo[] windows)
        {
            Brain = new DogBrain(new Random(seed));
            Map = SurfaceMap.Build(windows, Work, Array.Empty<Platform>());
            var floor = Map.Platforms.First(p => p.Kind == SurfaceKind.Floor);
            World.BowlPlatform = floor;
            Body = new CatBody(new Vec2(200, 1040));
            Body.PlaceOn(floor, 200);
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
    public void Barks_for_a_bowl_up_on_a_window_and_stays_on_the_floor()
    {
        var sim = new Sim(1, new WindowInfo(1, new RectI(600, 600, 1200, 1040)));
        sim.World.BowlPlatform = sim.Map.Platforms.First(p => p.Kind == SurfaceKind.WindowTop);
        sim.World.BowlX = 900;
        sim.Needs.Hunger = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Bark, 30));
        Assert.True(sim.RunUntil(() => sim.Brain.Action == "bark", 5));
        sim.RunUntil(() => false, 300);
        Assert.Equal(new[] { SurfaceKind.Floor }, sim.Supports.ToArray());
    }

    [Fact]
    public void Barks_at_an_empty_bowl()
    {
        var sim = new Sim(2);
        sim.World.BowlFood = 0;
        sim.Needs.Hunger = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Bark, 60));
    }

    [Fact]
    public void Never_does_what_only_cats_or_rabbits_do()
    {
        var sim = new Sim(3, new WindowInfo(1, new RectI(600, 600, 1200, 1040)));
        sim.Needs.Playfulness = 0.6;
        for (int i = 0; i < 20 * 30; i++)
        {
            sim.Brain.Cursor = new Vec2(sim.Body.Pos.X + 120 + 30 * Math.Sin(i * 0.3), 1000);
            sim.RunUntil(() => true, 1 / 30.0);
        }
        sim.RunUntil(() => false, 600);
        foreach (var s in new[] { PetState.Meow, PetState.Hunt, PetState.Climb, PetState.Binky, PetState.Flop, PetState.Thump, PetState.Groom })
            Assert.DoesNotContain(s, sim.States);
        Assert.Equal(new[] { SurfaceKind.Floor }, sim.Supports.ToArray());
    }

    [Fact]
    public void Hungry_it_goes_to_the_bowl_and_eats()
    {
        var sim = new Sim(4);
        sim.Needs.Hunger = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Eat, 60));
    }

    [Fact]
    public void Zoomies_are_a_gallop()
    {
        var sim = new Sim(5);
        sim.Needs.Playfulness = 0.9;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Zoomies, 20));
        Assert.True(sim.RunUntil(() => sim.Brain.Action == "run", 10));
    }

    [Fact]
    public void A_long_rest_is_sitting_then_lying_down()
    {
        var sim = new Sim(6);
        sim.RunUntil(() => false, 900);
        Assert.Contains("sit", sim.Actions);
        Assert.Contains("liedown", sim.Actions);
        Assert.Contains("crouch", sim.Actions);
        Assert.DoesNotContain("loaf", sim.Actions);   // a cat's tucked-in loaf, not a dog's
    }

    [Fact]
    public void Tired_it_sleeps_where_it_is()
    {
        var sim = new Sim(7);
        sim.Needs.Energy = 0.2;
        double x = sim.Body.Pos.X;
        Assert.True(sim.RunUntil(() => sim.Brain.State == PetState.Sleep, 20));
        Assert.InRange(sim.Body.Pos.X, x - 400, x + 400);
    }

    [Theory]
    [InlineData(DogBrain.WalkSpeed, "walk")]
    [InlineData(DogBrain.TrotSpeed, "trot")]
    [InlineData(DogBrain.RunSpeed, "run")]
    public void Gait_follows_the_speed(double speed, string action) =>
        Assert.Equal(action, new DogBrain(new Random(1)).GaitFor(speed));

    [Fact]
    public void The_dog_species_gets_the_dog_brain() =>
        Assert.IsType<DogBrain>(Brains.For("dog", new Random(1)));
}
