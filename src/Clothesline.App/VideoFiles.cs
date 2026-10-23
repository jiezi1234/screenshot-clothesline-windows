using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;
using Windows.Media.Editing;
using Windows.Storage;

namespace Clothesline.App;

internal sealed record VideoInfo(string Hash, double Seconds, uint Width, uint Height, string VideoCodec, string[] AudioCodecs, byte[] Thumbnail)
{
    public string Duration => TimeSpan.FromSeconds(Seconds).ToString(Seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public BitmapSource Image()
    {
        using var stream = new MemoryStream(Thumbnail);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}

/// Native decoders run in a short-lived child process. The UI only reads bounded, generated thumbnails.
internal sealed class VideoCatalog
{
    private readonly string folder;
    private readonly SemaphoreSlim probeGate = new(1);
    private readonly ConcurrentDictionary<string, VideoInfo> memory = new();
    private readonly TimeSpan timeout;
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;
    public VideoCatalog(string root, TimeSpan? timeout = null)
    {
        folder = Path.Combine(root, "VideoCache"); Directory.CreateDirectory(folder);
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        foreach (var old in new DirectoryInfo(folder).GetFiles("*.tmp").Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddMinutes(-2)))
            try { old.Delete(); } catch (IOException) { }
    }
    private static string Key(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant() + "|" + CollectionEngine.Signature(path))));
    public bool TryGet(string path, out VideoInfo? info) => memory.TryGetValue(Key(path), out info);
    public async Task<VideoInfo> Get(string path, CancellationToken cancellation = default)
    {
        path = Path.GetFullPath(path);
        if (!MediaFiles.IsVideo(path)) throw new NotSupportedException(T("首批录像支持 MP4（H.264 视频，AAC 音频或无音轨）。"));
        var length = new FileInfo(path).Length;
        if (length == 0 || length > MaxBytes) throw new IOException(T("录像必须非空，且不超过 2 GiB。"));
        var key = Key(path);
        if (memory.TryGetValue(key, out var found)) return found;
        await probeGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (memory.TryGetValue(key, out found)) return found;
            var target = Path.Combine(folder, key + ".json");
            if (File.Exists(target))
            {
                try { found = Read(target); }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException) { File.Delete(target); }
            }
            if (found is null)
            {
                var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using var process = Process.Start(StartInfo("--video-probe", path, temporary, UiText.Language)) ?? throw new IOException(T("无法启动录像解析。"));
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(timeout);
                    try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) process.Kill(true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                        cancellation.ThrowIfCancellationRequested(); throw new IOException(T("录像解析超时，文件保留，稍后可重试。"));
                    }
                    if (process.ExitCode != 0)
                    {
                        var error = File.Exists(temporary) && new FileInfo(temporary).Length < 8192 ? JsonSerializer.Deserialize<string>(File.ReadAllText(temporary)) : T("系统媒体组件无法读取此录像。");
                        throw new IOException(error);
                    }
                    found = Read(temporary);
                    if (Key(path) != key) throw new IOException(T("文件在读取期间发生变化。"));
                    File.Move(temporary, target, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            memory[key] = found;
            foreach (var old in memory.Keys.Where(k => k != key).Take(Math.Max(0, memory.Count - 128))) memory.TryRemove(old, out _);
            // Cache is disposable; keep at most 128 bounded thumbnails, never source recordings.
            foreach (var old in new DirectoryInfo(folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(128))
                try { old.Delete(); } catch (IOException) { }
            return found;
        }
        finally { probeGate.Release(); }
    }
    private static VideoInfo Read(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Video cache exceeds size limit.");
        var info = JsonSerializer.Deserialize<VideoInfo>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid video cache.");
        if (info.Seconds <= 0 || !double.IsFinite(info.Seconds) || info.Width == 0 || info.Height == 0 || info.Thumbnail is null || info.Thumbnail.Length > 1024 * 1024 || info.Hash?.StartsWith("video:", StringComparison.Ordinal) != true)
            throw new InvalidDataException("Invalid video metadata.");
        var bitmap = info.Image(); if (bitmap.PixelWidth > 420 || bitmap.PixelHeight > 420) throw new InvalidDataException("Invalid video thumbnail.");
        return info;
    }
    internal static ProcessStartInfo StartInfo(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    internal static int Probe(Application app, string path, string result)
    {
        // Bound child lifetime even if its parent crashes while a native decoder is running.
        using var lifetime = new Timer(_ => Environment.Exit(2), null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                // Deny writers for the whole parse. A recorder that is still writing will be retried.
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (source.Length == 0 || source.Length > MaxBytes) throw new IOException(T("录像必须非空，且不超过 2 GiB。"));
                var hash = "video:" + Convert.ToHexString(await SHA256.HashDataAsync(source));
                var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
                var properties = clip.GetVideoEncodingProperties();
                var audio = clip.EmbeddedAudioTracks.Select(a => a.GetAudioEncodingProperties().Subtype).ToArray();
                if (!properties.Subtype.Equals("H264", StringComparison.OrdinalIgnoreCase) || audio.Any(a => !a.Equals("AAC", StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException(T("首批录像支持 MP4（H.264 视频，AAC 音频或无音轨）。") + $" [{properties.Subtype}; {string.Join(",", audio)}]");
                if (properties.Width == 0 || properties.Height == 0 || Math.Max(properties.Width, properties.Height) > 3840 || Math.Min(properties.Width, properties.Height) > 2160 || clip.OriginalDuration <= TimeSpan.Zero)
                    throw new NotSupportedException(T("录像尺寸须不超过 3840 × 2160（支持竖屏），且时长有效。"));
                var composition = new MediaComposition(); composition.Clips.Add(clip);
                using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(Math.Min(.1, clip.OriginalDuration.TotalSeconds / 2)), properties.Width >= properties.Height ? 420 : 0, properties.Height > properties.Width ? 420 : 0, VideoFramePrecision.NearestFrame);
                using var stream = thumbnail.AsStreamForRead(); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes);
                var info = new VideoInfo(hash, clip.OriginalDuration.TotalSeconds, properties.Width, properties.Height, properties.Subtype, audio, bytes.ToArray());
                File.WriteAllText(result, JsonSerializer.Serialize(info)); app.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(result, JsonSerializer.Serialize(T("无法读取录像：") + ex.Message)); app.Shutdown(1); }
        }));
        return app.Run();
    }
    internal static BitmapSource Placeholder()
    {
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(DemoAssets.Brush("#203B36"), null, new Rect(0, 0, 420, 240));
            draw.DrawGeometry(Brushes.White, null, Geometry.Parse("M180,85 L180,155 L245,120 Z"));
        }
        var bitmap = new RenderTargetBitmap(420, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
