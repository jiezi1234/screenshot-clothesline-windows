using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private readonly Canvas desktopCanvas = new() { ClipToBounds = true };
    private readonly Dictionary<string, DesktopFolder> desktopFolders = new();
    private readonly UniformGrid desktopTasks = new() { Rows = 1 };
    private static readonly DataTemplate TaskLabel = CreateTaskLabel();
    private static DataTemplate CreateTaskLabel()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetBinding(FrameworkElement.MaxWidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1), Converter = new TaskLabelWidthConverter() });
        return new DataTemplate { VisualTree = text };
    }
    private sealed class TaskLabelWidthConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width ? Math.Max(0, width - 14) : 0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private int desktopZ;
    private bool selectingDesktopFolder;
    private DesktopFolder? activeDesktopFolder;
    private static readonly string[] desktopNames = ["SHELF.DIR", "ARCHIVE.DIR", "SOURCES.CFG", "KEYBOARD.CFG", "APPEARANCE.CFG", "FORMATS.SYS"];
    private static FontFamily PixelFont => new(new Uri("pack://application:,,,/"), "./Assets/#VT323");

    private sealed class DesktopFolder
    {
        internal required string Id { get; init; }
        internal DashboardPage? Page { get; init; }
        internal required Border Frame { get; init; }
        internal required Grid Header { get; init; }
        internal required Thumb Handle { get; init; }
        internal required TextBlock Caption { get; init; }
        internal required Button Task { get; init; }
        internal TextBlock Description { get; } = new();
        internal bool Maximized { get; set; }
        internal Rect RestoreBounds { get; set; }
    }

    private FrameworkElement CreateDesktop(ShelfController controller, Action quit)
    {
        var root = new Grid { Background = Brush("SurfaceBrush"), ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var desktop = new Grid { ClipToBounds = true, Background = Brush("CanvasBrush") };
        desktop.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, desktop)) { try { DragMove(); } catch (InvalidOperationException) { } } };
        if (!SystemParameters.HighContrast)
        {
            using var stream = typeof(Dashboard).Assembly.GetManifestResourceStream("Cordnote.MakeBelieveClouds.png");
            if (stream is not null)
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                var clouds = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(clouds, BitmapScalingMode.NearestNeighbor); desktop.Children.Add(clouds);
            }
        }
        var icons = new ScrollViewer { Width = 110, Margin = new Thickness(12, 16, 0, 12), HorizontalAlignment = HorizontalAlignment.Left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = navigation };
        for (var i = 0; i < titles.Length; i++)
        {
            var page = (DashboardPage)i;
            var icon = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            icon.Children.Add(PixelFolder());
            var label = Text(titles[i], 12); label.TextAlignment = TextAlignment.Center; label.MaxWidth = 92; label.Margin = new Thickness(0, 7, 0, 0);
            icon.Children.Add(new Border { Child = label, Background = Brush("SurfaceBrush"), Padding = new Thickness(3, 1, 3, 2) });
            var item = new ListBoxItem { Content = icon, Padding = new Thickness(3, 6, 3, 6), Margin = new Thickness(0, 0, 0, 9), Style = (Style)Resources["NavigationItem"] };
            AutomationProperties.SetName(item, titles[i]); item.MouseLeftButtonUp += (_, _) => Navigate(page); navigation.Items.Add(item);
        }
        AutomationProperties.SetName(navigation, T("控制面板导航"));
        navigation.SelectionChanged += (_, e) => { if (!selectingDesktopFolder && e.Source == navigation && navigation.SelectedIndex >= 0) Navigate((DashboardPage)navigation.SelectedIndex); };
        navigation.KeyDown += (_, e) => { if (e.Key == Key.Enter && navigation.SelectedIndex >= 0) { Navigate((DashboardPage)navigation.SelectedIndex); e.Handled = true; } };
        desktop.Children.Add(icons); desktop.Children.Add(desktopCanvas);
        Grid.SetRow(desktop, 0); root.Children.Add(desktop);
        desktopCanvas.SizeChanged += (_, _) => FitDesktopFolders();

        var bottom = Stack();
        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 4, 10, 4) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = Button(T("显示 / 收起"), controller.Toggle);
        var import = Button(T("导入图片或录像…"), Import);
        foreach (var action in new[] { toggle, import, pauseCollection, Button(T("收起控制面板"), Hide), Button(T("退出"), quit) })
        { action.MinHeight = 28; action.Padding = new Thickness(8, 3, 8, 3); action.FontSize = 11.5; action.Margin = new Thickness(0, 0, 6, 0); actions.Children.Add(action); }
        var github = Button("GitHub", ShowGithubCard);
        github.MinHeight = 28; github.Padding = new Thickness(8, 3, 8, 3); github.FontSize = 11.5; github.Margin = new Thickness(0, 0, 6, 0); AutomationProperties.SetName(github, "GitHub"); actions.Children.Insert(0, github);
        DockPanel.SetDock(actions, Dock.Right); bar.Children.Add(actions);
        desktopTasks.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(desktopTasks);
        bottom.Children.Add(bar);
        var saveLayout = new DockPanel { LastChildFill = true };
        var saveActions = new WrapPanel(); saveActions.Children.Add(discard); saveActions.Children.Add(save);
        DockPanel.SetDock(saveActions, Dock.Right); saveLayout.Children.Add(saveActions);
        var saveInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        draftStatus.VerticalAlignment = VerticalAlignment.Center; draftStatus.TextWrapping = TextWrapping.Wrap; draftStatus.Margin = new Thickness(0, 0, 0, 2); saveInfo.Children.Add(draftStatus);
        status.FontSize = 11; status.TextTrimming = TextTrimming.CharacterEllipsis; status.TextWrapping = TextWrapping.NoWrap; status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0); status.MaxHeight = 20; saveInfo.Children.Add(status);
        saveLayout.Children.Add(saveInfo);
        saveBar.Child = saveLayout; saveBar.Padding = new Thickness(12, 6, 4, 3); saveBar.BorderBrush = Brush("LineBrush"); saveBar.BorderThickness = new Thickness(0, 1, 0, 0); saveBar.Visibility = Visibility.Visible; bottom.Children.Add(saveBar);
        var footer = new Border { Child = bottom, BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 2, 0, 0), Background = Brush("SurfaceBrush") };
        Grid.SetRow(footer, 1); root.Children.Add(footer);
        CreateGithubOverlay(github, root);

        var quick = Stack();
        quick.Children.Add(Text(T("快捷键与启动"), 18, margin: new Thickness(0, 0, 0, 14)));
        quick.Children.Add(Text(T("显示 {0}；收回 {1}", library?.Settings.ShowShortcut ?? "Ctrl+Down", library?.Settings.HideShortcut ?? "Ctrl+Up"), 13));
        quick.Children.Add(Text(T("取下的项目会保留在这里；也可以清空搜索查看全部。"), 12, margin: new Thickness(0, 18, 0, 16)));
        quick.Children.Add(Button(T("快捷键与启动"), () => Navigate(DashboardPage.Shortcuts)));
        AddDesktopFolder("QUICK.TXT", null, T("快捷键与启动"), quick, new Rect(920, 190, 224, 250));
        var collect = Stack();
        collect.Children.Add(new TextBlock { Text = "COLLECT.SYS", FontFamily = PixelFont, FontSize = 23, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 12) });
        collect.Children.Add(Text(T("收集剪贴板图片和文字（默认开启）"), 12));
        collect.Children.Add(Text(T("截图目录") + " / " + T("录像目录"), 12, margin: new Thickness(0, 10, 0, 16)));
        collect.Children.Add(Button(T("来源与设置"), () => Navigate(DashboardPage.Sources)));
        AddDesktopFolder("COLLECT.SYS", null, T("来源与设置"), collect, new Rect(910, 470, 224, 220));
        var inner = new Border { BorderBrush = Brush("SurfaceBrush"), BorderThickness = new Thickness(1), Child = root };
        return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("AccentBrush"), BorderThickness = new Thickness(2), Child = inner };
    }

    private FrameworkElement PixelFolder()
    {
        // Pixel coordinates from the supplied .icon-folder box-shadow artwork.
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var outline = Geometry.Parse("M2,4 L14,4 14,8 28,8 28,10 30,10 30,20 28,20 28,22 2,22 2,20 0,20 0,6 2,6 Z");
            dc.DrawGeometry(Brush("AccentBrush"), null, outline);
            dc.DrawRectangle(Brush("SurfaceBrush"), null, new Rect(2, 6, 10, 4));
            dc.DrawRectangle(Brush("SurfaceBrush"), null, new Rect(2, 10, 26, 10));
        }
        group.Freeze(); var image = new Image { Source = new DrawingImage(group), Width = 36, Height = 30, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); return image;
    }

    private DesktopFolder AddDesktopFolder(string id, DashboardPage? page, string label, FrameworkElement body, Rect bounds)
    {
        var frame = new Border { BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(2), Background = Brush("SurfaceBrush"), Width = bounds.Width, Height = bounds.Height, SnapsToDevicePixels = true, ClipToBounds = true };
        if (!SystemParameters.HighContrast) frame.Effect = new DropShadowEffect { Color = Color.FromRgb(94, 166, 229), BlurRadius = 0, Direction = 315, ShadowDepth = 4, Opacity = .3 };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) }); frame.Child = grid;
        var header = new Grid { Background = Brush("SurfaceBrush") }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = id, FontFamily = PixelFont, FontSize = 20, Margin = new Thickness(8, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        var handle = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Focusable = true };
        var surface = new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.BackgroundProperty, Brushes.Transparent); handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = surface };
        header.Children.Add(handle); header.Children.Add(caption);
        var task = Button(label, () => { }); task.MinHeight = 28; task.Padding = new Thickness(6, 2, 6, 2); task.FontSize = 12; task.MinWidth = 0; task.Margin = new Thickness(1, 0, 1, 0); task.ContentTemplate = TaskLabel; task.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var folder = new DesktopFolder { Id = id, Page = page, Frame = frame, Header = header, Handle = handle, Caption = caption, Task = task };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 3, 4, 3) };
        Button Chrome(string text, string name, Action action)
        {
            var b = Button(text, action); b.Width = 22; b.Height = 22; b.MinHeight = 0; b.Padding = new Thickness(0); b.Margin = new Thickness(2, 0, 0, 0); b.FontFamily = new FontFamily("Consolas"); b.FontSize = 12; b.Background = Brush("SurfaceBrush"); b.Foreground = Brush("TextBrush"); AutomationProperties.SetName(b, name + " " + label); return b;
        }
        controls.Children.Add(Chrome("_", T("最小化"), () => HideDesktopFolder(folder, false)));
        controls.Children.Add(Chrome("□", T("最大化"), () => ToggleDesktopMaximize(folder)));
        controls.Children.Add(Chrome("×", T("关闭"), () => HideDesktopFolder(folder, true)));
        Grid.SetColumn(controls, 1); header.Children.Add(controls); grid.Children.Add(header);
        var inner = new DockPanel { Margin = new Thickness(16, 14, 16, 8) };
        if (page is { } key)
        {
            var heading = Stack(); heading.Margin = new Thickness(0, 0, 0, 12);
            heading.Children.Add(Text(titles[(int)key], 22)); folder.Description.Text = PageDescription(key); folder.Description.FontSize = 12; folder.Description.TextWrapping = TextWrapping.Wrap; folder.Description.Margin = new Thickness(0, 5, 0, 0); heading.Children.Add(folder.Description);
            DockPanel.SetDock(heading, Dock.Top); inner.Children.Add(heading);
        }
        inner.Children.Add(body); Grid.SetRow(inner, 1); grid.Children.Add(inner);
        var statusLine = new Border { BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(7, 2, 7, 1), Child = Text(page is null ? "LOCAL / CORDNOTE" : T("拖动标题栏移动窗口"), 11) };
        Grid.SetRow(statusLine, 2); grid.Children.Add(statusLine);
        AutomationProperties.SetName(frame, label + " · " + id); AutomationProperties.SetName(handle, T("移动窗口") + " " + label);
        AutomationProperties.SetHelpText(handle, T("拖动标题栏移动窗口") + "; Arrow keys / Shift+Arrow");
        desktopFolders.Add(id, folder); desktopCanvas.Children.Add(frame); desktopTasks.Children.Add(task); RefreshTaskColumns();
        Canvas.SetLeft(frame, bounds.X); Canvas.SetTop(frame, bounds.Y); Panel.SetZIndex(frame, ++desktopZ);
        Point dragStart = default;
        handle.DragStarted += (_, _) => { ActivateDesktopFolder(folder); if (folder.Maximized) ToggleDesktopMaximize(folder); dragStart = new Point(Canvas.GetLeft(frame), Canvas.GetTop(frame)); };
        handle.DragDelta += (_, e) => MoveDesktopFolder(folder, Canvas.GetLeft(frame) + e.HorizontalChange, Canvas.GetTop(frame) + e.VerticalChange);
        handle.DragCompleted += (_, e) => { if (e.Canceled) MoveDesktopFolder(folder, dragStart.X, dragStart.Y); };
        handle.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && handle.IsDragging) { handle.CancelDrag(); e.Handled = true; return; }
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 24 : 8;
            var x = Canvas.GetLeft(frame); var y = Canvas.GetTop(frame);
            switch (e.Key) { case Key.Left: x -= delta; break; case Key.Right: x += delta; break; case Key.Up: y -= delta; break; case Key.Down: y += delta; break; default: return; }
            MoveDesktopFolder(folder, x, y); e.Handled = true;
        };
        frame.PreviewMouseDown += (_, _) => ActivateDesktopFolder(folder);
        frame.GotKeyboardFocus += (_, _) => ActivateDesktopFolder(folder);
        task.Click += (_, _) => { task.Visibility = Visibility.Visible; frame.Visibility = Visibility.Visible; ActivateDesktopFolder(folder); FitDesktopFolder(folder); };
        FitDesktopFolder(folder); return folder;
    }

    private void OpenDesktopFolder(DashboardPage page)
    {
        var id = desktopNames[(int)page];
        if (!desktopFolders.TryGetValue(id, out var folder))
            folder = AddDesktopFolder(id, page, titles[(int)page], pages[(int)page], new Rect(132 + (int)page * 22, 20 + (int)page * 18, 720, 470));
        folder.Frame.Visibility = Visibility.Visible; folder.Task.Visibility = Visibility.Visible; RefreshTaskColumns(); ActivateDesktopFolder(folder); FitDesktopFolder(folder);
    }

    private void ActivateDesktopFolder(DesktopFolder folder)
    {
        if (folder.Frame.Visibility != Visibility.Visible) return;
        activeDesktopFolder = folder; Panel.SetZIndex(folder.Frame, ++desktopZ);
        foreach (var item in desktopFolders.Values)
        {
            var active = item == folder;
            item.Header.Background = Brush(active ? "AccentBrush" : "SurfaceBrush"); item.Caption.Foreground = Brush(active ? "AccentTextBrush" : "TextBrush");
            item.Task.Background = Brush(active ? "SelectedBrush" : "SurfaceBrush");
        }
        if (folder.Page is { } page)
        { selectingDesktopFolder = true; navigation.SelectedIndex = (int)page; selectingDesktopFolder = false; }
        UpdateDesktopSaveVisibility();
    }

    private void HideDesktopFolder(DesktopFolder folder, bool close)
    {
        folder.Frame.Visibility = Visibility.Collapsed;
        // Utility windows remain in the taskbar so they can always be reopened.
        if (close && folder.Page is not null) folder.Task.Visibility = Visibility.Collapsed;
        RefreshTaskColumns();
        var next = desktopFolders.Values.Where(f => f.Frame.Visibility == Visibility.Visible).OrderByDescending(f => Panel.GetZIndex(f.Frame)).FirstOrDefault();
        activeDesktopFolder = null; if (next is not null) ActivateDesktopFolder(next);
        UpdateDesktopSaveVisibility();
    }
    private const int DesktopTaskSlots = 8;
    private GithubHoverCard? githubCard;
    private Canvas? githubOverlay;
    private Button? githubButton;
    private DispatcherTimer? githubHoverTimer;
    private long githubOutsideSince;
    private bool githubWasHidden;
    private bool githubSizeNudge;
    private void CreateGithubOverlay(Button github, Grid root)
    {
        githubButton = github;
        githubOverlay = new Canvas { Visibility = Visibility.Visible, IsHitTestVisible = false };
        Panel.SetZIndex(githubOverlay, -1);
        Grid.SetRowSpan(githubOverlay, 2); root.Children.Add(githubOverlay);
        githubCard = new GithubHoverCard();
        githubCard.ProfileRequested += OpenGithubProfile;
        githubCard.CardBoundsChanged += () => { if (githubOverlay?.IsHitTestVisible == true) ShowGithubCard(); };
        githubOverlay.Children.Add(githubCard);
        Canvas.SetLeft(githubCard, 0); Canvas.SetTop(githubCard, 0);
        github.MouseEnter += (_, _) => ShowGithubCard();
        githubHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        githubHoverTimer.Tick += (_, _) => CheckGithubHover();
        githubHoverTimer.Start();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) { githubWasHidden = true; CloseGithubCard(); }
            else if (githubWasHidden) { githubWasHidden = false; _ = githubCard?.ResumeAsync(); }
        };
        Closed += (_, _) => { githubHoverTimer.Stop(); CloseGithubCard(); };
    }
    private void ShowGithubCard()
    {
        if (githubOverlay is null || githubCard is null || githubButton is null) return;
        var anchor = githubButton.TranslatePoint(new Point(githubButton.ActualWidth / 2, 0), githubOverlay);
        var bounds = githubCard.CardBounds;
        var overlayWidth = githubOverlay.ActualWidth > 0 ? githubOverlay.ActualWidth : ActualWidth;
        double left, top;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            // WebView2 stops presenting frames while hidden behind WPF content; a 1px resize wakes it on each show.
            githubSizeNudge = !githubSizeNudge;
            var nudge = githubSizeNudge ? 1 : 0;
            var targetWidth = bounds.Width + 16 + nudge; var targetHeight = bounds.Height + 16 + nudge;
            if (Math.Abs(githubCard.Width - targetWidth) > .5) githubCard.Width = targetWidth;
            if (Math.Abs(githubCard.Height - targetHeight) > .5) githubCard.Height = targetHeight;
            left = anchor.X - (bounds.Left + bounds.Width / 2);
            top = anchor.Y - 16 - (bounds.Top + bounds.Height);
            var minLeft = 8 - bounds.Left; var maxLeft = overlayWidth - 8 - bounds.Left - bounds.Width;
            left = maxLeft >= minLeft ? Math.Clamp(left, minLeft, maxLeft) : minLeft;
            top = Math.Max(8 - bounds.Top, top);
        }
        else
        {
            left = Math.Max(8, anchor.X - githubCard.Width / 2);
            top = Math.Max(8, anchor.Y - githubCard.Height - 16);
        }
        Canvas.SetLeft(githubCard, left);
        Canvas.SetTop(githubCard, top);
        Panel.SetZIndex(githubOverlay, 10);
        githubOverlay.IsHitTestVisible = true;
        _ = githubCard.WakeAsync();
        githubOutsideSince = 0;
    }
    private void CheckGithubHover()
    {
        if (githubOverlay is null || githubCard is null || githubButton is null) { githubHoverTimer?.Stop(); return; }
        if (!IsVisible) { HideGithubCard(); return; }
        var overButton = new Rect(0, 0, githubButton.ActualWidth, githubButton.ActualHeight);
        overButton.Inflate(10, 10);
        var buttonHovered = overButton.Contains(Mouse.GetPosition(githubButton));
        if (!githubOverlay.IsHitTestVisible)
        {
            if (buttonHovered) ShowGithubCard();
            return;
        }
        var overCard = new Rect(0, 0, githubCard.ActualWidth, githubCard.ActualHeight);
        overCard.Inflate(10, 10);
        if (buttonHovered || overCard.Contains(Mouse.GetPosition(githubCard))) { githubOutsideSince = 0; return; }
        var now = Environment.TickCount64;
        if (githubOutsideSince == 0) { githubOutsideSince = now; return; }
        if (now - githubOutsideSince >= 220) HideGithubCard();
    }
    private void HideGithubCard() { githubOutsideSince = 0; if (githubOverlay is null || githubCard is null) return; githubOverlay.IsHitTestVisible = false; Panel.SetZIndex(githubOverlay, -1); }
    private void CloseGithubCard() { githubHoverTimer?.Stop(); HideGithubCard(); }
    private void OpenGithubProfile()
    {
        CloseGithubCard();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/jiezi1234") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    private void RefreshTaskColumns() => desktopTasks.Columns = DesktopTaskSlots;

    private void UpdateDesktopSaveVisibility() => saveBar.Visibility = Visibility.Visible;

    private void MoveDesktopFolder(DesktopFolder folder, double x, double y)
    {
        var width = desktopCanvas.ActualWidth > 0 ? desktopCanvas.ActualWidth : Width - 16;
        var height = desktopCanvas.ActualHeight > 0 ? desktopCanvas.ActualHeight : Height - 240;
        Canvas.SetLeft(folder.Frame, Math.Clamp(x, 0, Math.Max(0, width - folder.Frame.Width)));
        Canvas.SetTop(folder.Frame, Math.Clamp(y, 0, Math.Max(0, height - folder.Frame.Height)));
    }

    private void FitDesktopFolder(DesktopFolder folder)
    {
        if (desktopCanvas.ActualWidth <= 0 || desktopCanvas.ActualHeight <= 0) return;
        if (folder.Maximized) { folder.Frame.Width = desktopCanvas.ActualWidth; folder.Frame.Height = desktopCanvas.ActualHeight; Canvas.SetLeft(folder.Frame, 0); Canvas.SetTop(folder.Frame, 0); return; }
        folder.Frame.Width = Math.Min(folder.Frame.Width, desktopCanvas.ActualWidth);
        folder.Frame.Height = Math.Min(folder.Frame.Height, desktopCanvas.ActualHeight);
        MoveDesktopFolder(folder, Canvas.GetLeft(folder.Frame), Canvas.GetTop(folder.Frame));
    }

    private void FitDesktopFolders() { foreach (var folder in desktopFolders.Values) FitDesktopFolder(folder); }

    private void ToggleDesktopMaximize(DesktopFolder folder)
    {
        if (folder.Maximized)
        { folder.Maximized = false; folder.Frame.Width = folder.RestoreBounds.Width; folder.Frame.Height = folder.RestoreBounds.Height; Canvas.SetLeft(folder.Frame, folder.RestoreBounds.X); Canvas.SetTop(folder.Frame, folder.RestoreBounds.Y); }
        else { folder.RestoreBounds = new Rect(Canvas.GetLeft(folder.Frame), Canvas.GetTop(folder.Frame), folder.Frame.Width, folder.Frame.Height); folder.Maximized = true; }
        ActivateDesktopFolder(folder); FitDesktopFolder(folder);
    }

    internal FrameworkElement? ActiveDesktopContent => activeDesktopFolder?.Page is { } page ? pages[(int)page] : null;
    internal bool GithubCardOpen => githubOverlay?.IsHitTestVisible == true;
    internal Task GithubCardReady => githubCard?.Ready ?? Task.CompletedTask;
    internal string GithubCardState => githubCard?.State ?? "none";
    internal Rect GithubCardBounds => githubCard?.CardBounds ?? Rect.Empty;
    internal Point GithubCardPosition => githubCard is null ? default : new Point(Canvas.GetLeft(githubCard), Canvas.GetTop(githubCard));
    internal Point GithubButtonCenter => githubButton is null || githubOverlay is null ? default : githubButton.TranslatePoint(new Point(githubButton.ActualWidth / 2, 0), githubOverlay);
    internal void CloseGithubCardForDiagnostics() => CloseGithubCard();
    internal void ShowGithubCardForDiagnostics() { ShowGithubCard(); githubHoverTimer?.Stop(); }
}
