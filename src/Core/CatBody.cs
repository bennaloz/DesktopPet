using System;
using System.Linq;

namespace ZairaPet.Core;

public enum BodyMode { Grounded, Airborne, Held }

/// <summary>
/// 2D kinematics of the cat in screen pixels (y down). Pos is the point between the feet.
/// </summary>
public sealed class CatBody
{
    public const double Gravity = 2400;

    public Vec2 Pos { get; private set; }
    public Vec2 Vel { get; private set; }
    public BodyMode Mode { get; private set; } = BodyMode.Airborne;
    public Platform? Support { get; private set; }

    /// <summary>Set by the last Step when a screen edge stopped the cat.</summary>
    public bool HitWall { get; private set; }
    /// <summary>Set by the last Step when the cat touched down.</summary>
    public bool JustLanded { get; private set; }
    /// <summary>Speed at touch-down, to pick a soft or hard landing.</summary>
    public double LandingSpeed { get; private set; }

    Vec2 _heldPrev;
    Vec2 _heldVel;

    public CatBody(Vec2 pos) => Pos = pos;

    public void Step(double dt, SurfaceMap map, double walkVelX)
    {
        HitWall = false;
        JustLanded = false;
        switch (Mode)
        {
            case BodyMode.Grounded:
                StepGrounded(dt, map, walkVelX);
                break;
            case BodyMode.Airborne:
                StepAirborne(dt, map);
                break;
        }
    }

    void StepGrounded(double dt, SurfaceMap map, double walkVelX)
    {
        var s = Support!;
        double x = ClampX(map, Pos.X + walkVelX * dt);
        Vel = new Vec2(walkVelX, 0);
        if (!s.SpansX(x))
        {
            Mode = BodyMode.Airborne;
            Support = null;
        }
        Pos = new Vec2(x, s.Y);
    }

    void StepAirborne(double dt, SurfaceMap map)
    {
        var vel = new Vec2(Vel.X, Vel.Y + Gravity * dt);
        var next = Pos + vel * dt;
        double nx = ClampX(map, next.X);
        if (nx != next.X) vel = new Vec2(0, vel.Y);
        next = new Vec2(nx, next.Y);

        if (vel.Y > 0)
        {
            var landing = map.Platforms
                .Where(p => p.SpansX(next.X) && p.Y >= Pos.Y && p.Y <= next.Y)
                .OrderBy(p => p.Y)
                .FirstOrDefault();
            if (landing != null)
            {
                LandingSpeed = vel.Y;
                Land(landing, next.X);
                return;
            }
        }

        Vel = vel;
        Pos = next;

        // Fell through everything (a floor vanished, screens changed): put it back on a floor.
        double lowest = map.Platforms.Max(p => p.Y);
        if (Pos.Y > lowest + 400)
        {
            var floor = map.NearestFloor(Pos.X);
            Land(floor, Math.Clamp(Pos.X, floor.X0, floor.X1 - 1));
        }
    }

    void Land(Platform p, double x)
    {
        Mode = BodyMode.Grounded;
        Support = p;
        Pos = new Vec2(x, p.Y);
        Vel = new Vec2(0, 0);
        JustLanded = true;
    }

    double ClampX(SurfaceMap map, double x)
    {
        double min = map.WorkAreas.Min(w => w.Left) + 30, max = map.WorkAreas.Max(w => w.Right) - 30;
        if (x < min) { HitWall = true; return min; }
        if (x > max) { HitWall = true; return max; }
        return x;
    }

    /// <summary>Ballistic jump reaching <paramref name="apexAbove"/> px above the higher of start and target.</summary>
    public void JumpTo(Vec2 target, double apexAbove)
    {
        double apexY = Math.Min(Pos.Y, target.Y) - apexAbove;
        double vy = -Math.Sqrt(2 * Gravity * (Pos.Y - apexY));
        double tUp = -vy / Gravity;
        double tDown = Math.Sqrt(2 * (target.Y - apexY) / Gravity);
        double vx = (target.X - Pos.X) / (tUp + tDown);
        Vel = new Vec2(vx, vy);
        Mode = BodyMode.Airborne;
        Support = null;
    }

    /// <summary>After the window map changed: ride along with the supporting window, or fall if it is gone.</summary>
    public void FollowSupport(SurfaceMap map)
    {
        if (Mode != BodyMode.Grounded || Support == null) return;
        var r = map.Relocate(Support, Pos.X);
        if (r == null)
        {
            Mode = BodyMode.Airborne;
            Support = null;
            Vel = new Vec2(0, 0);
            return;
        }
        Support = r.Value.platform;
        Pos = new Vec2(r.Value.x, r.Value.platform.Y);
    }

    public void Grab()
    {
        Mode = BodyMode.Held;
        Support = null;
        _heldPrev = Pos;
        _heldVel = new Vec2(0, 0);
    }

    public void MoveHeld(Vec2 pos, double dt = 1 / 30.0)
    {
        if (Mode != BodyMode.Held) return;
        var v = (pos - _heldPrev) * (1 / dt);
        _heldVel = _heldVel * 0.6 + v * 0.4;
        _heldPrev = pos;
        Pos = pos;
    }

    /// <summary>Estimated hand speed while held, useful for a throw.</summary>
    public Vec2 HeldVelocity => _heldVel;

    public void Release(Vec2 throwVel)
    {
        const double maxThrow = 2500;
        if (throwVel.Length > maxThrow) throwVel = throwVel * (maxThrow / throwVel.Length);
        Mode = BodyMode.Airborne;
        Vel = throwVel;
    }

    /// <summary>Teleport onto a platform (summon, load).</summary>
    public void PlaceOn(Platform p, double x) => Land(p, Math.Clamp(x, p.X0, p.X1 - 1));
}
