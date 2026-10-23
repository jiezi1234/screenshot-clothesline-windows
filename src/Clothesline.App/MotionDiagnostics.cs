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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class MotionDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { checks.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            Process? backdrop = null; CardWindow? card = null; ImageWorkspace? editor = null;
            try
            {
                var handshake = Path.Combine(directory, "backdrop-" + Guid.NewGuid().ToString("N") + ".json");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--test-backdrop"); start.ArgumentList.Add(handshake); backdrop = Process.Start(start)!;
                for (var i = 0; i < 100 && !File.Exists(handshake); i++) await Task.Delay(50);
                using var info = JsonDocument.Parse(File.ReadAllText(handshake)); var background = (nint)info.RootElement.GetProperty("Handle").GetInt64();
                File.Delete(handshake); Native.GetWindowRect(background, out var backgroundRect);
                var monitor = Native.MonitorAt(new PixelPoint(backgroundRect.Left + 40, backgroundRect.Top + 40));
                var fixtures = DemoAssets.Create(Path.Combine(directory, "fixtures")); var item = fixtures[0];
                var hash = ImageFiles.Hash(item.Path); card = new CardWindow(item); var dpi = Native.MoveAndGetScale(card, monitor);
                var rect = DesktopGeometry.Cards(monitor.WorkArea, dpi, 1)[0];
                // Avoid relying on the user's pointer position for entry/hover tests.
                rect = rect with { X = monitor.WorkArea.X + (int)(40 * dpi) };
                if (rect.Contains(Native.Cursor)) rect = rect with { X = monitor.WorkArea.Right - rect.Width - (int)(40 * dpi) };
                var point = new Native.Point { X = rect.X + rect.Width / 2, Y = rect.Y + (int)(70 * dpi) };
                object HitDetails()
                {
                    var hit = Native.WindowFromPoint(point); Native.GetWindowThreadProcessId(hit, out var pid);
                    return new { actual = hit.ToInt64(), actualPid = pid, expected = background.ToInt64(), expectedPid = backdrop.Id, backgroundVisible = Native.IsWindowVisible(background), backgroundStyle = Native.GetWindowLongPtrW(background, -20).ToInt64(), cardOpacity = card?.Opacity, cardVisible = card?.IsVisible, cardRetiring = card?.Retiring, point.X, point.Y };
                }
                var foreground = Native.GetForegroundWindow();
                Check("separate-process-backdrop-covers-test-location", Native.WindowFromPoint(point) == background, HitDetails());
                card.Present(rect, true);
                Check("entry-starts-finite-motion", card.MotionRunning);
                var inside = true;
                for (var frame = 0; frame < 9; frame++)
                {
                    await Task.Delay(65); card.UpdateLayout();
                    var view = (Viewbox)card.Content; var layout = (Grid)view.Child; var content = (Grid)layout.Children[0];
                    foreach (FrameworkElement child in content.Children)
                    {
                        var bounds = child.TransformToAncestor(view).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
                        inside &= bounds.Left >= -.25 && bounds.Top >= -.25 && bounds.Right <= view.ActualWidth + .25 && bounds.Bottom <= view.ActualHeight + .25;
                    }
                    if (frame == 1) Render(view, Path.Combine(directory, "card-entering.png"));
                }
                Check("swing-painted-elements-stay-inside-hwnd", inside);
                await Task.Delay(160);
                Check("entry-releases-animation-clocks-after-settling", !card.MotionRunning && card.Opacity == 1);
                Check("entry-keeps-foreground", Native.GetForegroundWindow() == foreground);
                card.Dismiss(); card.Present(rect, true);
                card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                Check("pointer-enter-immediately-stops-motion", !card.MotionRunning && card.Opacity == 1);
                Check("steady-card-remains-interactive", Native.WindowFromPoint(point) == new WindowInteropHelper(card).Handle);
                var closed = false; card.Closed += (_, _) => closed = true;
                card.Retire(true);
                Check("retirement-visible-but-immediately-pass-through", !closed && card.IsVisible && card.Opacity > .9 && !card.IsHitTestVisible && Native.WindowFromPoint(point) == background, HitDetails());
                await Task.Delay(260);
                Check("retirement-closes-window-and-stops-clocks", closed && !card.MotionRunning); card = null;
                Check("retirement-keeps-source-file", File.Exists(item.Path) && ImageFiles.Hash(item.Path) == hash);
                card = new CardWindow(item); Native.MoveAndGetScale(card, monitor); card.Present(rect, true);
                await Task.Delay(160); closed = false; card.Closed += (_, _) => closed = true; card.Retire(true); await Task.Delay(230);
                Check("entry-to-exit-race-does-not-leave-retiring-window", closed); card = null;
                card = new CardWindow(item); Native.MoveAndGetScale(card, monitor); card.Present(rect, true); card.Dismiss();
                Check("hide-during-entry-is-immediate-and-clock-free", !Native.IsWindowVisible(new WindowInteropHelper(card).Handle) && !card.MotionRunning);
                card.Present(rect, false); Check("disabled-motion-has-no-clocks", !card.MotionRunning && card.Opacity == 1);
                closed = false; card.Closed += (_, _) => closed = true; card.Retire(false); Check("disabled-removal-closes-immediately", closed); card = null;

                var initialWindows = app.Windows.Count; var systemMotion = true;
                using (var shelf = new ShelfController(() => systemMotion) { SuppressFullscreen = false })
                {
                    shelf.Start(false); shelf.SetItems([item], true); await Task.Delay(720);
                    var existing = app.Windows.OfType<CardWindow>().Single();
                    Action<DemoItem> failArchive = _ => throw new IOException("fixture history write failed");
                    shelf.Archived += failArchive; var rejected = false;
                    try { Descendants(existing).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } catch (IOException) { rejected = true; }
                    shelf.Archived -= failArchive;
                    Check("failed-history-write-keeps-card-visible-and-interactive", rejected && shelf.Count == 1 && existing.IsVisible && !existing.Retiring && existing.IsHitTestVisible);
                    shelf.SetItems([fixtures[1], item], false);
                    var added = app.Windows.OfType<CardWindow>().Single(w => w.Item == fixtures[1]);
                    Check("visible-shelf-animates-new-card-without-restarting-existing", added.MotionRunning && !existing.MotionRunning);
                    shelf.SetItems([item], false);
                    Check("removed-card-exits-outside-active-list", shelf.Count == 1 && shelf.RetiringCount == 1 && added.Retiring);
                    shelf.ReduceMotion = true;
                    Check("reduce-motion-stops-active-and-retiring-animations", !existing.MotionRunning && shelf.RetiringCount == 0 && !app.Windows.OfType<CardWindow>().Contains(added));
                    shelf.SetItems([fixtures[1], item], false);
                    Check("reduce-motion-prevents-new-entry", app.Windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                    shelf.SetItems([item], false); Check("reduce-motion-removal-leaves-no-retiring-window", shelf.RetiringCount == 0);
                    shelf.ReduceMotion = false; shelf.SetItems([fixtures[1], item], false);
                    systemMotion = false; shelf.RefreshMotion();
                    Check("system-animation-off-stops-current-motion", !shelf.MotionAllowed && app.Windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                    shelf.SetItems([fixtures[2], fixtures[1], item], false);
                    Check("system-animation-off-prevents-new-motion", app.Windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                    systemMotion = true; shelf.RefreshMotion(); shelf.SetItems([item], false); shelf.Hide();
                    Check("hide-closes-exiting-windows-and-hides-active-windows", shelf.RetiringCount == 0 && app.Windows.OfType<CardWindow>().All(w => !w.IsVisible && !w.MotionRunning));
                    shelf.SetItems([item], true);
                    for (var i = 0; i < 20; i++) shelf.SetItems([item with { EntryId = "burst-" + i }], false);
                    Check("rapid-list-updates-bound-retiring-window-count", shelf.RetiringCount <= shelf.DisplayCapacity);
                    shelf.Hide(); Check("rapid-hide-leaves-no-animation-clocks", shelf.RetiringCount == 0 && app.Windows.OfType<CardWindow>().All(w => !w.MotionRunning));
                }
                Check("controller-disposal-leaves-no-overlay-windows", app.Windows.Count == initialWindows);
                using (var shelf = new ShelfController(() => true) { SuppressFullscreen = false })
                using (var library = new LibraryRuntime(shelf, Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N"))))
                {
                    var entry = library.Store.Add(item.Path, Ownership.External, "Fixture", hash, LibraryRuntime.Now, 25);
                    shelf.Start(false); library.RefreshShelf(true);
                    library.ArchiveBatch(); library.RefreshShelf(false);
                    Check("archive-commits-history-before-visual-exit", library.Store.Find(entry.Id)?.State == EntryState.History && shelf.Count == 0 && shelf.RetiringCount == 1 && File.Exists(item.Path));
                    Check("last-removal-shows-empty-message-during-exit", app.Windows.OfType<RopeWindow>().Single().ShowEmptyHint);
                    shelf.Hide();
                }
                editor = new ImageWorkspace([new("fixture", item.Path, item.Title)], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), _ => new Release());
                editor.Show(); for (var i = 0; i < 100 && editor.Loading; i++) await Task.Delay(20); editor.Activate(); editor.UpdateLayout();
                var slider = Descendants(editor).OfType<Slider>().Single(); slider.Focus();
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                editor.RaiseEvent(key);
                Check("workspace-navigation-does-not-consume-slider-arrow", Keyboard.FocusedElement == slider && !key.Handled);
                var input = Descendants(editor).OfType<TextBox>().Single(); input.IsEnabled = true; input.Focus();
                key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; editor.RaiseEvent(key);
                Check("workspace-navigation-preserves-text-caret-arrows", Keyboard.FocusedElement == input && !key.Handled);
                editor.Close(); editor = null;
            }
            catch (Exception ex) { Check("motion-diagnostics-completed", false, ex.ToString()); }
            finally
            {
                card?.Close(); if (editor is not null) { editor.Document?.MarkSaved(); editor.Close(); }
                if (backdrop is not null) { if (!backdrop.HasExited) { backdrop.CloseMainWindow(); if (!backdrop.WaitForExit(2000)) backdrop.Kill(); } backdrop.Dispose(); }
                File.WriteAllText(Path.Combine(directory, "motion-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class Release : IDisposable { public void Dispose() { } }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, bounds); dc.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
