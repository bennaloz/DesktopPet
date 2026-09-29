using System;

namespace ZairaPet.Core;

/// <summary>
/// The cat (Zaira): walks, trots and gallops; jumps and climbs onto windows; hunts the cursor; meows at an empty
/// bowl; sits, lies down and tucks into a loaf; sleeps on the perch.
/// </summary>
public sealed class CatBrain : PetBrain
{
    public const double WalkSpeed = 110;
    /// <summary>The happy trot: diagonal legs, bouncy, tail straight up.</summary>
    public const double TrotSpeed = 150;
    /// <summary>The lope: getting somewhere fast without sprinting (starving, on the way to the bowl).</summary>
    public const double LopeSpeed = 300;
    /// <summary>Zoomies: flat out. ~2.2 m/s at her scale (a real cat gallops at 5-10 m/s; much slower looked like
    /// slow motion), 3 strides a second of about one and a half body lengths.</summary>
    public const double RunSpeed = 720;
    /// <summary>Zoomies speed up and slow down like a cat (px/s²): flat out in about a third of a second, stopping in
    /// a quarter. Stopping dead from a gallop to turn round looked like a stutter.</summary>
    public const double ZoomAccel = 2400, ZoomBrake = 3200, ZoomCreep = 40;
    public const double CatJumpUp = 460;
    public const double CatJumpGap = 340;
    /// <summary>Loading the hind legs before a jump, and each extra "taking aim" after it.</summary>
    public const double CrouchTime = 0.45, AimTime = 0.5;
    /// <summary>How close (px from the body) a moving cursor must come to catch her eye.</summary>
    public const double HuntRange = 260;
    /// <summary>Playfulness above which she crouches and follows the cursor, and above which she pounces.</summary>
    public const double StalkFrom = 0.35, PounceFrom = 0.7;
    /// <summary>A sit planned at least this long turns into a loaf...</summary>
    public const double LoafFromSit = 15;
    /// <summary>...after sitting up for this long (seconds, picked at random in the range)...</summary>
    public static readonly (double min, double max) LoafAfter = (3, 6);
    /// <summary>...and lying on her belly with the front paws out for this long before tucking them in.</summary>
    public static readonly (double min, double max) TuckAfter = (1.5, 3);
    /// <summary>Lying down from sitting and tucking the paws in take a cat this long: the length of the
    /// LieDown and Tuck clips (tools/blender/anim.py).</summary>
    public const double LieDownTime = 2.0, TuckTime = 1.6;

    double _prejump;          // crouch before a jump
    double _prejumpPlan = -1; // how long this take-off lasts: 0 straight away, a crouch, or taking aim
    double _huntCooldown;     // leave the cursor alone until this runs out
    int _huntLevel;           // 0 watch, 1 stalk, 2 stalk + wiggle + pounce
    double _swatLeft;         // a swat in progress
    int _swats;
    double _wiggleAt;         // when (state time) the rump wiggle starts
    double _pounceAt;         // when (state time) she leaps
    double _loafAt = -1;      // when (state time) a long sit turns into lying down; -1 never
    double _tuckAt;           // how long after lying down she tucks the front paws in (the loaf)

    public CatBrain(Random rng) : base(rng) { }

    protected override double StrollSpeed => WalkSpeed;
    protected override double MaxJumpUp => CatJumpUp;
    protected override double MaxJumpGap => CatJumpGap;

    /// <summary>Sit up, then lie down as a loaf for a while (self test, debugging).</summary>
    public void LoafFor(double seconds)
    {
        SitFor(seconds);
        _loafAt = LoafAfter.min;
        _tuckAt = TuckAfter.min;
    }

    /// <summary>Forget the last hunt so the next moving cursor is chased at once (self test).</summary>
    public void ForgetHunt() => _huntCooldown = 0;

