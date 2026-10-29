using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class LibraryDiagnostics
{
    public static int Run(Application app, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<object>(); var success = true;
        void Check(string name, bool passed, object? details = null) { success &= passed; checks.Add(new { name, passed, details }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                var fixtures = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
                var recycleProbe = Path.Combine(fixtures, "native-recycle-probe.txt"); File.WriteAllText(recycleProbe, "Clothesline generated test fixture");
                Check("native-recycle-generated-fixture", SafeRecycle.File(recycleProbe) && !File.Exists(recycleProbe));
                var watched = Path.Combine(fixtures, "watched"); Directory.CreateDirectory(watched);
                var original = DemoAssets.Create(Path.Combine(fixtures, "original"))[0];
                using var shelf = new ShelfController();
                using var runtime = new LibraryRuntime(shelf, Path.Combine(fixtures, "library"));
                runtime.ApplySettings(new LibrarySettings { ScreenshotFolder = watched, Capacity = 25, ShowNew = false });
                runtime.Start();
                for (var i = 0; i < 100; i++)
                {
                    var pixels = new byte[64 * 64 * 4];
                    for (var p = 0; p < pixels.Length; p += 4) { pixels[p] = (byte)i; pixels[p + 1] = (byte)(p % 251); pixels[p + 2] = 100; pixels[p + 3] = 255; }
                    var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
                    DemoAssets.Save(bitmap, Path.Combine(watched, $"capture-{i:D3}.png"));
                }
                var until = DateTime.UtcNow.AddSeconds(60);
                while (runtime.Store.Entries().Count < 100 && DateTime.UtcNow < until) await Task.Delay(150);
                Check("100-valid-pngs-collected", runtime.Store.Entries().Count == 100, runtime.Store.Entries().Count);
                Check("capacity25-media12-history88", runtime.Store.Entries(EntryState.Shelf).Count == 12 && runtime.Store.Entries(EntryState.History).Count == 88);
                Check("source100-files-preserved", Directory.GetFiles(watched, "*.png").Length == 100);
                var historical = runtime.Store.Entries(EntryState.History).First();
                Check("restore-real-image", runtime.Store.Restore(historical.Id, 25, LibraryRuntime.Now));
                runtime.RefreshShelf(false); Check("shelf-thumbnail-count", shelf.Count == 12);
                var dragEntry = runtime.Store.Entries(EntryState.Shelf).First();
                var dragItem = new DemoItem(dragEntry.Title, "生成的拖拽测试图片", dragEntry.Path, ImageFiles.Read(dragEntry.Path, true), dragEntry.Id);
                var dragHash = ImageFiles.Hash(dragEntry.Path);
                var droppedCopy = Path.Combine(fixtures, "dropped-copy.png");
                ShellTransfer.Drag(0, dragItem, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) =>
                {
                    Check("drag-offers-copy-without-move-or-link", (allowed & 1) != 0 && (allowed & 6) == 0);
                    var file = DragFile(data);
                    Check("drag-shell-data-contains-readable-original", file == dragEntry.Path && ImageFiles.Hash(file) == dragHash);
                    File.Copy(file, droppedCopy); effect = 1; return 0x40100;
                });
                bool Retained() => File.Exists(dragEntry.Path) && ImageFiles.Hash(dragEntry.Path) == dragHash && runtime.Store.Find(dragEntry.Id)?.State == EntryState.Shelf && shelf.Count == 12;
                runtime.RefreshShelf(false);
                Check("drag-copy-preserves-original-and-shelf-entry", Retained() && ImageFiles.Hash(droppedCopy) == dragHash);
                ShellTransfer.Drag(0, dragItem, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) => { effect = 2; return 0x40101; });
                runtime.RefreshShelf(false); Check("cancelled-drag-preserves-original-and-shelf-entry", Retained());
                ShellTransfer.Drag(0, dragItem, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) => { effect = 2; return 0x40100; });
                runtime.RefreshShelf(false); Check("unexpected-move-result-preserves-original-and-shelf-entry", Retained());
                var dragFailed = false;
                try { ShellTransfer.Drag(0, dragItem, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) => { effect = 0; return unchecked((int)0x80004005); }); }
                catch (COMException) { dragFailed = true; }
                runtime.RefreshShelf(false); Check("failed-drag-preserves-original-and-shelf-entry", dragFailed && Retained());
                var thumbnail = ImageFiles.Read(original.Path, true);
                Check("thumbnail-bounded-original-unchanged", thumbnail.PixelWidth <= 420 && ImageFiles.Read(original.Path).PixelWidth == 960);
                var copy1 = ImageFiles.Export(original.Path, Path.Combine(fixtures, "export.png"));
                var copy2 = ImageFiles.Export(original.Path, Path.Combine(fixtures, "export.png"));
                Check("export-never-overwrites", copy1 != copy2 && ImageFiles.Hash(copy1) == ImageFiles.Hash(copy2));
                var sourceHash = ImageFiles.Hash(original.Path);
                Check("clipboard-canonical-png-roundtrip", await runtime.Collector.ImportClipboard(ImageFiles.Png(original.Image), sourceHash, 1024 * 1024));
                var owned = runtime.Store.Entries().Single(e => e.Source == "Clipboard");
                Check("managed-file-readable-original-dimensions", ImageFiles.Read(owned.Path).PixelWidth == 960);
                var beforeSecondSource = runtime.Store.Entries().Count;
                await runtime.Collector.ImportExternal(original.Path);
                Check("clipboard-first-file-duplicate-merges", runtime.Store.Entries().Count == beforeSecondSource);
                runtime.Store.Archive(owned.Id, LibraryRuntime.Now - (long)TimeSpan.FromDays(8).TotalMilliseconds);
                var recycled = runtime.Store.Cleanup(7, LibraryRuntime.Now, SafeRecycle.File);
                Check("native-recycle-managed-expiry", recycled == 1 && !File.Exists(owned.Path), runtime.Store.Find(owned.Id)?.Error);
                var sink = new SafeRecycle.RecycleSink();
                Check("native-sink-rejects-permanent-delete", sink.PreDeleteItem(0, 0) < 0 && sink.Failed);
                runtime.RefreshShelf(false);
                // Render example configuration rather than publishing any machine-specific paths.
                runtime.ApplySettings(runtime.Settings with { ScreenshotFolder = @"C:\Example\Screenshots", Paused = true });
                var dashboard = new Dashboard(shelf, true, () => { }, runtime) { Exiting = true };
                dashboard.Show(); await Task.Delay(200); Render((FrameworkElement)dashboard.Content, Path.Combine(output, "stage1-settings.png"));
                dashboard.ShowHistory(); await Task.Delay(100); Render((FrameworkElement)dashboard.Content, Path.Combine(output, "stage1-history.png"));
                dashboard.Close();
                Check("clipboard-privacy-default-off", !runtime.Settings.ClipboardEnabled);
            }
            catch (Exception ex) { Check("diagnostics-completed", false, ex.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "stage1-diagnostics.json"), JsonSerializer.Serialize(new { passed = success, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(success ? 0 : 1);
            }
        }));
        return app.Run();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder? path, uint size);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    private static string DragFile(System.Runtime.InteropServices.ComTypes.IDataObject data)
    {
        var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        data.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || DragQueryFileW(medium.unionmember, uint.MaxValue, null, 0) != 1) throw new InvalidOperationException("Expected one Shell file.");
            var path = new StringBuilder((int)DragQueryFileW(medium.unionmember, 0, null, 0) + 1);
            DragQueryFileW(medium.unionmember, 0, path, (uint)path.Capacity); return path.ToString();
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var offset = VisualTreeHelper.GetOffset(element);
        var width = element.ActualWidth + element.Margin.Left + element.Margin.Right; var height = element.ActualHeight + element.Margin.Top + element.Margin.Bottom;
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, new Rect(0, 0, width, height));
            var brush = new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(offset.X, offset.Y, element.ActualWidth, element.ActualHeight) };
            dc.DrawRectangle(brush, null, new Rect(element.Margin.Left, element.Margin.Top, element.ActualWidth, element.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
