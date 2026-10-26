using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Clothesline.App;

/// Uses only the public Shell interface; never switches or pins the user's desktops.
internal static class DesktopLifecycle
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }
    private static IVirtualDesktopManager? manager;
    private static bool attempted;
    private static IVirtualDesktopManager? Manager
    {
        get
        {
            if (!attempted)
            {
                attempted = true;
                try { manager = (IVirtualDesktopManager?)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"))!); }
                catch (COMException) { }
            }
            return manager;
        }
    }
    internal static bool? IsCurrent(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            return handle != 0 && Manager is { } api && api.IsWindowOnCurrentVirtualDesktop(handle, out var current) >= 0 ? current : null;
        }
        catch (COMException) { manager = null; attempted = false; return null; }
    }
    internal static bool FollowForeground(Window window)
    {
        try
        {
            if (Manager is not { } api) return false;
            var target = Native.GetForegroundWindow();
            if (target == 0 || api.GetWindowDesktopId(target, out var desktop) < 0 || desktop == Guid.Empty) return false;
            var handle = new WindowInteropHelper(window).EnsureHandle();
            if (api.GetWindowDesktopId(handle, out var existing) >= 0 && existing == desktop) return true;
            return api.MoveWindowToDesktop(handle, desktop) >= 0;
        }
        catch (COMException) { manager = null; attempted = false; return false; }
    }
}
