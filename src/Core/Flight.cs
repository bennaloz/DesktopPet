using System;

namespace ZairaPet.Core;

public static class Flight
{
    /// <summary>
    /// Body pitch in the air (degrees, positive = nose up) from the velocity (screen px/s, y down): it follows
    /// the trajectory, almost upright when leaping up a window, never diving head first when coming down.
    /// </summary>
    public static double PitchDeg(Vec2 vel)
    {
        double deg = Math.Atan2(-vel.Y, Math.Abs(vel.X)) * 180 / Math.PI * 0.8;
        return Math.Clamp(deg, -40, 72);
    }
}
