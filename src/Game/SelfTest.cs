using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZairaPet.Core;

namespace ZairaPet.Game;

/// <summary>
/// `-- --selftest`: runs a scripted tour (treat, hunger, zoomies, sleep, being held), logs the state every
/// second and saves screenshots cropped around the cat into the user folder, then quits.
/// </summary>
public sealed class SelfTest
{
    readonly Main _main;
    readonly List<(double at, string what, Action act)> _script;
    readonly Queue<(string what, Action act)> _frames = new();
    readonly string _mode;

    double _t;
    double _clock;            // script time: stands still while frame steps or a wait are pending
    (Func<bool> done, double timeout, string what, double since)? _wait;
    double _clickY;
    double _logTimer;
    int _next;
    int _shot;
    string _lastState = "";
    double _eatTime;
    bool _eatChecked;
    bool _shotAim, _shotLeap;
    Vec2? _prey;              // the pretend cursor, jiggling like a hand on the mouse

    public SelfTest(Main main, string mode)
    {
        _main = main;
        _mode = mode;
        // The real mouse belongs to whoever is at the PC: tests use a pretend cursor (none unless a test sets it).
        _main.UseCursorOverride = true;
        _script = mode switch
        {
            "windows" => WindowScript(), "mouse" => MouseScript(), "input" => InputScript(), "gaze" => GazeScript(), "hunt" => HuntScript(), _ => TourScript(),
        };
        // Godot merges consecutive motion events without looking at the device, so a real mouse move could
        // lend its position to an injected one: every injected event goes through on its own, right away.
        if (mode == "input") Input.UseAccumulatedInput = false;
        Log.Info($"selftest: start ({mode})");
    }

