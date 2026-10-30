using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.App;

// Real HWNDs, positioned beyond every monitor. Generated fixtures only; no input or clipboard access.
internal static class ShelfEntranceDiagnostics
{
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool value, object? details = null) { passed &= value; checks.Add(new { name, passed = value, details }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            var windows = new List<OverlayWindow>();
            try
            {
                var monitors = Native.Monitors(); var monitor = monitors[0];
                var x = monitors.Max(m => m.Bounds.Right) + 2048; var y = monitors.Min(m => m.Bounds.Y);
                var foreground = Native.GetForegroundWindow();
                var fixtures = DemoAssets.Create(Path.Combine(directory, "fixtures"));
                var rope = new RopeWindow(); windows.Add(rope);
                var row = new List<(OverlayWindow Window, PixelRect Bounds)> { (rope, new(x, y, monitor.WorkArea.Width, 70)) };
                for (var i = 0; i < 12; i++)
                {
                    var text = "Generated note " + i + "\n整排同时出现，保留原卡片。";
                    var item = i < 6 ? fixtures[i % 3] with { EntryId = "photo-" + i }
                        : new DemoItem("Generated note", "", Path.Combine(directory, "note-" + i + ".txt"), TextCardImage.Create(text), "note-" + i, text);
                    var card = new CardWindow(item); windows.Add(card);
                    row.Add((card, new(x + i * 232, y + 30, 212, 190)));
                }
                PixelRect Bounds(Window window) { Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect); return rect.Pixels; }
                bool OutsideScreens() => windows.Where(w => w.IsVisible).All(w => monitors.All(m => !HoverPreviewGeometry.Overlaps(Bounds(w), m.Bounds)));
                var flash = false;
                foreach (var window in windows)
                    window.IsVisibleChanged += (_, _) => { if (window.IsVisible && window.Opacity != 0) flash = true; };
                foreach (var (window, rect) in row)
                {
                    window.PrepareShelfPresentation(rect, monitor);
                    Check("prepared-row-stays-transparent-through-window-" + (row.FindIndex(p => p.Window == window)),
                        windows.Where(w => w.IsVisible).All(w => w.Opacity == 0 && w.EntranceClock is null));
                }
                Check("preparation-finishes-native-bounds-and-content-layout", row.All(p => Bounds(p.Window) == p.Bounds
                    && ((FrameworkElement)p.Window.Content).ActualWidth > 0 && ((FrameworkElement)p.Window.Content).ActualHeight > 0),
                    row.Select(p => new { wanted = p.Bounds, actual = Bounds(p.Window), p.Window.Width, p.Window.Height,
                        contentWidth = ((FrameworkElement)p.Window.Content).ActualWidth, contentHeight = ((FrameworkElement)p.Window.Content).ActualHeight }).ToArray());
                await Task.Delay(100);
                Check("preparation-does-not-start-a-fade-even-across-dispatcher-ticks", windows.All(w => w.Opacity == 0 && w.EntrancePrepared));
                Check("preparation-never-flashes-an-opaque-window", !flash && OutsideScreens());
                // This also exercises re-preparing a row after a layout change, before it has appeared.
                ShelfPresentation.Present(row, monitor, true);
                var clock = rope.EntranceClock;
                Check("rope-images-and-notes-share-one-entrance-clock", clock is not null && windows.All(w => ReferenceEquals(w.EntranceClock, clock) && !w.EntrancePrepared));
                Check("row-stays-transparent-until-first-animation-tick", windows.All(w => w.Opacity == 0));
                var synchronized = true;
                for (var i = 0; i < 15; i++)
                {
                    await Task.Delay(25);
                    synchronized &= windows.Max(w => w.Opacity) - windows.Min(w => w.Opacity) < .000001;
                }
                Check("every-fade-sample-has-identical-progress-across-the-row", synchronized);
                async Task Until(Func<bool> ready)
                {
                    for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25);
                    if (!ready()) throw new TimeoutException("Shelf entrance did not settle");
                }
                await Until(() => windows.All(w => w.EntranceClock is null) && windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                Check("settled-entrance-releases-clocks-and-restores-opacity", windows.All(w => w.Opacity == 1 && !w.HasAnimatedProperties));
                foreach (var window in windows) window.Dismiss();
                ShelfPresentation.Present(row, monitor, true);
                windows[1].StopMotion();
                Check("stopping-one-card-does-not-stop-the-shared-clock-for-others", windows[1].Opacity == 1 && windows[1].EntranceClock is null
                    && windows.Skip(2).All(w => ReferenceEquals(w.EntranceClock, rope.EntranceClock)));
                foreach (var window in windows) window.Dismiss();
                await Task.Delay(250);
                Check("hide-during-entrance-clears-clocks-without-reappearing", windows.All(w => !w.IsVisible && !w.HasAnimatedProperties && !w.EntrancePrepared && w.EntranceClock is null));
                ShelfPresentation.Present(row, monitor, false);
                Check("reduced-motion-reveals-whole-row-without-clocks", windows.All(w => w.IsVisible && w.Opacity == 1 && w.EntranceClock is null)
                    && windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                var extra = new CardWindow(fixtures[0] with { EntryId = "extra" }); windows.Add(extra);
                var extended = row.Append(((OverlayWindow)extra, new PixelRect(x + 12 * 232, y + 30, 212, 190))).ToArray();
                ShelfPresentation.Present(extended, monitor, true);
                Check("new-card-fades-without-restarting-the-existing-row", extra.EntranceClock is not null && extra.Opacity == 0 && row.All(p => p.Window.Opacity == 1 && p.Window.EntranceClock is null));
                foreach (var window in windows) window.StopMotion();
                Check("disabling-motion-during-entry-settles-all-windows", windows.All(w => w.Opacity == 1 && w.EntranceClock is null) && windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                extra.Close(); windows.Remove(extra);
                for (var i = 0; i < 8; i++)
                {
                    foreach (var window in windows) window.Dismiss();
                    ShelfPresentation.Present(row, monitor, true);
                    foreach (var window in windows) window.Dismiss();
                }
                await Task.Delay(250);
                Check("rapid-show-hide-leaves-no-prepared-windows-or-stale-clocks", windows.All(w => !w.IsVisible && !w.EntrancePrepared && w.EntranceClock is null));
                foreach (var factor in new[] { .8, 1.0, 1.2, 1.5, 2.0 })
                {
                    var scaled = row.Select(p => (p.Window, new PixelRect(p.Bounds.X, p.Bounds.Y,
                        p.Window == rope ? p.Bounds.Width : (int)(p.Bounds.Width * factor), (int)(p.Bounds.Height * factor)))).ToArray();
                    ShelfPresentation.Present(scaled, monitor, false);
                    Check("staged-row-respects-size-" + factor, scaled.All(p => Bounds(p.Window) == p.Item2 && p.Window.Opacity == 1) && OutsideScreens(),
                        scaled.Take(2).Select(p => new { wanted = p.Item2, actual = Bounds(p.Window), p.Window.Width, p.Window.Height }).ToArray());
                    foreach (var window in windows) window.Dismiss();
                }
                rope.ShowEmptyHint = true;
                ShelfPresentation.Present(new[] { row[0] }, monitor, true);
                Check("empty-shelf-uses-the-same-prepared-entrance", rope.ShowEmptyHint && rope.EntranceClock is not null && rope.Opacity == 0);
                rope.Dismiss();
                var closed = new RopeWindow(); closed.Close(); var rejected = false;
                try { ShelfPresentation.Present(row.Append(((OverlayWindow)closed, row[0].Bounds)).ToArray(), monitor, true); }
                catch (ObjectDisposedException) { rejected = true; }
                Check("failed-preparation-hides-the-entire-uncommitted-row", rejected && windows.All(w => !w.IsVisible && !w.EntrancePrepared && w.EntranceClock is null));
                ShelfPresentation.Present(row, monitor, false);
                Check("row-can-be-reopened-after-aborted-preparation", windows.All(w => w.IsVisible && w.Opacity == 1) && OutsideScreens());
                foreach (var window in windows) window.Dismiss();
                Check("offscreen-entrance-keeps-user-foreground", Native.GetForegroundWindow() == foreground);
                foreach (var window in windows) window.Close(); windows.Clear();
                Check("entrance-disposal-releases-all-overlay-windows", !app.Windows.OfType<OverlayWindow>().Any());
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "shelf-entrance-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                foreach (var window in windows) window.Close();
                File.WriteAllText(Path.Combine(directory, "shelf-entrance-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
}
