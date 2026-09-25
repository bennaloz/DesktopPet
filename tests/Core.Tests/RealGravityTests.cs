using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Real gravity at the cat's scale: Zaira is ~150 px long, ~46 cm, so a metre is ~325 px and g is ~3200 px/s².
/// A real cat rising 1.5 m onto a shelf is in the air going up for about 0.55 s.
/// </summary>
public class RealGravityTests
{
    const double PxPerMetre = 150 / 0.46;

    [Fact]
    public void Gravity_is_a_real_cats_gravity() => Assert.InRange(CatBody.Gravity / PxPerMetre, 9.3, 10.3);

    [Fact]
    public void Rising_a_metre_and_a_half_takes_about_half_a_second()
    {
        var body = new CatBody(new Vec2(500, 1000));
        body.JumpTo(new Vec2(560, 1000 - 1.5 * PxPerMetre), 0);
        double tUp = -body.Vel.Y / CatBody.Gravity;
        Assert.InRange(tUp, 0.5, 0.6);
    }
}