    /// <summary>
    /// Pairs with tools/test-mouse.ps1, which reads targets.txt and drives the real cursor:
    /// grab and spin the cat, stroke it, click empty desktop, double-click the bowl.
    /// </summary>
    List<(double, string, Action)> MouseScript()
    {
        var list = new List<(double, string, Action)>
        {
            (2.0, "sit still", () => { _main.Brain.SitFor(120); _main.World.Bowl.Food = 0; }),
            (40.0, "quit", () => _main.Quit()),
        };
        for (double t = 2.5; t < 40; t += 0.5) list.Add((t, "", WriteTargets));
        return list.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>
    /// Same gestures as test-mouse.ps1 but injected into Godot's input queue: checks the game's handling
    /// (grab, spin, throw, stroke, double-click) without the OS. Works on a locked session.
    /// The cat starts from a fixed spot, the real mouse is ignored (see <see cref="IgnoresMouse"/>) and the
    /// drag runs one step per frame: the hand speed is measured per event and fades per frame, so the throw
    /// must not depend on how many frames fit between two wall-clock steps.
    /// </summary>
    List<(double, string, Action)> InputScript()
    {
        Vector2 cat = default;
        var list = new List<(double, string, Action)>
        {
            (1.0, "check clips", () => Expect(_main.Visual.EveryClipDrivesEveryBone(),
                "ogni animazione muove tutte le ossa (niente zampe congelate dalla clip precedente)")),
            (2.0, "sit still on the primary floor", () =>
            {
                _main.Summon();
                _main.Brain.SitFor(120);
                _main.World.Bowl.Food = 0;
            }),
            (3.0, "drag and throw", () =>
            {
                Frame("press on cat", () =>
                {
                    cat = CatPoint(0.5f);
                    Button(cat, true);
                });
                for (int k = 1; k <= 15; k++)
                {
                    int s = k;
                    Frame("", () => Motion(cat + new Vector2(s * 6, -s * 12), new Vector2(6, -12)));
                    if (s != 8) continue;
                    // Halfway through the drag: the cat is in hand and the window takes the whole mouse.
                    Frame("check held", () => Expect(_main.Brain.State == CatState.Held, "gatto in braccio"));
                    Frame("check region while dragging", () =>
                        Expect(InRegion(new Vec2(_main.OverlayWindow.Origin.X + 20, _main.OverlayWindow.Origin.Y + 20)),
                               "durante il trascinamento la finestra prende tutto il mouse"));
                    Frame("screenshot", Shot);
                }
                Frame("release", () => Button(cat + new Vector2(90, -180), false));
                Frame("check thrown", () => Expect(_main.Body.Mode == BodyMode.Airborne && _main.Body.Vel.X > 0,
                    $"gatto lanciato (vel {_main.Body.Vel.X:0},{_main.Body.Vel.Y:0})"));
            }),
            // A plain click (no drag) must leave the cat where it stood.
            (5.5, "click without moving", () =>
            {
                Frame("press", () =>
                {
                    _main.Summon();
                    _main.Brain.SitFor(60);
                    _clickY = _main.Body.Pos.Y;
                    Button(CatPoint(0.5f), true);
                });
                Frame("release", () => Button(CatPoint(0.5f), false));
            }),
            (6.8, "check plain click", () =>
                Expect(_main.Body.Mode == BodyMode.Grounded && Math.Abs(_main.Body.Pos.Y - _clickY) < 1,
                       $"clic senza trascinare: resta in piedi dov'era (y {_clickY:0} → {_main.Body.Pos.Y:0})")),
            (7.0, "sit again", () => _main.Brain.SitFor(120)),
            (7.2, "check region", CheckRegion),
        };
        // Stroking stays on the clock: petting fades per second, so it is a hand moving at a steady 30 Hz.
        for (int i = 0; i < 60; i++)
        {
            int k = i;
            list.Add((7.5 + k * 0.033, "", () =>
            {
                float dx = (k % 10 < 5 ? 1 : -1) * 12;
                Motion(CatPoint(0.4f) + new Vector2(dx * (k % 5) - 24, 0), new Vector2(dx, 0));
            }));
        }
        list.Add((9.6, "check purring", () => Expect(_main.Brain.State == CatState.Petted, "fusa dopo le carezze")));
        list.Add((9.7, "screenshot", Shot));
        list.Add((11.0, "double-click bowl", () =>
        {
            var bowl = _main.OverlayWindow.ToLocal(_main.World.Bowl.Body.Pos) - new Vector2(0, 10);
            Log.Info($"selftest bowl body={_main.World.Bowl.Body.Pos} rect={_main.World.Bowl.ScreenRect} click={bowl} cat={_main.Body.Pos} origin={_main.OverlayWindow.Origin}");
            Frame("", () => Button(bowl, true, doubleClick: true));
            Frame("", () => Button(bowl, false));
        }));
        list.Add((11.2, "check bowl", () => Expect(_main.World.Bowl.Food > 0.99, "ciotola riempita col doppio clic")));
        list.Add((12.0, "quit", () => _main.Quit()));
        return list.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>
    /// `-- --selftest-gaze`: the sitting cat looks at the viewer, then at a spot up ahead, then down at the floor
    /// ahead; each time the real head direction (from the posed skeleton) must point there.
    /// </summary>
    List<(double, string, Action)> GazeScript()
    {
        Vec2 target = default;
        void LookAt(GazeKind kind, Vec2 offset)
        {
            target = _main.HeadScreenPos + new Vec2(_main.Brain.Facing * offset.X, offset.Y);
            _main.GazeState.Force(kind, target, 3);
        }
        return new List<(double, string, Action)>
        {
            (1.0, "sit", () => { _main.Summon(); _main.Brain.SitFor(60); }),
            (1.5, "screenshot", Shot),
            (2.0, "look at the viewer", () => LookAt(GazeKind.Viewer, new Vec2(0, 0))),
            (4.0, "check viewer", () => CheckHead(new Vector3(0, 0, 1), 40, "guarda verso chi sta davanti allo schermo")),
            (4.1, "screenshot", Shot),
            (5.0, "look up ahead", () => LookAt(GazeKind.Point, new Vec2(220, -260))),
            (7.0, "check up", () => CheckHead(null, 30, "guarda un punto in alto davanti", target)),
            (7.1, "screenshot", Shot),
            (8.0, "look down ahead", () => LookAt(GazeKind.Point, new Vec2(260, 160))),
            (10.0, "check down", () => CheckHead(null, 30, "guarda un punto in basso davanti", target)),
            (10.1, "screenshot", Shot),
            (11.0, "quit", () => _main.Quit()),
        };
    }

    /// <summary>
    /// `-- --selftest-hunt`: a pretend cursor near the sitting cat at three levels of playfulness: she watches,
    /// crouches and follows it, then wiggles and pounces on it; within paw reach she swats it.
    /// </summary>
    List<(double, string, Action)> HuntScript()
    {
        Vec2 at(double ahead, double up) => _main.Body.Pos + new Vec2(_main.Brain.Facing * ahead, -up);
        void Round(double play)
        {
            _prey = null;
            _main.CursorOverride = null;
            _main.Summon();
            _main.Brain.SitFor(60);
            _main.NeedsState.Playfulness = play;
        }
        void Show(Vec2 p)
        {
            _main.Brain.ForgetHunt();   // each round starts fresh, without the pause after the previous hunt
            _prey = p;
        }
        return new List<(double, string, Action)>
        {
            (1.0, "a little playful", () => Round(0.1)),
            (1.5, "cursor near", () => Show(at(170, 60))),
            (3.5, "check watch", () => Expect(_main.Brain.State == CatState.Hunt && _main.Brain.Action == "idle", "poca voglia: si ferma e guarda il cursore")),
            (3.6, "screenshot", Shot),
            (4.0, "somewhat playful", () => Round(0.5)),
            (4.5, "cursor near", () => Show(at(200, 50))),
            (6.5, "check stalk", () => Expect(_main.Brain.Action == "stalk", "media voglia: acquattata, lo segue con lo sguardo")),
            (6.6, "screenshot", Shot),
            (7.0, "cursor in paw reach", () => _prey = at(75, 30)),
            (7.6, "check swat", () => Expect(_main.Brain.Action == "swat", "a portata di zampa: zampata")),
            (7.65, "screenshot", Shot),
            (9.0, "very playful", () => Round(0.9)),
            (9.5, "cursor ahead", () => Show(at(230, 40))),
            (11.3, "check wiggle", () => Expect(_main.Brain.Action == "wiggle", "tanta voglia: dondola il sedere")),
            (11.35, "screenshot", Shot),
            (13.5, "check pounce", () =>
            {
                var landed = _main.Body.Mode == BodyMode.Grounded;
                double head = _main.Body.Pos.X + _main.Brain.Facing * _main.Brain.EatReach;
                Expect(landed && Math.Abs(head - _prey!.Value.X) < 40,
                       $"balza sul cursore (testa a {head:0}, cursore a {_prey.Value.X:0})");
            }),
            (13.6, "screenshot", Shot),
            (14.5, "quit", () => _main.Quit()),
        };
    }

    /// <summary>Angle between where the head bone points and a direction (or a screen point) must be small.</summary>
    void CheckHead(Vector3? dir, double maxDeg, string what, Vec2? point = null)
    {
        if (_main.Visual.GazeHead is not { } h) { Expect(false, what + " (la testa non si gira)"); return; }
        var head = h.dir;
        var want = dir ?? (_main.OverlayWindow.ToWorld(point!.Value, 40) - h.pos).Normalized();
        double deg = Mathf.RadToDeg(head.AngleTo(want));
        Expect(deg <= maxDeg, $"{what} (scarto {deg:0}°)");
    }

    /// <summary>A point on the cat, <paramref name="up"/> of its height above the feet, in window coordinates.</summary>
    Vector2 CatPoint(float up) => _main.OverlayWindow.ToLocal(_main.Body.Pos) - new Vector2(0, _main.Visual.SizePx.Y * up);

    /// <summary>
    /// A script step that holds the script until the cat reaches <paramref name="state"/>: the steps after it
    /// run that much later. Not reaching it within <paramref name="timeout"/> seconds fails the check.
    /// </summary>
    (double, string, Action) Until(double at, CatState state, double timeout, string what) =>
        (at, $"wait for {state}", () => _wait = (() => _main.Brain.State == state, timeout, what, _t));

    /// <summary>Queue a step for its own frame: steps run one per frame, in order, before the timed script goes on.</summary>
    void Frame(string what, Action act) => _frames.Enqueue((what, act));

    /// <summary>Marks injected events so the input test can tell them from the real mouse.</summary>
    public const int InjectedDevice = 7;

    /// <summary>
    /// The input test drives the game alone: real mouse events (someone using the PC meanwhile) would drag
    /// or throw the cat, since the window takes the whole mouse while dragging.
    /// </summary>
    public bool IgnoresMouse(InputEvent e) => _mode == "input" && e is InputEventMouse && e.Device != InjectedDevice;

    static void Button(Vector2 at, bool pressed, bool doubleClick = false) =>
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Device = InjectedDevice,
            ButtonIndex = MouseButton.Left, Pressed = pressed, DoubleClick = doubleClick, Position = at, GlobalPosition = at,
        });