    public override string Describe(PetState s) => s switch
    {
        PetState.Idle => "si guarda intorno", PetState.Wander => "passeggia", PetState.Travel => "va da qualche parte",
        PetState.Zoomies => "zoomies!", PetState.Eat => "mangia", PetState.Sleep => "dorme", PetState.Sit => "seduta",
        PetState.Meow => "reclama la pappa", PetState.ChaseTreat => "insegue il bocconcino", PetState.Petted => "fa le fusa",
        PetState.Held => "in braccio", PetState.Airborne => "in volo", PetState.Landing => "atterra",
        PetState.Climb => "si arrampica", _ => s.ToString(),
    };

    protected override void Tick(double dt)
    {
        _huntCooldown = Math.Max(0, _huntCooldown - dt);
        _swatLeft = Math.Max(0, _swatLeft - dt);
    }

    protected override void OnEnter(PetState s)
    {
        _prejump = 0;
        _prejumpPlan = -1;
        _loafAt = -1;
    }

    protected override string? EnterAction(PetState s) => s switch
    {
        PetState.Meow => "meow",
        PetState.Hunt => "idle",
        _ => null,
    };

    protected override bool StopsToTurn(string action) => action is "run" or "lope" or "trot";

    protected override void BeforeThink(double dt, CatBody body, Needs needs, bool calm)
    {
        if (calm && State is not (PetState.Petted or PetState.Meow) && _petting <= 0 && _huntCooldown <= 0
            && _cursorStill < 0.5 && CursorDistance(body) is > 45 and < HuntRange)
            StartHunt(needs);
    }

    protected override double ThinkSpecial(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        switch (State)
        {
            case PetState.Meow:
                Action = (_stateTime % 5) < 1.2 ? "meow" : "sit";
                if (Action == "meow") Emote = "!";
                // Exhausted, or the bowl cannot be reached: stop insisting for a while.
                if (needs.Energy < 0.15 || _bowlFails >= 3)
                {
                    _bowlCooldown = 90;
                    _bowlFails = 0;
                    Goal = Goal.None;
                    Enter(PetState.Idle, 1);
                    return 0;
                }
                if (_stateTime > 5 && world.BowlFood > 0.05) { Goal = Goal.Bowl; Enter(PetState.Travel); }
                if (needs.Hunger < 0.5) Enter(PetState.Idle, 1);
                return 0;

            case PetState.Hunt:
                return DoHunt(dt, body, needs);

            default:
                return StandAbout(dt, body, needs, map, world);
        }
    }

    // ---------------------------------------------------------------- decisions

    protected override void GetSleepy(CatBody body, Needs needs)
    {
        Goal = Goal.Perch;
        Enter(PetState.Travel);
    }

    protected override void NoFood(bool unreachable) => Enter(PetState.Meow);

    protected override void DecideIdle(CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        double r = _rng.NextDouble();
        if (Happy > 0) r *= 0.6;   // in a good mood: more strolling about, less sitting
        if (r < 0.35)
        {
            var s = body.Support!;
            _wanderX = MathX.SafeClamp(body.Pos.X + (_rng.NextDouble() * 2 - 1) * 400, s.X0 + 30, s.X1 - 30);
            Enter(PetState.Wander, 12);
        }
        else if (r < 0.6 && PickExplore(body, map))
        {
            Goal = Goal.Explore;
            Enter(PetState.Travel);
        }
        else if (r < 0.85)
        {
            Enter(PetState.Sit, 8 + _rng.NextDouble() * 20);
            // Settling in for a long one: sit up for a moment, then tuck the paws in.
            if (_stateDuration >= LoafFromSit)
            {
                _loafAt = LoafAfter.min + _rng.NextDouble() * (LoafAfter.max - LoafAfter.min);
                _tuckAt = TuckAfter.min + _rng.NextDouble() * (TuckAfter.max - TuckAfter.min);
            }
        }
        else if (needs.Energy < 0.5 && r < 0.93)
            Enter(PetState.Sleep);  // cat nap on the spot
        else
            Enter(PetState.Idle, 3 + _rng.NextDouble() * 5);
    }

