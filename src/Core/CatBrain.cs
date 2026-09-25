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

public enum CatState { Idle, Wander, Travel, Zoomies, Eat, Sleep, Sit, Meow, ChaseTreat, Petted, Held, Airborne, Landing, Climb, Hunt }

/// <summary>Why the cat is travelling: decides what happens on arrival.</summary>
public enum Goal { None, Bowl, Perch, Treat, Explore, Wander, Zoom }

/// <summary>
/// Behaviour state machine. Each Update picks a behaviour from the needs, moves the body and exposes
/// the logical animation (<see cref="Action"/>) and facing for the visual.
/// </summary>
public sealed class CatBrain
{
    public const double WalkSpeed = 110;
    /// <summary>The happy trot: diagonal legs, bouncy, tail straight up.</summary>
    public const double TrotSpeed = 150;
    /// <summary>The lope: getting somewhere fast without sprinting (starving, on the way to the bowl).</summary>
    public const double LopeSpeed = 190;
    public const double RunSpeed = 360;
    public const double MaxJumpUp = 460;
    public const double MaxJumpGap = 340;
    public const double TravelTimeout = 45;
    /// <summary>Time to stop and turn round before running the other way (the model turns through the viewer side).</summary>
    public const double TurnTime = 0.3;
    /// <summary>A target this close behind her counts as reached rather than worth turning round for.</summary>
    public const double TurnSlack = 24;
    /// <summary>Loading the hind legs before a jump, and each extra "taking aim" after it.</summary>
    public const double CrouchTime = 0.45, AimTime = 0.5;
    /// <summary>How close (px from the body) a moving cursor must come to catch her eye.</summary>
    public const double HuntRange = 260;
    /// <summary>Playfulness above which she crouches and follows the cursor, and above which she pounces.</summary>
    public const double StalkFrom = 0.35, PounceFrom = 0.7;

    readonly Random _rng;

    public CatState State { get; private set; } = CatState.Idle;
    public Goal Goal { get; private set; }
    /// <summary>Logical animation: idle, walk, trot (happy), lope, run (sprint), stalk, wiggle, swat, prejump, aim, jump, fall, land, sit, sleep, eat, meow, purr, held, climb.</summary>
    public string Action { get; private set; } = "idle";
    /// <summary>+1 facing right, -1 facing left.</summary>
    public int Facing { get; private set; } = 1;
    /// <summary>Short symbol shown over the cat: ♥ z ! or null.</summary>
    public string? Emote { get; private set; }
    /// <summary>How far ahead of the body centre the mouth reaches when eating (px): where to stop before food.</summary>
    public double EatReach { get; init; } = 45;
    /// <summary>Seconds of good mood left (after petting, when called): it trots about instead of walking.</summary>
    public double Happy { get; private set; }
    /// <summary>The mouse cursor on the desktop (screen px), set by the game every frame; null if unknown.</summary>
    public Vec2? Cursor { get; set; }
    /// <summary>Where the jump being prepared or flown will land (screen px), for the eyes; null otherwise.</summary>
    public Vec2? JumpTarget { get; private set; }

    double _stateTime;        // time spent in the current state
    double _stateDuration;    // planned length for timed states
    double _petting;          // recent petting, decays
    double _prejump;          // crouch before a jump
    double _prejumpPlan = -1; // how long this take-off lasts: 0 straight away, a crouch, or taking aim
    double _speed = WalkSpeed;
    double _zoomTarget = double.NaN;
    Platform? _exploreTarget;
    double _exploreX;
    double _wanderX;
    int _stuckFrames;
    double _zoomLeft;
    Goal _afterLanding;
    Vec2? _treatIgnored;      // a treat we could not reach: leave it alone
    int _bowlFails;           // failed trips to the bowl in a row
    double _bowlCooldown;     // seconds before trying the bowl again after giving up
    double _turnLeft;         // stopped, turning round
    Vec2? _lastCursor;
    double _cursorStill = 99; // seconds since the cursor last moved
    double _huntCooldown;     // leave the cursor alone until this runs out
    int _huntLevel;           // 0 watch, 1 stalk, 2 stalk + wiggle + pounce
    double _swatLeft;         // a swat in progress
    int _swats;
    double _wiggleAt;         // when (state time) the rump wiggle starts
    double _pounceAt;         // when (state time) she leaps

    public CatBrain(Random rng) => _rng = rng;

    // ---------------------------------------------------------------- input from the game

    public void OnGrab(CatBody body)
    {
        body.Grab();
        Enter(CatState.Held);
        Goal = Goal.None;
    }

