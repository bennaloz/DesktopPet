using System;
using System.Collections.Generic;
using System.Linq;

namespace ZairaPet.Core;

/// <summary>What the brain needs to know about the props on the desktop.</summary>
public interface IWorld
{
    Platform? BowlPlatform { get; }
    double BowlX { get; }
    double BowlFood { get; }
    Platform? PerchTop { get; }
    double PerchX { get; }
    Vec2? TreatPos { get; }
    bool TreatLanded { get; }
    void EatFromBowl(double amount);
    void ConsumeTreat();
}

/// <summary>
/// What a pet is doing. The first ones every species has; then the ones of a single species, which the others
/// never enter.
/// </summary>
public enum PetState
{
    Idle, Wander, Travel, Zoomies, Eat, Sleep, Sit, ChaseTreat, Petted, Held, Airborne, Landing,
    /// <summary>Cat only.</summary>
    Meow, Climb, Hunt,
    /// <summary>Rabbit only.</summary>
    Groom, Binky, Flop, Thump,
    /// <summary>Dog only.</summary>
    Bark,
}

/// <summary>Why the pet is travelling: decides what happens on arrival.</summary>
public enum Goal { None, Bowl, Perch, Treat, Explore, Wander, Zoom }

/// <summary>
/// Behaviour state machine shared by every pet. Each Update picks a behaviour from the needs, moves the body and
/// exposes the logical animation (<see cref="Action"/>) and facing for the visual. What a species does its own way
/// (its gaits, how it rests and sleeps, its zoomies, where it can go, its own states) the species brain decides.
/// </summary>
public abstract class PetBrain
{
    public const double TravelTimeout = 45;
    /// <summary>Time to stop and turn round before running the other way (the model turns through the viewer side).</summary>
    public const double TurnTime = 0.3;
    /// <summary>A target this close behind it counts as reached rather than worth turning round for.</summary>
    public const double TurnSlack = 24;
    /// <summary>The cursor must stay behind it this long (s) before it turns round to face it...</summary>
    public const double TurnToCursorAfter = 0.8;
    /// <summary>...and be at least this far behind (px, from the middle of the body) and within this distance.</summary>
    public const double TurnToCursorBehind = 50, TurnToCursorRange = 400;

    protected readonly Random _rng;

    public PetState State { get; private set; } = PetState.Idle;
    public Goal Goal { get; protected set; }
    /// <summary>Logical animation, mapped to clips by the profile (walk, sit, sleep, eat, held...).</summary>
    public string Action { get; protected set; } = "idle";
    /// <summary>+1 facing right, -1 facing left.</summary>
    public int Facing { get; protected set; } = 1;
    /// <summary>Short symbol shown over the pet: ♥ z ! or null.</summary>
    public string? Emote { get; protected set; }
    /// <summary>How far ahead of the body centre the mouth reaches when eating (px): where to stop before food.</summary>
    public double EatReach { get; set; } = 45;
    /// <summary>Seconds of good mood left (after petting, when called).</summary>
    public double Happy { get; private set; }
    /// <summary>The mouse cursor on the desktop (screen px), set by the game every frame; null if unknown.</summary>
    public Vec2? Cursor { get; set; }
    /// <summary>Where the jump being prepared or flown will land (screen px), for the eyes; null otherwise.</summary>
    public Vec2? JumpTarget { get; protected set; }

    protected double _stateTime;        // time spent in the current state
    protected double _stateDuration;    // planned length for timed states
    protected double _petting;          // recent petting, decays
    protected double _speed;
    protected double _zoomTarget = double.NaN;
    protected double _zoomSpeed;        // current zoomies speed, ramping up and down
    protected int _stuckFrames;
    protected Platform? _exploreTarget;
    protected double _exploreX;
    protected double _wanderX;
    protected double _zoomLeft;
    protected Goal _afterLanding;
    protected Vec2? _treatIgnored;      // a treat we could not reach: leave it alone
    protected int _bowlFails;           // failed trips to the bowl in a row
    protected double _bowlCooldown;     // seconds before trying the bowl again after giving up
    protected double _turnLeft;         // stopped, turning round
    Vec2? _lastCursor;
    protected double _cursorStill = 99; // seconds since the cursor last moved
    double _cursorBehind;               // seconds the cursor has stayed behind it while it stood or sat about

    protected PetBrain(Random rng)
    {
        _rng = rng;
        _speed = StrollSpeed;
    }

