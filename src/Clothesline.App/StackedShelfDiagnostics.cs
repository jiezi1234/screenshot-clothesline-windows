using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class StackedShelfDiagnostics
{
    internal static int Run(Application app, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<object>(); var metrics = new List<object>(); var passed = true;
        void Check(string name, bool ok) { passed &= ok; checks.Add(new { name, passed = ok }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                var assets = DemoAssets.Create(Path.Combine(output, "fixtures-" + Guid.NewGuid().ToString("N")));
                var hashes = assets.Select(a => ImageFiles.Hash(a.Path)).ToArray();
                var loads = 0;
                using var shelf = new ShelfController(() => true, (_, _) => { loads++; return Task.FromResult(assets[0].Image); }) { SuppressFullscreen = false, ReduceMotion = true };
                shelf.SetItems(assets.Take(1).Select((a, i) => a with { EntryId = "sparse-" + i }), true);
                CardWindow Card(string id) => app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == id);
                PixelRect Bounds(Window w) { Native.GetWindowRect(new WindowInteropHelper(w).Handle, out var rect); return rect.Pixels; }
                nint Hit(PixelPoint p) => Native.WindowFromPoint(new Native.Point { X = p.X, Y = p.Y });
                PixelPoint Inside(PixelRect r) => new(r.X + r.Width / 4, r.Y + r.Height / 2);
                var sparse = Card("sparse-0"); var sparseBounds = Bounds(sparse);
                shelf.TrackHoverPreview(0, Inside(sparseBounds), false, false);
                Check("sparse-row-keeps-normal-card-bounds", shelf.EmphasizedCard is null && Bounds(sparse) == sparseBounds);
                var items = Enumerable.Range(0, shelf.ZoneDisplayCapacity + 3).Select(i => assets[i % assets.Count] with { EntryId = "stack-" + i }).ToArray();
                shelf.SetItems(items, true);
                var row = app.Windows.OfType<CardWindow>().Where(c => c.IsVisible).OrderBy(c => Bounds(c).X).ToArray();
                var homes = row.Select(Bounds).ToArray(); var source = row[1]; var sourceHome = homes[1]; var point = Inside(sourceHome);
                var work = Native.MonitorAt(point).WorkArea;
                var outside = new PixelPoint(work.X + 2, work.Bottom - 2);
                var preview = app.Windows.OfType<HoverPreviewWindow>().Single();
                var target = app.Windows.OfType<ShelfDropWindow>().Single();
                Check("overflow-uses-overlap-with-visible-neighbor-strips", row.Length == shelf.ZoneDisplayCapacity && DesktopGeometry.Stacked(homes) && homes.Zip(homes.Skip(1)).All(p => p.Second.X - p.First.X >= .6 * p.First.Width));
                Check("normal-layer-order-exposes-each-card", row.Select((c, i) => Hit(Inside(homes[i])) == new WindowInteropHelper(c).Handle).All(ok => ok));
                Capture(row, null, Path.Combine(output, "stacked-row.png"));
                var foreground = Native.GetForegroundWindow(); var order = shelf.OrderedIds;
                shelf.TrackHoverPreview(1000, point, false, false);
                var expanded = Bounds(source);
                Check("hover-expands-and-lifts-only-the-hit-card", shelf.EmphasizedCard == source && expanded == DesktopGeometry.EmphasizedCard(sourceHome, ShelfZones.Area(work, Native.GetDpiForWindow(new WindowInteropHelper(source).Handle) / 96.0, false)) && row.Where(c => c != source).All(c => Bounds(c) == homes[Array.IndexOf(row, c)]));
                Check("hovered-card-comes-in-front-of-overlapping-neighbor", Hit(new PixelPoint(sourceHome.Right - 20, point.Y)) == new WindowInteropHelper(source).Handle);
                Check("hover-does-not-activate-or-reorder", Native.GetForegroundWindow() == foreground && shelf.OrderedIds.SequenceEqual(order) && !shelf.Busy);
                Check("pop-precedes-large-preview", !preview.IsVisible && loads == 0);
                shelf.TrackHoverPreview(1399, point, false, false);
                Check("same-pointer-does-not-switch-card-or-restart-pop", shelf.EmphasizedCard == source && Bounds(source) == expanded && loads == 0);
                shelf.TrackHoverPreview(1400, point, false, false);
                Check("continued-hover-retains-second-stage-preview", preview.IsVisible && loads == 1);
                Check("large-preview-avoids-expanded-card-and-entire-row", row.All(c => !HoverPreviewGeometry.Overlaps(Bounds(preview), Bounds(c))));
                Capture(row, source, Path.Combine(output, "stacked-pop.png"));
                shelf.TrackHoverPreview(1500, point, false, true);
                Check("escape-dismisses-large-preview-but-keeps-card-accessible", !preview.IsVisible && shelf.EmphasizedCard == source && Bounds(source) == expanded);
                shelf.TrackHoverPreview(1600, point, true, false);
                Check("pressed-card-does-not-shift-before-click-or-drag", shelf.EmphasizedCard == source && Bounds(source) == expanded && !preview.IsVisible);
                var rightPoint = new PixelPoint((expanded.Right + homes[3].X) / 2, point.Y);
                Check("right-neighbor-still-has-a-native-hit-region", Hit(rightPoint) == new WindowInteropHelper(row[2]).Handle);
                shelf.TrackHoverPreview(1700, rightPoint, false, false);
                Check("moving-to-right-neighbor-restores-previous-card", shelf.EmphasizedCard == row[2] && Bounds(source) == sourceHome);
                shelf.TrackHoverPreview(1800, point, false, false);
                Check("moving-back-left-is-stable", shelf.EmphasizedCard == source && Bounds(row[2]) == homes[2]);
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 120; i++) { shelf.TrackHoverPreview(1900 + i * 2, rightPoint, false, false); shelf.TrackHoverPreview(1901 + i * 2, point, false, false); }
                watch.Stop(); metrics.Add(new { name = "240-hover-switches", meanMs = watch.Elapsed.TotalMilliseconds / 240 });
                Check("rapid-hover-switches-leave-one-pop-and-no-loads", shelf.EmphasizedCard == source && row.Count(c => Bounds(c) != homes[Array.IndexOf(row, c)]) == 1 && loads == 1);
                shelf.TrackHoverPreview(2300, outside, false, false);
                Check("leaving-restores-bounds-and-original-layer-order", shelf.EmphasizedCard is null && row.Select((c, i) => Bounds(c) == homes[i] && Hit(Inside(homes[i])) == new WindowInteropHelper(c).Handle).All(ok => ok));
                shelf.TrackHoverPreview(2400, point, false, false);
                var blocker = new OverlayWindow("Stack fixture occluder", false) { Content = new Border { Background = Brushes.AliceBlue } };
                try
                {
                    blocker.Present(Bounds(source), false); shelf.TrackHoverPreview(2500, point, false, false);
                    Check("covered-card-resets-without-raising-row-over-occluder", shelf.EmphasizedCard is null && Hit(point) == new WindowInteropHelper(blocker).Handle && !preview.IsVisible);
                }
                finally { blocker.Close(); }
                shelf.TrackHoverPreview(2600, point, false, false);
                var grip = (int)Math.Round((point.X - Bounds(source).X) * sourceHome.Width / (double)Bounds(source).Width);
                var gesture = shelf.BeginLocalReorder(source, point, false);
                var dpi = Native.GetDpiForWindow(new WindowInteropHelper(target).Handle) / 96.0;
                Check("drag-from-pop-restores-normal-size-and-preserves-grip", shelf.EmphasizedCard is null && Bounds(source) == sourceHome && Math.Abs(target.PreviewState(source.Item.EntryId!).X - (point.X - grip - Bounds(target).X) / dpi) < 1);
                var canvas = ((Grid)target.Content).Children.OfType<Canvas>().Single();
                Check("held-snapshot-is-above-all-stacked-neighbors", canvas.Children.OfType<Image>().Count(i => Panel.GetZIndex(i) == int.MaxValue) == 1 && canvas.Children.OfType<Image>().Select(Panel.GetZIndex).Distinct().Count() == canvas.Children.Count);
                shelf.UpdateLocalReorder(point, true, true, 2700); await gesture;
                Check("cancel-from-pop-preserves-order-and-opacity", shelf.OrderedIds.SequenceEqual(order) && !shelf.Busy && row.All(c => c.Opacity == 1));
                shelf.TrackHoverPreview(2800, point, false, false); shelf.UpdateDropFeedback(point, 2900);
                Check("incoming-image-preview-clears-pop-before-snapshot", shelf.EmphasizedCard is null && Bounds(source) == sourceHome && row.All(c => c.Opacity == 0));
                shelf.Hide(); shelf.Show();
                shelf.ReduceMotion = false; shelf.TrackHoverPreview(3000, point, false, false);
                Check("pop-has-finite-animation-when-motion-enabled", source.MotionRunning);
                await Task.Delay(220);
                Check("pop-releases-animation-clocks", !source.MotionRunning);
                shelf.TrackHoverPreview(3300, rightPoint, false, false); shelf.ReduceMotion = true;
                Check("reduce-motion-stops-current-pop-with-emphasis-retained", shelf.EmphasizedCard == row[2] && !row[2].MotionRunning);
                shelf.SetCardSize(120);
                Check("size-change-clears-emphasis-and-preview", shelf.EmphasizedCard is null && !preview.IsVisible);
                shelf.SetCardSize(100);
                shelf.SetItems(items.Take(shelf.ZoneDisplayCapacity + 1), true);
                source = Card(items[0].EntryId!);
                source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                var last = app.Windows.OfType<CardWindow>().Single(c => c.IsVisible); var lastHome = Bounds(last);
                shelf.TrackHoverPreview(3350, Inside(lastHome), false, false);
                Check("single-card-last-page-does-not-use-stack-pop", shelf.EmphasizedCard is null && Bounds(last) == lastHome);
                shelf.SetItems(items, true);
                var first = Card(items[0].EntryId!); shelf.TrackHoverPreview(3360, Inside(Bounds(first)), false, false);
                Check("first-card-pop-stays-inside-work-area", shelf.EmphasizedCard == first && Bounds(first).X >= work.X);
                shelf.TrackHoverPreview(3361, outside, false, false);
                var end = Card(items[shelf.ZoneDisplayCapacity - 1].EntryId!); shelf.TrackHoverPreview(3370, Inside(Bounds(end)), false, false);
                Check("last-visible-card-pop-stays-inside-work-area", shelf.EmphasizedCard == end && Bounds(end).Right <= work.Right);
                var videoItems = items.Select((item, i) => i == 1 ? item with { Path = Path.ChangeExtension(item.Path, ".mp4"), EntryId = "stack-video" } : item).ToArray();
                shelf.SetItems(videoItems, true); var video = Card("stack-video"); var beforeVideo = loads;
                shelf.TrackHoverPreview(3380, Inside(Bounds(video)), false, false);
                shelf.TrackHoverPreview(3980, Inside(Bounds(video)), false, false);
                Check("video-card-is-accessible-in-stack-without-image-decoding", shelf.EmphasizedCard == video && !preview.IsVisible && loads == beforeVideo);
                shelf.SetItems(items, true);
                source = Card(items[0].EntryId!); point = Inside(Bounds(source)); shelf.TrackHoverPreview(3400, point, false, false);
                shelf.SetItems(items.Skip(1), false);
                Check("removing-popped-item-clears-state", shelf.EmphasizedCard is null && !preview.IsVisible);
                shelf.SetItems(items, true); source = Card(items[0].EntryId!); point = Inside(Bounds(source));
                shelf.TrackHoverPreview(3500, point, false, false);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                Check("lock-clears-pop-and-all-card-windows", shelf.EmphasizedCard is null && !shelf.Visible && !app.Windows.OfType<CardWindow>().Any(c => c.IsVisible));
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionUnlock); shelf.Show();
                shelf.TrackHoverPreview(3600, point, false, false); shelf.Dispose();
                Check("dispose-closes-all-overlay-windows", !app.Windows.OfType<OverlayWindow>().Any());
                Check("stacking-and-hover-preserve-all-original-files", assets.Select(a => ImageFiles.Hash(a.Path)).SequenceEqual(hashes));
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "stacked-shelf-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "stacked-shelf-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks, metrics }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    // Render only generated test-card content; never capture the user's desktop.
    private static void Capture(CardWindow[] cards, CardWindow? front, string path)
    {
        var positions = cards.Select(c => { Native.GetWindowRect(new WindowInteropHelper(c).Handle, out var r); return r.Pixels; }).ToArray();
        var left = positions.Min(r => r.X) - 10; var top = positions.Min(r => r.Y) - 10;
        var width = positions.Max(r => r.Right) - left + 10; var height = positions.Max(r => r.Bottom) - top + 10;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, new Rect(0, 0, width, height));
            foreach (var card in cards.OrderBy(c => c == front ? 1 : 0))
            {
                card.UpdateLayout(); var rect = positions[Array.IndexOf(cards, card)]; var scale = Native.GetDpiForWindow(new WindowInteropHelper(card).Handle) / 96.0;
                var bitmap = new RenderTargetBitmap(rect.Width, rect.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render((FrameworkElement)card.Content);
                dc.DrawImage(bitmap, new Rect(rect.X - left, rect.Y - top, rect.Width, rect.Height));
            }
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); DemoAssets.Save(image, path);
    }
}
