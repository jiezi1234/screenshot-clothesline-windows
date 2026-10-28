using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal static class HoverPreviewDiagnostics
{
    private sealed record Request(CancellationToken Token, TaskCompletionSource<BitmapSource> Completion);
    internal static int Run(Application app, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                var assets = DemoAssets.Create(Path.Combine(output, "fixtures-" + Guid.NewGuid().ToString("N")));
                var hashes = assets.Select(a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Path)))).ToArray();
                var requests = new List<Request>();
                Task<BitmapSource> Load(string path, CancellationToken token)
                {
                    var request = new Request(token, new(TaskCreationOptions.RunContinuationsAsynchronously)); requests.Add(request); return request.Completion.Task;
                }
                using var shelf = new ShelfController(() => true, Load) { SuppressFullscreen = false, ReduceMotion = true };
                var items = assets.Select((a, i) => a with { Image = ImageDecoder.Thumbnail(a.Image), EntryId = "hover-" + i }).ToArray();
                shelf.SetItems(items, true);
                var preview = app.Windows.OfType<HoverPreviewWindow>().Single();
                CardWindow Card(string id) => app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == id);
                PixelRect Bounds(Window window) { Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect); return rect.Pixels; }
                PixelPoint Center(Window window) { var rect = Bounds(window); return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2); }
                var source = Card("hover-0"); var second = Card("hover-1"); var point = Center(source);
                var foreground = Native.GetForegroundWindow();
                shelf.TrackHoverPreview(0, point, false, false); shelf.TrackHoverPreview(399, point, false, false);
                Check("brief-hover-does-not-show-or-decode", !preview.IsVisible && requests.Count == 0);
                shelf.TrackHoverPreview(400, point, false, false);
                Check("dwell-shows-thumbnail-while-large-preview-loads", preview.IsVisible && ReferenceEquals(preview.PreviewImage, items[0].Image) && requests.Count == 1);
                Check("hover-keeps-foreground-and-does-not-block-collection", Native.GetForegroundWindow() == foreground && !shelf.Busy);
                var style = Native.GetWindowLongPtrW(new WindowInteropHelper(preview).Handle, -20).ToInt64();
                Check("preview-is-layered-transparent-nonactivating-toolwindow", (style & 0x080800a0) == 0x080800a0);
                var rect = Bounds(preview); var monitor = Native.MonitorAt(point);
                Check("preview-enlarges-without-covering-row-or-leaving-work-area", (rect.Width > Bounds(source).Width || rect.Height > Bounds(source).Height)
                    && rect.X >= monitor.WorkArea.X && rect.Right <= monitor.WorkArea.Right && rect.Y >= monitor.WorkArea.Y && rect.Bottom <= monitor.WorkArea.Bottom
                    && app.Windows.OfType<CardWindow>().Where(c => c.IsVisible).All(c => !HoverPreviewGeometry.Overlaps(rect, Bounds(c))));
                var hit = Native.WindowFromPoint(new Native.Point { X = rect.X + rect.Width / 2, Y = rect.Y + rect.Height / 2 });
                Native.GetWindowThreadProcessId(hit, out var pid);
                Check("painted-preview-passes-input-to-another-process", hit != 0 && pid != Environment.ProcessId);
                requests[0].Completion.SetResult(assets[0].Image); await Task.Delay(40);
                Check("large-image-replaces-thumbnail-without-extra-loads", ReferenceEquals(preview.PreviewImage, assets[0].Image) && requests.Count == 1);
                Capture(preview, Path.Combine(output, "hover-preview.png"));
                shelf.TrackHoverPreview(1000, Center(second), false, false);
                Check("switching-card-hides-old-preview-immediately", !preview.IsVisible && preview.PreviewImage is null);
                shelf.TrackHoverPreview(1400, Center(second), false, false); var stale = requests.Last();
                shelf.TrackHoverPreview(1500, point, false, false); shelf.TrackHoverPreview(1900, point, false, false); var current = requests.Last();
                stale.Completion.SetResult(assets[1].Image); await Task.Delay(40);
                Check("late-load-cannot-replace-new-hover", stale.Token.IsCancellationRequested && ReferenceEquals(preview.PreviewImage, items[0].Image));
                current.Completion.SetResult(assets[0].Image); await Task.Delay(40);
                shelf.TrackHoverPreview(2000, point, false, true); shelf.TrackHoverPreview(2600, point, false, false);
                Check("escape-dismisses-until-pointer-leaves-card", !preview.IsVisible && preview.PreviewImage is null && requests.Count == 3);
                var outside = new PixelPoint(monitor.WorkArea.X + 2, monitor.WorkArea.Bottom - 2);
                shelf.TrackHoverPreview(2700, outside, false, false); shelf.TrackHoverPreview(2800, point, false, false); shelf.TrackHoverPreview(3200, point, false, false);
                Check("leaving-and-returning-rearms-preview", preview.IsVisible && requests.Count == 4);
                shelf.TrackHoverPreview(3300, point, true, false);
                Check("pressed-mouse-cancels-preview-and-load", !preview.IsVisible && requests.Last().Token.IsCancellationRequested);
                shelf.TrackHoverPreview(3400, point, false, false); shelf.TrackHoverPreview(3800, point, false, false);
                source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Check("mouse-down-dismisses-before-menu-or-drag", !preview.IsVisible && requests.Last().Token.IsCancellationRequested);
                shelf.TrackHoverPreview(4000, point, false, false); shelf.TrackHoverPreview(4400, point, false, false);
                source.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Check("mouse-leave-releases-image-immediately", !preview.IsVisible && preview.PreviewImage is null);
                shelf.TrackHoverPreview(4600, point, false, false); shelf.TrackHoverPreview(5000, point, false, false);
                shelf.TrackHoverPreview(5100, Center(preview), false, false);
                Check("preview-does-not-sustain-its-own-hover", !preview.IsVisible);
                shelf.TrackHoverPreview(5200, point, false, false); shelf.TrackHoverPreview(5600, point, false, false);
                using (var blocker = new WindowScope(new OverlayWindow("Hover test occluder", false) { Content = new Border { Background = Brushes.AliceBlue } }))
                {
                    blocker.Window.Present(Bounds(source), false); shelf.TrackHoverPreview(5700, point, false, false);
                    Check("covered-card-does-not-trigger-preview", !preview.IsVisible);
                }
                shelf.TrackHoverPreview(5800, point, false, false); shelf.TrackHoverPreview(6200, point, false, false);
                requests.Last().Completion.SetException(new IOException("fixture decode failed")); await Task.Delay(40);
                Check("decode-failure-closes-preview-without-changing-shelf", !preview.IsVisible && shelf.Count == 3 && preview.PreviewImage is null);
                shelf.TrackHoverPreview(6300, outside, false, false); shelf.TrackHoverPreview(6400, point, false, false); shelf.TrackHoverPreview(6800, point, false, false);
                var gesture = shelf.BeginLocalReorder(source, point, false);
                Check("internal-reorder-dismisses-hover", !preview.IsVisible && requests.Last().Token.IsCancellationRequested);
                shelf.Hide(); await gesture; shelf.Show();
                shelf.TrackHoverPreview(7000, point, false, false); shelf.TrackHoverPreview(7400, point, false, false);
                shelf.UpdateDropFeedback(point, 7500);
                Check("incoming-file-drag-dismisses-hover", !preview.IsVisible);
                shelf.Hide(); shelf.Show();
                shelf.TrackHoverPreview(7600, point, false, false); shelf.TrackHoverPreview(8000, point, false, false);
                shelf.SetCardSize(120);
                Check("layout-change-cancels-preview", !preview.IsVisible && requests.Last().Token.IsCancellationRequested);
                point = Center(source); shelf.TrackHoverPreview(8200, point, false, false); shelf.TrackHoverPreview(8600, point, false, false);
                shelf.SetItems(items.Skip(1), false);
                Check("removing-source-cancels-preview", !preview.IsVisible && requests.Last().Token.IsCancellationRequested);
                source = Card("hover-1"); point = Center(source); shelf.TrackHoverPreview(8800, point, false, false); shelf.TrackHoverPreview(9200, point, false, false);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                Check("lock-cancels-and-releases-preview", !preview.IsVisible && preview.PreviewImage is null && requests.Last().Token.IsCancellationRequested);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionUnlock);
                shelf.SetItems([items[0] with { Path = Path.ChangeExtension(items[0].Path, ".mp4"), EntryId = "video" }], true);
                var beforeVideo = requests.Count; var video = Card("video");
                shelf.TrackHoverPreview(9400, Center(video), false, false); shelf.TrackHoverPreview(9900, Center(video), false, false);
                Check("video-cards-do-not-start-image-preview", !preview.IsVisible && requests.Count == beforeVideo);
                shelf.SetItems(items, true); source = Card("hover-0"); point = Center(source);
                shelf.TrackHoverPreview(10000, point, false, false); shelf.TrackHoverPreview(10400, point, false, false); var last = requests.Last();
                shelf.Dispose(); last.Completion.SetResult(assets[0].Image); await Task.Delay(40);
                Check("dispose-cancels-load-and-late-result-cannot-reopen-window", last.Token.IsCancellationRequested && !app.Windows.OfType<HoverPreviewWindow>().Any());
                foreach (var request in requests) request.Completion.TrySetCanceled();
                Check("hover-preserves-all-original-files", assets.Select(a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Path)))).SequenceEqual(hashes));

                var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(assets[0].Image, new Rect(0, 0, 3200, 2000));
                var large = new RenderTargetBitmap(3200, 2000, 96, 96, PixelFormats.Pbgra32); large.Render(visual); large.Freeze();
                var largePath = Path.Combine(output, "large-fixture.png"); DemoAssets.Save(large, largePath);
                var cacheBefore = ImageDecoder.CacheCount;
                var loaded = await ImageDecoder.ReadPreview(largePath);
                Check("isolated-decoder-bounds-large-preview-and-preserves-aspect", loaded.IsFrozen && loaded.PixelWidth == 1600 && loaded.PixelHeight == 1000);
                Check("preview-is-not-retained-in-thumbnail-cache", ImageDecoder.CacheCount == cacheBefore);
                var small = await ImageDecoder.Read(largePath, true);
                Check("existing-thumbnail-route-still-bounded", Math.Max(small.PixelWidth, small.PixelHeight) <= 420);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var didCancel = false;
                try { await ImageDecoder.ReadPreview(largePath, cancelled.Token); } catch (OperationCanceledException) { didCancel = true; }
                Check("preview-decoder-honors-cancellation", didCancel);
                var missingRejected = false;
                try { await ImageDecoder.ReadPreview(Path.Combine(output, "missing.png")); } catch (IOException) { missingRejected = true; }
                Check("missing-source-is-handled-without-a-preview", missingRejected);
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "hover-preview-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                ImageDecoder.StopIdleWorkers();
                File.WriteAllText(Path.Combine(output, "hover-preview-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class WindowScope(OverlayWindow window) : IDisposable
    {
        internal OverlayWindow Window => window;
        public void Dispose() => window.Close();
    }
    private static void Capture(HoverPreviewWindow window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content); DemoAssets.Save(image, path);
    }
}
