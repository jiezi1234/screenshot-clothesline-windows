using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static Clothesline.App.ImageHoverTheme;

namespace Clothesline.App;

internal sealed record WorkspaceImage(string Id, string Path, string Title, string Hash = "");
internal sealed record OriginalReference(string Id, string Path, string Hash);
internal sealed record EditSaveRequest(BitmapSource Image, BitmapSource SessionOriginal, string SourcePath, OriginalReference? Original);
internal sealed record SavedImage(string Path, bool Hung, OriginalReference? Original = null, string Warning = "");

internal sealed class ImageWorkspace : Window
{
    private readonly WorkspaceImage[] gallery;
    private readonly Func<EditSaveRequest, Window, Task<SavedImage?>> save;
    private readonly Func<WorkspaceImage, string, OriginalReference?> resolveOriginal;
    private readonly Func<string, IDisposable> acquireLease;
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ImageSurface canvas = new();
    private readonly TextBlock heading = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock dirtyLabel = new() { Text = T("未保存修改"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock dimensions = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock galleryPosition = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 42, TextAlignment = TextAlignment.Center, FontSize = 11 };
    private readonly Button zoomButton, saveButton, previousButton, nextButton, undoButton, redoButton, moreButton;
    private readonly MenuItem resetItem, restoreItem;
    private readonly DockPanel editBar = new();
    private readonly StackPanel options = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel palette = new() { Orientation = Orientation.Horizontal };
    private readonly Grid annotationField = new() { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider strokeSize = new() { Minimum = 1, Maximum = 24, Value = 4, Width = 80, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sizeValue = new() { Width = 39, FontSize = 11, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox annotation = new() { Width = 148, MaxLength = 300, Padding = new Thickness(6, 3, 6, 3), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<EditorTool, ToggleButton> tools = new();
    private readonly Dictionary<Color, ToggleButton> swatches = new();
    private IDisposable? lease;
    private IDisposable? originalLease;
    private OriginalReference? originalReference;
    private int index, loadVersion;
    private bool loading, saving, closed, fitMode = true;
    private double scale = 1;
    private Point? panStart;
    private Point panOffset;
    private ImageDocument? document;
    private readonly CancellationTokenSource closing = new();
    internal ImageDocument? Document => document;
    internal bool Loading => loading;
    internal double PhysicalMagnification => scale;
    internal double CanvasScale => canvas.LayoutTransform.Value.M11;

    public ImageWorkspace(WorkspaceImage[] gallery, int start, bool edit,
        Func<EditSaveRequest, Window, Task<SavedImage?>> save, Func<string, IDisposable> acquireLease,
        Func<WorkspaceImage, string, OriginalReference?>? resolveOriginal = null)
    {
        this.gallery = gallery; index = start; this.save = save; this.acquireLease = acquireLease;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Clothesline.Core.UiText.Language);
        this.resolveOriginal = resolveOriginal ?? ((_, _) => null);
        Title = T("图片工作台 · 绳笺 Cordnote"); Width = 1040; Height = 720; MinWidth = 740; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources = ImageHoverTheme.Create(neutral: true);
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        viewport.SetResourceReference(Control.BackgroundProperty, "SidebarBrush");
        foreach (var label in new[] { dirtyLabel, status, dimensions, galleryPosition, sizeValue })
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"); UseLayoutRounding = true;

        var root = new DockPanel { Margin = new Thickness(9, 6, 9, 6) }; Content = root;
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 5) };
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        commands.Children.Add(undoButton = Button(T("撤销"), () => document?.Undo(), "undo", "Ctrl+Z"));
        commands.Children.Add(redoButton = Button(T("重做"), () => document?.Redo(), "redo", "Ctrl+Y"));
        resetItem = new MenuItem { Header = T("重置本次修改") };
        resetItem.Click += (_, _) => { canvas.Cancel(); document?.Reset(); };
        restoreItem = new MenuItem { Header = T("恢复最初原图") };
        restoreItem.Click += async (_, _) => await RestoreOriginal();
        var more = new ContextMenu { Placement = PlacementMode.Bottom, Items = { resetItem, restoreItem } };
        moreButton = Button(T("更多操作"), () => more.IsOpen = true, "more");
        moreButton.ContextMenu = more; more.PlacementTarget = moreButton;
        commands.Children.Add(moreButton);

        saveButton = Button(T("另存为"), () => _ = Save(), shortcut: "Ctrl+S");
        saveButton.ToolTip = T("另存副本并挂上…") + " · Ctrl+S";
        saveButton.Margin = new Thickness(7, 0, 0, 0);
        saveButton.Padding = new Thickness(10, 2, 10, 2);
        saveButton.FontWeight = FontWeights.SemiBold;
        saveButton.SetResourceReference(Control.BackgroundProperty, "SelectedBrush");
        saveButton.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        commands.Children.Add(saveButton);
        DockPanel.SetDock(commands, Dock.Right); header.Children.Add(commands);
        DockPanel.SetDock(dirtyLabel, Dock.Right); header.Children.Add(dirtyLabel);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var toolRail = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, label, symbol) in new[] { (EditorTool.Pan, T("浏览"), "pan"), (EditorTool.Pen, T("画笔"), "pen"), (EditorTool.Arrow, T("箭头"), "arrow"), (EditorTool.Rectangle, T("矩形"), "rectangle"), (EditorTool.Text, T("文字"), "text"), (EditorTool.Mosaic, T("马赛克"), "mosaic"), (EditorTool.Crop, T("裁剪"), "crop") })
        {
            var button = new ToggleButton { Content = EditorIcon(symbol), Width = 30, Height = 29, ToolTip = label };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => SelectTool(tool);
            tools.Add(tool, button); toolRail.Children.Add(button);
        }
        DockPanel.SetDock(toolRail, Dock.Left); editBar.Children.Add(toolRail);
        var divider = new Border { Width = 1, Height = 17, Margin = new Thickness(7, 0, 7, 0) };
        divider.SetResourceReference(Border.BackgroundProperty, "LineBrush"); options.Children.Add(divider);
        foreach (var (color, name) in new[] { (Colors.Crimson, T("红色")), (Colors.Orange, T("橙色")), (Colors.ForestGreen, T("绿色")), (Colors.DodgerBlue, T("蓝色")), (Colors.Black, T("黑色")), (Colors.White, T("白色")) })
        {
            var choice = new ToggleButton
            {
                Style = (Style)Resources["ColorSwatch"], ToolTip = name, Width = 23, Height = 29,
                Content = new Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(color), Stroke = DemoAssets.Brush("#55000000"), StrokeThickness = 1 }
            };
            AutomationProperties.SetName(choice, name); choice.Click += (_, _) => SelectInk(color);
            swatches.Add(color, choice); palette.Children.Add(choice);
        }
        SelectInk(canvas.Ink);
        options.Children.Add(palette); options.Children.Add(strokeSize); options.Children.Add(sizeValue);
        AutomationProperties.SetName(annotation, T("标注文字")); annotation.ToolTip = T("在此输入文字");
        var watermark = new TextBlock { Text = T("在此输入文字"), FontSize = 12, Margin = new Thickness(7, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
        watermark.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        annotation.TextChanged += (_, _) =>
        {
            canvas.Annotation = annotation.Text;
            watermark.Visibility = annotation.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        strokeSize.ValueChanged += (_, _) => { canvas.StrokeSize = strokeSize.Value; UpdateToolSize(); };
        annotationField.Children.Add(annotation); annotationField.Children.Add(watermark);
        options.Children.Add(annotationField); editBar.Children.Add(options);
        var toolbar = new Border { Child = editBar, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        toolbar.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);

        var footer = new DockPanel { Margin = new Thickness(1, 4, 1, 0) };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        navigation.Children.Add(previousButton = Button(T("← 上一张"), () => _ = Navigate(-1), "previous"));
        navigation.Children.Add(galleryPosition);
        navigation.Children.Add(nextButton = Button(T("下一张 →"), () => _ = Navigate(1), "next"));
        if (gallery.Length == 1)
            previousButton.Visibility = nextButton.Visibility = galleryPosition.Visibility = Visibility.Collapsed;
        navigation.Children.Add(dimensions);
        DockPanel.SetDock(navigation, Dock.Left); footer.Children.Add(navigation);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        zoom.Children.Add(Button(T("适应窗口"), Fit, "fit", "0"));
        zoom.Children.Add(Button(T("缩小"), () => Zoom(scale / 1.25), "minus"));
        zoomButton = Button("100%", () => Zoom(1), shortcut: "1"); zoomButton.MinWidth = 52;
        AutomationProperties.SetName(zoomButton, T("缩放比例") + " · 100%");
        zoom.Children.Add(zoomButton);
        zoom.Children.Add(Button(T("放大"), () => Zoom(scale * 1.25), "plus"));
        var shortcuts = T("Ctrl + 滚轮缩放 · 空格拖动 · ← → 切图 · Ctrl + Z / Y 撤销重做 · Ctrl + S 另存 · Esc 关闭");
        var help = Button(T("快捷键"), () => MessageBox.Show(this, shortcuts, T("快捷键")), "help");
        help.ToolTip = new TextBlock { Text = shortcuts, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        AutomationProperties.SetHelpText(help, shortcuts); zoom.Children.Add(help);
        DockPanel.SetDock(zoom, Dock.Right); footer.Children.Add(zoom);
        status.Margin = new Thickness(10, 0, 10, 0);
        status.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = status });
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        canvas.Focusable = true; AutomationProperties.SetName(canvas, T("图片画布"));
        AutomationProperties.SetHelpText(canvas, T("Alt+C 聚焦画布。方向键移动光标，Shift 加速，Enter 设置起点或完成标注，Esc 取消。"));
        canvas.KeyboardPositionChanged += text => status.Text = text;
        canvas.HorizontalAlignment = HorizontalAlignment.Center; canvas.VerticalAlignment = VerticalAlignment.Center; viewport.Content = canvas; root.Children.Add(viewport);
        canvas.Completed += () => { if (canvas.Tool == EditorTool.Crop) Fit(); };
        canvas.Failed += message => status.Text = message;
        viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; Zoom(scale * (e.Delta > 0 ? 1.25 : .8)); e.Handled = true; };
        viewport.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (document is null || saving || loading || (canvas.Tool != EditorTool.Pan && !Keyboard.IsKeyDown(Key.Space))) return;
            panStart = e.GetPosition(viewport); panOffset = new Point(viewport.HorizontalOffset, viewport.VerticalOffset); viewport.CaptureMouse(); e.Handled = true;
        };
        viewport.PreviewMouseMove += (_, e) => { if (panStart is not Point start) return; var delta = e.GetPosition(viewport) - start; viewport.ScrollToHorizontalOffset(panOffset.X - delta.X); viewport.ScrollToVerticalOffset(panOffset.Y - delta.Y); e.Handled = true; };
        viewport.PreviewMouseLeftButtonUp += (_, e) => { if (panStart is null) return; panStart = null; viewport.ReleaseMouseCapture(); e.Handled = true; };
        viewport.LostMouseCapture += (_, _) => panStart = null;
        viewport.SizeChanged += (_, _) => { if (fitMode) Fit(); };
        PreviewKeyDown += KeyPressed;
        Closing += ClosingWorkspace;
        Closed += (_, _) => { closed = true; closing.Cancel(); loadVersion++; lease?.Dispose(); originalLease?.Dispose(); if (document is not null) document.Changed -= DocumentChanged; };
        heading.Text = gallery[index].Title; heading.ToolTip = gallery[index].Title;
        Loaded += async (_, _) => await Load(index);
        SelectTool(edit ? EditorTool.Pen : EditorTool.Pan); UpdateActions();
    }
    private Button Button(string text, Action action, string? symbol = null, string? shortcut = null)
    {
        var button = new Button
        {
            Content = symbol is null ? text : EditorIcon(symbol),
            Width = symbol is null ? double.NaN : 28, Height = 28, Padding = new Thickness(4),
            ToolTip = shortcut is null ? text : text + " · " + shortcut
        };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => { try { canvas.Cancel(); action(); } catch (Exception ex) { status.Text = ex.Message; } };
        return button;
    }
    private void SelectTool(EditorTool tool)
    {
        if (canvas.Processing)
        {
            tools[tool].IsChecked = tool == canvas.Tool;
            return;
        }
        canvas.Cancel(); canvas.Tool = tool;
        foreach (var item in tools) item.Value.IsChecked = item.Key == tool;
        canvas.Cursor = tool == EditorTool.Pan ? Cursors.Hand : Cursors.Cross;
        options.Visibility = tool is EditorTool.Pan or EditorTool.Crop ? Visibility.Collapsed : Visibility.Visible;
        palette.Visibility = tool == EditorTool.Mosaic ? Visibility.Collapsed : Visibility.Visible;
        annotationField.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        var sizeName = tool switch { EditorTool.Text => T("字号"), EditorTool.Mosaic => T("马赛克大小"), _ => T("画笔粗细") };
        AutomationProperties.SetName(strokeSize, sizeName); strokeSize.ToolTip = sizeName;
        UpdateToolSize();
    }
    private void SelectInk(Color color)
    {
        canvas.Ink = color;
        foreach (var swatch in swatches) swatch.Value.IsChecked = swatch.Key == color;
    }
    private void UpdateToolSize()
    {
        var size = canvas.Tool switch
        {
            EditorTool.Text => Math.Max(14, strokeSize.Value * 4),
            EditorTool.Mosaic => Math.Max(12, strokeSize.Value * 4),
            _ => strokeSize.Value
        };
        sizeValue.Text = $"{size:0} px";
    }
    private bool DiscardAllowed() => document?.IsDirty != true || MessageBox.Show(this, T("放弃本次尚未保存的修改？"), T("未保存修改"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void ClosingWorkspace(object? sender, CancelEventArgs e)
    {
        if (canvas.Processing) { e.Cancel = true; return; }
        if (saving) { status.Text = T("正在保存副本，请完成后再关闭。"); e.Cancel = true; return; }
        canvas.Cancel(); if (!DiscardAllowed()) e.Cancel = true;
    }
    private async Task Navigate(int direction)
    {
        var next = index + direction;
        if (loading || saving || canvas.Processing || next < 0 || next >= gallery.Length || !DiscardAllowed()) return;
        await Load(next);
    }
    private async Task Load(int next)
    {
        var version = ++loadVersion; loading = true; UpdateActions(); status.Text = T("正在读取图片…");
        IDisposable? candidate = null;
        try
        {
            candidate = acquireLease(gallery[next].Path);
            var bitmap = await ImageDecoder.Read(gallery[next].Path, cancellation: closing.Token);
            var loaded = (Image: bitmap, Hash: await Task.Run(() => ImageFiles.Hash(bitmap), closing.Token));
            if (closed || version != loadVersion) return;
            var reference = resolveOriginal(gallery[next], loaded.Hash);
            if (document is not null) document.Changed -= DocumentChanged;
            lease?.Dispose(); lease = candidate; candidate = null; index = next;
            SetOriginal(reference);
            document = new ImageDocument(loaded.Image); document.Changed += DocumentChanged; canvas.Document = document; DocumentChanged(); Fit();
            saveButton.ToolTip = T("另存副本并挂上…") + " · Ctrl+S\n" + (reference is null
                ? T("另存时保留本次打开图片的原版快照。旧版副本无法追溯更早的原图。")
                : T("已关联最初原图，可恢复后另存；当前文件不会被覆盖。"));
            status.Text = "";
        }
        catch (Exception ex) { if (!closed) status.Text = T("无法读取图片：") + ex.Message; }
        finally { candidate?.Dispose(); if (!closed && version == loadVersion) { loading = false; UpdateActions(); } }
    }
    private void DocumentChanged()
    {
        if (document is null) return;
        var crop = document.State.Crop; canvas.Width = crop.Width; canvas.Height = crop.Height; canvas.InvalidateVisual();
        heading.Text = gallery[index].Title; heading.ToolTip = gallery[index].Title;
        galleryPosition.Text = $"{index + 1} / {gallery.Length}";
        dimensions.Text = $"{crop.Width} × {crop.Height}";
        dirtyLabel.Visibility = document.IsDirty ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions(); if (fitMode) Fit();
    }
    private void UpdateActions()
    {
        var enabled = document is not null && !loading && !saving;
        canvas.IsEnabled = enabled; editBar.IsEnabled = enabled; saveButton.IsEnabled = enabled;
        moreButton.IsEnabled = resetItem.IsEnabled = enabled;
        restoreItem.IsEnabled = enabled && originalReference is not null;
        undoButton.IsEnabled = enabled && document!.CanUndo; redoButton.IsEnabled = enabled && document!.CanRedo;
        previousButton.IsEnabled = !loading && !saving && index > 0; nextButton.IsEnabled = !loading && !saving && index < gallery.Length - 1;
    }
    private void Fit()
    {
        fitMode = true; if (document is null || viewport.ActualWidth < 1 || viewport.ActualHeight < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetScale(Math.Min(1, Math.Min(Math.Max(1, viewport.ActualWidth - 24) * dpi / document.State.Crop.Width, Math.Max(1, viewport.ActualHeight - 24) * dpi / document.State.Crop.Height)));
    }
    private void Zoom(double value)
    {
        if (document is null) return;
        canvas.Cancel(); fitMode = false;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; var old = scale / dpi;
        var x = (viewport.HorizontalOffset + viewport.ViewportWidth / 2) / old; var y = (viewport.VerticalOffset + viewport.ViewportHeight / 2) / old;
        SetScale(value); viewport.UpdateLayout(); viewport.ScrollToHorizontalOffset(x * scale / dpi - viewport.ViewportWidth / 2); viewport.ScrollToVerticalOffset(y * scale / dpi - viewport.ViewportHeight / 2);
    }
    private void SetScale(double value)
    {
        scale = Math.Clamp(value, .01, 8); var logical = scale / VisualTreeHelper.GetDpi(this).DpiScaleX;
        canvas.LayoutTransform = new ScaleTransform(logical, logical);
        zoomButton.Content = $"{scale:P0}";
        AutomationProperties.SetName(zoomButton, T("缩放比例") + " · " + zoomButton.Content);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi); if (document is null) return;
        canvas.Cancel(); if (fitMode) Fit(); else SetScale(scale);
    }
    private void SetOriginal(OriginalReference? reference)
    {
        var nextLease = reference is null ? null : acquireLease(reference.Path);
        originalLease?.Dispose(); originalLease = nextLease; originalReference = reference;
    }
    internal async Task RestoreOriginal()
    {
        if (document is null || originalReference is null || saving || loading) return;
        canvas.Cancel(); loading = true; UpdateActions(); status.Text = T("正在读取最初原图…");
        try
        {
            var reference = originalReference;
            var bitmap = await ImageDecoder.Read(reference.Path, cancellation: closing.Token);
            if (await Task.Run(() => ImageFiles.Hash(bitmap), closing.Token) != reference.Hash) throw new InvalidOperationException(T("原版快照内容已改变，无法可靠恢复。"));
            if (closed) return;
            document.RestoreOriginal(bitmap); Fit(); status.Text = T("已恢复最初原图，可撤销；另存后生成新副本。");
        }
        catch (Exception ex) { if (!closed) status.Text = T("恢复未完成，当前图片保留：") + ex.Message; }
        finally { loading = false; if (!closed) UpdateActions(); }
    }
    private async Task Save()
    {
        if (document is null || saving || loading || canvas.Processing) return;
        canvas.Cancel(); saving = true; UpdateActions();
        try
        {
            var rendered = await document.RenderAsync();
            var result = await save(new(rendered, document.Original, gallery[index].Path, originalReference), this);
            if (result is not null)
            {
                SetOriginal(result.Original); document.MarkSaved();
                status.Text = (result.Hung ? T("副本已保存并挂上：") : T("副本已保存，未能挂上；可手动导入：")) + result.Path + (result.Warning.Length > 0 ? "\n" + result.Warning : "");
            }
        }
        catch (Exception ex) { status.Text = T("保存未完成，修改仍保留在窗口中：") + ex.Message; }
        finally { saving = false; UpdateActions(); }
    }
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.SystemKey == Key.C && Keyboard.Modifiers == ModifierKeys.Alt) { canvas.Focus(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { if (!canvas.Cancel()) Close(); e.Handled = true; return; }
        if (Keyboard.FocusedElement == canvas && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter) return;
        var textInput = Keyboard.FocusedElement is TextBoxBase;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.S) { _ = Save(); e.Handled = true; }
            else if (!textInput && !saving && !loading && e.Key is Key.Z or Key.Y) { canvas.Cancel(); if (e.Key == Key.Z) document?.Undo(); else document?.Redo(); e.Handled = true; }
            return;
        }
        if (textInput || Keyboard.FocusedElement is RangeBase || canvas.Drawing || saving || loading) return;
        switch (e.Key)
        {
            case Key.Left: _ = Navigate(-1); break;
            case Key.Right: _ = Navigate(1); break;
            case Key.Add: case Key.OemPlus: Zoom(scale * 1.25); break;
            case Key.Subtract: case Key.OemMinus: Zoom(scale / 1.25); break;
            case Key.D0: Fit(); break;
            case Key.D1: Zoom(1); break;
            default: return;
        }
        e.Handled = true;
    }
}