    public void OnRelease(CatBody body, Vec2 throwVel, SurfaceMap? map = null)
    {
        body.Release(throwVel, map);
        Enter(CatState.Airborne);
        _afterLanding = Goal.None;
    }

    /// <summary>Called every frame the cursor strokes the cat.</summary>
    public void OnPetting(double dt)
    {
        _petting = Math.Min(_petting + dt * 2, 3);
        Cheer(25);
    }

    /// <summary>Put the cat in a good mood for a while.</summary>
    public void Cheer(double seconds) => Happy = Math.Max(Happy, seconds);

    /// <summary>Something happened (a treat appeared, needs changed): a resting cat reconsiders right away.</summary>
    public void Notice()
    {
        if (State is CatState.Idle or CatState.Sit or CatState.Wander or CatState.Petted or CatState.Meow)
            Enter(CatState.Idle, 0.3);
    }

    /// <summary>Keep the cat sitting still for a while (self test, debugging).</summary>
    public void SitFor(double seconds)
    {
        Goal = Goal.None;
        Enter(CatState.Sit, seconds);
    }

    /// <summary>Send the cat to a given spot (self test, debugging).</summary>
    public void ExploreTo(Platform p, double x)
    {
        _exploreTarget = p;
        _exploreX = x;
        Goal = Goal.Explore;
        Enter(CatState.Travel);
    }

    /// <summary>Bring the cat back onto a floor (tray "call the cat").</summary>
    public void Summon(CatBody body, Platform floor, double x)
    {
        body.PlaceOn(floor, x);
        Goal = Goal.None;
        Cheer(15);
        Enter(CatState.Idle, 2);
    }

    // ---------------------------------------------------------------- main loop

    public void Update(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        _stateTime += dt;
        _petting = Math.Max(0, _petting - dt);
        _bowlCooldown = Math.Max(0, _bowlCooldown - dt);
        _turnLeft = Math.Max(0, _turnLeft - dt);
        Happy = Math.Max(0, Happy - dt);
        _huntCooldown = Math.Max(0, _huntCooldown - dt);
        _swatLeft = Math.Max(0, _swatLeft - dt);
        bool moved = Cursor is { } cur && _lastCursor is { } last && (cur - last).Length > 0.5;
        _cursorStill = moved ? 0 : _cursorStill + dt;
        _lastCursor = Cursor;
        needs.Tick(dt, State == CatState.Sleep);
        Emote = null;

        if (State == CatState.Held)
        {
            Action = "held";
            if (_petting > 0.5) { needs.Pet(dt); Emote = "♥"; }
            return;
        }

        double walk = 0;
        if (body.Mode == BodyMode.Climbing)
        {
            if (State != CatState.Climb) Enter(CatState.Climb);
            Action = "climb";
            Facing = -body.Wall!.Side;   // facing the window side it hangs on
        }
        else if (body.Mode == BodyMode.Airborne)
        {
            if (State != CatState.Airborne)
            {
                _afterLanding = State is CatState.Travel or CatState.Zoomies or CatState.ChaseTreat ? Goal
                              : State == CatState.Climb ? _afterLanding : Goal.None;
                if (State == CatState.Zoomies) _zoomLeft = Math.Max(0, _stateDuration - _stateTime);
                Enter(CatState.Airborne);
            }
            Action = Flight.Stretched(body.Vel) ? "jump" : "fall";
            if (Math.Abs(body.Vel.X) > 20) Facing = Math.Sign(body.Vel.X);
        }
        else
        {
            walk = Think(dt, body, needs, map, world);
            if (_turnLeft > 0 && Action is "run" or "lope" or "trot") Action = "idle";   // stopped for a moment, turning
        }

        body.Step(dt, map, walk);
        if (body.Mode != BodyMode.Airborne && Action != "prejump") JumpTarget = null;

        if (body.JustLanded)
        {
            if (State == CatState.Climb)
                ResumeAfterLanding();
            else if (body.LandingSpeed > 1100)
                Enter(CatState.Landing, 0.45);
            else if (body.LandingSpeed > 450)
                Enter(CatState.Landing, 0.3);   // an ordinary jump: a quick absorb on the front legs
            else
                ResumeAfterLanding();
        }
        if (body.HitWall && State is CatState.Wander or CatState.Zoomies)
        {
            Facing = -Facing;
            if (State == CatState.Zoomies) _turnLeft = TurnTime;
            _zoomTarget = double.NaN;
            _wanderX = body.Pos.X + Facing * 150;
        }
    }

