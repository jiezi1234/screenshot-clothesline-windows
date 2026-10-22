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
    private readonly HashSet<ImageWorkspace> workspaces = new();
    private readonly Dictionary<string, int> leases = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? Status;
    public event Action? Changed;
    public int Capacity => Settings.Capacity == 0 ? shelf.DisplayCapacity : Settings.Capacity;
    public LibraryRuntime(ShelfController shelf, string? root = null)
    {
        this.shelf = shelf; Store = new LibraryStore(root ?? DefaultRoot); Settings = Store.LoadSettings();
        if (!Store.HasSettings && root is null) { Settings = Settings with { ScreenshotFolder = ScreenshotFolder() }; Store.SaveSettings(Settings); }
        Collector = new CollectionEngine(Store, ImageFiles.Hash, () => Capacity);
        clipboard = new ClipboardCollector(async (bytes, hash) => { if (!disposed && !Settings.Paused) await Collector.ImportClipboard(bytes, hash, Settings.MaxManagedBytes, closing.Token); });
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        refresh.Tick += (_, _) => { if (shelf.Busy || fileOperation > 0) return; refresh.Stop(); RefreshShelf(revealPending); revealPending = false; };
        maintenance = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        maintenance.Tick += (_, _) => { if (!shelf.Busy) { Cleanup(); Collector.RequestRescan(); ScheduleRefresh(false); } };
        Collector.Changed += () => ScheduleRefresh(true);
        Collector.Status += Report; clipboard.Status += Report;
        shelf.Archived += item => { if (item.EntryId is not null) { Store.Archive(item.EntryId, Now); ScheduleRefresh(false); } };
        shelf.RecycleRequested += item => { if (item.EntryId is not null) Recycle(item.EntryId); };
        shelf.ExportRequested += (item, desktop) => Export(item.Path, desktop);
        shelf.WorkspaceRequested += (item, edit) => { if (item.EntryId is not null) OpenWorkspace(item.EntryId, edit); };
        shelf.BatchArchiveRequested += (item, scope) => ArchiveBatch(item.EntryId, scope);
        shelf.FilesChanged += () => ScheduleRefresh(false);
    }
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Start() { shelf.Start(false); ApplySettings(Settings, false); RefreshShelf(false); maintenance.Start(); }
    public void ApplySettings(LibrarySettings settings, bool updateDesktop = true)
    {
        if (fileOperation > 0) throw new InvalidOperationException("文件操作进行中，请稍后再保存设置。");
        settings = settings with { Shortcut = ShortcutGesture.Parse(settings.Shortcut).Display };
        LibraryStore.ValidateSettings(settings);
        var all = new[] { settings.ScreenshotFolder }.Concat(settings.ExtraFolders).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath);
        foreach (var folder in all)
            if (folder.Equals(Store.Root, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("不能将应用数据目录用作截图来源。");
        if (updateDesktop && Desktop is not null) Desktop.Apply(settings, Store.SaveSettings); else Store.SaveSettings(settings);
        Settings = settings;
        shelf.ReduceMotion = settings.ReduceMotion; shelf.SuppressFullscreen = settings.SuppressFullscreen;
        Collector.Configure(settings); clipboard.Enable(settings.ClipboardEnabled && !settings.Paused);
        Store.EnforceCapacity(Capacity, Now); ScheduleRefresh(false);
        Report(settings.Paused ? "收集已暂停；恢复后会补扫新增文件。" : "设置已保存，开始监听新增图片；首次选择目录不会导入旧文件。");
    }
    public void TogglePause() => ApplySettings(Settings with { Paused = !Settings.Paused }, false);
    public async Task ImportFiles(string[] paths)
    {
        var count = 0;
        foreach (var path in paths)
        {
            if (disposed) break;
            try { if (await Collector.ImportExternal(path, cancellation: closing.Token)) count++; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Report("未导入 " + Path.GetFileName(path) + "：" + ex.Message); }
        }
        if (!disposed) Report($"导入完成，共处理 {count} 个文件。重复内容会合并。");
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
                if (!cache.TryGetValue(entry.Id, out var cached) || cached.Signature != signature)
                    cache[entry.Id] = (signature, new DemoItem(entry.Title, (entry.Ownership == Ownership.Managed ? "应用保存" : "外部引用") + " · 滚轮翻页", entry.Path, ImageFiles.Read(entry.Path, true), entry.Id));
                items.Add(cache[entry.Id].Item);
            }
            catch (Exception ex) { Store.SetError(entry.Id, ex.Message); Store.Archive(entry.Id, Now); }
        }
        foreach (var id in cache.Keys.Except(items.Select(i => i.EntryId!)).ToArray()) cache.Remove(id);
        shelf.SetItems(items, reveal); Changed?.Invoke();
    }
    public void Archive(string id) { Store.Archive(id, Now); ScheduleRefresh(false); }
    public void ArchiveBatch(string? anchor = null, ArchiveScope scope = ArchiveScope.All)
    {
        var ordered = Store.Entries(EntryState.Shelf).Select(e => e.Id).ToArray();
        var ids = anchor is null && scope == ArchiveScope.All ? ordered : BatchSelection.Select(ordered, anchor ?? "", scope);
        var count = Store.ArchiveMany(ids, Now);
        Report($"已取下 {count} 张图片到本地历史，原文件保留。"); ScheduleRefresh(false);
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
        var gallery = Store.Entries(entry.State).Where(e => !e.Missing && File.Exists(e.Path)).Select(e => new WorkspaceImage(e.Id, e.Path, e.Title, e.Hash)).ToArray();
        var index = Array.FindIndex(gallery, e => e.Id == id);
        if (index < 0) { Report("原文件已失效，无法打开。请在历史中检查路径。"); return; }
        var window = new ImageWorkspace(gallery, index, edit, SaveEditedCopy, AcquireLease, ResolveOriginal);
        workspaces.Add(window); window.Closed += (_, _) => workspaces.Remove(window); window.Show(); window.Activate();
    }
    public bool TryCloseWorkspaces()
    {
        foreach (var window in workspaces.ToArray()) { window.Close(); if (workspaces.Contains(window)) return false; }
        return true;
    }
    internal OriginalReference? ResolveOriginal(WorkspaceImage image, string loadedHash)
    {
        if (loadedHash != image.Hash) return null; // External replacement starts a new lineage instead of recovering an unrelated image.
        var root = Store.OriginalFor(image.Id);
        return root is null ? null : new(root.Id, root.Path, root.Hash);
    }
    private async Task<SavedImage?> SaveEditedCopy(EditSaveRequest request, Window owner)
    {
        if (fileOperation > 0) throw new InvalidOperationException("另一项文件操作进行中，请稍后再保存。");
        fileOperation++;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(request.SourcePath) + "-edited.png", Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = false };
            if (dialog.ShowDialog(owner) != true) return null;
            return await PersistEditedCopy(request, dialog.FileName);
        }
        finally { fileOperation--; }
    }
    // The dialog caller holds fileOperation throughout this operation; diagnostics use an isolated, inactive runtime.
    internal async Task<SavedImage> PersistEditedCopy(EditSaveRequest request, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择应用数据目录以外的位置保存副本。");
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
                throw new InvalidOperationException("关联原版已失效或改变，请先检查历史中的原版快照。");
        }
        var path = await Task.Run(() => ImageFiles.ExportPng(output, destination));
        try
        {
            await Collector.ImportExternal(path, cancellation: closing.Token, preserveCopy: true);
            var entry = Store.Entries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return new(path, false, original, "原版快照保留在本地历史中，副本尚未建立关联。");
            Store.BindOriginal(entry.Id, original.Id);
            var hung = entry.State == EntryState.Shelf || Store.Restore(entry.Id, Capacity, Now);
            ScheduleRefresh(true); return new(path, hung, original);
        }
        catch (Exception ex) { Report("副本已保存，但挂上或原版关联未完成：" + ex.Message); return new(path, false, original, "原版快照保留在历史中；挂上或关联失败：" + ex.Message); }
    }
    public void Restore(string id)
    {
        Report(Store.Restore(id, Capacity, Now) ? "已重新挂上；历史计时已取消。" : "原文件已失效，无法重新挂上。"); ScheduleRefresh(true);
    }
    public void Forget(string id) { Store.ForgetExternalOrMissing(id); ScheduleRefresh(false); }
    public void Recycle(string id)
    {
        if (fileOperation > 0) return;
        fileOperation++;
        try
        {
        var entry = Store.Find(id); if (entry is null) return;
        if (Store.HasOriginalDependents(id)) { Report("仍有编辑副本依赖这份原版，请先移除相关副本记录。"); return; }
        if (InUse(entry.Path)) { Report("这张图片正在工作台中使用，请关闭对应窗口后再回收。"); return; }
        if (MessageBox.Show("将这个文件移入系统回收站？\n" + entry.Path, "删除文件", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Report(Store.Recycle(id, SafeRecycle.File) ? "文件已移入回收站。" : "未能回收，文件与记录已保留。请在历史中查看原因。"); ScheduleRefresh(false);
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
        if (count > 0) { Report($"已清理 {count} 条到期历史；外部原文件保留。"); ScheduleRefresh(false); }
        }
        finally { fileOperation--; }
    }
    public void Export(string source, bool desktop = false)
    {
        if (fileOperation > 0) return;
        fileOperation++;
        try
        {
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetFileName(source));
        if (!desktop)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(source), Filter = "原格式图片|*" + Path.GetExtension(source), OverwritePrompt = false };
            if (dialog.ShowDialog() != true) return; destination = dialog.FileName;
        }
        Report("已导出：" + ImageFiles.Export(source, destination));
        }
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
        disposed = true; closing.Cancel(); maintenance.Stop(); refresh.Stop(); clipboard.Dispose(); Collector.Dispose(); Store.Dispose(); closing.Dispose();
    }
}
