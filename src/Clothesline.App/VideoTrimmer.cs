using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Clothesline.Core;
using Clothesline.Storage;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Clothesline.App;

internal sealed record VideoTrimRequest(string SourcePath, string Signature, VideoTrimRange Range);
internal sealed record SavedVideo(string Path, bool Hung, string Warning = "");
internal sealed record VideoRenderJob(VideoTrimRequest Request, string Hash, string Output, string Language);

internal static class VideoTrimmer
{
    internal static async Task<string> Export(VideoTrimRequest request, string destination, VideoCatalog catalog, IProgress<double>? progress, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (CollectionEngine.Signature(request.SourcePath) != request.Signature) throw new IOException(T("原录像已发生变化，请关闭预览后重新打开。"));
        var original = await catalog.Get(request.SourcePath, cancellation).ConfigureAwait(false);
        var range = VideoTrimRange.Create(request.Range.Start, request.Range.End, original.Seconds);
        request = request with { SourcePath = Path.GetFullPath(request.SourcePath), Range = range };
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) destination += ".mp4";
        var directory = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(directory);
        // Same-volume staging makes the final rename atomic; nonrecursive watchers cannot import partial output.
        var staging = Path.Combine(directory, ".clothesline-trim-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var output = Path.Combine(staging, "encoded.mp4"); var jobPath = Path.Combine(staging, "job.json");
        try
        {
            File.WriteAllText(jobPath, JsonSerializer.Serialize(new VideoRenderJob(request, original.Hash, output, UiText.Language)));
            var start = VideoCatalog.StartInfo("--video-trim-worker", jobPath);
            using var process = Process.Start(start) ?? throw new IOException(T("无法启动录像导出。"));
            var reading = ReadProgress(process, jobPath + ".progress", progress);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(30));
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync().ConfigureAwait(false); await reading.ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); throw new IOException(T("录像导出超时，原文件保留。"));
            }
            await reading.ConfigureAwait(false);
            var error = File.Exists(jobPath + ".error") ? File.ReadAllText(jobPath + ".error") : "";
            if (process.ExitCode != 0) throw new IOException(T("录像导出失败：") + (string.IsNullOrWhiteSpace(error) ? T("系统编码器未能完成导出。") + " [" + process.ExitCode + "]" : error.Trim()));
            progress?.Report(95);
            // A separate decoder must accept the completed output before it becomes a user-visible file.
            var result = await catalog.Get(output, cancellation).ConfigureAwait(false);
            if (Math.Abs(result.Seconds - range.Seconds) > .15 || result.Width != original.Width || result.Height != original.Height || result.AudioCodecs.Length != Math.Min(1, original.AudioCodecs.Length))
                throw new IOException(T("导出验证未通过，原文件保留。"));
            cancellation.ThrowIfCancellationRequested();
            var saved = Commit(output, destination);
            progress?.Report(100); return saved;
        }
        finally
        {
            // Only our known temporary files; never recursively remove a destination directory.
            foreach (var temporary in new[] { output, jobPath, jobPath + ".error", jobPath + ".progress" })
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { Directory.Delete(staging, false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private static async Task ReadProgress(Process process, string path, IProgress<double>? progress)
    {
        double previous = -1;
        do
        {
            try
            {
                if (File.Exists(path) && double.TryParse(File.ReadAllText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value != previous)
                { previous = value; progress?.Report(Math.Clamp(value * .9, 0, 90)); }
            }
            catch (IOException) { /* The worker may be replacing its progress value. */ }
            if (process.HasExited) break;
            await Task.Delay(100).ConfigureAwait(false);
        } while (true);
    }
    internal static string Commit(string staged, string requested)
    {
        var folder = Path.GetDirectoryName(requested)!; var name = Path.GetFileNameWithoutExtension(requested);
        for (var n = 0; n < 10000; n++)
        {
            var path = Path.Combine(folder, name + (n == 0 ? "" : $" ({n})") + ".mp4");
            try { File.Move(staged, path, false); return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException(T("无法生成不重名的导出文件。"));
    }
    internal static int Worker(Application app, string jobPath)
    {
        using var lifetime = new Timer(_ => Environment.Exit(2), null, TimeSpan.FromMinutes(35), Timeout.InfiniteTimeSpan);
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                var job = JsonSerializer.Deserialize<VideoRenderJob>(File.ReadAllText(jobPath)) ?? throw new IOException("Invalid video job."); UiText.Language = job.Language;
                using var source = new FileStream(job.Request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (source.Length == 0 || source.Length > VideoCatalog.MaxBytes || CollectionEngine.Signature(job.Request.SourcePath) != job.Request.Signature || "video:" + Convert.ToHexString(await SHA256.HashDataAsync(source)) != job.Hash)
                    throw new IOException(T("原录像已发生变化，请关闭预览后重新打开。"));
                var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(job.Request.SourcePath));
                var range = VideoTrimRange.Create(job.Request.Range.Start, job.Request.Range.End, clip.OriginalDuration.TotalSeconds);
                clip.TrimTimeFromStart = TimeSpan.FromSeconds(range.Start); clip.TrimTimeFromEnd = clip.OriginalDuration - TimeSpan.FromSeconds(range.End);
                var composition = new MediaComposition(); composition.Clips.Add(clip);
                var sourceVideo = clip.GetVideoEncodingProperties();
                // Auto is a capture preset and leaves required encoding attributes unset.
                var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                profile.Video.Width = sourceVideo.Width; profile.Video.Height = sourceVideo.Height;
                profile.Video.FrameRate.Numerator = sourceVideo.FrameRate.Numerator; profile.Video.FrameRate.Denominator = sourceVideo.FrameRate.Denominator;
                profile.Video.Bitrate = Math.Clamp(sourceVideo.Bitrate, 500_000u, 40_000_000u);
                if (clip.EmbeddedAudioTracks.Count == 0) profile.Audio = null;
                var outputFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(job.Output)!);
                var file = await outputFolder.CreateFileAsync(Path.GetFileName(job.Output), CreationCollisionOption.FailIfExists);
                var operation = composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile);
                var reporting = new object(); var last = -1;
                operation.Progress = (_, value) => { lock (reporting) { var percent = (int)Math.Clamp(value, 0, 100); if (last != percent) { last = percent; File.WriteAllText(jobPath + ".progress", percent.ToString(CultureInfo.InvariantCulture)); } } };
                var reason = await operation;
                if (reason != TranscodeFailureReason.None) throw new IOException(T("系统编码器未能完成导出。") + " " + reason);
                app.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(jobPath + ".error", ex.Message + " [0x" + ex.HResult.ToString("X8") + "]"); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
