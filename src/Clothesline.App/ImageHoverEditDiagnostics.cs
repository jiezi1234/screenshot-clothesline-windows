using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Storage;
using Clothesline.Core;
using System.Windows.Interop;

namespace Clothesline.App;

internal static class ImageHoverEditDiagnostics
{
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    internal static int Run(Application app, string output, bool desktop = false)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool value) { passed &= value; checks.Add(new { name, passed = value }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            var window = new HoverPreviewWindow();
            try
            {
                var assets = DemoAssets.Create(Path.Combine(output, "fixtures-" + Guid.NewGuid().ToString("N")));
                var bitmap = assets[0].Image;
                void Mark(ImageDocument document, EditorTool tool = EditorTool.Arrow) => document.Add(document.CreateMark(tool, [new Point(20, 20), new Point(220, 130)], Colors.Crimson, 6, "Note"));
                async Task Until(Func<bool> done) { for (var i = 0; i < 150 && !done(); i++) await Task.Delay(40); if (!done()) throw new TimeoutException("Image editor did not settle"); }
                var held = 0; var writes = 0; BitmapSource? saved = null;
                ImageEditSession Session(Func<BitmapSource, Task> save)
                { held++; return new(new(bitmap), save, new Lease(() => held--)); }
                window.SetSession(Session(image => { saved = image; writes++; return Task.CompletedTask; }));
                var doc = window.Document!; var initial = doc.State;
                Check("hover-editor-ready-without-showing-a-window", window.IsEditing && !window.IsVisible && held == 1 && !window.Dirty);
                Mark(doc); var arrow = doc.State; await Until(() => writes == 1 && !window.Saving);
                Check("change-saves-automatically-at-original-resolution", !window.Dirty && saved?.PixelWidth == bitmap.PixelWidth && saved.PixelHeight == bitmap.PixelHeight);
                window.Undo(); await Until(() => writes == 2 && !window.Saving);
                Check("previous-step-restores-and-autosaves-original-state", ReferenceEquals(doc.State, initial) && !window.Dirty && doc.CanRedo);
                window.Redo(); await Until(() => writes == 3 && !window.Saving);
                Check("next-step-restores-and-autosaves-edit-after-save", ReferenceEquals(doc.State, arrow) && !window.Dirty && doc.CanUndo);
                window.Undo(); Mark(doc, EditorTool.Rectangle);
                Check("new-edit-after-undo-discards-redo-branch", !doc.CanRedo);
                doc.Crop(new Rect(10, 10, 300, 180)); await window.Save();
                Check("crop-saves-image-dimensions-not-preview-size", saved?.PixelWidth == 300 && saved.PixelHeight == 180);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(620, 520)); content.Arrange(new Rect(0, 0, 620, 520)); content.UpdateLayout();
                var capture = new RenderTargetBitmap(620, 520, 96, 96, PixelFormats.Pbgra32); capture.Render(content); DemoAssets.Save(capture, Path.Combine(output, "image-hover-editor.png"));
                Check("leaving-flushes-and-releases-session", await window.SaveAndDismiss() && !window.IsEditing && held == 0);

                var blocked = new TaskCompletionSource(); writes = 0;
                window.SetSession(Session(async image => { saved = image; writes++; if (writes == 1) await blocked.Task; }));
                doc = window.Document!; Mark(doc); var first = doc.State; var pending = window.Save();
                await Until(() => writes == 1); Mark(doc, EditorTool.Rectangle); var latest = doc.State;
                Check("editing-and-undo-remain-available-during-save", window.Saving && doc.CanUndo && !ReferenceEquals(latest, first));
                blocked.SetResult(); await pending;
                Check("completed-save-does-not-mark-newer-changes-saved", window.Dirty && ReferenceEquals(doc.State, latest));
                Check("mouse-leave-flushes-newer-revision-before-closing", await window.SaveAndDismiss() && writes == 2 && held == 0);

                var fail = true;
                window.SetSession(Session(_ => fail ? Task.FromException(new IOException("Fixture disk full")) : Task.CompletedTask));
                doc = window.Document!; Mark(doc); var draft = doc.State;
                Check("save-failure-preserves-editor-draft-and-lease", !await window.SaveAndDismiss() && window.SaveFailed && window.IsEditing && window.Dirty && held == 1 && ReferenceEquals(doc.State, draft));
                Check("declining-discard-keeps-draft", !window.TryEndEditing(() => false) && window.Dirty);
                fail = false;
                Check("retry-saves-retained-draft", await window.SaveAndDismiss() && held == 0);

                using (var hover = new ShelfHoverPreview())
                {
                    var popup = hover.Window;
                    blocked = new TaskCompletionSource(); writes = 0;
                    popup.SetSession(Session(async _ => { writes++; await blocked.Task; }));
                    hover.UpdatePointer(1000, false, true, true, true, false);
                    Check("card-to-popup-travel-grace", hover.UpdatePointer(1200, false, false, false, true, false));
                    Mark(popup.Document!); hover.UpdatePointer(2000, false, false, false, false, false);
                    await Until(() => writes == 1);
                    hover.UpdatePointer(2100, true, false, false, true, false); blocked.SetResult(); await Until(() => !popup.Saving);
                    Check("returning-during-save-keeps-editor-open", popup.IsEditing && !popup.Dirty && held == 1);
                    hover.UpdatePointer(3000, false, false, false, false, false);
                    Check("leaving-again-closes-clean-editor", !popup.IsEditing && held == 0);
                }

                using (var shelf = new ShelfController())
                using (var runtime = new LibraryRuntime(shelf, Path.Combine(output, "library-" + Guid.NewGuid().ToString("N"))))
                {
                    var source = assets[0].Path; var sourceDigest = LibraryStore.FileDigest(source);
                    await runtime.Collector.ImportExternal(source);
                    var entry = runtime.Store.Entries().Single();
                    var session = await runtime.CreateImageEditSession(assets[0] with { EntryId = entry.Id }, default);
                    window.SetSession(session!); doc = window.Document!; Mark(doc); await window.Save();
                    var edited = runtime.Store.Find(entry.Id)!;
                    Check("runtime-updates-same-card-and-preserves-file", edited.Path != source && edited.Source == LibraryStore.ImageEditSource && LibraryStore.FileDigest(source) == sourceDigest && runtime.Store.Entries(EntryState.Shelf).Count == 1);
                    Check("runtime-saves-full-resolution-png-with-original-link", ImageFiles.Read(edited.Path).PixelWidth == bitmap.PixelWidth && runtime.Store.OriginalFor(entry.Id) is not null);
                    Check("saved-metadata-hash-matches-decoded-png", ImageFiles.Hash(edited.Path) == edited.Hash);
                    var editedPath = edited.Path; window.Undo(); var undoSaved = await window.Save();
                    var outputHash = ImageFiles.Hash(editedPath); var originalHash = ImageFiles.Hash(bitmap);
                    Check("undo-auto-save-keeps-same-copy-path", undoSaved && runtime.Store.Find(entry.Id)!.Path == editedPath && outputHash == originalHash);
                    if (!undoSaved || outputHash != originalHash) checks.Add(new { name = "undo-details", undoSaved, status = window.EditStatus, outputHash, originalHash, loadedHash = ImageFiles.Hash(doc.Original) });
                    await window.SaveAndDismiss();
                    var rootId = runtime.Store.OriginalFor(entry.Id)!.Id;
                    var reopened = await runtime.CreateImageEditSession(assets[0] with { EntryId = entry.Id, Path = editedPath }, default);
                    window.SetSession(reopened!); Mark(window.Document!); await window.SaveAndDismiss();
                    Check("reopened-editor-retains-original-lineage-and-same-card", runtime.Store.OriginalFor(entry.Id)!.Id == rootId && runtime.Store.Find(entry.Id)!.Path == editedPath && runtime.Store.Entries(EntryState.Shelf).Count == 1);
                }
                if (desktop)
                {
                    using var shelf = new ShelfController { ReduceMotion = true, SuppressFullscreen = false };
                    var popup = app.Windows.OfType<HoverPreviewWindow>().Single(w => w != window);
                    shelf.StartImageEdit = (_, _) => Task.FromResult<ImageEditSession?>(Session(image => { saved = image; writes++; return Task.CompletedTask; }));
                    shelf.SetItems([assets[0] with { EntryId = "desktop-fixture" }], true);
                    var card = app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == "desktop-fixture");
                    PixelPoint Center(Window value) { Native.GetWindowRect(new WindowInteropHelper(value).Handle, out var rect); return new((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2); }
                    bool Hit(Window value) { var p = Center(value); return Native.WindowFromPoint(new Native.Point { X = p.X, Y = p.Y }) == new WindowInteropHelper(value).Handle; }
                    await Until(() => Hit(card)); var foreground = Native.GetForegroundWindow();
                    shelf.TrackHoverPreview(0, Center(card), false, false); shelf.TrackHoverPreview(450, Center(card), false, false);
                    await Until(() => popup.IsEditing && popup.IsVisible && Hit(popup));
                    Check("desktop-hover-opens-inline-editor-without-workspace-or-focus-steal", !app.Windows.OfType<ImageWorkspace>().Any() && Native.GetForegroundWindow() == foreground && shelf.Busy);
                    shelf.TrackHoverPreview(500, Center(popup), true, false);
                    Check("desktop-editing-mouse-press-keeps-popup-and-shelf-open", popup.IsEditing && popup.IsVisible && shelf.Visible);
                    Mark(popup.Document!); await popup.Save(); popup.Undo(); await popup.Save();
                    Check("desktop-undo-saves-and-retains-redo", !popup.Dirty && popup.Document!.CanRedo && held == 1);
                    var area = Native.MonitorAt(Center(card)).WorkArea;
                    shelf.TrackHoverPreview(1500, new PixelPoint(area.X + 2, area.Bottom - 2), false, false);
                    await Until(() => !popup.IsEditing && !popup.IsVisible);
                    Check("desktop-leaving-dismisses-popup-and-releases-busy", !shelf.Busy && held == 0);
                    shelf.TrackHoverPreview(2000, Center(card), false, false); shelf.TrackHoverPreview(2450, Center(card), false, false);
                    await Until(() => popup.IsEditing); Mark(popup.Document!);
                    shelf.Hide();
                    Check("desktop-hide-removes-popup-without-dropping-unsaved-draft", !popup.IsVisible && popup.Dirty && held == 1);
                    await Until(() => !popup.Saving && !popup.Dirty);
                    Check("desktop-hidden-save-releases-clean-session", !popup.IsEditing && held == 0);
                }
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "completed", passed = false, error = ex.ToString() }); }
            finally
            {
                window.TryEndEditing(() => true); window.ClosePermanently(); ImageDecoder.StopIdleWorkers();
                File.WriteAllText(Path.Combine(output, "image-hover-edit-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
}
