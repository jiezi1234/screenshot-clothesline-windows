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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class ReorderMotionDiagnostics
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
                using var store = new LibraryStore(Path.Combine(output, "library-" + Guid.NewGuid().ToString("N")));
                using var shelf = new ShelfController(() => true) { SuppressFullscreen = false };
                var items = Enumerable.Range(0, 18).Select(i =>
                {
                    if (i % 2 == 0) return assets[i % assets.Count] with { EntryId = "card-" + i };
                    var path = Path.Combine(store.ManagedFolder, "note-" + i + ".txt"); var text = "Generated note " + i; File.WriteAllText(path, text);
                    return new DemoItem(text, "Mixed reorder fixture", path, TextCardImage.Create(text), "card-" + i, text);
                }).ToArray();
                foreach (var item in items) store.Add(item.Path, Ownership.External, "Fixture", "fixture", LibraryRuntime.Now, 100, id: item.EntryId);
                store.MoveShelfItems(items.Select(i => i.EntryId!).ToArray(), null);
                var writes = 0;
                shelf.ReorderRequested = (id, before) => { if (!store.MoveShelfItems([id], before)) throw new IOException("stale fixture"); writes++; };
                shelf.SetItems(items, true);
                await Task.Delay(720);
                Check("interleaved-fixture-keeps-global-persisted-order", shelf.OrderedIds.SequenceEqual(items.Select(i => i.EntryId))
                    && store.Entries(EntryState.Shelf).Select(e => e.Id).SequenceEqual(shelf.OrderedIds));
                var target = app.Windows.OfType<ShelfDropWindow>().Single();
                CardWindow Card(string id) => app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == id);
                PixelRect Bounds(CardWindow card) { Native.GetWindowRect(new WindowInteropHelper(card).Handle, out var rect); return rect.Pixels; }
                PixelPoint Grip(CardWindow card) { var rect = Bounds(card); return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2); }
                var source = Card(items[0].EntryId!); var left = Card(items[1].EntryId!); var right = Card(items[2].EntryId!);
                var a = Bounds(left); var b = Bounds(right); var gap = new PixelPoint((a.Right + b.X) / 2, a.Y + a.Height / 2);
                var sourceBounds = Bounds(source); var leftBounds = Bounds(left); var originalOrder = shelf.OrderedIds;
                var gesture = shelf.BeginLocalReorder(source, Grip(source), false);
                shelf.UpdateLocalReorder(gap, true, false, 20);
                Check("internal-gap-neighbors-lean-outward-around-pins", target.PreviewState(left.Item.EntryId!).TargetAngle == 5 && target.PreviewState(right.Item.EntryId!).TargetAngle == -5);
                Check("preview-does-not-write-or-reorder-library", writes == 0 && shelf.OrderedIds.SequenceEqual(originalOrder) && store.Entries(EntryState.Shelf).Select(e => e.Id).SequenceEqual(originalOrder));
                var placements = target.PlacementCount; var snapshots = target.SnapshotCount;
                var times = new List<double>();
                for (var i = 0; i < 240; i++)
                {
                    var watch = Stopwatch.StartNew(); shelf.UpdateLocalReorder(gap with { X = gap.X + i % 10 }, true, false, 30 + i); watch.Stop(); times.Add(watch.Elapsed.TotalMilliseconds);
                }
                metrics.Add(new { name = "240-preview-updates", meanMs = times.Average(), p95Ms = times.Order().ElementAt(227), maxMs = times.Max() });
                Check("steady-drag-reuses-hwnd-placement-and-thumbnails", target.PlacementCount == placements && target.SnapshotCount == snapshots);
                Check("preview-does-not-move-real-card-hwnds", Bounds(source) == sourceBounds && Bounds(left) == leftBounds);
                var heldX = target.PreviewState(source.Item.EntryId!).X;
                shelf.UpdateLocalReorder(gap with { X = gap.X + 29 }, true, false, 300);
                var dpi = Native.GetDpiForWindow(new WindowInteropHelper(target).Handle) / 96.0;
                Check("held-image-follows-pointer-without-easing-lag", Math.Abs(target.PreviewState(source.Item.EntryId!).X - heldX - 20 / dpi) < .1);
                await Task.Delay(180);
                Check("neighbor-tilt-settles-and-releases-clocks", !target.PreviewAnimating && Math.Abs(target.PreviewState(left.Item.EntryId!).Angle - 5) < .01);
                var canvas = ((Grid)target.Content).Children.OfType<Canvas>().Single();
                var bitmap = (BitmapSource)canvas.Children.OfType<Image>().First().Source;
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Check("preview-snapshots-contain-visible-card-pixels", pixels.Where((_, i) => i % 4 == 3).Count(v => v != 0) > 1000);
                Capture(target, Path.Combine(output, "internal-gap.png"));
                shelf.UpdateLocalReorder(gap, false, false, 500);
                Check("release-commits-once-and-animates-landing", writes == 1 && !gesture.IsCompleted && target.PreviewAnimating);
                Check("release-completes-and-cleans-preview", await gesture.WaitAsync(TimeSpan.FromSeconds(3)) == ShelfController.ReorderExit.Committed && !shelf.Busy && !target.IsVisible && target.PreviewCount == 0);
                Check("committed-order-matches-library-and-visible-cards", shelf.OrderedIds[1] == source.Item.EntryId && shelf.OrderedIds.SequenceEqual(store.Entries(EntryState.Shelf).Select(e => e.Id)) && app.Windows.OfType<CardWindow>().All(c => c.Opacity == 1));
                var committed = shelf.OrderedIds;
                gesture = shelf.BeginLocalReorder(source, Grip(source), false); shelf.UpdateLocalReorder(new PixelPoint(sourceBounds.X, gap.Y), true, false, 1000);
                shelf.UpdateLocalReorder(gap, true, true, 1100);
                Check("escape-restores-order-and-opacity", await gesture == ShelfController.ReorderExit.Cancelled && shelf.OrderedIds.SequenceEqual(committed) && writes == 1 && app.Windows.OfType<CardWindow>().All(c => c.Opacity == 1));
                gesture = shelf.BeginLocalReorder(source, Grip(source), false); shelf.UpdateLocalReorder(gap with { Y = a.Bottom + 100 }, true, false, 1200);
                Check("leaving-rope-hands-off-to-native-copy-without-commit", await gesture == ShelfController.ReorderExit.Export && writes == 1 && !target.IsVisible && target.PreviewCount == 0);
                shelf.UpdateDropFeedback(gap, 1400); await Task.Delay(180);
                // The source is now between the original two neighbors; inspect the pair under this cursor.
                var visible = app.Windows.OfType<CardWindow>().Where(c => c.IsVisible).OrderBy(c => Bounds(c).X).ToArray();
                var before = visible.Last(c => Bounds(c).X + Bounds(c).Width / 2 < gap.X);
                var after = visible.First(c => Bounds(c).X + Bounds(c).Width / 2 > gap.X);
                Check("external-image-gap-also-tilts-both-neighbors", target.PreviewState(before.Item.EntryId!).TargetAngle == 5 && target.PreviewState(after.Item.EntryId!).TargetAngle == -5);
                Capture(target, Path.Combine(output, "external-gap.png"));
                Check("incoming-preview-defers-library-refresh", shelf.Busy);
                for (var i = 0; i < 12; i++) { shelf.UpdateDropFeedback(gap with { X = gap.X + (i % 2 == 0 ? -a.Width : a.Width) }, 1401 + i); await Task.Delay(12); }
                shelf.UpdateDropFeedback(gap, 1415); await Task.Delay(180);
                Check("rapid-reversals-settle-at-latest-gap-without-stale-clocks", !target.PreviewAnimating && target.PreviewState(before.Item.EntryId!).Angle == 5 && target.PreviewState(after.Item.EntryId!).Angle == -5);
                shelf.ReduceMotion = true;
                Check("reduce-motion-updates-existing-incoming-preview", !target.PreviewAnimating && target.PreviewState(before.Item.EntryId!).Angle == 0 && app.Windows.OfType<CardWindow>().Where(c => c.IsVisible).All(c => c.Opacity == 0));
                shelf.ReduceMotion = false;
                shelf.Hide(); Check("hide-restores-all-real-cards", !target.IsVisible && app.Windows.OfType<CardWindow>().All(c => c.Opacity == 1));
                shelf.Show(); source = Card(shelf.OrderedIds[0]);
                gesture = shelf.BeginLocalReorder(source, Grip(source), false);
                shelf.Hide(); Check("hide-cancels-active-gesture-immediately", await gesture == ShelfController.ReorderExit.Cancelled && !shelf.Busy && target.PreviewCount == 0);
                shelf.Show(); gesture = shelf.BeginLocalReorder(source, Grip(source), true);
                Check("live-input-session-captures-on-stationary-overlay", target.IsMouseCaptured);
                shelf.Hide(); await gesture;
                Check("cancel-releases-capture-and-render-listener", !target.IsMouseCaptured && !shelf.Busy);
                shelf.Show();
                gesture = shelf.BeginLocalReorder(source, gap with { Y = a.Bottom + 100 }, true);
                Check("fast-first-move-outside-band-does-not-capture-after-handoff", await gesture == ShelfController.ReorderExit.Export && !target.IsMouseCaptured && !target.IsVisible && !shelf.Busy);
                shelf.Show(); shelf.ReduceMotion = true;
                gesture = shelf.BeginLocalReorder(source, Grip(source), false); shelf.UpdateLocalReorder(gap, true, false, 1500);
                Check("reduce-motion-has-no-tilt-or-animation-clocks", !target.PreviewAnimating && canvas.Children.OfType<Image>().All(i => ((RotateTransform)((TransformGroup)i.RenderTransform).Children[0]).Angle == 0));
                shelf.UpdateLocalReorder(gap, false, false, 1600); await gesture;
                shelf.ReduceMotion = false;
                var saveReorder = shelf.ReorderRequested; var beforeFailure = shelf.OrderedIds;
                source = Card(shelf.OrderedIds[0]); shelf.ReorderRequested = (_, _) => throw new IOException("fixture write failed");
                gesture = shelf.BeginLocalReorder(source, Grip(source), false); shelf.UpdateLocalReorder(gap, false, false, 1700);
                var rejected = false; try { await gesture; } catch (IOException) { rejected = true; }
                shelf.ReorderRequested = saveReorder;
                Check("failed-write-cleans-up-with-original-order", rejected && shelf.OrderedIds.SequenceEqual(beforeFailure) && !shelf.Busy && !target.IsVisible && app.Windows.OfType<CardWindow>().All(c => c.Opacity == 1));
                source = Card(shelf.OrderedIds[0]); gesture = shelf.BeginLocalReorder(source, Grip(source), false);
                var monitor = Native.MonitorAt(gap); var edge = new PixelPoint(monitor.WorkArea.Right - 20, gap.Y);
                shelf.UpdateLocalReorder(edge, true, false, 2000); shelf.UpdateLocalReorder(edge, true, false, 2650);
                Check("cross-page-preview-keeps-held-image-visible", !source.IsVisible && target.PreviewCount > 0);
                var returnEdge = edge with { X = monitor.WorkArea.X + 20 };
                shelf.UpdateLocalReorder(returnEdge, true, false, 2700); shelf.UpdateLocalReorder(returnEdge, true, false, 3350);
                Check("returning-to-page-stops-real-card-entry-animations", app.Windows.OfType<CardWindow>().Where(c => c.IsVisible).All(c => c.Opacity == 0 && !c.MotionRunning));
                shelf.UpdateLocalReorder(edge, true, false, 3400); shelf.UpdateLocalReorder(edge, true, false, 4050);
                shelf.UpdateLocalReorder(edge, false, false, 4100); await gesture;
                Check("cross-page-drop-persists-and-shows-moved-card", Array.IndexOf(shelf.OrderedIds, source.Item.EntryId!) >= shelf.DisplayCapacity && source.IsVisible && shelf.OrderedIds.SequenceEqual(store.Entries(EntryState.Shelf).Select(e => e.Id)));
                gesture = shelf.BeginLocalReorder(source, Grip(source), false);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                Check("lock-cancels-without-invisible-cards", await gesture == ShelfController.ReorderExit.Cancelled && !target.IsVisible && !shelf.Busy && app.Windows.OfType<CardWindow>().All(c => c.Opacity == 1));
                Check("gesture-tests-preserve-original-files", assets.Select(a => ImageFiles.Hash(a.Path)).SequenceEqual(hashes));
                using var closingShelf = new ShelfController(() => true) { SuppressFullscreen = false };
                closingShelf.SetItems([items[0] with { EntryId = "disposal-fixture" }], true);
                var closingCard = Card("disposal-fixture");
                var closingGesture = closingShelf.BeginLocalReorder(closingCard, Grip(closingCard), false);
                closingShelf.Dispose();
                Check("disposal-resolves-active-gesture-and-closes-preview", await closingGesture == ShelfController.ReorderExit.Cancelled && !closingShelf.Busy && app.Windows.OfType<ShelfDropWindow>().Count() == 1);
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "reorder-motion-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "reorder-motion-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks, metrics }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static void Capture(ShelfDropWindow window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); bitmap.Render(content); DemoAssets.Save(bitmap, path);
    }
}