    void ResumeAfterLanding()
    {
        if (_afterLanding is Goal.Bowl or Goal.Perch or Goal.Treat or Goal.Explore)
        {
            Goal = _afterLanding;
            Enter(Goal == Goal.Treat ? CatState.ChaseTreat : CatState.Travel);
        }
        else if (_afterLanding == Goal.Zoom)
            Enter(CatState.Zoomies, _zoomLeft);
        else
            Enter(CatState.Idle, 1 + _rng.NextDouble());
        _afterLanding = Goal.None;
    }

    /// <summary>Grounded behaviour. Returns the horizontal walking speed.</summary>
    double Think(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        bool calm = State is CatState.Idle or CatState.Sit or CatState.Wander or CatState.Meow or CatState.Petted
                    || State == CatState.Travel && Goal == Goal.Explore;
        if (_petting > 0.6 && calm && State != CatState.Petted)
            Enter(CatState.Petted);
        else if (calm && State is not (CatState.Petted or CatState.Meow) && _petting <= 0 && _huntCooldown <= 0
                 && _cursorStill < 0.5 && CursorDistance(body) is > 45 and < HuntRange)
            StartHunt(needs);

        switch (State)
        {
            case CatState.Landing:
                Action = "land";
                if (_stateTime >= _stateDuration) ResumeAfterLanding();
                return 0;

            case CatState.Airborne:
                ResumeAfterLanding();
                return 0;

            case CatState.Petted:
                Action = "purr";
                Emote = "♥";
                if (_petting > 0) needs.Pet(dt);
                if (_petting <= 0 && _stateTime > 1.5) Enter(CatState.Sit, 4 + _rng.NextDouble() * 6);
                return 0;

            case CatState.Sleep:
                Action = "sleep";
                Emote = "z";
                if (_petting > 0) needs.Pet(dt * 0.5);
                if (needs.Energy >= 0.98) { Goal = Goal.None; Enter(CatState.Idle, 2); }
                return 0;

            case CatState.Eat:
                Action = "eat";
                if (world.BowlFood <= 0 || needs.Hunger <= 0.02) { Goal = Goal.None; Enter(CatState.Sit, 5); return 0; }
                world.EatFromBowl(0.06 * dt);
                needs.Eat(0.12 * dt);
                return 0;

            case CatState.Meow:
                Action = (_stateTime % 5) < 1.2 ? "meow" : "sit";
                if (Action == "meow") Emote = "!";
                // Exhausted, or the bowl cannot be reached: stop insisting for a while.
                if (needs.Energy < 0.15 || _bowlFails >= 3)
                {
                    _bowlCooldown = 90;
                    _bowlFails = 0;
                    Goal = Goal.None;
                    Enter(CatState.Idle, 1);
                    return 0;
                }
                if (_stateTime > 5 && world.BowlFood > 0.05) { Goal = Goal.Bowl; Enter(CatState.Travel); }
                if (needs.Hunger < 0.5) Enter(CatState.Idle, 1);
                return 0;

            case CatState.Zoomies:
                _speed = RunSpeed;
                if (_stateTime >= _stateDuration)
                {
                    needs.Play(0.8);
                    Enter(CatState.Sit, 5);
                    return 0;
                }
                return DoZoomies(dt, body, map);

            case CatState.Travel:
            case CatState.ChaseTreat:
                // Watchdog: a trip that never ends means something unforeseen; give up and look around.
                if (_stateTime > TravelTimeout) { Goal = Goal.None; Enter(CatState.Idle, 2); return 0; }
                return DoTravel(dt, body, needs, map, world);

            case CatState.Wander:
                _speed = Happy > 0 ? TrotSpeed : WalkSpeed;
                Action = Gait(_speed);
                if (Math.Abs(body.Pos.X - _wanderX) < 4 || _stateTime > _stateDuration || !body.Support!.SpansX(_wanderX))
                {
                    Enter(CatState.Idle, 1 + _rng.NextDouble() * 3);
                    return 0;
                }
                double wv = Toward(body.Pos.X, _wanderX, _speed, TurnSlack);
                if (wv == 0 && _turnLeft <= 0) Enter(CatState.Idle, 1 + _rng.NextDouble() * 3);   // close enough: no walking on the spot
                return wv;

            case CatState.Hunt:
                return DoHunt(dt, body, needs);

            case CatState.Sit:
                Action = "sit";
                if (_stateTime >= _stateDuration) Decide(body, needs, map, world);
                return 0;

            default: // Idle
                Action = "idle";
                if (_stateTime >= _stateDuration) Decide(body, needs, map, world);
                return 0;
        }
    }

