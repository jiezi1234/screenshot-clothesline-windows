using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Core;
using Clothesline.Storage;

namespace Clothesline.App;

internal static class DashboardDiagnostics
{
    internal static int Run(Application app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var passed = true;
        void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); passed &= ok; }
        app.Dispatcher.BeginInvoke((Action)(async () =>
        {
            Dashboard? dashboard = null; var initialLanguage = UiText.Language;
            try
            {
                using var shelf = new ShelfController();
                using var runtime = new LibraryRuntime(shelf, Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N")));
                dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.Show(); dashboard.UpdateLayout();
                TControl Named<TControl>(string name) where TControl : FrameworkElement { dashboard.UpdateLayout(); return UiAccessibility.Descendants(dashboard).OfType<TControl>().Single(c => AutomationProperties.GetName(c) == T(name)); }
                Button Action(string title) { dashboard.UpdateLayout(); return UiAccessibility.Descendants(dashboard).OfType<Button>().Single(b => b.Content as string == T(title) && b.IsVisible); }
                void Click(string title) => Action(title).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("default-page-is-shelf-with-accessible-empty-state", Named<ListBox>("晾衣绳列表").IsVisible && UiAccessibility.Descendants(dashboard).OfType<TextBlock>().Any(t => t.Text == T("暂时没有图片") && t.IsVisible));
                Capture(dashboard, Path.Combine(directory, "dashboard-empty.png"));
                var files = DemoAssets.Create(Path.Combine(directory, "fixtures", Guid.NewGuid().ToString("N")));
                foreach (var item in files)
                {
                    var path = Path.Combine(Path.GetDirectoryName(item.Path)!, item.Title + "-sample.png"); File.Copy(item.Path, path);
                    runtime.Store.Add(path, Ownership.External, "Fixture", Guid.NewGuid().ToString("N"), LibraryRuntime.Now, 25);
                }
                dashboard.ShowShelfList(); dashboard.UpdateLayout();
                Check("shelf-has-real-entries-and-selected-actions", Named<ListBox>("晾衣绳列表").Items.Count == 3 && Action("复制").IsEnabled && Action("编辑图片…").IsEnabled);
                Capture(dashboard, Path.Combine(directory, "dashboard-shelf.png"));
                var entry = runtime.Store.Entries(EntryState.Shelf)[0]; runtime.Store.Archive(entry.Id, LibraryRuntime.Now);
                dashboard.ShowHistory(); dashboard.UpdateLayout();
                Check("history-restore-enabled-for-selected-record", Named<ListBox>("本地历史").Items.Count == 1 && Action("重新挂上").IsEnabled);
                var search = Named<TextBox>("搜索历史"); search.Text = "not-a-fixture-" + Guid.NewGuid().ToString("N");
                Check("history-search-has-no-destructive-side-effects", Named<ListBox>("本地历史").Items.Count == 0 && !Action("重新挂上").IsEnabled && runtime.Store.Find(entry.Id)?.State == EntryState.History && File.Exists(entry.Path));
                search.Text = Path.GetFileName(entry.Path).ToUpperInvariant(); Check("history-search-matches-file-name-case-insensitively", Named<ListBox>("本地历史").Items.Count == 1);
                search.Text = ""; dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-history.png"));
                Click("重新挂上"); await Task.Delay(100); dashboard.ShowShelfList();
                Check("history-restore-returns-original-file-to-shelf", Named<ListBox>("晾衣绳列表").Items.Count == 3 && File.Exists(entry.Path));
                dashboard.ShowSources(); dashboard.UpdateLayout();
                var source = Named<TextBox>("截图目录"); var chosen = Path.Combine(directory, "incoming"); Directory.CreateDirectory(chosen); source.Text = chosen;
                Check("changed-source-enables-save-and-discard", dashboard.HasUnsavedChanges && Action("保存设置").IsEnabled && Action("撤销更改").IsEnabled);
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                var size = Named<ComboBox>("卡片大小"); size.SelectedIndex = 2;
                dashboard.ShowSources(); dashboard.UpdateLayout();
                Check("switching-pages-retains-unsaved-fields", source.Text == chosen && size.SelectedIndex == 2 && dashboard.HasUnsavedChanges);
                Click("暂停收集"); Check("pausing-collection-keeps-settings-draft", runtime.Settings.Paused && source.Text == chosen && dashboard.HasUnsavedChanges);
                Click("保存设置");
                Check("saving-applies-draft-across-settings-pages", runtime.Store.LoadSettings().ScreenshotFolder == chosen && runtime.Store.LoadSettings().CardSizePercent == 120 && runtime.Settings.Paused && !dashboard.HasUnsavedChanges && !Action("保存设置").IsEnabled);
                Click("恢复收集"); dashboard.ShowDesktopSettings(); dashboard.UpdateLayout();
                var show = Named<TextBox>("显示快捷键"); var hide = Named<TextBox>("收回快捷键"); show.Text = hide.Text;
                Click("保存设置"); Check("failed-save-keeps-draft-and-persisted-settings", dashboard.HasUnsavedChanges && show.Text == hide.Text && runtime.Store.LoadSettings().ShowShortcut == "Ctrl+Down");
                Click("撤销更改"); Check("discard-restores-persisted-shortcuts", !dashboard.HasUnsavedChanges && show.Text == "Ctrl+Down" && hide.Text == "Ctrl+Up");
                Capture(dashboard, Path.Combine(directory, "dashboard-shortcuts.png"));
                dashboard.ShowSources(); dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-sources.png"));
                dashboard.ShowAppearance(); dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-appearance.png"));
                size.Focus(); size.IsDropDownOpen = true; await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle); dashboard.UpdateLayout();
                var dropdown = (Popup)size.Template.FindName("PART_Popup", size);
                var popup = dropdown.Child as FrameworkElement; popup?.UpdateLayout();
                var popupPassed = dropdown.IsOpen && popup is not null && popup.ActualWidth >= size.ActualWidth - 1;
                checks.Add(new { name = "custom-combobox-opens-usable-popup", passed = popupPassed, open = dropdown.IsOpen, popupWidth = popup?.ActualWidth, controlWidth = size.ActualWidth }); passed &= popupPassed;
                size.IsDropDownOpen = false;
                dashboard.Width = 840; dashboard.Height = 620; dashboard.UpdateLayout();
                var button = Action("保存设置"); var bounds = button.TransformToAncestor((FrameworkElement)dashboard.Content).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                Check("save-remains-inside-small-window", bounds.Left >= 0 && bounds.Bottom <= ((FrameworkElement)dashboard.Content).ActualHeight && bounds.Right <= ((FrameworkElement)dashboard.Content).ActualWidth);
                Capture(dashboard, Path.Combine(directory, "dashboard-small.png")); dashboard.Close(); dashboard = null;
                foreach (var locale in UiText.Languages)
                {
                    UiText.Language = locale.Code;
                    dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.ShowAppearance(); dashboard.UpdateLayout();
                    Check("sidebar-and-save-accessible-" + locale.Code, Named<ListBox>("控制面板导航").Items.Count == 6 && Action("保存设置").IsVisible && Named<ComboBox>("界面语言（重启后生效）").Items.Count == 11);
                    Capture(dashboard, Path.Combine(directory, "dashboard-" + locale.Code + ".png"));
                    dashboard.Width = 840; dashboard.Height = 620;
                    var overflow = new List<string>();
                    foreach (var page in Enum.GetValues<DashboardPage>())
                    {
                        dashboard.Navigate(page); dashboard.UpdateLayout(); var content = (FrameworkElement)dashboard.Content;
                        if (page == DashboardPage.Formats)
                        {
                            // Finish the asynchronous probe before disposing its window or changing locale.
                            var deadline = DateTime.UtcNow.AddSeconds(30);
                            while (!Action("重新检测").IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
                            if (!Action("重新检测").IsEnabled) throw new TimeoutException("Image format panel did not finish its probe.");
                        }
                        foreach (var control in UiAccessibility.Descendants(dashboard).OfType<Control>().Where(c => c.IsVisible && c is Button or TextBox or ComboBox))
                        {
                            var rect = control.TransformToAncestor(content).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                            if (rect.Left < -1 || rect.Right > content.ActualWidth + 1) overflow.Add(page + ":" + AutomationProperties.GetName(control) + ":" + rect);
                        }
                    }
                    checks.Add(new { name = "all-pages-fit-small-window-" + locale.Code, passed = overflow.Count == 0, overflow }); passed &= overflow.Count == 0;
                    dashboard.Close(); dashboard = null;
                }
            }
            catch (Exception ex) { passed = false; checks.Add(new { name = "dashboard-diagnostics-completed", passed = false, error = ex.ToString() }); }
            finally
            {
                dashboard?.Close(); ImageDecoder.StopIdleWorkers(); UiText.Language = initialLanguage;
                File.WriteAllText(Path.Combine(directory, "dashboard-diagnostics.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(passed ? 0 : 1);
            }
        }));
        return app.Run();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content; var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Render(content); DemoAssets.Save(bitmap, path);
    }
}