    // ---------------------------------------------------------------- what each species decides

    /// <summary>Its everyday speed (px/s): wandering about, and the pace below which it turns round on the spot.</summary>
    protected abstract double StrollSpeed { get; }
    /// <summary>How high (px) it can jump up and how wide a gap it can clear.</summary>
    protected abstract double MaxJumpUp { get; }
    protected abstract double MaxJumpGap { get; }
    /// <summary>The animation for a ground speed.</summary>
    public abstract string GaitFor(double speed);
    /// <summary>How fast it travels towards the current goal.</summary>
    protected abstract double TravelSpeed(Needs needs, IWorld world);
    /// <summary>How fast it wanders about.</summary>
    protected virtual double WanderSpeed() => StrollSpeed;
    /// <summary>With nothing pressing: stroll, explore, sit, nap or stand about.</summary>
    protected abstract void DecideIdle(CatBody body, Needs needs, SurfaceMap map, IWorld world);
    /// <summary>Zoomies: running about. Returns the horizontal speed.</summary>
    protected abstract double DoZoomies(double dt, CatBody body, SurfaceMap map);
    /// <summary>Tired: go and sleep (on the perch, or on the spot).</summary>
    protected abstract void GetSleepy(CatBody body, Needs needs);
    /// <summary>The food is out of reach, or the bowl is empty.</summary>
    protected abstract void NoFood(bool unreachable);
    /// <summary>The overlay's words for a state.</summary>
    public abstract string Describe(PetState s);
    /// <summary>The animation while sitting about (the long sit may turn into lying down).</summary>
    protected virtual string RestingAction() => "sit";
    protected virtual string SleepAction() => "sleep";
    protected virtual string PettedAction => "purr";
    /// <summary>Running actions it stops before turning round (the visual turns it through the viewer side).</summary>
    protected virtual bool StopsToTurn(string action) => false;
    /// <summary>Whether it can take this step of a path (walking, jumping up or down, climbing a window side).</summary>
    protected virtual bool CanTake(NavStep step, Platform from) => true;
    /// <summary>At the takeoff point of a jump or a climb along a path. Returns the horizontal speed.</summary>
    protected virtual double TakeOff(double dt, CatBody body, NavStep hop)
    {
        Facing = Math.Sign(hop.LandX - body.Pos.X) is var s && s != 0 ? s : Facing;
        JumpTarget = new Vec2(hop.LandX, hop.Target.Y);
        body.JumpTo(JumpTarget.Value, 15 + Math.Abs(hop.LandX - body.Pos.X) * 0.1);
        return 0;
    }
    /// <summary>Species reactions before the state runs (a cat starts hunting a cursor).</summary>
    protected virtual void BeforeThink(double dt, CatBody body, Needs needs, bool calm) { }
    /// <summary>States of a single species. Returns the horizontal speed.</summary>
    protected virtual double ThinkSpecial(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world) =>
        StandAbout(dt, body, needs, map, world);
    /// <summary>Species timers, every frame.</summary>
    protected virtual void Tick(double dt) { }
    /// <summary>Called on entering a state, after the common reset.</summary>
    protected virtual void OnEnter(PetState s) { }
    /// <summary>The action a species state starts with; null keeps the current one.</summary>
    protected virtual string? EnterAction(PetState s) => null;

    // ---------------------------------------------------------------- input from the game

    public void OnGrab(CatBody body)
    {
        body.Grab();
        Enter(PetState.Held);
        Goal = Goal.None;
    }

    public void OnRelease(CatBody body, Vec2 throwVel, SurfaceMap? map = null)
    {
        body.Release(throwVel, map);
        Enter(PetState.Airborne);
        _afterLanding = Goal.None;
    }

    /// <summary>Called every frame the cursor strokes the pet.</summary>
    public void OnPetting(double dt)
    {
        _petting = Math.Min(_petting + dt * 2, 3);
        Cheer(25);
    }

    /// <summary>Put the pet in a good mood for a while.</summary>
    public void Cheer(double seconds) => Happy = Math.Max(Happy, seconds);

    /// <summary>Something happened (a treat appeared, needs changed): a resting pet reconsiders right away.</summary>
    public void Notice()
    {
        if (State is PetState.Idle or PetState.Sit or PetState.Wander or PetState.Petted or PetState.Meow or PetState.Bark)
            Enter(PetState.Idle, 0.3);
    }