    // ---------------------------------------------------------------- decisions

    void Decide(CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        if (world.TreatPos is { } t && world.TreatLanded
            && !(_treatIgnored is { } ig && (ig - t).Length < 3))
        {
            Goal = Goal.Treat;
            Enter(CatState.ChaseTreat);
            return;
        }
        if (needs.Hunger > 0.65 && world.BowlPlatform != null && _bowlCooldown <= 0)
        {
            Goal = Goal.Bowl;
            Enter(CatState.Travel);
            return;
        }
        if (needs.Energy < 0.25)
        {
            Goal = Goal.Perch;
            Enter(CatState.Travel);
            return;
        }
        if (needs.Playfulness > 0.75) { Goal = Goal.Zoom; Enter(CatState.Zoomies, 8 + _rng.NextDouble() * 5); return; }

        double r = _rng.NextDouble();
        if (Happy > 0) r *= 0.6;   // in a good mood: more strolling about, less sitting
        if (r < 0.35)
        {
            var s = body.Support!;
            _wanderX = MathX.SafeClamp(body.Pos.X + (_rng.NextDouble() * 2 - 1) * 400, s.X0 + 30, s.X1 - 30);
            Enter(CatState.Wander, 12);
        }
        else if (r < 0.6 && PickExplore(body, map))
        {
            Goal = Goal.Explore;
            Enter(CatState.Travel);
        }
        else if (r < 0.85)
            Enter(CatState.Sit, 8 + _rng.NextDouble() * 20);
        else if (needs.Energy < 0.5 && r < 0.93)
            Enter(CatState.Sleep);  // cat nap on the spot
        else
            Enter(CatState.Idle, 3 + _rng.NextDouble() * 5);
    }

