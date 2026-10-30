using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

// Isolated generated records and offscreen controls only. No clipboard, input or visible windows.
internal static class ScreenQuotaDiagnostics
{
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool value) { passed &= value; checks.Add(new { name, passed = value }); }
        app.Dispatcher.BeginInvoke((Action)(() =>
        {
            try
            {
                var root = Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N"));
                using (var store = new LibraryStore(root))
                {
                    store.SaveSettings(new LibrarySettings { Capacity = 100, ClipboardEnabled = false, ShowNew = false, ReduceMotion = true });
                    for (var i = 0; i < 30; i++)
                    {
                        var image = Path.Combine(root, i + ".png"); File.WriteAllText(image, "Generated fixture");
                        var note = Path.Combine(store.ManagedFolder, i + ".txt"); File.WriteAllText(note, "Generated note " + i);
                        store.Add(image, Ownership.External, "Fixture", "media-" + i, i, 100, id: "media-" + i);
                        store.Add(note, Ownership.Managed, LibraryStore.TextSource, "note-" + i, i, 100, id: "note-" + i);
                    }
                }
                using var shelf = new ShelfController(() => false);
                using var runtime = new LibraryRuntime(shelf, root);
                var monitor = Native.MonitorAt(Native.Cursor);
                var rope = app.Windows.OfType<RopeWindow>().Single();
                var dpi = Native.GetDpiForWindow(new WindowInteropHelper(rope).Handle) / 96.0;
                Check("startup-measures-real-screen-before-applying-quota", runtime.Capacity == ShelfZones.DisplayCapacity(monitor.WorkArea, dpi)
                    && runtime.Store.Entries(EntryState.Shelf).Count == 60);
                Check("legacy-fixed-capacity-is-ignored-with-other-preferences-preserved", runtime.Settings.Capacity == 0 && !runtime.Settings.ClipboardEnabled && runtime.Settings.ReduceMotion);
                runtime.ApplySettings(runtime.Settings with { Capacity = 100 }, false);
                var quota = runtime.TypeCapacity;
                Check("legacy-fixed-choice-saves-as-automatic-screen-capacity", runtime.Settings.Capacity == 0 && runtime.Store.LoadSettings().Capacity == 0 && runtime.Capacity == shelf.DisplayCapacity);
                Check("upgrade-trims-each-category-independently-to-screen-half", runtime.Store.Entries(EntryState.Shelf).Count(e => MediaFiles.IsText(e.Path)) == quota
                    && runtime.Store.Entries(EntryState.Shelf).Count(e => !MediaFiles.IsText(e.Path)) == quota);
                Check("upgrade-keeps-newest-arrivals-and-all-original-files", runtime.Store.Find("note-29")!.State == EntryState.Shelf
                    && runtime.Store.Find("media-29")!.State == EntryState.Shelf && runtime.Store.Entries().All(e => File.Exists(e.Path)));
                runtime.ApplySettings(runtime.Settings with { CardSizePercent = 80 }, false);
                var smallerCards = runtime.Capacity;
                foreach (var entry in runtime.Store.Entries(EntryState.History)) runtime.Store.Restore(entry.Id, 100, 1000 + entry.AddedAt);
                runtime.ApplySettings(runtime.Settings with { Capacity = 25, CardSizePercent = 120 }, false);
                Check("card-size-change-recomputes-both-category-quotas", runtime.Capacity <= smallerCards && runtime.Capacity == shelf.DisplayCapacity
                    && runtime.Store.Entries(EntryState.Shelf).Count(e => MediaFiles.IsText(e.Path)) == runtime.TypeCapacity
                    && runtime.Store.Entries(EntryState.Shelf).Count(e => !MediaFiles.IsText(e.Path)) == runtime.TypeCapacity);
                var count = runtime.Store.Entries(EntryState.Shelf).Count;
                runtime.ApplySettings(runtime.Settings with { CardSizePercent = 80 }, false);
                Check("more-screen-space-does-not-restore-history-implicitly", runtime.Store.Entries(EntryState.Shelf).Count == count);
                foreach (var locale in UiText.Languages)
                {
                    UiText.Language = locale.Code;
                    var dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true };
                    try
                    {
                        dashboard.Navigate(DashboardPage.Sources);
                        var content = (FrameworkElement)dashboard.Content; content.Measure(new Size(1060, 740)); content.Arrange(new Rect(0, 0, 1060, 740)); content.UpdateLayout();
                        var controls = UiAccessibility.Descendants(content).OfType<FrameworkElement>().ToArray();
                        var label = controls.OfType<TextBlock>().Single(t => AutomationProperties.GetName(t) == UiText.T("晾衣绳容量"));
                        Check("capacity-is-live-readonly-and-localized-in-" + locale.Code,
                            label.Text == UiText.T("当前屏幕最多 {0} 项：图片与录像最多 {1} 项，文字最多 {1} 项。", runtime.Capacity, runtime.TypeCapacity)
                            && label.ActualWidth > 0 && !controls.OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == UiText.T("晾衣绳容量")));
                        if (locale.Code == "zh-CN")
                        {
                            label.BringIntoView(); content.UpdateLayout();
                            var bitmap = new RenderTargetBitmap(1060, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                            DemoAssets.Save(bitmap, Path.Combine(directory, "screen-capacity-settings.png"));
                        }
                    }
                    finally { dashboard.Close(); }
                }
                Check("screen-quota-diagnostics-do-not-show-windows", app.Windows.Cast<Window>().All(w => !w.IsVisible));
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "screen-quota-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                UiText.Language = "zh-CN";
                File.WriteAllText(Path.Combine(directory, "screen-quota-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
}
