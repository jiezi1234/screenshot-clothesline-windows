using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline.App;

internal static class EditorDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            ImageWorkspace? workspace = null;
            try
            {
                const int width = 480, height = 320;
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var p = (y * width + x) * 4; pixels[p] = (byte)(120 + x % 100); pixels[p + 1] = (byte)(100 + y % 100); pixels[p + 2] = (byte)(60 + (x + y) % 100); pixels[p + 3] = 255;
                }
                // Deliberately non-96 DPI: edits must use physical pixels regardless of metadata.
                var original = BitmapSource.Create(width, height, 144, 144, PixelFormats.Bgra32, null, pixels, width * 4); original.Freeze();
                var hash = ImageFiles.Hash(original); var source = Path.Combine(directory, "source.png"); DemoAssets.Save(original, source);
                var document = new ImageDocument(original);
                Check("initial-render-preserves-pixels-at-non96-dpi", ImageFiles.Hash(document.Render()) == hash && !document.IsDirty && !document.CanUndo);
                document.Add(document.CreateMark(EditorTool.Pen, [new(20, 20), new(180, 20)], Colors.Red, 6));
                Check("pen-renders-in-image-pixels", Pixel(document.Render(), 80, 20) == Colors.Red && document.IsDirty);
                var penHash = ImageFiles.Hash(document.Render()); document.MarkSaved(); document.Undo();
                Check("undo-to-original-after-save-is-dirty", document.IsDirty && ImageFiles.Hash(document.Render()) == hash && document.CanRedo);
                document.Redo(); Check("redo-to-saved-state-clears-dirty", !document.IsDirty && ImageFiles.Hash(document.Render()) == penHash);
                document.Add(document.CreateMark(EditorTool.Arrow, [new(30, 60), new(180, 60)], Colors.Blue, 6));
                Check("arrow-renders-shaft-and-head", Pixel(document.Render(), 80, 60) == Colors.Blue && Pixel(document.Render(), 165, 67).B > 220);
                document.Add(document.CreateMark(EditorTool.Rectangle, [new(200, 40), new(300, 100)], Colors.Lime, 6));
                Check("rectangle-renders-border-with-unfilled-center", Pixel(document.Render(), 250, 40) == Colors.Lime && Pixel(document.Render(), 250, 70) == Pixel(original, 250, 70));
                var beforeText = ImageFiles.Hash(document.Render());
                document.Add(document.CreateMark(EditorTool.Text, [new(20, 120)], Colors.Black, 6, "截图 / Notes"));
                Check("unicode-text-changes-render", ImageFiles.Hash(document.Render()) != beforeText);
                document.Add(document.CreateMark(EditorTool.Mosaic, [new(320, 40), new(450, 170)], Colors.Black, 4));
                var mosaic = document.Render();
                Check("mosaic-covers-selected-area-with-uniform-blocks", Pixel(mosaic, 321, 41) == Pixel(mosaic, 330, 50) && Pixel(mosaic, 321, 41) != Pixel(original, 321, 41) && Pixel(mosaic, 460, 40) == Pixel(original, 460, 40));
                var annotated = ImageFiles.Hash(document.Render());
                Check("crop-rejects-zero-area", !document.Crop(new Rect(10, 10, 0, 30)));
                Check("crop-uses-original-pixel-coordinates", document.Crop(new Rect(10, 10, 450, 280)) && document.Render().PixelWidth == 450 && document.Render().PixelHeight == 280 && Pixel(document.Render(), 70, 10) == Colors.Red);
                document.Undo(); Check("undo-crop-restores-all-annotations", document.State.Crop.Width == width && ImageFiles.Hash(document.Render()) == annotated);
                document.Redo(); document.Crop(new Rect(30, 30, 100, 100));
                Check("repeated-crop-retains-absolute-origin", document.State.Crop.X == 30 && document.State.Crop.Y == 30 && Pixel(document.Render(), 50, 30) == Colors.Blue);
                document.Undo(); document.Undo(); document.Add(document.CreateMark(EditorTool.Pen, [new(5, 5)], Colors.Red, 2));
                Check("new-mark-after-undo-clears-redo", !document.CanRedo);
                document.Reset(); Check("reset-restores-session-original", ImageFiles.Hash(document.Render()) == hash);
                document.Undo(); Check("reset-can-be-undone", ImageFiles.Hash(document.Render()) != hash);
                Check("editing-never-mutates-original-bitmap-or-file", ImageFiles.Hash(original) == hash && ImageFiles.Hash(source) == hash);
                var output = ImageFiles.ExportPng(ImageFiles.Png(document.Render()), source);
                Check("save-copy-does-not-overwrite-colliding-source", output != source && ImageFiles.Hash(source) == hash && ImageFiles.Hash(output) == ImageFiles.Hash(document.Render()));
                var extension = ImageFiles.ExportPng(ImageFiles.Png(document.Render()), Path.Combine(directory, "named.jpg"));
                Check("edited-output-is-png-with-matching-extension", Path.GetExtension(extension) == ".png" && ImageFiles.Hash(extension) == ImageFiles.Hash(document.Render()) && !Directory.GetFiles(directory, ".clothesline-*.tmp").Any());
                var leases = 0;
                workspace = new ImageWorkspace([new("fixture", source, "工作台示例.png")], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), _ => { leases++; return new Release(() => leases--); });
                workspace.Show();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((workspace.Loading || workspace.Document is null) && DateTime.UtcNow < deadline) await Task.Delay(40);
                Check("workspace-loads-own-fixture-and-acquires-lease", workspace.Document is not null && !workspace.Loading && leases == 1);
                Check("zoom-percentage-uses-physical-pixels-at-monitor-dpi", Math.Abs(workspace.CanvasScale * VisualTreeHelper.GetDpi(workspace).DpiScaleX - workspace.PhysicalMagnification) < .001);
                if (workspace.Document is not null)
                {
                    foreach (var mark in document.State.Marks) workspace.Document.Add(mark);
                    workspace.Document.MarkSaved(); await Task.Delay(100);
                    Render((FrameworkElement)workspace.Content, Path.Combine(directory, "image-workspace.png"));
                }
                workspace.Close(); workspace = null; Check("closing-workspace-releases-file-lease", leases == 0);
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(directory, "lease-library"));
                // Isolated store with no watched folders and clipboard collection disabled.
                var archived = runtime.Store.Add(source, Storage.Ownership.External, "test", hash, LibraryRuntime.Now, 100);
                runtime.Store.Archive(archived.Id, LibraryRuntime.Now - (long)TimeSpan.FromDays(8).TotalMilliseconds);
                var first = runtime.AcquireLease(source); var second = runtime.AcquireLease(source); first.Dispose(); first.Dispose();
                runtime.ApplySettings(new Storage.LibrarySettings { RetentionDays = 7 }); runtime.Cleanup();
                Check("multiple-workspace-leases-protect-until-last-close", runtime.Store.Find(archived.Id) is not null);
                second.Dispose(); runtime.Cleanup(); Check("last-lease-release-restores-cleanup", runtime.Store.Find(archived.Id) is null && File.Exists(source));
                var lineageDirectory = Path.Combine(directory, "lineage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(lineageDirectory);
                var lineageLibrary = Path.Combine(lineageDirectory, "library"); var externalOriginal = Path.Combine(lineageDirectory, "external.png"); DemoAssets.Save(original, externalOriginal);
                SavedImage saved, descendant; string descendantId;
                using (var lineageShelf = new ShelfController())
                using (var lineage = new LibraryRuntime(lineageShelf, lineageLibrary))
                {
                    saved = await lineage.PersistEditedCopy(new(document.Render(), original, externalOriginal, null), Path.Combine(lineageDirectory, "edited.png"));
                    Check("save-persists-independent-original-snapshot-and-linked-copy", saved.Hung && saved.Original is not null && ImageFiles.Hash(saved.Original.Path) == hash && ImageFiles.Hash(externalOriginal) == hash);
                    var child = new ImageDocument(ImageFiles.Read(saved.Path)); child.Crop(new Rect(30, 30, 100, 80));
                    descendant = await lineage.PersistEditedCopy(new(child.Render(), child.Original, saved.Path, saved.Original), Path.Combine(lineageDirectory, "edited-twice.png"));
                    descendantId = lineage.Store.Entries().Single(e => e.Path == descendant.Path).Id;
                    Check("editing-a-copy-retains-earliest-original-without-extra-snapshot", descendant.Original?.Id == saved.Original?.Id && lineage.Store.Entries().Count(e => e.Source == Storage.LibraryStore.OriginalSource) == 1);
                }
                File.Move(externalOriginal, externalOriginal + ".moved");
                using (var lineageShelf = new ShelfController())
                using (var lineage = new LibraryRuntime(lineageShelf, lineageLibrary))
                {
                    var entry = lineage.Store.Find(descendantId)!; var item = new WorkspaceImage(entry.Id, entry.Path, entry.Title, entry.Hash);
                    var reference = lineage.ResolveOriginal(item, ImageFiles.Hash(entry.Path));
                    Check("lineage-reopens-after-original-source-moved", reference?.Id == saved.Original!.Id && ImageFiles.Hash(reference.Path) == hash);
                    Check("external-content-replacement-does-not-inherit-stale-lineage", lineage.ResolveOriginal(item, "different-pixels") is null);
                    workspace = new ImageWorkspace([item], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), lineage.AcquireLease, lineage.ResolveOriginal);
                    workspace.Show(); deadline = DateTime.UtcNow.AddSeconds(10);
                    while ((workspace.Loading || workspace.Document is null) && DateTime.UtcNow < deadline) await Task.Delay(40);
                    var descendantHash = ImageFiles.Hash(workspace.Document!.Render());
                    await workspace.RestoreOriginal();
                    Check("workspace-restores-earliest-pixels-and-dimensions-after-restart", workspace.Document.IsDirty && ImageFiles.Hash(workspace.Document.Render()) == hash && workspace.Document.State.Crop.Width == width);
                    Render((FrameworkElement)workspace.Content, Path.Combine(directory, "restored-original.png"));
                    workspace.Document.Undo(); Check("original-restoration-is-undoable", ImageFiles.Hash(workspace.Document.Render()) == descendantHash);
                    workspace.Document.Redo(); Check("original-restoration-is-redoable", ImageFiles.Hash(workspace.Document.Render()) == hash);
                    workspace.Document.Reset(); Check("session-reset-remains-distinct-from-earliest-original", ImageFiles.Hash(workspace.Document.Render()) == descendantHash);
                    var snapshotPath = reference!.Path; File.Move(snapshotPath, snapshotPath + ".moved");
                    await workspace.RestoreOriginal(); Check("missing-snapshot-leaves-current-edits-intact", ImageFiles.Hash(workspace.Document.Render()) == descendantHash);
                    DemoAssets.Save(document.Render(), snapshotPath);
                    await workspace.RestoreOriginal(); Check("changed-snapshot-rejected-without-changing-current-document", ImageFiles.Hash(workspace.Document.Render()) == descendantHash);
                    File.Move(snapshotPath, snapshotPath + ".changed"); File.Move(snapshotPath + ".moved", snapshotPath);
                    workspace.Document.MarkSaved(); workspace.Close(); workspace = null;
                    var impossible = Path.Combine(lineageDirectory, "file-as-directory"); File.WriteAllText(impossible, "fixture");
                    var failed = false;
                    try { await lineage.PersistEditedCopy(new(document.Render(), original, externalOriginal, reference), Path.Combine(impossible, "edited.png")); } catch (IOException) { failed = true; }
                    Check("export-failure-preserves-original-snapshot-and-existing-copies", failed && ImageFiles.Hash(snapshotPath) == hash && File.Exists(saved.Path) && File.Exists(descendant.Path));
                }
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "editor-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                if (workspace is not null) { workspace.Document?.MarkSaved(); workspace.Close(); }
                File.WriteAllText(Path.Combine(directory, "editor-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var bytes = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, bounds); dc.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
