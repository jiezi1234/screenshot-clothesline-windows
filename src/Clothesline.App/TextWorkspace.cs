using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed class TextWorkspace : Window
{
    internal string EntryId { get; }
    internal TextBox Editor { get; } = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 16, Padding = new Thickness(16), BorderThickness = new Thickness(1), BorderBrush = DemoAssets.Brush("#D6DED6"), Background = DemoAssets.Brush("#FFFEFA"), UndoLimit = 100 };
    private readonly TextBlock status = new() { MinHeight = 24, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button saveButton;
    private readonly Func<string, string, Task<string>> save;
    private string original = "", hash = "";
    internal bool Loading { get; private set; } = true;
    internal bool Dirty => !Loading && Editor.Text != original;
    private bool saving, closed;
    public TextWorkspace(string id, string path, Func<string, string, Task<string>> save, IDisposable lease)
    {
        EntryId = id; this.save = save;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Language);
        Title = T("文字工作台 · Screenshot Clothesline"); Width = 760; Height = 620; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = DemoAssets.Brush("#F4F5EE"); Foreground = DemoAssets.Brush("#294535"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = T("编辑文字"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = T("Ctrl+S 保存到原卡片 · Esc 关闭"), Margin = new Thickness(0, 6, 0, 12), Foreground = DemoAssets.Brush("#6D7B70") });
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        saveButton = Button(T("保存文字"), () => _ = Save()); actions.Children.Add(saveButton);
        actions.Children.Add(Button(T("复制文字"), () => { if (!Loading) { Clipboard.SetDataObject(ShellTransfer.TextData(Editor.Text), true); status.Text = T("已复制文字"); } }));
        actions.Children.Add(Button(T("撤销"), () => { if (!Loading && !saving) Editor.Undo(); })); actions.Children.Add(Button(T("重做"), () => { if (!Loading && !saving) Editor.Redo(); }));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(Editor);
        AutomationProperties.SetName(Editor, T("文字内容")); UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        Editor.IsEnabled = false; saveButton.IsEnabled = false;
        Editor.TextChanged += (_, _) => { if (!Loading) { saveButton.IsEnabled = Dirty && !saving; status.Text = T("文字 · {0} 个字符", Editor.Text.Length); } };
        foreach (var command in new[] { ApplicationCommands.Copy, ApplicationCommands.Cut })
            Editor.CommandBindings.Add(new CommandBinding(command, (_, e) =>
            {
                try { Clipboard.SetDataObject(ShellTransfer.TextData(Editor.SelectedText), true); if (e.Command == ApplicationCommands.Cut) Editor.SelectedText = ""; }
                catch (Exception ex) { status.Text = ex.Message; }
                e.Handled = true;
            }, (_, e) => { e.CanExecute = Editor.SelectionLength > 0 && !saving; e.Handled = true; }));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _ = Save(); e.Handled = true; } else if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closing += (_, e) =>
        {
            if (saving) { e.Cancel = true; return; }
            if (Dirty && MessageBox.Show(this, T("放弃本次尚未保存的修改？"), T("未保存修改"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
        };
        Closed += (_, _) => { closed = true; lease.Dispose(); };
        Loaded += async (_, _) =>
        {
            try
            {
                var text = await Task.Run(() => TextContent.Read(path)); if (closed) return;
                hash = TextContent.Hash(text); Editor.Text = text; original = Editor.Text; Loading = false; Editor.IsEnabled = true; Editor.Focus(); status.Text = T("文字 · {0} 个字符", text.Length);
            }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        };
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Background = DemoAssets.Brush("#E2E9DD"), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button;
    }
    internal async Task<bool> Save()
    {
        if (Loading || saving || closed) return false;
        if (!Dirty) return true;
        saving = true; Editor.IsReadOnly = true; saveButton.IsEnabled = false;
        try { var text = Editor.Text; hash = await save(text, hash); original = text; status.Text = T("文字已保存，原卡片已更新。"); return true; }
        catch (Exception ex) { status.Text = ex.Message; return false; }
        finally { saving = false; Editor.IsReadOnly = false; saveButton.IsEnabled = Dirty; }
    }
}
