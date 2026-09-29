using System;
using System.Linq;
using ZairaPet.Core;
using Xunit;

namespace ZairaPet.Tests;

/// <summary>
/// Walking or running, the tail shows her mood: pleased, high with the tip hooked; calm, low and soft with a slow
/// wave; playful, high and waving faster, the tip quivering; tired, low, almost dragging; hungry or annoyed,
/// level with the tip lashing; in the zoomies straight back for balance.
/// Angles: per tail bone, from the root to the tip, degrees above straight back along the body (90 = up).
/// </summary>
public class TailMoodTests
{
    static Needs N(double hunger = 0.3, double energy = 0.8, double play = 0.3, double affection = 0.5) =>
        new() { Hunger = hunger, Energy = energy, Playfulness = play, Affection = affection };

    [Theory]
    [InlineData(PetState.Zoomies, 0.9, 0.1, 1.0, 0.9, 0, TailMood.Zoomies)]   // zoomies beat everything
    [InlineData(PetState.Travel, 0.8, 0.8, 0.3, 0.9, 0, TailMood.Annoyed)]    // hungry beats pleased
    [InlineData(PetState.Wander, 0.3, 0.2, 0.3, 0.9, 0, TailMood.Tired)]
    [InlineData(PetState.Wander, 0.3, 0.8, 0.3, 0.5, 10, TailMood.Content)]   // just petted or called
    [InlineData(PetState.Wander, 0.3, 0.8, 0.3, 0.85, 0, TailMood.Content)]   // feeling loved
    [InlineData(PetState.Wander, 0.3, 0.8, 0.7, 0.5, 0, TailMood.Playful)]
    [InlineData(PetState.Wander, 0.3, 0.8, 0.3, 0.5, 0, TailMood.Calm)]
    public void Mood_comes_from_the_needs(PetState state, double hunger, double energy, double play, double affection,
                                          double happy, TailMood expected) =>
        Assert.Equal(expected, TailMoods.For(state, N(hunger, energy, play, affection), happy));

    [Fact]
    public void Pleased_and_playful_tails_are_up_tired_ones_down()
    {
        foreach (var m in new[] { TailMood.Content, TailMood.Playful })
            Assert.True(TailMoods.Shape(m).Lift[1] > 60, $"{m} tail should stand up");
        Assert.True(TailMoods.Shape(TailMood.Tired).Lift.Take(4).All(a => a < -25), "tired tail hangs low");
        Assert.True(TailMoods.Shape(TailMood.Calm).Lift[0] < 0, "calm tail is carried low");
    }

    [Fact]
    public void A_relaxed_tail_curls_up_at_the_tip()
    {
        var lift = TailMoods.Shape(TailMood.Calm).Lift;
        Assert.True(lift[0] < -40 && lift[4] > 30, "low tail, tip turned up: a J");
    }

    [Fact]
    public void Pleased_tail_ends_in_a_hook()
    {
        var lift = TailMoods.Shape(TailMood.Content).Lift;
        Assert.True(lift[4] > 120, "the tip bends over forwards");
    }

    [Fact]
    public void Annoyed_tail_is_level_and_only_the_tip_lashes()
    {
        var s = TailMoods.Shape(TailMood.Annoyed);
        Assert.All(s.Lift, a => Assert.InRange(a, -20, 20));
        double Swing(int bone) => Enumerable.Range(0, 200).Select(i => TailMoods.Sideways(s, i * 0.01)[bone]).Max()
                                  - Enumerable.Range(0, 200).Select(i => TailMoods.Sideways(s, i * 0.01)[bone]).Min();
        Assert.True(Swing(4) > 3 * Swing(0), "the tip moves much more than the root");
        Assert.True(Swing(4) > 30);
    }

    [Fact]
    public void Playful_tail_waves_faster_than_a_calm_one() =>
        Assert.True(TailMoods.Shape(TailMood.Playful).WaveHz > 1.5 * TailMoods.Shape(TailMood.Calm).WaveHz);

    [Theory]
    [InlineData(TailMood.Calm)]
    [InlineData(TailMood.Tired)]
    [InlineData(TailMood.Content)]
    [InlineData(TailMood.Playful)]
    public void Running_the_tail_goes_out_behind_for_balance(TailMood mood)
    {
        // galloping, a low tail would trail between the hind legs and a tall one would seesaw with the rump:
        // it is carried out behind, curving up a little towards the tip, with only a hint of the mood
        var run = TailMoods.Running(TailMoods.Shape(mood));
        Assert.All(run.Lift, a => Assert.InRange(a, -30, 40));
        Assert.True(run.Lift[4] > run.Lift[0], "the tip curves up");
        Assert.True(run.WaveDeg <= TailMoods.Shape(mood).WaveDeg, "no bigger sideways wave than walking");
    }

    [Fact]
    public void Running_keeps_a_hint_of_the_mood() =>
        Assert.True(TailMoods.Running(TailMoods.Shape(TailMood.Content)).Lift[1]
                    > TailMoods.Running(TailMoods.Shape(TailMood.Tired)).Lift[1] + 10);

    [Fact]
    public void Zoomies_tail_is_straight_back_and_steady()
    {
        var s = TailMoods.Shape(TailMood.Zoomies);
        Assert.All(s.Lift, a => Assert.InRange(a, -10, 15));
        Assert.True(Enumerable.Range(0, 100).All(i => TailMoods.Sideways(s, i * 0.01).All(y => Math.Abs(y) < 6)));
    }
}
