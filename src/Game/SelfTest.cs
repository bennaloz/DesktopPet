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

    public SelfTest(Main main, bool windows)
    {
        _main = main;
        _script = windows ? WindowScript() : TourScript();
        Log.Info($"selftest: start ({(windows ? "finestre" : "giro")})");
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
            Log.Info($"selftest {_t:0.0}s: {what}");
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
