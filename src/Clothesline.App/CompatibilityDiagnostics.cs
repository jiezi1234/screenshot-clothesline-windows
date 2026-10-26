using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;
using Microsoft.Win32;

namespace Clothesline.App;

internal static class CompatibilityDiagnostics
{
    internal static int Backdrop(Application app, string path, string mode)
    {
        var monitor = Native.MonitorAt(Native.Cursor);
        var window = new Window { Title = "Clothesline fullscreen diagnostic", Background = Brushes.DarkSlateGray, ShowInTaskbar = false,
            WindowStyle = mode == "maximized" ? WindowStyle.SingleBorderWindow : WindowStyle.None, Content = new TextBlock { Text = "Clothesline compatibility test", Foreground = Brushes.White, FontSize = 24 } };
        if (mode == "fullscreen") window.ResizeMode = ResizeMode.NoResize;
        window.ContentRendered += (_, _) => File.WriteAllText(path, JsonSerializer.Serialize(new { monitor.Bounds, Handle = new WindowInteropHelper(window).Handle.ToInt64() }));
        window.Show();
        if (mode == "maximized") window.WindowState = WindowState.Maximized;
        else Native.Place(window, mode == "fullscreen" ? monitor.Bounds : new(monitor.WorkArea.X + 60, monitor.WorkArea.Y + 60, Math.Max(100, monitor.WorkArea.Width - 120), Math.Max(100, monitor.WorkArea.Height - 120)));
        window.Activate(); window.Closed += (_, _) => app.Shutdown(); return app.Run();
    }
    internal static int Run(Application app, string directory, bool performance, bool soak = false, bool foregroundChecks = true)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var measurements = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { checks.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            var language = UiText.Language;
            try
            {
                var run = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
                if (soak) await Soak(run, Check, measurements);
                else if (performance) await Measure(run, Check, measurements);
                else
                {
                    using var shelf = new ShelfController(); shelf.Start(false); shelf.SuppressFullscreen = false;
                    var recovered = 0; shelf.EnvironmentRecovered += () => recovered++;
                    shelf.Toggle(); shelf.HandlePower(PowerModes.Suspend);
                    Check("suspend-dismisses-and-blocks-shelf", !shelf.Visible && shelf.SessionBlocked);
                    shelf.Toggle(); Check("suspend-blocks-shortcut-reveal", !shelf.Visible);
                    shelf.HandleSession(SessionSwitchReason.SessionLock); shelf.HandlePower(PowerModes.Resume);
                    Check("resume-cannot-unblock-a-locked-session", shelf.SessionBlocked && recovered == 0);
                    shelf.HandleSession(SessionSwitchReason.SessionUnlock);
                    Check("unlock-recovers-without-automatic-reveal", !shelf.SessionBlocked && recovered == 1 && !shelf.Visible);
                    shelf.Toggle(); shelf.HandleSession(SessionSwitchReason.RemoteDisconnect);
                    Check("remote-disconnect-dismisses-overlays", !shelf.Visible && shelf.SessionBlocked);
                    shelf.HandleSession(SessionSwitchReason.RemoteConnect);
                    Check("remote-reconnect-requests-recovery", !shelf.SessionBlocked && recovered == 2);
                    shelf.Toggle(); shelf.ResetEnvironment(true);
                    Check("display-change-invalidates-position-and-recovers", !shelf.Visible && recovered == 3);
                    using var runtime = new LibraryRuntime(shelf, Path.Combine(run, "library"));
                    var fixture = DemoAssets.Create(Path.Combine(run, "fixtures"))[0];
                    await runtime.Collector.ImportExternal(fixture.Path);
                    runtime.RefreshShelf(false);
                    var dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true };
                    try
                    {
                        dashboard.ShowShelfList(); dashboard.UpdateLayout();
                        var list = UiAccessibility.Descendants(dashboard).OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == T("晾衣绳列表"));
                        Check("keyboard-shelf-lists-real-records", list.Items.Count == 1 && list.SelectedItem is LibraryEntry);
                        Check("keyboard-shelf-has-selection-automation-peer", UIElementAutomationPeer.CreatePeerForElement(list)?.GetPattern(PatternInterface.Selection) is not null);
                        Check("dashboard-status-is-polite-live-region", UiAccessibility.Descendants(dashboard).OfType<TextBlock>().Any(c => AutomationProperties.GetLiveSetting(c) == AutomationLiveSetting.Polite));
                        SaveWindow(dashboard, Path.Combine(directory, "keyboard-shelf.png"));
                        var source = (HwndSource)PresentationSource.FromVisual(dashboard);
                        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Delete) { RoutedEvent = Keyboard.KeyDownEvent });
                        Check("keyboard-delete-archives-without-deleting-source", runtime.Store.Entries(EntryState.Shelf).Count == 0 && runtime.Store.Entries(EntryState.History).Count == 1 && File.Exists(fixture.Path));
                        var current = DesktopLifecycle.IsCurrent(dashboard);
                        Check("public-virtual-desktop-interface-available", current.HasValue, new { current });
                        Check("own-window-can-follow-current-desktop", DesktopLifecycle.FollowForeground(dashboard));
                    }
                    finally { dashboard.Close(); }
                    var workspace = new ImageWorkspace([new("sample", fixture.Path, "Keyboard sample")], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), _ => new Release());
                    try
                    {
                        workspace.Show(); await Wait(() => workspace.Document is not null, 10000); workspace.UpdateLayout();
                        var canvas = UiAccessibility.Descendants(workspace).OfType<ImageSurface>().Single();
                        Check("canvas-exposes-named-focusable-pane", canvas.Focusable && UIElementAutomationPeer.CreatePeerForElement(canvas)?.GetName() == T("图片画布"));
                        canvas.Focus(); var source = (HwndSource)PresentationSource.FromVisual(workspace);
                        void Key(Key key) => canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
                        canvas.Tool = EditorTool.Rectangle; Key(System.Windows.Input.Key.Enter); for (var i = 0; i < 10; i++) Key(System.Windows.Input.Key.Right); Key(System.Windows.Input.Key.Down); Key(System.Windows.Input.Key.Enter);
                        Check("keyboard-can-create-a-mark", workspace.Document!.State.Marks.Length == 1 && workspace.Document.State.Marks[0].Tool == EditorTool.Rectangle);
                        workspace.Document.Undo(); Check("keyboard-mark-uses-standard-undo", workspace.Document.State.Marks.Length == 0);
                        canvas.Tool = EditorTool.Crop; Key(System.Windows.Input.Key.Enter); Key(System.Windows.Input.Key.Left); Key(System.Windows.Input.Key.Up); Key(System.Windows.Input.Key.Enter);
                        Check("keyboard-can-crop-in-pixel-coordinates", workspace.Document.State.Crop.Width == 1 && workspace.Document.State.Crop.Height == 1);
                        workspace.Document.Undo(); workspace.Document.MarkSaved();
                    }
                    finally { workspace.Document?.MarkSaved(); workspace.Close(); }
                    foreach (var display in Native.Monitors())
                    foreach (var percent in new[] { 80, 100, 120 })
                    {
                        var card = new CardWindow(fixture);
                        try
                        {
                            var dpi = Native.MoveAndGetScale(card, display); var expected = DesktopGeometry.Cards(display.WorkArea, dpi, 1, percent / 100.0)[0];
                            card.Present(expected, false); await Task.Delay(40);
                            Native.GetWindowRect(new WindowInteropHelper(card).Handle, out var actual);
                            Check("native-monitor-card-" + percent, expected == actual.Pixels, new { display.Bounds, dpi, expected, actual = actual.Pixels });
                            card.Dismiss(); Check("native-monitor-hide-removes-hit-surface-" + percent, !Native.IsWindowVisible(new WindowInteropHelper(card).Handle));
                        }
                        finally { card.Close(); }
                    }
                    if (!foregroundChecks) checks.Add(new { name = "foreground-fixtures", status = "manual-required", reason = "Explicit functional-only diagnostic: fullscreen/foreground checks are excluded and must be run separately." });
                    foreach (var mode in foregroundChecks ? new[] { "fullscreen", "maximized", "windowed" } : Array.Empty<string>())
                    {
                        var handshake = Path.Combine(run, "fullscreen-" + mode + ".json");
                        using var child = Process.Start(VideoCatalog.StartInfo("--compatibility-backdrop", handshake, mode)) ?? throw new IOException("Could not start compatibility window.");
                        try
                        {
                            await Wait(() => File.Exists(handshake), 5000); await Task.Delay(150);
                            using var info = JsonDocument.Parse(File.ReadAllText(handshake)); var bounds = info.RootElement.GetProperty("Bounds").Deserialize<PixelRect>();
                            var handle = (nint)info.RootElement.GetProperty("Handle").GetInt64();
                            try { await Wait(() => Native.GetForegroundWindow() == handle, 3000); }
                            catch (TimeoutException)
                            {
                                var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var actualPid);
                                throw new TimeoutException($"Foreground fixture unavailable ({mode}): expected HWND {handle}, PID {child.Id}; actual HWND {foreground}, PID {actualPid}.");
                            }
                            var monitor = Native.Monitors().First(m => m.Bounds == bounds);
                            Native.GetWindowRect(handle, out var actual);
                            Native.DwmGetWindowAttribute(handle, 9, out Native.Rect frame, System.Runtime.InteropServices.Marshal.SizeOf<Native.Rect>());
                            Check("native-fullscreen-detection-" + mode, Native.IsFullscreen(monitor) == (mode == "fullscreen"), new { bounds, actual = actual.Pixels, frame = frame.Pixels, style = Native.GetWindowLongPtrW(handle, -16).ToInt64(), foreground = Native.GetForegroundWindow().ToInt64(), handle = handle.ToInt64() });
                            if (mode == "fullscreen") { shelf.SuppressFullscreen = true; shelf.Hide(); shelf.Toggle(); Check("native-fullscreen-suppresses-reveal", !shelf.Visible); shelf.SuppressFullscreen = false; }
                        }
                        finally { if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(2000)) child.Kill(true); } }
                    }
                    foreach (var locale in UiText.Languages)
                    {
                        UiText.Language = locale.Code;
                        runtime.ApplySettings(runtime.Settings with { Language = locale.Code });
                        dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true };
                        try
                        {
                            dashboard.ShowAppearance(); dashboard.UpdateLayout();
                            Check("language-picker-" + locale.Code, UiAccessibility.Descendants(dashboard).OfType<ComboBox>().Any(c => c.Items.Count == 11));
                            Check("window-language-" + locale.Code, dashboard.Language.IetfLanguageTag.Equals(locale.Code, StringComparison.OrdinalIgnoreCase));
                            Check("localized-title-" + locale.Code, dashboard.Title == T("Screenshot Clothesline · 截图与历史"));
                            Check("language-selection-persists-" + locale.Code, runtime.Store.LoadSettings().Language == locale.Code && UiAccessibility.Descendants(dashboard).OfType<ComboBox>().Any(c => c.SelectedValue as string == locale.Code));
                            SaveWindow(dashboard, Path.Combine(directory, "language-" + locale.Code + ".png"));
                        }
                        finally { dashboard.Close(); }
                    }
                    UiText.Language = "en";
                    var tempRoot = Path.Combine(run, "stale"); Directory.CreateDirectory(tempRoot);
                    var stale = Path.Combine(tempRoot, "Clothesline-decode-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stale);
                    File.WriteAllText(Path.Combine(stale, "pixels.bgra"), "test pixels"); File.WriteAllText(Path.Combine(stale, "unrelated.txt"), "keep");
                    Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-3)); ImageDecoder.CleanupStale(tempRoot);
                    Check("stale-cleanup-removes-only-known-decoder-output", !File.Exists(Path.Combine(stale, "pixels.bgra")) && File.ReadAllText(Path.Combine(stale, "unrelated.txt")) == "keep");
                    ImageDecoder.CleanupStale(Path.Combine(run, "missing-temp"));
                    Check("missing-cleanup-directory-does-not-disable-decoder", true);
                    foreach (var mode in new[] { "timeout", "crash", "cancel", "memory" })
                    {
                        using var cancellation = new CancellationTokenSource(); if (mode == "cancel") cancellation.CancelAfter(200);
                        var watch = Stopwatch.StartNew(); var rejected = false;
                        try { await ImageDecoder.DiagnosticFault(Path.Combine(run, "fault-" + mode), mode, cancellation.Token); }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException)
                        { rejected = mode == "cancel" ? ex is OperationCanceledException : ex.Message == T(mode == "timeout" ? "图片解析超时，原文件保留，可稍后重试。" : mode == "memory" ? "图片解析超过内存限制，原文件保留。" : "图片解析失败，原文件保留。"); }
                        Check("isolated-image-worker-" + mode, rejected && watch.Elapsed.TotalSeconds < 4, watch.Elapsed.TotalMilliseconds);
                    }
                    var malformed = new ImageDecoder.Metadata(10000, 10000, 96, 96, new string('A', 64));
                    var invalid = false; try { ImageDecoder.Validate(malformed, 400000000, false); } catch (IOException) { invalid = true; }
                    Check("worker-result-cannot-bypass-pixel-limit", invalid);
                    var original = ImageFiles.Read(fixture.Path); var hash = ImageFiles.Hash(original);
                    var thumb = await ImageDecoder.Read(fixture.Path, true);
                    Check("isolated-decoder-preserves-source-pixels-and-hash", ImageFiles.Hash(fixture.Path) == hash && thumb.IsFrozen && Math.Max(thumb.PixelWidth, thumb.PixelHeight) <= 420);
                    var render = await new ImageDocument(original).RenderAsync(); Check("background-render-matches-existing-compositor", ImageFiles.Hash(render) == ImageFiles.Hash(new ImageDocument(original).Render()));
                    var decodeStarted = new TaskCompletionSource(); var releaseDecode = new TaskCompletionSource<string>();
                    using var store = new LibraryStore(Path.Combine(run, "blocked-decode-library"));
                    using var collector = new CollectionEngine(store, _ => "unused", () => 25, imageHashAsync: async (_, token) => { decodeStarted.SetResult(); return await releaseDecode.Task.WaitAsync(token); });
                    var slow = collector.ImportExternal(fixture.Path); await decodeStarted.Task;
                    try
                    {
                        var clipboard = collector.ImportClipboard(ImageFiles.Png(original), "clipboard-hash", 1024 * 1024 * 10);
                        Check("slow-image-does-not-block-clipboard-commit", await Task.WhenAny(clipboard, Task.Delay(1000)) == clipboard && await clipboard);
                    }
                    finally { releaseDecode.TrySetResult(hash); await slow; }
                    checks.Add(new { name = "external-environments", status = "manual-required", cases = new[] { "actual mixed-DPI changes and unplugging", "switching real virtual desktops", "RDP connection and reconnect", "physical sleep/wake and lock/unlock", "screen-sharing applications", "browser/Office/chat drag targets", "Narrator spoken output" } });
                }
            }
            catch (Exception ex) { Check("completed", false, ex.ToString()); }
            finally
            {
                UiText.Language = language;
                File.WriteAllText(Path.Combine(directory, soak ? "soak-diagnostics.json" : performance ? "performance-diagnostics.json" : "compatibility-diagnostics.json"), JsonSerializer.Serialize(new { passed, scope = foregroundChecks ? "full-owned-fixture" : "functional-only-foreground-excluded", os = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(), checks, measurements }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static async Task Wait(Func<bool> condition, int milliseconds)
    { var until = Environment.TickCount64 + milliseconds; while (!condition() && Environment.TickCount64 < until) await Task.Delay(30); if (!condition()) throw new TimeoutException("Diagnostic condition timed out."); }
    private static async Task Measure(string run, Action<string, bool, object?> check, List<object> measurements)
    {
        var folder = Path.Combine(run, "captures"); Directory.CreateDirectory(folder);
        using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(run, "library"));
        runtime.ApplySettings(new LibrarySettings { ScreenshotFolder = folder, ShowNew = false, ReduceMotion = true, Capacity = 100, Shortcut = "" }); runtime.Start();
        await Task.Delay(10000);
        using var process = Process.GetCurrentProcess();
        var samples = new List<double>(); var combinedSamples = new List<double>();
        for (var n = 0; n < 3; n++)
        {
            process.Refresh(); var childrenBefore = ImageDecoder.Resources().ToDictionary(r => r.Pid); var start = process.TotalProcessorTime; var watch = Stopwatch.StartNew(); await Task.Delay(10000); process.Refresh();
            samples.Add((process.TotalProcessorTime - start).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100);
            var childCpu = ImageDecoder.Resources().Sum(r => Math.Max(0, r.CpuMs - (childrenBefore.GetValueOrDefault(r.Pid)?.CpuMs ?? 0)));
            combinedSamples.Add(samples[^1] + childCpu / watch.Elapsed.TotalMilliseconds * 100);
        }
        var resources = ImageDecoder.Resources();
        measurements.Add(new { name = "hidden-idle", parentCpuOneCorePercent = samples, cpuIncludingDecodersOneCorePercent = combinedSamples, average = combinedSamples.Average(), workingSetIncludingDecodersMiB = (process.WorkingSet64 + resources.Sum(r => r.WorkingSet)) / 1048576.0, privateIncludingDecodersMiB = (process.PrivateMemorySize64 + resources.Sum(r => r.PrivateBytes)) / 1048576.0, decoderCount = resources.Length, warmupSeconds = 10, seconds = 30 });
        check("hidden-runtime-has-no-visible-overlays", !shelf.Visible, null);
        var latency = new List<double>();
        for (var n = 0; n < 10; n++)
        {
            var path = Path.Combine(folder, "latency-" + n + ".png"); WriteImage(path, n, 1920, 1080); var watch = Stopwatch.StartNew();
            await Wait(() => runtime.Store.Entries(EntryState.Shelf).Any(e => e.Path == path) && shelf.Count == n + 1, 10000); latency.Add(watch.Elapsed.TotalMilliseconds);
        }
        measurements.Add(new { name = "file-close-to-shelf-record-and-card", width = 1920, height = 1080, milliseconds = latency, median = latency.Order().Skip((latency.Count - 1) / 2).Take(latency.Count % 2 == 0 ? 2 : 1).Average(), p95 = latency.Max(), note = "Generated 1080p PNGs; warm decoder after startup. Includes watcher stability delay, database commit and UI refresh; excludes compositor scanout and initial cold decoder startup." });
        var burst = Stopwatch.StartNew(); for (var n = 10; n < 110; n++) WriteImage(Path.Combine(folder, "burst-" + n + ".png"), n);
        await Wait(() => runtime.Store.Entries().Count == 110 && shelf.Count == 100, 120000);
        check("100-file-burst-no-loss-or-duplicates", runtime.Store.Entries().Count == 110 && runtime.Store.Entries(EntryState.Shelf).Count == 100 && runtime.Store.Entries(EntryState.History).Count == 10, null);
        measurements.Add(new { name = "100-file-burst", milliseconds = burst.Elapsed.TotalMilliseconds, cacheEntries = ImageDecoder.CacheCount });
        check("thumbnail-cache-bounded", ImageDecoder.CacheCount <= 64, null);
        var bytes = new byte[4000 * 3000 * 4]; for (var n = 0; n < bytes.Length; n += 4) { bytes[n] = 80; bytes[n + 1] = 140; bytes[n + 3] = 255; }
        var large = BitmapSource.Create(4000, 3000, 96, 96, PixelFormats.Bgra32, null, bytes, 4000 * 4); large.Freeze();
        var document = new ImageDocument(large); var ticks = 0; var pulse = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; pulse.Tick += (_, _) => ticks++; pulse.Start();
        var renderWatch = Stopwatch.StartNew(); var mark = await document.CreateMarkAsync(EditorTool.Mosaic, [new Point(0, 0), new Point(4000, 3000)], Colors.Red, 4); document.Add(mark); var rendered = await document.RenderAsync(); pulse.Stop();
        check("large-edit-renders-on-background-thread", ticks > 0 && rendered.PixelWidth == 4000 && rendered.PixelHeight == 3000 && mark.Tiles.Length <= 11000, new { uiTicks = ticks });
        process.Refresh(); measurements.Add(new { name = "12MP-mosaic-and-render", milliseconds = renderWatch.Elapsed.TotalMilliseconds, uiTicks = ticks, privateMiB = process.PrivateMemorySize64 / 1048576.0 });
    }
    private static void WriteImage(string path, int value, int width = 64, int height = 64)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var i = (y * width + x) * 4; pixels[i] = (byte)(x / 8 + value); pixels[i + 1] = (byte)(y / 16 + value / 256); pixels[i + 2] = (byte)((x ^ y) % 128); pixels[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); File.WriteAllBytes(path, ImageFiles.Png(image));
    }
    private static async Task Soak(string run, Action<string, bool, object?> check, List<object> measurements)
    {
        using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(run, "library"));
        runtime.ApplySettings(new LibrarySettings { ShowNew = false, ReduceMotion = true, Capacity = 25, Shortcut = "" }); runtime.Start();
        for (var n = 0; n < 10; n++) { var path = Path.Combine(run, "soak-" + n + ".png"); WriteImage(path, n); await runtime.Collector.ImportExternal(path, preserveCopy: true); }
        runtime.ArchiveBatch(); runtime.RefreshShelf(false); var baselineWindows = Application.Current.Windows.Count;
        var ids = runtime.Store.Entries().Select(e => e.Id).ToArray(); var watch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess(); var memory = new List<double>();
        for (var cycle = 0; cycle < 60; cycle++)
        {
            foreach (var id in ids) runtime.Restore(id);
            runtime.RefreshShelf(false);
            if (shelf.Count != 10) throw new InvalidOperationException("Soak restore lost a card.");
            runtime.ArchiveBatch(); runtime.RefreshShelf(false);
            if (shelf.Count != 0 || Application.Current.Windows.Count != baselineWindows) throw new InvalidOperationException("Soak archive left card windows alive.");
            if (cycle % 10 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); process.Refresh(); memory.Add(process.PrivateMemorySize64 / 1048576.0); }
            await Task.Delay(2000);
        }
        check("60-restore-archive-cycles-release-all-card-windows", Application.Current.Windows.Count == baselineWindows && shelf.Count == 0, null);
        check("soak-retains-ten-original-files-and-records", runtime.Store.Entries().Count == 10 && runtime.Store.Entries().All(e => File.Exists(e.Path)), null);
        check("soak-thumbnail-cache-remains-bounded", ImageDecoder.CacheCount <= 64, null);
        check("soak-private-memory-growth-below-64MiB", memory[^1] - memory[0] < 64, memory);
        measurements.Add(new { name = "hidden-restore-archive-soak", cycles = 60, cardsPerCycle = 10, seconds = watch.Elapsed.TotalSeconds, privateMiBEveryTenCycles = memory, note = "Synthetic isolated library. Does not establish overnight reliability or real cross-application drag behavior." });
    }
    private sealed class Release : IDisposable { public void Dispose() { } }
    private static void SaveWindow(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) { var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight); dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, bounds); dc.DrawRectangle(new VisualBrush(content), null, bounds); }
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(drawing); DemoAssets.Save(image, path);
    }
}