    /// <summary>Keep the pet sitting still for a while (self test, debugging).</summary>
    public void SitFor(double seconds)
    {
        Goal = Goal.None;
        Enter(PetState.Sit, seconds);
    }

    /// <summary>Keep the pet standing still for a while (self test, debugging).</summary>
    public void StandFor(double seconds)
    {
        Goal = Goal.None;
        Enter(PetState.Idle, seconds);
    }

    /// <summary>Send the pet to a given spot (self test, debugging).</summary>
    public void ExploreTo(Platform p, double x)
    {
        _exploreTarget = p;
        _exploreX = x;
        Goal = Goal.Explore;
        Enter(PetState.Travel);
    }

    /// <summary>Bring the pet back onto a floor (tray "call").</summary>
    public void Summon(CatBody body, Platform floor, double x)
    {
        body.PlaceOn(floor, x);
        Goal = Goal.None;
        Cheer(15);
        Enter(PetState.Idle, 2);
    }

    // ---------------------------------------------------------------- main loop

    public void Update(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        _stateTime += dt;
        _petting = Math.Max(0, _petting - dt);
        _bowlCooldown = Math.Max(0, _bowlCooldown - dt);
        _turnLeft = Math.Max(0, _turnLeft - dt);
        Happy = Math.Max(0, Happy - dt);
        Tick(dt);
        bool moved = Cursor is { } cur && _lastCursor is { } last && (cur - last).Length > 0.5;
        _cursorStill = moved ? 0 : _cursorStill + dt;
        _lastCursor = Cursor;
        needs.Tick(dt, State == PetState.Sleep);
        Emote = null;

        if (State == PetState.Held)
        {
            Action = "held";
            if (_petting > 0.5) { needs.Pet(dt); Emote = "♥"; }
            return;
        }

        double walk = 0;
        if (body.Mode == BodyMode.Climbing)
        {
            if (State != PetState.Climb) Enter(PetState.Climb);
            Action = "climb";
            Facing = -body.Wall!.Side;   // facing the window side it hangs on
        }
        else if (body.Mode == BodyMode.Airborne)
        {
            if (State != PetState.Airborne)
            {
                _afterLanding = State is PetState.Travel or PetState.Zoomies or PetState.ChaseTreat ? Goal
                              : State == PetState.Climb ? _afterLanding : Goal.None;
                if (State == PetState.Zoomies) _zoomLeft = Math.Max(0, _stateDuration - _stateTime);
                Enter(PetState.Airborne);
            }
            Action = Flight.Stretched(body.Vel) ? "jump" : "fall";
            if (Math.Abs(body.Vel.X) > 20) Facing = Math.Sign(body.Vel.X);
        }
        else
        {
            walk = Think(dt, body, needs, map, world);
            if (_turnLeft > 0 && StopsToTurn(Action)) Action = "idle";   // stopped for a moment, turning
        }

        body.Step(dt, map, walk);
        if (body.Mode != BodyMode.Airborne && Action != "prejump") JumpTarget = null;

        if (body.JustLanded)
        {
            if (State == PetState.Climb)
                ResumeAfterLanding();
            else if (body.LandingSpeed > 1100)
                Enter(PetState.Landing, 0.45);
            else if (body.LandingSpeed > 450)
                Enter(PetState.Landing, 0.3);   // an ordinary jump: a quick absorb on the front legs
            else
                ResumeAfterLanding();
        }
        if (body.HitWall && State is PetState.Wander or PetState.Zoomies)
        {
            Facing = -Facing;
            if (State == PetState.Zoomies) _turnLeft = TurnTime;
            _zoomTarget = double.NaN;
            _wanderX = body.Pos.X + Facing * 150;
        }
    }

    protected void ResumeAfterLanding()
    {
        if (_afterLanding is Goal.Bowl or Goal.Perch or Goal.Treat or Goal.Explore)
        {
            Goal = _afterLanding;
            Enter(Goal == Goal.Treat ? PetState.ChaseTreat : PetState.Travel);
        }
        else if (_afterLanding == Goal.Zoom)
            Enter(PetState.Zoomies, _zoomLeft);
        else
            Enter(PetState.Idle, 1 + _rng.NextDouble());
        _afterLanding = Goal.None;
    }

