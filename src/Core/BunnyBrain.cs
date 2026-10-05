using System;

namespace ZairaPet.Core;

/// <summary>
/// The rabbit (Bretzel): hops about the floor and never leaves it (dropped on a window, it hops down); loafs and
/// washes its face, and on a long rest sometimes lounges, half over on its side; zoomies are dashes broken by binkies,
/// leaps with a twist on the run; relaxed and tired it flops
/// onto its side to sleep; food it cannot get it thumps about with a hind foot. No meowing, no hunting.
/// </summary>
public sealed class BunnyBrain : PetBrain
{
    /// <summary>The slow hop (px/s): a third of its length per hop, two hops a second (Hop clip).</summary>
    public const double HopSpeed = 80;
    /// <summary>Dashing about (the half-bound, Run clip): a rabbit sprints far faster than it hops.</summary>
    public const double RunSpeed = 440;
    /// <summary>Whether it runs at all: the Run clip is set aside for now (review: to be redone), so it hops
    /// everywhere, its zoomies are hopping about with binkies, and a hungry dash to the bowl is a hop too.</summary>
    public const bool Runs = false;
    public const double DashAccel = 1600, DashBrake = 2000, DashCreep = 30;
    /// <summary>Length of the one-shot clips (tools/blender/bretzel_anim.py).</summary>
    public const double BinkyTime = 22 / 30.0, FlopTime = 1.0, ThumpTime = 0.8, LoungeDownTime = 36 / 30.0;
    /// <summary>Chance that a long rest (and any long one when it is happy) is spent lounging.</summary>
    public const double LoungeChance = 0.4;
    /// <summary>
    /// A binky is a leap on the run: the clip carries the body 1.6 m (BINKY_TRAVEL), about 180 px at 110 px a
    /// body length, so the pet goes on at this speed while it plays.
    /// </summary>
    public const double BinkySpeed = 245, BinkyTravel = BinkySpeed * BinkyTime;
    /// <summary>Chance that a dash has a binky in it.</summary>
    public const double BinkyChance = 0.45;

    bool _binkyDash;                   // this dash breaks into a binky once it is going
    double _sitUpFor;                  // a rest starts sitting up, looking about, for this long
    double _groomAt = -1, _groomFor;   // when (state time) it washes its face during a rest, and for how long
    double _loungeAt = -1;             // when (state time) it lets itself down to lounge for the rest of a rest
    bool _flopped;                     // asleep on its side
    bool _thumpUnreachable;

    public BunnyBrain(Random rng) : base(rng) { }

    protected override double StrollSpeed => HopSpeed;
    protected override double MaxJumpUp => 0;
    protected override double MaxJumpGap => 60;

    /// <summary>Along a path: walking, dropping down, or a hop across onto a surface no higher than this one.</summary>
    protected override bool CanTake(NavStep step, Platform from) =>
        step.Kind switch
        {
            NavStepKind.Climb => false,
            NavStepKind.Jump => step.Target.Y >= from.Y - 2,
            _ => true,
        };

    public override string GaitFor(double speed) => Runs && speed >= (HopSpeed + RunSpeed) / 2 ? "run" : "hop";

    public override string Describe(PetState s) => s switch
    {
        PetState.Idle => "si guarda intorno", PetState.Wander => "saltella in giro", PetState.Travel => "va da qualche parte",
        PetState.Zoomies => "corse pazze!", PetState.Eat => "mangia", PetState.Sleep => _flopped ? "dorme sul fianco" : "dorme",
        PetState.Sit => Lounging ? "rilassato sul fianco" : "a pagnotta", PetState.ChaseTreat => "corre al bocconcino", PetState.Petted => "si fa coccolare",
        PetState.Held => "in braccio", PetState.Airborne => "in volo", PetState.Landing => "atterra",
        PetState.Binky => "binky!", PetState.Flop => "si butta sul fianco", PetState.Thump => "batte la zampa",
        _ => s.ToString(),
    };

    protected override string PettedAction => "petted";
    protected override string SleepAction() => _flopped ? "flopsleep" : "sleep";
    protected override bool StopsToTurn(string action) => action == "run";

    protected override string? EnterAction(PetState s) => s switch
    {
        PetState.Binky => "binky",
        PetState.Flop => "flop",
        PetState.Thump => "thump",
        PetState.Zoomies when !Runs => "hop",
        _ => null,
    };

    protected override void OnEnter(PetState s)
    {
        if (s == PetState.Sleep) return;   // keeps _flopped from the flop that led into it
        _flopped = false;
        _groomAt = -1;
        _loungeAt = -1;
        _sitUpFor = 0;
    }

    // ---------------------------------------------------------------- decisions

    protected override void DecideIdle(CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        double r = _rng.NextDouble();
        if (Happy > 0 && r < 0.12 && RoomToBinky(body)) { Enter(PetState.Binky, BinkyTime); return; }   // happy: a binky for joy
        if (Happy > 0) r *= 0.7;
        if (r < 0.3)
        {
            var s = body.Support!;
            _wanderX = MathX.SafeClamp(body.Pos.X + (_rng.NextDouble() * 2 - 1) * 300, s.X0 + 30, s.X1 - 30);
            Enter(PetState.Wander, 15);
        }
        else if (r < 0.8)
            Rest(8 + _rng.NextDouble() * 22);
        else if (needs.Energy < 0.5 && r < 0.9)
            GetSleepy(body, needs);   // a nap on the spot
        else
            Enter(PetState.Idle, 3 + _rng.NextDouble() * 5);
    }

