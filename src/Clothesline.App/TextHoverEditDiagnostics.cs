using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

// Own generated content and an isolated library; no displayed windows or clipboard writes.
internal static class TextHoverEditDiagnostics
{
    private sealed class Lease(Action release) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; release(); }
    }
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool value) { passed &= value; checks.Add(new { name, passed = value }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            var window = new TextHoverWindow(); var leases = 0;
            try
            {
                using var store = new LibraryStore(Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N")));
                using var engine = new CollectionEngine(store, _ => "fixture", () => 25);
                const string original = "原来的文字 🧺\r\nSecond line.\n  保留空格。";
                await engine.ImportText(original, 1024 * 1024);
                var entry = store.Entries(EntryState.Shelf).Single();
                DemoItem Item(string text) => new(TextContent.Title(text), "Generated note", entry.Path,
                    TextCardImage.Create(text, TextPaperStyles.ForEntry(entry.Id)), entry.Id, text);
                TextEditSession Session(DemoItem item, Func<string, string, Task<string>> save)
                { leases++; return new TextEditSession(entry.Id, item.Text!, save, new Lease(() => leases--)); }
                window.StartEdit = item => Session(item, (text, hash) => engine.SaveText(entry.Id, text, hash, 1024 * 1024));
                window.SetPreview(Item(original)); Render(window, directory, "preview", 560, 440);
                var handle = new WindowInteropHelper(window).EnsureHandle();
                bool NonActivating() => (Native.GetWindowLongPtrW(handle, -20).ToInt64() & 0x08000000) != 0;
                Check("hover-is-immediately-editable-without-an-edit-button", !NonActivating() && window.IsEditing && window.AcceptsKeyboard && !window.TextView.IsReadOnly && window.TextView.Focusable && leases == 1
                    && !UiAccessibility.Descendants((FrameworkElement)window.Content).OfType<Button>().Any(b => (string)b.Content == UiText.T("编辑文字")));
                Check("hover-prepares-input-without-activating-or-focusing", !window.ShowActivated && !window.IsActive && !window.TextView.IsKeyboardFocused);
                IDataObject? copied = null; window.CopyToClipboard = data => copied = data;
                var selected = "文字 🧺"; window.TextView.Select(original.IndexOf(selected, StringComparison.Ordinal), selected.Length);
                ApplicationCommands.Copy.Execute(null, window.TextView);
                Check("copy-command-copies-only-selection-and-marks-it-against-recollection", copied?.GetData(DataFormats.UnicodeText) as string == selected
                    && ClipboardPolicy.Select(copied.GetFormats(false), _ => null) == ClipboardContentKind.None && !window.Dirty);
                var copyMenu = (MenuItem)window.TextView.ContextMenu!.Items[0];
                Check("right-click-copy-uses-the-same-selection-command", copyMenu.Command == ApplicationCommands.Copy && copyMenu.CommandTarget == window.TextView);
                window.TextView.Select(0, 0); Check("copy-with-no-selection-is-disabled", !ApplicationCommands.Copy.CanExecute(null, window.TextView));
                window.TextView.Select(0, 3); window.CopyToClipboard = _ => throw new IOException("Fixture clipboard busy");
                ApplicationCommands.Cut.Execute(null, window.TextView);
                Check("failed-selection-cut-keeps-original-text", window.TextView.Text == original);
                window.CopyToClipboard = data => copied = data;
                var saved = "在预览里编辑 🧺\r\nSaved in the same note.";
                window.TextView.Text = saved;
                Check("typing-marks-inline-draft-dirty", window.Dirty);
                window.Dismiss();
                Check("hiding-preview-retains-draft-and-file-lease", window.IsEditing && window.Dirty && window.PreviewText == saved && leases == 1);
                window.SetPreview(Item("another preview")); window.SetText("refresh");
                Check("hover-refresh-cannot-overwrite-active-draft", window.PreviewText == saved && window.EditingId == entry.Id);
                Check("declining-discard-keeps-inline-editor", !window.TryEndEditing(() => false) && window.Dirty && leases == 1);
                Render(window, directory, "editing", 560, 440);
                Check("save-updates-original-file-and-card-identity", await window.Save() && TextContent.Read(entry.Path) == saved
                    && store.Find(entry.Id)!.AddedAt == entry.AddedAt && store.Find(entry.Id)!.Title == "在预览里编辑 🧺");
                Check("manual-save-keeps-popup-ready-for-further-editing", window.IsEditing && !window.TextView.IsReadOnly && !window.Dirty && leases == 1);
                Check("leaving-clean-preview-releases-file-and-input", await window.SaveAndDismiss() && NonActivating() && !window.IsEditing && !window.AcceptsKeyboard && window.PreviewText.Length == 0 && leases == 0);
                window.SetPreview(Item(saved)); window.TextView.Text = saved + "\nAutomatic save.";
                Check("leaving-dirty-preview-saves-original-and-closes", await window.SaveAndDismiss() && !window.IsEditing && leases == 0 && TextContent.Read(entry.Path) == saved + "\nAutomatic save.");
                saved += "\nAutomatic save.";
                window.SetPreview(Item(saved)); window.BeginEdit(); window.TextView.Text = " \r\n";
                Check("failed-auto-save-keeps-draft-session-and-original-file", !await window.SaveAndDismiss() && window.SaveFailed && window.Dirty && window.EditStatus.Length > 0 && TextContent.Read(entry.Path) == saved && leases == 1);
                Check("explicit-cancel-restores-original-preview", window.TryEndEditing(() => true) && window.PreviewText == saved && !window.AcceptsKeyboard && leases == 0);
                window.SetPreview(Item(saved)); window.BeginEdit(); window.TextView.Text = "stale draft";
                File.WriteAllText(entry.Path, "external change", TextContent.Encoding);
                Check("external-edit-conflict-does-not-overwrite-or-lose-draft", !await window.Save() && TextContent.Read(entry.Path) == "external change" && window.PreviewText == "stale draft" && window.Dirty);
                window.TryEndEditing(() => true);
                window.SetPreview(Item("external change")); window.BeginEdit(); window.TextView.Text = "locked draft";
                using (var locked = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Check("locked-file-keeps-inline-draft", !await window.Save() && window.PreviewText == "locked draft" && window.Dirty);
                window.TryEndEditing(() => true);
                var completion = new TaskCompletionSource<string>();
                window.StartEdit = item => Session(item, (_, _) => completion.Task);
                window.SetPreview(Item("external change")); window.BeginEdit(); window.TextView.Text = "pending write";
                var pending = window.SaveAndDismiss();
                Check("pending-save-allows-typing-but-blocks-discard-and-duplicate-save", window.Saving && !window.TextView.IsReadOnly && !window.TryEndEditing(() => true) && !await window.Save());
                window.Dismiss();
                Check("hiding-during-save-does-not-drop-session", window.IsEditing && leases == 1 && window.PreviewText == "pending write");
                completion.SetResult(TextContent.Hash("pending write"));
                Check("save-completion-releases-hidden-editor", await pending && !window.IsEditing && leases == 0 && !window.AcceptsKeyboard);
                window.Dismiss(); Check("finished-preview-clears-on-dismissal", window.PreviewText.Length == 0);
                completion = new TaskCompletionSource<string>();
                window.StartEdit = item => Session(item, (_, _) => completion.Task);
                window.SetPreview(Item(original)); window.TextView.Text = "Manual save then close";
                var manualSave = window.Save(); var closeDuringSave = window.SaveAndDismiss();
                completion.SetResult(TextContent.Hash(window.PreviewText));
                Check("close-during-manual-save-waits-for-the-same-write", await manualSave && await closeDuringSave && !window.IsEditing && leases == 0);
                async Task Until(Func<bool> ready)
                {
                    for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25);
                    if (!ready()) throw new TimeoutException("Auto-save fixture did not become ready");
                }
                var autoWrites = 0;
                window.StartEdit = item => Session(item, async (text, hash) => { autoWrites++; return await engine.SaveText(entry.Id, text, hash, 1024 * 1024); });
                window.SetPreview(Item("external change"));
                await Task.Delay(TextHoverWindow.AutoSaveDelayMilliseconds + 100);
                Check("reading-without-changes-does-not-write", autoWrites == 0 && !window.Dirty);
                window.TextView.Text = "First typing burst"; await Task.Delay(150);
                window.TextView.Text = "Automatically saved while hovering 🧺"; await Task.Delay(300);
                Check("continued-typing-restarts-auto-save-delay", autoWrites == 0 && window.Dirty);
                await Until(() => !window.Dirty && !window.Saving);
                Check("idle-edit-saves-original-file-without-leaving-or-closing", autoWrites == 1 && window.IsEditing && !window.TextView.IsReadOnly
                    && TextContent.Read(entry.Path) == window.PreviewText && store.Find(entry.Id)!.AddedAt == entry.AddedAt);
                window.SetCompositionActive(true); window.TextView.Text = "Input method composition";
                await Task.Delay(TextHoverWindow.AutoSaveDelayMilliseconds + 100);
                Check("input-method-composition-defers-auto-save-and-dismiss", autoWrites == 1 && window.Dirty && window.PointerInteraction && !await window.SaveAndDismiss());
                window.SetCompositionActive(false);
                await Until(() => !window.Dirty && !window.Saving);
                Check("committed-composition-resumes-auto-save", autoWrites == 2 && TextContent.Read(entry.Path) == "Input method composition");
                window.TextView.Text = " \r\n";
                await Until(() => window.SaveFailed);
                var writesAfterFailure = autoWrites;
                await Task.Delay(TextHoverWindow.AutoSaveDelayMilliseconds + 100);
                Check("failed-idle-save-retains-draft-without-retry-loop", autoWrites == writesAfterFailure && window.Dirty && window.PreviewText == " \r\n" && TextContent.Read(entry.Path) == "Input method composition");
                window.TextView.Text = "Recovered draft";
                await Until(() => !window.Dirty && !window.Saving);
                Check("new-edit-retries-auto-save-after-failure", !window.SaveFailed && TextContent.Read(entry.Path) == "Recovered draft");
                window.TextView.Text = "Undo this new edit"; window.TextView.Undo();
                var writesBeforeUndoWait = autoWrites;
                await Task.Delay(TextHoverWindow.AutoSaveDelayMilliseconds + 100);
                Check("undo-to-saved-text-does-not-write-again", !window.Dirty && autoWrites == writesBeforeUndoWait && window.TextView.Text == "Recovered draft");
                window.TextView.Text = "Discard before debounce"; window.TryEndEditing(() => true);
                await Task.Delay(TextHoverWindow.AutoSaveDelayMilliseconds + 100);
                Check("discard-stops-pending-auto-save", autoWrites == writesBeforeUndoWait && TextContent.Read(entry.Path) == "Recovered draft" && leases == 0);
                window.SetPreview(Item("Recovered draft")); window.TextView.Text = "Draft while deciding whether to quit";
                window.TryEndEditing(() =>
                {
                    window.SetCompositionActive(false); // Losing focus to a modal must not rearm auto-save.
                    var frame = new DispatcherFrame(); var decision = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TextHoverWindow.AutoSaveDelayMilliseconds + 100) };
                    decision.Tick += (_, _) => { decision.Stop(); frame.Continue = false; }; decision.Start(); Dispatcher.PushFrame(frame);
                    Check("discard-confirmation-pauses-save-even-when-focus-changes", autoWrites == writesBeforeUndoWait && window.Dirty && !window.Save().GetAwaiter().GetResult());
                    return false;
                });
                await Until(() => !window.Dirty && !window.Saving);
                Check("declining-discard-resumes-auto-save", autoWrites == writesBeforeUndoWait + 1 && TextContent.Read(entry.Path) == "Draft while deciding whether to quit");
                window.TryEndEditing(() => true);
                var snapshots = new List<string>(); var hashes = new List<string>();
                completion = new TaskCompletionSource<string>();
                window.StartEdit = item => Session(item, (text, hash) =>
                {
                    snapshots.Add(text); hashes.Add(hash);
                    return snapshots.Count == 1 ? completion.Task : Task.FromResult(TextContent.Hash(text));
                });
                window.SetPreview(Item("Recovered draft")); window.TextView.Text = "Snapshot one";
                await Until(() => window.Saving);
                window.TextView.Text = "Snapshot two, typed during the write";
                Check("auto-save-in-flight-does-not-disable-typing", !window.TextView.IsReadOnly && window.Dirty);
                completion.SetResult(TextContent.Hash("Snapshot one"));
                await Until(() => !window.Dirty && !window.Saving);
                Check("newer-draft-is-saved-after-in-flight-snapshot", snapshots.SequenceEqual(new[] { "Snapshot one", "Snapshot two, typed during the write" })
                    && hashes[1] == TextContent.Hash("Snapshot one") && window.PreviewText == snapshots[1]);
                window.TryEndEditing(() => true);
                snapshots.Clear(); hashes.Clear(); completion = new TaskCompletionSource<string>();
                window.SetPreview(Item("Recovered draft")); window.TextView.Text = "Closing snapshot";
                var flush = window.SaveAndDismiss(); window.TextView.Text = "Newest text before close";
                completion.SetResult(TextContent.Hash("Closing snapshot"));
                Check("close-flushes-newest-edit-made-during-save", await flush && snapshots.SequenceEqual(new[] { "Closing snapshot", "Newest text before close" }) && !window.IsEditing && leases == 0);
                using (var hover = new ShelfTextHover())
                {
                    var popup = app.Windows.OfType<TextHoverWindow>().Single(w => w != window);
                    var writes = 0; var write = new TaskCompletionSource<string>();
                    hover.StartEdit = item => Session(item, (_, _) => { writes++; return write.Task; });
                    popup.SetPreview(Item(original));
                    hover.UpdatePointer(1000, false, true, true, true, false);
                    Check("moving-from-card-to-popup-keeps-travel-grace", hover.UpdatePointer(1200, false, false, false, true, false) && popup.IsEditing);
                    popup.TextView.Text = "Leave and return";
                    hover.UpdatePointer(2000, false, false, false, false, false);
                    hover.UpdatePointer(2100, false, false, false, false, false);
                    Check("mouse-leave-starts-only-one-save", writes == 1 && popup.Saving && leases == 1);
                    hover.UpdatePointer(2200, true, false, false, true, false);
                    write.SetResult(TextContent.Hash(popup.PreviewText)); await Task.Delay(10);
                    Check("returning-during-save-keeps-popup-editable", popup.IsEditing && !popup.Saving && !popup.Dirty && !popup.TextView.IsReadOnly && leases == 1);
                    hover.UpdatePointer(3000, false, false, false, false, false);
                    Check("leaving-after-return-closes-clean-popup", !popup.IsEditing && popup.PreviewText.Length == 0 && leases == 0 && writes == 1);
                    write = new TaskCompletionSource<string>(); popup.SetPreview(Item(original)); popup.TextView.Text = "Leave twice";
                    hover.UpdatePointer(4000, false, false, false, false, false);
                    hover.UpdatePointer(4100, true, false, false, true, false);
                    hover.UpdatePointer(4200, false, false, false, false, false);
                    write.SetResult(TextContent.Hash(popup.PreviewText)); await Task.Delay(10);
                    Check("leaving-again-during-save-still-closes-after-write", !popup.IsEditing && leases == 0);
                    var failures = 0;
                    hover.StartEdit = item => Session(item, (_, _) => { failures++; throw new IOException("Fixture save failed"); });
                    popup.SetPreview(Item(original)); popup.TextView.Text = "Keep failed draft";
                    hover.UpdatePointer(5000, false, false, false, false, false);
                    hover.UpdatePointer(6000, false, false, false, false, false);
                    Check("failed-leave-save-retains-draft-without-retry-loop", failures == 1 && popup.SaveFailed && popup.Dirty && leases == 1);
                    popup.TextView.Text += " corrected";
                    hover.UpdatePointer(7000, false, false, false, false, false);
                    Check("editing-failed-draft-allows-new-save-attempt", failures == 2 && popup.PreviewText.EndsWith("corrected", StringComparison.Ordinal));
                    popup.TryEndEditing(() => true);
                    popup.SetPreview(Item(original));
                    Check("clean-hover-does-not-block-reorder", hover.ReleaseCleanPreview() && !hover.IsEditing && leases == 0);
                    popup.SetPreview(Item(original)); hover.Hide();
                    Check("hiding-clean-hover-does-not-leave-a-session-behind", !hover.IsEditing && leases == 0);
                    hover.StartEdit = item => Session(item, (_, _) => Task.FromResult("saved"));
                    popup.SetPreview(Item(original)); popup.TextView.Text = "Hidden auto-save"; hover.Hide();
                    await Until(() => !hover.IsEditing);
                    Check("hidden-draft-auto-saves-and-releases-file-protection", leases == 0 && popup.PreviewText.Length == 0);
                }
                foreach (var style in Enum.GetValues<TextPaperStyle>())
                {
                    var id = Enumerable.Range(0, 100).Select(i => i.ToString("x32")).First(i => TextPaperStyles.ForEntry(i) == style);
                    window.StartEdit = item => Session(item, (_, _) => Task.FromResult("unused"));
                    window.SetPreview(Item(original) with { EntryId = id }); window.BeginEdit();
                    window.TextView.Text = string.Join("\n", Enumerable.Range(0, 900).Select(i => i + " 完整文字 · preserved 🧺"));
                    Render(window, directory, "editor-" + style, 360, 300);
                    window.TextView.ScrollToEnd(); await Task.Delay(20);
                    Render(window, directory, "editor-end-" + style, 360, 300);
                    Check("long-text-remains-editable-and-scrollable-on-" + style, window.PaperStyle == style && !window.TextView.IsReadOnly
                        && window.TextView.GetLastVisibleLineIndex() >= window.TextView.LineCount - 2 && window.TextView.ActualHeight >= 70);
                    window.TryEndEditing(() => true);
                }
                using (var shelf = new ShelfController(() => false))
                {
                    shelf.StartTextEdit = item => Session(item, (_, _) => Task.FromResult("unused"));
                    var popup = app.Windows.OfType<TextHoverWindow>().Single(w => w != window);
                    popup.SetPreview(Item(original)); popup.BeginEdit(); popup.TextView.Text = "controller draft";
                    Check("editing-reserves-shelf-against-refresh-and-reorder", shelf.Busy && popup.IsEditing);
                    shelf.HideFromShortcut();
                    Check("hide-shortcut-retains-inline-draft", popup.Dirty && popup.PreviewText == "controller draft" && leases == 1);
                    shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                    Check("session-lock-hides-but-preserves-inline-draft", shelf.SessionBlocked && !popup.IsVisible && popup.PreviewText == "controller draft");
                    popup.TryEndEditing(() => true);
                    Check("ending-inline-edit-releases-shelf-for-refresh", !shelf.Busy && leases == 0 && shelf.TryCloseTextEditor());
                }
                window.Close(); window.SetPreview(Item(original));
                Check("user-close-leaves-preview-reusable", window.BeginEdit() && window.PreviewText == original);
                window.TryEndEditing(() => true);
                foreach (var language in UiText.Languages)
                {
                    UiText.Language = language.Code;
                    var localized = new TextHoverWindow();
                    try
                    {
                        localized.StartEdit = item => Session(item, (_, _) => Task.FromResult("unused"));
                        localized.SetPreview(Item(original)); localized.BeginEdit();
                        Render(localized, directory, "editing-" + language.Code, 360, 300);
                        Check("inline-editor-keeps-text-space-in-" + language.Code, localized.TextView.ActualHeight >= 70);
                    }
                    finally { localized.TryEndEditing(() => true); localized.ClosePermanently(); }
                }
                Check("all-inline-edit-leases-released", leases == 0);
                Check("inline-diagnostics-never-show-or-activate-windows", app.Windows.Cast<Window>().All(w => !w.IsVisible && !w.IsActive));
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "inline-editor-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                window.TryEndEditing(() => true); window.ClosePermanently(); UiText.Language = "zh-CN";
                File.WriteAllText(Path.Combine(directory, "text-hover-edit-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static void Render(TextHoverWindow window, string directory, string name, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(content);
        DemoAssets.Save(bitmap, Path.Combine(directory, name + ".png"));
    }
}
