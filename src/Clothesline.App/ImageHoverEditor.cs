using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline.App;

internal sealed class ImageEditSession(ImageDocument document, Func<BitmapSource, Task> save, IDisposable lease, Action<ImageDocument>? remember = null) : IDisposable
{
    internal ImageDocument Document => document;
    internal async Task Save(BitmapSource image, ImageDocument checkpoint)
    { await save(image); remember?.Invoke(checkpoint); }
    public void Dispose() => lease.Dispose();
}

internal sealed partial class HoverPreviewWindow
{
    private ImageEditSession? session;
    private readonly ImageSurface canvas = new() { Focusable = true, Tool = EditorTool.Pan };
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DockPanel editor = new() { LastChildFill = true };
    private readonly WrapPanel tools = new();
    private readonly Dictionary<EditorTool, ToggleButton> toolButtons = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), MaxHeight = 48, FontSize = 12 };
    private readonly Button undo, redo, retry;
    private readonly TextBox annotation = new() { Width = 140, MaxLength = 300, Margin = new Thickness(3), ToolTip = T("文字内容") };
    private readonly DispatcherTimer autoSave = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Task<bool>? pendingSave;
    private bool saving, permanentClose, confirming, fit = true;
    private double zoom = 1;
    private nint previousForeground;
    internal bool IsEditing => session is not null;
    internal bool Dirty => session?.Document.IsDirty == true;
    internal bool Saving => saving;
    internal bool SaveFailed { get; private set; }
    internal ImageDocument? Document => session?.Document;
    internal ImageSurface Canvas => canvas;
    internal string EditStatus => status.Text;
    internal bool PointerInteraction => IsMouseCaptureWithin || canvas.Drawing || canvas.Processing;
    internal event Action? CloseRequested;
    internal event Action? Saved;

    // Called by the preview constructor. This is the same popup HWND throughout the session.
    private void InitializeEditor(out Button previous, out Button next, out Button tryAgain)
    {
        var commands = new WrapPanel();
        Button Add(string text, Action action)
        {
            var button = new Button { Content = T(text), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
            button.Click += (_, _) => action(); commands.Children.Add(button); return button;
        }
        previous = Add("上一步", Undo); next = Add("下一步", Redo);
        previous.ToolTip = "Ctrl+Z"; next.ToolTip = "Ctrl+Y";
        Add("适应窗口", () => { fit = true; ResizeCanvas(); });
        Add("100%", () => { fit = false; zoom = 1 / VisualTreeHelper.GetDpi(this).DpiScaleX; ResizeCanvas(); });
        tryAgain = Add("重试", () => _ = Save()); tryAgain.Visibility = Visibility.Collapsed;
        foreach (var (tool, name) in new[] { (EditorTool.Pan, "浏览"), (EditorTool.Crop, "裁剪"), (EditorTool.Pen, "画笔"), (EditorTool.Arrow, "箭头"), (EditorTool.Rectangle, "矩形"), (EditorTool.Text, "文字"), (EditorTool.Mosaic, "马赛克") })
        {
            var button = new ToggleButton { Content = T(name), Margin = new Thickness(2), Padding = new Thickness(6, 3, 6, 3) };
            button.Click += (_, _) => SelectTool(tool); tools.Children.Add(button); toolButtons.Add(tool, button);
        }
        var options = new WrapPanel();
        foreach (var color in new[] { Colors.Crimson, Colors.Orange, Colors.ForestGreen, Colors.DodgerBlue, Colors.Black, Colors.White })
        {
            var swatch = new Button { Width = 20, Height = 20, Background = new SolidColorBrush(color), Margin = new Thickness(3), ToolTip = T("颜色") + " " + color };
            AutomationProperties.SetName(swatch, swatch.ToolTip.ToString()); swatch.Click += (_, _) => canvas.Ink = color; options.Children.Add(swatch);
        }
        var size = new Slider { Minimum = 1, Maximum = 24, Value = 4, Width = 90, Margin = new Thickness(6), ToolTip = T("笔画粗细") };
        AutomationProperties.SetName(size, T("笔画粗细")); size.ValueChanged += (_, _) => canvas.StrokeSize = size.Value; options.Children.Add(size);
        AutomationProperties.SetName(annotation, T("文字内容")); annotation.TextChanged += (_, _) => canvas.Annotation = annotation.Text; options.Children.Add(annotation);
        var header = new StackPanel(); header.Children.Add(commands); header.Children.Add(tools); header.Children.Add(options);
        DockPanel.SetDock(header, Dock.Top); editor.Children.Add(header);
        DockPanel.SetDock(status, Dock.Bottom); editor.Children.Add(status);
        viewport.Content = canvas; editor.Children.Add(viewport);
        viewport.SizeChanged += (_, _) => ResizeCanvas();
        viewport.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            fit = false; zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), .01, 8); ResizeCanvas(); e.Handled = true;
        };
        canvas.Completed += () => { UpdateActions(); ScheduleSave(); };
        canvas.Failed += message => { status.Text = message; };
        canvas.KeyboardPositionChanged += message => status.Text = message;
        AutomationProperties.SetName(canvas, T("图片编辑画布"));
        autoSave.Tick += async (_, _) => { autoSave.Stop(); if (PointerInteraction) ScheduleSave(); else await Save(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.SystemKey == Key.C && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) { canvas.Focus(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { if (!canvas.Cancel()) CloseRequested?.Invoke(); e.Handled = true; }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.S) { _ = Save(); e.Handled = true; }
                else if (!annotation.IsKeyboardFocusWithin && e.Key == Key.Z) { Undo(); e.Handled = true; }
                else if (!annotation.IsKeyboardFocusWithin && e.Key == Key.Y) { Redo(); e.Handled = true; }
            }
        };
        Closing += (_, e) => { if (!permanentClose) { e.Cancel = true; CloseRequested?.Invoke(); } };
        UiAccessibility.LiveStatus(this, status);
    }
    internal void SetSession(ImageEditSession value)
    {
        session = value; canvas.Document = value.Document; fit = true; SaveFailed = false;
        previousForeground = Native.GetForegroundWindow(); SetKeyboardInput(true);
        frame.Child = editor; image.Source = value.Document.Original;
        value.Document.Changed += DocumentChanged;
        SelectTool(EditorTool.Pan); DocumentChanged();
    }
    private void SelectTool(EditorTool tool)
    {
        if (canvas.Processing) return;
        canvas.Cancel(); canvas.Tool = tool;
        foreach (var pair in toolButtons) pair.Value.IsChecked = pair.Key == tool;
        annotation.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
    }
    private void DocumentChanged()
    {
        ResizeCanvas(); canvas.InvalidateVisual();
        if (!saving) status.Text = Dirty ? T("未保存修改") : T("图片修改后自动保存 · 原图保留");
        UpdateActions(); ScheduleSave();
    }
    private void ResizeCanvas()
    {
        if (Document is not { } document) return;
        var crop = document.State.Crop; canvas.Width = crop.Width; canvas.Height = crop.Height;
        if (fit) zoom = Math.Clamp(Math.Min(Math.Max(1, viewport.ActualWidth - 20) / crop.Width, Math.Max(1, viewport.ActualHeight - 20) / crop.Height), .001, 1);
        canvas.LayoutTransform = new ScaleTransform(zoom, zoom);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi); canvas.Cancel();
        if (!fit) zoom *= oldDpi.DpiScaleX / newDpi.DpiScaleX;
        ResizeCanvas();
    }
    private void UpdateActions()
    {
        undo.IsEnabled = Document?.CanUndo == true && !canvas.Processing;
        redo.IsEnabled = Document?.CanRedo == true && !canvas.Processing;
        tools.IsEnabled = !canvas.Processing;
        retry.Visibility = SaveFailed ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void Undo() { if (!canvas.Processing) { canvas.Cancel(); Document?.Undo(); SaveFailed = false; ScheduleSave(); } }
    internal void Redo() { if (!canvas.Processing) { canvas.Cancel(); Document?.Redo(); SaveFailed = false; ScheduleSave(); } }
    private void ScheduleSave() { autoSave.Stop(); if (Dirty && !saving && !SaveFailed && !confirming) autoSave.Start(); }
    internal Task<bool> Save()
    {
        if (saving) return pendingSave!;
        if (!IsEditing || PointerInteraction || confirming) return Task.FromResult(false);
        autoSave.Stop(); return pendingSave = SaveCore();
    }
    private async Task<bool> SaveCore()
    {
        if (!Dirty) return true;
        saving = true; status.Text = T("正在保存…");
        var active = session!; var snapshot = active.Document.State; var checkpoint = active.Document.SavedCheckpoint();
        try
        {
            var rendered = await active.Document.RenderAsync(); await active.Save(rendered, checkpoint);
            // Editing remains available while encoding/writing. Only acknowledge this exact snapshot.
            active.Document.MarkSaved(snapshot); SaveFailed = false;
            status.Text = Dirty ? T("未保存修改") : T("图片已保存，原卡片已更新。"); return true;
        }
        catch (Exception ex) { SaveFailed = true; status.Text = ex.Message; return false; }
        finally { saving = false; UpdateActions(); ScheduleSave(); if (!SaveFailed) Saved?.Invoke(); }
    }
    internal async Task<bool> SaveAndDismiss(Func<bool>? shouldClose = null)
    {
        while (IsEditing)
        {
            if (PointerInteraction || shouldClose is not null && !shouldClose()) return false;
            if (!await Save()) return false;
            if (!Dirty) break;
        }
        if (shouldClose is not null && !shouldClose()) return false;
        FinishEdit(); Dismiss(); return true;
    }
    internal bool TryEndEditing(Func<bool>? confirmDiscard = null)
    {
        if (saving || canvas.Processing) return false;
        confirming = true; autoSave.Stop();
        try
        {
            if (Dirty && !(confirmDiscard?.Invoke() ?? MessageBox.Show(T("放弃本次尚未保存的修改？"), T("未保存修改"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)) return false;
            FinishEdit(); return true;
        }
        finally { confirming = false; ScheduleSave(); }
    }
    private void FinishEdit()
    {
        autoSave.Stop(); canvas.Cancel();
        if (session is { } previous) { previous.Document.Changed -= DocumentChanged; session = null; previous.Dispose(); }
        canvas.Document = null; canvas.InvalidateVisual(); frame.Child = image; SaveFailed = false; SetKeyboardInput(false);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (previousForeground != 0 && previousForeground != handle && Native.GetForegroundWindow() == handle) Native.SetForegroundWindow(previousForeground);
        previousForeground = 0;
    }
    internal void ClosePermanently() { autoSave.Stop(); permanentClose = true; Close(); }
}
