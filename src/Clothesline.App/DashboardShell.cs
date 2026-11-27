using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using Clothesline.Storage;
using Clothesline.Core;

namespace Clothesline.App;

internal enum DashboardPage { Shelf, History, Sources, Shortcuts, Appearance, Formats }

public sealed class DashboardThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path) || Clothesline.Core.MediaFiles.IsVideo(path) || Clothesline.Core.MediaFiles.IsText(path)) return null;
        try
        {
            using var stream = File.OpenRead(path); return ImageFiles.ReadStream(stream, true, Clothesline.Core.MediaFiles.IsExtendedImage(path));
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EntryGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string path && Clothesline.Core.MediaFiles.IsText(path) ? char.ConvertFromUtf32(0xE8A5) : char.ConvertFromUtf32(0xE91B);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

internal sealed partial class Dashboard : Window
{
    private readonly LibraryRuntime? library;
    private readonly ListBox history = new(), historyText = new(), shelfList = new();
    private readonly TextBlock status = new(), draftStatus = new();
    private readonly TextBox screenshot = new(), videoFolder = new(), showShortcut = new(), hideShortcut = new(), historySearch = new();
    private readonly TextBox[] extras = [new(), new(), new()];
    private readonly CheckBox clipboard = new() { Content = T("收集剪贴板图片和文字（默认开启）") };
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
    private readonly TextBlock capacity = new() { Text = T("自动：当前屏幕一排"), TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly ComboBox retention = new() { ItemsSource = new[] { T("永久保留（不自动清理）"), T("取下 7 天后清理"), T("取下 30 天后清理") } };
    private readonly ComboBox storage = new() { ItemsSource = new[] { "512 MiB", "1 GiB", "2 GiB" } };
    private readonly TextBlock interactionHint = new();
    private readonly Border saveBar = new();
    private readonly Button save, discard, pauseCollection;
    private readonly FrameworkElement shelfEmpty, historyEmpty;
    private readonly Dictionary<DashboardPage, FrameworkElement> sections = new();
    private readonly string[] titles = [T("晾衣绳列表"), T("本地历史"), T("来源与设置"), T("快捷键与启动"), T("外观与语言"), T("格式支持")];
    private bool fillingSettings;
    private string savedFingerprint = "";
    public bool Exiting { get; set; }
    internal bool HasUnsavedChanges => DraftFingerprint() != savedFingerprint;

    public Dashboard(ShelfController controller, bool shortcutRegistered, Action quit, LibraryRuntime? library = null)
    {
        this.library = library; Resources = DashboardTheme.Create();
        if (!SystemParameters.HighContrast)
        {
            Resources["CanvasBrush"] = Resources["SurfaceBrush"] = Brushes.White;
            Resources["TextBrush"] = Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(38, 39, 42));
            Resources["MutedBrush"] = Resources["Ink2Brush"] = new SolidColorBrush(Color.FromRgb(96, 98, 104));
            Resources["LineBrush"] = new SolidColorBrush(Color.FromRgb(227, 228, 230));
            Resources["LineStrongBrush"] = new SolidColorBrush(Color.FromRgb(206, 207, 211));
            Resources["HoverBrush"] = Resources["AccentSoftBrush"] = Resources["SelectedBrush"] = new SolidColorBrush(Color.FromRgb(239, 240, 242));
        }
        Resources["ButtonCorners"] = new CornerRadius(6);
        var primary = new Style(typeof(Button), (Style)Resources["PrimaryButton"]);
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Resources["TextBrush"]));
        primary.Triggers.Add(hover); Resources["PrimaryButton"] = primary;
        Resources["DisplayFont"] = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");
        Title = T("绳笺 Cordnote · 截图与历史"); Width = 1000; Height = 620; MinWidth = 840; MinHeight = 560;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("CanvasBrush"); Foreground = Brush("TextBrush");
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI"); FontSize = 13; UseLayoutRounding = true;
        save = Button(T("保存设置"), SaveSettings, "PrimaryButton"); discard = Button(T("撤销更改"), () => { FillSettings(); SetStatus(""); }, "QuietButton");
        pauseCollection = Button(T("暂停收集"), () => { library?.TogglePause(); UpdateCollectionState(); }, "QuietButton");
        shelfEmpty = EmptyState(T("暂时没有图片"), T("导入图片或录像，或等待新的截图。"));
        historyEmpty = EmptyState(T("暂无可显示的历史记录"), T("取下的项目会保留在这里；也可以清空搜索查看全部。"));
        settingsGroups = CreateSettingGroups();
        settingsById = settingsGroups.SelectMany(group => group.Fields).ToDictionary(field => field.Id);
        BuildNativeSettings();
        Content = CreateDesktop(controller, quit);
        UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !settingsWeb.IsKeyboardFocusWithin) { Hide(); e.Handled = true; } };
        controller.Status += SetStatus;
        if (library is not null) { library.Status += SetStatus; library.Changed += UpdateHistory; FillSettings(); UpdateHistory(); }
        else { savedFingerprint = DraftFingerprint(); SetStatus(T("界面诊断模式：没有启用收集。")); }
        AttachDraftTracking(); UpdateCollectionState(); UpdateDraftState();
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { controller.Status -= SetStatus; if (library is not null) { library.Status -= SetStatus; library.Changed -= UpdateHistory; } };
    }

    private static void AddListActions(Panel actions, params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            button.MinHeight = 26; button.Padding = new Thickness(7, 2, 7, 2);
            button.Margin = new Thickness(0, 0, 3, 1); button.FontSize = 12;
            actions.Children.Add(button);
        }
    }
    private FrameworkElement HistoryPanel()
    {
        var panel = new DockPanel(); AutomationProperties.SetName(history, T("图片") + " / " + T("录像")); AutomationProperties.SetName(historyText, T("文字")); AutomationProperties.SetName(historySearch, T("搜索历史"));
        historySearch.ToolTip = T("按文件名或路径搜索"); historySearch.TextChanged += (_, _) => UpdateHistory();
        var search = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var searchLabel = Text(T("搜索历史"), 12, true, new Thickness(0, 0, 8, 0)); searchLabel.VerticalAlignment = VerticalAlignment.Center;
        search.Children.Add(searchLabel); historySearch.Height = 28; historySearch.Padding = new Thickness(6, 2, 6, 2);
        search.Children.Add(historySearch); DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var actions = new WrapPanel();
        var actionBar = new Border { Background = Brushes.Transparent, BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(4, 4, 0, 2), Margin = new Thickness(0, 4, 0, 0), Child = actions };
        DockPanel.SetDock(actionBar, Dock.Bottom); panel.Children.Add(actionBar);
        var preview = Button(T("预览"), () => Selected(Preview)); var restore = Button(T("重新挂上"), () => Selected(e => library?.Restore(e.Id)), "PrimaryButton");
        var edit = Button(T("编辑图片…"), () => Selected(e => library?.OpenWorkspace(e.Id, true)));
        var more = More((T("导出…"), () => Selected(e => library?.Export(e.Path))), (T("定位"), () => Selected(e => ShellTransfer.Reveal(e.Path))), (T("移除记录"), () => Selected(e => library?.Forget(e.Id))), (T("回收文件"), () => Selected(e => library?.Recycle(e.Id))));
        AddListActions(actions, restore, preview, edit, more);
        var changingSelection = false;
        void Selection(ListBox source, ListBox other) { if (!changingSelection && source.SelectedItem is not null) { changingSelection = true; other.SelectedItem = null; changingSelection = false; } var entry = SelectedHistoryRow?.Entry; preview.IsEnabled = restore.IsEnabled = more.IsEnabled = entry is not null; edit.IsEnabled = entry is not null && Clothesline.Core.MediaFiles.IsImage(entry.Path); edit.Visibility = entry is not null && Clothesline.Core.MediaFiles.IsText(entry.Path) ? Visibility.Collapsed : Visibility.Visible; }
        history.SelectionChanged += (_, _) => Selection(history, historyText); historyText.SelectionChanged += (_, _) => Selection(historyText, history); Selection(history, historyText);
        void AttachHistoryInput(ListBox list) { list.MouseDoubleClick += (_, _) => Selected(Preview); list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Selected(Preview); e.Handled = true; } }; }
        AttachHistoryInput(history); AttachHistoryInput(historyText);
        history.ItemTemplate = MediaTemplate(true, true); historyText.ItemTemplate = MediaTemplate(true, false); panel.Children.Add(TwoColumnListSurface(history, historyText, historyEmpty)); return panel;
    }
    private FrameworkElement ShelfListPanel()
    {
        var panel = new DockPanel(); var actions = new WrapPanel();
        var actionBar = new Border { Background = Brushes.Transparent, BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(4, 4, 0, 2), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = actions };
        void SelectedShelf(Action<LibraryEntry> action) { if (SelectedShelfEntry is { } entry) action(entry); }
        var preview = Button(T("预览"), () => SelectedShelf(Preview)); var copy = Button(T("复制"), () => _ = CopySelectedShelf(), "PrimaryButton");
        var edit = Button(T("编辑图片…"), () => SelectedShelf(e => library?.OpenWorkspace(e.Id, true)));
        var more = More((T("导出…"), () => SelectedShelf(e => library?.Export(e.Path))), (T("定位"), () => SelectedShelf(e => ShellTransfer.Reveal(e.Path))), (T("取下（保留文件）"), () => SelectedShelf(e => library?.Archive(e.Id))), (T("全部取下"), () => library?.ArchiveBatch()));
        AddListActions(actions, copy, preview, edit, more);
        void Selection()
        {
            var entry = SelectedShelfEntry; copy.IsEnabled = preview.IsEnabled = more.IsEnabled = entry is not null;
            edit.IsEnabled = entry is not null && Clothesline.Core.MediaFiles.IsImage(entry.Path);
            edit.Visibility = entry is not null && Clothesline.Core.MediaFiles.IsText(entry.Path) ? Visibility.Collapsed : Visibility.Visible;
        }
        shelfList.SelectionChanged += (_, _) => Selection(); Selection();
        var shortcutHelp = T("方向键选择，Enter 预览，Ctrl+C 复制，Delete 取下到历史。文件保留。");
        var actionDock = new Grid(); actionDock.Children.Add(actionBar);
        DockPanel.SetDock(actionDock, Dock.Bottom); panel.Children.Add(actionDock);
        AutomationProperties.SetName(shelfList, T("晾衣绳列表")); AutomationProperties.SetHelpText(shelfList, shortcutHelp); shelfList.ToolTip = shortcutHelp;
        shelfList.MouseDoubleClick += (_, _) => SelectedShelf(Preview);
        shelfList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectedShelf(Preview); e.Handled = true; }
            else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None) { SelectedShelf(x => library?.Archive(x.Id)); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { _ = CopySelectedShelf(); e.Handled = true; }
        };
        shelfList.ItemTemplate = MediaTemplate(false, true, cord: true);
        panel.Children.Add(ShelfSurface(shelfList, shelfEmpty)); return panel;
    }
    private FrameworkElement ShelfSurface(ListBox items, FrameworkElement empty)
    {
        items.Background = Brushes.Transparent; items.HorizontalContentAlignment = HorizontalAlignment.Left; items.Margin = new Thickness(0, 8, 0, 0);
        items.ItemContainerStyle = (Style)Resources["ShelfItem"];
        var wrap = new FrameworkElementFactory(typeof(WrapPanel)); wrap.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        items.ItemsPanel = new ItemsPanelTemplate { VisualTree = wrap };
        var grid = new Grid(); grid.Children.Add(items); grid.Children.Add(empty);
        return new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 10, 0, 0), SnapsToDevicePixels = true, Child = grid };
    }

    private Brush Brush(string key) => (Brush)Resources[key];
    private FontFamily DisplayFont() => (FontFamily)Resources["DisplayFont"];
    private static Style SelectedBarStyle()
    {
        var style = new Style(typeof(Border));
        style.Triggers.Add(new DataTrigger { Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) }, Value = true, Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Visible) } });
        return style;
    }
    private static StackPanel Stack() => new();
    private TextBlock Text(string text, double size = 13, bool muted = false, Thickness? margin = null) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(muted ? "MutedBrush" : "TextBrush"), Margin = margin ?? new Thickness(0) };
    private FrameworkElement SectionTitle(string title)
    {
        return new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 5) };
    }
    private FrameworkElement Card(string title, FrameworkElement content)
    {
        var layout = new DockPanel();
        var heading = SectionTitle(title);
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(content);
        return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 6), Margin = new Thickness(2), Child = layout };
    }
    private FrameworkElement Field(string title, FrameworkElement control)
    { AutomationProperties.SetName(control, title); var stack = Stack(); stack.Margin = new Thickness(0, 0, 0, 14); var label = Text(title, 12.5, false, new Thickness(0, 0, 0, 7)); label.Foreground = Brush("Ink2Brush"); stack.Children.Add(label); stack.Children.Add(control); return stack; }
    private FrameworkElement FolderRow(TextBox text)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(text);
        text.ToolTipOpening += (_, _) => text.ToolTip = text.Text; text.ToolTip = " ";
        var browse = Button(T("选择…"), () => ChooseFolder(text)); browse.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(browse, 1); row.Children.Add(browse); return row;
    }
    private bool ChooseFolder(TextBox box)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) != true) return false;
        box.Text = dialog.FolderName;
        return true;
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
    private FrameworkElement EmptyState(string title, string description)
    {
        var heading = Text(title, 14, true);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.ToolTip = description;
        return heading;
    }
    private FrameworkElement TwoColumnListSurface(ListBox images, ListBox texts, FrameworkElement empty)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        var imageHeader = Text(T("图片") + " / " + T("录像"), 15, false, new Thickness(4, 2, 4, 4)); imageHeader.FontWeight = FontWeights.SemiBold; imageHeader.FontFamily = DisplayFont();
        var textHeader = Text(T("文字"), 15, false, new Thickness(14, 2, 4, 4)); textHeader.FontWeight = FontWeights.SemiBold; textHeader.FontFamily = DisplayFont(); Grid.SetColumn(textHeader, 2);
        var divider = new Border { Background = Brush("LineBrush"), Margin = new Thickness(0, 2, 0, 2) }; Grid.SetColumn(divider, 1); Grid.SetRowSpan(divider, 2);
        var imagePanel = new FrameworkElementFactory(typeof(WrapPanel)); imagePanel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        images.ItemsPanel = new ItemsPanelTemplate { VisualTree = imagePanel }; images.HorizontalContentAlignment = HorizontalAlignment.Left;
        images.Margin = new Thickness(0, 0, 8, 0); texts.Margin = new Thickness(8, 0, 0, 0); Grid.SetRow(images, 1); Grid.SetRow(texts, 1); Grid.SetColumn(texts, 2);
        grid.Children.Add(imageHeader); grid.Children.Add(textHeader); grid.Children.Add(divider); grid.Children.Add(images); grid.Children.Add(texts);
        Grid.SetRow(empty, 1); Grid.SetColumnSpan(empty, 3); grid.Children.Add(empty);
        return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(5), SnapsToDevicePixels = true, Child = grid };
    }
    private static DataTemplate MediaTemplate(bool archived, bool visual, bool cord = false)
    {
        var prefix = archived ? "Entry." : "";
        var tileWidth = visual ? (cord ? 96.0 : 88.0) : 44.0;
        var root = new FrameworkElementFactory(visual ? typeof(StackPanel) : typeof(DockPanel)); root.SetValue(FrameworkElement.MarginProperty, new Thickness(visual && !cord ? 8 : 0, 1, visual && !cord ? 8 : 0, 1));
        if (visual) root.SetValue(FrameworkElement.WidthProperty, tileWidth);
        var preview = new FrameworkElementFactory(typeof(Border));
        if (!visual) preview.SetValue(DockPanel.DockProperty, Dock.Left);
        preview.Name = "Preview";
        preview.SetValue(FrameworkElement.WidthProperty, tileWidth); preview.SetValue(FrameworkElement.HeightProperty, visual ? (cord ? 60.0 : 54.0) : 52.0); preview.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); preview.SetValue(FrameworkElement.MarginProperty, visual ? new Thickness(0, 0, 0, 9) : new Thickness(0, 0, 12, 0)); preview.SetValue(Border.CornerRadiusProperty, new CornerRadius(0)); preview.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("SoftBrush")); preview.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("LineBrush")); preview.SetValue(Border.BorderThicknessProperty, new Thickness(1)); preview.SetValue(UIElement.ClipToBoundsProperty, true); preview.SetBinding(FrameworkElement.ToolTipProperty, new Binding(prefix + "Path"));
        var previewGrid = new FrameworkElementFactory(typeof(Grid));
        var icon = new FrameworkElementFactory(typeof(TextBlock)); if (visual) icon.SetBinding(TextBlock.TextProperty, new Binding(prefix + "Path") { Converter = new EntryGlyphConverter() }); else icon.SetValue(TextBlock.TextProperty, char.ConvertFromUtf32(0xE8A5)); icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); icon.SetValue(TextBlock.FontSizeProperty, 18.0); icon.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension(visual ? "MutedBrush" : "AccentBrush")); icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); previewGrid.AppendChild(icon);
        if (visual)
        {
            var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(Image.StretchProperty, Stretch.UniformToFill); image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality); image.SetBinding(Image.SourceProperty, new Binding(prefix + "Path") { Converter = new DashboardThumbnailConverter() }); previewGrid.AppendChild(image);
        }
        preview.AppendChild(previewGrid); root.AppendChild(preview);
        var copy = new FrameworkElementFactory(typeof(StackPanel)); copy.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); if (visual) copy.SetValue(FrameworkElement.WidthProperty, tileWidth);
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(prefix + "Title")); title.SetBinding(FrameworkElement.ToolTipProperty, new Binding(prefix + "Path")); title.SetValue(TextBlock.FontSizeProperty, visual ? (cord ? 13.0 : 12.5) : 13.0); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); if (visual) title.SetValue(FrameworkElement.WidthProperty, tileWidth); copy.AppendChild(title);
        if (!cord)
        {
            var path = new FrameworkElementFactory(typeof(TextBlock)); path.SetBinding(TextBlock.TextProperty, new Binding(prefix + "Path")); path.SetBinding(FrameworkElement.ToolTipProperty, new Binding(prefix + "Path")); path.SetValue(TextBlock.FontSizeProperty, 11.0); path.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("MutedBrush")); path.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); path.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); path.SetValue(FrameworkElement.MarginProperty, new Thickness(0, visual ? 4 : 6, 0, 0)); if (visual) path.SetValue(FrameworkElement.WidthProperty, tileWidth); copy.AppendChild(path);
        }
        if (archived)
        {
            var details = new FrameworkElementFactory(typeof(TextBlock)); details.SetBinding(TextBlock.TextProperty, new Binding("Label")); details.SetValue(TextBlock.FontSizeProperty, 11.0); details.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("MutedBrush")); details.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); details.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0)); copy.AppendChild(details);
        }
        root.AppendChild(copy);
        var template = new DataTemplate { VisualTree = root };
        if (visual)
        {
            var selected = new DataTrigger { Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) }, Value = true };
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")) { TargetName = "Preview" });
            template.Triggers.Add(selected);
        }
        return template;
    }
    internal void Navigate(DashboardPage page)
    {
        currentSection = page;
        var id = page switch
        {
            DashboardPage.Sources => "screenshot", DashboardPage.Shortcuts => "shortcutEnabled",
            DashboardPage.Appearance => "cardSize", DashboardPage.Shelf => "shelf-stage",
            DashboardPage.History => "history-search", _ => null
        };
        if (id is null) return;
        if (!settingsLoaded.Task.IsCompleted) requestedSection = page;
        if (webConnected && settingsLoaded.Task.IsCompleted)
        {
            settingsWeb.Focus();
            PostSettings(new { type = "focus", id });
        }
        else if (page == DashboardPage.History) historySearch.Focus();
        else if (page == DashboardPage.Shelf) shelfList.Focus();
        else settingsById[id].Control.Focus();
    }
    public void Reveal() { DesktopLifecycle.FollowForeground(this); Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowShelfList() { UpdateHistory(); Reveal(); Navigate(DashboardPage.Shelf); }
    public void ShowHistory() { UpdateHistory(); Reveal(); Navigate(DashboardPage.History); }
    public void ShowSources() { Reveal(); Navigate(DashboardPage.Sources); }
    public void ShowDesktopSettings() { Reveal(); Navigate(DashboardPage.Shortcuts); }
    public void ShowAppearance() { Reveal(); Navigate(DashboardPage.Appearance); }
}
