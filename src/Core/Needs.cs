using System;

namespace ZairaPet.Core;

/// <summary>
/// The cat's drives, all in 0..1. Hunger 1 = starving, Energy 0 = exhausted,
/// Playfulness 1 = must run around now, Affection 1 = very content.
/// </summary>
public sealed class Needs
{
    public double Hunger { get; set; } = 0.3;
    public double Energy { get; set; } = 0.8;
    public double Playfulness { get; set; } = 0.3;
    public double Affection { get; set; } = 0.5;

    // Per-second rates: hungry in about two hours, tired after two hours awake,
    // rested after half an hour of sleep, restless after forty minutes.
    public const double HungerRate = 1.0 / 7200;
    public const double EnergyDrain = 1.0 / 7200;
    public const double EnergyRestore = 1.0 / 1800;
    public const double PlayRate = 1.0 / 2400;
    public const double AffectionDecay = 1.0 / 5400;

    public void Tick(double dt, bool sleeping)
    {
        Hunger = Clamp(Hunger + HungerRate * dt);
        Affection = Clamp(Affection - AffectionDecay * dt);
        if (sleeping)
        {
            Energy = Clamp(Energy + EnergyRestore * dt);
        }
        else
        {
            Energy = Clamp(Energy - EnergyDrain * dt);
            Playfulness = Clamp(Playfulness + PlayRate * dt);
        }
    }

    public void Eat(double amount) => Hunger = Clamp(Hunger - amount);
    public void Pet(double seconds) => Affection = Clamp(Affection + seconds / 12);
    public void Play(double amount) => Playfulness = Clamp(Playfulness - amount);

    static double Clamp(double v) => Math.Clamp(v, 0, 1);
}
