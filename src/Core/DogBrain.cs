using System;

namespace ZairaPet.Core;

/// <summary>
/// The golden retriever: walks, trots when pleased, gallops in its zoomies, all along the floor (a dog does not
/// climb onto windows; dropped on one, it jumps down); sits, then lies down with its front legs out; sleeps where
/// it is; barks for food it cannot get. Its tail says the most: <see cref="TailMoods"/> for a dog wags it.
/// </summary>
public sealed class DogBrain : PetBrain
{
    // Speeds of its clips at 200 px long (the Zaira clips on the dog's legs: tools/blender/anim.py, PET=golden).
    public const double WalkSpeed = 145;
    public const double TrotSpeed = 215;
    public const double LopeSpeed = 420;
    public const double RunSpeed = 900;
    public const double ZoomAccel = 2600, ZoomBrake = 3400, ZoomCreep = 40;
    /// <summary>A sit planned at least this long turns into lying down, after sitting up for a moment.</summary>
    public const double LieFromSit = 12;
    public static readonly (double min, double max) LieAfter = (2, 5);
    /// <summary>How long it keeps barking at a bowl before it gives up for a while.</summary>
    public const double BarkFor = 6;

    double _lieAt = -1;              // when (state time) a long sit turns into lying down; -1 never
    bool _barkUnreachable;

    public DogBrain(Random rng) : base(rng) { }

    protected override double StrollSpeed => WalkSpeed;
    protected override double MaxJumpUp => 0;
    protected override double MaxJumpGap => 80;

    /// <summary>Along a path: walking, jumping down, or across onto a surface no higher than this one.</summary>
    protected override bool CanTake(NavStep step, Platform from) =>
        step.Kind switch
        {
            NavStepKind.Climb => false,
            NavStepKind.Jump => step.Target.Y >= from.Y - 2,
            _ => true,
        };

    public override string GaitFor(double speed) =>
        speed >= RunSpeed ? "run" : speed >= LopeSpeed ? "lope" : speed > WalkSpeed + 1 ? "trot" : "walk";

    public override string Describe(PetState s) => s switch
    {
        PetState.Idle => "si guarda intorno", PetState.Wander => "gironzola", PetState.Travel => "va da qualche parte",
        PetState.Zoomies => "corse pazze!", PetState.Eat => "mangia", PetState.Sleep => "dorme",
        PetState.Sit => Action is "crouch" or "liedown" ? "sdraiato" : "seduto", PetState.ChaseTreat => "corre al bocconcino",
        PetState.Petted => "scodinzola alle coccole", PetState.Held => "in braccio", PetState.Airborne => "in volo",
        PetState.Landing => "atterra", PetState.Bark => "abbaia per la pappa", _ => s.ToString(),
    };

    protected override string PettedAction => "petted";
    protected override bool StopsToTurn(string action) => action is "run" or "lope" or "trot";

    protected override string? EnterAction(PetState s) => s == PetState.Bark ? "bark" : null;

    protected override void OnEnter(PetState s) => _lieAt = -1;

    // ---------------------------------------------------------------- decisions

    protected override void DecideIdle(CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        double r = _rng.NextDouble();
        if (Happy > 0) r *= 0.6;   // pleased: more trotting about
        if (r < 0.35)
        {
            var s = body.Support!;
            _wanderX = MathX.SafeClamp(body.Pos.X + (_rng.NextDouble() * 2 - 1) * 450, s.X0 + 40, s.X1 - 40);
            Enter(PetState.Wander, 14);
        }
        else if (r < 0.82)
            Rest(8 + _rng.NextDouble() * 25);
        else if (needs.Energy < 0.5 && r < 0.92)
            Enter(PetState.Sleep);   // a nap on the spot
        else
            Enter(PetState.Idle, 3 + _rng.NextDouble() * 5);
    }

    /// <summary>A rest: sitting, and for a long one lying down after a moment (front legs out, head up).</summary>
    void Rest(double seconds)
    {
        Enter(PetState.Sit, seconds);
        if (seconds >= LieFromSit) _lieAt = LieAfter.min + _rng.NextDouble() * (LieAfter.max - LieAfter.min);
    }

    protected override string RestingAction()
    {
        double t = _stateTime - _lieAt;
        if (_lieAt < 0 || t < 0) return "sit";
        return t < CatBrain.LieDownTime ? "liedown" : "crouch";
    }

    protected override void GetSleepy(CatBody body, Needs needs)
    {
        Goal = Goal.None;
        Enter(PetState.Sleep);
    }

    protected override void NoFood(bool unreachable)
    {
        _barkUnreachable = unreachable;
        Goal = Goal.None;
        Enter(PetState.Bark, BarkFor);
    }

    protected override double ThinkSpecial(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        if (State != PetState.Bark) return StandAbout(dt, body, needs, map, world);
        // a woof or two, then looking at you, again
        Action = (_stateTime % 2.5) < 0.9 ? "bark" : "sit";
        if (Action == "bark") Emote = "!";
        if (_stateTime < _stateDuration) return 0;
        _bowlCooldown = _barkUnreachable ? 90 : 40;
        _bowlFails = 0;
        Rest(6 + _rng.NextDouble() * 6);
        return 0;
    }

    // ---------------------------------------------------------------- moving

    protected override double WanderSpeed() => Happy > 0 ? TrotSpeed : WalkSpeed;

    protected override double TravelSpeed(Needs needs, IWorld world) =>
        State == PetState.ChaseTreat ? LopeSpeed
        : Goal == Goal.Bowl && needs.Hunger > 0.8 ? LopeSpeed
        : Goal == Goal.Bowl && world.BowlFood > 0.02 || Happy > 0 ? TrotSpeed
        : WalkSpeed;

    /// <summary>Galloping back and forth along the floor, speeding up and braking like a dog.</summary>
    protected override double DoZoomies(double dt, CatBody body, SurfaceMap map)
    {
        var s = body.Support!;
        if (double.IsNaN(_zoomTarget) || Math.Abs(body.Pos.X - _zoomTarget) < 8 || !s.SpansX(_zoomTarget))
        {
            double len = 300 + _rng.NextDouble() * 600;
            double dir = _rng.NextDouble() < 0.5 ? -1 : 1;
            _zoomTarget = MathX.SafeClamp(body.Pos.X + dir * len, s.X0 + 40, s.X1 - 40);
        }
        double v = RunTowardsZoomTarget(dt, body, RunSpeed, ZoomAccel, ZoomBrake, ZoomCreep);
        Action = _zoomSpeed > (LopeSpeed + RunSpeed) / 2 ? "run" : _zoomSpeed > TrotSpeed ? "lope" : "trot";
        return v;
    }
}