    /// <summary>A rest: sitting up looking about for a moment, then down into a loaf; a long one has a face wash, and
    /// some long ones (more of them when it is happy) are spent lounging, after the wash: sitting up, it lets itself
    /// down onto its side (loungedown) and stays so to the end, never loafing.</summary>
    void Rest(double seconds)
    {
        Enter(PetState.Sit, seconds);
        _sitUpFor = _rng.NextDouble() < 0.5 ? 1.5 + _rng.NextDouble() * 2.5 : 0;
        if (seconds >= 10)
        {
            _groomAt = _sitUpFor + 1 + _rng.NextDouble() * (seconds - _sitUpFor - 7);
            _groomFor = 3 + _rng.NextDouble() * 3;
            double from = _groomAt + _groomFor + 0.5;
            if ((Happy > 0 || _rng.NextDouble() < LoungeChance) && from < seconds - LoungeDownTime - 4) _loungeAt = from;
        }
    }

    bool Lounging => State == PetState.Sit && _loungeAt >= 0 && _stateTime >= _loungeAt;

    protected override string RestingAction()
    {
        if (_stateTime < _sitUpFor) return "sit";
        if (_groomAt >= 0 && _stateTime >= _groomAt && _stateTime < _groomAt + _groomFor) return "groom";
        if (Lounging) return _stateTime - _loungeAt < LoungeDownTime ? "loungedown" : "lounge";
        // lounging later on, it waits sitting up rather than going down into a loaf to get up again
        return _loungeAt >= 0 ? "sit" : "loaf";
    }

    /// <summary>Tired: it sleeps where it is. Relaxed (content, or just cuddled) it flops onto its side first.</summary>
    protected override void GetSleepy(CatBody body, Needs needs)
    {
        Goal = Goal.None;
        if (needs.Affection > 0.6 || Happy > 0) Enter(PetState.Flop, FlopTime);
        else Enter(PetState.Sleep);
    }

    protected override void NoFood(bool unreachable)
    {
        _thumpUnreachable = unreachable;
        Goal = Goal.None;
        Enter(PetState.Thump, ThumpTime);
    }

    protected override double ThinkSpecial(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        switch (State)
        {
            case PetState.Binky:
                Action = "binky";
                if (_stateTime < _stateDuration) return Facing * BinkySpeed;   // carried on by the leap
                needs.Play(0.05);
                if (Goal == Goal.Zoom && _zoomLeft > 0) Enter(PetState.Zoomies, _zoomLeft);
                else { Goal = Goal.None; Enter(PetState.Idle, 1 + _rng.NextDouble()); }
                return 0;

            case PetState.Flop:
                Action = "flop";
                if (_stateTime < _stateDuration) return 0;
                Enter(PetState.Sleep);
                _flopped = true;
                Action = SleepAction();
                return 0;

            case PetState.Thump:
                Action = "thump";
                Emote = "!";
                if (_stateTime < _stateDuration) return 0;
                // Sulk for a while: an empty bowl is looked at again sooner than one it cannot reach.
                _bowlCooldown = _thumpUnreachable ? 90 : 30;
                _bowlFails = 0;
                Rest(5 + _rng.NextDouble() * 5);
                return 0;

            default:
                return StandAbout(dt, body, needs, map, world);
        }
    }

    // ---------------------------------------------------------------- moving

    protected override double TravelSpeed(Needs needs, IWorld world) =>
        Runs && (State == PetState.ChaseTreat || Goal == Goal.Bowl && needs.Hunger > 0.8) ? RunSpeed : HopSpeed;

    /// <summary>
    /// Room on its support for a binky's leap ahead; if there is none, turned round when there is room behind.
    /// </summary>
    bool RoomToBinky(CatBody body)
    {
        var s = body.Support;
        if (s == null) return false;
        double Ahead(int dir) => dir > 0 ? s.X1 - body.Pos.X : body.Pos.X - s.X0;
        if (Ahead(Facing) >= BinkyTravel + 20) return true;
        if (Ahead(-Facing) < BinkyTravel + 20) return false;
        Facing = -Facing;
        return true;
    }

    /// <summary>How fast its zoomies go: a dash, or hopping about while it does not run.</summary>
    static double Top => Runs ? RunSpeed : HopSpeed;

    /// <summary>Dashes back and forth along the floor; often a dash breaks into a binky once it is going.</summary>
    protected override double DoZoomies(double dt, CatBody body, SurfaceMap map)
    {
        var s = body.Support!;
        bool arrived = !double.IsNaN(_zoomTarget) && Math.Abs(body.Pos.X - _zoomTarget) < 8;
        // at full tilt, with the rest of the dash still ahead of it: the leap, carried on by the run
        double ahead = double.IsNaN(_zoomTarget) ? 0 : Math.Abs(_zoomTarget - body.Pos.X);
        if (_binkyDash && _zoomSpeed > Top * 0.6 && ahead >= BinkyTravel + 20
            && Math.Sign(_zoomTarget - body.Pos.X) == Facing)
        {
            _binkyDash = false;
            _zoomLeft = Math.Max(0, _stateDuration - _stateTime);
            Goal = Goal.Zoom;
            Enter(PetState.Binky, BinkyTime);
            return Facing * BinkySpeed;
        }
        if (double.IsNaN(_zoomTarget) || arrived || !s.SpansX(_zoomTarget))
        {
            // a dash of a few body lengths, to one side or the other
            double len = 150 + _rng.NextDouble() * 350;
            double dir = _rng.NextDouble() < 0.5 ? -1 : 1;
            _zoomTarget = MathX.SafeClamp(body.Pos.X + dir * len, s.X0 + 30, s.X1 - 30);
            _binkyDash = _rng.NextDouble() < BinkyChance;
        }
        double v = RunTowardsZoomTarget(dt, body, Top, DashAccel, DashBrake, DashCreep);
        Action = Runs && _zoomSpeed > HopSpeed * 1.5 ? "run" : "hop";
        return v;
    }
}
