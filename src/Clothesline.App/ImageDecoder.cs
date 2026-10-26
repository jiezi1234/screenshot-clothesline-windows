using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

/// Only bounded raw BGRA pixels cross the process boundary; the UI never decodes source files.
internal static class ImageDecoder
{
    internal sealed record Metadata(int Width, int Height, double DpiX, double DpiY, string Hash);
    private sealed record Cached(string Key, string Hash, BitmapSource Thumbnail);
    private static readonly object cacheGate = new();
    private static readonly LinkedList<Cached> cache = new();
    private static readonly SemaphoreSlim workers = new(2);
    private sealed record Job(string Path, string Directory, string Mode, string Language);
    private sealed record Server(Process Process, string Directory, int Jobs = 0, long LastUsed = 0);
    private static readonly System.Collections.Concurrent.ConcurrentBag<Server> servers = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Process> allServers = new();
    static ImageDecoder()
    {
        CleanupStale(Path.GetTempPath());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { while (servers.TryTake(out var server)) CloseServer(server); };
    }
    internal static void CleanupStale(string root)
    {
        // Only our fixed filenames in old GUID directories. Never recurse or follow reparse points.
        try
        {
            foreach (var folder in new DirectoryInfo(root).EnumerateDirectories("Clothesline-decod*"))
            {
                try
                {
                    if ((folder.Attributes & FileAttributes.ReparsePoint) != 0 || folder.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-2)
                        || !System.Text.RegularExpressions.Regex.IsMatch(folder.Name, "^Clothesline-decode(r)?-[0-9a-f]{32}$")) continue;
                    foreach (var name in new[] { "result.json", "pixels.bgra", "error.txt", "complete", "job.json", "job.tmp" })
                        try { File.Delete(Path.Combine(folder.FullName, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    try { folder.Delete(false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        // Cleanup is optional; an inaccessible or concurrently removed temp directory must not disable decoding.
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal sealed record WorkerResource(int Pid, double CpuMs, long PrivateBytes, long WorkingSet);
    internal static WorkerResource[] Resources()
    {
        var result = new List<WorkerResource>();
        foreach (var process in allServers.Values)
            try { if (!process.HasExited) { process.Refresh(); result.Add(new(process.Id, process.TotalProcessorTime.TotalMilliseconds, process.PrivateMemorySize64, process.WorkingSet64)); } }
            catch (InvalidOperationException) { }
        return result.ToArray();
    }
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    internal static int CacheCount { get { lock (cacheGate) return cache.Count; } }
    internal static void ClearCache() { lock (cacheGate) cache.Clear(); }
    private static string Key(string path) => Path.GetFullPath(path).ToUpperInvariant() + "|" + CollectionEngine.Signature(path);
    private static Cached? Find(string key)
    {
        lock (cacheGate)
        {
            var node = cache.First;
            while (node is not null)
            {
                if (node.Value.Key == key) { cache.Remove(node); cache.AddFirst(node); return node.Value; }
                node = node.Next;
            }
            return null;
        }
    }
    internal static bool TryThumbnail(string path, out BitmapSource? bitmap)
    { bitmap = Find(Key(path))?.Thumbnail; return bitmap is not null; }
    internal static async Task<string> Hash(string path, CancellationToken cancellation = default)
    { await Read(path, true, cancellation).ConfigureAwait(false); return Find(Key(path))?.Hash ?? throw new IOException(T("文件在读取期间发生变化。")); }
    internal static async Task<BitmapSource> Read(string path, bool thumbnail = false, CancellationToken cancellation = default)
    {
        path = Path.GetFullPath(path);
        var length = new FileInfo(path).Length;
        if (length > ImageFiles.MaxFileBytes) throw new IOException(T("图片超过 64 MiB 限制。"));
        var key = Key(path);
        if (thumbnail && Find(key) is { } hit) return hit.Thumbnail;
        await workers.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (thumbnail && Find(key) is { } cached) return cached.Thumbnail;
            var result = await Decode(path, thumbnail, cancellation).ConfigureAwait(false);
            if (key != Key(path)) throw new IOException(T("文件在读取期间发生变化。"));
            var small = thumbnail ? result.Image : Thumbnail(result.Image);
            lock (cacheGate)
            {
                var previous = cache.First;
                while (previous is not null) { var next = previous.Next; if (previous.Value.Key == key) cache.Remove(previous); previous = next; }
                cache.AddFirst(new Cached(key, result.Info.Hash, small));
                // 64 * 420 * 420 * 4 <= 44 MiB. Full-size images are never retained here.
                while (cache.Count > 64) cache.RemoveLast();
            }
            return result.Image;
        }
        finally { workers.Release(); }
    }
    internal static BitmapSource Thumbnail(BitmapSource image)
    {
        var ratio = 420.0 / Math.Max(image.PixelWidth, image.PixelHeight);
        if (ratio >= 1) return image;
        var scaled = new TransformedBitmap(image, new ScaleTransform(ratio, ratio));
        // Detach from the full bitmap so a thumbnail cannot retain 160 MB through its transform.
        var stride = scaled.PixelWidth * 4; var bytes = new byte[stride * scaled.PixelHeight]; scaled.CopyPixels(bytes, stride, 0);
        var result = BitmapSource.Create(scaled.PixelWidth, scaled.PixelHeight, 96, 96, PixelFormats.Bgra32, null, bytes, stride); result.Freeze(); return result;
    }
    private static async Task<(BitmapSource Image, Metadata Info)> Decode(string path, bool thumbnail, CancellationToken cancellation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Clothesline-decode-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            await DecodeOnServer(new Job(path, directory, thumbnail ? "thumbnail" : "full", UiText.Language), cancellation).ConfigureAwait(false);
            var metadataFile = Path.Combine(directory, "result.json");
            if (!File.Exists(metadataFile) || new FileInfo(metadataFile).Length > 8192) throw new IOException(T("图片解析未返回有效结果，原文件保留。"));
            var info = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metadataFile, cancellation).ConfigureAwait(false)) ?? throw new IOException(T("图片解析未返回有效结果，原文件保留。"));
            var file = Path.Combine(directory, "pixels.bgra");
            Validate(info, new FileInfo(file).Length, thumbnail);
            var pixels = await File.ReadAllBytesAsync(file, cancellation).ConfigureAwait(false);
            var image = BitmapSource.Create(info.Width, info.Height, info.DpiX, info.DpiY, PixelFormats.Bgra32, null, pixels, checked(info.Width * 4)); image.Freeze(); return (image, info);
        }
        catch (IOException) when (File.Exists(Path.Combine(directory, "error.txt")) && new FileInfo(Path.Combine(directory, "error.txt")).Length <= 8192)
        { throw new IOException(await File.ReadAllTextAsync(Path.Combine(directory, "error.txt"), cancellation).ConfigureAwait(false)); }
        finally
        {
            foreach (var name in new[] { "result.json", "pixels.bgra", "error.txt", "complete" })
                try { File.Delete(Path.Combine(directory, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(directory, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private static void CloseServer(Server server)
    {
        allServers.TryRemove(server.Process.Id, out _);
        try { if (!server.Process.HasExited) { server.Process.Kill(true); server.Process.WaitForExit(2000); } } catch (InvalidOperationException) { }
        server.Process.Dispose();
        foreach (var name in new[] { "job.json", "job.tmp" }) try { File.Delete(Path.Combine(server.Directory, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { Directory.Delete(server.Directory, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static void StopIdleWorkers() { while (servers.TryTake(out var server)) CloseServer(server); }
    private static async Task DecodeOnServer(Job job, CancellationToken cancellation, TimeSpan? timeout = null, long maxBytes = 768L * 1024 * 1024)
    {
        Server? server = null;
        while (servers.TryTake(out var candidate)) { if (!candidate.Process.HasExited && Environment.TickCount64 - candidate.LastUsed < 55000) { server = candidate; break; } CloseServer(candidate); }
        if (server is null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "Clothesline-decoder-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            server = new Server(Process.Start(VideoCatalog.StartInfo("--image-server", directory)) ?? throw new IOException(T("无法启动图片解析。")), directory);
            allServers[server.Process.Id] = server.Process;
        }
        var healthy = false;
        try
        {
            var request = Path.Combine(server.Directory, "job.json");
            await File.WriteAllTextAsync(Path.Combine(server.Directory, "job.tmp"), JsonSerializer.Serialize(job), cancellation).ConfigureAwait(false);
            File.Move(Path.Combine(server.Directory, "job.tmp"), request, false);
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(job.Directory, "complete")))
            {
                cancellation.ThrowIfCancellationRequested();
                if (server.Process.HasExited) throw new IOException(T("图片解析失败，原文件保留。"));
                if (watch.Elapsed >= (timeout ?? Timeout)) throw new IOException(T("图片解析超时，原文件保留，可稍后重试。"));
                server.Process.Refresh();
                if (server.Process.PrivateMemorySize64 > maxBytes) throw new IOException(T("图片解析超过内存限制，原文件保留。"));
                await Task.Delay(15, cancellation).ConfigureAwait(false);
            }
            healthy = true; server = server with { Jobs = server.Jobs + 1, LastUsed = Environment.TickCount64 };
            if (File.Exists(Path.Combine(job.Directory, "error.txt"))) throw new IOException(T("图片解析失败，原文件保留。"));
        }
        finally { if (healthy && server.Jobs < 64) servers.Add(server); else CloseServer(server); }
    }
    internal static int Serve(string directory)
    {
        var request = Path.Combine(directory, "job.json"); var idle = Stopwatch.StartNew();
        try
        {
            while (idle.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (!File.Exists(request)) { Thread.Sleep(idle.ElapsedMilliseconds < 2000 ? 15 : 80); continue; }
                var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(request)) ?? throw new IOException("Invalid decoder request.");
                File.Delete(request); UiText.Language = job.Language;
                Worker(job.Path, job.Directory, job.Mode);
                File.WriteAllText(Path.Combine(job.Directory, "complete"), "done");
                // Release WIC buffers between jobs. Recycle the process after 64 files to bound native accumulation.
                GC.Collect(); GC.WaitForPendingFinalizers(); idle.Restart();
            }
            return 0;
        }
        catch { return 1; }
    }
    internal static void Validate(Metadata info, long bytes, bool thumbnail)
    {
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > ImageFiles.MaxPixels || bytes != (long)info.Width * info.Height * 4
            || !double.IsFinite(info.DpiX) || !double.IsFinite(info.DpiY) || info.DpiX <= 0 || info.DpiY <= 0 || info.DpiX > 100000 || info.DpiY > 100000
            || info.Hash is null || info.Hash.Length != 64 || !info.Hash.All(Uri.IsHexDigit) || (thumbnail && Math.Max(info.Width, info.Height) > 420))
            throw new IOException(T("图片解析未返回有效结果，原文件保留。"));
    }
    internal static async Task DiagnosticFault(string directory, string mode, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        await workers.WaitAsync(cancellation);
        try
        {
            await DecodeOnServer(new Job("", directory, mode == "crash" ? "test-crash" : "test-hang", UiText.Language), cancellation,
                mode == "timeout" ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(5), mode == "memory" ? 1024 : 768L * 1024 * 1024);
        }
        finally { workers.Release(); }
    }
    internal static int Worker(string path, string directory, string mode)
    {
        // A parent crash must not leave a native decoder running indefinitely.
        using var lifetime = new Timer(_ => Environment.Exit(2), null, TimeSpan.FromSeconds(30), System.Threading.Timeout.InfiniteTimeSpan);
        try
        {
            if (mode == "test-crash") Environment.Exit(7);
            if (mode == "test-hang") Thread.Sleep(30000);
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoded = ImageFiles.ReadLocal(source, path);
            var hash = ImageFiles.Hash(decoded);
            BitmapSource image = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0); image.Freeze();
            if (mode == "thumbnail") image = Thumbnail(image);
            var stride = checked(image.PixelWidth * 4); var pixels = new byte[checked(stride * image.PixelHeight)]; image.CopyPixels(pixels, stride, 0);
            File.WriteAllBytes(Path.Combine(directory, "pixels.bgra"), pixels);
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new Metadata(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, hash)));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message); return 1; }
    }
}
