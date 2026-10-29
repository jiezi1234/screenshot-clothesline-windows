using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class DragDropDiagnostics
{
    // Exercise the real routed handlers with OLE-compatible data without moving the user's pointer.
    private static DragEventArgs Event(Window target, IDataObject data, RoutedEvent routed, DragDropEffects allowed = DragDropEffects.Copy)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, [data, DragDropKeyStates.LeftMouseButton, allowed, target, new Point(10, 10)], null)!;
        args.RoutedEvent = routed; target.RaiseEvent(args); return args;
    }
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { passed &= ok; checks.Add(new { name, passed = ok }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                var root = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N"));
                var fixtures = DemoAssets.Create(Path.Combine(root, "images"));
                var initialHashes = fixtures.Select(i => ImageFiles.Hash(i.Path)).ToArray();
                var paths = fixtures.Select(i => i.Path).ToArray();
                var textData = new DataObject(DataFormats.Text, paths[0]);
                var mixed = new DataObject(DataFormats.FileDrop, new[] { paths[0], paths[0].ToUpperInvariant(), "relative.png", Path.Combine(root, "movie.mp4"), paths[1] });
                Check("drop-parser-only-accepts-file-drop", ShelfController.ImageDropPaths(textData).Length == 0);
                Check("drop-parser-filters-and-deduplicates-images", ShelfController.ImageDropPaths(mixed).SequenceEqual(paths.Take(2)));
                Check("drop-parser-rejects-over-100-files", ShelfController.ImageDropPaths(new DataObject(DataFormats.FileDrop, Enumerable.Repeat(paths[0], 101).ToArray())).Length == 0);
                using (var shelf = new ShelfController { SuppressFullscreen = false, ReduceMotion = true })
                using (var runtime = new LibraryRuntime(shelf, Path.Combine(root, "library")))
                {
                    runtime.ApplySettings(new LibrarySettings { ClipboardEnabled = false, Capacity = 25, Paused = true, ShowNew = false, SuppressFullscreen = false, ReduceMotion = true });
                    shelf.Start(false);
                    var target = app.Windows.OfType<ShelfDropWindow>().Single();
                    var monitor = Native.MonitorAt(Native.Cursor);
                    var near = new PixelPoint(monitor.WorkArea.X + monitor.WorkArea.Width / 2, monitor.WorkArea.Y + 20);
                    shelf.TrackDropTarget(0, near, monitor, true);
                    Check("near-edge-does-not-intercept-immediately", !target.IsVisible);
                    var foreground = Native.GetForegroundWindow();
                    shelf.TrackDropTarget(200, near, monitor, true);
                    Check("held-drag-near-hidden-shelf-offers-target-without-opening-rope", target.IsVisible && !shelf.Visible);
                    var style = Native.GetWindowLongPtrW(new WindowInteropHelper(target).Handle, -20).ToInt64();
                    Check("drop-target-keeps-foreground-and-is-nonactivating", (style & 0x08000000L) != 0 && Native.GetForegroundWindow() == foreground);
                    var rejected = Event(target, textData, DragDrop.PreviewDragOverEvent);
                    Check("invalid-payload-does-not-open-shelf", rejected.Effects == DragDropEffects.None && !shelf.Visible);
                    var data = new DataObject(DataFormats.FileDrop, paths.Take(2).ToArray());
                    rejected = Event(target, data, DragDrop.PreviewDropEvent, DragDropEffects.Move);
                    Check("move-only-drop-is-rejected", rejected.Effects == DragDropEffects.None && runtime.Store.Entries().Count == 0);
                    shelf.TrackDropTarget(300, near, monitor, false);
                    Check("release-restores-click-through", !target.IsVisible);
                    var enter = Event(target, data, DragDrop.PreviewDragEnterEvent);
                    Check("valid-file-drag-expands-empty-shelf-with-copy-effect", enter.Effects == DragDropEffects.Copy && shelf.Visible && target.IsVisible);
                    var drop = Event(target, data, DragDrop.PreviewDropEvent);
                    Check("drop-returns-copy-before-async-decoding", drop.Effects == DragDropEffects.Copy);
                    for (var i = 0; i < 600 && shelf.Busy; i++) await Task.Delay(20);
                    var entries = runtime.Store.Entries(EntryState.Shelf);
                    Check("drop-hangs-images-in-payload-order-even-when-collection-paused", entries.Select(e => e.Path).SequenceEqual(paths.Take(2)) && shelf.Count == 2);
                    Check("drop-does-not-create-managed-copies", entries.All(e => e.Ownership == Ownership.External) && !Directory.EnumerateFiles(runtime.Store.ManagedFolder).Any());
                    await runtime.ImportDroppedImages([paths[2]], entries[1].Id);
                    Check("drop-inserts-before-selected-card", runtime.Store.Entries(EntryState.Shelf).Select(e => e.Path).SequenceEqual(new[] { paths[0], paths[2], paths[1] }));
                    await runtime.ImportDroppedImages([paths[0]], null);
                    Check("already-hung-file-moves-without-duplicate", runtime.Store.Entries().Count == 3 && runtime.Store.Entries(EntryState.Shelf).Last().Path == paths[0]);
                    var last = runtime.Store.Entries(EntryState.Shelf).Last();
                    runtime.Store.Archive(last.Id, LibraryRuntime.Now); runtime.RefreshShelf(false);
                    await runtime.ImportDroppedImages([paths[0]], runtime.Store.Entries(EntryState.Shelf)[0].Id);
                    Check("dragging-history-image-restores-existing-record", runtime.Store.Entries().Count == 3 && runtime.Store.Entries(EntryState.Shelf)[0].Id == last.Id);
                    // Wait for asynchronous thumbnails so a card cannot be replaced during this check.
                    for (var i = 0; i < 200 && app.Windows.OfType<CardWindow>().Any(c => c.Item.Subtitle == T("正在读取图片…")); i++) await Task.Delay(20);
                    var first = app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == last.Id);
                    var beforeArrival = runtime.Store.Find(last.Id)!.AddedAt;
                    shelf.MoveCardBefore(first, null);
                    Check("internal-reorder-keeps-card-instance-and-syncs-database", app.Windows.OfType<CardWindow>().Contains(first) && shelf.OrderedIds.SequenceEqual(runtime.Store.Entries(EntryState.Shelf).Select(e => e.Id)) && shelf.OrderedIds[^1] == last.Id);
                    Check("internal-reorder-keeps-original-arrival-time", runtime.Store.Find(last.Id)!.AddedAt == beforeArrival);
                    var nativePayloadSeen = false; uint offered = 0;
                    shelf.DragCard(first, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject native, nint source, uint allowed, out uint effect) =>
                    {
                        offered = allowed;
                        var payload = new DataObject(native);
                        nativePayloadSeen = ShelfController.ImageDropPaths(payload).SingleOrDefault() == first.Item.Path;
                        Event(target, payload, DragDrop.PreviewDragEnterEvent);
                        var handled = Event(target, payload, DragDrop.PreviewDropEvent);
                        effect = (uint)handled.Effects; return 0x40100;
                    });
                    Check("native-shell-data-reaches-wpf-target-with-copy-only", nativePayloadSeen && offered == 1 && !target.IsVisible && shelf.Count == 3);
                    var order = shelf.OrderedIds;
                    shelf.DragCard(first, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject native, nint source, uint allowed, out uint effect) => { effect = 0; return 0x40101; });
                    Check("cancelled-native-drag-keeps-order-and-cleans-target", shelf.OrderedIds.SequenceEqual(order) && !target.IsVisible);
                    var originalReorder = shelf.ReorderRequested; shelf.ReorderRequested = (_, _) => throw new IOException("fixture write failure");
                    var failedWrite = false;
                    try { shelf.MoveCardBefore(first, shelf.OrderedIds.First(id => id != first.Item.EntryId)); } catch (IOException) { failedWrite = true; }
                    shelf.ReorderRequested = originalReorder;
                    Check("failed-database-write-keeps-display-order", failedWrite && shelf.OrderedIds.SequenceEqual(order));
                    var corrupt = Path.Combine(root, "corrupt.png"); File.WriteAllText(corrupt, "invalid image");
                    await runtime.ImportDroppedImages([corrupt, paths[1]], null);
                    Check("bad-image-does-not-stop-following-valid-file", runtime.Store.Entries().Count == 3 && runtime.Store.Entries(EntryState.Shelf).Last().Path == paths[1] && File.Exists(corrupt));
                    shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                    var lockedDrop = Event(target, data, DragDrop.PreviewDragOverEvent);
                    Check("session-lock-hides-and-rejects-drop", !target.IsVisible && !shelf.Visible && lockedDrop.Effects == DragDropEffects.None);
                    Check("all-original-images-still-present-and-unchanged", paths.Select(ImageFiles.Hash).SequenceEqual(initialHashes));
                }
                Check("disposal-closes-all-drop-and-card-windows", !app.Windows.OfType<ShelfDropWindow>().Any() && !app.Windows.OfType<CardWindow>().Any());
                using (var shelf = new ShelfController { SuppressFullscreen = false, ReduceMotion = true })
                using (var runtime = new LibraryRuntime(shelf, Path.Combine(root, "overflow")))
                {
                    runtime.ApplySettings(new LibrarySettings { ClipboardEnabled = false, Capacity = 25, Paused = true, ShowNew = false, SuppressFullscreen = false, ReduceMotion = true }); shelf.Start(false);
                    var batch = Enumerable.Range(0, 27).Select(i => Path.Combine(root, $"batch-{i:D2}.png")).ToArray();
                    foreach (var path in batch) File.Copy(paths[0], path);
                    await runtime.ImportDroppedImages(batch, null);
                    Check("overflow-drop-keeps-last-25-in-payload-order", runtime.Store.Entries(EntryState.Shelf).Select(e => e.Path).SequenceEqual(batch.Skip(2)));
                    Check("overflow-goes-to-history-with-all-files-kept", runtime.Store.Entries(EntryState.History).Count == 2 && batch.All(File.Exists));
                    var monitor = Native.MonitorAt(Native.Cursor);
                    var right = new PixelPoint(monitor.WorkArea.Right - 20, monitor.WorkArea.Y + 70);
                    var left = new PixelPoint(monitor.WorkArea.X + 20, monitor.WorkArea.Y + 70);
                    shelf.UpdateDropFeedback(right, 0);
                    string FirstVisible() => shelf.OrderedIds.First(id => app.Windows.OfType<CardWindow>().Any(c => c.IsVisible && c.Item.EntryId == id));
                    var firstPage = FirstVisible(); shelf.UpdateDropFeedback(right, 649);
                    Check("drag-edge-does-not-flip-page-before-dwell", FirstVisible() == firstPage);
                    shelf.UpdateDropFeedback(right, 650);
                    Check("drag-edge-dwell-advances-page", FirstVisible() == shelf.OrderedIds[shelf.DisplayCapacity]);
                    shelf.UpdateDropFeedback(left, 700); shelf.UpdateDropFeedback(left, 1350);
                    Check("drag-edge-dwell-can-return-to-previous-page", FirstVisible() == firstPage);
                    for (var i = 0; i < 40; i++) shelf.UpdateDropFeedback(right, 2000 + i * 650);
                    Check("drag-page-clamps-at-short-final-page", app.Windows.OfType<CardWindow>().Count(c => c.IsVisible) == (25 - 1) % shelf.DisplayCapacity + 1);
                    var target = app.Windows.OfType<ShelfDropWindow>().Single();
                    Check("drag-feedback-renders-cards-making-room", target.PreviewCount == app.Windows.OfType<CardWindow>().Count(c => c.IsVisible));
                    shelf.Hide(); Check("manual-hide-dismisses-drop-target", !target.IsVisible);
                }
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "drag-drop-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                ImageDecoder.StopIdleWorkers();
                File.WriteAllText(Path.Combine(directory, "drag-drop-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
}
