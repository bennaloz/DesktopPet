using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZairaPet.Core;
using ZairaPet.Windows;

namespace ZairaPet.Game;

/// <summary>
/// The transparent always-on-top window stretched over every monitor, and the conversions between
/// Win32 screen pixels (what the logic uses) and the window/world coordinates Godot renders in.
/// </summary>
public sealed class Overlay
{
    public RectI Virtual { get; private set; }
    public List<(RectI bounds, RectI work)> Monitors { get; private set; } = new();
    /// <summary>Win32 position of the window's top-left pixel.</summary>
    public Vector2I Origin { get; private set; }
    public Vector2I Size { get; private set; }
    public IntPtr Handle { get; private set; }

    Rect2I? _passthrough;

    public void Setup(Window window)
    {
        Monitors = Win32.EnumerateMonitors();
        Virtual = new RectI(Monitors.Min(m => m.bounds.Left), Monitors.Min(m => m.bounds.Top),
                            Monitors.Max(m => m.bounds.Right), Monitors.Max(m => m.bounds.Bottom));

        // Godot shifts screen coordinates so the desktop starts at 0,0; the Win32 primary monitor is at 0,0.
        var primary = DisplayServer.ScreenGetPosition(DisplayServer.GetPrimaryScreen());
        var godotPos = new Vector2I(Virtual.Left, Virtual.Top) + primary;
        // One pixel short of the full height, or Windows treats us as a fullscreen app.
        Size = new Vector2I(Virtual.Width, Virtual.Height - 1);

        window.Mode = Window.ModeEnum.Windowed;
        window.Size = Size;
        window.Position = godotPos;

        Handle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        Win32.MakeToolWindow(Handle);
        var r = Win32.GetBounds(Handle);
        Origin = new Vector2I(r.Left, r.Top);
        Log.Info($"overlay: virtual={Virtual} godotPos={godotPos} win32={r} monitors={Monitors.Count}");
    }

    /// <summary>Screen (Win32) → window-local pixels.</summary>
    public Vector2 ToLocal(Vec2 p) => new((float)(p.X - Origin.X), (float)(p.Y - Origin.Y));

    /// <summary>Window-local pixels → screen (Win32).</summary>
    public Vec2 ToScreen(Vector2 local) => new(local.X + Origin.X, local.Y + Origin.Y);

    /// <summary>Screen (Win32) → world: x right, y up, 1 unit per pixel.</summary>
    public Vector3 ToWorld(Vec2 p, float z = 0) => new((float)(p.X - Origin.X), -(float)(p.Y - Origin.Y), z);

    public Rect2I ToLocal(RectI r) => new(r.Left - Origin.X, r.Top - Origin.Y, r.Width, r.Height);

    /// <summary>
    /// Only this rectangle (window-local) receives the mouse; everything else goes to the windows below.
    /// Null captures nothing, the whole window when <paramref name="all"/> is set.
    /// </summary>
    public void SetClickable(Rect2I? rect, bool all = false)
    {
        if (all) rect = new Rect2I(0, 0, Size.X, Size.Y);
        rect ??= new Rect2I(-10, -10, 1, 1);   // an empty region would mean "everything"
        if (_passthrough == rect) return;
        _passthrough = rect;
        var r = rect.Value;
        DisplayServer.WindowSetMousePassthrough(new[]
        {
            new Vector2(r.Position.X, r.Position.Y), new Vector2(r.End.X, r.Position.Y),
            new Vector2(r.End.X, r.End.Y), new Vector2(r.Position.X, r.End.Y),
        });
    }
}

/// <summary>Tiny file logger in the Godot user folder.</summary>
public static class Log
{
    static string? _path;

    public static void Info(string msg)
    {
        GD.Print(msg);
        try
        {
            _path ??= System.IO.Path.Combine(OS.GetUserDataDir(), "zaira.log");
            System.IO.File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{System.Environment.NewLine}");
        }
        catch (System.IO.IOException) { }
    }
}
