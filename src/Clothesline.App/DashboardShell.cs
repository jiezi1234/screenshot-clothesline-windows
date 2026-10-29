using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Markup;
using Clothesline.Storage;
using Clothesline.Core;

namespace Clothesline.App;

internal enum DashboardPage { Shelf, History, Sources, Shortcuts, Appearance, Formats }

internal sealed partial class Dashboard : Window
{
    private readonly LibraryRuntime? library;
    private readonly ListBox navigation = new(), history = new(), shelfList = new();
    private readonly ContentControl pageHost = new();
    private readonly TextBlock pageTitle = new(), pageDescription = new(), collectionState = new(), status = new(), draftStatus = new();
    private readonly TextBox screenshot = new(), videoFolder = new(), showShortcut = new(), hideShortcut = new(), historySearch = new();
    private readonly TextBox[] extras = [new(), new(), new()];
    private readonly CheckBox clipboard = new() { Content = T("收集剪贴板图片和文字（默认关闭）") };
    private readonly CheckBox popup = new() { Content = T("新项目加入时短暂展开") };
    private readonly CheckBox full = new() { Content = T("全屏时自动收起") };
    private readonly CheckBox motion = new() { Content = T("减少动画") };
    private readonly CheckBox sound = new() { Content = T("自动收集新项目时播放提示音（默认关闭）") };
    private readonly CheckBox shortcutEnabled = new() { Content = T("启用全局快捷键") };
    private readonly CheckBox login = new() { Content = T("登录 Windows 时启动到托盘（默认关闭）") };
    private readonly ComboBox cardSize = new() { ItemsSource = new[] { T("小 · 80%"), T("标准 · 100%"), T("大 · 120%") } };
    private static readonly int[] protectionDurations = RevealGate.ProtectionSecondsOptions.ToArray();
    private readonly ComboBox protection = new() { ItemsSource = protectionDurations.Select(seconds => seconds == RevealGate.DefaultProtectionSeconds ? T("{0} 秒（默认）", seconds) : T("{0} 秒", seconds)).ToArray() };
    private readonly ComboBox language = new() { ItemsSource = Clothesline.Core.UiText.Languages, DisplayMemberPath = "Name", SelectedValuePath = "Code" };
    private readonly ComboBox capacity = new() { ItemsSource = new[] { T("自动：当前屏幕一排"), T("25 项"), T("50 项"), T("100 项") } };
    private readonly ComboBox retention = new() { ItemsSource = new[] { T("永久保留（不自动清理）"), T("取下 7 天后清理"), T("取下 30 天后清理") } };
    private readonly ComboBox storage = new() { ItemsSource = new[] { "512 MiB", "1 GiB", "2 GiB" } };
    private readonly TextBlock interactionHint = new();
    private readonly Border saveBar = new();
    private readonly Button save, discard, pauseCollection;
    private readonly FrameworkElement shelfEmpty, historyEmpty;
    private readonly FrameworkElement[] pages;
    private readonly string[] titles = [T("晾衣绳列表"), T("本地历史"), T("来源与设置"), T("快捷键与启动"), T("外观与语言"), T("格式支持")];
    private bool fillingSettings;
    private string savedFingerprint = "";
    public bool Exiting { get; set; }
    internal bool HasUnsavedChanges => DraftFingerprint() != savedFingerprint;