    /// <summary>
    /// A long sit: sitting up, lying down (the front paws stepping forward), lying with the paws out, tucking
    /// them in, then the loaf. Each change takes the time a cat takes; a short sit stays a sit.
    /// </summary>
    protected override string RestingAction()
    {
        double t = _stateTime - _loafAt;
        if (_loafAt < 0 || t < 0) return "sit";
        if (t < LieDownTime) return "liedown";
        t -= LieDownTime;
        if (t < _tuckAt) return "crouch";
        return t < _tuckAt + TuckTime ? "tuck" : "loaf";
    }

    // ---------------------------------------------------------------- moving

    protected override double WanderSpeed() => Happy > 0 ? TrotSpeed : WalkSpeed;

    protected override double TravelSpeed(Needs needs, IWorld world) =>
        State == PetState.ChaseTreat ? RunSpeed
        : Goal == Goal.Bowl && needs.Hunger > 0.8 ? LopeSpeed
        : Goal == Goal.Bowl && world.BowlFood > 0.02 || Happy > 0 ? TrotSpeed   // pleased: food waiting, or cheered up
        : WalkSpeed;

    /// <summary>The animation for a ground speed: walk, happy trot, lope, sprint.</summary>
    public override string GaitFor(double speed) =>
        speed >= RunSpeed ? "run" : speed >= LopeSpeed ? "lope" : speed > WalkSpeed + 1 ? "trot" : "walk";

    protected override void OnWalkingToStep() => _prejump = 0;

    /// <summary>At the takeoff point: crouch briefly, then jump (or grab the window side).</summary>
    protected override double TakeOff(double dt, CatBody body, NavStep hop)
    {
        if (hop.Kind == NavStepKind.Climb)
        {
            Facing = -hop.Via!.Side;
            _afterLanding = Goal;
            body.StartClimb(hop.Via, hop.Target, hop.LandX);
            Enter(PetState.Climb);
            return 0;
        }
        Facing = Math.Sign(hop.LandX - body.Pos.X) is var s && s != 0 ? s : Facing;
        JumpTarget = new Vec2(hop.LandX, hop.Target.Y);
        if (_prejumpPlan < 0)
        {
            // Every jump its own way: sometimes she just goes, sometimes she crouches first,
            // sometimes she takes aim two or three times before leaping.
            double r = _rng.NextDouble();
            _prejumpPlan = r < 0.3 ? 0 : r < 0.65 ? CrouchTime : CrouchTime + AimTime * (2 + _rng.Next(2));
        }
        _prejump += dt;
        // load the hind legs, eyes on the target; then, if she is taking aim, measure it a few more times
        Action = _prejump <= CrouchTime ? "prejump" : "aim";
        if (_prejump < _prejumpPlan) return 0;
        _prejump = 0;
        _prejumpPlan = -1;
        double dx = Math.Abs(hop.LandX - body.Pos.X);
        double apex = hop.Kind == NavStepKind.Jump ? 35 + dx * 0.12 : 15;
        body.JumpTo(new Vec2(hop.LandX, hop.Target.Y), apex);
        return 0;
    }

    protected override double DoZoomies(double dt, CatBody body, SurfaceMap map)
    {
        _speed = RunSpeed;
        var s = body.Support!;
        if (double.IsNaN(_zoomTarget) || Math.Abs(body.Pos.X - _zoomTarget) < 8 || !s.SpansX(_zoomTarget))
        {
            // Sometimes leap onto another surface in the middle of a sprint.
            if (_rng.NextDouble() < 0.3 && PickExplore(body, map))
            {
                var path = FindPath(map, s, body.Pos.X, _exploreTarget!, _exploreX);
                if (path != null && path.Count >= 2 && path[1].Kind is NavStepKind.Jump or NavStepKind.Drop
                    && Math.Abs(path[0].X - body.Pos.X) < 400)
                {
                    _zoomTarget = double.NaN;
                    var hop = path[1];
                    if (Math.Abs(body.Pos.X - path[0].X) < 10)
                    {
                        JumpTarget = new Vec2(hop.LandX, hop.Target.Y);
                        body.JumpTo(JumpTarget.Value, 40 + Math.Abs(hop.LandX - body.Pos.X) * 0.1);
                        return 0;
                    }
                    _zoomTarget = path[0].X;
                }
            }
            if (double.IsNaN(_zoomTarget))
            {
                double span = s.X1 - s.X0 - 60;
                _zoomTarget = s.X0 + 30 + _rng.NextDouble() * Math.Max(1, span);
            }
        }
        double v = RunTowardsZoomTarget(dt, body, RunSpeed, ZoomAccel, ZoomBrake, ZoomCreep);
        // the gait follows the speed: gallop, lope, trot while slowing down or getting going
        Action = _zoomSpeed > (LopeSpeed + RunSpeed) / 2 ? "run" : _zoomSpeed > TrotSpeed ? "lope" : "trot";
        return v;
    }

