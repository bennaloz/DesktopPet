using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZairaPet.Core;
using ZairaPet.Windows;

namespace ZairaPet.Game;

/// <summary>Root node: wires the Win32 window map, the brain, the 3D cat, the props, the mouse and the tray.</summary>
public partial class Main : Node3D
{
    enum Drag { None, Cat, Bowl, Perch }

    const double TrackInterval = 1.0 / 8;
    const double SaveInterval = 30;

    readonly Overlay _overlay = new();
    readonly Random _rng = new();
    readonly Needs _needs = new();
    readonly GameWorld _world = new();
    CatBrain _brain = null!;
    Gaze _gaze = null!;
    CatBody _body = null!;
    CatVisual _visual = null!;
    Label3D _emote = null!;
    SurfaceMap _map = null!;
    StatusIndicator _tray = null!;
    PopupMenu _menu = null!;
    SelfTest? _selfTest;

    double _trackTimer;
    double _monitorTimer;
    double _saveTimer;
    Camera3D _cam = null!;
    double _tooltipTimer;
    bool _paused;
    Drag _drag;
    Vector2 _dragOffset;
    Vector2 _mouse;

    string SavePath => System.IO.Path.Combine(OS.GetUserDataDir(), "save.json");

    public override void _Ready()
    {
        Engine.MaxFps = 30;
        GetViewport().TransparentBg = true;
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
        _overlay.Setup(GetWindow());
        SetupScene();

        var profile = CatProfile.Load(CatFolder());
        _visual = new CatVisual { Name = "Cat" };
        AddChild(_visual);
        _visual.Load(profile);
        Log.Info($"cat: {profile.Name}, size {_visual.SizePx}");

        _emote = new Label3D
        {
            Font = new SystemFont { FontNames = new[] { "Segoe UI Emoji", "Segoe UI Symbol", "Segoe UI" } },
            FontSize = 40, PixelSize = 1, OutlineSize = 10, NoDepthTest = true,
            Modulate = new Color(1, 0.45f, 0.55f), OutlineModulate = new Color(1, 1, 1, 0.9f),
        };
        AddChild(_emote);

        RebuildMap(initial: true);
        PlaceEverything(LoadSave());
        SetupTray();

        _brain = new CatBrain(_rng) { EatReach = profile.EatReach * profile.LengthPx };
        _gaze = new Gaze(_rng);
        _world.TreatEaten = RemoveTreat;

        var args = OS.GetCmdlineUserArgs();
        string? mode = args.Contains("--selftest") ? "tour" : args.Contains("--selftest-windows") ? "windows"
                     : args.Contains("--selftest-mouse") ? "mouse" : args.Contains("--selftest-input") ? "input"
                     : args.Contains("--selftest-gaze") ? "gaze" : null;
        if (mode != null) _selfTest = new SelfTest(this, mode);
    }

