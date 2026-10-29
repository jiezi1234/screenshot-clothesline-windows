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
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class TextCardDiagnostics
{
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { passed &= ok; checks.Add(new { name, passed = ok }); }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                const string text = "  一段文字 🧺\r\nHello, world!\n\t<blockquote>保持原样</blockquote>  ";
                var data = new DataObject(); data.SetData(DataFormats.UnicodeText, text); data.SetData(DataFormats.Html, "<b>ignored formatting</b>");
                Check("clipboard-reads-literal-Unicode-not-html", ClipboardCollector.ReadText(data) == text && ClipboardPolicy.Select(data.GetFormats(false), _ => null) == ClipboardContentKind.Text);
                var empty = new DataObject(); empty.SetText(" \t\r\n"); Check("whitespace-only-clipboard-is-ignored", ClipboardCollector.ReadText(empty) is null);
                var own = ShellTransfer.TextData(text);
                Check("text-copy-is-exact-and-marked-against-recollection", own.GetData(DataFormats.UnicodeText) as string == text && ClipboardPolicy.Select(own.GetFormats(false), _ => null) == ClipboardContentKind.None);
                Check("single-click-copy-delivers-text-not-file-or-bitmap", !own.GetDataPresent(DataFormats.FileDrop) && !own.GetDataPresent(DataFormats.Bitmap));
                var longText = string.Join("\n", Enumerable.Range(1, 1200).Select(i => $"{i:D4} 文字预览 · Plain text, preserved literally. 🧺"));
                using var shelf = new ShelfController(() => false) { SuppressFullscreen = false, ReduceMotion = true };
                using var library = new LibraryRuntime(shelf, Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N")));
                library.ApplySettings(library.Settings with { Capacity = 25, ReduceMotion = true, ClipboardEnabled = false }, false);
                shelf.SuppressFullscreen = false;
                await library.Collector.ImportText(text, library.Settings.MaxManagedBytes);
                var first = library.Store.Entries(EntryState.Shelf).Single();
                await library.Collector.ImportText(longText, library.Settings.MaxManagedBytes);
                var second = library.Store.Entries(EntryState.Shelf).Single(e => e.Id != first.Id);
                library.RefreshShelf(true);
                async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Fixture did not become ready"); }
                CardWindow? Find(string id) => app.Windows.OfType<CardWindow>().FirstOrDefault(c => !c.Retiring && c.Item.EntryId == id && c.Item.Text is not null);
                await Until(() => Find(first.Id) is not null && Find(second.Id) is not null);
                shelf.Show();
                await Until(() => Find(first.Id)!.IsVisible && ((FrameworkElement)Find(first.Id)!.Content).ActualWidth > 0 && ((FrameworkElement)Find(second.Id)!.Content).ActualWidth > 0);
                var card = Find(second.Id)!; var shortCard = Find(first.Id)!;
                PixelRect Bounds(Window w) { Native.GetWindowRect(new WindowInteropHelper(w).Handle, out var r); return r.Pixels; }
                PixelPoint Center(Window w) { var r = Bounds(w); return new(r.X + r.Width / 2, r.Y + r.Height / 2); }
                var point = Center(card); var home = Bounds(card); var monitor = Native.MonitorAt(point);
                var dpi = Native.GetDpiForWindow(new WindowInteropHelper(card).Handle) / 96.0;
                Check("text-cards-use-the-same-image-card-dimensions", home.Width == (int)Math.Round(DesktopGeometry.CardWidth * dpi) && home.Height == (int)Math.Round(DesktopGeometry.CardHeight * dpi));
                Check("card-keeps-full-text-with-a-bounded-thumbnail", card.Item.Text == longText && card.Item.Image.PixelWidth == 400 && card.Item.Image.PixelHeight == 270);
                Capture(shortCard, Path.Combine(directory, "text-card.png"));
                var copy = await ShellTransfer.CopyDataAsync(first.Path);
                Check("copy-from-persisted-text-keeps-whitespace-and-line-endings", copy.GetData(DataFormats.UnicodeText) as string == text);
                var nativeCopy = false;
                ShellTransfer.Drag(0, shortCard.Item, (nint owner, System.Runtime.InteropServices.ComTypes.IDataObject shellData, nint dragSource, uint allowed, out uint effect) =>
                {
                    var transfer = new DataObject(shellData); var files = transfer.GetData(DataFormats.FileDrop) as string[];
                    nativeCopy = allowed == TransferPolicy.AllowedEffects && files is { Length: 1 } && TextContent.Read(files[0]) == text;
                    effect = 1; return 0x40100;
                });
                Check("native-text-drag-offers-readable-txt-copy-and-retains-source", nativeCopy && File.Exists(first.Path) && library.Store.Find(first.Id)!.State == EntryState.Shelf);
                var popup = app.Windows.OfType<TextHoverWindow>().Single(); var imagePopup = app.Windows.OfType<HoverPreviewWindow>().Single(); var foreground = Native.GetForegroundWindow();
                shelf.TrackHoverPreview(1000, point, false, false); shelf.TrackHoverPreview(1399, point, false, false);
                Check("brief-text-hover-does-not-open-preview", !popup.IsVisible);
                shelf.TrackHoverPreview(1400, point, false, false); popup.UpdateLayout();
                Check("text-hover-opens-complete-document-without-image-decoding", popup.IsVisible && popup.PreviewText == longText && !imagePopup.IsVisible);
                Check("text-preview-does-not-activate-window", Native.GetForegroundWindow() == foreground);
                var previewStyle = Native.GetWindowLongPtrW(new WindowInteropHelper(popup).Handle, -20).ToInt64();
                Check("text-preview-is-interactive-nonactivating-toolwindow", (previewStyle & 0x08080080) == 0x08080080 && (previewStyle & 0x20) == 0);
                Check("text-preview-avoids-cards-and-stays-in-work-area", Bounds(popup).X >= monitor.WorkArea.X && Bounds(popup).Right <= monitor.WorkArea.Right && Bounds(popup).Bottom <= monitor.WorkArea.Bottom && !HoverPreviewGeometry.Overlaps(Bounds(popup), home));
                var popupPoint = Center(popup);
                shelf.TrackHoverPreview(1450, popupPoint, false, false);
                shelf.TrackHoverPreview(20000, popupPoint, false, false);
                Check("pointer-can-remain-in-scrollable-preview", popup.IsVisible && popup.PreviewText == longText);
                popup.TextView.ScrollToEnd(); await Task.Delay(50);
                Check("long-text-preview-scrolls-to-last-lines", popup.TextView.GetFirstVisibleLineIndex() > 0 && popup.TextView.GetLastVisibleLineIndex() >= popup.TextView.LineCount - 2);
                Capture(popup, Path.Combine(directory, "text-preview-end.png"));
                shelf.TrackHoverPreview(20100, popupPoint, true, false);
                Check("scrollbar-button-press-does-not-dismiss-preview", popup.IsVisible);
                shelf.TrackHoverPreview(20200, popupPoint, false, true);
                Check("escape-dismisses-text-preview", !popup.IsVisible && popup.PreviewText.Length == 0);
                var outside = new PixelPoint(monitor.WorkArea.X + 2, monitor.WorkArea.Bottom - 2);
                shelf.TrackHoverPreview(20300, outside, false, false); shelf.TrackHoverPreview(20400, point, false, false); shelf.TrackHoverPreview(20800, point, false, false);
                var previewBounds = Bounds(popup); var gapPoint = new PixelPoint(point.X, (home.Bottom + previewBounds.Y) / 2);
                shelf.TrackHoverPreview(20850, gapPoint, false, false);
                Check("brief-travel-gap-keeps-preview-open", popup.IsVisible);
                shelf.TrackHoverPreview(21400, outside, false, false);
                Check("leaving-both-card-and-preview-releases-text", !popup.IsVisible && popup.PreviewText.Length == 0);
                shelf.TrackHoverPreview(22000, point, false, false); shelf.TrackHoverPreview(22400, point, false, false);
                var gesture = shelf.BeginLocalReorder(card, point, false);
                Check("starting-text-reorder-closes-preview", !popup.IsVisible);
                shelf.UpdateLocalReorder(point, true, true, 22500); await gesture;
                Check("cancelling-text-drag-retains-files-and-cards", File.Exists(first.Path) && File.Exists(second.Path) && shelf.Count == 2);
                shelf.Hide(); library.OpenWorkspace(first.Id, true);
                var editor = app.Windows.OfType<TextWorkspace>().Single(); await Until(() => !editor.Loading);
                Check("text-opens-in-editable-in-app-workspace", editor.Editor.Text == text && !editor.Editor.IsReadOnly && !editor.Dirty);
                library.OpenWorkspace(first.Id, false);
                Check("opening-same-card-reuses-existing-editor", app.Windows.OfType<TextWorkspace>().Count() == 1);
                editor.Editor.Text = "新的文字\r\nEdited in the same card.";
                Check("editing-marks-document-dirty", editor.Dirty);
                var added = library.Store.Find(first.Id)!.AddedAt; var oldOrder = library.Store.Entries(EntryState.Shelf).Select(e => e.Id).ToArray();
                Check("save-updates-original-card", await editor.Save() && !editor.Dirty && TextContent.Read(first.Path) == editor.Editor.Text && library.Store.Find(first.Id)!.AddedAt == added && library.Store.Entries(EntryState.Shelf).Select(e => e.Id).SequenceEqual(oldOrder));
                library.RefreshShelf(false); await Until(() => Find(first.Id)?.Item.Text == editor.Editor.Text);
                Check("saved-text-refreshes-card-and-title", Find(first.Id)!.Item.Title == "新的文字");
                Capture(editor, Path.Combine(directory, "text-editor.png"));
                var saved = editor.Editor.Text; editor.Editor.Text = " \r\n";
                Check("invalid-edit-keeps-editor-draft-and-saved-file", !await editor.Save() && editor.Dirty && TextContent.Read(first.Path) == saved);
                editor.Editor.Text = saved;
                library.Store.Archive(first.Id, LibraryRuntime.Now - (long)TimeSpan.FromDays(8).TotalMilliseconds);
                library.ApplySettings(library.Settings with { RetentionDays = 7 }, false); library.Cleanup();
                Check("open-text-editor-protects-file-from-history-cleanup", library.Store.Find(first.Id) is not null && File.Exists(first.Path));
                editor.Close(); library.Restore(first.Id); library.RefreshShelf(true);
                await Until(() => Find(first.Id) is not null);
                Check("text-history-restores-with-saved-content", library.Store.Find(first.Id)!.State == EntryState.Shelf && TextContent.Read(first.Path) == saved);
                var export = ImageFiles.Export(first.Path, Path.Combine(directory, "exported-text.txt"));
                Check("text-export-keeps-original-and-identical-content", File.Exists(first.Path) && TextContent.Read(export) == saved);
                shelf.HandleSession(Microsoft.Win32.SessionSwitchReason.SessionLock);
                Check("lock-clears-text-preview", !popup.IsVisible && popup.PreviewText.Length == 0);
                library.Dispose(); shelf.Dispose();
                using (var mixed = new ShelfController(() => false) { SuppressFullscreen = false, ReduceMotion = true })
                {
                    var photo = DemoAssets.Create(Path.Combine(directory, "mixed-fixtures"))[0];
                    mixed.SetItems(Enumerable.Range(0, 25).Select(i => i % 2 == 0
                        ? shortCard.Item with { EntryId = "mixed-" + i, Title = TextContent.Title(saved), Text = saved, Image = TextCardImage.Create(saved) }
                        : photo with { EntryId = "mixed-" + i }), true);
                    var target = app.Windows.OfType<CardWindow>().Single(c => c.Item.EntryId == "mixed-0");
                    await Until(() => target.IsVisible);
                    CardWindow[] Visible(bool text) => app.Windows.OfType<CardWindow>().Where(c => c.IsVisible && c.Item.IsText == text).OrderBy(c => Bounds(c).X).ToArray();
                    var work = Native.MonitorAt(Center(target)).WorkArea;
                    Check("media-before-text-with-shared-page-allocation", Visible(false).Length == mixed.ZoneCapacity(false) && Visible(true).Length == mixed.ZoneCapacity(true)
                        && Visible(false).Length + Visible(true).Length == mixed.DisplayCapacity && Bounds(Visible(false)[^1]).X < Bounds(Visible(true)[0]).X);
                    await Task.Delay(50); CaptureRow(Visible(false).Concat(Visible(true)).ToArray(), Path.Combine(directory, "shared-shelf.png"));
                    var mediaPage = Visible(false).Select(c => c.Item.EntryId).ToArray(); var notesPage = Visible(true).Select(c => c.Item.EntryId).ToArray();
                    target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    Check("text-paging-keeps-media-page-still", Visible(false).Select(c => c.Item.EntryId).SequenceEqual(mediaPage) && !Visible(true).Select(c => c.Item.EntryId).SequenceEqual(notesPage));
                    Visible(true)[0].RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    var targetBounds = Bounds(target); var exposed = new PixelPoint(targetBounds.X + 20, targetBounds.Y + targetBounds.Height / 2);
                    mixed.TrackHoverPreview(30000, exposed, false, false);
                    Check("mixed-stack-pops-text-card-without-changing-order", mixed.EmphasizedCard == target && mixed.Count == 25);
                    mixed.TrackHoverPreview(30400, Center(target), false, false);
                    var mixedPopup = app.Windows.OfType<TextHoverWindow>().Single();
                    Check("stacked-text-keeps-second-stage-complete-preview", mixedPopup.IsVisible && mixedPopup.PreviewText == saved);
                    mixed.TrackHoverPreview(30500, Center(mixedPopup), false, false);
                    Check("reading-stacked-text-keeps-card-emphasized", mixedPopup.IsVisible && mixed.EmphasizedCard == target);
                    var gestureText = mixed.BeginLocalReorder(target, Center(target), false);
                    mixed.UpdateLocalReorder(new PixelPoint(work.X + 100, Center(target).Y), true, false, 31000);
                    var drop = app.Windows.OfType<ShelfDropWindow>().Single(); var dropDpi = Native.GetDpiForWindow(new WindowInteropHelper(drop).Handle) / 96.0;
                    Check("text-drag-can-cross-midpoint-and-keeps-media-visible", drop.PreviewState(target.Item.EntryId!).X * dropDpi + Bounds(drop).X < work.X + work.Width / 2
                        && mediaPage.All(id => drop.PreviewState(id!).TargetAngle == 0) && drop.PreviewCount > mixed.ZoneCapacity(true));
                    mixed.UpdateLocalReorder(Center(target), true, true, 31100); await gestureText;
                    mixed.MoveCardBefore(target, null);
                    Check("text-reorder-never-mixes-with-media-order", mixed.OrderedIds.Take(12).All(id => int.Parse(id[6..]) % 2 == 1) && mixed.OrderedIds[^1] == "mixed-0");
                    var note = target.Item;
                    mixed.SetItems(Enumerable.Range(0, mixed.DisplayCapacity - 1).Select(i => photo with { EntryId = "photo-" + i }).Append(note), true);
                    await Task.Delay(50);
                    Check("media-borrows-right-space-with-one-note", Visible(false).Length == mixed.DisplayCapacity - 1 && Bounds(Visible(false)[^1]).X > work.X + work.Width / 2
                        && Visible(true).Length == 1 && Bounds(Visible(true)[0]).Right > work.Right - 50);
                    CaptureRow(Visible(false).Concat(Visible(true)).ToArray(), Path.Combine(directory, "fluid-media-heavy.png"));
                    mixed.TrackHoverPreview(31200, Center(Visible(true)[0]), false, false);
                    Check("single-note-at-media-boundary-pops-when-overlapped", mixed.EmphasizedCard == Visible(true)[0]);
                    mixed.SetItems(new[] { photo with { EntryId = "one-photo" } }.Concat(Enumerable.Range(0, mixed.DisplayCapacity - 1).Select(i => note with { EntryId = "note-" + i })), true);
                    await Task.Delay(50);
                    Check("text-borrows-left-space-with-one-image", Visible(true).Length == mixed.DisplayCapacity - 1 && Bounds(Visible(true)[0]).X < work.X + work.Width / 2);
                    CaptureRow(Visible(false).Concat(Visible(true)).ToArray(), Path.Combine(directory, "fluid-text-heavy.png"));
                    var onlyImage = Visible(false)[0]; var onlyImageBounds = Bounds(onlyImage);
                    mixed.TrackHoverPreview(31300, new PixelPoint(onlyImageBounds.X + 20, onlyImageBounds.Y + onlyImageBounds.Height / 2), false, false);
                    Check("single-image-at-text-boundary-pops-when-overlapped", mixed.EmphasizedCard == onlyImage);
                    mixed.SetItems(Enumerable.Range(0, mixed.DisplayCapacity + 1).Select(i => note with { EntryId = "only-note-" + i }), true);
                    var noteTotal = mixed.DisplayCapacity + 1;
                    Check("text-only-uses-full-row-capacity", Visible(true).Length == mixed.DisplayCapacity && Bounds(Visible(true)[0]).X < work.X + 50);
                    Visible(true)[0].RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    Check("short-final-text-page-remains-right-aligned", Visible(true).Length == 1 && Visible(true)[0].Item.EntryId == "only-note-" + (noteTotal - 1) && Bounds(Visible(true)[0]).Right > work.Right - 50);
                    mixed.SetItems([note], true);
                    mixed.UpdateDropFeedback(new PixelPoint(work.X + 100, work.Y + 70), 32000);
                    Check("incoming-image-into-empty-left-zone-preserves-right-preview", drop.PreviewCount == 1 && drop.PreviewState(target.Item.EntryId!).TargetAngle == 0);
                    mixed.Hide();
                }
                Check("text-disposal-releases-all-windows", !app.Windows.OfType<OverlayWindow>().Any() && !app.Windows.OfType<TextWorkspace>().Any());
                ImageDecoder.StopIdleWorkers();
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "text-card-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "text-card-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(window.Background, null, rect); dc.DrawRectangle(new VisualBrush(content), null, rect); }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); DemoAssets.Save(bitmap, path);
    }
    private static void CaptureRow(CardWindow[] cards, string path)
    {
        PixelRect Bounds(CardWindow c) { Native.GetWindowRect(new WindowInteropHelper(c).Handle, out var r); return r.Pixels; }
        var left = cards.Min(c => Bounds(c).X); var top = cards.Min(c => Bounds(c).Y);
        var width = cards.Max(c => Bounds(c).Right) - left; var height = cards.Max(c => Bounds(c).Bottom) - top;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(DemoAssets.Brush("#E9EFE9"), null, new Rect(0, 0, width, height));
            dc.DrawLine(new Pen(DemoAssets.Brush("#7A9687"), 2), new Point(0, 12), new Point(width, 12));
            foreach (var card in cards) { card.UpdateLayout(); var r = Bounds(card); dc.DrawRectangle(new VisualBrush((FrameworkElement)card.Content), null, new Rect(r.X - left, r.Y - top, r.Width, r.Height)); }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