    /// <summary>Grounded behaviour. Returns the horizontal walking speed.</summary>
    double Think(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        bool calm = State is PetState.Idle or PetState.Sit or PetState.Wander or PetState.Meow or PetState.Bark or PetState.Petted
                    || State == PetState.Travel && Goal == Goal.Explore;
        if (_petting > 0.6 && calm && State != PetState.Petted)
            Enter(PetState.Petted);
        else
            BeforeThink(dt, body, needs, calm);

        switch (State)
        {
            case PetState.Landing:
                Action = "land";
                if (_stateTime >= _stateDuration) ResumeAfterLanding();
                return 0;

            case PetState.Airborne:
                ResumeAfterLanding();
                return 0;

            case PetState.Petted:
                Action = PettedAction;
                Emote = "♥";
                if (_petting > 0) needs.Pet(dt);
                if (_petting <= 0 && _stateTime > 1.5) Enter(PetState.Sit, 4 + _rng.NextDouble() * 6);
                return 0;

            case PetState.Sleep:
                Action = SleepAction();
                Emote = "z";
                if (_petting > 0) needs.Pet(dt * 0.5);
                if (needs.Energy >= 0.98) { Goal = Goal.None; Enter(PetState.Idle, 2); }
                return 0;

            case PetState.Eat:
                Action = "eat";
                if (world.BowlFood <= 0 || needs.Hunger <= 0.02) { Goal = Goal.None; Enter(PetState.Sit, 5); return 0; }
                world.EatFromBowl(0.06 * dt);
                needs.Eat(0.12 * dt);
                return 0;

            case PetState.Zoomies:
                if (_stateTime >= _stateDuration)
                {
                    needs.Play(0.8);
                    Enter(PetState.Sit, 5);
                    return 0;
                }
                return DoZoomies(dt, body, map);

            case PetState.Travel:
            case PetState.ChaseTreat:
                // Watchdog: a trip that never ends means something unforeseen; give up and look around.
                if (_stateTime > TravelTimeout) { Goal = Goal.None; Enter(PetState.Idle, 2); return 0; }
                return DoTravel(dt, body, needs, map, world);

            case PetState.Wander:
                _speed = WanderSpeed();
                Action = GaitFor(_speed);
                if (Math.Abs(body.Pos.X - _wanderX) < 4 || _stateTime > _stateDuration || !body.Support!.SpansX(_wanderX))
                {
                    Enter(PetState.Idle, 1 + _rng.NextDouble() * 3);
                    return 0;
                }
                double wv = Toward(body.Pos.X, _wanderX, _speed, TurnSlack);
                if (wv == 0 && _turnLeft <= 0) Enter(PetState.Idle, 1 + _rng.NextDouble() * 3);   // close enough: no walking on the spot
                return wv;

            case PetState.Sit:
                Action = RestingAction();
                if (Action == "sit") TurnToCursor(dt, body);
                if (_stateTime >= _stateDuration) Decide(body, needs, map, world);
                return 0;

            case PetState.Idle:
                return StandAbout(dt, body, needs, map, world);

            default:
                return ThinkSpecial(dt, body, needs, map, world);
        }
    }

    /// <summary>Standing about, looking round; deciding what next when the time is up.</summary>
    protected double StandAbout(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        Action = "idle";
        TurnToCursor(dt, body);
        if (_stateTime >= _stateDuration) Decide(body, needs, map, world);
        return 0;
    }

    /// <summary>
    /// The head turns only so far: a cursor that stays behind it makes it turn round to face it (the visual turns
    /// it through the viewer's side). Straight above or in front it just looks.
    /// </summary>
    protected void TurnToCursor(double dt, CatBody body)
    {
        bool behind = Cursor is { } c && (c - body.Pos).Length < TurnToCursorRange
                      && (c.X - body.Pos.X) * Facing < -TurnToCursorBehind;
        _cursorBehind = behind ? _cursorBehind + dt : 0;
        if (_cursorBehind < TurnToCursorAfter) return;
        Facing = -Facing;
        _cursorBehind = 0;
    }

    // ---------------------------------------------------------------- decisions

    protected void Decide(CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        if (world.TreatPos is { } t && world.TreatLanded
            && !(_treatIgnored is { } ig && (ig - t).Length < 3))
        {
            Goal = Goal.Treat;
            Enter(PetState.ChaseTreat);
            return;
        }
        if (needs.Hunger > 0.65 && world.BowlPlatform != null && _bowlCooldown <= 0)
        {
            Goal = Goal.Bowl;
            Enter(PetState.Travel);
            return;
        }
        if (needs.Energy < 0.25)
        {
            GetSleepy(body, needs);
            return;
        }
        if (needs.Playfulness > 0.75) { Goal = Goal.Zoom; Enter(PetState.Zoomies, 8 + _rng.NextDouble() * 5); return; }
        DecideIdle(body, needs, map, world);
    }

