using System.Collections.Concurrent;

namespace Clothesline.Storage;

public sealed class CollectionEngine : IDisposable
{
    private readonly LibraryStore store;
    private readonly Func<string, string> imageHash;
    private readonly Func<int> capacity;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim importLock = new(1);
    private readonly object watcherGate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly ConcurrentDictionary<string, (long Due, int Retry)> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task worker;
    private string[] folders = [];
    private volatile bool paused;
    private int scanRequested;
    private long nextScan;
    public event Action? Changed;
    public event Action<string>? Status;
    public static bool Supported(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static string Signature(string path) { var info = new FileInfo(path); return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}"; }
    public CollectionEngine(LibraryStore store, Func<string, string> imageHash, Func<int> capacity)
    {
        this.store = store; this.imageHash = imageHash; this.capacity = capacity;
        worker = Task.Run(Work);
    }
    public void Configure(LibrarySettings settings)
    {
        paused = settings.Paused;
        var paths = new[] { settings.ScreenshotFolder }.Concat(settings.ExtraFolders).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length > 4) throw new ArgumentException("最多一个截图目录与三个额外目录。");
        lock (watcherGate)
        {
            foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); folders = paths;
            foreach (var folder in paths) { if (!Directory.Exists(folder)) store.InitializeSource(folder, []); Attach(folder); }
            Interlocked.Exchange(ref scanRequested, 1);
        }
    }
    private void Attach(string folder)
    {
        if (!Directory.Exists(folder)) { Status?.Invoke("等待目录可用：" + folder); return; }
        try
        {
            var existing = Directory.EnumerateFiles(folder).Where(Supported).Select(p => (p, Signature(p))).ToArray();
            store.InitializeSource(folder, existing);
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 32768 };
            watcher.Created += (_, e) => Queue(e.FullPath);
            watcher.Changed += (_, e) => Queue(e.FullPath);
            watcher.Renamed += (_, e) => Queue(e.FullPath);
            watcher.Deleted += (_, _) => Changed?.Invoke();
            watcher.Error += (_, _) => { Interlocked.Exchange(ref scanRequested, 1); Status?.Invoke("目录监听需要补扫，正在恢复。"); };
            watchers.Add(watcher); watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status?.Invoke("目录暂不可用：" + ex.Message); }
    }
    private void Queue(string path)
    {
        if (Supported(path)) pending.AddOrUpdate(path, (Environment.TickCount64 + 180, 0), (_, old) => (Environment.TickCount64 + 180, old.Retry));
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
                    foreach (var path in Directory.EnumerateFiles(folder).Where(Supported))
                        if (!store.HasObserved(path, Signature(path))) pending.TryAdd(path, (Environment.TickCount64, 0));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status?.Invoke("补扫未完成：" + ex.Message); }
            }
        }
    }
    private async Task Work()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(120, stop.Token);
                if (paused) continue;
                if (Interlocked.Exchange(ref scanRequested, 0) == 1 || Environment.TickCount64 > nextScan) { nextScan = Environment.TickCount64 + 30000; Rescan(); }
                foreach (var pair in pending.Where(p => p.Value.Due <= Environment.TickCount64).ToArray())
                {
                    if (!pending.TryRemove(pair.Key, out var state)) continue;
                    lock (watcherGate) if (!folders.Any(f => string.Equals(Path.GetDirectoryName(pair.Key), f, StringComparison.OrdinalIgnoreCase))) continue;
                    try { await ImportExternal(pair.Key, true, stop.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (state.Retry < 9) pending.TryAdd(pair.Key, (Environment.TickCount64 + Math.Min(5000, 250 * (1 << Math.Min(state.Retry, 4))), state.Retry + 1));
                        else { Status?.Invoke("图片暂未收集，补扫时会重试：" + Path.GetFileName(pair.Key) + " · " + ex.Message); }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task<bool> ImportExternal(string path, bool automatic = false, CancellationToken cancellation = default, bool preserveCopy = false)
    {
        path = Path.GetFullPath(path);
        if (!Supported(path)) return false;
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return false;
            var before = Signature(path);
            if (automatic && store.HasObserved(path, before)) return false;
            await Task.Delay(180, cancellation).ConfigureAwait(false);
            if (Signature(path) != before) throw new IOException("文件仍在写入。");
            var hash = await Task.Run(() => imageHash(path), cancellation).ConfigureAwait(false);
            if (Signature(path) != before) throw new IOException("文件在读取期间发生变化。");
            if (automatic && paused) return false;
            if (automatic) { lock (watcherGate) if (!folders.Any(f => string.Equals(Path.GetDirectoryName(path), f, StringComparison.OrdinalIgnoreCase))) return false; }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var source = "Folder:" + Path.GetDirectoryName(path);
            if (!store.IsDuplicate(path, hash, source, now, !preserveCopy)) { store.Add(path, Ownership.External, source, hash, now, capacity()); Changed?.Invoke(); }
            store.Observe(path, before); return true;
        }
        finally { importLock.Release(); }
    }
    public async Task<bool> ImportClipboard(byte[] png, string hash, long limit, CancellationToken cancellation = default)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (store.Entries().Any(e => e.Source != LibraryStore.OriginalSource && e.Hash == hash && now - e.AddedAt < 5000 && !e.Missing)) return false;
            var used = Directory.EnumerateFiles(store.ManagedFolder).Sum(p => new FileInfo(p).Length);
            if (used + png.Length > limit) { Status?.Invoke("自管图片已达到存储上限，暂停新增剪贴板图片；可导出或清理历史后重试。"); return false; }
            var id = Guid.NewGuid().ToString("N"); var path = Path.Combine(store.ManagedFolder, $"clip-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id}.png");
            store.Add(path, Ownership.Managed, "Clipboard", hash, now, capacity(), EntryState.Pending, id);
            try
            {
                using (var file = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.WriteAsync(png, cancellation).ConfigureAwait(false); file.Flush(true); }
                File.Move(path + ".part", path);
                store.CompleteManaged(id, capacity(), now); Changed?.Invoke(); return true;
            }
            catch (Exception ex) { store.FailManaged(id, "保存未完成：" + ex.Message); Changed?.Invoke(); throw; }
        }
        finally { importLock.Release(); }
    }
    public async Task<LibraryEntry> PreserveOriginal(byte[] png, string hash, long limit, CancellationToken cancellation = default)
    {
        await importLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var used = Directory.EnumerateFiles(store.ManagedFolder).Sum(p => new FileInfo(p).Length);
            if (used + png.Length > limit) throw new IOException("自管图片空间不足，无法保留可恢复原版。请增加上限或清理历史后再保存。");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(store.ManagedFolder, "original-" + id + ".png");
            store.Add(path, Ownership.Managed, LibraryStore.OriginalSource, hash, now, capacity(), EntryState.Pending, id);
            try
            {
                using (var file = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.WriteAsync(png, cancellation).ConfigureAwait(false); file.Flush(true); }
                File.Move(path + ".part", path); store.CompleteOriginal(id, now); Changed?.Invoke(); return store.Find(id)!;
            }
            catch (Exception ex) { store.FailManaged(id, "原版保存未完成：" + ex.Message); Changed?.Invoke(); throw; }
        }
        finally { importLock.Release(); }
    }
    public void Dispose()
    {
        stop.Cancel(); lock (watcherGate) { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
        worker.GetAwaiter().GetResult();
        importLock.Wait(); importLock.Release();
        stop.Dispose();
    }
}
