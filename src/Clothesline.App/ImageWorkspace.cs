using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = DemoAssets.Brush("#DDE3D9") };
    private readonly ImageSurface canvas = new();
    private readonly TextBlock heading = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 36, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 55 };
    private readonly TextBox annotation = new() { Width = 200, MaxLength = 300, Text = T("在此输入文字"), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 4) };
    private readonly Dictionary<EditorTool, ToggleButton> tools = new();
    private readonly Button undoButton, redoButton, resetButton, restoreButton, saveButton, previousButton, nextButton;
    private readonly WrapPanel editBar = new();
    private IDisposable? lease;
    private IDisposable? originalLease;
    private OriginalReference? originalReference;
    private int index, loadVersion;
    private bool loading, saving, closed, fitMode = true;
    private double scale = 1;
    private Point? panStart;
    private Point panOffset;
    private ImageDocument? document;
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
        Title = T("图片工作台 · Screenshot Clothesline"); Width = 1100; Height = 800; MinWidth = 820; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = DemoAssets.Brush("#F4F5EE");
        Foreground = DemoAssets.Brush("#294535"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(heading);
        var navigation = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) }; header.Children.Add(navigation);
        navigation.Children.Add(previousButton = Button(T("← 上一张"), () => _ = Navigate(-1)));
        navigation.Children.Add(nextButton = Button(T("下一张 →"), () => _ = Navigate(1)));
        navigation.Children.Add(Button(T("适应窗口"), Fit)); navigation.Children.Add(Button("100%", () => Zoom(1)));
        var zoomOut = Button("−", () => Zoom(scale / 1.25)); AutomationProperties.SetName(zoomOut, T("缩小")); zoomOut.ToolTip = T("缩小"); navigation.Children.Add(zoomOut);
        var zoomIn = Button("+", () => Zoom(scale * 1.25)); AutomationProperties.SetName(zoomIn, T("放大")); zoomIn.ToolTip = T("放大"); navigation.Children.Add(zoomIn); navigation.Children.Add(zoomLabel);
        navigation.Children.Add(undoButton = Button(T("撤销"), () => document?.Undo())); navigation.Children.Add(redoButton = Button(T("重做"), () => document?.Redo()));
        navigation.Children.Add(resetButton = Button(T("重置本次修改"), () => document?.Reset()));
        navigation.Children.Add(restoreButton = Button(T("恢复最初原图"), () => _ = RestoreOriginal()));
        navigation.Children.Add(saveButton = Button(T("另存副本并挂上…"), () => _ = Save()));
        header.Children.Add(editBar);
        foreach (var (tool, label) in new[] { (EditorTool.Pan, T("浏览")), (EditorTool.Pen, T("画笔")), (EditorTool.Arrow, T("箭头")), (EditorTool.Rectangle, T("矩形")), (EditorTool.Text, T("文字")), (EditorTool.Mosaic, T("马赛克")), (EditorTool.Crop, T("裁剪")) })
        {
            var button = new ToggleButton { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 5, 5) };
            button.Click += (_, _) => SelectTool(tool); tools[tool] = button; editBar.Children.Add(button);
        }
        var options = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) }; header.Children.Add(options);
        foreach (var (color, name) in new[] { (Colors.Crimson, T("红色")), (Colors.Orange, T("橙色")), (Colors.ForestGreen, T("绿色")), (Colors.DodgerBlue, T("蓝色")), (Colors.Black, T("黑色")), (Colors.White, T("白色")) })
        {
            var choice = Button("●", () => { canvas.Ink = color; }); choice.Foreground = new SolidColorBrush(color); choice.Width = 30; choice.Padding = new Thickness(2); choice.ToolTip = name; AutomationProperties.SetName(choice, name); options.Children.Add(choice);
        }
        options.Children.Add(new TextBlock { Text = T("粗细"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 4) });
        var size = new Slider { Minimum = 1, Maximum = 24, Value = 4, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(size, T("画笔粗细")); AutomationProperties.SetName(annotation, T("标注文字"));
        size.ValueChanged += (_, _) => canvas.StrokeSize = size.Value; options.Children.Add(size); options.Children.Add(annotation);
        annotation.TextChanged += (_, _) => canvas.Annotation = annotation.Text; canvas.Annotation = annotation.Text;
        header.Children.Add(new TextBlock { Text = T("Ctrl + 滚轮缩放 · 空格拖动 · ← → 切图 · Ctrl + Z / Y 撤销重做 · Ctrl + S 另存 · Esc 关闭"), FontSize = 11, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
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
        Closed += (_, _) => { closed = true; loadVersion++; lease?.Dispose(); originalLease?.Dispose(); if (document is not null) document.Changed -= DocumentChanged; };
        Loaded += async (_, _) => await Load(index);
        SelectTool(edit ? EditorTool.Pen : EditorTool.Pan); UpdateActions();
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 4), Background = DemoAssets.Brush("#E2E9DD"), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => { try { canvas.Cancel(); action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button;
    }
    private void SelectTool(EditorTool tool)
    {
        canvas.Cancel(); canvas.Tool = tool; foreach (var item in tools) item.Value.IsChecked = item.Key == tool;
        canvas.Cursor = tool == EditorTool.Pan ? Cursors.Hand : Cursors.Cross; annotation.IsEnabled = tool == EditorTool.Text;
    }
    private bool DiscardAllowed() => document?.IsDirty != true || MessageBox.Show(this, T("放弃本次尚未保存的修改？"), T("未保存修改"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void ClosingWorkspace(object? sender, CancelEventArgs e)
    {
        if (saving) { status.Text = T("正在保存副本，请完成后再关闭。"); e.Cancel = true; return; }
        canvas.Cancel(); if (!DiscardAllowed()) e.Cancel = true;
    }
    private async Task Navigate(int direction)
    {
        var next = index + direction;
        if (loading || saving || next < 0 || next >= gallery.Length || !DiscardAllowed()) return;
        await Load(next);
    }
    private async Task Load(int next)
    {
        var version = ++loadVersion; loading = true; UpdateActions(); status.Text = T("正在读取图片…");
        IDisposable? candidate = null;
        try
        {
            candidate = acquireLease(gallery[next].Path);
            var loaded = await Task.Run(() => { var image = ImageFiles.Read(gallery[next].Path); return (Image: image, Hash: ImageFiles.Hash(image)); });
            if (closed || version != loadVersion) return;
            var reference = resolveOriginal(gallery[next], loaded.Hash);
            if (document is not null) document.Changed -= DocumentChanged;
            lease?.Dispose(); lease = candidate; candidate = null; index = next;
            SetOriginal(reference);
            document = new ImageDocument(loaded.Image); document.Changed += DocumentChanged; canvas.Document = document; DocumentChanged(); Fit();
            status.Text = reference is null ? T("另存时保留本次打开图片的原版快照。旧版副本无法追溯更早的原图。") : T("已关联最初原图，可恢复后另存；当前文件不会被覆盖。");
        }
        catch (Exception ex) { if (!closed) status.Text = T("无法读取图片：") + ex.Message; }
        finally { candidate?.Dispose(); if (!closed && version == loadVersion) { loading = false; UpdateActions(); } }
    }
    private void DocumentChanged()
    {
        if (document is null) return;
        var crop = document.State.Crop; canvas.Width = crop.Width; canvas.Height = crop.Height; canvas.InvalidateVisual();
        heading.Text = $"{gallery[index].Title}   ·   {index + 1} / {gallery.Length}   ·   {crop.Width} × {crop.Height}" + (document.IsDirty ? T("   ·   未保存") : "");
        Title = (document.IsDirty ? "* " : "") + gallery[index].Title + T(" · 图片工作台");
        UpdateActions(); if (fitMode) Fit();
    }
    private void UpdateActions()
    {
        var enabled = document is not null && !loading && !saving;
        canvas.IsEnabled = enabled; editBar.IsEnabled = enabled; saveButton.IsEnabled = enabled;
        resetButton.IsEnabled = enabled;
        restoreButton.IsEnabled = enabled && originalReference is not null;
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
        canvas.LayoutTransform = new ScaleTransform(logical, logical); zoomLabel.Text = $"{scale:P0}";
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
            var bitmap = await Task.Run(() =>
            {
                var image = ImageFiles.Read(reference.Path);
                if (ImageFiles.Hash(image) != reference.Hash) throw new InvalidOperationException(T("原版快照内容已改变，无法可靠恢复。"));
                return image;
            });
            if (closed) return;
            document.RestoreOriginal(bitmap); Fit(); status.Text = T("已恢复最初原图，可撤销；另存后生成新副本。");
        }
        catch (Exception ex) { if (!closed) status.Text = T("恢复未完成，当前图片保留：") + ex.Message; }
        finally { loading = false; if (!closed) UpdateActions(); }
    }
    private async Task Save()
    {
        if (document is null || saving || loading) return;
        canvas.Cancel(); saving = true; UpdateActions();
        try
        {
            var result = await save(new(document.Render(), document.Original, gallery[index].Path, originalReference), this);
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
        if (e.Key == Key.Escape) { if (!canvas.Cancel()) Close(); e.Handled = true; return; }
        var textInput = Keyboard.FocusedElement is TextBoxBase;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.S) { _ = Save(); e.Handled = true; }
            else if (!textInput && !saving && !loading && e.Key is Key.Z or Key.Y) { canvas.Cancel(); if (e.Key == Key.Z) document?.Undo(); else document?.Redo(); e.Handled = true; }
            return;
        }
        if (textInput || canvas.Drawing || saving || loading) return;
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
    public bool Drawing => points.Count > 0;
    public event Action? Completed;
    public event Action<string>? Failed;
    public bool Cancel() { var active = Drawing; points.Clear(); if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); return active; }
    private Point Position(MouseEventArgs e) { var p = e.GetPosition(this); return Document!.Clamp(new Point(p.X + Document.State.Crop.X, p.Y + Document.State.Crop.Y)); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (Document is null || Tool == EditorTool.Pan) return;
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
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (!Drawing || Document is null) return;
        var start = points[0]; var end = Position(e);
        if (Tool != EditorTool.Pen) { if (points.Count == 1) points.Add(end); else points[^1] = end; }
        try { if (Tool == EditorTool.Crop) Document.Crop(new Rect(start, end)); else Document.Add(Document.CreateMark(Tool, points, Ink, StrokeSize, Annotation)); }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
        finally { Cancel(); Completed?.Invoke(); }
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); points.Clear(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight)); if (Document is null) return;
        var preview = Drawing && Tool is not EditorTool.Crop and not EditorTool.Mosaic ? Document.CreateMark(Tool, points, Ink, StrokeSize, Annotation) : null;
        Document.Draw(dc, preview);
        if (Drawing && Tool is EditorTool.Crop or EditorTool.Mosaic)
        {
            var crop = Document.State.Crop; var rect = new Rect(points[0], points[^1]); rect.Offset(-crop.X, -crop.Y);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 40, 80, 50)), new Pen(Brushes.ForestGreen, 2) { DashStyle = DashStyles.Dash }, rect);
        }
    }
}
