using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>In the air the body follows the trajectory: nose up going up, level at the top, nose down landing.</summary>
public class FlightPitchTests
{
    [Fact] public void Leaping_up_a_window_the_body_is_nearly_upright() => Assert.InRange(Flight.PitchDeg(new Vec2(80, -1100)), 78, 86);
    [Fact] public void At_the_top_of_the_arc_it_is_level() => Assert.InRange(Flight.PitchDeg(new Vec2(300, 0)), -1, 1);
    [Fact] public void A_long_flat_leap_stays_almost_level() => Assert.InRange(Flight.PitchDeg(new Vec2(600, -150)), 5, 20);
    [Fact] public void Coming_down_the_nose_dips_but_not_head_first() => Assert.InRange(Flight.PitchDeg(new Vec2(50, 1500)), -45, -30);
    [Fact] public void Direction_does_not_matter() => Assert.Equal(Flight.PitchDeg(new Vec2(200, -500)), Flight.PitchDeg(new Vec2(-200, -500)));
}

/// <summary>Seen three-quarter the body looks short; the steeper the flight, the more she turns side-on.</summary>
public class FlightSideOnTests
{
    [Fact] public void Leaping_straight_up_she_is_side_on() => Assert.InRange(Flight.SideOn(new Vec2(80, -1100)), 0.9, 1.0);
    [Fact] public void A_flat_leap_keeps_the_three_quarter_view() => Assert.InRange(Flight.SideOn(new Vec2(600, -100)), 0.0, 0.2);
    [Fact] public void Coming_down_she_turns_back() => Assert.Equal(0, Flight.SideOn(new Vec2(50, 900)));
}
