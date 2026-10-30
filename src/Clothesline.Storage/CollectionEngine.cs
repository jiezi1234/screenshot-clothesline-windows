using System.Collections.Concurrent;
using Clothesline.Core;

namespace Clothesline.Storage;

public sealed partial class CollectionEngine : IDisposable
{
    private readonly LibraryStore store;
    private readonly Func<string, string> imageHash;
    private readonly Func<string, CancellationToken, Task<string>>? imageHashAsync;
    private readonly Func<string, CancellationToken, Task<string>>? videoHash;
    private readonly Func<int> capacity;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim importLock = new(1);
    private readonly object watcherGate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly ConcurrentDictionary<string, (long Due, int Retry)> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task worker;
    private Task? videoImport;
    private Task? extendedImport;
    private readonly List<Task> imageImports = [];
    private string[] folders = [];
    private string[] imageFolders = [];
    private string videoFolder = "";
    private volatile bool paused;
    private int scanRequested;
    private long nextScan;
    public event Action? Changed;
    public event Action? CaptureAdded;
    public event Action<string>? Status;
    public static bool Supported(string path) => MediaFiles.IsImage(path) || MediaFiles.IsVideo(path);
    public static string Signature(string path) { var info = new FileInfo(path); return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}"; }
    public CollectionEngine(LibraryStore store, Func<string, string> imageHash, Func<int> capacity, Func<string, CancellationToken, Task<string>>? videoHash = null, Func<string, CancellationToken, Task<string>>? imageHashAsync = null)
    {
        this.store = store; this.imageHash = imageHash; this.capacity = capacity; this.videoHash = videoHash; this.imageHashAsync = imageHashAsync;
        worker = Task.Run(Work);
    }
    public void Configure(LibrarySettings settings)
    {
        paused = settings.Paused;
        var paths = new[] { settings.ScreenshotFolder }.Concat(settings.ExtraFolders).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length > 4) throw new ArgumentException(T("最多一个截图目录与三个额外目录。"));
        lock (watcherGate)
        {
            foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); imageFolders = paths;
            videoFolder = string.IsNullOrWhiteSpace(settings.VideoFolder) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.VideoFolder));
            folders = paths.Concat(videoFolder.Length == 0 ? [] : new[] { videoFolder }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var folder in folders) { if (!Directory.Exists(folder)) Baseline(folder, []); Attach(folder); }
            Interlocked.Exchange(ref scanRequested, 1);
        }
    }
    private bool Allowed(string path)
    {
        var folder = Path.GetDirectoryName(path);
        return MediaFiles.IsVideo(path) ? videoHash is not null && folder!.Equals(videoFolder, StringComparison.OrdinalIgnoreCase)
            : MediaFiles.IsImage(path) && imageFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);
    }
    private void Baseline(string folder, string[] files)
    {
        if (imageFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            store.InitializeSource(folder, files.Where(p => MediaFiles.IsImage(p) && !MediaFiles.IsExtendedImage(p)).Select(p => (p, Signature(p))));
            // Existing sources acquire a separate baseline on upgrade, without flooding the shelf with old HEIC/WebP files.
            store.InitializeSource("ExtendedImages:" + folder, files.Where(MediaFiles.IsExtendedImage).Select(p => (p, Signature(p))));
        }
        // Separate baseline: enabling video in an existing image directory must not import old recordings.
        if (folder.Equals(videoFolder, StringComparison.OrdinalIgnoreCase)) store.InitializeSource("Video:" + folder, files.Where(MediaFiles.IsVideo).Select(p => (p, Signature(p))));
    }
    private void Attach(string folder)
    {
        if (!Directory.Exists(folder)) { Status?.Invoke(T("等待目录可用：") + folder); return; }
        try
        {
            Baseline(folder, Directory.GetFiles(folder));
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 32768 };
            watcher.Created += (_, e) => Queue(e.FullPath);
            watcher.Changed += (_, e) => Queue(e.FullPath);
            watcher.Renamed += (_, e) => Queue(e.FullPath);
            watcher.Deleted += (_, _) => Changed?.Invoke();
            watcher.Error += (_, _) => { Interlocked.Exchange(ref scanRequested, 1); Status?.Invoke(T("目录监听需要补扫，正在恢复。")); };
            watchers.Add(watcher); watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status?.Invoke(T("目录暂不可用：") + ex.Message); }
    }
    private void Queue(string path)
    {
        lock (watcherGate) if (Allowed(path)) pending.AddOrUpdate(path, (Environment.TickCount64 + 100, 0), (_, old) => (Environment.TickCount64 + 100, old.Retry));
    }
    public void RequestRescan() => Interlocked.Exchange(ref scanRequested, 1);
    private void Rescan()
    {
        lock (watcherGate)
        {
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                if (!watchers.Any(w => w.Path.Equals(folder, StringComparison.OrdinalIgnoreCase))) Attach(folder);
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder).Where(Allowed))
                        if (!store.HasObserved(path, Signature(path))) pending.TryAdd(path, (Environment.TickCount64, 0));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status?.Invoke(T("补扫未完成：") + ex.Message); }
            }
        }
    }
    private async Task Work()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(80, stop.Token);
                if (paused) continue;
                if (Interlocked.Exchange(ref scanRequested, 0) == 1 || Environment.TickCount64 > nextScan) { nextScan = Environment.TickCount64 + 30000; Rescan(); }
                foreach (var pair in pending.Where(p => p.Value.Due <= Environment.TickCount64).OrderBy(p => MediaFiles.IsVideo(p.Key)).ToArray())
                {
                    imageImports.RemoveAll(task => task.IsCompleted);
                    if (!MediaFiles.IsVideo(pair.Key) && !MediaFiles.IsExtendedImage(pair.Key) && imageImports.Count >= 2) continue;
                    if (MediaFiles.IsVideo(pair.Key) && videoImport is { IsCompleted: false }) continue;
                    if (MediaFiles.IsExtendedImage(pair.Key) && extendedImport is { IsCompleted: false }) continue;
                    if (!pending.TryRemove(pair.Key, out var state)) continue;
                    lock (watcherGate) if (!Allowed(pair.Key)) continue;
                    if (MediaFiles.IsVideo(pair.Key)) videoImport = ProcessPending(pair.Key, state.Retry);
                    else if (MediaFiles.IsExtendedImage(pair.Key)) extendedImport = ProcessPending(pair.Key, state.Retry);
                    else imageImports.Add(ProcessPending(pair.Key, state.Retry));
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task ProcessPending(string path, int retry)
    {
        try { await ImportExternal(path, true, stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (MediaFiles.IsVideo(path) || MediaFiles.IsExtendedImage(path))
            {
                pending.TryAdd(path, (Environment.TickCount64 + (retry < 2 ? 30000 : 300000), retry + 1));
                Status?.Invoke((MediaFiles.IsVideo(path) ? T("录像暂未收集，稍后重试：") : T("扩展格式图片暂未收集，稍后重试：")) + Path.GetFileName(path) + " · " + ex.Message);
            }
            else if (retry < 9) pending.TryAdd(path, (Environment.TickCount64 + Math.Min(5000, 250 * (1 << Math.Min(retry, 4))), retry + 1));
            else Status?.Invoke(T("图片暂未收集，补扫时会重试：") + Path.GetFileName(path) + " · " + ex.Message);
        }
    }
    public async Task<bool> ImportExternal(string path, bool automatic = false, CancellationToken cancellation = default, bool preserveCopy = false)
    {
        path = Path.GetFullPath(path);
        if (!Supported(path)) return false;
        string? preparedSignature = null, preparedHash = null;
        // Decode every format before taking the commit lock: a bad PNG must not stall clipboard writes either.
        {
            if (!File.Exists(path) || (automatic && paused)) return false;
            preparedSignature = Signature(path);
            if (automatic && store.HasObserved(path, preparedSignature)) return false;
            await Task.Delay(MediaFiles.IsVideo(path) ? 1000 : 100, cancellation).ConfigureAwait(false);
            if (Signature(path) != preparedSignature) throw new IOException(T("文件仍在写入。"));
            // Optional codecs must not hold the ordinary image/clipboard commit lock.
            preparedHash = MediaFiles.IsVideo(path)
                ? videoHash is null ? throw new NotSupportedException(T("录像解析不可用。")) : await videoHash(path, cancellation).ConfigureAwait(false)
                : imageHashAsync is not null ? await imageHashAsync(path, cancellation).ConfigureAwait(false)
                : await Task.Run(() => imageHash(path), cancellation).ConfigureAwait(false);
        }
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return false;
            var before = Signature(path);
            if (automatic && store.HasObserved(path, before)) return false;
            if (preparedSignature is not null && preparedSignature != before) throw new IOException(T("文件在读取期间发生变化。"));
            if (preparedSignature is null) await Task.Delay(180, cancellation).ConfigureAwait(false);
            if (Signature(path) != before) throw new IOException(T("文件仍在写入。"));
            var hash = preparedHash ?? await Task.Run(() => imageHash(path), cancellation).ConfigureAwait(false);
            if (Signature(path) != before) throw new IOException(T("文件在读取期间发生变化。"));
            if (automatic && paused) return false;
            if (automatic) { lock (watcherGate) if (!Allowed(path)) return false; }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var source = "Folder:" + Path.GetDirectoryName(path);
            if (!store.IsDuplicate(path, hash, source, now, !preserveCopy)) { store.Add(path, Ownership.External, source, hash, now, capacity()); Changed?.Invoke(); if (automatic) CaptureAdded?.Invoke(); }
            store.Observe(path, before); return true;
        }
        finally { importLock.Release(); }
    }
    public Task<bool> ImportClipboard(byte[] png, string hash, long limit, CancellationToken cancellation = default)
        => ImportManaged(png, hash, limit, null, cancellation);
    public Task<bool> ImportText(string text, long limit, CancellationToken cancellation = default)
        => ImportManaged(TextContent.Encode(text), TextContent.Hash(text), limit, text, cancellation);
    public async Task<string> SaveText(string id, string text, string expectedHash, long limit, CancellationToken cancellation = default)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try { var hash = await Task.Run(() => store.SaveText(id, text, expectedHash, limit), cancellation).ConfigureAwait(false); Changed?.Invoke(); return hash; }
        finally { importLock.Release(); }
    }
    public async Task<LibraryEntry> SaveImageEdit(string id, byte[] png, string hash, string expectedDigest, string originalId, long limit, CancellationToken cancellation = default)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var entry = await Task.Run(() => store.SaveImageEdit(id, png, hash, expectedDigest, originalId, limit, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), cancellation).ConfigureAwait(false);
            Changed?.Invoke(); return entry;
        }
        finally { importLock.Release(); }
    }
    private async Task<bool> ImportManaged(byte[] png, string hash, long limit, string? text, CancellationToken cancellation)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (store.Entries().Any(e => e.Source != LibraryStore.OriginalSource && e.Hash == hash && now - e.AddedAt < 5000 && !e.Missing)) return false;
            var used = Directory.EnumerateFiles(store.ManagedFolder).Sum(p => new FileInfo(p).Length);
            if (used + png.Length > limit) { Status?.Invoke(T("自管内容空间不足，请增加上限或清理历史后重试。")); return false; }
            var id = Guid.NewGuid().ToString("N"); var path = Path.Combine(store.ManagedFolder, $"clip-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id}" + (text is null ? ".png" : ".txt"));
            store.Add(path, Ownership.Managed, text is null ? "Clipboard" : LibraryStore.TextSource, hash, now, capacity(), EntryState.Pending, id);
            try
            {
                using (var file = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.WriteAsync(png, cancellation).ConfigureAwait(false); file.Flush(true); }
                File.Move(path + ".part", path);
                if (text is not null) store.UpdateTextMetadata(id, text);
                store.CompleteManaged(id, capacity(), now); Changed?.Invoke(); CaptureAdded?.Invoke(); return true;
            }
            catch (Exception ex) { store.FailManaged(id, T("保存未完成：") + ex.Message); Changed?.Invoke(); throw; }
        }
        finally { importLock.Release(); }
    }
    public async Task<LibraryEntry> PreserveOriginal(byte[] png, string hash, long limit, CancellationToken cancellation = default)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var used = Directory.EnumerateFiles(store.ManagedFolder).Sum(p => new FileInfo(p).Length);
            if (used + png.Length > limit) throw new IOException(T("自管图片空间不足，无法保留可恢复原版。请增加上限或清理历史后再保存。"));
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(store.ManagedFolder, "original-" + id + ".png");
            store.Add(path, Ownership.Managed, LibraryStore.OriginalSource, hash, now, capacity(), EntryState.Pending, id);
            try
            {
                using (var file = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.WriteAsync(png, cancellation).ConfigureAwait(false); file.Flush(true); }
                File.Move(path + ".part", path); store.CompleteOriginal(id, now); Changed?.Invoke(); return store.Find(id)!;
            }
            catch (Exception ex) { store.FailManaged(id, T("原版保存未完成：") + ex.Message); Changed?.Invoke(); throw; }
        }
        finally { importLock.Release(); }
    }
    public void Dispose()
    {
        stop.Cancel(); lock (watcherGate) { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
        worker.GetAwaiter().GetResult();
        videoImport?.GetAwaiter().GetResult();
        extendedImport?.GetAwaiter().GetResult();
        Task.WhenAll(imageImports).GetAwaiter().GetResult();
        importLock.Wait(); importLock.Release();
        stop.Dispose();
    }
}
