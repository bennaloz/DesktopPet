using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// A cat's leap from A to B: it takes off nose-up but less steep than its path, rotates forward as it rises
/// and is about level at the top, where it lands on a ledge; coming down the nose dips, never head first.
/// </summary>
public class FlightPitchTests
{
    [Fact] public void Taking_off_up_a_window_the_body_is_steep_but_not_upright() => Assert.InRange(Flight.PitchDeg(new Vec2(80, -1100)), 40, 48);
    [Fact] public void At_the_top_of_the_arc_it_is_level() => Assert.InRange(Flight.PitchDeg(new Vec2(300, 0)), -1, 1);
    [Fact] public void Slowing_near_the_top_it_has_rotated_forward() => Assert.InRange(Flight.PitchDeg(new Vec2(80, -200)), 20, 45);
    [Fact] public void A_long_flat_leap_stays_almost_level() => Assert.InRange(Flight.PitchDeg(new Vec2(600, -150)), 5, 15);
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

/// <summary>Stretched out while the push carries her up; hind legs drawn in under the belly before the top,
/// ready to land on the ledge.</summary>
public class FlightPhaseTests
{
    [Fact] public void Just_after_take_off_she_is_stretched_out() => Assert.True(Flight.Stretched(new Vec2(80, -1000)));
    [Fact] public void Near_the_top_she_draws_the_hind_legs_in() => Assert.False(Flight.Stretched(new Vec2(80, -200)));
    [Fact] public void Coming_down_she_is_not_stretched() => Assert.False(Flight.Stretched(new Vec2(80, 300)));
}