    bool PickExplore(CatBody body, SurfaceMap map)
    {
        var here = body.Support!;
        // From up high, half of the time head back down; otherwise any other surface.
        bool down = here.Kind != SurfaceKind.Floor && _rng.NextDouble() < 0.5;
        var candidates = map.Platforms.Where(p => p.Id != here.Id && (!down || p.Kind == SurfaceKind.Floor)).ToList();
        while (candidates.Count > 0)
        {
            var p = candidates[_rng.Next(candidates.Count)];
            double x = p.X0 + 30 + _rng.NextDouble() * Math.Max(1, p.X1 - p.X0 - 60);
            if (Navigator.FindPath(map, here, body.Pos.X, p, x, MaxJumpUp, MaxJumpGap) != null)
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
    static Platform? Current(SurfaceMap map, Platform p) =>
        map.Platforms.FirstOrDefault(q => q.Kind == p.Kind && q.Owner == p.Owner && q.Y == p.Y && q.X0 <= p.Center && q.X1 > p.Center)
        ?? map.Platforms.FirstOrDefault(q => q.Kind == p.Kind && q.Owner == p.Owner);

    double DoTravel(double dt, CatBody body, Needs needs, SurfaceMap map, IWorld world)
    {
        var dest = Destination(needs, map, world, body);
        if (dest == null) { Goal = Goal.None; Enter(CatState.Idle, 1); return 0; }
        var (target, tx) = dest.Value;

        var path = Navigator.FindPath(map, body.Support!, body.Pos.X, target, tx, MaxJumpUp, MaxJumpGap);
        if (path == null)
        {
            // Unreachable: complain a bit if it was food, otherwise give up.
            if (Goal == Goal.Bowl)
            {
                _bowlFails++;
                Enter(CatState.Meow);
                return 0;
            }
            if (Goal == Goal.Treat) _treatIgnored = world.TreatPos;
            Goal = Goal.None;
            Enter(CatState.Idle, 2);
            return 0;
        }

        _speed = State == CatState.ChaseTreat ? RunSpeed
               : Goal == Goal.Bowl && needs.Hunger > 0.8 ? LopeSpeed
               : Goal == Goal.Bowl && world.BowlFood > 0.02 || Happy > 0 ? TrotSpeed   // pleased: food waiting, or cheered up
               : WalkSpeed;
        var step = path[0];
        bool final = path.Count == 1;

        if (Math.Abs(body.Pos.X - step.X) > 4)
        {
            Action = Gait(_speed);
            _prejump = 0;
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

        // At the takeoff point: crouch briefly, then jump (or grab the window side).
        var hop = path[1];
        if (hop.Kind == NavStepKind.Climb)
        {
            Facing = -hop.Via!.Side;
            _afterLanding = Goal;
            body.StartClimb(hop.Via, hop.Target, hop.LandX);
            Enter(CatState.Climb);
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

    void Arrive(CatBody body, IWorld world, Needs needs)
    {
        switch (Goal)
        {
            case Goal.Bowl:
                _bowlFails = 0;
                Facing = world.BowlX > body.Pos.X ? 1 : -1;
                Enter(world.BowlFood > 0.02 ? CatState.Eat : CatState.Meow);
                break;
            case Goal.Perch:
                Enter(CatState.Sleep);
                break;
            case Goal.Treat:
                world.ConsumeTreat();
                needs.Eat(0.08);
                needs.Play(0.15);
                Goal = Goal.None;
                Enter(CatState.Sit, 3);
                break;
            default:
                Goal = Goal.None;
                Enter(CatState.Idle, 1 + _rng.NextDouble() * 2);
                break;
        }
    }

    double DoZoomies(double dt, CatBody body, SurfaceMap map)
    {
        Action = "run";
        var s = body.Support!;
        if (double.IsNaN(_zoomTarget) || Math.Abs(body.Pos.X - _zoomTarget) < 8 || !s.SpansX(_zoomTarget))
        {
            // Sometimes leap onto another surface in the middle of a sprint.
            if (_rng.NextDouble() < 0.3 && PickExplore(body, map))
            {
                var path = Navigator.FindPath(map, s, body.Pos.X, _exploreTarget!, _exploreX, MaxJumpUp, MaxJumpGap);
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
        _stuckFrames = Math.Abs(body.Vel.X) < 1 ? _stuckFrames + 1 : 0;
        if (_stuckFrames > 15)
        {
            // Pinned against a screen edge: pick a new target next frame.
            _zoomTarget = double.NaN;
            _stuckFrames = 0;
            return 0;
        }
        double v = Toward(body.Pos.X, _zoomTarget, RunSpeed, TurnSlack);
        // Brake on the last few pixels: at 12 px a frame she would overshoot and turn back, again and again.
        double remaining = Math.Abs(body.Pos.X - _zoomTarget);
        if (remaining < Math.Abs(v) * dt) v = Math.Sign(v) * remaining / dt;
        return v;
    }

    // ---------------------------------------------------------------- hunting the cursor

    /// <summary>Distance from the cursor to the middle of the body (it moves the head, not the feet).</summary>
    /// <summary>Forget the last hunt so the next moving cursor is chased at once (self test).</summary>
    public void ForgetHunt() => _huntCooldown = 0;

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
        Enter(CatState.Hunt);
    }

    void EndHunt(Needs needs, double played)
    {
        needs.Play(played);
        _huntCooldown = 25 + _rng.NextDouble() * 35;
        Enter(CatState.Sit, 3 + _rng.NextDouble() * 3);
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

    // ---------------------------------------------------------------- helpers

    /// <summary>The animation for a ground speed: walk, happy trot, lope, sprint.</summary>
    static string Gait(double speed) =>
        speed >= RunSpeed ? "run" : speed >= LopeSpeed ? "lope" : speed > WalkSpeed + 1 ? "trot" : "walk";

    double Toward(double from, double to, double speed, double slack = 0)
    {
        double d = to - from;
        if (double.IsNaN(d) || Math.Abs(d) < 1) return 0;
        int dir = Math.Sign(d);
        // A cat does not turn round for a few pixels behind it: close enough.
        if (dir != Facing && Math.Abs(d) < slack) return 0;
        if (dir != Facing && speed > WalkSpeed + 1) _turnLeft = TurnTime;   // running the other way: turn first
        Facing = dir;
        return _turnLeft > 0 ? 0 : Facing * speed;
    }

    void Enter(CatState s, double duration = 0)
    {
        State = s;
        _stateTime = 0;
        _stateDuration = duration;
        if (s != CatState.Zoomies) _zoomTarget = double.NaN;
        _prejump = 0;
        _prejumpPlan = -1;
        Action = s switch
        {
            CatState.Sleep => "sleep",
            CatState.Zoomies => "run",
            CatState.Eat => "eat",
            CatState.Meow => "meow",
            CatState.Sit => "sit",
            CatState.Petted => "purr",
            CatState.Held => "held",
            CatState.Landing => "land",
            CatState.Wander => "walk",
            CatState.Idle => "idle",
            CatState.Climb => "climb",
            CatState.Hunt => "idle",
            _ => Action,
        };
    }
}