    /// <summary>A path it can walk and jump, or null.</summary>
    protected List<NavStep>? FindPath(SurfaceMap map, Platform from, double fromX, Platform to, double toX)
    {
        var path = Navigator.FindPath(map, from, fromX, to, toX, MaxJumpUp, MaxJumpGap);
        if (path == null) return null;
        var here = from;
        foreach (var step in path)
        {
            if (step.Kind != NavStepKind.Walk && !CanTake(step, here)) return null;
            here = step.Target;
        }
        return path;
    }

    protected bool PickExplore(CatBody body, SurfaceMap map)
    {
        var here = body.Support!;
        // From up high, half of the time head back down; otherwise any other surface.
        bool down = here.Kind != SurfaceKind.Floor && _rng.NextDouble() < 0.5;
        var candidates = map.Platforms.Where(p => p.Id != here.Id && (!down || p.Kind == SurfaceKind.Floor)).ToList();
        while (candidates.Count > 0)
        {
            var p = candidates[_rng.Next(candidates.Count)];
            double x = p.X0 + 30 + _rng.NextDouble() * Math.Max(1, p.X1 - p.X0 - 60);
            if (FindPath(map, here, body.Pos.X, p, x) != null)
            {
                _exploreTarget = p;
                _exploreX = x;
                return true;
            }
            candidates.Remove(p);
        }
        return false;
    }

    // ---------------------------------------------------------------- travelling

    (Platform platform, double x)? Destination(Needs needs, SurfaceMap map, IWorld world, CatBody body)
    {
        switch (Goal)
        {
            case Goal.Bowl when world.BowlPlatform != null:
                // Stand beside the bowl, on the side we come from, with the mouth over it.
                double side = body.Pos.X < world.BowlX ? -1 : 1;
                return (Current(map, world.BowlPlatform) ?? world.BowlPlatform, world.BowlX + side * EatReach);
            case Goal.Perch when world.PerchTop != null:
                return (Current(map, world.PerchTop) ?? world.PerchTop, world.PerchX);
            case Goal.Perch:
                return (body.Support!, body.Pos.X);   // no perch: sleep where we are
            case Goal.Treat when world.TreatPos is { } t && world.TreatLanded:
                var tp = map.SupportAt(t.X, t.Y, 4);
                if (tp == null) return null;
                double tside = body.Pos.X < t.X ? -1 : 1;
                return (tp, MathX.SafeClamp(t.X + tside * EatReach, tp.X0 + 1, tp.X1 - 1));
            case Goal.Explore when _exploreTarget != null:
                return (Current(map, _exploreTarget) ?? _exploreTarget, _exploreX);
            default:
                return null;
        }
    }

    /// <summary>The same surface in a freshly built map (ids change on every rebuild).</summary>
    protected static Platform? Current(SurfaceMap map, Platform p) =>
        map.Platforms.FirstOrDefault(q => q.Kind == p.Kind && q.Owner == p.Owner && q.Y == p.Y && q.X0 <= p.Center && q.X1 > p.Center)
        ?? map.Platforms.FirstOrDefault(q => q.Kind == p.Kind && q.Owner == p.Owner);

    double DoTravel(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        var dest = Destination(needs, map, world, body);
        if (dest == null) { Goal = Goal.None; Enter(PetState.Idle, 1); return 0; }
        var (target, tx) = dest.Value;

        var path = FindPath(map, body.Support!, body.Pos.X, target, tx);
        if (path == null)
        {
            // Unreachable: complain a bit if it was food, otherwise give up.
            if (Goal == Goal.Bowl)
            {
                _bowlFails++;
                NoFood(unreachable: true);
                return 0;
            }
            if (Goal == Goal.Treat) _treatIgnored = world.TreatPos;
            Goal = Goal.None;
            Enter(PetState.Idle, 2);
            return 0;
        }

        _speed = TravelSpeed(needs, world);
        var step = path[0];
        bool final = path.Count == 1;

        if (Math.Abs(body.Pos.X - step.X) > 4)
        {
            Action = GaitFor(_speed);
            OnWalkingToStep();
            double v = Toward(body.Pos.X, step.X, _speed);
            // Slow down on the last few pixels so we do not overshoot at 30 fps.
            double remaining = Math.Abs(body.Pos.X - step.X);
            if (remaining < Math.Abs(v) * dt) v = Math.Sign(v) * remaining / dt;
            return v;
        }

        if (final)
        {
            Arrive(body, world, needs);
            return 0;
        }
        return TakeOff(dt, body, path[1]);
    }

