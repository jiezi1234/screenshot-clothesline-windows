using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class VideoTrimDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { checks.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            VideoWorkspace? window = null;
            try
            {
                var fixtures = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
                var source = await VideoDiagnostics.Generate(fixtures, true);
                var portrait = await VideoDiagnostics.Generate(fixtures, false, true);
                var folder = Path.Combine(fixtures, "output"); Directory.CreateDirectory(folder);
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(fixtures, "library"));
                runtime.ApplySettings(new LibrarySettings { VideoFolder = folder, Capacity = 25, ShowNew = false, ReduceMotion = true }); runtime.Start();
                await runtime.Collector.ImportExternal(source); var original = await runtime.Videos.Get(source);
                var request = new VideoTrimRequest(source, CollectionEngine.Signature(source), new(2.25, 3.25));
                var progress = new List<double>();
                var saved = await runtime.PersistTrimmedCopy(request, Path.Combine(folder, "clip.mp4"), new InlineProgress(v => { lock (progress) progress.Add(v); }));
                var trimmed = await runtime.Videos.Get(saved.Path);
                Check("native-non-keyframe-trim-duration", Math.Abs(trimmed.Seconds - 1) <= .15, trimmed.Seconds);
                var image = new FormatConvertedBitmap(trimmed.Image(), PixelFormats.Bgra32, null, 0); var pixel = new byte[4];
                image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixel, 4, 0);
                Check("trim-begins-in-selected-blue-section", pixel[0] > pixel[1] + 50 && pixel[0] > pixel[2] + 50, pixel);
                Check("export-preserves-video-size-and-AAC", trimmed.Width == original.Width && trimmed.Height == original.Height && trimmed.AudioCodecs.SequenceEqual(new[] { "AAC" }));
                Check("source-bytes-preserved", original.Hash == (await new VideoCatalog(Path.Combine(fixtures, "source-check")).Get(source)).Hash);
                Check("export-progress-and-validation-complete", progress.Count > 1 && progress.Last() == 100 && progress.Contains(95));
                Check("trimmed-copy-hung-as-external", saved.Hung && runtime.Store.Entries().Single(e => e.Path == saved.Path).Ownership == Ownership.External);
                await Task.Delay(1600); runtime.Collector.RequestRescan(); await Task.Delay(300);
                Check("watcher-does-not-import-staging-or-duplicate-output", runtime.Store.Entries().Count == 2 && runtime.Store.Entries().All(e => !e.Path.Contains(".clothesline-trim-")));
                Check("successful-staging-cleaned", !Directory.GetDirectories(folder, ".clothesline-trim-*").Any());
                var existing = File.ReadAllBytes(saved.Path);
                var second = await runtime.PersistTrimmedCopy(request with { Range = new(.25, 1.25) }, saved.Path);
                Check("same-name-export-never-overwrites", second.Path != saved.Path && existing.SequenceEqual(File.ReadAllBytes(saved.Path)));
                var withoutAudio = await runtime.PersistTrimmedCopy(new(portrait, CollectionEngine.Signature(portrait), new(.2, 2.2)), Path.Combine(folder, "portrait.mp4"));
                var portraitResult = await runtime.Videos.Get(withoutAudio.Path);
                Check("portrait-trim-keeps-size-and-no-audio", portraitResult.Width == 180 && portraitResult.Height == 320 && portraitResult.AudioCodecs.Length == 0 && Math.Abs(portraitResult.Seconds - 2) <= .15);
                var minimum = await runtime.PersistTrimmedCopy(request with { Range = new(.25, .35) }, Path.Combine(folder, "minimum.mp4"));
                Check("minimum-tenth-second-selection-decodes", (await runtime.Videos.Get(minimum.Path)).Seconds is > 0 and <= .25);
                var beforeCancel = runtime.Store.Entries().Count;
                using (var cancel = new CancellationTokenSource())
                {
                    try { await runtime.PersistTrimmedCopy(request, Path.Combine(folder, "cancel.mp4"), new InlineProgress(_ => cancel.Cancel()), cancel.Token); Check("cancel-before-commit", false); }
                    catch (OperationCanceledException) { Check("cancel-before-commit", !File.Exists(Path.Combine(folder, "cancel.mp4")) && runtime.Store.Entries().Count == beforeCancel && !Directory.GetDirectories(folder, ".clothesline-trim-*").Any()); }
                }
                using (var late = new CancellationTokenSource())
                {
                    var result = await runtime.PersistTrimmedCopy(request, Path.Combine(folder, "late-cancel.mp4"), new InlineProgress(v => { if (v == 100) late.Cancel(); }), late.Token);
                    Check("cancel-after-commit-keeps-copy-and-import", late.IsCancellationRequested && result.Hung && File.Exists(result.Path));
                }
                try { await VideoTrimmer.Export(request, Path.Combine(folder, "timeout.mp4"), runtime.Videos, null, default, TimeSpan.FromMilliseconds(1)); Check("encode-timeout", false); }
                catch (IOException) { Check("encode-timeout", !File.Exists(Path.Combine(folder, "timeout.mp4")) && !Directory.GetDirectories(folder, ".clothesline-trim-*").Any()); }
                var replaced = Path.Combine(fixtures, "changed.mp4"); File.Copy(source, replaced); await runtime.Videos.Get(replaced);
                var changedRequest = new VideoTrimRequest(replaced, CollectionEngine.Signature(replaced), new(1, 2)); var stamp = File.GetLastWriteTimeUtc(replaced);
                using (var mutation = File.Open(replaced, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { mutation.Position = mutation.Length - 1; var previous = mutation.ReadByte(); mutation.Position--; mutation.WriteByte((byte)(previous ^ 1)); }
                File.SetLastWriteTimeUtc(replaced, stamp);
                try { await runtime.PersistTrimmedCopy(changedRequest, Path.Combine(folder, "changed.mp4")); Check("worker-verifies-source-hash-even-with-identical-signature", false); }
                catch (IOException) { Check("worker-verifies-source-hash-even-with-identical-signature", !File.Exists(Path.Combine(folder, "changed.mp4"))); }
                try { await runtime.PersistTrimmedCopy(request with { Range = new(3, 2) }, Path.Combine(folder, "invalid.mp4")); Check("invalid-range-refused", false); }
                catch (ArgumentException) { Check("invalid-range-refused", !File.Exists(Path.Combine(folder, "invalid.mp4"))); }
                window = new VideoWorkspace(source, runtime.AcquireLease, async (r, _, p, token) => await runtime.PersistTrimmedCopy(r, Path.Combine(folder, "ui-export.mp4"), p, token)); window.Show();
                Check("trim-ui-opens-paused", await window.Ready.WaitAsync(TimeSpan.FromSeconds(25)) && !window.IsPlaying);
                window.SetRange(2.25, 3.25); Check("trim-controls-retain-fractional-times", window.Range().Start == 2.25 && window.Range().End == 3.25);
                Check("trim-inputs-have-accessible-names", Find<TextBox>(window).Any(t => AutomationProperties.GetName(t) == T("裁剪开始时间（秒）")) && Find<TextBox>(window).Any(t => AutomationProperties.GetName(t) == T("裁剪结束时间（秒）")));
                window.PreviewSegment(); await Task.Delay(150); var previewStart = window.Position.TotalSeconds;
                await Task.Delay(1400); Check("segment-preview-stops-at-out-point", previewStart >= 2.2 && !window.IsPlaying && Math.Abs(window.Position.TotalSeconds - 3.25) < .15, new { previewStart, end = window.Position.TotalSeconds, window.IsPlaying });
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); DemoAssets.Save(bitmap, Path.Combine(directory, "video-trim.png"));
                await window.ExportSelection(); Check("trim-ui-export-hangs-copy", File.Exists(Path.Combine(folder, "ui-export.mp4")) && runtime.Store.Entries().Any(e => e.Path.EndsWith("ui-export.mp4", StringComparison.Ordinal)) && !window.Exporting);
                window.SetRange(3.5, 4); window.PreviewSegment(); await Task.Delay(1200);
                Check("segment-at-file-end-stays-at-end", !window.IsPlaying && window.Position.TotalSeconds >= 3.85);
                window.Close(); window = null;
                UiText.Language = "en";
                window = new VideoWorkspace(source, runtime.AcquireLease) { Width = 740, Height = 560 }; window.Show(); await window.Ready.WaitAsync(TimeSpan.FromSeconds(25)); window.SetRange(.25, 1.25); window.UpdateLayout();
                Check("english-trim-controls-at-minimum-size", Find<TextBox>(window).Count(t => AutomationProperties.GetName(t).StartsWith("Trim ", StringComparison.Ordinal)) == 2 && Find<Button>(window).Any(b => Equals(b.Content, "Preview selection")));
                var english = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); english.Render(window); DemoAssets.Save(english, Path.Combine(directory, "video-trim-en.png"));
                window.Close(); window = null; UiText.Language = "zh-CN";
                var cancelled = false; var released = false;
                window = new VideoWorkspace(source, _ => new Release(() => released = true), async (_, _, _, token) =>
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { cancelled = true; throw; } return null;
                }); window.Show(); await window.Ready.WaitAsync(TimeSpan.FromSeconds(25));
                var closingExport = window.ExportSelection(); window.Close(); await closingExport;
                Check("close-cancels-active-export-before-releasing-file", cancelled && released && !window.Exporting && !window.IsVisible); window = null;
            }
            catch (Exception ex) { Check("unhandled", false, ex.ToString()); }
            finally
            {
                window?.Close(); File.WriteAllText(Path.Combine(directory, "video-trim-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
