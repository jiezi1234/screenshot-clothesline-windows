using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed record MonitorInfo(nint Handle, PixelRect Bounds, PixelRect WorkArea);

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorData
    { public int Size; public Rect Bounds, Work; public uint Flags; }
    internal delegate bool MonitorCallback(nint monitor, nint hdc, nint rect, nint data);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorData info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out int value, int size);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);

    public static PixelPoint Cursor { get { GetCursorPos(out var p); return new(p.X, p.Y); } }
    public static MonitorInfo MonitorAt(PixelPoint p) => Monitor(MonitorFromPoint(new Point { X = p.X, Y = p.Y }, 2));
    private static MonitorInfo Monitor(nint handle)
    {
        var data = new MonitorData { Size = Marshal.SizeOf<MonitorData>() };
        if (!GetMonitorInfoW(handle, ref data)) throw new InvalidOperationException(T("无法读取显示器信息。"));
        return new(handle, data.Bounds.Pixels, data.Work.Pixels);
    }
    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var result = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint h, nint dc, nint rect, nint data) => { result.Add(Monitor(h)); return true; }, 0);
        return result;
    }
    public static void ConfigureOverlay(Window window, bool passThrough)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtrW(hwnd, -20).ToInt64() | 0x08000000L | 0x80; // NOACTIVATE, TOOLWINDOW
        if (passThrough) style |= 0x20; // WS_EX_TRANSPARENT on a layered window
        SetWindowLongPtrW(hwnd, -20, (nint)style);
        HwndSource.FromHwnd(hwnd)?.AddHook((nint h, int message, nint w, nint l, ref bool handled) =>
        {
            if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE; don't eat the click
            return 0;
        });
    }
    public static double MoveAndGetScale(Window window, MonitorInfo monitor)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, 0, monitor.WorkArea.X, monitor.WorkArea.Y, 1, 1, 0x14); // fit wholly on target before querying DPI
        return Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
    }
    public static bool MakeInputTransparent(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLongPtrW(hwnd, -20, (nint)(GetWindowLongPtrW(hwnd, -20).ToInt64() | 0x20));
        return (GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x20) != 0;
    }
    public static void Place(Window window, PixelRect rect)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (!SetWindowPos(hwnd, -1, rect.X, rect.Y, rect.Width, rect.Height, 0x10))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static bool IsFullscreen(MonitorInfo monitor)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassNameW(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (DwmGetWindowAttribute(hwnd, 9, out Rect rect, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(hwnd, out rect)) return false;
        var style = GetWindowLongPtrW(hwnd, -16).ToInt64();
        // Framed maximized apps are deliberately excluded. Borderless fullscreen remains heuristic.
        return DesktopGeometry.CoversMonitor(rect.Pixels, monitor.Bounds, (style & 0x00c00000) != 0);
    }
}

internal sealed class HotKey : IDisposable
{
    private readonly HwndSource source;
    private int activeId = 1;
    private bool disposed;
    public bool Registered { get; private set; }
    public string Gesture { get; private set; }
    public HotKey(Action toggle, string gesture = "Ctrl+Alt+T")
    {
        source = new HwndSource(new HwndSourceParameters("Clothesline.Messages") { ParentWindow = -3, Width = 0, Height = 0 });
        source.AddHook((nint h, int message, nint w, nint l, ref bool handled) =>
        { if (message == 0x312 && Registered && w == activeId) { handled = true; toggle(); } return 0; });
        var parsed = Clothesline.Core.ShortcutGesture.Parse(gesture); Gesture = parsed.Display;
        Registered = parsed.Key != 0 && Native.RegisterHotKey(source.Handle, activeId, parsed.Modifiers | 0x4000, parsed.Key);
    }
    public PreparedShortcut Prepare(string gesture)
    {
        var parsed = Clothesline.Core.ShortcutGesture.Parse(gesture);
        if (parsed.Display == Gesture) return new(() => { }, () => { });
        var candidateId = activeId == 1 ? 2 : 1;
        if (parsed.Key != 0 && !Native.RegisterHotKey(source.Handle, candidateId, parsed.Modifiers | 0x4000, parsed.Key))
            throw new InvalidOperationException(T("快捷键被占用或被系统保留，原设置已保留。请选择其他组合。"));
        return new(() =>
        {
            if (Registered) Native.UnregisterHotKey(source.Handle, activeId);
            activeId = candidateId; Gesture = parsed.Display; Registered = parsed.Key != 0;
        }, () => { if (parsed.Key != 0) Native.UnregisterHotKey(source.Handle, candidateId); });
    }
    public bool Retry()
    {
        if (Registered || Gesture.Length == 0) return Registered;
        var parsed = Clothesline.Core.ShortcutGesture.Parse(Gesture);
        return Registered = Native.RegisterHotKey(source.Handle, activeId, parsed.Modifiers | 0x4000, parsed.Key);
    }
    public void Dispose() { if (disposed) return; disposed = true; if (Registered) Native.UnregisterHotKey(source.Handle, activeId); Registered = false; source.Dispose(); }
}

internal sealed class PreparedShortcut(Action commit, Action rollback) : IDisposable
{
    private bool finished;
    public void Commit() { if (finished) return; commit(); finished = true; }
    public void Dispose() { if (finished) return; finished = true; rollback(); }
}