internal sealed class ImageSurface : FrameworkElement
{
    public ImageDocument? Document { get; set; }
    public EditorTool Tool { get; set; }
    public Color Ink { get; set; } = Colors.Crimson;
    public double StrokeSize { get; set; } = 4;
    public string Annotation { get; set; } = "";
    private readonly List<Point> points = new();
    private Point keyboardPoint;
    private bool keyboardDrawing, processing;
    internal bool Processing => processing;
    public event Action<string>? KeyboardPositionChanged;
    public bool Drawing => points.Count > 0;
    public event Action? Completed;
    public event Action<string>? Failed;
    public bool Cancel() { var active = Drawing; keyboardDrawing = false; points.Clear(); if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); return active; }
    protected override AutomationPeer OnCreateAutomationPeer() => new ImageSurfacePeer(this);
    private sealed class ImageSurfacePeer(ImageSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ImageSurface);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (Document is not null) keyboardPoint = Document.Clamp(keyboardPoint);
        AnnouncePosition(); InvalidateVisual();
    }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); if (keyboardDrawing) Cancel(); InvalidateVisual(); }
    private void AnnouncePosition() => KeyboardPositionChanged?.Invoke(T("画布位置：X {0}，Y {1}。Enter 开始或完成，Esc 取消。", (int)keyboardPoint.X, (int)keyboardPoint.Y));
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Document is null || processing) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 20 : 1;
        var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => new Vector() };
        if (delta.Length > 0)
        {
            keyboardPoint = Document.Clamp(keyboardPoint + delta);
            BringIntoView(new Rect(keyboardPoint.X - Document.State.Crop.X, keyboardPoint.Y - Document.State.Crop.Y, 1, 1));
            if (keyboardDrawing) { if (Tool == EditorTool.Pen && points.Count < 10000) points.Add(keyboardPoint); else if (points.Count == 1) points.Add(keyboardPoint); else points[^1] = keyboardPoint; }
            AnnouncePosition(); InvalidateVisual(); e.Handled = true;
        }
        else if (e.Key == Key.Enter && Tool != EditorTool.Pan)
        {
            e.Handled = true;
            if (Tool == EditorTool.Text) { Document.Add(Document.CreateMark(Tool, [keyboardPoint], Ink, StrokeSize, Annotation)); Completed?.Invoke(); }
            else if (!keyboardDrawing) { points.Clear(); points.Add(keyboardPoint); keyboardDrawing = true; InvalidateVisual(); AnnouncePosition(); }
            else { if (points.Count == 1) points.Add(keyboardPoint); await Complete(); }
        }
    }
    private async System.Threading.Tasks.Task Complete()
    {
        if (Document is null || processing || points.Count == 0) return;
        var document = Document; var state = document.State; var selected = points.ToArray(); var tool = Tool;
        Cancel(); processing = true;
        try
        {
            if (tool == EditorTool.Crop) document.Crop(new Rect(selected[0], selected[^1]));
            else
            {
                var mark = tool == EditorTool.Mosaic ? await document.CreateMarkAsync(tool, selected, Ink, StrokeSize, Annotation) : document.CreateMark(tool, selected, Ink, StrokeSize, Annotation);
                if (Document == document && ReferenceEquals(document.State, state)) document.Add(mark);
            }
        }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
        finally { processing = false; Completed?.Invoke(); }
    }
    private Point Position(MouseEventArgs e) { var p = e.GetPosition(this); return Document!.Clamp(new Point(p.X + Document.State.Crop.X, p.Y + Document.State.Crop.Y)); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (Document is null || processing || Tool == EditorTool.Pan) return;
        try
        {
            var p = Position(e);
            if (Tool == EditorTool.Text) { Document.Add(Document.CreateMark(Tool, [p], Ink, StrokeSize, Annotation)); Completed?.Invoke(); }
            else { points.Clear(); points.Add(p); CaptureMouse(); InvalidateVisual(); }
        }
        catch (Exception ex) { Failed?.Invoke(ex.Message); Cancel(); }
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!Drawing || Document is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Cancel(); return; }
        var p = Position(e);
        if (Tool == EditorTool.Pen) { if (points.Count < 10000 && (p - points[^1]).Length >= .5) points.Add(p); }
        else if (points.Count == 1) points.Add(p); else points[^1] = p;
        InvalidateVisual(); e.Handled = true;
    }
    protected override async void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (!Drawing || Document is null) return;
        var start = points[0]; var end = Position(e);
        if (Tool != EditorTool.Pen) { if (points.Count == 1) points.Add(end); else points[^1] = end; }
        await Complete();
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); points.Clear(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight)); if (Document is null) return;
        var preview = Drawing && Tool is not EditorTool.Crop and not EditorTool.Mosaic ? Document.CreateMark(Tool, points, Ink, StrokeSize, Annotation) : null;
        Document.Draw(dc, preview);
        if (IsKeyboardFocused)
        {
            var point = keyboardPoint - new Vector(Document.State.Crop.X, Document.State.Crop.Y);
            var pen = new Pen(Brushes.DodgerBlue, 2);
            dc.DrawLine(pen, point - new Vector(8, 0), point + new Vector(8, 0)); dc.DrawLine(pen, point - new Vector(0, 8), point + new Vector(0, 8));
        }
        if (Drawing && Tool is EditorTool.Crop or EditorTool.Mosaic)
        {
            var crop = Document.State.Crop; var rect = new Rect(points[0], points[^1]); rect.Offset(-crop.X, -crop.Y);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 40, 80, 50)), new Pen(Brushes.ForestGreen, 2) { DashStyle = DashStyles.Dash }, rect);
        }
    }
}
