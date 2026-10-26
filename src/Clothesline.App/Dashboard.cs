using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Input;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed class Dashboard : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), MinHeight = 34 };
    private readonly ListBox history = new() { MinHeight = 180, DisplayMemberPath = "Label" };
    private readonly ListBox shelfList = new() { MinHeight = 100, DisplayMemberPath = "Title" };
    private readonly TabControl tabs = new();
    private readonly LibraryRuntime? library;
    private readonly TextBox screenshot = new();
    private readonly TextBox videoFolder = new();
    private readonly TextBox[] extras = [new(), new(), new()];
    private readonly CheckBox clipboard = new() { Content = T("收集剪贴板纯图片（默认关闭）") };
    private readonly CheckBox pause = new() { Content = T("暂停自动收集") };
    private readonly CheckBox popup = new() { Content = T("新项目加入时短暂展开") };
    private readonly CheckBox full = new() { Content = T("全屏时自动收起") };
    private readonly CheckBox motion = new() { Content = T("减少动画") };
    private readonly CheckBox sound = new() { Content = T("自动收集新项目时播放提示音（默认关闭）") };
    private readonly ComboBox cardSize = new() { ItemsSource = new[] { T("小 · 80%"), T("标准 · 100%"), T("大 · 120%") }, MinWidth = 200 };
    private readonly ComboBox language = new() { ItemsSource = Clothesline.Core.UiText.Languages, DisplayMemberPath = "Name", SelectedValuePath = "Code", MinWidth = 200 };
    private readonly TextBox showShortcut = new() { MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox hideShortcut = new() { MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox shortcutEnabled = new() { Content = T("启用全局快捷键") };
    private readonly CheckBox login = new() { Content = T("登录 Windows 时启动到托盘（默认关闭）") };
    private readonly TextBlock interactionHint = new() { Margin = new Thickness(0, 12, 0, 12), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox capacity = new() { ItemsSource = new[] { T("自动：当前屏幕一排"), T("25 项"), T("50 项"), T("100 项") }, MinWidth = 170 };
    private readonly ComboBox retention = new() { ItemsSource = new[] { T("永久保留（不自动清理）"), T("取下 7 天后清理"), T("取下 30 天后清理") }, MinWidth = 200 };
    private readonly ComboBox storage = new() { ItemsSource = new[] { "512 MiB", "1 GiB", "2 GiB" }, MinWidth = 120 };
    public bool Exiting { get; set; }
    public Dashboard(ShelfController controller, bool shortcutRegistered, Action quit, LibraryRuntime? library = null)
    {
        this.library = library;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Clothesline.Core.UiText.Language);
        Title = T("Screenshot Clothesline · 截图与历史"); Width = 880; Height = 760; MinWidth = 740; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = DemoAssets.Brush("#F4F5EE");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Foreground = DemoAssets.Brush("#294535");
        var root = new DockPanel { Margin = new Thickness(28, 20, 28, 20) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = T("把灵感，晾在手边。"), FontSize = 29 });
        header.Children.Add(new TextBlock { Text = T("截图与录像 / 本地历史     ·     Windows 11 x64"), Margin = new Thickness(0, 5, 0, 12), Foreground = DemoAssets.Brush("#748171") });
        var actions = new WrapPanel();
        actions.Children.Add(Button(T("显示 / 收起"), controller.Toggle)); actions.Children.Add(Button(T("导入图片或录像…"), Import));
        actions.Children.Add(Button(T("本地历史"), ShowHistory)); actions.Children.Add(Button(T("暂停 / 恢复"), () => { library?.TogglePause(); FillSettings(); }));
        actions.Children.Add(Button(T("全部取下"), () => library?.ArchiveBatch()));
        actions.Children.Add(Button(T("键盘浏览晾衣绳"), ShowShelfList));
        header.Children.Add(actions);
        UpdateShortcutHint();
        header.Children.Add(interactionHint); root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(status); var exit = new WrapPanel(); exit.Children.Add(Button(T("收起控制面板"), Hide)); exit.Children.Add(Button(T("退出"), quit)); footer.Children.Add(exit); root.Children.Add(footer);
        tabs.Items.Add(new TabItem { Header = T("来源与设置"), Content = SettingsPanel() }); tabs.Items.Add(new TabItem { Header = T("本地历史"), Content = HistoryPanel() });
        tabs.Items.Add(new TabItem { Header = T("快捷键与启动"), Content = DesktopSettingsPanel() });
        tabs.Items.Add(new TabItem { Header = T("外观与语言"), Content = AppearancePanel() });
        tabs.Items.Add(new TabItem { Header = T("格式支持"), Content = new ScrollViewer { Content = new ImageFormatPanel(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = T("晾衣绳列表"), Content = ShelfListPanel() }); root.Children.Add(tabs); Content = root;
        UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        AutomationProperties.SetName(screenshot, T("截图目录"));
        AutomationProperties.SetName(videoFolder, T("录像目录"));
        for (var i = 0; i < extras.Length; i++) AutomationProperties.SetName(extras[i], T("额外目录 {0}", i + 1));
        AutomationProperties.SetName(showShortcut, T("显示快捷键")); AutomationProperties.SetName(hideShortcut, T("收回快捷键")); AutomationProperties.SetName(history, T("本地历史"));
        controller.Status += SetStatus;
        if (library is not null) { library.Status += SetStatus; library.Changed += UpdateHistory; FillSettings(); UpdateHistory(); SetStatus(library.Settings.Paused ? T("自动收集已暂停。") : T("准备收集新增图片与录像；取下和超出容量的项目可从历史找回。")); } else SetStatus(T("界面诊断模式：没有启用收集。"));
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { controller.Status -= SetStatus; if (library is not null) { library.Status -= SetStatus; library.Changed -= UpdateHistory; } };
    }
    private FrameworkElement SettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label(T("截图目录（首次选择只监听新增图片）"))); panel.Children.Add(FolderRow(screenshot));
        panel.Children.Add(Label(T("额外目录：最多 3 个，不包含子目录"))); foreach (var text in extras) panel.Children.Add(FolderRow(text));
        panel.Children.Add(Label(T("录像目录（可选；只收集新增 MP4，不包含子目录）"))); panel.Children.Add(FolderRow(videoFolder));
        panel.Children.Add(new TextBlock { Text = T("支持 H.264 视频与 AAC 音频或无音轨，最高 4K、单文件 2 GiB。首次选择不导入旧录像；留空关闭。"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 8) });
        foreach (var check in new[] { clipboard, pause, popup, full, motion }) { check.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(check); }
        motion.ToolTip = T("关闭卡片入场、摆动、取下及绳线淡入；Windows 关闭动画时也会自动停用。鼠标进入卡片时立即停稳。");
        var limits = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; limits.Children.Add(Choice(T("晾衣绳容量"), capacity)); limits.Children.Add(Choice(T("历史期限"), retention)); limits.Children.Add(Choice(T("自管图片上限"), storage)); panel.Children.Add(limits);
        panel.Children.Add(new TextBlock { Text = T("取下或超出容量会进入历史。到期时外部原文件保留；应用保存的图片仅尝试移入回收站，失败保留记录。"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 12, 0, 12) });
        var container = new DockPanel(); var save = Button(T("保存设置"), SaveSettings); save.Margin = new Thickness(16, 8, 16, 10); DockPanel.SetDock(save, Dock.Bottom); container.Children.Add(save);
        container.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); return container;
    }
    private FrameworkElement DesktopSettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label(T("全局快捷键")));
        var shortcuts = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) }; shortcutEnabled.Margin = new Thickness(0, 5, 12, 5); shortcuts.Children.Add(shortcutEnabled);
        shortcuts.Children.Add(Button(T("重试当前注册"), () => { library?.Desktop?.RetryShortcut(); UpdateShortcutHint(); })); panel.Children.Add(shortcuts);
        panel.Children.Add(Choice(T("显示快捷键"), showShortcut)); panel.Children.Add(Choice(T("收回快捷键"), hideShortcut));
        shortcutEnabled.Checked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = true; shortcutEnabled.Unchecked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = false;
        panel.Children.Add(new TextBlock { Text = T("默认 Ctrl+Down（↓）显示，Ctrl+Up（↑）收回；重复按不会反向切换。留空可单独关闭，冲突时保留原设置。"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(new TextBlock { Text = T("组合至少包含 Ctrl 或 Alt，可增加 Shift；支持字母、数字、Up/Down（↑/↓）和 F1–F24（不含 F12）。关闭后仍可使用顶部悬停或托盘。"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        login.Margin = new Thickness(0, 24, 0, 0); panel.Children.Add(login);
        panel.Children.Add(new TextBlock { Text = T("登录时启动到托盘，不弹出控制面板。移动便携版目录后，请从新位置打开应用并重新保存设置以更新启动路径。Windows 的启动应用设置可能另外禁用此项。"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 20) });
        panel.Children.Add(Button(T("保存设置"), SaveSettings));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private FrameworkElement AppearancePanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Choice(T("卡片大小"), cardSize));
        panel.Children.Add(new TextBlock { Text = T("大小立即生效；自动容量会随每排可显示数量调整，超出的图片进入历史。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) });
        sound.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(sound);
        panel.Children.Add(new TextBlock { Text = T("连续收集最多每 2 秒播放一次；暂停或当前屏幕全屏时静音。提示音遵循 Windows 声音方案和音量设置。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(Choice(T("界面语言（重启后生效）"), language));
        panel.Children.Add(new TextBlock { Text = T("保存后，请先保存编辑内容，再从托盘退出并重新打开应用。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) });
        panel.Children.Add(Button(T("保存设置"), SaveSettings));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private FrameworkElement HistoryPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(16) }; var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom);
        actions.Children.Add(Button(T("预览"), () => Selected(Preview))); actions.Children.Add(Button(T("重新挂上"), () => Selected(e => library?.Restore(e.Id))));
        var edit = Button(T("编辑图片…"), () => Selected(e => library?.OpenWorkspace(e.Id, true))); edit.IsEnabled = false;
        history.SelectionChanged += (_, _) => edit.IsEnabled = history.SelectedItem is HistoryRow row && !Clothesline.Core.MediaFiles.IsVideo(row.Entry.Path);
        actions.Children.Add(edit);
        actions.Children.Add(Button(T("导出…"), () => Selected(e => library?.Export(e.Path)))); actions.Children.Add(Button(T("定位"), () => Selected(e => ShellTransfer.Reveal(e.Path))));
        actions.Children.Add(Button(T("移除记录"), () => Selected(e => library?.Forget(e.Id)))); actions.Children.Add(Button(T("回收文件"), () => Selected(e => library?.Recycle(e.Id)))); panel.Children.Add(actions);
        var note = new TextBlock { Text = T("外部引用依赖原路径。取下时间用于计算期限；重新挂上会取消计时。"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        history.MouseDoubleClick += (_, _) => Selected(Preview);
        history.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Selected(Preview); e.Handled = true; } };
        panel.Children.Add(history); return panel;
    }
    private FrameworkElement ShelfListPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(16) };
        var note = new TextBlock { Text = T("方向键选择，Enter 预览，Ctrl+C 复制，Delete 取下到历史。文件保留。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        void SelectedShelf(Action<LibraryEntry> action) { if (shelfList.SelectedItem is LibraryEntry entry) action(entry); }
        actions.Children.Add(Button(T("预览"), () => SelectedShelf(Preview)));
        actions.Children.Add(Button(T("复制"), () => _ = CopySelectedShelf()));
        var edit = Button(T("编辑图片…"), () => SelectedShelf(e => library?.OpenWorkspace(e.Id, true))); edit.IsEnabled = false;
        shelfList.SelectionChanged += (_, _) => edit.IsEnabled = shelfList.SelectedItem is LibraryEntry entry && !Clothesline.Core.MediaFiles.IsVideo(entry.Path);
        actions.Children.Add(edit);
        actions.Children.Add(Button(T("导出…"), () => SelectedShelf(e => library?.Export(e.Path))));
        actions.Children.Add(Button(T("取下（保留文件）"), () => SelectedShelf(e => library?.Archive(e.Id))));
        AutomationProperties.SetName(shelfList, T("晾衣绳列表")); AutomationProperties.SetHelpText(shelfList, note.Text);
        shelfList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectedShelf(Preview); e.Handled = true; }
            else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None) { SelectedShelf(x => library?.Archive(x.Id)); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { _ = CopySelectedShelf(); e.Handled = true; }
        };
        panel.Children.Add(shelfList); return panel;
    }
    private async System.Threading.Tasks.Task CopySelectedShelf()
    {
        if (shelfList.SelectedItem is not LibraryEntry entry) return;
        using var lease = library?.AcquireLease(entry.Path);
        try { var data = await ShellTransfer.CopyDataAsync(entry.Path); Clipboard.SetDataObject(data, true); SetStatus(T("已复制：") + entry.Title); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    private sealed record HistoryRow(LibraryEntry Entry)
    {
        public string Label => $"{Entry.Title}  ·  {(Entry.Missing ? T("文件失效") : Entry.Source == LibraryStore.OriginalSource ? T("编辑原版快照") : Entry.Ownership == Ownership.Managed ? T("应用保存") : T("外部引用"))}\n{(Entry.ArchivedAt is long time ? DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "")}  {Entry.Error}";
    }
    private void Selected(Action<LibraryEntry> action)
    { if (history.SelectedItem is HistoryRow row) { try { action(row.Entry); } catch (Exception ex) { SetStatus(ex.Message); } } else SetStatus(T("请先选择一条历史记录。")); }
    private void Preview(LibraryEntry entry)
    {
        library?.OpenWorkspace(entry.Id, false);
    }
    private void UpdateHistory()
    {
        var id = (history.SelectedItem as HistoryRow)?.Entry.Id;
        var rows = library?.Store.Entries(EntryState.History).Select(e => new HistoryRow(e)).ToArray() ?? [];
        history.ItemsSource = rows; history.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == id);
        var shelfId = (shelfList.SelectedItem as LibraryEntry)?.Id; var selectedIndex = shelfList.SelectedIndex;
        var entries = library?.Store.Entries(EntryState.Shelf).ToArray() ?? [];
        if (!(shelfList.ItemsSource is LibraryEntry[] old && old.SequenceEqual(entries)))
        {
            shelfList.ItemsSource = entries;
            shelfList.SelectedItem = entries.FirstOrDefault(e => e.Id == shelfId) ?? entries.ElementAtOrDefault(Math.Clamp(selectedIndex, 0, Math.Max(0, entries.Length - 1)));
        }
    }
    private void FillSettings()
    {
        if (library is null) return; var settings = library.Settings; screenshot.Text = settings.ScreenshotFolder; videoFolder.Text = settings.VideoFolder;
        for (var i = 0; i < 3; i++) extras[i].Text = i < settings.ExtraFolders.Length ? settings.ExtraFolders[i] : "";
        clipboard.IsChecked = settings.ClipboardEnabled; pause.IsChecked = settings.Paused; popup.IsChecked = settings.ShowNew; full.IsChecked = settings.SuppressFullscreen; motion.IsChecked = settings.ReduceMotion;
        capacity.SelectedIndex = Array.IndexOf(new[] { 0, 25, 50, 100 }, settings.Capacity); retention.SelectedIndex = Array.IndexOf(new[] { 0, 7, 30 }, settings.RetentionDays);
        storage.SelectedIndex = settings.MaxManagedBytes >= 2L * 1024 * 1024 * 1024 ? 2 : settings.MaxManagedBytes >= 1024L * 1024 * 1024 ? 1 : 0;
        cardSize.SelectedIndex = Array.IndexOf(new[] { 80, 100, 120 }, settings.CardSizePercent); sound.IsChecked = settings.CaptureSound;
        language.SelectedValue = settings.Language;
        shortcutEnabled.IsChecked = settings.ShowShortcut.Length > 0 || settings.HideShortcut.Length > 0;
        showShortcut.Text = shortcutEnabled.IsChecked == true ? settings.ShowShortcut : LibrarySettings.DefaultShowShortcut;
        hideShortcut.Text = shortcutEnabled.IsChecked == true ? settings.HideShortcut : LibrarySettings.DefaultHideShortcut;
        showShortcut.IsEnabled = hideShortcut.IsEnabled = shortcutEnabled.IsChecked == true; login.IsChecked = library.Desktop?.LoginEnabled ?? settings.RunAtLogin; UpdateShortcutHint();
    }
    private void UpdateShortcutHint() => interactionHint.Text = T("顶部悬停 / ") + (library?.Desktop?.ShortcutStatus ?? (library is null ? T("诊断模式") : T("显示 {0}；收回 {1}", library.Settings.ShowShortcut, library.Settings.HideShortcut))) + T(" · 单击复制 · 双击打开 · 长按编辑图片或播放录像 · 拖出 · 滚轮翻页");
    private void SaveSettings()
    {
        if (library is null) return;
        library.ApplySettings(library.Settings with { ScreenshotFolder = screenshot.Text.Trim(), VideoFolder = videoFolder.Text.Trim(), ExtraFolders = extras.Select(e => e.Text.Trim()).Where(p => p.Length > 0).ToArray(), ClipboardEnabled = clipboard.IsChecked == true, Paused = pause.IsChecked == true, ShowNew = popup.IsChecked == true, SuppressFullscreen = full.IsChecked == true, ReduceMotion = motion.IsChecked == true,
            Capacity = new[] { 0, 25, 50, 100 }[Math.Max(0, capacity.SelectedIndex)], RetentionDays = new[] { 0, 7, 30 }[Math.Max(0, retention.SelectedIndex)], MaxManagedBytes = new[] { 512L, 1024L, 2048L }[Math.Max(0, storage.SelectedIndex)] * 1024 * 1024,
            ShowShortcut = shortcutEnabled.IsChecked == true ? showShortcut.Text : "", HideShortcut = shortcutEnabled.IsChecked == true ? hideShortcut.Text : "", RunAtLogin = login.IsChecked == true,
            CardSizePercent = new[] { 80, 100, 120 }[Math.Max(0, cardSize.SelectedIndex)], CaptureSound = sound.IsChecked == true, Language = language.SelectedValue as string ?? "zh-CN" });
        FillSettings();
    }
    private async void Import()
    {
        if (library is null) return; var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = T("图片与 MP4 录像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.heic;*.heif;*.mp4|图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.heic;*.heif|MP4 录像|*.mp4") };
        if (dialog.ShowDialog(this) == true) await library.ImportFiles(dialog.FileNames);
    }
    private FrameworkElement FolderRow(TextBox text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 2) }; var browse = Button(T("选择…"), () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog(this) == true) text.Text = dialog.FolderName; });
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); text.VerticalContentAlignment = VerticalAlignment.Center; row.Children.Add(text); return row;
    }
    private static FrameworkElement Choice(string title, FrameworkElement control) { AutomationProperties.SetName(control, title); var p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; p.Children.Add(Label(title)); p.Children.Add(control); return p; }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 7, 0, 5), FontSize = 12 };
    private Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 4), Background = DemoAssets.Brush("#E2E9DD"), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } }; return button;
    }
    public void SetStatus(string text) => status.Text = text + (library is not null && library.Settings.Language != Clothesline.Core.UiText.Language ? "\n" + T("语言设置已保存，重启应用后生效。") : "");
    public void Reveal() { DesktopLifecycle.FollowForeground(this); Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowShelfList() { UpdateHistory(); tabs.SelectedIndex = 5; Reveal(); shelfList.Focus(); }
    public void ShowHistory() { UpdateHistory(); tabs.SelectedIndex = 1; Reveal(); }
    public void ShowDesktopSettings() { FillSettings(); tabs.SelectedIndex = 2; Reveal(); }
    public void ShowAppearance() { FillSettings(); tabs.SelectedIndex = 3; Reveal(); }
}
