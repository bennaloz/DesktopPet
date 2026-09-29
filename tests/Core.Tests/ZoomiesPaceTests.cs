using System;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Zoomies speed up and slow down the way a cat does: no dead stop from a flat-out gallop (it looked like a stutter)
/// and no instant flat-out start; slowing down she drops to a lope and a trot.
/// </summary>
public class ZoomiesPaceTests
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

    [Fact]
    public void Speed_never_jumps_while_running()
    {
        var map = SurfaceMap.Build(Array.Empty<WindowInfo>(), new[] { new RectI(0, 0, 1920, 1040) }, Array.Empty<Platform>());
        var body = new CatBody(new Vec2(900, 1040));
        body.PlaceOn(map.Platforms[0], 900);
        var brain = new CatBrain(new Random(11));
        var needs = new Needs { Hunger = 0, Energy = 1, Playfulness = 1 };
        const double dt = 1 / 60.0;
        double last = 0, fastest = 0;
        var gaits = new System.Collections.Generic.HashSet<string>();
        for (double t = 0; t < 30; t += dt)
        {
            needs.Playfulness = 1;
            brain.Update(dt, body, needs, map, new World());
            if (brain.State != PetState.Zoomies || body.Mode != BodyMode.Grounded) { last = Math.Abs(body.Vel.X); continue; }
            double v = Math.Abs(body.Vel.X);
            Assert.True(Math.Abs(v - last) <= CatBrain.ZoomBrake * dt + CatBrain.ZoomCreep, $"speed jumped {last:0} -> {v:0} at {t:0.00}s");
            last = v;
            fastest = Math.Max(fastest, v);
            gaits.Add(brain.Action);
        }
        Assert.True(fastest > CatBrain.RunSpeed * 0.95, $"never got going: {fastest:0}");
        Assert.Contains("run", gaits);
        Assert.Contains("lope", gaits);
    }
}
