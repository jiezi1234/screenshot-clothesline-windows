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
                Check("shelf-has-accessible-empty-state", Named<ListBox>("晾衣绳列表").IsVisible && UiAccessibility.Descendants(dashboard).OfType<TextBlock>().Any(t => t.Text == T("暂时没有图片") && t.IsVisible));
                if (!await dashboard.SettingsReady.WaitAsync(TimeSpan.FromSeconds(15))) throw new InvalidOperationException("Settings WebView failed to initialize.");
                Task<string> Web(string js) => dashboard.InspectSettings(js);
                async Task SetChoice(string id, int index)
                {
                    await Web($"var el = document.getElementById('{id}'); el.value = '{index}'; el.dispatchEvent(new Event('change', {{ bubbles: true }}))");
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                Check("coss-settings-share-one-page", await Web("document.querySelectorAll('section[data-slot=card]').length === 4 && document.querySelectorAll('[role=switch]').length === 7 && document.querySelectorAll('select').length === 5 && document.querySelectorAll('input:not([type=hidden])').length >= 7") == "true");
                await Task.Delay(400); // Allow the composition surface to present its first frame.
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
                Check("github-link-remains-accessible", Action("GitHub").IsVisible);
                var githubButton = Action("GitHub");
                dashboard.ShowGithubCardForDiagnostics();
                await Task.Delay(150);
                Check("github-card-opens-on-hover", dashboard.GithubCardOpen);
                await Task.WhenAny(dashboard.GithubCardReady, Task.Delay(8000));
                await Task.Delay(1500);
                checks.Add(new { name = "github-card-state-before-capture", passed = true, state = dashboard.GithubCardState });
                checks.Add(new { name = "github-card-position", passed = true, bounds = dashboard.GithubCardBounds, position = dashboard.GithubCardPosition, button = dashboard.GithubButtonCenter });
                dashboard.UpdateGithubPointerForDiagnostics(new Point(githubButton.ActualWidth / 2, githubButton.ActualHeight / 2));
                await Task.Delay(350);
                var centerTransform = await dashboard.GithubCardTransformForDiagnostics();
                dashboard.UpdateGithubPointerForDiagnostics(new Point(githubButton.ActualWidth - 1, 1));
                await Task.Delay(350);
                var edgeTransform = await dashboard.GithubCardTransformForDiagnostics();
                Check("github-card-follows-pointer-with-original-transform", centerTransform.Length > 0 && edgeTransform.Length > 0 && centerTransform != edgeTransform);
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
                var donateButton = Action(T("捐赠"));
                Check("donate-link-remains-accessible", donateButton.IsVisible);
                dashboard.ShowDonateCardForDiagnostics();
                await Task.Delay(150);
                Check("donate-card-opens-on-hover", dashboard.DonateCardOpen);
                await Task.WhenAny(dashboard.DonateCardReady, Task.Delay(8000));
                await Task.Delay(1500);
                checks.Add(new { name = "donate-card-state", passed = true, state = dashboard.DonateCardState });
                dashboard.UpdateDonatePointerForDiagnostics(new Point(donateButton.ActualWidth / 2, donateButton.ActualHeight / 2));
                await Task.Delay(350);
                var donateCenterTransform = await dashboard.DonateCardTransformForDiagnostics();
                dashboard.UpdateDonatePointerForDiagnostics(new Point(donateButton.ActualWidth - 1, 1));
                await Task.Delay(350);
                var donateEdgeTransform = await dashboard.DonateCardTransformForDiagnostics();
                Check("donate-card-follows-pointer-with-original-transform", donateCenterTransform.Length > 0 && donateEdgeTransform.Length > 0 && donateCenterTransform != donateEdgeTransform);
                dashboard.CloseDonateCardForDiagnostics();
                Check("donate-card-closes-on-leave", !dashboard.DonateCardOpen);
                dashboard.ShowAppearance();
                Check("tray-focuses-appearance", await Web("document.activeElement.id") == JsonSerializer.Serialize("cardSize"));
                dashboard.ShowShelfList();
                Check("tray-focuses-shelf", shelfItems.IsKeyboardFocusWithin);
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
                var source = Named<TextBox>("截图目录"); var chosen = Path.Combine(directory, "incoming"); Directory.CreateDirectory(chosen);
                await Web($"var input = document.getElementById('screenshot'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, {JsonSerializer.Serialize(chosen)}); input.dispatchEvent(new Event('input', {{bubbles:true}}))");
                await Web("document.getElementById('motion').click()");
                Check("switch-edits-draft-in-coss-form", Named<CheckBox>("减少动画").IsChecked == true);
                Check("changed-source-enables-save-and-discard", dashboard.HasUnsavedChanges && Action("保存设置").IsEnabled && Action("撤销更改").IsEnabled);
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                var size = Named<ComboBox>("卡片大小"); await SetChoice("cardSize", 2);
                var protection = Named<ComboBox>("展开保护期");
                Check("protection-defaults-to-ten-seconds-with-five-choices", protection.SelectedIndex == 1 && protection.Items.Count == 5 && shelf.RevealProtectionSeconds == 10);
                await SetChoice("protection", 3);
                dashboard.ShowSources(); dashboard.UpdateLayout();
                Check("focus-changes-retain-unsaved-fields", source.Text == chosen && size.SelectedIndex == 2 && protection.SelectedIndex == 3 && dashboard.HasUnsavedChanges);
                Click("暂停收集"); Check("pausing-collection-keeps-settings-draft", runtime.Settings.Paused && source.Text == chosen && dashboard.HasUnsavedChanges && shelf.RevealProtectionSeconds == 10);
                Click("保存设置");
                Check("saving-applies-draft-across-sections", runtime.Store.LoadSettings().ScreenshotFolder == chosen && runtime.Store.LoadSettings().CardSizePercent == 120 && runtime.Settings.Paused && !dashboard.HasUnsavedChanges && !Action("保存设置").IsEnabled);
                Check("saving-protection-persists-and-applies-to-running-controller", runtime.Settings.RevealProtectionSeconds == 30 && runtime.Store.LoadSettings().RevealProtectionSeconds == 30 && shelf.RevealProtectionSeconds == 30);
                Click("恢复收集"); dashboard.ShowDesktopSettings(); dashboard.UpdateLayout();
                var show = Named<TextBox>("显示快捷键"); var hide = Named<TextBox>("收回快捷键"); show.Text = hide.Text;
                Click("保存设置"); Check("failed-save-keeps-draft-and-persisted-settings", dashboard.HasUnsavedChanges && show.Text == hide.Text && runtime.Store.LoadSettings().ShowShortcut == "Ctrl+Down");
                Click("撤销更改"); Check("discard-restores-persisted-shortcuts", !dashboard.HasUnsavedChanges && show.Text == "Ctrl+Down" && hide.Text == "Ctrl+Up");
                Capture(dashboard, Path.Combine(directory, "dashboard-shortcuts.png"));
                dashboard.ShowSources(); dashboard.UpdateLayout(); Capture(dashboard, Path.Combine(directory, "dashboard-sources.png"));
                dashboard.ShowAppearance(); dashboard.UpdateLayout();
                protection.SelectedIndex = 4;
                Check("protection-only-change-enables-save-without-applying-draft", dashboard.HasUnsavedChanges && Action("保存设置").IsEnabled && shelf.RevealProtectionSeconds == 30);
                Click("撤销更改");
                Check("discard-restores-saved-protection", protection.SelectedIndex == 3 && !dashboard.HasUnsavedChanges && shelf.RevealProtectionSeconds == 30);
                protection.SelectedIndex = 1; Click("保存设置");
                Check("protection-can-return-to-default", runtime.Store.LoadSettings().RevealProtectionSeconds == 10 && shelf.RevealProtectionSeconds == 10);
                Capture(dashboard, Path.Combine(directory, "dashboard-appearance.png"));
                Check("coss-choice-reflects-saved-value", await Web("+document.getElementById('protection').value") == "1");
                dashboard.Width = 840; dashboard.Height = 620; dashboard.UpdateLayout();
                var button = Action("保存设置"); var bounds = button.TransformToAncestor((FrameworkElement)dashboard.Content).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                Check("save-remains-inside-small-window", bounds.Left >= 0 && bounds.Bottom <= ((FrameworkElement)dashboard.Content).ActualHeight && bounds.Right <= ((FrameworkElement)dashboard.Content).ActualWidth);
                await Task.Delay(400); // Wait for WebView2 to present the resized surface.
                Capture(dashboard, Path.Combine(directory, "dashboard-small.png")); dashboard.Close(); dashboard = null;
                foreach (var locale in UiText.Languages)
                {
                    UiText.Language = locale.Code;
                    dashboard = new Dashboard(shelf, false, () => { }, runtime) { Exiting = true }; dashboard.ShowAppearance(); dashboard.UpdateLayout();
                    await dashboard.SettingsReady.WaitAsync(TimeSpan.FromSeconds(15));
                    Check("localized-coss-settings-" + locale.Code, Action("保存设置").IsVisible && await Web("document.documentElement.lang") == JsonSerializer.Serialize(locale.Code) && await Web("document.querySelector('#language').options.length") == "2");
                    Capture(dashboard, Path.Combine(directory, "dashboard-" + locale.Code + ".png"));
                    dashboard.Width = 840; dashboard.Height = 620;
                    dashboard.UpdateLayout();
                    await Task.Delay(200);
                    var outside = await Web("""
                        (() => {
                          const outside = [...document.querySelectorAll('section,section label,section button,section select,section input:not([type=hidden])')].filter(el => {
                            const r = el.getBoundingClientRect();
                            return !r.width || !r.height || r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1;
                          }).map(el => [el.id || el.tagName, el.getBoundingClientRect().toJSON()]);
                          if (document.documentElement.scrollHeight > innerHeight + 1 || document.documentElement.scrollWidth > innerWidth + 1)
                            outside.push(['document', innerWidth, innerHeight, document.documentElement.scrollWidth, document.documentElement.scrollHeight]);
                          return outside;
                        })()
                        """);
                    checks.Add(new { name = "all-coss-options-fit-small-window-" + locale.Code, passed = outside == "[]", outside }); passed &= outside == "[]";
                    var overflow = new List<string>();
                    var content = (FrameworkElement)dashboard.Content;
                    foreach (var control in UiAccessibility.Descendants(dashboard).OfType<Control>().Where(c => c.IsVisible && c is Button or TextBox or ComboBox))
                    {
                        var rect = Bounds(control);
                        if (rect.Left < -1 || rect.Top < -1 || rect.Right > content.ActualWidth + 1 || rect.Bottom > content.ActualHeight + 1)
                            overflow.Add(AutomationProperties.GetName(control) + ":" + rect);
                    }
                    checks.Add(new { name = "native-actions-fit-small-window-" + locale.Code, passed = overflow.Count == 0, overflow }); passed &= overflow.Count == 0;
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (!Action("重新检测").IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
                    if (!Action("重新检测").IsEnabled) throw new TimeoutException("Image format panel did not finish its probe.");
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
