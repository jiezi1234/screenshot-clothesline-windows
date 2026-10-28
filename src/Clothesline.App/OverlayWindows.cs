using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
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
    private int opacityGeneration;
    public OverlayWindow(string title, bool passThrough)
    {
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Clothesline.Core.UiText.Language);
        Title = title; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SourceInitialized += (_, _) => Native.ConfigureOverlay(this, passThrough);
    }
    public virtual void Present(PixelRect rect, bool animate)
    {
        var entering = !IsVisible;
        StopMotion();
        if (!IsVisible) { DesktopLifecycle.FollowForeground(this); Show(); }
        Native.Place(this, rect);
        if (animate && entering)
        {
            var current = opacityGeneration;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
            fade.Completed += (_, _) => { if (current == opacityGeneration) BeginAnimation(OpacityProperty, null); };
            BeginAnimation(OpacityProperty, fade);
        }
    }
    public virtual void StopMotion() { opacityGeneration++; BeginAnimation(OpacityProperty, null); Opacity = 1; }
    public virtual void Dismiss() { StopMotion(); Hide(); }
}

internal sealed class RopeWindow : OverlayWindow
{
    private readonly Border emptyHint;
    public void RefreshLanguage() { Language = System.Windows.Markup.XmlLanguage.GetLanguage(Clothesline.Core.UiText.Language); Title = T("Clothesline · 绳线"); ((TextBlock)emptyHint.Child).Text = T("暂时没有图片"); }
    public bool ShowEmptyHint
    {
        get => emptyHint.Visibility == Visibility.Visible;
        set => emptyHint.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    public RopeWindow() : base(T("Clothesline · 绳线"), true)
    {
        var content = new Grid();
        content.Children.Add(new RopeVisual());
        emptyHint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 46, 0, 0), Padding = new Thickness(18, 8, 18, 8),
            Background = DemoAssets.Brush("#F9FAF7"), BorderBrush = DemoAssets.Brush("#D6DED6"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = T("暂时没有图片"), FontSize = 14,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                Foreground = DemoAssets.Brush("#294535")
            }
        };
        content.Children.Add(emptyHint); Content = content;
    }
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
    public bool InteractionActive => Busy || copying || down is not null || clickTimer.IsEnabled;
    private bool copying;
    public event Action<string>? Status;
    public event Action<CardWindow>? TakeDown;
    public event Action? DragEnded;
    public Func<CardWindow, Task<string>>? DragRequested { get; set; }
    public Action<DemoItem>? RecycleRequested { get; set; }
    public Action<DemoItem, bool>? ExportRequested { get; set; }
    public Action<DemoItem, bool>? WorkspaceRequested { get; set; }
    public Action<DemoItem, ArchiveScope>? BatchArchiveRequested { get; set; }
    private Point? down;
    private readonly DispatcherTimer clickTimer;
    private readonly DispatcherTimer holdTimer;
    private readonly TextBlock caption;
    private readonly Border surface;
    private readonly CardMotion motion;
    private bool closed;
    public bool Retiring { get; private set; }
    internal bool MotionRunning => motion.Running || HasAnimatedProperties;
    private string feedback = "";

    public CardWindow(DemoItem item) : base("Clothesline · " + item.Title, false)
    {
        Item = item; Width = DesktopGeometry.CardWidth; Height = DesktopGeometry.CardHeight;
        clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime()) };
        holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        var root = new Grid { Margin = new Thickness(5, 1, 5, 5) };
        motion = new CardMotion(root);
        surface = new Border { Background = DemoAssets.Brush("#F9FAF7"), BorderBrush = DemoAssets.Brush("#D6DED6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 19, 0, 0), Padding = new Thickness(8) };
        var inside = new Grid();
        inside.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inside.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inside.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Source = item.Image, Stretch = Stretch.UniformToFill, ClipToBounds = true };
        inside.Children.Add(image);
        if (item.IsVideo)
            inside.Children.Add(new TextBlock { Text = "▶", FontSize = 28, Foreground = Brushes.White, Background = DemoAssets.Brush("#203B36"), Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        caption = new TextBlock { Text = item.Title, ToolTip = item.Title, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = DemoAssets.Brush("#243E35"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 7, 0, 0) };
        Grid.SetRow(caption, 1); inside.Children.Add(caption);
        var sub = new TextBlock { Text = item.Subtitle, ToolTip = item.Subtitle, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 9, Foreground = DemoAssets.Brush("#7D8B83"), Margin = new Thickness(2, 3, 0, 0) };
        Grid.SetRow(sub, 2); inside.Children.Add(sub);
        surface.Child = inside; root.Children.Add(surface);
        var clip = new Border { Width = 14, Height = 28, Background = DemoAssets.Brush("#BC8F58"), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(clip);
        var remove = new Button { Content = "×", Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 23, 4, 0), Background = DemoAssets.Brush("#F7FAF6"), Foreground = DemoAssets.Brush("#355446"), BorderThickness = new Thickness(0), Focusable = false, ToolTip = T("取下到历史（保留文件）") };
        remove.Click += (_, _) => { clickTimer.Stop(); TakeDown?.Invoke(this); };
        AutomationProperties.SetName(remove, T("取下") + item.Title);
        root.Children.Add(remove);
        var layout = new Grid { Width = DesktopGeometry.CardWidth, Height = DesktopGeometry.CardHeight }; layout.Children.Add(root);
        Content = new Viewbox { Child = layout, Stretch = Stretch.Uniform };
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); Copy(); };
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Stop(); if (down is null || Busy || Mouse.LeftButton != MouseButtonState.Pressed) return;
            down = null; clickTimer.Stop(); surface.ReleaseMouseCapture();
            try { WorkspaceRequested?.Invoke(Item, !Item.IsVideo); } catch (Exception ex) { Report(ex.Message); }
        };
        surface.MouseLeftButtonDown += Down;
        surface.MouseMove += Move;
        surface.MouseLeftButtonUp += Up;
        surface.LostMouseCapture += (_, _) => { down = null; holdTimer.Stop(); };
        MouseEnter += (_, _) => { if (!Retiring) StopMotion(); };
        surface.MouseEnter += (_, _) => { if (!Busy) { caption.Text = T("单击复制 · 拖动排序或拖出"); caption.ToolTip = caption.Text; } };
        surface.MouseLeave += (_, _) => { if (!Busy) { caption.Text = item.Title; caption.ToolTip = item.Title; } };
        var menu = new ContextMenu();
        Add(menu, item.IsVideo ? T("复制录像文件") : T("复制图片"), Copy);
        Add(menu, item.IsVideo ? T("播放录像") : T("预览与缩放"), () => WorkspaceRequested?.Invoke(item, false));
        if (!item.IsVideo) Add(menu, T("编辑图片…"), () => WorkspaceRequested?.Invoke(item, true));
        Add(menu, item.IsVideo ? T("用默认应用打开") : T("打开图片"), () => ShellTransfer.Open(item.Path));
        Add(menu, T("在资源管理器中定位"), () => ShellTransfer.Reveal(item.Path));
        Add(menu, T("另存为"), () => ExportRequested?.Invoke(item, false));
        Add(menu, T("保存副本到桌面"), () => ExportRequested?.Invoke(item, true));
        menu.Items.Add(new Separator());
        Add(menu, T("取下（保留文件）"), () => TakeDown?.Invoke(this));
        Add(menu, T("取下左侧全部（跨页）"), () => BatchArchiveRequested?.Invoke(item, ArchiveScope.Left));
        Add(menu, T("取下右侧全部（跨页）"), () => BatchArchiveRequested?.Invoke(item, ArchiveScope.Right));
        Add(menu, T("全部取下到历史"), () => BatchArchiveRequested?.Invoke(item, ArchiveScope.All));
        Add(menu, T("将文件移入回收站"), () => { if (RecycleRequested is not null) RecycleRequested(item); else Report(ShellTransfer.Recycle(item.Path)); DragEnded?.Invoke(); });
        menu.Opened += (_, _) => { StopMotion(); Busy = true; clickTimer.Stop(); holdTimer.Stop(); };
        menu.Closed += (_, _) => Busy = false;
        surface.ContextMenu = menu;
        Closed += (_, _) => { closed = true; StopMotion(); clickTimer.Stop(); holdTimer.Stop(); };
    }
    public override void Present(PixelRect rect, bool animate)
    {
        if (Retiring || closed) return;
        var entering = !IsVisible;
        base.Present(rect, animate && !InteractionActive);
        if (entering && animate && !InteractionActive && !IsMouseOver) motion.Enter();
    }
    public override void StopMotion() { base.StopMotion(); motion.Stop(); }
    internal void SetStackEmphasis(PixelRect bounds, bool emphasized, bool animate)
    {
        StopMotion();
        surface.BorderBrush = DemoAssets.Brush(emphasized ? "#709780" : "#D6DED6");
        Native.Place(this, bounds, emphasized);
        if (emphasized && animate) motion.Emphasize();
    }
    public override void Dismiss()
    {
        if (closed) return;
        if (Retiring) { Close(); return; }
        CancelInteraction(); base.Dismiss();
    }
    private void CancelInteraction()
    {
        clickTimer.Stop(); holdTimer.Stop(); down = null; surface.ReleaseMouseCapture();
        if (surface.ContextMenu is not null) surface.ContextMenu.IsOpen = false;
    }
    public void Retire(bool animate)
    {
        if (Retiring || closed) return;
        Retiring = true; CancelInteraction(); StopMotion();
        if (!animate || !IsVisible || !Native.MakeInputTransparent(this)) { Close(); return; }
        IsHitTestVisible = false; motion.Leave();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => { if (!closed) Close(); };
        BeginAnimation(OpacityProperty, fade);
    }
    private void Add(ContextMenu menu, string title, Action action)
    {
        var entry = new MenuItem { Header = title };
        entry.Click += (_, _) => { try { action(); } catch (Exception ex) { Report(T("操作未完成：") + ex.Message); } };
        menu.Items.Add(entry);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        StopMotion();
        clickTimer.Stop(); holdTimer.Stop();
        if (e.ClickCount == 2)
        {
            down = null;
            try { ShellTransfer.Open(Item.Path); } catch (Exception ex) { Report(ex.Message); }
            e.Handled = true; return;
        }
        down = e.GetPosition(this); surface.CaptureMouse(); holdTimer.Start(); e.Handled = true;
    }
    private async void Move(object sender, MouseEventArgs e)
    {
        if (down is not Point start || e.LeftButton != MouseButtonState.Pressed || Busy) return;
        var p = e.GetPosition(this); // Keep the drag threshold in window DIPs, independent of the Viewbox size.
        if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        down = null; holdTimer.Stop(); surface.ReleaseMouseCapture(); clickTimer.Stop(); Busy = true;
        try { var result = DragRequested is null ? ShellTransfer.Drag(this, Item) : await DragRequested(this); if (!closed) Report(result); }
        catch (Exception ex) { if (!closed) Report(T("拖拽未完成：") + ex.Message); }
        finally { Busy = false; if (!closed) DragEnded?.Invoke(); }
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (down is null) return;
        down = null; holdTimer.Stop(); surface.ReleaseMouseCapture(); clickTimer.Start(); e.Handled = true;
    }
    private async void Copy()
    {
        if (copying || closed) return;
        if (!File.Exists(Item.Path)) { Report(T("原文件已移走，可在历史中查看失效记录。")); return; }
        copying = true;
        try
        {
            var data = await ShellTransfer.CopyDataAsync(Item.Path);
            if (closed) return;
            Clipboard.SetDataObject(data, true);
            Report(Item.IsVideo ? T("已复制录像文件") : T("已复制原图"));
        }
        catch (System.Runtime.InteropServices.COMException) { Report(T("剪贴板正在被占用，请稍后再试。")); }
        catch (Exception ex) { Report(T("操作未完成：") + ex.Message); }
        finally { copying = false; }
    }
    private void Report(string text) { feedback = text; caption.Text = feedback; Status?.Invoke(text); }
}
