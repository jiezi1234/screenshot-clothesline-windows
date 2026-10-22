using System;
using System.Drawing;
using System.IO;
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length == 2 && args[0] == "--self-test") return Diagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--test-backdrop") return Diagnostics.Backdrop(app, args[1]);
        if (args.Length == 2 && args[0] == "--library-test") return LibraryDiagnostics.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--editor-test") return EditorDiagnostics.Run(app, args[1]);
        using var instance = new Mutex(true, "Local\\ScreenshotClothesline.Prototype", out var first);
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ScreenshotClothesline.Prototype.Wake");
        if (!first) { wake.Set(); return 0; }
        using var controller = new ShelfController();
        using var library = new LibraryRuntime(controller);
        using var shortcut = new HotKey(controller.Toggle);
        Dashboard? dashboard = null;
        void Quit() { if (!library.TryCloseWorkspaces()) return; if (dashboard is not null) dashboard.Exiting = true; app.Shutdown(); }
        dashboard = new Dashboard(controller, shortcut.Registered, Quit, library);
        using var tray = new Forms.NotifyIcon { Text = "Screenshot Clothesline · 截图与历史", Icon = CreateIcon(), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 收起晾衣绳", null, (_, _) => controller.Toggle());
        menu.Items.Add("打开控制面板", null, (_, _) => dashboard.Reveal());
        menu.Items.Add("本地历史", null, (_, _) => dashboard.ShowHistory());
        menu.Items.Add("暂停 / 恢复收集", null, (_, _) => library.TogglePause());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Quit()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => dashboard.Reveal();
        var waiter = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => app.Dispatcher.BeginInvoke((Action)dashboard.Reveal), null, Timeout.Infinite, false);
        app.DispatcherUnhandledException += (_, e) => { dashboard.SetStatus("操作失败：" + e.Exception.Message); e.Handled = true; };
        try { library.Start(); dashboard.Show(); return app.Run(); }
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
