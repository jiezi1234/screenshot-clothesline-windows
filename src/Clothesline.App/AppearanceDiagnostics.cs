using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class AppearanceDiagnostics
{
    public static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            Dashboard? dashboard = null; ImageWorkspace? workspace = null; CardWindow? card = null; RopeWindow? rope = null;
            var initialLanguage = UiText.Language;
            try
            {
                var root = Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N"));
                using (var store = new LibraryStore(root)) store.SaveSettings(new LibrarySettings { ClipboardEnabled = false, Language = "en", Capacity = 100 });
                using var shelf = new ShelfController(); using var runtime = new LibraryRuntime(shelf, root);
                Check("persisted-English-applied-before-dashboard", UiText.Language == "en");
                dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true };
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                var controls = Descendants(dashboard).ToArray();
                Check("appearance-controls-have-accessible-English-names", controls.OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Card size") && controls.OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Interface language (restart required)"));
                Check("English-dashboard-has-no-untranslated-visible-labels", controls.OfType<TextBlock>().Where(t => t.IsVisible).All(t => !System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"\p{IsCJKUnifiedIdeographs}")));
                Render((FrameworkElement)dashboard.Content, Path.Combine(directory, "appearance-en.png"));
                var size = controls.OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Card size");
                size.SelectedIndex = 2;
                var sound = controls.OfType<CheckBox>().Single(c => c.Content as string == T("自动收集新项目时播放提示音（默认关闭）")); sound.IsChecked = true;
                var save = controls.OfType<Button>().Single(b => b.Content as string == "Save settings" && b.IsVisible);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("saving-appearance-controls-persists-size-and-sound", runtime.Settings.CardSizePercent == 120 && runtime.Store.LoadSettings().CaptureSound);
                var monitor = Native.MonitorAt(Native.Cursor);
                rope = new RopeWindow { ShowEmptyHint = true }; var dpi = Native.MoveAndGetScale(rope, monitor);
                rope.Present(new PixelRect(monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, (int)(100 * dpi)), false); rope.UpdateLayout();
                Check("empty-rope-English-after-language-load", Descendants(rope).OfType<TextBlock>().Any(t => t.Text == "No images yet"));
                Check("card-size-updates-automatic-capacity", shelf.DisplayCapacity == ShelfZones.DisplayCapacity(monitor.WorkArea, dpi, 1.2));
                Render((FrameworkElement)rope.Content, Path.Combine(directory, "empty-en.png")); rope.Close(); rope = null;
                var fixture = DemoAssets.Create(Path.Combine(directory, "fixtures"))[0] with { Title = "Fixture image.png", Subtitle = "External file · Scroll for pages" };
                var total = shelf.DisplayCapacity + 3;
                for (var i = 0; i < total; i++) runtime.Store.Add(fixture.Path, Ownership.External, "Fixture", "size-" + i, LibraryRuntime.Now + i, 100);
                runtime.ApplySettings(runtime.Settings with { Capacity = 0, CardSizePercent = 120 });
                Check("large-size-overflow-keeps-records-and-files", runtime.Store.Entries(EntryState.Shelf).Count == shelf.DisplayCapacity && runtime.Store.Entries(EntryState.History).Count == 3 && runtime.Store.Entries().Count == total && File.Exists(fixture.Path));
                runtime.ApplySettings(runtime.Settings with { Capacity = 25, CardSizePercent = 80 });
                foreach (var entry in runtime.Store.Entries(EntryState.History)) runtime.Store.Restore(entry.Id, 25, LibraryRuntime.Now);
                runtime.ApplySettings(runtime.Settings with { CardSizePercent = 120 });
                Check("fixed-capacity-independent-of-card-size", runtime.Store.Entries(EntryState.Shelf).Count == total && runtime.Capacity == 25);
                runtime.ApplySettings(runtime.Settings with { Capacity = 0, CardSizePercent = 80 });
                var oldCapacity = shelf.DisplayCapacity; var capacityEvents = 0;
                shelf.DisplayCapacityChanged += () => capacityEvents++;
                shelf.SetCardSize(120); await Task.Delay(200);
                Check("automatic-shared-limit-refreshes-after-layout-capacity-change", runtime.Store.Entries(EntryState.Shelf).Count <= shelf.DisplayCapacity && (oldCapacity == shelf.DisplayCapacity || capacityEvents == 1));
                card = new CardWindow(fixture);
                foreach (var percent in new[] { 80, 100, 120 })
                {
                    Native.MoveAndGetScale(card, monitor);
                    var rect = DesktopGeometry.Cards(monitor.WorkArea, dpi, 1, percent / 100.0)[0];
                    card.Present(rect, false); await Task.Delay(40); card.UpdateLayout();
                    Native.GetWindowRect(new WindowInteropHelper(card).Handle, out var actual);
                    Check("card-native-bounds-" + percent, actual.Pixels == rect);
                    var view = (Viewbox)card.Content; var layout = (FrameworkElement)view.Child;
                    var remove = Descendants(layout).OfType<Button>().Single();
                    var box = remove.TransformToAncestor(view).TransformBounds(new Rect(0, 0, remove.ActualWidth, remove.ActualHeight));
                    Check("scaled-remove-button-inside-card-" + percent, box.Left >= 0 && box.Top >= 0 && box.Right <= view.ActualWidth + 1 && box.Bottom <= view.ActualHeight + 1 && box.Width > 18);
                    Check("card-keeps-base-layout-and-noactivate-" + percent, layout.ActualWidth == 212 && layout.ActualHeight == 190 && (Native.GetWindowLongPtrW(new WindowInteropHelper(card).Handle, -20).ToInt64() & 0x08000000) != 0);
                    Check("card-captions-stay-inside-view-" + percent, Descendants(layout).OfType<TextBlock>().All(t => t.TransformToAncestor(view).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight)).Bottom <= view.ActualHeight + .25));
                    Render(view, Path.Combine(directory, "card-" + percent + ".png"));
                }
                card.Close(); card = null;
                workspace = new ImageWorkspace([new("fixture", fixture.Path, fixture.Title)], 0, true, (_, _) => Task.FromResult<SavedImage?>(null), _ => new Release());
                workspace.Show();
                for (var i = 0; i < 100 && workspace.Loading; i++) await Task.Delay(20);
                workspace.UpdateLayout(); var editorControls = Descendants(workspace).ToArray();
                Check("English-workspace-loads-generated-image", workspace.Document is not null && editorControls.OfType<Button>().Any(b => b.Content as string == "Save a copy and hang…"));
                Check("editor-icon-controls-have-accessible-names", editorControls.OfType<Button>().Where(b => b.Content as string is "●" or "+" or "−").All(b => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
                Check("editor-inputs-have-accessible-names", editorControls.OfType<Slider>().All(s => AutomationProperties.GetName(s) == "Stroke width") && editorControls.OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Annotation text"));
                Render((FrameworkElement)workspace.Content, Path.Combine(directory, "editor-en.png")); workspace.Close(); workspace = null;
                var language = controls.OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Interface language (restart required)"); language.SelectedIndex = 0;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(20);
                Check("language-change-persists-without-replacing-open-UI", runtime.Settings.Language == "zh-CN" && UiText.Language == "en" && dashboard.Title.Contains("Images and history"));
                Check("language-save-shows-restart-notice", Descendants(dashboard).OfType<TextBlock>().Any(t => t.Text.Contains("Restart the app to apply it.")));
                dashboard.Close(); dashboard = null;
                // Emulate a fresh process using an isolated library and reconstructed windows, with no real startup/clipboard changes.
                UiText.Language = runtime.Store.LoadSettings().Language;
                dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.ShowAppearance(); dashboard.UpdateLayout();
                Check("Chinese-reconstruction-uses-saved-language", dashboard.Title.Contains("截图与历史") && Descendants(dashboard).OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "卡片大小"));
                Render((FrameworkElement)dashboard.Content, Path.Combine(directory, "appearance-zh.png")); dashboard.Close(); dashboard = null;
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "appearance-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                dashboard?.Close(); if (workspace is not null) { workspace.Document?.MarkSaved(); workspace.Close(); } card?.Close(); rope?.Close(); UiText.Language = initialLanguage;
                File.WriteAllText(Path.Combine(directory, "appearance-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private sealed class Release : IDisposable { public void Dispose() { } }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            dc.DrawRectangle(DemoAssets.Brush("#F4F5EE"), null, bounds); dc.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); DemoAssets.Save(bitmap, path);
    }
}
