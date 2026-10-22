using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clothesline.Storage;

namespace Clothesline.App;

internal sealed class Dashboard : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), MinHeight = 34 };
    private readonly ListBox history = new() { MinHeight = 180, DisplayMemberPath = "Label" };
    private readonly TabControl tabs = new();
    private readonly LibraryRuntime? library;
    private readonly TextBox screenshot = new();
    private readonly TextBox[] extras = [new(), new(), new()];
    private readonly CheckBox clipboard = new() { Content = "收集剪贴板纯图片（默认关闭）" };
    private readonly CheckBox pause = new() { Content = "暂停自动收集" };
    private readonly CheckBox popup = new() { Content = "新图片加入时短暂展开" };
    private readonly CheckBox full = new() { Content = "全屏时自动收起" };
    private readonly CheckBox motion = new() { Content = "减少动画" };
    private readonly ComboBox capacity = new() { ItemsSource = new[] { "自动：当前屏幕一排", "25 张", "50 张", "100 张" }, MinWidth = 170 };
    private readonly ComboBox retention = new() { ItemsSource = new[] { "永久保留（不自动清理）", "取下 7 天后清理", "取下 30 天后清理" }, MinWidth = 200 };
    private readonly ComboBox storage = new() { ItemsSource = new[] { "512 MiB", "1 GiB", "2 GiB" }, MinWidth = 120 };
    public bool Exiting { get; set; }
    public Dashboard(ShelfController controller, bool shortcutRegistered, Action quit, LibraryRuntime? library = null)
    {
        this.library = library;
        Title = "Screenshot Clothesline · 截图与历史"; Width = 880; Height = 760; MinWidth = 740; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = DemoAssets.Brush("#F4F5EE");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Foreground = DemoAssets.Brush("#294535");
        var root = new DockPanel { Margin = new Thickness(28, 20, 28, 20) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = "把灵感，晾在手边。", FontSize = 29 });
        header.Children.Add(new TextBlock { Text = "截图收集 / 本地历史     ·     Windows 11 x64", Margin = new Thickness(0, 5, 0, 12), Foreground = DemoAssets.Brush("#748171") });
        var actions = new WrapPanel();
        actions.Children.Add(Button("显示 / 收起", controller.Toggle)); actions.Children.Add(Button("导入图片…", Import));
        actions.Children.Add(Button("本地历史", ShowHistory)); actions.Children.Add(Button("暂停 / 恢复", () => { library?.TogglePause(); FillSettings(); }));
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = shortcutRegistered ? "顶部中央悬停或 Ctrl + Alt + T · 单击复制 · 双击打开 · 拖出 · 滚轮翻页" : "快捷键被占用，请使用按钮或托盘；可在卡片上滚轮翻页。", Margin = new Thickness(0, 12, 0, 12), FontSize = 12 }); root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(status); var exit = new WrapPanel(); exit.Children.Add(Button("收起控制面板", Hide)); exit.Children.Add(Button("退出", quit)); footer.Children.Add(exit); root.Children.Add(footer);
        tabs.Items.Add(new TabItem { Header = "来源与设置", Content = SettingsPanel() }); tabs.Items.Add(new TabItem { Header = "本地历史", Content = HistoryPanel() }); root.Children.Add(tabs); Content = root;
        controller.Status += SetStatus;
        controller.EmptyRevealRequested += Reveal;
        if (library is not null) { library.Status += SetStatus; library.Changed += UpdateHistory; FillSettings(); UpdateHistory(); SetStatus(library.Settings.Paused ? "自动收集已暂停。" : "准备收集新增图片；取下和超出容量的图片可从历史找回。"); } else SetStatus("界面诊断模式：没有启用收集。");
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { controller.Status -= SetStatus; controller.EmptyRevealRequested -= Reveal; if (library is not null) { library.Status -= SetStatus; library.Changed -= UpdateHistory; } };
    }
    private FrameworkElement SettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label("截图目录（首次选择只监听新增图片）")); panel.Children.Add(FolderRow(screenshot));
        panel.Children.Add(Label("额外目录：最多 3 个，不包含子目录")); foreach (var text in extras) panel.Children.Add(FolderRow(text));
        foreach (var check in new[] { clipboard, pause, popup, full, motion }) { check.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(check); }
        var limits = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; limits.Children.Add(Choice("晾衣绳容量", capacity)); limits.Children.Add(Choice("历史期限", retention)); limits.Children.Add(Choice("自管图片上限", storage)); panel.Children.Add(limits);
        panel.Children.Add(new TextBlock { Text = "取下或超出容量会进入历史。到期时外部原文件保留；应用保存的图片仅尝试移入回收站，失败保留记录。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 12, 0, 12) });
        var container = new DockPanel(); var save = Button("保存设置", SaveSettings); save.Margin = new Thickness(16, 8, 16, 10); DockPanel.SetDock(save, Dock.Bottom); container.Children.Add(save);
        container.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); return container;
    }
    private FrameworkElement HistoryPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(16) }; var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom);
        actions.Children.Add(Button("预览", () => Selected(Preview))); actions.Children.Add(Button("重新挂上", () => Selected(e => library?.Restore(e.Id))));
        actions.Children.Add(Button("导出…", () => Selected(e => library?.Export(e.Path)))); actions.Children.Add(Button("定位", () => Selected(e => ShellTransfer.Reveal(e.Path))));
        actions.Children.Add(Button("移除记录", () => Selected(e => library?.Forget(e.Id)))); actions.Children.Add(Button("回收文件", () => Selected(e => library?.Recycle(e.Id)))); panel.Children.Add(actions);
        var note = new TextBlock { Text = "外部引用依赖原路径。取下时间用于计算期限；重新挂上会取消计时。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        history.MouseDoubleClick += (_, _) => Selected(Preview); panel.Children.Add(history); return panel;
    }
    private sealed record HistoryRow(LibraryEntry Entry)
    {
        public string Label => $"{Entry.Title}  ·  {(Entry.Missing ? "文件失效" : Entry.Ownership == Ownership.Managed ? "应用保存" : "外部引用")}\n{(Entry.ArchivedAt is long time ? DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "")}  {Entry.Error}";
    }
    private void Selected(Action<LibraryEntry> action)
    { if (history.SelectedItem is HistoryRow row) { try { action(row.Entry); } catch (Exception ex) { SetStatus(ex.Message); } } else SetStatus("请先选择一条历史记录。"); }
    private void Preview(LibraryEntry entry)
    {
        var preview = new Window { Title = entry.Title, Width = 800, Height = 560, Owner = this, Background = DemoAssets.Brush("#1F2D26"), Content = new Image { Source = ImageFiles.Read(entry.Path), Stretch = Stretch.Uniform } };
        preview.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) preview.Close(); }; preview.Show();
    }
    private void UpdateHistory()
    {
        var id = (history.SelectedItem as HistoryRow)?.Entry.Id;
        var rows = library?.Store.Entries(EntryState.History).Select(e => new HistoryRow(e)).ToArray() ?? [];
        history.ItemsSource = rows; history.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == id);
    }
    private void FillSettings()
    {
        if (library is null) return; var settings = library.Settings; screenshot.Text = settings.ScreenshotFolder;
        for (var i = 0; i < 3; i++) extras[i].Text = i < settings.ExtraFolders.Length ? settings.ExtraFolders[i] : "";
        clipboard.IsChecked = settings.ClipboardEnabled; pause.IsChecked = settings.Paused; popup.IsChecked = settings.ShowNew; full.IsChecked = settings.SuppressFullscreen; motion.IsChecked = settings.ReduceMotion;
        capacity.SelectedIndex = Array.IndexOf(new[] { 0, 25, 50, 100 }, settings.Capacity); retention.SelectedIndex = Array.IndexOf(new[] { 0, 7, 30 }, settings.RetentionDays);
        storage.SelectedIndex = settings.MaxManagedBytes >= 2L * 1024 * 1024 * 1024 ? 2 : settings.MaxManagedBytes >= 1024L * 1024 * 1024 ? 1 : 0;
    }
    private void SaveSettings()
    {
        if (library is null) return;
        library.ApplySettings(library.Settings with { ScreenshotFolder = screenshot.Text.Trim(), ExtraFolders = extras.Select(e => e.Text.Trim()).Where(p => p.Length > 0).ToArray(), ClipboardEnabled = clipboard.IsChecked == true, Paused = pause.IsChecked == true, ShowNew = popup.IsChecked == true, SuppressFullscreen = full.IsChecked == true, ReduceMotion = motion.IsChecked == true,
            Capacity = new[] { 0, 25, 50, 100 }[Math.Max(0, capacity.SelectedIndex)], RetentionDays = new[] { 0, 7, 30 }[Math.Max(0, retention.SelectedIndex)], MaxManagedBytes = new[] { 512L, 1024L, 2048L }[Math.Max(0, storage.SelectedIndex)] * 1024 * 1024 });
    }
    private async void Import()
    {
        if (library is null) return; var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif" };
        if (dialog.ShowDialog(this) == true) await library.ImportFiles(dialog.FileNames);
    }
    private FrameworkElement FolderRow(TextBox text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 2) }; var browse = Button("选择…", () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog(this) == true) text.Text = dialog.FolderName; });
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); text.VerticalContentAlignment = VerticalAlignment.Center; row.Children.Add(text); return row;
    }
    private static FrameworkElement Choice(string title, ComboBox combo) { var p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; p.Children.Add(Label(title)); p.Children.Add(combo); return p; }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 7, 0, 5), FontSize = 12 };
    private Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 4), Background = DemoAssets.Brush("#E2E9DD"), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } }; return button;
    }
    public void SetStatus(string text) => status.Text = text;
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowHistory() { UpdateHistory(); tabs.SelectedIndex = 1; Reveal(); }
}
