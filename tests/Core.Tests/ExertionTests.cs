using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>A dog pants when winded: running winds it, resting gives it its breath back after a while.</summary>
public class ExertionTests
{
    const double Length = 200;     // Sally, px

    static void For(Exertion e, double seconds, double speedPx)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) e.Update(1 / 30.0, speedPx, Length);
    }

    [Fact]
    public void Rested_the_mouth_is_shut() => Assert.Equal(0, new Exertion().Pant);

    [Fact]
    public void Walking_and_trotting_do_not_wind_it()
    {
        var e = new Exertion();
        For(e, 60, 218);
        Assert.Equal(0, e.Pant);
    }

    [Fact]
    public void Running_it_starts_panting_within_a_couple_of_seconds_and_is_wide_open_soon_after()
    {
        var e = new Exertion();
        For(e, 2, 1194);
        Assert.True(e.Pant > 0.3, $"{e.Pant}");
        For(e, 2, 1194);
        Assert.True(e.Pant > 0.99, $"{e.Pant}");
    }

    [Fact]
    public void After_a_run_it_pants_on_for_a_while_then_shuts_its_mouth()
    {
        var e = new Exertion();
        For(e, 8, 1194);
        For(e, 10, 0);
        Assert.True(e.Pant > 0.9, $"still panting 10 s after: {e.Pant}");
        For(e, 15, 0);
        Assert.Equal(0, e.Pant);
    }
}