    public Dashboard(ShelfController controller, bool shortcutRegistered, Action quit, LibraryRuntime? library = null)
    {
        this.library = library; Resources = DashboardTheme.Create();
        Title = T("Screenshot Clothesline · 截图与历史"); Width = 1060; Height = 780; MinWidth = 840; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("CanvasBrush"); Foreground = Brush("TextBrush");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13; UseLayoutRounding = true;
        save = Button(T("保存设置"), SaveSettings, "PrimaryButton"); discard = Button(T("撤销更改"), () => { FillSettings(); SetStatus(""); }, "QuietButton");
        pauseCollection = Button(T("暂停收集"), () => { library?.TogglePause(); UpdateCollectionState(); }, "QuietButton");
        shelfEmpty = EmptyState(T("暂时没有图片"), T("导入图片或录像，或等待新的截图。"), Import);
        historyEmpty = EmptyState(T("暂无可显示的历史记录"), T("取下的项目会保留在这里；也可以清空搜索查看全部。"));
        pages = [ShelfListPanel(), HistoryPanel(), SettingsPanel(), DesktopSettingsPanel(), AppearancePanel(), Scroll(Card(T("格式支持"), new ImageFormatPanel { Margin = new Thickness(0) }))];
        var root = new Grid(); var sidebarColumn = new ColumnDefinition { Width = new GridLength(208) };
        root.ColumnDefinitions.Add(sidebarColumn); root.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new DockPanel { Margin = new Thickness(18, 26, 18, 18) };
        var brand = new StackPanel { Margin = new Thickness(12, 0, 0, 28) }; DockPanel.SetDock(brand, Dock.Top);
        var brandLine = new Grid(); brandLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(33) }); brandLine.ColumnDefinitions.Add(new ColumnDefinition()); brandLine.Children.Add(Glyph("\uE8A5", 23));
        var brandName = Text(T("晾衣绳"), 23); brandName.FontWeight = FontWeights.SemiBold; Grid.SetColumn(brandName, 1); brandLine.Children.Add(brandName); brand.Children.Add(brandLine);
        brand.Children.Add(Text(T("把灵感，晾在手边。"), 11, true, new Thickness(0, 9, 0, 0))); side.Children.Add(brand);
        var sideFooter = new StackPanel(); DockPanel.SetDock(sideFooter, Dock.Bottom);
        collectionState.FontSize = 12; collectionState.TextWrapping = TextWrapping.Wrap;
        var run = Stack(); run.Children.Add(collectionState); run.Children.Add(pauseCollection);
        sideFooter.Children.Add(new Border { Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 16, 0, 14), Child = run });
        sideFooter.Children.Add(Button(T("收起控制面板"), Hide, "QuietButton")); sideFooter.Children.Add(Button(T("退出"), quit, "QuietButton")); side.Children.Add(sideFooter);
        var icons = new[] { "\uE8A5", "\uE81C", "\uE8B7", "\uE765", "\uE790", "\uE946" };
        for (var i = 0; i < titles.Length; i++)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Glyph(icons[i], 16)); var label = Text(titles[i]); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 1); row.Children.Add(label);
            var item = new ListBoxItem { Content = row, Padding = new Thickness(12, 14, 10, 14), Margin = new Thickness(0, i == 2 ? 22 : 2, 0, 2) };
            AutomationProperties.SetName(item, titles[i]); navigation.Items.Add(item);
        }
        AutomationProperties.SetName(navigation, T("控制面板导航")); navigation.SelectionChanged += (_, e) => { if (e.Source == navigation) Navigate((DashboardPage)Math.Max(0, navigation.SelectedIndex)); };
        side.Children.Add(navigation); root.Children.Add(new Border { Background = Brush("SidebarBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = side });
        var main = new DockPanel { Margin = new Thickness(30, 26, 30, 0) }; Grid.SetColumn(main, 1); root.Children.Add(main);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 22) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); DockPanel.SetDock(header, Dock.Top);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 18, 0) }; pageTitle.FontSize = 27; pageTitle.FontWeight = FontWeights.SemiBold; pageTitle.TextWrapping = TextWrapping.Wrap;
        pageDescription.FontSize = 12; pageDescription.Foreground = Brush("MutedBrush"); pageDescription.Margin = new Thickness(0, 8, 0, 0); pageDescription.TextWrapping = TextWrapping.Wrap;
        heading.Children.Add(pageTitle); heading.Children.Add(pageDescription); header.Children.Add(heading);
        var headerActions = new WrapPanel { VerticalAlignment = VerticalAlignment.Top, MaxWidth = 340 }; headerActions.Children.Add(Button(T("显示 / 收起"), controller.Toggle)); headerActions.Children.Add(Button(T("导入图片或录像…"), Import, "PrimaryButton")); Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions); main.Children.Add(header);
        var footer = Stack(); DockPanel.SetDock(footer, Dock.Bottom);
        var saveLayout = new Grid(); saveLayout.ColumnDefinitions.Add(new ColumnDefinition()); saveLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        draftStatus.VerticalAlignment = VerticalAlignment.Center; draftStatus.TextWrapping = TextWrapping.Wrap; draftStatus.Foreground = Brush("MutedBrush"); saveLayout.Children.Add(draftStatus);
        var saveActions = new WrapPanel(); saveActions.Children.Add(discard); saveActions.Children.Add(save); Grid.SetColumn(saveActions, 1); saveLayout.Children.Add(saveActions);
        saveBar.Child = saveLayout; saveBar.BorderBrush = Brush("LineBrush"); saveBar.BorderThickness = new Thickness(0, 1, 0, 0); saveBar.Padding = new Thickness(0, 14, 0, 4); saveBar.Margin = new Thickness(0, 14, 0, 0); footer.Children.Add(saveBar);
        status.TextWrapping = TextWrapping.Wrap; status.FontSize = 11; status.Foreground = Brush("MutedBrush"); status.Margin = new Thickness(0, 14, 0, 16); status.MaxHeight = 46; footer.Children.Add(status); main.Children.Add(footer); main.Children.Add(pageHost); Content = root;
        SizeChanged += (_, _) => { sidebarColumn.Width = new GridLength(ActualWidth < 950 ? 180 : 208); main.Margin = new Thickness(ActualWidth < 950 ? 20 : 30, 26, ActualWidth < 950 ? 20 : 30, 0); headerActions.MaxWidth = ActualWidth < 950 ? 180 : 340; };
        UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        controller.Status += SetStatus;
        if (library is not null) { library.Status += SetStatus; library.Changed += UpdateHistory; FillSettings(); UpdateHistory(); SetStatus(T("准备收集新增图片与录像；取下和超出容量的项目可从历史找回。")); }
        else { savedFingerprint = DraftFingerprint(); SetStatus(T("界面诊断模式：没有启用收集。")); }
        AttachDraftTracking(); UpdateCollectionState(); Navigate(DashboardPage.Shelf);
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { controller.Status -= SetStatus; if (library is not null) { library.Status -= SetStatus; library.Changed -= UpdateHistory; } };
    }

    private FrameworkElement SettingsPanel()
    {
        AutomationProperties.SetName(screenshot, T("截图目录")); AutomationProperties.SetName(videoFolder, T("录像目录"));
        var capture = Stack(); capture.Children.Add(FolderRow(screenshot)); capture.Children.Add(Text(T("首次选择只监听新增文件；已有文件可手动导入。"), 12, true, new Thickness(0, 10, 0, 0)));
        var extra = Stack(); for (var i = 0; i < extras.Length; i++) { AutomationProperties.SetName(extras[i], T("额外目录 {0}", i + 1)); var row = FolderRow(extras[i]); row.Margin = new Thickness(0, 4, 0, 4); extra.Children.Add(row); }
        capture.Children.Add(new Expander { Header = T("额外目录：最多 3 个，不包含子目录"), Content = extra });
        var video = Stack(); video.Children.Add(FolderRow(videoFolder)); video.Children.Add(Text(T("支持 H.264 视频与 AAC 音频或无音轨，最高 4K、单文件 2 GiB。首次选择不导入旧录像；留空关闭。"), 12, true, new Thickness(0, 10, 0, 0)));
        var behavior = Stack(); foreach (var check in new[] { clipboard, popup, full }) behavior.Children.Add(check);
        var limits = Stack(); limits.Children.Add(Field(T("晾衣绳容量"), capacity)); limits.Children.Add(Field(T("历史期限"), retention)); limits.Children.Add(Field(T("自管内容上限"), storage));
        limits.Children.Add(Text(T("图片和录像靠左，文字靠右，自动共享整条绳子的空间。容量为所有类型合计；超额时最旧项目进入历史。"), 12, true));
        limits.Children.Add(Text(T("取下或超出容量会进入历史。到期时外部原文件保留；应用保存的内容仅尝试移入回收站，失败保留记录。"), 12, true));
        return Scroll(Card(T("截图目录"), capture), Card(T("录像目录"), video), Card(T("收集行为"), behavior), Card(T("容量与历史"), limits));
    }
    private FrameworkElement DesktopSettingsPanel()
    {
        var keys = Stack(); keys.Children.Add(shortcutEnabled); keys.Children.Add(Field(T("显示快捷键"), showShortcut)); keys.Children.Add(Field(T("收回快捷键"), hideShortcut));
        keys.Children.Add(Text(T("默认 Ctrl+Down（↓）显示，Ctrl+Up（↑）收回；重复按不会反向切换。留空可单独关闭，冲突时保留原设置。"), 12, true));
        var advanced = Stack(); advanced.Children.Add(Text(T("组合至少包含 Ctrl 或 Alt，可增加 Shift；支持字母、数字、Up/Down（↑/↓）和 F1–F24（不含 F12）。关闭后仍可使用顶部悬停或托盘。"), 12, true)); advanced.Children.Add(Button(T("重试当前注册"), () => { library?.Desktop?.RetryShortcut(); UpdateShortcutHint(); }));
        keys.Children.Add(new Expander { Header = T("更多选项"), Content = advanced });
        shortcutEnabled.Checked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = true; shortcutEnabled.Unchecked += (_, _) => showShortcut.IsEnabled = hideShortcut.IsEnabled = false;
        var launch = Stack(); launch.Children.Add(login); launch.Children.Add(Text(T("登录时启动到托盘，不弹出控制面板。移动便携版目录后，请从新位置打开应用并重新保存设置以更新启动路径。Windows 的启动应用设置可能另外禁用此项。"), 12, true, new Thickness(0, 10, 0, 0)));
        interactionHint.TextWrapping = TextWrapping.Wrap; interactionHint.Foreground = Brush("MutedBrush"); interactionHint.FontSize = 12; interactionHint.Margin = new Thickness(0, 10, 0, 0); keys.Children.Add(interactionHint);
        return Scroll(Card(T("全局快捷键"), keys), Card(T("登录启动"), launch));
    }
    private FrameworkElement AppearancePanel()
    {
        var visual = Stack(); visual.Children.Add(Field(T("卡片大小"), cardSize)); visual.Children.Add(motion); motion.ToolTip = T("关闭卡片入场、摆动、取下及绳线淡入；Windows 关闭动画时也会自动停用。鼠标进入卡片时立即停稳。");
        visual.Children.Add(Text(T("大小立即生效；自动容量会随每排可显示数量调整，超出的图片进入历史。"), 12, true, new Thickness(0, 8, 0, 0)));
        var timing = Field(T("展开保护期"), protection); timing.Margin = new Thickness(0, 18, 0, 8); visual.Children.Add(timing);
        visual.Children.Add(Text(T("给鼠标移入晾衣绳预留时间；保存后立即生效，已展开时重新计时。"), 12, true));
        var audio = Stack(); audio.Children.Add(sound); audio.Children.Add(Text(T("连续收集最多每 2 秒播放一次；暂停或当前屏幕全屏时静音。提示音遵循 Windows 声音方案和音量设置。"), 12, true, new Thickness(0, 8, 0, 0)));
        var locales = Stack(); locales.Children.Add(Field(T("界面语言（重启后生效）"), language)); locales.Children.Add(Text(T("保存后，请先保存编辑内容，再从托盘退出并重新打开应用。"), 12, true));
        return Scroll(Card(T("外观与动效"), visual), Card(T("提示音"), audio), Card(T("界面语言"), locales));
    }
    private FrameworkElement HistoryPanel()
    {
        var panel = new DockPanel(); AutomationProperties.SetName(history, T("本地历史")); AutomationProperties.SetName(historySearch, T("搜索历史"));
        historySearch.ToolTip = T("按文件名或路径搜索"); historySearch.TextChanged += (_, _) => UpdateHistory();
        var search = Field(T("搜索历史"), historySearch); DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var preview = Button(T("预览"), () => Selected(Preview)); var restore = Button(T("重新挂上"), () => Selected(e => library?.Restore(e.Id)), "PrimaryButton");
        var edit = Button(T("编辑图片…"), () => Selected(e => library?.OpenWorkspace(e.Id, true)));
        var more = More((T("导出…"), () => Selected(e => library?.Export(e.Path))), (T("定位"), () => Selected(e => ShellTransfer.Reveal(e.Path))), (T("移除记录"), () => Selected(e => library?.Forget(e.Id))), (T("回收文件"), () => Selected(e => library?.Recycle(e.Id))));
        foreach (var button in new[] { restore, preview, edit, more }) actions.Children.Add(button);
        void Selection() { var entry = (history.SelectedItem as HistoryRow)?.Entry; preview.IsEnabled = restore.IsEnabled = more.IsEnabled = entry is not null; edit.IsEnabled = entry is not null && !Clothesline.Core.MediaFiles.IsVideo(entry.Path); edit.Content = entry is not null && Clothesline.Core.MediaFiles.IsText(entry.Path) ? T("编辑文字…") : T("编辑图片…"); AutomationProperties.SetName(edit, edit.Content.ToString()); }
        history.SelectionChanged += (_, _) => Selection(); Selection();
        history.MouseDoubleClick += (_, _) => Selected(Preview); history.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Selected(Preview); e.Handled = true; } };
        history.ItemTemplate = MediaTemplate(true); panel.Children.Add(ListSurface(history, historyEmpty)); return panel;
    }
    private FrameworkElement ShelfListPanel()
    {
        var panel = new DockPanel(); var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        void SelectedShelf(Action<LibraryEntry> action) { if (shelfList.SelectedItem is LibraryEntry entry) action(entry); }
        var preview = Button(T("预览"), () => SelectedShelf(Preview)); var copy = Button(T("复制"), () => _ = CopySelectedShelf(), "PrimaryButton");
        var edit = Button(T("编辑图片…"), () => SelectedShelf(e => library?.OpenWorkspace(e.Id, true)));
        var more = More((T("导出…"), () => SelectedShelf(e => library?.Export(e.Path))), (T("定位"), () => SelectedShelf(e => ShellTransfer.Reveal(e.Path))), (T("取下（保留文件）"), () => SelectedShelf(e => library?.Archive(e.Id))), (T("全部取下"), () => library?.ArchiveBatch()));
        foreach (var button in new[] { copy, preview, edit, more }) actions.Children.Add(button);
        void Selection() { var entry = shelfList.SelectedItem as LibraryEntry; copy.IsEnabled = preview.IsEnabled = more.IsEnabled = entry is not null; edit.IsEnabled = entry is not null && !Clothesline.Core.MediaFiles.IsVideo(entry.Path); edit.Content = entry is not null && Clothesline.Core.MediaFiles.IsText(entry.Path) ? T("编辑文字…") : T("编辑图片…"); AutomationProperties.SetName(edit, edit.Content.ToString()); }
        shelfList.SelectionChanged += (_, _) => Selection(); Selection();
        var note = Text(T("方向键选择，Enter 预览，Ctrl+C 复制，Delete 取下到历史。文件保留。"), 11, true, new Thickness(0, 14, 0, 0)); DockPanel.SetDock(note, Dock.Bottom); panel.Children.Add(note);
        AutomationProperties.SetName(shelfList, T("晾衣绳列表")); AutomationProperties.SetHelpText(shelfList, note.Text);
        shelfList.MouseDoubleClick += (_, _) => SelectedShelf(Preview);
        shelfList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectedShelf(Preview); e.Handled = true; }
            else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None) { SelectedShelf(x => library?.Archive(x.Id)); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { _ = CopySelectedShelf(); e.Handled = true; }
        };
        shelfList.ItemTemplate = MediaTemplate(false); panel.Children.Add(ListSurface(shelfList, shelfEmpty)); return panel;
    }

    private Brush Brush(string key) => (Brush)Resources[key];
    private static StackPanel Stack() => new();
    private TextBlock Text(string text, double size = 13, bool muted = false, Thickness? margin = null) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(muted ? "MutedBrush" : "TextBrush"), Margin = margin ?? new Thickness(0) };
    private FrameworkElement Glyph(string symbol, double size)
    {
        if (symbol == "\uE8A5") return new Viewbox { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = new System.Windows.Shapes.Path { Data = Geometry.Parse("M1,5 Q14,11 27,5 M7,8 L7,23 L21,23 L21,8 M12,4 L12,12 M16,4 L16,12"), Stroke = Brush("AccentBrush"), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(1) } };
        return new TextBlock { Text = symbol, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("AccentBrush"), IsHitTestVisible = false };
    }
    private FrameworkElement Card(string title, FrameworkElement content)
    {
        var stack = Stack(); var heading = Text(title, 15); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 16); stack.Children.Add(heading); stack.Children.Add(content);
        return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = stack };
    }
    private static ScrollViewer Scroll(params FrameworkElement[] elements)
    { var stack = Stack(); foreach (var element in elements) stack.Children.Add(element); return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 0) }; }
    private FrameworkElement Field(string title, FrameworkElement control)
    { AutomationProperties.SetName(control, title); var stack = Stack(); stack.Margin = new Thickness(0, 0, 0, 14); stack.Children.Add(Text(title, 12, true, new Thickness(0, 0, 0, 7))); stack.Children.Add(control); return stack; }
    private FrameworkElement FolderRow(TextBox text)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(text);
        text.ToolTipOpening += (_, _) => text.ToolTip = text.Text; text.ToolTip = " ";
        var browse = Button(T("选择…"), () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog(this) == true) text.Text = dialog.FolderName; }); browse.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(browse, 1); row.Children.Add(browse); return row;
    }
    private Button Button(string title, Action action, string? style = null)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) }; if (style is not null) button.Style = (Style)Resources[style];
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } }; return button;
    }
    private Button More(params (string Title, Action Action)[] entries)
    {
        var menu = new ContextMenu(); foreach (var entry in entries) { var item = new MenuItem { Header = entry.Title, Padding = new Thickness(12, 8, 12, 8) }; item.Click += (_, _) => { try { entry.Action(); } catch (Exception ex) { SetStatus(ex.Message); } }; menu.Items.Add(item); }
        var button = Button(T("更多操作"), () => menu.IsOpen = true); button.ContextMenu = menu; menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; return button;
    }
    private FrameworkElement EmptyState(string title, string description, Action? action = null)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 340, Margin = new Thickness(24) };
        var icon = Glyph("\uE8A5", 34); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = new Thickness(0, 0, 0, 18); stack.Children.Add(icon);
        var heading = Text(title, 18); heading.TextAlignment = TextAlignment.Center; stack.Children.Add(heading); var note = Text(description, 13, true, new Thickness(0, 10, 0, 18)); note.TextAlignment = TextAlignment.Center; stack.Children.Add(note);
        if (action is not null) { var button = Button(T("导入图片或录像…"), action, "PrimaryButton"); button.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(button); }
        return stack;
    }
    private FrameworkElement ListSurface(ListBox list, FrameworkElement empty)
    {
        var grid = new Grid(); grid.Children.Add(list); grid.Children.Add(empty); return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(10), Child = grid };
    }
    private static DataTemplate MediaTemplate(bool archived)
    {
        var prefix = archived ? "Entry." : "";
        return (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="48"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Border Width="36" Height="42" Background="{DynamicResource SidebarBrush}" CornerRadius="8" HorizontalAlignment="Left" VerticalAlignment="Top"><TextBlock Text="&#xE8A5;" FontFamily="Segoe MDL2 Assets" FontSize="18" Foreground="{DynamicResource AccentBrush}" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <StackPanel Grid.Column="1"><TextBlock Text="{Binding PREFIXTitle}" ToolTip="{Binding PREFIXTitle}" FontSize="14" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" TextTrimming="CharacterEllipsis"/><TextBlock Text="{Binding PREFIXPath}" ToolTip="{Binding PREFIXPath}" FontSize="11" Foreground="{DynamicResource MutedBrush}" TextTrimming="CharacterEllipsis" Margin="0,6,0,0"/>DETAILS</StackPanel></Grid></DataTemplate>
        """.Replace("PREFIX", prefix).Replace("DETAILS", archived ? "<TextBlock Text=\"{Binding Label}\" FontSize=\"11\" Foreground=\"{DynamicResource MutedBrush}\" TextWrapping=\"Wrap\" Margin=\"0,5,0,0\"/>" : ""));
    }
    internal void Navigate(DashboardPage page)
    {
        if ((int)page < 0 || (int)page >= pages.Length) return;
        if (navigation.SelectedIndex != (int)page) { navigation.SelectedIndex = (int)page; return; }
        pageHost.Content = pages[(int)page]; pageTitle.Text = titles[(int)page];
        saveBar.Visibility = page is DashboardPage.Sources or DashboardPage.Shortcuts or DashboardPage.Appearance ? Visibility.Visible : Visibility.Collapsed;
        UpdatePageDescription(); UpdateDraftState();
    }
    private void UpdatePageDescription() => pageDescription.Text = navigation.SelectedIndex switch
    {
        0 => T("{0} 项 · 随手取用，文件保留", shelfList.Items.Count), 1 => T("{0} 项 · 可以重新挂上", history.Items.Count),
        2 => T("选择收集位置，安排保留方式。"), 3 => T("让常用操作更顺手。"), 4 => T("按你的习惯，调整晾衣绳。"), _ => T("查看这台电脑的图片格式支持。")
    };
    public void Reveal() { DesktopLifecycle.FollowForeground(this); Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowShelfList() { UpdateHistory(); Navigate(DashboardPage.Shelf); Reveal(); UpdateLayout(); shelfList.Focus(); }
    public void ShowHistory() { UpdateHistory(); Navigate(DashboardPage.History); Reveal(); }
    public void ShowSources() { Navigate(DashboardPage.Sources); Reveal(); }
    public void ShowDesktopSettings() { Navigate(DashboardPage.Shortcuts); Reveal(); }
    public void ShowAppearance() { Navigate(DashboardPage.Appearance); Reveal(); }
}
