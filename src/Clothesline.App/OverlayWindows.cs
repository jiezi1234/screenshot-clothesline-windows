using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Clothesline.Core;

namespace Clothesline.App;

internal class OverlayWindow : Window
{
    public OverlayWindow(string title, bool passThrough)
    {
        Title = title; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SourceInitialized += (_, _) => Native.ConfigureOverlay(this, passThrough);
    }
    public void Present(PixelRect rect, bool animate)
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        if (!IsVisible) Show();
        Native.Place(this, rect);
        if (animate) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
    public void Dismiss() { BeginAnimation(OpacityProperty, null); Hide(); }
}

internal sealed class RopeWindow : OverlayWindow
{
    public RopeWindow() : base("Clothesline · 绳线", true) { Content = new RopeVisual(); }
}

internal sealed class RopeVisual : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 16), false, false);
            context.QuadraticBezierTo(new Point(ActualWidth / 2, 47), new Point(ActualWidth, 16), true, false);
        }
        dc.DrawGeometry(null, new Pen(DemoAssets.Brush("#507D8C83"), 2), geometry);
    }
}

internal sealed class CardWindow : OverlayWindow
{
    public DemoItem Item { get; }
    public bool Busy { get; private set; }
    public event Action<string>? Status;
    public event Action<CardWindow>? TakeDown;
    public event Action? DragEnded;
    private Point? down;
    private readonly DispatcherTimer clickTimer;
    private readonly TextBlock caption;
    private readonly Border surface;
    private string feedback = "";

    public CardWindow(DemoItem item) : base("Clothesline · " + item.Title, false)
    {
        Item = item; Width = DesktopGeometry.CardWidth; Height = DesktopGeometry.CardHeight;
        clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime()) };
        var root = new Grid { Margin = new Thickness(5, 0, 5, 5) };
        surface = new Border { Background = DemoAssets.Brush("#F9FAF7"), BorderBrush = DemoAssets.Brush("#D6DED6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 19, 0, 0), Padding = new Thickness(8) };
        var inside = new Grid();
        inside.RowDefinitions.Add(new RowDefinition { Height = new GridLength(113) });
        inside.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inside.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Source = item.Image, Stretch = Stretch.UniformToFill, ClipToBounds = true };
        inside.Children.Add(image);
        caption = new TextBlock { Text = item.Title, Foreground = DemoAssets.Brush("#243E35"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 7, 0, 0) };
        Grid.SetRow(caption, 1); inside.Children.Add(caption);
        var sub = new TextBlock { Text = item.Subtitle, FontSize = 9, Foreground = DemoAssets.Brush("#7D8B83"), Margin = new Thickness(2, 3, 0, 0) };
        Grid.SetRow(sub, 2); inside.Children.Add(sub);
        surface.Child = inside; root.Children.Add(surface);
        var clip = new Border { Width = 14, Height = 28, Background = DemoAssets.Brush("#BC8F58"), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(clip);
        var remove = new Button { Content = "×", Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 23, 4, 0), Background = DemoAssets.Brush("#F7FAF6"), Foreground = DemoAssets.Brush("#355446"), BorderThickness = new Thickness(0), Focusable = false, ToolTip = "取下示例（保留文件）" };
        remove.Click += (_, _) => { clickTimer.Stop(); TakeDown?.Invoke(this); };
        AutomationProperties.SetName(remove, "取下" + item.Title);
        root.Children.Add(remove); Content = root;
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); Copy(); };
        surface.MouseLeftButtonDown += Down;
        surface.MouseMove += Move;
        surface.MouseLeftButtonUp += Up;
        surface.LostMouseCapture += (_, _) => down = null;
        surface.MouseEnter += (_, _) => { if (!Busy) caption.Text = "单击复制 · 双击打开 · 拖出"; };
        surface.MouseLeave += (_, _) => { if (!Busy) caption.Text = item.Title; };
        var menu = new ContextMenu();
        Add(menu, "复制图片", Copy);
        Add(menu, "打开图片", () => ShellTransfer.Open(item.Path));
        Add(menu, "打开示例文件夹", () => ShellTransfer.Open(Path.GetDirectoryName(item.Path)!));
        menu.Items.Add(new Separator());
        Add(menu, "取下（保留文件）", () => TakeDown?.Invoke(this));
        Add(menu, "将示例文件移入回收站", () => { Report(ShellTransfer.Recycle(item.Path)); DragEnded?.Invoke(); });
        menu.Opened += (_, _) => { Busy = true; clickTimer.Stop(); };
        menu.Closed += (_, _) => Busy = false;
        surface.ContextMenu = menu;
        Closed += (_, _) => clickTimer.Stop();
    }
    private void Add(ContextMenu menu, string title, Action action)
    {
        var entry = new MenuItem { Header = title };
        entry.Click += (_, _) => { try { action(); } catch (Exception ex) { Report("操作未完成：" + ex.Message); } };
        menu.Items.Add(entry);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        clickTimer.Stop();
        if (e.ClickCount == 2)
        {
            down = null;
            try { ShellTransfer.Open(Item.Path); } catch (Exception ex) { Report(ex.Message); }
            e.Handled = true; return;
        }
        down = e.GetPosition(surface); surface.CaptureMouse(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (down is not Point start || e.LeftButton != MouseButtonState.Pressed || Busy) return;
        var p = e.GetPosition(surface);
        if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        down = null; surface.ReleaseMouseCapture(); clickTimer.Stop(); Busy = true;
        try { Report(ShellTransfer.Drag(this, Item)); }
        catch (Exception ex) { Report("拖拽未完成：" + ex.Message); }
        finally { Busy = false; DragEnded?.Invoke(); }
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (down is null) return;
        down = null; surface.ReleaseMouseCapture(); clickTimer.Start(); e.Handled = true;
    }
    private void Copy()
    {
        if (!File.Exists(Item.Path)) { Report("示例文件已移走，请恢复示例。"); return; }
        try
        {
            var data = new DataObject();
            data.SetImage(Item.Image);
            data.SetFileDropList(new StringCollection { Item.Path });
            data.SetData("PNG", new MemoryStream(File.ReadAllBytes(Item.Path)));
            data.SetData("ScreenshotClothesline.OwnCopy", "1", false);
            Clipboard.SetDataObject(data, true);
            Report("已复制原图");
        }
        catch (System.Runtime.InteropServices.COMException) { Report("剪贴板正在被占用，请稍后再试。"); }
    }
    private void Report(string text) { feedback = text; caption.Text = feedback; Status?.Invoke(text); }
}