    /// <summary>Walking along a path, not at a takeoff point yet.</summary>
    protected virtual void OnWalkingToStep() { }

    void Arrive(CatBody body, IWorld world, Needs needs)
    {
        switch (Goal)
        {
            case Goal.Bowl:
                _bowlFails = 0;
                Facing = world.BowlX > body.Pos.X ? 1 : -1;
                if (world.BowlFood > 0.02) Enter(PetState.Eat);
                else NoFood(unreachable: false);
                break;
            case Goal.Perch:
                Enter(PetState.Sleep);
                break;
            case Goal.Treat:
                world.ConsumeTreat();
                needs.Eat(0.08);
                needs.Play(0.15);
                Goal = Goal.None;
                Enter(PetState.Sit, 3);
                break;
            default:
                Goal = Goal.None;
                Enter(PetState.Idle, 1 + _rng.NextDouble() * 2);
                break;
        }
    }

    /// <summary>
    /// Run about the surface it is on at up to <paramref name="top"/> px/s, speeding up and braking like an animal:
    /// braking in time to stop on the target, speeding up again after it. Returns the horizontal speed; the target
    /// is reached when <see cref="_zoomTarget"/> is within 8 px (callers pick a new one when it is NaN).
    /// </summary>
    protected double RunTowardsZoomTarget(double dt, CatBody body, double top, double accel, double brake, double creep)
    {
        _stuckFrames = Math.Abs(body.Vel.X) < 1 ? _stuckFrames + 1 : 0;
        if (_stuckFrames > 15)
        {
            // Pinned against a screen edge: pick a new target next frame.
            _zoomTarget = double.NaN;
            _stuckFrames = 0;
            return 0;
        }
        double remaining = Math.Abs(body.Pos.X - _zoomTarget);
        // (down to a creep by the 8 px that count as arrived)
        double want = Math.Min(top, Math.Max(creep, Math.Sqrt(2 * brake * Math.Max(0, remaining - 8))));
        _zoomSpeed = want > _zoomSpeed ? Math.Min(want, _zoomSpeed + accel * dt) : Math.Max(want, _zoomSpeed - brake * dt);
        double v = Toward(body.Pos.X, _zoomTarget, top, TurnSlack);   // (a running turn: stops to turn round)
        if (v == 0) _zoomSpeed = 0;   // turning round
        else v = Math.Sign(v) * _zoomSpeed;
        // Brake on the last few pixels: at speed it would overshoot and turn back, again and again.
        if (remaining < Math.Abs(v) * dt) v = Math.Sign(v) * remaining / dt;
        return v;
    }

    // ---------------------------------------------------------------- helpers

    protected double Toward(double from, double to, double speed, double slack = 0)
    {
        double d = to - from;
        if (double.IsNaN(d) || Math.Abs(d) < 1) return 0;
        int dir = Math.Sign(d);
        // An animal does not turn round for a few pixels behind it: close enough.
        if (dir != Facing && Math.Abs(d) < slack) return 0;
        if (dir != Facing && speed > StrollSpeed + 1) _turnLeft = TurnTime;   // running the other way: turn first
        Facing = dir;
        return _turnLeft > 0 ? 0 : Facing * speed;
    }

    protected void Enter(PetState s, double duration = 0)
    {
        State = s;
        _stateTime = 0;
        _stateDuration = duration;
        if (s != PetState.Zoomies) _zoomTarget = double.NaN;
        _zoomSpeed = 0;
        Action = s switch
        {
            PetState.Sleep => SleepAction(),
            PetState.Zoomies => EnterAction(s) ?? "run",
            PetState.Eat => "eat",
            PetState.Sit => "sit",
            PetState.Petted => PettedAction,
            PetState.Held => "held",
            PetState.Landing => "land",
            PetState.Wander => GaitFor(StrollSpeed),
            PetState.Idle => "idle",
            PetState.Climb => "climb",
            _ => EnterAction(s) ?? Action,
        };
        OnEnter(s);
    }
}
