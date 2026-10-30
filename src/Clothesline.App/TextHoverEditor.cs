using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed class TextEditSession(string id, string original, Func<string, string, Task<string>> save, IDisposable lease) : IDisposable
{
    internal string EntryId => id;
    private string hash = TextContent.Hash(original);
    internal async Task Save(string text) => hash = await save(text, hash);
    public void Dispose() => lease.Dispose();
}

internal sealed partial class TextHoverWindow
{
    private DemoItem? previewItem;
    private TextEditSession? session;
    private string original = "";
    private bool saving, savedNotice, permanentClose;
    private Task<bool>? pendingSave;
    private nint previousForeground;
    private readonly TextBlock editStatus = new() { TextWrapping = TextWrapping.Wrap, MaxHeight = 48, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    private Button saveButton = null!, cancelButton = null!;
    private MenuItem copyItem = null!;
    internal Func<DemoItem, TextEditSession?>? StartEdit { get; set; }
    internal Action<IDataObject> CopyToClipboard { get; set; } = data => Clipboard.SetDataObject(data, true);
    internal event Action? CloseRequested;
    internal bool IsEditing => session is not null;
    internal bool Dirty => IsEditing && TextView.Text != original;
    internal bool Saving => saving;
    internal bool SaveFailed { get; private set; }
    internal bool PointerInteraction => IsMouseCaptureWithin || TextView.ContextMenu?.IsOpen == true;
    internal string? EditingId => session?.EntryId;
    internal string EditStatus => editStatus.Text;

    private void InitializeEditor()
    {
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Insert(1, footer);
        footer.Children.Add(editStatus);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        Button Action(string label, Action action)
        {
            var button = new Button { Content = T(label), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 2), BorderThickness = new Thickness(0) };
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        saveButton = Action("保存文字", () => _ = Save());
        cancelButton = Action("取消编辑", () => { if (TryEndEditing(() => true)) Dismiss(); });
        TextView.AcceptsReturn = true; TextView.AcceptsTab = true; TextView.UndoLimit = 100;
        TextView.TextChanged += (_, _) => { if (IsEditing && !saving) { SaveFailed = false; editStatus.Text = Dirty ? T("未保存修改") : T("直接编辑 · 移开自动保存 · Ctrl+C 复制所选文字"); UpdateEditorTools(); } };
        copyItem = new MenuItem { Header = T("复制"), Command = ApplicationCommands.Copy, CommandTarget = TextView, InputGestureText = "Ctrl+C" };
        TextView.ContextMenu = new ContextMenu(); TextView.ContextMenu.Items.Add(copyItem);
        foreach (var command in new[] { ApplicationCommands.Copy, ApplicationCommands.Cut })
            TextView.CommandBindings.Add(new CommandBinding(command, (_, e) =>
            {
                try { CopyToClipboard(ShellTransfer.TextData(TextView.SelectedText)); if (e.Command == ApplicationCommands.Cut) TextView.SelectedText = ""; }
                catch (Exception ex) { editStatus.Text = ex.Message; editStatus.Visibility = Visibility.Visible; }
                e.Handled = true;
            }, (_, e) => { e.CanExecute = TextView.SelectionLength > 0 && (e.Command == ApplicationCommands.Copy || IsEditing && !saving); e.Handled = true; }));
        PreviewKeyDown += (_, e) =>
        {
            if (!IsEditing) return;
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _ = Save(); e.Handled = true; }
            else if (e.Key == Key.Escape) { RequestClose(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (permanentClose) return;
            e.Cancel = true; // Alt+F4 dismisses this reusable popup, rather than destroying it.
            RequestClose();
        };
        UiAccessibility.LiveStatus(this, editStatus); UpdateEditorTools();
    }
    internal void SetPreview(DemoItem item)
    {
        if (IsEditing) return;
        savedNotice = false; SaveFailed = false; SetText(item.Text ?? "", item.PaperStyle); previewItem = item;
        if (!BeginEdit()) { TextView.Focusable = true; SetKeyboardInput(true); }
        UpdateEditorTools();
    }
    internal bool BeginEdit()
    {
        if (IsEditing) return true;
        if (previewItem is null || StartEdit is null) return false;
        try
        {
            session = StartEdit(previewItem); if (session is null) return false;
            original = TextView.Text; savedNotice = false;
            previousForeground = Native.GetForegroundWindow();
            TextView.IsUndoEnabled = false; TextView.IsUndoEnabled = true;
            TextView.IsReadOnly = false; TextView.Focusable = true; SetKeyboardInput(true);
            editStatus.Text = T("直接编辑 · 移开自动保存 · Ctrl+C 复制所选文字"); UpdateEditorTools(); return true;
        }
        catch (Exception ex) { if (session is not null) FinishEdit(); savedNotice = true; editStatus.Text = ex.Message; UpdateEditorTools(); return false; }
    }
    internal void FocusEditor()
    {
        if (!IsVisible || !IsEditing) return;
        Activate(); TextView.Focus(); Keyboard.Focus(TextView);
    }
    private void UpdateEditorTools()
    {
        var palette = TextPaper.Palette(PaperStyle);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Language);
        copyItem.Header = T("复制"); saveButton.Content = T("保存文字"); cancelButton.Content = T("取消编辑");
        heading.Text = IsEditing ? T("编辑文字") : T("文字预览 · 滚轮查看全文");
        foreach (var button in new[] { saveButton, cancelButton })
        { button.Background = DemoAssets.Brush(palette.Edge); button.Foreground = DemoAssets.Brush(palette.Ink); }
        editStatus.Foreground = DemoAssets.Brush(palette.Ink);
        saveButton.Visibility = cancelButton.Visibility = Dirty || SaveFailed ? Visibility.Visible : Visibility.Collapsed;
        saveButton.IsEnabled = Dirty && !saving; cancelButton.IsEnabled = !saving;
        editStatus.Visibility = IsEditing || savedNotice ? Visibility.Visible : Visibility.Collapsed;
    }
    internal Task<bool> Save()
    {
        if (session is null || saving) return Task.FromResult(false);
        return pendingSave = SaveCore();
    }
    private async Task<bool> SaveCore()
    {
        if (!Dirty) return true;
        saving = true; TextView.IsReadOnly = true; UpdateEditorTools();
        try
        {
            var text = TextView.Text; await session!.Save(text); original = text;
            if (previewItem is not null) previewItem = previewItem with { Text = text, Title = TextContent.Title(text) };
            SaveFailed = false; savedNotice = true; editStatus.Text = T("文字已保存，原卡片已更新。"); return true;
        }
        catch (Exception ex) { SaveFailed = true; editStatus.Text = ex.Message; return false; }
        finally { saving = false; TextView.IsReadOnly = !IsEditing; UpdateEditorTools(); }
    }
    private void RequestClose()
    {
        if (CloseRequested is { } close) close(); else _ = SaveAndDismiss();
    }
    internal async Task<bool> SaveAndDismiss(Func<bool>? shouldClose = null)
    {
        if (IsEditing && !await (saving && pendingSave is not null ? pendingSave : Save())) return false;
        if (shouldClose is not null && !shouldClose()) return false;
        FinishEdit(); Dismiss(); return true;
    }
    internal bool TryEndEditing(Func<bool>? confirmDiscard = null)
    {
        if (saving) return false;
        if (!IsEditing) return true;
        if (Dirty && !(confirmDiscard?.Invoke() ?? MessageBox.Show(T("放弃本次尚未保存的修改？"), T("未保存修改"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)) return false;
        TextView.Text = original; FinishEdit(); return true;
    }
    private void FinishEdit()
    {
        var previous = session; session = null; previous?.Dispose(); SaveFailed = false;
        TextView.IsReadOnly = true; TextView.Focusable = false; SetKeyboardInput(false);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (previousForeground != 0 && previousForeground != handle && Native.GetForegroundWindow() == handle)
            Native.SetForegroundWindow(previousForeground);
        previousForeground = 0;
        TextView.IsUndoEnabled = false; TextView.IsUndoEnabled = true;
        UpdateEditorTools();
    }
    internal void ClosePermanently() { permanentClose = true; Close(); }
}
