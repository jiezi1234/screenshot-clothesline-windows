using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Clothesline.App;

internal sealed class Dashboard : Window
{
    private readonly TextBlock status;
    private readonly TextBlock metrics;
    private readonly DispatcherTimer timer;
    public bool Exiting { get; set; }
    public Dashboard(ShelfController controller, bool shortcutRegistered, Action quit)
    {
        Title = "Screenshot Clothesline · 阶段 0"; Width = 790; Height = 650;
        MinWidth = 690; MinHeight = 610; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = DemoAssets.Brush("#F4F5EE"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new Grid { Margin = new Thickness(30, 25, 30, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(Label("SCREENSHOT CLOTHESLINE     /     WINDOWS PROTOTYPE", 11, "#648070"));
        header.Children.Add(Label("把灵感，晾在手边。", 32, "#213D30", 10));
        header.Children.Add(Label("阶段 0 · 桌面交互实验     Windows 11 / x64", 13, "#718276", 8));
        root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(0, 24, 0, 0) }; Grid.SetRow(body, 1);
        var tip = new Border { Background = DemoAssets.Brush("#E3EBE0"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
        var instructions = new StackPanel();
        instructions.Children.Add(Label("屏幕顶部中央停留片刻，即可展开晾衣绳", 17, "#294C38"));
        instructions.Children.Add(Label(shortcutRegistered ? "Ctrl + Alt + T  显示 / 收起 · 按鼠标所在显示器定位" : "Ctrl + Alt + T 注册失败（可能已被占用），请使用下方按钮或托盘菜单。", 12, "#54715D", 8));
        instructions.Children.Add(Label("单击复制原图 · 双击打开 · 拖到其他应用 · 右键更多操作", 12, "#54715D", 7));
        tip.Child = instructions; body.Children.Add(tip);
        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(Button("显示 / 收起晾衣绳", () => controller.Toggle(), true));
        actions.Children.Add(Button("恢复 3 张示例", controller.ResetSamples));
        actions.Children.Add(Button("打开示例文件夹", () => ShellTransfer.Open(DemoAssets.Folder)));
        body.Children.Add(actions);
        var options = new StackPanel { Margin = new Thickness(1, 16, 0, 12) };
        var full = new CheckBox { Content = "全屏时自动收起", IsChecked = true, Margin = new Thickness(0, 0, 0, 10), Foreground = DemoAssets.Brush("#355240") };
        full.Checked += (_, _) => controller.SuppressFullscreen = true; full.Unchecked += (_, _) => controller.SuppressFullscreen = false;
        var motion = new CheckBox { Content = "减少动画", Foreground = DemoAssets.Brush("#355240") };
        motion.Checked += (_, _) => controller.ReduceMotion = true; motion.Unchecked += (_, _) => controller.ReduceMotion = false;
        options.Children.Add(full); options.Children.Add(motion); body.Children.Add(options);
        status = Label("准备就绪。当前仅使用程序生成的示例图片。", 13, "#354D3D", 7);
        status.TextWrapping = TextWrapping.Wrap; status.MinHeight = 42; body.Children.Add(status);
        metrics = Label("", 11, "#7B887E", 10); body.Children.Add(metrics);
        var footer = new StackPanel(); Grid.SetRow(footer, 2);
        footer.Children.Add(Label("示例模式：取下保留文件；恢复会生成新样例。尚未接入截图收集与历史。", 11, "#7B887E"));
        var bottom = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        bottom.Children.Add(Button("收起控制面板", Hide)); bottom.Children.Add(Button("退出原型", quit));
        footer.Children.Add(bottom); root.Children.Add(body); root.Children.Add(footer); Content = root;
        controller.Status += SetStatus;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { if (IsVisible) { using var process = Process.GetCurrentProcess(); metrics.Text = $"{controller.Count} 张示例  ·  {Native.Monitors().Count} 个显示器  ·  工作集 {process.WorkingSet64 / 1048576.0:F1} MB  ·  最近显示调用 {controller.LastShowMilliseconds:F1} ms"; } };
        timer.Start();
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { timer.Stop(); controller.Status -= SetStatus; };
    }
    public void SetStatus(string text) => status.Text = text;
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    private static TextBlock Label(string text, double size, string color, double top = 0) => new() { Text = text, FontSize = size, Foreground = DemoAssets.Brush(color), Margin = new Thickness(0, top, 0, 0) };
    private System.Windows.Controls.Button Button(string title, Action action, bool primary = false)
    {
        var button = new System.Windows.Controls.Button { Content = title, Padding = new Thickness(15, 10, 15, 10), Margin = new Thickness(0, 0, 10, 0), Background = DemoAssets.Brush(primary ? "#315B43" : "#E8ECE3"), Foreground = DemoAssets.Brush(primary ? "#FFFFFF" : "#37503E"), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } };
        return button;
    }
}
