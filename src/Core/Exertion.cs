using System;

namespace ZairaPet.Core;

/// <summary>
/// How winded the pet is. Running (faster than <see cref="RunLengths"/> body lengths a second) winds it, all the way
/// in <see cref="BuildSeconds"/>; anything slower lets it get its breath back, from all the way in
/// <see cref="RecoverSeconds"/>. A dog pants while it is winded (<see cref="Pant"/>): as it runs, and for a while
/// after it stops.
/// </summary>
public sealed class Exertion
{
    public const double BuildSeconds = 6, RecoverSeconds = 25, RunLengths = 2.4;

    /// <summary>0 rested, 1 out of breath.</summary>
    public double Level { get; set; }

    public void Update(double dt, double speedPx, double lengthPx)
    {
        bool running = speedPx > RunLengths * lengthPx;
        Level = Math.Clamp(Level + dt * (running ? 1 / BuildSeconds : -1 / RecoverSeconds), 0, 1);
    }

    /// <summary>How far the mouth is open for panting, 0..1: it starts at 15% winded and is wide open from 45%.</summary>
    public double Pant
    {
        get
        {
            double t = Math.Clamp((Level - 0.15) / 0.30, 0, 1);
            return t * t * (3 - 2 * t);
        }
    }
}
