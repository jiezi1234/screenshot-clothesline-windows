using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                Check("hover-remains-readonly-without-keyboard-activation", NonActivating() && !window.AcceptsKeyboard && window.TextView.IsReadOnly && !window.TextView.Focusable);
                Check("edit-starts-in-the-same-paper-preview", window.BeginEdit() && !NonActivating() && window.IsEditing && window.AcceptsKeyboard && !window.TextView.IsReadOnly && window.TextView.Focusable && leases == 1);
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
                Check("successful-save-returns-to-nonactivating-preview", NonActivating() && !window.IsEditing && !window.AcceptsKeyboard && window.TextView.IsReadOnly && !window.TextView.Focusable && window.PreviewText == saved && leases == 0);
                window.SetPreview(Item(saved)); window.BeginEdit(); window.TextView.Text = " \r\n";
                Check("invalid-save-keeps-draft-and-original-file", !await window.Save() && window.Dirty && window.EditStatus.Length > 0 && TextContent.Read(entry.Path) == saved && leases == 1);
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
                var pending = window.Save();
                Check("pending-save-disables-editing-and-discard", window.Saving && window.TextView.IsReadOnly && !window.TryEndEditing(() => true) && !await window.Save());
                window.Dismiss();
                Check("hiding-during-save-does-not-drop-session", window.IsEditing && leases == 1 && window.PreviewText == "pending write");
                completion.SetResult(TextContent.Hash("pending write"));
                Check("save-completion-releases-hidden-editor", await pending && !window.IsEditing && leases == 0 && !window.AcceptsKeyboard);
                window.Dismiss(); Check("finished-preview-clears-on-dismissal", window.PreviewText.Length == 0);
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
                        Check("inline-editor-keeps-text-space-in-" + language.Code, localized.TextView.ActualHeight >= 70
                            && UiAccessibility.Descendants((FrameworkElement)localized.Content).OfType<Button>().Where(b => b.Visibility == Visibility.Visible)
                                .All(b => b.ActualWidth <= 280));
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
