using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>In the air the body follows the trajectory: nose up going up, level at the top, nose down landing.</summary>
public class FlightPitchTests
{
    [Fact] public void Leaping_up_a_window_the_body_is_nearly_upright() => Assert.InRange(Flight.PitchDeg(new Vec2(80, -1100)), 60, 75);
    [Fact] public void At_the_top_of_the_arc_it_is_level() => Assert.InRange(Flight.PitchDeg(new Vec2(300, 0)), -1, 1);
    [Fact] public void A_long_flat_leap_stays_almost_level() => Assert.InRange(Flight.PitchDeg(new Vec2(600, -150)), 5, 20);
    [Fact] public void Coming_down_the_nose_dips_but_not_head_first() => Assert.InRange(Flight.PitchDeg(new Vec2(50, 1500)), -45, -30);
    [Fact] public void Direction_does_not_matter() => Assert.Equal(Flight.PitchDeg(new Vec2(200, -500)), Flight.PitchDeg(new Vec2(-200, -500)));
}
