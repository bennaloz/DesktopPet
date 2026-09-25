using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ZairaPet.Core;

namespace ZairaPet.Windows;

/// <summary>Thin P/Invoke layer over user32/dwmapi. All coordinates are physical screen pixels.</summary>
internal static partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    struct POINT { public int X, Y; }

    /// <summary>Mouse cursor on the virtual desktop, even when it is over other applications' windows.</summary>
    public static (int x, int y)? CursorPos() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const long WS_CHILD = 0x40000000;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_APPWINDOW = 0x00040000;
    const long WS_EX_NOACTIVATE = 0x08000000;
    const long WS_EX_TRANSPARENT = 0x00000020;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const int DWMWA_CLOAKED = 14;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    /// <summary>Visible top-level application windows, topmost first.</summary>
    public static List<WindowInfo> EnumerateWindows(IntPtr exclude, uint excludePid)
    {
        var list = new List<WindowInfo>();
        Scan(exclude, excludePid, (info, reason) => { if (reason == null) list.Add(info); });
        return list;
    }

    /// <summary>Every visible top-level window with the reason it is ignored (null = used). For the log.</summary>
    public static List<string> Describe(IntPtr exclude, uint excludePid)
    {
        var lines = new List<string>();
        var title = new StringBuilder(128);
        Scan(exclude, excludePid, (info, reason) =>
        {
            title.Clear();
            GetWindowTextW(new IntPtr(info.Handle), title, title.Capacity);
            lines.Add($"{(reason == null ? "USATA  " : "scartata")} {info.Bounds} \"{title}\" {reason}");
        }, includeInvisible: false);
        return lines;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int max);

    static void Scan(IntPtr exclude, uint excludePid, Action<WindowInfo, string?> report, bool includeInvisible = false)
    {
        var cls = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            if (h == exclude || !IsWindowVisible(h)) return true;
            if (!GetWindowRect(h, out RECT raw)) return true;
            var info = new WindowInfo(h.ToInt64(), new RectI(raw.Left, raw.Top, raw.Right, raw.Bottom));
            string? reason = Reject(h, excludePid, cls, ref info);
            report(info, reason);
            return true;
        }, IntPtr.Zero);
    }

    static string? Reject(IntPtr h, uint excludePid, StringBuilder cls, ref WindowInfo info)
    {
        if (IsIconic(h)) return "minimizzata";
        long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        if ((style & WS_CHILD) != 0) return "child";
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return "tool window";
        if ((ex & WS_EX_TRANSPARENT) != 0) return "trasparente ai clic";
        if ((ex & WS_EX_NOACTIVATE) != 0 && (ex & WS_EX_APPWINDOW) == 0) return "noactivate";
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return "cloaked";
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == excludePid) return "nostra";
        if (GetWindowTextLengthW(h) == 0) return "senza titolo";
        cls.Clear();
        GetClassNameW(h, cls, cls.Capacity);
        if (IgnoredClasses.Contains(cls.ToString())) return $"classe {cls}";
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0)
            info = info with { Bounds = new RectI(r.Left, r.Top, r.Right, r.Bottom) };
        var b = info.Bounds;
        if (b.Width < 120 || b.Height < 80) return "troppo piccola";
        return null;
    }

    /// <summary>Monitor bounds and work areas (the part not covered by the taskbar).</summary>
    public static List<(RectI bounds, RectI work)> EnumerateMonitors()
    {
        var list = new List<(RectI, RectI)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT __, IntPtr ___) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(hMon, ref mi))
                list.Add((ToRect(mi.rcMonitor), ToRect(mi.rcWork)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Hide the overlay from the taskbar and Alt-Tab, and never give it focus.</summary>
    public static void MakeToolWindow(IntPtr hWnd)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr hRgn, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    /// <summary>
    /// Whether a screen point lies inside the window region Windows actually applied (what gets drawn and
    /// clicked). True when the window has no region at all.
    /// </summary>
    public static bool RegionContains(IntPtr hWnd, int screenX, int screenY)
    {
        var r = GetBounds(hWnd);
        IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
        try
        {
            int kind = GetWindowRgn(hWnd, rgn);
            if (kind == 0) return true;   // ERROR: no region set
            return PtInRegion(rgn, screenX - r.Left, screenY - r.Top);
        }
        finally { DeleteObject(rgn); }
    }

    public static RectI GetBounds(IntPtr hWnd) => GetWindowRect(hWnd, out var r) ? ToRect(r) : default;

    static RectI ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