    static void Motion(Vector2 at, Vector2 rel) =>
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Device = InjectedDevice, Position = at, GlobalPosition = at, Relative = rel,
        });

    bool InRegion(Vec2 p) => ZairaPet.Windows.Win32.RegionContains(_main.OverlayWindow.Handle, (int)p.X, (int)p.Y);

    /// <summary>The applied window region covers every object and leaves the rest of the desktop to other windows.</summary>
    void CheckRegion()
    {
        var cat = _main.Body.Pos - new Vec2(0, _main.Visual.SizePx.Y * 0.5);
        var bowl = _main.World.Bowl.Body.Pos - new Vec2(0, 10);
        var perch = _main.World.Perch.Body.Pos - new Vec2(0, 100);
        var perchTop = _main.World.Perch.Body.Pos - new Vec2(0, Perch.Height + 5);
        Expect(InRegion(cat), "gatto dentro la regione (visibile e cliccabile)");
        Expect(InRegion(bowl), "ciotola dentro la regione");
        Expect(InRegion(perch) && InRegion(perchTop), "trespolo dentro la regione");
        var o = _main.OverlayWindow;
        Expect(!InRegion(new Vec2(o.Origin.X + 20, o.Origin.Y + 20)), "angolo vuoto fuori dalla regione (clic passano sotto)");
        var far = _main.World.Bowl.Body.Pos.X < o.Origin.X + o.Size.X / 2 ? o.Origin.X + o.Size.X - 20 : o.Origin.X + 20;
        Expect(!InRegion(new Vec2(far, _main.Body.Pos.Y - 30)), "pavimento lontano dagli oggetti fuori dalla regione");
    }

    static void Expect(bool ok, string what) => Log.Info($"selftest CHECK {(ok ? "OK  " : "FAIL")} {what}");

    void WriteTargets()
    {
        var b = _main.Body.Pos;
        var bowl = _main.World.Bowl.Body.Pos;
        string text = FormattableString.Invariant(
            $"{b.X:0} {b.Y - _main.Visual.SizePx.Y * 0.5:0} {bowl.X:0} {bowl.Y - 10:0} {_main.Brain.State}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(OS.GetUserDataDir(), "targets.txt"), text);
    }

    /// <summary>
    /// Needs tools/test-window.ps1 running: a topmost window at 700,1000 that moves at ~18 s and closes at ~26 s.
    /// </summary>
    List<(double, string, Action)> WindowScript() => new()
    {
        (2.0, "platforms", LogPlatforms),
        (3.0, "go to window", () =>
        {
            var top = _main.Map.Platforms.FirstOrDefault(p => p.Kind == SurfaceKind.WindowTop);
            if (top == null) { Log.Info("selftest: NESSUNA finestra calpestabile"); return; }
            _main.NeedsState.Playfulness = 0;
            _main.Brain.ExploreTo(top, top.Center);
        }),
        (14.0, "screenshot", Shot),
        (15.0, "stay", () => _main.NeedsState.Energy = 0.9),
        (21.0, "screenshot", Shot),
        (22.0, "platforms", LogPlatforms),
        (28.5, "screenshot", Shot),
        (31.0, "screenshot", Shot),
        (32.0, "quit", () => _main.Quit()),
    };

    List<(double, string, Action)> TourScript()
    {
        return new()
        {
            (1.5, "screenshot", Shot),
            (2.0, "platforms", LogPlatforms),
            (3.0, "treat", () => _main.ThrowTreat()),
            (4.0, "screenshot", Shot),
            (8.0, "screenshot", Shot),
            (14.0, "hungry", () => { _main.NeedsState.Hunger = 0.9; _main.Brain.Notice(); }),
            (18.0, "screenshot", Shot),
            (26.0, "screenshot", Shot),
            (30.0, "restless", () => { _main.NeedsState.Hunger = 0.1; _main.NeedsState.Playfulness = 1; _main.Brain.Notice(); }),
            // She finishes eating and sits a moment before running off.
            Until(30.01, CatState.Zoomies, 20, "corre all'impazzata quando ha voglia di giocare"),
            (32.0, "screenshot", Shot),
            (34.0, "screenshot", Shot),
            (46.0, "tired", () => { _main.NeedsState.Playfulness = 0; _main.NeedsState.Energy = 0.1; _main.Brain.Notice(); }),
            // She walks to the perch, possibly across the screen, and falls asleep on it.
            Until(46.01, CatState.Sleep, 45, "stanca, va a dormire sul trespolo"),
            (48.0, "screenshot", Shot),
            (52.0, "grab", Grab),
            (53.0, "screenshot", Shot),
            (54.0, "release", () => _main.Brain.OnRelease(_main.Body, new Vec2(300, -600), _main.Map)),
            (54.3, "screenshot", Shot),
            (58.0, "screenshot", Shot),
            (60.0, "quit", () => _main.Quit()),
        };
    }

    public void Tick(double dt)
    {
        _t += dt;
        _main.CursorOverride = _prey is { } p ? p + new Vec2(5 * Math.Sin(_t * 9), 3 * Math.Cos(_t * 7)) : null;
        if (_frames.TryDequeue(out var step))
        {
            if (step.what != "") Log.Info($"selftest {_t:0.00}s: {step.what}");
            step.act();
        }
        else if (_wait is { } w)
        {
            bool done = w.done();
            if (done || _t - w.since > w.timeout)
            {
                _wait = null;
                Expect(done, $"{w.what} (dopo {_t - w.since:0.0}s)");
            }
        }
        else _clock += dt;
        while (_frames.Count == 0 && _wait == null && _next < _script.Count && _script[_next].at <= _clock)
        {
            var (_, what, act) = _script[_next++];
            if (what != "") Log.Info($"selftest {_t:0.0}s: {what}");
            act();
        }

        // Whenever the cat eats: the mouth must be over the bowl, and a picture shows it.
        _eatTime = _main.Brain.State == CatState.Eat ? _eatTime + dt : 0;
        if (!_eatChecked && _eatTime > 0.8)
        {
            _eatChecked = true;
            // The real nose tip from the posed skeleton (1 world unit = 1 px, y up), in screen pixels.
            var nose = _main.Visual.BonePoint("Head", 0.14f);
            double bowl = _main.World.Bowl.Body.Pos.X;
            if (nose is { } n)
            {
                double mouth = _main.OverlayWindow.ToScreen(new Vector2(n.X, -n.Y)).X;
                Expect(Math.Abs(mouth - bowl) < 18, $"mangia con il muso sopra la ciotola (muso {mouth:0}, ciotola {bowl:0})");
            }
            Shot();
        }

        // Pictures of the take-off: taking aim, and rising steeply (the body should be nearly upright).
        if (_mode == "tour" && !_shotAim && _main.Brain.Action == "aim") { _shotAim = true; Shot(); }
        if (_mode == "tour" && !_shotLeap && _main.Body.Mode == BodyMode.Airborne && _main.Body.Vel.Y < -700
            && Math.Abs(_main.Body.Vel.X) < 400 && _main.Brain.State != CatState.Held)
        {
            _shotLeap = true;
            Log.Info($"selftest leap vel=({_main.Body.Vel.X:0},{_main.Body.Vel.Y:0}) pitch={Flight.PitchDeg(_main.Body.Vel):0}");
            Shot();
        }

        string state = $"{_main.Brain.State}/{_main.Brain.Action}";
        _logTimer += dt;
        if (_logTimer >= 1 || state != _lastState)
        {
            _logTimer = 0;
            _lastState = state;
            var b = _main.Body;
            var n = _main.NeedsState;
            Log.Info($"t={_t:0.0} {state} pos=({b.Pos.X:0},{b.Pos.Y:0}) mode={b.Mode} on={b.Support?.Kind} " +
                     $"fame={n.Hunger:0.00} energia={n.Energy:0.00} gioco={n.Playfulness:0.00} ciotola={_main.World.BowlFood:0.00}");
        }
    }

    void Grab()
    {
        _main.Brain.OnGrab(_main.Body);
        _main.Body.MoveHeld(_main.Body.Pos + new Vec2(0, -250));
        _main.Visual.HeldSpin = 20;
    }

    void LogPlatforms()
    {
        foreach (var line in ZairaPet.Windows.Win32.Describe(_main.OverlayWindow.Handle, (uint)OS.GetProcessId()))
            Log.Info("  finestra " + line);
        foreach (var p in _main.Map.Platforms)
            Log.Info($"  piattaforma {p.Kind} y={p.Y} x={p.X0}..{p.X1} owner={p.Owner}");
    }

    void Shot()
    {
        var img = _main.GetViewport().GetTexture().GetImage();
        var c = _main.OverlayWindow.ToLocal(_main.Body.Pos);
        int w = 700, h = 500;
        var rect = new Rect2I((int)c.X - w / 2, (int)c.Y - h + 120, w, h).Intersection(new Rect2I(Vector2I.Zero, img.GetSize()));
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;
        var crop = img.GetRegion(rect);
        // Composite on grey so transparency is visible.
        var bg = Image.CreateEmpty(crop.GetWidth(), crop.GetHeight(), false, Image.Format.Rgba8);
        bg.Fill(new Color(0.55f, 0.6f, 0.65f));
        crop.Convert(Image.Format.Rgba8);
        bg.BlendRect(crop, new Rect2I(Vector2I.Zero, crop.GetSize()), Vector2I.Zero);
        string path = System.IO.Path.Combine(OS.GetUserDataDir(), $"selftest_{_shot++:00}_{_main.Brain.State}.png");
        bg.SavePng(path);
        Log.Info($"screenshot {path}");
    }
}
