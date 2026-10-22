using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal static class Diagnostics
{
    public static int Backdrop(Application app, string path)
    {
        var monitor = Native.MonitorAt(Native.Cursor);
        var window = new Window { Title = "Clothesline integration-test backdrop", WindowStyle = WindowStyle.None, Background = Brushes.AliceBlue, ShowInTaskbar = false };
        window.Show(); Native.Place(window, new PixelRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, Math.Min(450, monitor.WorkArea.Height)));
        window.Activate();
        File.WriteAllText(path, JsonSerializer.Serialize(new { Handle = new WindowInteropHelper(window).Handle.ToInt64(), ProcessId = Environment.ProcessId }));
        window.Closed += (_, _) => app.Shutdown();
        return app.Run();
    }
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var results = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { results.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            Process? backdrop = null; RopeWindow? rope = null; var cards = new List<CardWindow>();
            try
            {
                var handshake = Path.Combine(directory, "backdrop-" + Guid.NewGuid().ToString("N") + ".json");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--test-backdrop"); start.ArgumentList.Add(handshake);
                backdrop = Process.Start(start) ?? throw new InvalidOperationException("Could not start test backdrop.");
                for (var i = 0; i < 100 && !File.Exists(handshake); i++) await Task.Delay(50);
                using var info = JsonDocument.Parse(File.ReadAllText(handshake));
                var backdropHandle = (nint)info.RootElement.GetProperty("Handle").GetInt64();
                File.Delete(handshake);
                var monitor = Native.MonitorAt(Native.Cursor);
                rope = new RopeWindow(); var scale = Native.MoveAndGetScale(rope, monitor);
                var items = DemoAssets.Create(Path.Combine(directory, "samples"));
                cards.AddRange(items.Select(item => new CardWindow(item)));
                var rects = DesktopGeometry.Cards(monitor.WorkArea, scale, cards.Count);
                var foreground = Native.GetForegroundWindow();
                var show = Stopwatch.StartNew();
                rope.Present(new PixelRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, (int)(70 * scale)), false);
                for (var i = 0; i < rects.Count; i++) { Native.MoveAndGetScale(cards[i], monitor); cards[i].Present(rects[i], false); }
                var showMs = show.Elapsed.TotalMilliseconds;
                await Task.Delay(350);
                Check("show-does-not-change-foreground", Native.GetForegroundWindow() == foreground);
                // Sample the painted line itself, not an already transparent pixel of its window.
                var lineX = rects.Count >= 2 ? (rects[0].Right + rects[1].X) / 2 : monitor.WorkArea.X + 20;
                var t = (lineX - monitor.WorkArea.X) / (double)monitor.WorkArea.Width;
                var lineY = (int)Math.Round((16 + 62 * t * (1 - t)) * scale);
                var ropePixels = new RenderTargetBitmap(monitor.WorkArea.Width, (int)(70 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                ropePixels.Render((Visual)rope.Content);
                var pixel = new byte[4]; ropePixels.CopyPixels(new Int32Rect(lineX - monitor.WorkArea.X, lineY, 1, 1), pixel, 4, 0);
                Check("painted-rope-passes-to-another-process", pixel[3] > 0 && Native.WindowFromPoint(new Native.Point { X = lineX, Y = monitor.WorkArea.Y + lineY }) == backdropHandle, new { paintedPixelAlpha = pixel[3] });
                if (rects.Count >= 2)
                {
                    var gap = new Native.Point { X = (rects[0].Right + rects[1].X) / 2, Y = rects[0].Y + (int)(50 * scale) };
                    Check("gap-passes-to-another-process", Native.WindowFromPoint(gap) == backdropHandle);
                }
                var firstHandle = new WindowInteropHelper(cards[0]).Handle;
                Check("card-is-interactive-hit-target", Native.WindowFromPoint(new Native.Point { X = rects[0].X + rects[0].Width / 2, Y = rects[0].Y + (int)(60 * scale) }) == firstHandle);
                Native.GetWindowRect(firstHandle, out var actual);
                Check("physical-card-bounds", actual.Pixels == rects[0], new { expected = rects[0], actual = actual.Pixels });
                var extended = Native.GetWindowLongPtrW(firstHandle, -20).ToInt64();
                Check("card-toolwindow-noactivate", (extended & 0x08000080) == 0x08000080);
                RenderShelf(directory, rope, cards, rects, monitor.WorkArea, scale);
                var warm = Stopwatch.StartNew();
                cards[0].Dismiss(); cards[0].Present(rects[0], false);
                var warmShowMs = warm.Elapsed.TotalMilliseconds;
                foreach (var display in Native.Monitors())
                {
                    cards[0].Dismiss();
                    var displayScale = Native.MoveAndGetScale(cards[0], display);
                    var target = DesktopGeometry.Cards(display.WorkArea, displayScale, 1)[0];
                    cards[0].Present(target, false); await Task.Delay(150);
                    Native.GetWindowRect(firstHandle, out var placed);
                    Check("monitor-physical-placement", placed.Pixels == target, new { display.Bounds, display.WorkArea, dpi = Native.GetDpiForWindow(firstHandle), expected = target, actual = placed.Pixels });
                }
                rope.Dismiss(); foreach (var card in cards) card.Dismiss();
                Check("hidden-windows-are-hidden", !Native.IsWindowVisible(new WindowInteropHelper(rope).Handle) && cards.All(c => !Native.IsWindowVisible(new WindowInteropHelper(c).Handle)));
                Check("hidden-cards-pass-to-backdrop", Native.WindowFromPoint(new Native.Point { X = rects[0].X + 30, Y = rects[0].Y + 60 }) == backdropHandle);
                using (var hotkey = new HotKey(() => { }))
                {
                    using var conflict = new HotKey(() => { });
                    Check("hotkey-conflict-is-detected", !conflict.Registered, new { initialRegistration = hotkey.Registered });
                }
                backdrop.CloseMainWindow(); await Task.Delay(100);
                using var controller = new ShelfController();
                var dashboard = new Dashboard(controller, true, () => { }) { Exiting = true };
                dashboard.Show(); await Task.Delay(150);
                Render((FrameworkElement)dashboard.Content, Path.Combine(directory, "control-panel.png"));
                dashboard.Hide(); controller.Start();
                await Task.Delay(2000); // exclude first render/JIT work from the idle sample
                using var process = Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime; var interval = Stopwatch.StartNew();
                await Task.Delay(5000); process.Refresh();
                var cpuOneCore = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / interval.Elapsed.TotalMilliseconds * 100;
                results.Add(new { name = "measurements", monitorCount = Native.Monitors().Count, dpi = scale * 96, hiddenIdleCpuOneCorePercent = cpuOneCore, workingSetMiB = process.WorkingSet64 / 1048576.0, coldShelfShowCallMilliseconds = showMs, warmSingleCardShowCallMilliseconds = warmShowMs, note = "Single five-second sample after two-second warmup; CPU is one logical core basis. Show time excludes compositor/input latency. Self-test process includes diagnostic UI allocations." });
                dashboard.Close();
            }
            catch (Exception ex) { Check("diagnostics-completed", false, ex.ToString()); }
            finally
            {
                foreach (var card in cards) card.Close(); rope?.Close();
                if (backdrop is not null) { if (!backdrop.HasExited) { backdrop.CloseMainWindow(); if (!backdrop.WaitForExit(2000)) backdrop.Kill(); } backdrop.Dispose(); }
                File.WriteAllText(Path.Combine(directory, "diagnostics.json"), JsonSerializer.Serialize(new { passed, os = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(), checks = results }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = element.ActualWidth + element.Margin.Left + element.Margin.Right;
        var height = element.ActualHeight + element.Margin.Top + element.Margin.Bottom;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(BrushFor(element), null, new Rect(element.Margin.Left, element.Margin.Top, element.ActualWidth, element.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
    private static VisualBrush BrushFor(FrameworkElement element)
    {
        var offset = VisualTreeHelper.GetOffset(element);
        return new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(offset.X, offset.Y, element.ActualWidth, element.ActualHeight), Stretch = Stretch.Fill };
    }
    private static void RenderShelf(string directory, RopeWindow rope, List<CardWindow> cards, IReadOnlyList<PixelRect> rects, PixelRect work, double scale)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#E9EFE7"), null, new Rect(0, 0, work.Width / scale, 250));
            dc.DrawRectangle(BrushFor((FrameworkElement)rope.Content), null, new Rect(0, 0, work.Width / scale, 70));
            for (var i = 0; i < rects.Count; i++) dc.DrawRectangle(BrushFor((FrameworkElement)cards[i].Content), null,
                new Rect((rects[i].X - work.X) / scale, (rects[i].Y - work.Y) / scale, DesktopGeometry.CardWidth, DesktopGeometry.CardHeight));
        }
        var image = new RenderTargetBitmap((int)(work.Width / scale), 250, 96, 96, PixelFormats.Pbgra32);
        image.Render(drawing); DemoAssets.Save(image, Path.Combine(directory, "shelf.png"));
    }
}
