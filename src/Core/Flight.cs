using System;

namespace ZairaPet.Core;

public static class Flight
{
    /// <summary>
    /// Body pitch in the air (degrees, positive = nose up) from the velocity (screen px/s, y down): it follows
    /// the trajectory, stretched out upright when leaping up a window, never diving head first when coming down.
    /// </summary>
    public static double PitchDeg(Vec2 vel)
    {
        double deg = Math.Atan2(-vel.Y, Math.Abs(vel.X)) * 180 / Math.PI;
        return Math.Clamp(deg, -40, 82);
    }

    /// <summary>
    /// How far (0..1) to turn from the usual three-quarter view to side-on: seen three-quarter an upright body
    /// looks short, so the steeper she rises the more of her length she shows.
    /// </summary>
    public static double SideOn(Vec2 vel) => Math.Clamp((PitchDeg(vel) - 30) / 45, 0, 1);
}
