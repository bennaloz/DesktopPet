using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Gravity at the cat's scale: Zaira is ~150 px long, ~46 cm, so a metre is ~325 px and real g is ~3200 px/s².
/// On screen that looked floaty, so the leaps use about 1.35 g: rising 1.5 m takes a little under half a second
/// (a real cat: about 0.55 s).
/// </summary>
public class RealGravityTests
{
    const double PxPerMetre = 150 / 0.46;

    [Fact]
    public void Gravity_is_a_bit_stronger_than_real() => Assert.InRange(CatBody.Gravity / PxPerMetre / 9.81, 1.2, 1.5);

    [Fact]
    public void Rising_a_metre_and_a_half_takes_under_half_a_second()
    {
        var body = new CatBody(new Vec2(500, 1000));
        body.JumpTo(new Vec2(560, 1000 - 1.5 * PxPerMetre), 0);
        double tUp = -body.Vel.Y / CatBody.Gravity;
        Assert.InRange(tUp, 0.44, 0.5);
    }
}
