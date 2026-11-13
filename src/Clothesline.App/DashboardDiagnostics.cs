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
using System.Windows.Input;
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
                runtime.ApplySettings(runtime.Settings with { Capacity = 25, ClipboardEnabled = false }, false);
                dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.Show(); dashboard.UpdateLayout();
                TControl Named<TControl>(string name) where TControl : FrameworkElement { dashboard.UpdateLayout(); return UiAccessibility.Descendants(dashboard).OfType<TControl>().Single(c => AutomationProperties.GetName(c) == T(name)); }
                Button Action(string title)
                {
                    dashboard.UpdateLayout();
                    bool Matches(Button b) => b.IsVisible && (b.Content as string == T(title) || AutomationProperties.GetName(b) == T(title));
                    var local = dashboard.ActiveDesktopContent is { } active ? UiAccessibility.Descendants(active).OfType<Button>().Where(Matches).ToArray() : [];
                    return local.Length == 1 ? local[0] : UiAccessibility.Descendants(dashboard).OfType<Button>().Single(Matches);
                }
                Rect Bounds(FrameworkElement element) { var content = (FrameworkElement)dashboard.Content; return element.TransformToAncestor(content).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
                void Click(string title) => Action(title).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("default-page-is-shelf-with-accessible-empty-state", Named<ListBox>("晾衣绳列表").IsVisible && UiAccessibility.Descendants(dashboard).OfType<TextBlock>().Any(t => t.Text == T("暂时没有图片") && t.IsVisible));
                Capture(dashboard, Path.Combine(directory, "dashboard-empty.png"));
                var files = DemoAssets.Create(Path.Combine(directory, "fixtures", Guid.NewGuid().ToString("N")));
                foreach (var item in files)
                {
                    var path = Path.Combine(Path.GetDirectoryName(item.Path)!, item.Title + "-sample.png"); File.Copy(item.Path, path);
                    runtime.Store.Add(path, Ownership.External, "Fixture", Guid.NewGuid().ToString("N"), LibraryRuntime.Now, 25);
                }
                var notePath = Path.Combine(Path.GetDirectoryName(files[0].Path)!, "sample-note.txt"); File.WriteAllText(notePath, "A short dashboard note.");
                runtime.Store.Add(notePath, Ownership.External, "Fixture", Guid.NewGuid().ToString("N"), LibraryRuntime.Now + 1, 25);
                dashboard.ShowShelfList(); await Task.Delay(250); dashboard.UpdateLayout();
                var shelfItems = Named<ListBox>("晾衣绳列表");
                Check("shelf-lists-mixed-items-with-selected-actions", shelfItems.Items.Count == 4 && Action("复制").IsEnabled);
                shelfItems.SelectedIndex = shelfItems.Items.OfType<LibraryEntry>().ToList().FindIndex(item => !MediaFiles.IsText(item.Path));
                dashboard.UpdateLayout();
                Check("shelf-selection-enables-image-editing", Action("编辑图片…").IsEnabled);
                var shelfThumbnails = UiAccessibility.Descendants(shelfItems).OfType<Image>().Where(image => image.IsVisible && image.ActualWidth >= 60 && image.Source is not null).Take(3).Select(Bounds).ToArray();
                Check("shelf-renders-real-image-thumbnails", shelfThumbnails.Length == 3);
                var mixedTiles = Enumerable.Range(0, shelfItems.Items.Count).Select(i => shelfItems.ItemContainerGenerator.ContainerFromIndex(i)).OfType<ListBoxItem>().Take(3).Select(Bounds).ToArray();
                Check("shelf-images-and-text-render-in-mixed-grid", mixedTiles.Length == 3 && Math.Abs(mixedTiles[0].Top - mixedTiles[1].Top) <= 1 && mixedTiles[1].Left > mixedTiles[0].Right && (mixedTiles[2].Left > mixedTiles[1].Right || mixedTiles[2].Top > mixedTiles[0].Bottom));
                var toggleBounds = Bounds(Action("显示 / 收起")); var importBounds = Bounds(Action("导入图片或录像…"));
                Check("header-actions-share-top-edge-and-height", Math.Abs(toggleBounds.Top - importBounds.Top) <= 1 && Math.Abs(toggleBounds.Height - importBounds.Height) <= 1);
                var navigationItems = Named<ListBox>("控制面板导航").Items.OfType<ListBoxItem>().Select(Bounds).ToArray();
                Check("navigation-items-share-left-and-right-edges", navigationItems.Length == 6 && navigationItems.All(item => Math.Abs(item.Left - navigationItems[0].Left) <= 1 && Math.Abs(item.Right - navigationItems[0].Right) <= 1));
                Check("github-link-remains-accessible", Action("GitHub").IsVisible);
                var githubButton = Action("GitHub");
                dashboard.ShowGithubCardForDiagnostics();
                await Task.Delay(150);
                Check("github-card-opens-on-hover", dashboard.GithubCardOpen);
                await Task.WhenAny(dashboard.GithubCardReady, Task.Delay(8000));
                await Task.Delay(1500);
                checks.Add(new { name = "github-card-state-before-capture", passed = true, state = dashboard.GithubCardState });
                checks.Add(new { name = "github-card-position", passed = true, bounds = dashboard.GithubCardBounds, position = dashboard.GithubCardPosition, button = dashboard.GithubButtonCenter });
                dashboard.CloseGithubCardForDiagnostics();
                githubButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
                Check("github-card-opens-on-click", dashboard.GithubCardOpen);
                Check("github-hover-tracking-restarts-after-close", dashboard.GithubHoverTracking);
                var buttonCenter = githubButton.PointToScreen(new Point(githubButton.ActualWidth / 2, githubButton.ActualHeight / 2));
                var outsideButton = githubButton.PointToScreen(new Point(githubButton.ActualWidth + 4, githubButton.ActualHeight / 2));
                var outsideWindow = dashboard.PointToScreen(new Point(-100, -100));
                dashboard.CheckGithubHoverForDiagnostics(buttonCenter, 1000);
                dashboard.CheckGithubHoverForDiagnostics(outsideButton, 1100);
                dashboard.CheckGithubHoverForDiagnostics(outsideButton, 1319);
                Check("github-card-keeps-transfer-grace-period", dashboard.GithubCardOpen);
                dashboard.CheckGithubHoverForDiagnostics(outsideButton, 1320);
                Check("github-card-closes-just-outside-button", !dashboard.GithubCardOpen && !dashboard.GithubHoverTracking);

                githubButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dashboard.CheckGithubHoverForDiagnostics(outsideButton, 2000);
                dashboard.CheckGithubHoverForDiagnostics(dashboard.GithubCardScreenCenter, 2100);
                dashboard.CheckGithubHoverForDiagnostics(dashboard.GithubCardScreenCenter, 2500);
                Check("github-card-stays-open-when-entering-card", dashboard.GithubCardOpen);
                dashboard.CheckGithubHoverForDiagnostics(outsideWindow, 2600);
                dashboard.CheckGithubHoverForDiagnostics(buttonCenter, 2700);
                dashboard.CheckGithubHoverForDiagnostics(buttonCenter, 3000);
                Check("github-card-stays-open-when-returning-to-button", dashboard.GithubCardOpen);
                dashboard.CheckGithubHoverForDiagnostics(outsideWindow, 3100);
                dashboard.CheckGithubHoverForDiagnostics(outsideWindow, 3320);
                Check("github-card-closes-after-leaving-window", !dashboard.GithubCardOpen);

                githubButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dashboard.Hide();
                Check("github-card-closes-with-dashboard", !dashboard.GithubCardOpen && !dashboard.GithubHoverTracking);
                dashboard.Show(); dashboard.UpdateLayout();
                githubButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Check("github-hover-tracking-restarts-after-dashboard-reopens", dashboard.GithubCardOpen && dashboard.GithubHoverTracking);
                dashboard.CheckGithubHoverForDiagnostics(outsideWindow, 4000);
                dashboard.CheckGithubHoverForDiagnostics(outsideWindow, 4220);
                Check("github-card-closes-after-dashboard-reopens", !dashboard.GithubCardOpen);
                dashboard.CloseGithubCardForDiagnostics();
                var shelfHandle = Named<Thumb>("移动窗口" + " " + T("晾衣绳列表"));
                var shelfFolder = UiAccessibility.Descendants(dashboard).OfType<Border>().Single(b => AutomationProperties.GetName(b) == T("晾衣绳列表") + " · SHELF.DIR");
                var beforeMove = new Point(Canvas.GetLeft(shelfFolder), Canvas.GetTop(shelfFolder));
                shelfHandle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                shelfHandle.RaiseEvent(new DragDeltaEventArgs(48, 16) { RoutedEvent = Thumb.DragDeltaEvent });
                shelfHandle.RaiseEvent(new DragCompletedEventArgs(48, 16, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Check("titlebar-drag-moves-native-folder", Canvas.GetLeft(shelfFolder) == beforeMove.X + 48 && Canvas.GetTop(shelfFolder) == beforeMove.Y + 16);
                var movedPosition = new Point(Canvas.GetLeft(shelfFolder), Canvas.GetTop(shelfFolder));
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                Check("folders-stay-open-independently", shelfFolder.IsVisible && Named<ComboBox>("卡片大小").IsVisible);
                dashboard.ShowShelfList(); dashboard.UpdateLayout();
                Check("reopening-folder-retains-position-and-front-order", Canvas.GetLeft(shelfFolder) == movedPosition.X && Canvas.GetTop(shelfFolder) == movedPosition.Y && Panel.GetZIndex(shelfFolder) > UiAccessibility.Descendants(dashboard).OfType<Border>().Where(b => AutomationProperties.GetName(b).EndsWith(" · APPEARANCE.CFG", StringComparison.Ordinal)).Select(Panel.GetZIndex).Single());
                shelfHandle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                shelfHandle.RaiseEvent(new DragDeltaEventArgs(-10000, -10000) { RoutedEvent = Thumb.DragDeltaEvent });
                Check("drag-stays-inside-desktop", Canvas.GetLeft(shelfFolder) == 0 && Canvas.GetTop(shelfFolder) == 0);
                shelfHandle.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
                Check("cancelled-drag-restores-position", Canvas.GetLeft(shelfFolder) == movedPosition.X && Canvas.GetTop(shelfFolder) == movedPosition.Y);
                Named<Button>("关闭" + " " + T("晾衣绳列表")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("closing-folder-preserves-its-content", !shelfFolder.IsVisible && shelfItems.Items.Count == 4);
                dashboard.ShowShelfList(); dashboard.UpdateLayout();
                Check("closed-folder-reopens-at-same-position", shelfFolder.IsVisible && Canvas.GetLeft(shelfFolder) == movedPosition.X && Canvas.GetTop(shelfFolder) == movedPosition.Y);
                Named<Button>("最小化" + " " + T("晾衣绳列表")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("minimized-folder-keeps-taskbar-entry", !shelfFolder.IsVisible && UiAccessibility.Descendants(dashboard).OfType<Button>().Any(b => b.IsVisible && b.Content as string == T("晾衣绳列表")));
                dashboard.ShowShelfList(); dashboard.UpdateLayout();
                var normalSize = new Size(shelfFolder.Width, shelfFolder.Height);
                Named<Button>("最大化" + " " + T("晾衣绳列表")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dashboard.UpdateLayout();
                Check("maximize-fills-desktop-with-accessible-titlebar", Canvas.GetLeft(shelfFolder) == 0 && Canvas.GetTop(shelfFolder) == 0 && shelfFolder.Width == ((Canvas)shelfFolder.Parent).ActualWidth);
                Named<Button>("最大化" + " " + T("晾衣绳列表")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dashboard.UpdateLayout();
                Check("maximize-restores-folder-size-and-position", shelfFolder.Width == normalSize.Width && shelfFolder.Height == normalSize.Height && Canvas.GetLeft(shelfFolder) == movedPosition.X && Canvas.GetTop(shelfFolder) == movedPosition.Y);
                Capture(dashboard, Path.Combine(directory, "dashboard-shelf.png"));
                var entry = runtime.Store.Entries(EntryState.Shelf).First(item => !MediaFiles.IsText(item.Path)); runtime.Store.Archive(entry.Id, LibraryRuntime.Now);
                dashboard.ShowHistory(); dashboard.UpdateLayout();
                Check("history-restore-enabled-for-selected-record", Named<ListBox>("图片" + " / " + "录像").Items.Count == 1 && Named<ListBox>("文字").Items.Count == 0 && Action("重新挂上").IsEnabled);
                var search = Named<TextBox>("搜索历史"); search.Text = "not-a-fixture-" + Guid.NewGuid().ToString("N");
                Check("history-search-has-no-destructive-side-effects", Named<ListBox>("图片" + " / " + "录像").Items.Count == 0 && Named<ListBox>("文字").Items.Count == 0 && !Action("重新挂上").IsEnabled && runtime.Store.Find(entry.Id)?.State == EntryState.History && File.Exists(entry.Path));
                search.Text = Path.GetFileName(entry.Path).ToUpperInvariant(); Check("history-search-matches-file-name-case-insensitively", Named<ListBox>("图片" + " / " + "录像").Items.Count == 1);
                search.Text = ""; dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-history.png"));
                Click("重新挂上"); await Task.Delay(100); dashboard.ShowShelfList();
                Check("history-restore-returns-original-file-to-shelf", Named<ListBox>("晾衣绳列表").Items.Count == 4 && File.Exists(entry.Path));
                dashboard.ShowSources(); dashboard.UpdateLayout();
                var source = Named<TextBox>("截图目录"); var chosen = Path.Combine(directory, "incoming"); Directory.CreateDirectory(chosen); source.Text = chosen;
                Check("changed-source-enables-save-and-discard", dashboard.HasUnsavedChanges && Action("保存设置").IsEnabled && Action("撤销更改").IsEnabled);
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                var size = Named<ComboBox>("卡片大小"); size.SelectedIndex = 2;
                var protection = Named<ComboBox>("展开保护期");
                Check("protection-defaults-to-ten-seconds-with-five-choices", protection.SelectedIndex == 1 && protection.Items.Count == 5 && shelf.RevealProtectionSeconds == 10);
                protection.SelectedIndex = 3;
                dashboard.ShowSources(); dashboard.UpdateLayout();
                Check("switching-pages-retains-unsaved-fields", source.Text == chosen && size.SelectedIndex == 2 && protection.SelectedIndex == 3 && dashboard.HasUnsavedChanges);
                Click("暂停收集"); Check("pausing-collection-keeps-settings-draft", runtime.Settings.Paused && source.Text == chosen && dashboard.HasUnsavedChanges && shelf.RevealProtectionSeconds == 10);
                Click("保存设置");
                Check("saving-applies-draft-across-settings-pages", runtime.Store.LoadSettings().ScreenshotFolder == chosen && runtime.Store.LoadSettings().CardSizePercent == 120 && runtime.Settings.Paused && !dashboard.HasUnsavedChanges && !Action("保存设置").IsEnabled);
                Check("saving-protection-persists-and-applies-to-running-controller", runtime.Settings.RevealProtectionSeconds == 30 && runtime.Store.LoadSettings().RevealProtectionSeconds == 30 && shelf.RevealProtectionSeconds == 30);
                Click("恢复收集"); dashboard.ShowDesktopSettings(); dashboard.UpdateLayout();
                var show = Named<TextBox>("显示快捷键"); var hide = Named<TextBox>("收回快捷键"); show.Text = hide.Text;
                Click("保存设置"); Check("failed-save-keeps-draft-and-persisted-settings", dashboard.HasUnsavedChanges && show.Text == hide.Text && runtime.Store.LoadSettings().ShowShortcut == "Ctrl+Down");
                Click("撤销更改"); Check("discard-restores-persisted-shortcuts", !dashboard.HasUnsavedChanges && show.Text == "Ctrl+Down" && hide.Text == "Ctrl+Up");
                Capture(dashboard, Path.Combine(directory, "dashboard-shortcuts.png"));
                dashboard.ShowSources(); dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-sources.png"));
                var sourceInputs = UiAccessibility.Descendants(dashboard).OfType<Control>().Where(control => control.IsVisible && control is TextBox or ComboBox).ToArray();
                Check("source-inputs-use-uniform-control-height", sourceInputs.Length > 0 && sourceInputs.All(control => Math.Abs(control.ActualHeight - 40) <= 1));
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                protection.SelectedIndex = 4;
                Check("protection-only-change-enables-save-without-applying-draft", dashboard.HasUnsavedChanges && Action("保存设置").IsEnabled && shelf.RevealProtectionSeconds == 30);
                Click("撤销更改");
                Check("discard-restores-saved-protection", protection.SelectedIndex == 3 && !dashboard.HasUnsavedChanges && shelf.RevealProtectionSeconds == 30);
                protection.SelectedIndex = 1; Click("保存设置");
                Check("protection-can-return-to-default", runtime.Store.LoadSettings().RevealProtectionSeconds == 10 && shelf.RevealProtectionSeconds == 10);
                Capture(dashboard, Path.Combine(directory, "dashboard-appearance.png"));
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
                    Check("sidebar-and-save-accessible-" + locale.Code, Named<ListBox>("控制面板导航").Items.Count == 6 && Action("保存设置").IsVisible && Named<ComboBox>("界面语言（重启后生效）").Items.Count == 11 && Named<ComboBox>("展开保护期").SelectedIndex == 1);
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
