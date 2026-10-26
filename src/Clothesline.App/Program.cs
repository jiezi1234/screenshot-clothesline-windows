using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Clothesline.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--image-server") return ImageDecoder.Serve(args[1]);
        if (args.Length == 5 && args[0] == "--image-worker") { Clothesline.Core.UiText.Language = args[4]; return ImageDecoder.Worker(args[1], args[2], args[3]); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length == 3 && args[0] == "--compatibility-backdrop") return CompatibilityDiagnostics.Backdrop(app, args[1], args[2]);
        if (args.Length == 2 && args[0] == "--compatibility-test") return CompatibilityDiagnostics.Run(app, args[1], false);
        if (args.Length == 2 && args[0] == "--performance-test") return CompatibilityDiagnostics.Run(app, args[1], true);
        if (args.Length == 2 && args[0] == "--soak-test") return CompatibilityDiagnostics.Run(app, args[1], false, true);
        if (args.Length == 2 && args[0] == "--image-format-test") return ImageFormatDiagnostics.Run(app, args[1]);
        if (args.Length == 4 && args[0] == "--video-probe") { Clothesline.Core.UiText.Language = args[3]; return VideoCatalog.Probe(app, args[1], args[2]); }
        if (args.Length == 2 && args[0] == "--video-test") return VideoDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--video-trim-worker") return VideoTrimmer.Worker(app, args[1]);
        if (args.Length == 2 && args[0] == "--video-trim-test") return VideoTrimDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--self-test") return Diagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--test-backdrop") return Diagnostics.Backdrop(app, args[1]);
        if (args.Length == 2 && args[0] == "--library-test") return LibraryDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--editor-test") return EditorDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--preferences-test") return PreferencesDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--appearance-test") return AppearanceDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--motion-test") return MotionDiagnostics.Run(app, args[1]);
        using var instance = new Mutex(true, "Local\\ScreenshotClothesline.Prototype", out var first);
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ScreenshotClothesline.Prototype.Wake");
        if (!first) { if (!args.Contains("--startup")) wake.Set(); return 0; }
        using var controller = new ShelfController();
        using var library = new LibraryRuntime(controller);
        using var shortcut = new HotKey(controller.Toggle, library.Settings.Shortcut);
        library.Desktop = DesktopPreferences.Create(shortcut);
        Dashboard? dashboard = null;
        void Quit() { if (!library.TryCloseWorkspaces()) return; if (dashboard is not null) dashboard.Exiting = true; app.Shutdown(); }
        dashboard = new Dashboard(controller, shortcut.Registered, Quit, library);
        using var tray = new Forms.NotifyIcon { Text = T("Screenshot Clothesline · 截图与历史"), Icon = CreateIcon(), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("显示 / 收起晾衣绳"), null, (_, _) => controller.Toggle());
        menu.Items.Add(T("打开控制面板"), null, (_, _) => dashboard.Reveal());
        menu.Items.Add(T("本地历史"), null, (_, _) => dashboard.ShowHistory());
        menu.Items.Add(T("快捷键与登录启动"), null, (_, _) => dashboard.ShowDesktopSettings());
        menu.Items.Add(T("外观与语言"), null, (_, _) => dashboard.ShowAppearance());
        menu.Items.Add(T("暂停 / 恢复收集"), null, (_, _) => library.TogglePause());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("退出"), null, (_, _) => Quit()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => dashboard.Reveal();
        var waiter = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => app.Dispatcher.BeginInvoke((Action)dashboard.Reveal), null, Timeout.Infinite, false);
        app.DispatcherUnhandledException += (_, e) => { dashboard.SetStatus(T("操作失败：") + e.Exception.Message); e.Handled = true; };
        try { library.Start(); if (!args.Contains("--startup")) dashboard.Show(); return app.Run(); }
        finally { waiter.Unregister(null); tray.Visible = false; tray.Icon?.Dispose(); menu.Dispose(); instance.ReleaseMutex(); }
    }
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32); using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent); graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var green = new SolidBrush(Color.FromArgb(49, 91, 67)); using var cream = new SolidBrush(Color.FromArgb(244, 245, 238)); using var pin = new SolidBrush(Color.FromArgb(188, 143, 88));
        graphics.FillEllipse(green, 0, 0, 32, 32); graphics.FillRectangle(cream, 8, 10, 16, 15); graphics.FillRectangle(pin, 14, 6, 4, 8);
        var handle = bitmap.GetHicon(); try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); } finally { Native.DestroyIcon(handle); }
    }
}
