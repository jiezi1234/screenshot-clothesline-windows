using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

// Dashboard/history preview only. Editing is owned exclusively by the shelf hover popup.
internal sealed class TextPreviewWindow : Window
{
    internal string EntryId { get; }
    internal TextBox TextView { get; } = new() { IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 16, Padding = new Thickness(16), BorderThickness = new Thickness(1) };
    private readonly TextBlock status = new() { MinHeight = 24, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly string path;
    private bool closed;
    internal bool Loading { get; private set; }
    internal Action<IDataObject> CopyToClipboard { get; set; } = data => Clipboard.SetDataObject(data, true);

    public TextPreviewWindow(string id, string path, IDisposable lease)
    {
        EntryId = id; this.path = path;
        var paper = TextPaper.Palette(TextPaperStyles.ForEntry(id));
        TextView.Background = DemoAssets.Brush(paper.Paper); TextView.Foreground = DemoAssets.Brush(paper.Ink);
        TextView.BorderBrush = DemoAssets.Brush(paper.Edge); TextView.FontFamily = new FontFamily(paper.Font);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Language);
        Title = T("文字预览 · 滚轮查看全文"); Width = 760; Height = 620; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = DemoAssets.Brush("#F4F5EE"); Foreground = DemoAssets.Brush("#294535"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(TextView);
        AutomationProperties.SetName(TextView, T("文字内容")); UiAccessibility.Install(this); UiAccessibility.LiveStatus(this, status);
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = T("复制"), Command = ApplicationCommands.Copy, CommandTarget = TextView, InputGestureText = "Ctrl+C" });
        TextView.ContextMenu = menu;
        TextView.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            try { CopyToClipboard(ShellTransfer.TextData(TextView.SelectedText)); status.Text = T("已复制文字"); }
            catch (Exception ex) { status.Text = ex.Message; }
            e.Handled = true;
        }, (_, e) => { e.CanExecute = !Loading && TextView.SelectionLength > 0; e.Handled = true; }));
        TextView.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => e.Handled = true,
            (_, e) => { e.CanExecute = false; e.Handled = true; }));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { closed = true; lease.Dispose(); };
        Loaded += async (_, _) => await RefreshText();
    }

    internal async Task RefreshText()
    {
        if (Loading || closed) return;
        Loading = true; TextView.IsEnabled = false;
        try
        {
            var text = await Task.Run(() => TextContent.Read(path)); if (closed) return;
            TextView.Text = text; status.Text = T("文字 · {0} 个字符", text.Length);
        }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        finally { Loading = false; if (!closed) TextView.IsEnabled = true; }
    }
}