    // ---------------------------------------------------------------- hunting the cursor

    /// <summary>Distance from the cursor to the middle of the body (it moves the head, not the feet).</summary>
    double CursorDistance(CatBody body) =>
        Cursor is { } c ? (c - (body.Pos + new Vec2(0, -40))).Length : double.MaxValue;

    void StartHunt(Needs needs)
    {
        _huntLevel = needs.Playfulness >= PounceFrom ? 2 : needs.Playfulness >= StalkFrom ? 1 : 0;
        _swats = 0;
        _swatLeft = 0;
        _wiggleAt = 0.8 + _rng.NextDouble() * 0.6;
        _pounceAt = _wiggleAt + 1.2 + _rng.NextDouble();
        Goal = Goal.None;
        Enter(PetState.Hunt);
    }

    void EndHunt(Needs needs, double played)
    {
        needs.Play(played);
        _huntCooldown = 25 + _rng.NextDouble() * 35;
        Enter(PetState.Sit, 3 + _rng.NextDouble() * 3);
    }

    double DoHunt(double dt, CatBody body, Needs needs)
    {
        var c = Cursor;
        // Gone, gone still for long, or simply over: back to her business.
        if (c == null || CursorDistance(body) > HuntRange * 1.4 || _stateTime > 15 || _cursorStill > 6)
        {
            EndHunt(needs, 0.05 + 0.1 * _huntLevel);
            return 0;
        }
        double dx = c.Value.X - body.Pos.X;
        if (Math.Abs(dx) > 12 && Math.Sign(dx) != Facing) Facing = Math.Sign(dx);   // keep it in front

        if (_huntLevel == 0) { Action = "idle"; return 0; }   // stops and watches it

        if (_swatLeft > 0) { Action = "swat"; return 0; }
        double ahead = dx * Facing;
        bool inReach = ahead > 20 && ahead < EatReach + 60 && c.Value.Y > body.Pos.Y - 110 && c.Value.Y < body.Pos.Y + 10;
        if (inReach && _stateTime > 0.4)
        {
            if (++_swats > 3) { EndHunt(needs, 0.3); return 0; }
            _swatLeft = 0.5;
            Action = "swat";
            return 0;
        }

        if (_huntLevel == 1 || _stateTime < _wiggleAt) { Action = "stalk"; return 0; }
        if (_stateTime < _pounceAt) { Action = "wiggle"; return 0; }

        // Pounce: land with the head on the cursor, if it is low enough to reach and on this surface.
        var s = body.Support!;
        double landX = MathX.SafeClamp(c.Value.X - Facing * EatReach, s.X0 + 1, s.X1 - 1);
        if (c.Value.Y < body.Pos.Y - 260 || Math.Abs(landX - body.Pos.X) < 30) { Action = "stalk"; return 0; }
        JumpTarget = new Vec2(landX, s.Y);
        body.JumpTo(JumpTarget.Value, 25 + Math.Abs(landX - body.Pos.X) * 0.12);
        needs.Play(0.4);
        _huntCooldown = 25 + _rng.NextDouble() * 35;
        return 0;
    }
}
