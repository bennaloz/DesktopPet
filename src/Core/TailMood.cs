using System;

namespace ZairaPet.Core;

/// <summary>What the tail says while she walks or runs.</summary>
public enum TailMood { Calm, Content, Playful, Tired, Annoyed, Zoomies }

/// <summary>
/// How a tail mood looks. <see cref="Lift"/>: per tail bone from the root to the tip, degrees above straight back
/// along the body (90 = straight up, more bends over forwards). The tail waves sideways: the whole of it
/// (<see cref="WaveDeg"/> at <see cref="WaveHz"/>, growing towards the tip) and the tip on its own
/// (<see cref="TipDeg"/> at <see cref="TipHz"/>; <see cref="TipJerky"/>: in snaps, not smoothly).
/// </summary>
public sealed record TailShape(double[] Lift, double WaveDeg, double WaveHz, double TipDeg, double TipHz, bool TipJerky);

public static class TailMoods
{
    public static TailMood For(CatState state, Needs needs, double happy)
    {
        if (state == CatState.Zoomies) return TailMood.Zoomies;
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

    public static TailShape Shape(TailMood mood) => mood switch
    {
        TailMood.Content => Content,
        TailMood.Playful => Playful,
        TailMood.Tired => Tired,
        TailMood.Annoyed => Annoyed,
        TailMood.Zoomies => Zoomies,
        _ => Calm,
    };

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
