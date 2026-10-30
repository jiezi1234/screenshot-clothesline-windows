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
    private readonly Grid tools = new();
    private readonly Dictionary<EditorTool, ToggleButton> toolButtons = new();
    private readonly TextBlock status = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    private readonly Button undo, redo, retry;
    private readonly TextBox annotation = new() { MaxLength = 300, ToolTip = T("文字内容") };
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
    internal bool PointerInteraction => IsMouseCaptureWithin || canvas.Drawing || canvas.Processing || zoomPicker.IsDropDownOpen;
    internal event Action? CloseRequested;
    internal event Action? Saved;

    // Called by the preview constructor. This is the same popup HWND throughout the session.
    private void InitializeEditor(out Button previous, out Button next, out Button tryAgain)
    {
        BuildEditorChrome(out previous, out next, out tryAgain);
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
            if (e.Key == Key.Escape) { if (zoomPicker.IsDropDownOpen) zoomPicker.IsDropDownOpen = false; else if (!canvas.Cancel()) CloseRequested?.Invoke(); e.Handled = true; }
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
    internal void SetSession(ImageEditSession value, string? title = null)
    {
        session = value; canvas.Document = value.Document; fit = true; SaveFailed = false;
        previousForeground = Native.GetForegroundWindow(); SetKeyboardInput(true);
        frame.Padding = new Thickness(0); frame.CornerRadius = new CornerRadius(18);
        frame.Child = editor; image.Source = value.Document.Original;
        filename.Text = string.IsNullOrWhiteSpace(title) ? T("图片") : title; filename.ToolTip = filename.Text;
        value.Document.Changed += DocumentChanged;
        SelectTool(EditorTool.Pan); DocumentChanged();
    }
    private void SelectTool(EditorTool tool)
    {
        if (canvas.Processing) return;
        canvas.Cancel(); canvas.Tool = tool;
        foreach (var pair in toolButtons) pair.Value.IsChecked = pair.Key == tool;
        UpdateToolOptions();
    }
    private void DocumentChanged()
    {
        ResizeCanvas(); canvas.InvalidateVisual();
        if (!saving && !SaveFailed) status.Text = Dirty ? T("未保存修改") : T("已自动保存");
        UpdateActions(); ScheduleSave();
    }
    private void ResizeCanvas()
    {
        if (Document is not { } document) return;
        var crop = document.State.Crop; canvas.Width = crop.Width; canvas.Height = crop.Height;
        if (fit) zoom = Math.Clamp(Math.Min(Math.Max(1, viewport.ActualWidth - 20) / crop.Width, Math.Max(1, viewport.ActualHeight - 20) / crop.Height), .001, 1);
        canvas.LayoutTransform = new ScaleTransform(zoom, zoom);
        UpdateZoomDisplay();
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
        UpdateSaveIndicator();
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
        saving = true; status.Text = T("正在保存…"); UpdateSaveIndicator();
        var active = session!; var snapshot = active.Document.State; var checkpoint = active.Document.SavedCheckpoint();
        try
        {
            var rendered = await active.Document.RenderAsync(); await active.Save(rendered, checkpoint);
            // Editing remains available while encoding/writing. Only acknowledge this exact snapshot.
            active.Document.MarkSaved(snapshot); SaveFailed = false;
            status.Text = Dirty ? T("未保存修改") : T("已自动保存"); return true;
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
        autoSave.Stop(); canvas.Cancel(); zoomPicker.IsDropDownOpen = false;
        if (session is { } previous) { previous.Document.Changed -= DocumentChanged; session = null; previous.Dispose(); }
        canvas.Document = null; canvas.InvalidateVisual(); frame.Child = image; frame.Padding = new Thickness(5); frame.CornerRadius = new CornerRadius(10); SaveFailed = false; SetKeyboardInput(false);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (previousForeground != 0 && previousForeground != handle && Native.GetForegroundWindow() == handle) Native.SetForegroundWindow(previousForeground);
        previousForeground = 0;
    }
    internal void ClosePermanently() { autoSave.Stop(); permanentClose = true; Close(); }
}
