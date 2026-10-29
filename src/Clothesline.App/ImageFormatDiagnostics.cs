using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class ImageFormatDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok, object? details = null) { checks.Add(new { name, passed = ok, details }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            Window? window = null; ImageWorkspace? workspace = null;
            try
            {
                var run = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
                string Fixture(string name) { var path = Path.Combine(run, name); File.WriteAllBytes(path, ImageFormatSupport.Fixture(name)); return path; }
                var webp = Fixture("format-probe.webp"); var heic = Fixture("format-probe.heic"); var png = Fixture("format-probe.png");
                var capabilities = await Task.Run(ImageFormatSupport.ProbeAll);
                Check("capability-probe-completes-for-both-formats", capabilities.Length == 2, capabilities);
                Check("missing-codec-reported-as-unavailable", !ImageFormatSupport.Probe("WebP", "format-probe.webp", _ => throw new NotSupportedException("Simulated missing codec.")).CanDecode);
                var blank = BitmapSource.Create(32, 24, 96, 96, PixelFormats.Bgra32, null, new byte[32 * 24 * 4], 32 * 4); blank.Freeze();
                Check("metadata-only-success-does-not-pass-probe", !ImageFormatSupport.Probe("WebP", "format-probe.webp", _ => blank).CanDecode);
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, Path.Combine(run, "library"));
                foreach (var pair in new[] { (Path: webp, Capability: capabilities[0]), (Path: heic, Capability: capabilities[1]) })
                {
                    var bytes = File.ReadAllBytes(pair.Path);
                    if (!pair.Capability.CanDecode)
                    {
                        try { ImageFiles.Read(pair.Path); Check("unavailable-format-gives-actionable-error", false); }
                        catch (IOException ex) { Check("unavailable-format-gives-actionable-error", ex.Message.Contains(T("格式支持"), StringComparison.Ordinal) && bytes.SequenceEqual(File.ReadAllBytes(pair.Path))); }
                        continue;
                    }
                    var original = ImageFiles.Read(pair.Path); var hash = ImageFiles.Hash(original);
                    Check(pair.Capability.Name + "-real-file-decodes", original.PixelWidth == 32 && original.PixelHeight == 24 && original.IsFrozen);
                    Check(pair.Capability.Name + "-manual-import", await runtime.Collector.ImportExternal(pair.Path, preserveCopy: true));
                    var data = ShellTransfer.CopyData(pair.Path);
                    Check(pair.Capability.Name + "-copy-has-PNG-bitmap-and-original-file", data.GetDataPresent("PNG") && data.GetDataPresent(DataFormats.Bitmap) && data.GetFileDropList().Cast<string>().SequenceEqual(new[] { pair.Path }));
                    var export = ImageFiles.Export(pair.Path, Path.Combine(run, "copy" + Path.GetExtension(pair.Path)));
                    var second = ImageFiles.Export(pair.Path, export);
                    Check(pair.Capability.Name + "-original-format-export-preserves-bytes-and-no-overwrite", export != second && bytes.SequenceEqual(File.ReadAllBytes(export)) && bytes.SequenceEqual(File.ReadAllBytes(pair.Path)));
                    var document = new ImageDocument(original); document.Crop(new Rect(0, 0, 16, 24));
                    var edited = await runtime.PersistEditedCopy(new(document.Render(), original, pair.Path, null), Path.Combine(run, Path.GetFileName(pair.Path) + "-edited.png"));
                    Check(pair.Capability.Name + "-edited-PNG-keeps-original-snapshot", edited.Hung && edited.Original is not null && ImageFiles.Hash(edited.Original.Path) == hash && ImageFiles.Read(edited.Path).PixelWidth == 16 && bytes.SequenceEqual(File.ReadAllBytes(pair.Path)));
                    var entry = runtime.Store.Entries().Single(e => e.Path == pair.Path); runtime.Archive(entry.Id); runtime.Restore(entry.Id);
                    Check(pair.Capability.Name + "-history-restores-original-reference", runtime.Store.Find(entry.Id)?.State == EntryState.Shelf && File.Exists(pair.Path));
                }
                if (capabilities[0].CanDecode)
                {
                    Check("lossless-WebP-matches-PNG-pixels", ImageFiles.Hash(webp) == ImageFiles.Hash(png));
                    var alpha = Fixture("alpha.webp"); var transparent = new FormatConvertedBitmap(ImageFiles.Read(alpha), PixelFormats.Bgra32, null, 0); var pixel = new byte[4];
                    transparent.CopyPixels(new Int32Rect(10, 10, 1, 1), pixel, 4, 0);
                    Check("WebP-alpha-preserved", pixel[3] == 128, pixel);
                    using var alphaCopy = ShellTransfer.CopyData(alpha).GetData("PNG") as Stream ?? throw new IOException("PNG copy missing.");
                    Check("WebP-copy-PNG-keeps-alpha-pixels", ImageFiles.Hash(ImageFiles.ReadStream(alphaCopy)) == ImageFiles.Hash(alpha));
                    var thumbnail = ImageFiles.Read(Fixture("large.webp"), true);
                    var thumbnailPixel = new byte[4]; thumbnail.CopyPixels(new Int32Rect(200, 150, 1, 1), thumbnailPixel, 4, 0);
                    Check("optional-thumbnail-bounded-with-alpha", thumbnail.PixelWidth == 420 && thumbnail.PixelHeight == 315 && thumbnailPixel[3] == 128);
                    var excessive = ImageFormatSupport.Fixture("alpha.webp");
                    // VP8L width/height live in the four bytes after the 0x2f signature; no giant pixel allocation in the fixture.
                    BitConverter.GetBytes((uint)(9999 | (4999 << 14) | (1 << 28))).CopyTo(excessive, 21);
                    var oversizedPixels = Path.Combine(run, "too-many-pixels.webp"); File.WriteAllBytes(oversizedPixels, excessive);
                    try { ImageFiles.Read(oversizedPixels); Check("optional-image-pixel-limit-before-render", false); }
                    catch (IOException ex) { Check("optional-image-pixel-limit-before-render", ex.Message == T("图片超过 4000 万像素限制。")); }
                    Check("animated-WebP-is-first-frame-only", ImageFiles.Hash(Fixture("animated.webp")) == ImageFiles.Hash(png));
                    workspace = new ImageWorkspace([new("webp", webp, "sample.webp")], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), runtime.AcquireLease);
                    workspace.Show(); var until = DateTime.UtcNow.AddSeconds(10); while ((workspace.Loading || workspace.Document is null) && DateTime.UtcNow < until) await Task.Delay(40);
                    Check("WebP-opens-image-editor", workspace.Document is not null && ImageFiles.Hash(workspace.Document.Original) == ImageFiles.Hash(png)); workspace.Close(); workspace = null;
                    var watched = Path.Combine(run, "watched"); Directory.CreateDirectory(watched); File.Copy(webp, Path.Combine(watched, "old.webp"));
                    using var watchedShelf = new ShelfController(); using var watcher = new LibraryRuntime(watchedShelf, Path.Combine(run, "watch-library"));
                    watcher.ApplySettings(new LibrarySettings { ClipboardEnabled = false, ScreenshotFolder = watched, Capacity = 25, ShowNew = false, ReduceMotion = true }); watcher.Start();
                    await Task.Delay(300); Check("native-optional-image-baseline-skips-existing", watcher.Store.Entries().Count == 0);
                    var fresh = Path.Combine(watched, "new.webp"); File.Copy(alpha, fresh);
                    until = DateTime.UtcNow.AddSeconds(8); while (watcher.Store.Entries().Count == 0 && DateTime.UtcNow < until) await Task.Delay(80);
                    watcher.RefreshShelf(false); var entry = watcher.Store.Entries().Single();
                    Check("native-WebP-watcher-to-card", entry.Path == fresh && entry.Ownership == Ownership.External && watchedShelf.Count == 1);
                    var item = new DemoItem("fixture", "", fresh, ImageFiles.Read(fresh, true), entry.Id);
                    ShellTransfer.Drag(0, item, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect) =>
                    { Check("optional-image-drag-is-copy-only", allowed == 1); effect = 2; return 0x40100; });
                    Check("optional-image-drag-retains-source-and-card", File.Exists(fresh) && watcher.Store.Find(entry.Id)?.State == EntryState.Shelf && watchedShelf.Count == 1);
                }
                if (capabilities[1].CanDecode) { var alias = Path.Combine(run, "alias.heif"); File.Copy(heic, alias); Check("HEIF-alias-decodes-same-pixels", ImageFiles.Hash(alias) == ImageFiles.Hash(heic)); }
                var corrupt = Path.Combine(run, "broken.webp"); File.WriteAllText(corrupt, "not an image");
                var last = ""; runtime.Status += message => last = message;
                await runtime.ImportFiles([corrupt]); await Task.Delay(100);
                Check("failed-manual-import-keeps-error-in-final-summary", last.Contains("broken.webp", StringComparison.Ordinal) && last.Contains(T("格式支持"), StringComparison.Ordinal) && File.ReadAllText(corrupt) == "not an image" && !runtime.Store.Entries().Any(e => e.Path == corrupt));
                var huge = Path.Combine(run, "oversized.heic"); using (var file = File.Create(huge)) file.SetLength(ImageFiles.MaxFileBytes + 1);
                try { ImageFiles.Read(huge); Check("optional-image-byte-limit-before-decode", false); }
                catch (IOException ex) { Check("optional-image-byte-limit-before-decode", ex.Message == T("图片超过 64 MiB 限制。") && File.Exists(huge)); }
                var panel = new ImageFormatPanel(); window = new Window { Title = "Image format diagnostics", Content = new ScrollViewer { Content = panel }, Width = 700, Height = 500 };
                await panel.Refresh(); window.Show(); window.UpdateLayout();
                Check("format-panel-exposes-actual-capabilities", panel.Capabilities!.Select(c => c.CanDecode).SequenceEqual(capabilities.Select(c => c.CanDecode)));
                Check("format-status-has-accessible-name", Find<TextBlock>(panel).Any(t => AutomationProperties.GetName(t) == T("图片格式检测结果")));
                Render(window, Path.Combine(directory, "image-formats.png")); window.Close(); window = null;
                UiText.Language = "en"; panel = new ImageFormatPanel(); await panel.Refresh(() => [new("WebP", true, ""), new("HEIC / HEIF", false, "Simulated missing codec.")]);
                window = new Window { Title = "Image format diagnostics", Content = new ScrollViewer { Content = panel }, Width = 700, Height = 500 }; window.Show(); window.UpdateLayout();
                Check("English-panel-distinguishes-available-and-unavailable", Find<TextBlock>(panel).Any(t => t.Text.Contains("Sample decoded", StringComparison.Ordinal) && t.Text.Contains("Sample could not be decoded", StringComparison.Ordinal)));
                Render(window, Path.Combine(directory, "image-formats-en.png")); window.Close(); window = null; UiText.Language = "zh-CN";
                using var reopened = new LibraryStore(Path.Combine(run, "library"));
                Check("optional-image-records-and-lineage-survive-reopen", reopened.Entries().Count == runtime.Store.Entries().Count && reopened.Entries().Where(e => e.Title.EndsWith("-edited.png", StringComparison.Ordinal)).All(e => reopened.OriginalFor(e.Id) is not null));
            }
            catch (Exception ex) { Check("unhandled", false, ex.ToString()); }
            finally { workspace?.Close(); window?.Close(); File.WriteAllText(Path.Combine(directory, "image-format-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(passed ? 0 : 1); }
        }));
        return app.Run();
    }
    private static void Render(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window); DemoAssets.Save(image, path);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
