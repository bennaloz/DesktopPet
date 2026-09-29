using System;

namespace ZairaPet.Core;

/// <summary>What the tail says while she walks or runs.</summary>
public enum TailMood { Calm, Content, Playful, Tired, Annoyed, Zoomies }

/// <summary>Whose tail: a cat's says it with its height and curl, a dog's with its wag.</summary>
public enum TailStyle { Cat, Dog }

/// <summary>
/// How a tail mood looks. <see cref="Lift"/>: per tail bone from the root to the tip, degrees above straight back
/// along the body (90 = straight up, more bends over forwards). The tail waves sideways: the whole of it
/// (<see cref="WaveDeg"/> at <see cref="WaveHz"/>, growing towards the tip) and the tip on its own
/// (<see cref="TipDeg"/> at <see cref="TipHz"/>; <see cref="TipJerky"/>: in snaps, not smoothly).
/// </summary>
public sealed record TailShape(double[] Lift, double WaveDeg, double WaveHz, double TipDeg, double TipHz, bool TipJerky);

public static class TailMoods
{
    public static TailMood For(PetState state, Needs needs, double happy)
    {
        if (state == PetState.Zoomies) return TailMood.Zoomies;
        if (needs.Hunger > 0.7) return TailMood.Annoyed;
        if (needs.Energy < 0.3) return TailMood.Tired;
        if (happy > 0 || needs.Affection > 0.8) return TailMood.Content;
        if (needs.Playfulness > 0.6) return TailMood.Playful;
        return TailMood.Calm;
    }

    // A cat's tail curls at the tip: low and relaxed it turns up in a "J", pleased it hooks over forwards,
    // playful it makes a question mark. Only annoyed (lashing) and flat out (balance) is it held straight.
    static readonly TailShape Calm = new(new double[] { -52, -40, -20, 12, 45 }, 7, 0.6, 4, 0.6, false);
    static readonly TailShape Content = new(new double[] { 55, 80, 100, 140, 175 }, 3, 0.8, 6, 1.2, false);
    static readonly TailShape Playful = new(new double[] { 60, 85, 105, 145, 185 }, 10, 1.6, 8, 5.0, false);
    static readonly TailShape Tired = new(new double[] { -62, -55, -45, -30, -10 }, 3, 0.5, 2, 0.5, false);
    static readonly TailShape Annoyed = new(new double[] { -12, -4, 0, 2, 4 }, 2, 0.7, 28, 2.2, true);
    static readonly TailShape Zoomies = new(new double[] { -5, 2, 5, 5, 5 }, 1, 1.0, 1, 1.0, false);

    // A dog's tail hangs when it is relaxed and wags when it is pleased: wide, quick sweeps of the whole tail,
    // carried about level or a little up (a golden's plume never curls over its back). Hungry and waiting it
    // wags hopefully; tired it hangs low and still.
    static readonly TailShape DogCalm = new(new double[] { -45, -55, -60, -60, -55 }, 6, 0.8, 2, 0.8, false);
    static readonly TailShape DogContent = new(new double[] { 5, 12, 18, 22, 26 }, 22, 2.8, 8, 2.8, false);
    static readonly TailShape DogPlayful = new(new double[] { 20, 28, 34, 38, 42 }, 26, 3.4, 8, 3.4, false);
    static readonly TailShape DogTired = new(new double[] { -60, -70, -74, -74, -70 }, 2, 0.5, 1, 0.5, false);
    static readonly TailShape DogHungry = new(new double[] { -10, 0, 6, 10, 14 }, 15, 2.2, 6, 2.2, false);
    static readonly TailShape DogZoomies = new(new double[] { -5, 2, 6, 8, 10 }, 3, 1.5, 2, 1.5, false);

    public static TailShape Shape(TailMood mood, TailStyle style) => style == TailStyle.Cat ? Shape(mood) : mood switch
    {
        TailMood.Content => DogContent,
        TailMood.Playful => DogPlayful,
        TailMood.Tired => DogTired,
        TailMood.Annoyed => DogHungry,
        TailMood.Zoomies => DogZoomies,
        _ => DogCalm,
    };

    public static TailShape Shape(TailMood mood) => mood switch
    {
        TailMood.Content => Content,
        TailMood.Playful => Playful,
        TailMood.Tired => Tired,
        TailMood.Annoyed => Annoyed,
        TailMood.Zoomies => Zoomies,
        _ => Calm,
    };

    // Galloping the tail is a counterweight: out behind the body, curving up a little towards the tip.
    static readonly double[] RunCarry = { -10, -4, 2, 8, 14 };

    /// <summary>
    /// The tail at a lope or gallop: carried out behind with only a hint (30%) of the mood's shape, never low
    /// enough to trail between the hind legs nor so tall that it seesaws with the rump; smaller waves.
    /// </summary>
    public static TailShape Running(TailShape mood)
    {
        var lift = new double[RunCarry.Length];
        for (int i = 0; i < lift.Length; i++)
            lift[i] = Math.Clamp(RunCarry[i] + 0.3 * (mood.Lift[i] - RunCarry[i]), -25, 35);
        return mood with { Lift = lift, WaveDeg = Math.Min(mood.WaveDeg, 4), TipDeg = Math.Min(mood.TipDeg, 4) };
    }

    /// <summary>Sideways angle of each tail bone (degrees, + to her left) at time <paramref name="t"/> (s).</summary>
    public static double[] Sideways(TailShape s, double t)
    {
        var y = new double[5];
        for (int i = 0; i < 5; i++)
        {
            // the wave travels down the tail: each bone a little later, more towards the tip
            double wave = s.WaveDeg * (0.3 + 0.35 * i) * Math.Sin(2 * Math.PI * s.WaveHz * t - 0.7 * i);
            y[i] = wave;
        }
        double tip = Math.Sin(2 * Math.PI * s.TipHz * t);
        if (s.TipJerky) tip = Math.Sign(tip) * Math.Pow(Math.Abs(tip), 0.3);   // snaps from side to side, pauses at the ends
        y[3] += 0.4 * s.TipDeg * tip;
        y[4] += s.TipDeg * tip;
        return y;
    }
}
