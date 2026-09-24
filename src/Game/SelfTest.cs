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

    double _t;
    double _logTimer;
    int _next;
    int _shot;
    string _lastState = "";

    public SelfTest(Main main, string mode)
    {
        _main = main;
        _script = mode switch
        {
            "windows" => WindowScript(), "mouse" => MouseScript(), "input" => InputScript(), _ => TourScript(),
        };
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
    /// </summary>
    List<(double, string, Action)> InputScript()
    {
        Vector2 cat = default;
        var list = new List<(double, string, Action)>
        {
            (2.0, "sit still", () => { _main.Brain.SitFor(120); _main.World.Bowl.Food = 0; }),
            (3.0, "press on cat", () =>
            {
                cat = _main.OverlayWindow.ToLocal(_main.Body.Pos) - new Vector2(0, _main.Visual.SizePx.Y * 0.5f);
                Button(cat, true);
            }),
        };
        for (int i = 1; i <= 15; i++)
        {
            int k = i;
            list.Add((3.0 + k * 0.05, "", () => Motion(cat + new Vector2(k * 6, -k * 12), new Vector2(6, -12), false)));
        }
        list.Add((4.0, "check held", () => Expect(_main.Brain.State == CatState.Held, "gatto in braccio")));
        list.Add((4.05, "check region while dragging", () =>
            Expect(InRegion(new Vec2(_main.OverlayWindow.Origin.X + 20, _main.OverlayWindow.Origin.Y + 20)), "durante il trascinamento la finestra prende tutto il mouse")));
        list.Add((4.1, "screenshot", Shot));
        list.Add((4.2, "release", () => Button(cat + new Vector2(90, -180), false)));
        list.Add((4.25, "check thrown", () => Expect(_main.Body.Mode == BodyMode.Airborne, "gatto lanciato")));
        list.Add((7.0, "sit again", () => _main.Brain.SitFor(120)));
        list.Add((7.2, "check region", CheckRegion));
        for (int i = 0; i < 60; i++)
        {
            int k = i;
            list.Add((7.5 + k * 0.033, "", () =>
            {
                var c = _main.OverlayWindow.ToLocal(_main.Body.Pos) - new Vector2(0, _main.Visual.SizePx.Y * 0.4f);
                float dx = (k % 10 < 5 ? 1 : -1) * 12;
                Motion(c + new Vector2(dx * (k % 5) - 24, 0), new Vector2(dx, 0), false);
            }));
        }
        list.Add((9.6, "check purring", () => Expect(_main.Brain.State == CatState.Petted, "fusa dopo le carezze")));
        list.Add((9.7, "screenshot", Shot));
        list.Add((11.0, "double-click bowl", () =>
        {
            var bowl = _main.OverlayWindow.ToLocal(_main.World.Bowl.Body.Pos) - new Vector2(0, 10);
            Log.Info($"selftest bowl body={_main.World.Bowl.Body.Pos} rect={_main.World.Bowl.ScreenRect} click={bowl} cat={_main.Body.Pos} origin={_main.OverlayWindow.Origin}");
            Button(bowl, true, doubleClick: true);
            Button(bowl, false);
        }));
        list.Add((11.2, "check bowl", () => Expect(_main.World.Bowl.Food > 0.99, "ciotola riempita col doppio clic")));
        list.Add((12.0, "quit", () => _main.Quit()));
        return list;
    }

    static void Button(Vector2 at, bool pressed, bool doubleClick = false) =>
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = pressed, DoubleClick = doubleClick, Position = at, GlobalPosition = at,
        });

    static void Motion(Vector2 at, Vector2 rel, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = at, GlobalPosition = at, Relative = rel,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
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
            (32.0, "screenshot", Shot),
            (34.0, "screenshot", Shot),
            (46.0, "tired", () => { _main.NeedsState.Playfulness = 0; _main.NeedsState.Energy = 0.1; _main.Brain.Notice(); }),
            (58.0, "screenshot", Shot),
            (62.0, "grab", Grab),
            (63.0, "screenshot", Shot),
            (64.0, "release", () => _main.Brain.OnRelease(_main.Body, new Vec2(300, -600))),
            (64.3, "screenshot", Shot),
            (68.0, "screenshot", Shot),
            (70.0, "quit", () => _main.Quit()),
        };
    }

    public void Tick(double dt)
    {
        _t += dt;
        while (_next < _script.Count && _script[_next].at <= _t)
        {
            var (_, what, act) = _script[_next++];
            if (what != "") Log.Info($"selftest {_t:0.0}s: {what}");
            act();
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