    string CatFolder()
    {
        // A cats/zaira folder wins over the placeholder as soon as it exists.
        string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--cat="))?.Substring(6) ?? "";
        foreach (var name in new[] { arg, "zaira", "fox" })
        {
            if (name == "") continue;
            string dir = ProjectSettings.GlobalizePath($"res://cats/{name}");
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "profile.json"))) return dir;
        }
        throw new InvalidOperationException("nessun profilo gatto in cats/");
    }

    void SetupScene()
    {
        _cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = 1, Far = 4000,
        };
        AddChild(_cam);
        _cam.MakeCurrent();
        FitCamera();

        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 35, 0), LightEnergy = 1.15f });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, -150, 0), LightEnergy = 0.35f });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.8f, 0.8f, 0.85f),
                AmbientLightEnergy = 0.55f,
            },
        });
    }

    /// <summary>1 world unit = 1 pixel over the whole overlay.</summary>
    void FitCamera()
    {
        var size = _overlay.Size;
        _cam.Size = size.Y;
        _cam.Position = new Vector3(size.X / 2f, -size.Y / 2f, 1500);
    }

    /// <summary>Monitors changed (docking, resolution, taskbar): stretch the overlay again over the new desktop.</summary>
    void ReconfigureDisplay()
    {
        Log.Info("monitor cambiati: riconfiguro l'overlay");
        _overlay.Setup(GetWindow());
        FitCamera();
        RebuildMap();
    }

    /// <summary>Screen position of the head, roughly: in front of the body centre, high up.</summary>
    Vec2 HeadPos => _body.Pos + new Vec2(_brain.Facing * _visual.SizePx.X * 0.38, -_visual.SizePx.Y * 0.8);

    void UpdateGaze(double dt)
    {
        var cursor = Win32.CursorPos();
        var head = HeadPos;
        _gaze.Update(dt, new GazeInput(_paused ? CatState.Sit : _brain.State, head, _brain.Facing,
            cursor is { } c ? new Vec2(c.x, c.y) : null, _brain.JumpTarget, _world.Treat?.Body.Pos));
        Vector3? target = _gaze.Kind switch
        {
            // The viewer sits in front of the screen: straight out of it, level with the head.
            GazeKind.Viewer => _overlay.ToWorld(head, 900),
            GazeKind.Point => _overlay.ToWorld(_gaze.Point, 40),
            _ => null,
        };
        _visual.Look(target, dt);
    }

    // ------------------------------------------------------------------ world state

    void RebuildMap(bool initial = false)
    {
        List<WindowInfo> windows;
        try { windows = Win32.EnumerateWindows(_overlay.Handle, (uint)OS.GetProcessId()); }
        catch (Exception e) when (!initial)
        {
            // Keep the previous map: a failed scan must not stop the cat.
            Log.Info($"scansione finestre fallita: {e.Message}");
            return;
        }
        var work = _overlay.Monitors.Select(m => m.work).ToList();
        if (initial)
        {
            _map = SurfaceMap.Build(windows, work, Array.Empty<Platform>());
            return;
        }

        // The perch may stand on a window: move it first, then build its top where it is now,
        // then move whatever stands on that top. Otherwise the perch surface lags one scan behind.
        _world.Perch.Body.FollowSupport(SurfaceMap.Build(windows, work, Array.Empty<Platform>()));
        var extra = new List<Platform>();
        if (_world.Perch.Body.Mode == BodyMode.Grounded)
            extra.Add(_world.Perch.TopPlatform());
        _map = SurfaceMap.Build(windows, work, extra);
        _world.PerchPlatform = _map.Platforms.FirstOrDefault(p => p.Kind == SurfaceKind.Perch);

        _body.FollowSupport(_map);
        _world.Bowl.Body.FollowSupport(_map);
        _world.Treat?.Body.FollowSupport(_map);
    }

    /// <summary>Self tests start from a fresh cat and never touch the real one's save.</summary>
    static bool Testing => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--selftest"));

    SaveData LoadSave()
    {
        if (Testing) return new SaveData();
        try
        {
            if (System.IO.File.Exists(SavePath)) return SaveData.FromJson(System.IO.File.ReadAllText(SavePath));
        }
        catch (System.IO.IOException e) { Log.Info($"save non leggibile: {e.Message}"); }
        return new SaveData();
    }

    void Save()
    {
        if (Testing) return;
        var s = SaveData.Capture(_needs, _world.Bowl.Food, _world.Bowl.Body.Pos.X, _world.Perch.Body.Pos.X, _body.Pos.X);
        try { System.IO.File.WriteAllText(SavePath, s.ToJson()); }
        catch (System.IO.IOException e) { Log.Info($"save fallito: {e.Message}"); }
    }

    void PlaceEverything(SaveData save)
    {
        var restored = save.RestoreNeeds(DateTime.UtcNow);
        _needs.Hunger = restored.Hunger;
        _needs.Energy = restored.Energy;
        _needs.Playfulness = restored.Playfulness;
        _needs.Affection = restored.Affection;

        // Primary monitor floor unless the save says otherwise.
        var primary = _overlay.Monitors.FirstOrDefault(m => m.bounds.Left == 0 && m.bounds.Top == 0);
        if (primary == default) primary = _overlay.Monitors[0];
        var floor = _map.NearestFloor(primary.work.Left + primary.work.Width / 2.0);
        double X(double saved, double fraction) =>
            double.IsNaN(saved) || _map.Platforms.All(p => p.Kind != SurfaceKind.Floor || !p.SpansX(saved))
                ? floor.X0 + (floor.X1 - floor.X0) * fraction : saved;

        _world.Bowl = new Bowl(new Vec2(X(save.BowlX, 0.72), floor.Y)) { Name = "Bowl", Food = save.BowlFood };
        _world.Perch = new Perch(new Vec2(X(save.PerchX, 0.9), floor.Y)) { Name = "Perch" };
        AddChild(_world.Perch);
        AddChild(_world.Bowl);
        _world.Bowl.Body.PlaceOn(_map.NearestFloor(_world.Bowl.Body.Pos.X), _world.Bowl.Body.Pos.X);
        _world.Perch.Body.PlaceOn(_map.NearestFloor(_world.Perch.Body.Pos.X), _world.Perch.Body.Pos.X);

        double catX = X(save.CatX, 0.5);
        _body = new CatBody(new Vec2(catX, floor.Y));
        _body.PlaceOn(_map.NearestFloor(catX), catX);
        RebuildMap();
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        double dt = Math.Min(delta, 0.1);

        _monitorTimer += dt;
        if (_monitorTimer >= 3)
        {
            _monitorTimer = 0;
            if (_overlay.MonitorsChanged()) ReconfigureDisplay();
        }

        _trackTimer += dt;
        if (_trackTimer >= TrackInterval)
        {
            _trackTimer = 0;
            RebuildMap();
        }

        foreach (var prop in Props())
        {
            if (prop.Body.Mode == BodyMode.Held) prop.Body.TickHeld(dt);
            else prop.Body.Step(dt, _map, 0);
        }
        _body.TickHeld(dt);

        if (_paused)
        {
            if (_body.Mode != BodyMode.Held) _body.Step(dt, _map, 0);
            _visual.Play(_body.Mode == BodyMode.Grounded ? "sit" : "fall");
        }
        else
        {
            _brain.Update(dt, _body, _needs, _map, _world);
            _visual.Play(_brain.Action);
        }

        _visual.Position = _overlay.ToWorld(_body.Pos, 40);
        bool climbing = _body.Mode == BodyMode.Climbing;
        _visual.Animate(dt, _brain.Facing, climbing ? CatBody.ClimbSpeed : Math.Abs(_body.Vel.X),
                        _body.Mode == BodyMode.Held, climbing);
        UpdateGaze(dt);
        foreach (var prop in Props())
            prop.Position = _overlay.ToWorld(prop.Body.Pos, prop is Perch ? -120 : 0);

        _emote.Text = _paused ? "" : _brain.Emote ?? "";
        _emote.Position = _overlay.ToWorld(_body.Pos + new Vec2(0, -_visual.SizePx.Y - 30), 200);

        UpdateClickable();

        _saveTimer += dt;
        if (_saveTimer >= SaveInterval) { _saveTimer = 0; Save(); }
        _tooltipTimer += dt;
        if (_tooltipTimer >= 2) { _tooltipTimer = 0; _tray.Tooltip = Tooltip(); }

        _selfTest?.Tick(dt);
    }

    IEnumerable<Prop> Props()
    {
        yield return _world.Perch;
        yield return _world.Bowl;
        if (_world.Treat != null) yield return _world.Treat;
    }

    RectI CatRect()
    {
        float w = _visual.SizePx.X, h = Math.Max(_visual.SizePx.Y, 40);
        if (_body.Mode is BodyMode.Held or BodyMode.Climbing || _brain.State == CatState.Sleep)
        {
            w = Math.Max(w, h) * 0.9f;
            h = Math.Max(h, _visual.SizePx.X * 0.9f);
        }
        return new RectI((int)(_body.Pos.X - w / 2), (int)(_body.Pos.Y - h), (int)(_body.Pos.X + w / 2), (int)_body.Pos.Y + 4);
    }

    /// <summary>
    /// The window region covers exactly what is visible (cat with its emote, props), with a margin for
    /// animation overshoot. Outside it nothing is drawn and clicks reach the desktop.
    /// </summary>
    void UpdateClickable()
    {
        if (_drag != Drag.None) { _overlay.SetVisible(Array.Empty<RectI>(), all: true); return; }
        var rects = new List<RectI>();
        var c = CatRect();
        rects.Add(new RectI(c.Left - 30, c.Top - 80, c.Right + 30, c.Bottom + 12));   // room for the emote above
        foreach (var prop in Props())
        {
            var r = prop.ScreenRect;
            rects.Add(new RectI(r.Left - 10, r.Top - 12, r.Right + 10, r.Bottom + 6));
        }
        _overlay.SetVisible(rects);
    }

    // ------------------------------------------------------------------ mouse

    public override void _Input(InputEvent e)
    {
        if (_selfTest?.IgnoresMouse(e) == true) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                if (b.Pressed) Press(b.Position, b.DoubleClick);
                else ReleaseDrag();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } w
                when _drag == Drag.Cat:
                _visual.HeldSpin += w.ButtonIndex == MouseButton.WheelUp ? 25 : -25;
                break;
            case InputEventMouseMotion mm:
                Motion(mm);
                break;
        }
    }

    void Press(Vector2 local, bool doubleClick)
    {
        var p = _overlay.ToScreen(local);
        // The bowl first: it is small and the cat often stands right over it while eating.
        if (_world.Bowl.ScreenRect.Contains(p.X, p.Y))
        {
            if (doubleClick) { FillBowl(); return; }
            _drag = Drag.Bowl;
            _world.Bowl.Body.Grab();
            _dragOffset = new Vector2((float)(_world.Bowl.Body.Pos.X - p.X), (float)(_world.Bowl.Body.Pos.Y - p.Y));
        }
        else if (CatRect().Contains(p.X, p.Y))
        {
            _drag = Drag.Cat;
            _brain.OnGrab(_body);
            _visual.HeldSpin = _brain.Facing * 65;
            // Hold the cat by the scruff: the cursor sits on its back.
            _dragOffset = new Vector2(0, _visual.ScruffHeight);
            _body.MoveHeld(Held(local));
        }
        else if (_world.Perch.ScreenRect.Contains(p.X, p.Y))
        {
            _drag = Drag.Perch;
            _world.Perch.Body.Grab();
            _dragOffset = new Vector2((float)(_world.Perch.Body.Pos.X - p.X), (float)(_world.Perch.Body.Pos.Y - p.Y));
        }
    }

    Vec2 Held(Vector2 local) => _overlay.ToScreen(local + _dragOffset);

    void Motion(InputEventMouseMotion mm)
    {
        _mouse = mm.Position;
        switch (_drag)
        {
            case Drag.Cat:
                _body.MoveHeld(Held(mm.Position));
                _visual.HeldSpin += mm.Relative.X * 0.6;
                break;
            case Drag.Bowl:
                _world.Bowl.Body.MoveHeld(Held(mm.Position));
                break;
            case Drag.Perch:
                _world.Perch.Body.MoveHeld(Held(mm.Position));
                break;
            default:
                // Stroking: moving over the cat without pressing.
                var p = _overlay.ToScreen(mm.Position);
                if (CatRect().Contains(p.X, p.Y) && mm.Relative.Length() > 1.5f)
                    _brain.OnPetting(Math.Min(0.06, mm.Relative.Length() / 300));
                break;
        }
    }

    void ReleaseDrag()
    {
        switch (_drag)
        {
            case Drag.Cat:
                _brain.OnRelease(_body, _body.HeldVelocity * 0.8, _map);
                break;
            case Drag.Bowl:
                _world.Bowl.Body.Release(new Vec2(0, 0), _map);
                break;
            case Drag.Perch:
                _world.Perch.Body.Release(new Vec2(0, 0), _map);
                break;
        }
        _drag = Drag.None;
    }

    // ------------------------------------------------------------------ actions (tray and self test)

    public void FillBowl()
    {
        _world.Bowl.Food = 1;
        _brain.Notice();
        Log.Info("ciotola riempita");
    }

    public void ThrowTreat()
    {
        if (_world.Treat != null) RemoveTreat();
        var mon = _overlay.Monitors.FirstOrDefault(m => m.bounds.Contains(_body.Pos.X, Math.Max(_body.Pos.Y - 1, m.bounds.Top)));
        if (mon == default) mon = _overlay.Monitors[0];
        double x = Math.Clamp(_body.Pos.X + (_rng.NextDouble() * 2 - 1) * 400, mon.work.Left + 60, mon.work.Right - 60);
        _world.Treat = new Treat(new Vec2(x, mon.work.Top + 60)) { Name = "Treat" };
        AddChild(_world.Treat);
        _brain.Notice();
        Log.Info($"bocconcino lanciato a x={x:0}");
    }

    void RemoveTreat()
    {
        _world.Treat?.QueueFree();
        _world.Treat = null;
    }

    public void Summon()
    {
        var primary = _overlay.Monitors.FirstOrDefault(m => m.bounds.Left == 0 && m.bounds.Top == 0);
        if (primary == default) primary = _overlay.Monitors[0];
        var floor = _map.NearestFloor(primary.work.Left + primary.work.Width / 2.0);
        _drag = Drag.None;
        _brain.Summon(_body, floor, (floor.X0 + floor.X1) / 2.0);
    }

    public void Quit()
    {
        Save();
        GetTree().Quit();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Save();
    }

    string Tooltip() =>
        $"Zaira — {StateName(_brain.State)}\n" +
        $"Fame {Pct(_needs.Hunger)}  Energia {Pct(_needs.Energy)}\n" +
        $"Voglia di giocare {Pct(_needs.Playfulness)}  Contentezza {Pct(_needs.Affection)}\n" +
        $"Ciotola {Pct(_world.Bowl.Food)}";

    static string Pct(double v) => $"{v * 100:0}%";

    static string StateName(CatState s) => s switch
    {
        CatState.Idle => "si guarda intorno", CatState.Wander => "passeggia", CatState.Travel => "va da qualche parte",
        CatState.Zoomies => "zoomies!", CatState.Eat => "mangia", CatState.Sleep => "dorme", CatState.Sit => "seduta",
        CatState.Meow => "reclama la pappa", CatState.ChaseTreat => "insegue il bocconcino", CatState.Petted => "fa le fusa",
        CatState.Held => "in braccio", CatState.Airborne => "in volo", CatState.Landing => "atterra",
        CatState.Climb => "si arrampica", _ => s.ToString(),
    };

    void SetupTray()
    {
        _menu = new PopupMenu { Name = "TrayMenu" };
        _menu.AddItem("Chiama Zaira", 1);
        _menu.AddItem("Lancia un bocconcino", 2);
        _menu.AddItem("Riempi la ciotola", 3);
        _menu.AddSeparator();
        _menu.AddCheckItem("Pausa", 4);
        _menu.AddSeparator();
        _menu.AddItem("Esci", 5);
        _menu.IdPressed += id =>
        {
            switch (id)
            {
                case 1: Summon(); break;
                case 2: ThrowTreat(); break;
                case 3: FillBowl(); break;
                case 4:
                    _paused = !_paused;
                    _menu.SetItemChecked(_menu.GetItemIndex(4), _paused);
                    break;
                case 5: Quit(); break;
            }
        };
        AddChild(_menu);

        var icon = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://icon.png"));
        _tray = new StatusIndicator
        {
            Name = "Tray",
            Icon = ImageTexture.CreateFromImage(icon),
            Tooltip = "Zaira",
        };
        AddChild(_tray);
        _tray.Menu = _tray.GetPathTo(_menu);
    }

    // ------------------------------------------------------------------ self test hooks

    internal CatBrain Brain => _brain;
    internal CatBody Body => _body;
    internal Needs NeedsState => _needs;
    internal SurfaceMap Map => _map;
    internal GameWorld World => _world;
    internal Overlay OverlayWindow => _overlay;
    internal CatVisual Visual => _visual;
    internal Gaze GazeState => _gaze;
    internal Vec2 HeadScreenPos => HeadPos;
}
