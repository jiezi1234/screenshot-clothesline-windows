using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed class LibraryRuntime : IDisposable
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "Library");
    public LibraryStore Store { get; }
    public LibrarySettings Settings { get; private set; }
    public CollectionEngine Collector { get; }
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
        shelf.FilesChanged += () => ScheduleRefresh(false);
    }
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Start() { shelf.Start(false); ApplySettings(Settings); RefreshShelf(false); maintenance.Start(); }
    public void ApplySettings(LibrarySettings settings)
    {
        if (fileOperation > 0) throw new InvalidOperationException("文件操作进行中，请稍后再保存设置。");
        var all = new[] { settings.ScreenshotFolder }.Concat(settings.ExtraFolders).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath);
        foreach (var folder in all)
            if (folder.Equals(Store.Root, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("不能将应用数据目录用作截图来源。");
        Store.SaveSettings(settings); Settings = settings;
        shelf.ReduceMotion = settings.ReduceMotion; shelf.SuppressFullscreen = settings.SuppressFullscreen;
        Collector.Configure(settings); clipboard.Enable(settings.ClipboardEnabled && !settings.Paused);
        Store.EnforceCapacity(Capacity, Now); ScheduleRefresh(false);
        Report(settings.Paused ? "收集已暂停；恢复后会补扫新增文件。" : "设置已保存，开始监听新增图片；首次选择目录不会导入旧文件。");
    }
    public void TogglePause() => ApplySettings(Settings with { Paused = !Settings.Paused });
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
        var count = Store.Cleanup(Settings.RetentionDays, Now, SafeRecycle.File);
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
