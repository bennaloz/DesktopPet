using System;

namespace ZairaPet.Core;

/// <summary>
/// How a cat leaps from A to B: it takes off nose-up, though less steeply than its path; rotates forward as the
/// climb slows, so it is about level at the top of the arc (where it lands on a ledge); draws its hind legs in
/// under the belly before the top; coming down it dips the nose, never diving head first.
/// </summary>
public static class Flight
{
    /// <summary>Body pitch in the air (degrees, positive = nose up) from the velocity (screen px/s, y down).</summary>
    public static double PitchDeg(Vec2 vel)
    {
        double deg = Math.Atan2(-vel.Y, Math.Abs(vel.X)) * 180 / Math.PI * 0.52;
        return Math.Clamp(deg, -40, 45);
    }

    /// <summary>
    /// How far (0..1) to turn from the usual three-quarter view to side-on: seen three-quarter a steep body
    /// looks short, so the steeper she rises the more of her length she shows.
    /// </summary>
    public static double SideOn(Vec2 vel) => Math.Clamp((PitchDeg(vel) - 15) / 30, 0, 1);

    /// <summary>Still stretched out from the push; after that the hind legs come in under the belly.</summary>
    public static bool Stretched(Vec2 vel) => vel.Y < -350;
}
