using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed class LibraryRuntime : IDisposable
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "Library");
    public LibraryStore Store { get; }
    public LibrarySettings Settings { get; private set; }
    public CollectionEngine Collector { get; }
    public DesktopPreferences? Desktop { get; set; }
    internal VideoCatalog Videos { get; }
    private readonly ShelfController shelf;
    private readonly ClipboardCollector clipboard;
    private readonly DispatcherTimer refresh;
    private readonly DispatcherTimer maintenance;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, (string Signature, DemoItem Item)> cache = new();
    private readonly CancellationTokenSource closing = new();
    private bool revealPending;
    private bool disposed;
    private int fileOperation;
    private readonly CaptureSoundGate soundGate = new();
    private readonly HashSet<Window> workspaces = new();
    private readonly HashSet<string> videoLoads = new();
    private readonly HashSet<string> imageLoads = new();
    private readonly Dictionary<string, int> leases = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? Status;
    public event Action? Changed;
    public int Capacity => Settings.Capacity == 0 ? shelf.DisplayCapacity : Settings.Capacity;
    public LibraryRuntime(ShelfController shelf, string? root = null)
    {
        this.shelf = shelf; Store = new LibraryStore(root ?? DefaultRoot); Settings = Store.LoadSettings();
        UiText.Language = Settings.Language; shelf.RefreshLanguage();
        if (!Store.HasSettings && root is null) { Settings = Settings with { ScreenshotFolder = ScreenshotFolder() }; Store.SaveSettings(Settings); }
        Videos = new VideoCatalog(Store.Root);
        Collector = new CollectionEngine(Store, ImageFiles.Hash, () => Capacity, async (path, cancellation) => (await Videos.Get(path, cancellation).ConfigureAwait(false)).Hash, ImageDecoder.Hash);
        clipboard = new ClipboardCollector(async (bytes, hash) => { if (!disposed && !Settings.Paused) await Collector.ImportClipboard(bytes, hash, Settings.MaxManagedBytes, closing.Token); });
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        refresh.Tick += (_, _) => { if (shelf.Busy || fileOperation > 0) return; refresh.Stop(); RefreshShelf(revealPending); revealPending = false; };
        maintenance = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        maintenance.Tick += (_, _) => { if (!shelf.Busy) { Cleanup(); Collector.RequestRescan(); ScheduleRefresh(false); } };
        Collector.Changed += () => ScheduleRefresh(true);
        Collector.CaptureAdded += CaptureAdded;
        Collector.Status += Report; clipboard.Status += Report;
        shelf.Archived += item => { if (item.EntryId is not null) { Store.Archive(item.EntryId, Now); ScheduleRefresh(false); } };
        shelf.RecycleRequested += item => { if (item.EntryId is not null) Recycle(item.EntryId); };
        shelf.ExportRequested += (item, desktop) => Export(item.Path, desktop);
        shelf.WorkspaceRequested += (item, edit) => { if (item.EntryId is not null) OpenWorkspace(item.EntryId, edit); };
        shelf.BatchArchiveRequested += (item, scope) => ArchiveBatch(item.EntryId, scope);
        shelf.FilesChanged += () => ScheduleRefresh(false);
        shelf.EnvironmentRecovered += RecoverEnvironment;
    }
    private void RecoverEnvironment()
    {
        if (disposed) return;
        Collector.RequestRescan(); Desktop?.RetryShortcut(); ScheduleRefresh(false);
    }
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Start() { shelf.Start(false); ApplySettings(Settings, false); RefreshShelf(false); maintenance.Start(); _ = WarmDecoder(); }
    private async Task WarmDecoder()
    {
        var path = Path.Combine(Path.GetTempPath(), "Clothesline-warm-" + Guid.NewGuid().ToString("N") + ".png");
        try { await File.WriteAllBytesAsync(path, ImageFormatSupport.Fixture("format-probe.png"), closing.Token); await ImageDecoder.Read(path, true, closing.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
    public void ApplySettings(LibrarySettings settings, bool updateDesktop = true)
    {
        if (fileOperation > 0) throw new InvalidOperationException(T("文件操作进行中，请稍后再保存设置。"));
        if (shelf.Busy && settings.CardSizePercent != Settings.CardSizePercent) throw new InvalidOperationException(T("图片交互进行中，请稍后再更改卡片大小。"));
        settings = settings with { Shortcut = ShortcutGesture.Parse(settings.Shortcut).Display };
        LibraryStore.ValidateSettings(settings);
        var all = new[] { settings.ScreenshotFolder, settings.VideoFolder }.Concat(settings.ExtraFolders).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath);
        foreach (var folder in all)
            if (folder.Equals(Store.Root, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(T("不能将应用数据目录用作截图来源。"));
        if (updateDesktop && Desktop is not null) Desktop.Apply(settings, Store.SaveSettings); else Store.SaveSettings(settings);
        Settings = settings;
        shelf.ReduceMotion = settings.ReduceMotion; shelf.SuppressFullscreen = settings.SuppressFullscreen;
        shelf.SetCardSize(settings.CardSizePercent);
        Collector.Configure(settings); clipboard.Enable(settings.ClipboardEnabled && !settings.Paused);
        Store.EnforceCapacity(Capacity, Now); ScheduleRefresh(false);
        Report(settings.Paused ? T("收集已暂停；恢复后会补扫新增文件。") : T("设置已保存，开始监听新增图片与录像；首次选择目录不会导入旧文件。"));
    }
    public void TogglePause() => ApplySettings(Settings with { Paused = !Settings.Paused }, false);
    private void CaptureAdded()
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        // Capture the setting at arrival too: turning sound on cannot replay earlier captures.
        var enabled = Settings.CaptureSound;
        if (!enabled) return;
        dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed || !soundGate.ShouldPlay(Environment.TickCount64, enabled && Settings.CaptureSound, Settings.Paused, Native.IsFullscreen(Native.MonitorAt(Native.Cursor)))) return;
            try { System.Media.SystemSounds.Asterisk.Play(); }
            catch (Exception ex) { Report(T("提示音未播放：") + ex.Message); }
        }));
    }
    public async Task ImportFiles(string[] paths)
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var path in paths)
        {
            if (disposed) break;
            try { if (await Collector.ImportExternal(path, cancellation: closing.Token)) count++; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { var message = T("未导入 ") + Path.GetFileName(path) + "：" + ex.Message; failures.Add(message); Report(message); }
        }
        if (!disposed) Report(T("导入完成，共处理 {0} 个文件。重复内容会合并。", count) + (failures.Count > 0 ? "\n" + T("失败 {0} 个文件。", failures.Count) + "\n" + string.Join("\n", failures.Take(3)) : ""));
    }
    private void ScheduleRefresh(bool reveal)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke((Action)(() => { if (disposed) return; revealPending |= reveal && Settings.ShowNew; if (!refresh.IsEnabled) refresh.Start(); }));
    }
    public void RefreshShelf(bool reveal)
    {
        Store.EnforceCapacity(Capacity, Now);
        Store.RefreshMissing();
        var items = new List<DemoItem>();
        foreach (var entry in Store.Entries(EntryState.Shelf))
        {
            if (entry.Missing) { Store.Archive(entry.Id, Now); continue; }
            try
            {
                var signature = CollectionEngine.Signature(entry.Path);
                if (cache.TryGetValue(entry.Id, out var loading) && loading.Signature == "loading:" + signature && (videoLoads.Contains(entry.Id) || imageLoads.Contains(entry.Id))) { items.Add(loading.Item); continue; }
                if (!cache.TryGetValue(entry.Id, out var cached) || cached.Signature != signature)
                {
                    if (MediaFiles.IsVideo(entry.Path))
                    {
                        if (Videos.TryGet(entry.Path, out var video))
                            cache[entry.Id] = (signature, new DemoItem(entry.Title, T("录像") + " · " + video!.Duration + " · MP4", entry.Path, video.Image(), entry.Id));
                        else
                        {
                            cache[entry.Id] = ("loading:" + signature, new DemoItem(entry.Title, T("正在读取录像…"), entry.Path, VideoCatalog.Placeholder(), entry.Id));
                            if (videoLoads.Add(entry.Id)) _ = LoadVideo(entry, signature);
                        }
                    }
                    else if (ImageDecoder.TryThumbnail(entry.Path, out var image))
                        cache[entry.Id] = (signature, new DemoItem(entry.Title, (entry.Ownership == Ownership.Managed ? T("应用保存") : T("外部引用")) + T(" · 滚轮翻页"), entry.Path, image!, entry.Id));
                    else
                    {
                        cache[entry.Id] = ("loading:" + signature, new DemoItem(entry.Title, T("正在读取图片…"), entry.Path, ImagePlaceholder(), entry.Id));
                        if (imageLoads.Add(entry.Id)) _ = LoadImage(entry, signature);
                    }
                }
                items.Add(cache[entry.Id].Item);
            }
            catch (Exception ex) { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); }
        }
        foreach (var id in cache.Keys.Except(items.Select(i => i.EntryId!)).ToArray()) cache.Remove(id);
        shelf.SetItems(items, reveal); Changed?.Invoke();
    }
    private static System.Windows.Media.Imaging.BitmapSource? imagePlaceholder;
    private static System.Windows.Media.Imaging.BitmapSource ImagePlaceholder()
    {
        if (imagePlaceholder is not null) return imagePlaceholder;
        var bytes = new byte[] { 220, 230, 220, 255 };
        imagePlaceholder = BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bytes, 4); imagePlaceholder.Freeze(); return imagePlaceholder;
    }
    private async Task LoadImage(LibraryEntry entry, string signature)
    {
        try
        {
            var bitmap = await ImageDecoder.Read(entry.Path, true, closing.Token);
            if (!disposed && Store.Find(entry.Id)?.State == EntryState.Shelf && CollectionEngine.Signature(entry.Path) == signature)
                cache[entry.Id] = (signature, new DemoItem(entry.Title, (entry.Ownership == Ownership.Managed ? T("应用保存") : T("外部引用")) + T(" · 滚轮翻页"), entry.Path, bitmap, entry.Id));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!disposed && Store.Find(entry.Id) is not null && File.Exists(entry.Path) && CollectionEngine.Signature(entry.Path) == signature)
            { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); Report(ex.Message); }
        }
        finally { imageLoads.Remove(entry.Id); if (!disposed) ScheduleRefresh(false); }
    }
    private async Task LoadVideo(LibraryEntry entry, string signature)
    {
        try { await Videos.Get(entry.Path, closing.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!disposed && Store.Find(entry.Id) is not null && File.Exists(entry.Path) && CollectionEngine.Signature(entry.Path) == signature)
            { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); Report(ex.Message); }
        }
        finally { videoLoads.Remove(entry.Id); if (!disposed) ScheduleRefresh(false); }
    }
    public void Archive(string id) { Store.Archive(id, Now); ScheduleRefresh(false); }
    public void ArchiveBatch(string? anchor = null, ArchiveScope scope = ArchiveScope.All)
    {
        var ordered = Store.Entries(EntryState.Shelf).Select(e => e.Id).ToArray();
        var ids = anchor is null && scope == ArchiveScope.All ? ordered : BatchSelection.Select(ordered, anchor ?? "", scope);
        var count = Store.ArchiveMany(ids, Now);
        Report(T("已取下 {0} 个项目到本地历史，原文件保留。", count)); ScheduleRefresh(false);
    }
    internal IDisposable AcquireLease(string path)
    {
        path = Path.GetFullPath(path); leases.TryGetValue(path, out var count); leases[path] = count + 1;
        return new FileLease(() => { if (leases[path] == 1) leases.Remove(path); else leases[path]--; });
    }
    private sealed class FileLease(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() { var callback = action; action = null; callback?.Invoke(); }
    }
    private bool InUse(string path) => leases.ContainsKey(Path.GetFullPath(path));
    public void OpenWorkspace(string id, bool edit)
    {
        var entry = Store.Find(id); if (entry is null) return;
        if (MediaFiles.IsVideo(entry.Path))
        {
            var video = new VideoWorkspace(entry.Path, AcquireLease, SaveTrimmedCopy);
            workspaces.Add(video); video.Closed += (_, _) => workspaces.Remove(video); video.Show(); video.Activate(); return;
        }
        var gallery = Store.Entries(entry.State).Where(e => !MediaFiles.IsVideo(e.Path) && !e.Missing && File.Exists(e.Path)).Select(e => new WorkspaceImage(e.Id, e.Path, e.Title, e.Hash)).ToArray();
        var index = Array.FindIndex(gallery, e => e.Id == id);
        if (index < 0) { Report(T("原文件已失效，无法打开。请在历史中检查路径。")); return; }
        var window = new ImageWorkspace(gallery, index, edit, SaveEditedCopy, AcquireLease, ResolveOriginal);
        workspaces.Add(window); window.Closed += (_, _) => workspaces.Remove(window); window.Show(); window.Activate();
    }
    public bool TryCloseWorkspaces()
    {
        if (fileOperation > 0) { Report(T("文件操作进行中，请稍后再退出。")); return false; }
        foreach (var window in workspaces.ToArray()) { window.Close(); if (workspaces.Contains(window)) return false; }
        return true;
    }
    internal OriginalReference? ResolveOriginal(WorkspaceImage image, string loadedHash)
    {
        if (loadedHash != image.Hash) return null; // External replacement starts a new lineage instead of recovering an unrelated image.
        var root = Store.OriginalFor(image.Id);
        return root is null ? null : new(root.Id, root.Path, root.Hash);
    }
    private async Task<SavedVideo?> SaveTrimmedCopy(VideoTrimRequest request, Window owner, IProgress<double> progress, CancellationToken cancellation)
    {
        if (fileOperation > 0) throw new InvalidOperationException(T("另一项文件操作进行中，请稍后再保存。"));
        fileOperation++;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(request.SourcePath) + "-trimmed.mp4", Filter = T("MP4 录像|*.mp4"), DefaultExt = ".mp4", AddExtension = true, OverwritePrompt = false };
            if (dialog.ShowDialog(owner) != true) return null;
            return await PersistTrimmedCopy(request, dialog.FileName, progress, cancellation);
        }
        finally { fileOperation--; }
    }
    internal async Task<SavedVideo> PersistTrimmedCopy(VideoTrimRequest request, string destination, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.Equals(Store.Root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(T("请选择应用数据目录以外的位置保存副本。"));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, closing.Token);
        using var lease = AcquireLease(request.SourcePath);
        var path = await VideoTrimmer.Export(request, destination, Videos, progress, linked.Token);
        // The atomic rename is the commit point. A subsequent cancel or import failure never deletes the saved copy.
        try
        {
            await Collector.ImportExternal(path, cancellation: closing.Token, preserveCopy: true);
            var entry = Store.Entries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            var hung = entry is not null && (entry.State == EntryState.Shelf || Store.Restore(entry.Id, Capacity, Now));
            ScheduleRefresh(true); return new(path, hung, hung ? "" : T("副本已保存，请稍后手动导入。"));
        }
        catch (Exception ex) { Report(T("副本已保存，挂上未完成：") + ex.Message); return new(path, false, ex.Message); }
    }
    private async Task<SavedImage?> SaveEditedCopy(EditSaveRequest request, Window owner)
    {
        if (fileOperation > 0) throw new InvalidOperationException(T("另一项文件操作进行中，请稍后再保存。"));
        fileOperation++;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(request.SourcePath) + "-edited.png", Filter = T("PNG 图片|*.png"), DefaultExt = ".png", AddExtension = true, OverwritePrompt = false };
            if (dialog.ShowDialog(owner) != true) return null;
            return await PersistEditedCopy(request, dialog.FileName);
        }
        finally { fileOperation--; }
    }
    // The dialog caller holds fileOperation throughout this operation; diagnostics use an isolated, inactive runtime.
    internal async Task<SavedImage> PersistEditedCopy(EditSaveRequest request, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(T("请选择应用数据目录以外的位置保存副本。"));
        var output = await Task.Run(() => ImageFiles.Png(request.Image));
        var original = request.Original;
        if (original is null)
        {
            var snapshot = await Task.Run(() => (Png: ImageFiles.Png(request.SessionOriginal), Hash: ImageFiles.Hash(request.SessionOriginal)));
            var root = await Collector.PreserveOriginal(snapshot.Png, snapshot.Hash, Settings.MaxManagedBytes, closing.Token);
            original = new(root.Id, root.Path, root.Hash);
        }
        else
        {
            var root = Store.Find(original.Id);
            if (root?.Source != LibraryStore.OriginalSource || root.Path != original.Path || root.Hash != original.Hash || await Task.Run(() => ImageFiles.Hash(original.Path)) != original.Hash)
                throw new InvalidOperationException(T("关联原版已失效或改变，请先检查历史中的原版快照。"));
        }
        var path = await Task.Run(() => ImageFiles.ExportPng(output, destination));
        try
        {
            await Collector.ImportExternal(path, cancellation: closing.Token, preserveCopy: true);
            var entry = Store.Entries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return new(path, false, original, T("原版快照保留在本地历史中，副本尚未建立关联。"));
            Store.BindOriginal(entry.Id, original.Id);
            var hung = entry.State == EntryState.Shelf || Store.Restore(entry.Id, Capacity, Now);
            ScheduleRefresh(true); return new(path, hung, original);
        }
        catch (Exception ex) { Report(T("副本已保存，但挂上或原版关联未完成：") + ex.Message); return new(path, false, original, T("原版快照保留在历史中；挂上或关联失败：") + ex.Message); }
    }
    public void Restore(string id)
    {
        Report(Store.Restore(id, Capacity, Now) ? T("已重新挂上；历史计时已取消。") : T("原文件已失效，无法重新挂上。")); ScheduleRefresh(true);
    }
    public void Forget(string id) { Store.ForgetExternalOrMissing(id); ScheduleRefresh(false); }
    public void Recycle(string id)
    {
        if (fileOperation > 0) return;
        fileOperation++;
        try
        {
        var entry = Store.Find(id); if (entry is null) return;
        if (Store.HasOriginalDependents(id)) { Report(T("仍有编辑副本依赖这份原版，请先移除相关副本记录。")); return; }
        if (InUse(entry.Path)) { Report(T("这个文件正在使用，请关闭对应窗口后再回收。")); return; }
        if (MessageBox.Show(T("将这个文件移入系统回收站？\n") + entry.Path, T("删除文件"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Report(Store.Recycle(id, SafeRecycle.File) ? T("文件已移入回收站。") : T("未能回收，文件与记录已保留。请在历史中查看原因。")); ScheduleRefresh(false);
        }
        finally { fileOperation--; }
    }
    public void Cleanup()
    {
        if (shelf.Busy || fileOperation > 0) return;
        fileOperation++;
        try
        {
        var count = Store.Cleanup(Settings.RetentionDays, Now, SafeRecycle.File, e => InUse(e.Path));
        if (count > 0) { Report(T("已清理 {0} 条到期历史；外部原文件保留。", count)); ScheduleRefresh(false); }
        }
        finally { fileOperation--; }
    }
    public async void Export(string source, bool desktop = false)
    {
        if (fileOperation > 0) return;
        fileOperation++;
        try
        {
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetFileName(source));
        if (!desktop)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(source), Filter = T("原格式文件|*") + Path.GetExtension(source), OverwritePrompt = false };
            if (dialog.ShowDialog() != true) return; destination = dialog.FileName;
        }
        Report(T("已导出：") + await Task.Run(() => ImageFiles.Export(source, destination)));
        }
        catch (Exception ex) { Report(T("操作未完成：") + ex.Message); }
        finally { fileOperation--; }
    }
    private void Report(string text)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke((Action)(() => { if (!disposed) Status?.Invoke(text); }));
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
    private static string ScreenshotFolder()
    {
        var id = new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
        var result = SHGetKnownFolderPath(id, 0x4000, 0, out var text);
        try { return result >= 0 ? Marshal.PtrToStringUni(text)! : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"); }
        finally { if (text != 0) Marshal.FreeCoTaskMem(text); }
    }
    public void Dispose()
    {
        disposed = true; shelf.EnvironmentRecovered -= RecoverEnvironment; closing.Cancel(); maintenance.Stop(); refresh.Stop(); clipboard.Dispose(); Collector.Dispose(); Store.Dispose(); closing.Dispose();
    }
}
