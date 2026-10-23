using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Clothesline.App;

internal sealed class VideoWorkspace : Window
{
    private readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, ScrubbingEnabled = true, Volume = .5 };
    private readonly Slider seek = new() { Minimum = 0, IsEnabled = false, Margin = new Thickness(12), MinWidth = 180 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private readonly Button toggle = new() { Content = T("播放"), IsEnabled = false, Padding = new Thickness(18, 7, 18, 7) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer opening = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly IDisposable lease;
    private bool updating, playing, closed;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<bool> Ready => ready.Task;
    internal TimeSpan Position => player.Position;
    internal bool IsPlaying => playing;
    internal bool TimerRunning => timer.IsEnabled;
    public VideoWorkspace(string path, Func<string, IDisposable> acquireLease)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("原文件已失效，无法打开。请在历史中检查路径。"), path);
        Title = T("录像预览") + " · " + Path.GetFileName(path); Width = 960; Height = 640; MinWidth = 620; MinHeight = 420;
        Background = DemoAssets.Brush("#F4F5EE"); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel();
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(seek);
        var controls = new WrapPanel { Margin = new Thickness(12, 0, 12, 0) }; controls.Children.Add(toggle);
        var mute = new CheckBox { Content = T("静音"), Margin = new Thickness(16, 8, 16, 8) };
        mute.Checked += (_, _) => player.IsMuted = true; mute.Unchecked += (_, _) => player.IsMuted = false; controls.Children.Add(mute);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = .5, Width = 120, Margin = new Thickness(0, 8, 16, 8) };
        volume.ValueChanged += (_, _) => player.Volume = volume.Value; controls.Children.Add(volume);
        AutomationProperties.SetName(volume, T("音量")); AutomationProperties.SetName(seek, T("播放进度"));
        var external = new Button { Content = T("用默认应用打开"), Padding = new Thickness(12, 7, 12, 7) };
        external.Click += (_, _) => { try { ShellTransfer.Open(path); } catch (Exception ex) { status.Text = ex.Message; } }; controls.Children.Add(external);
        footer.Children.Add(controls); footer.Children.Add(status); root.Children.Add(footer);
        root.Children.Add(new Border { Background = Brushes.Black, Child = player }); Content = root;
        status.Text = T("正在打开录像…");
        toggle.Click += (_, _) => TogglePlayback();
        seek.ValueChanged += (_, _) => { if (!updating && seek.IsEnabled) player.Position = TimeSpan.FromSeconds(seek.Value); };
        player.MediaOpened += (_, _) =>
        {
            if (closed) return; opening.Stop();
            if (!player.NaturalDuration.HasTimeSpan || player.NaturalDuration.TimeSpan <= TimeSpan.Zero || player.NaturalVideoWidth == 0) { Fail(T("录像没有可播放的视频轨道。")); return; }
            seek.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds; seek.IsEnabled = toggle.IsEnabled = true;
            timer.Start(); UpdatePosition(); ready.TrySetResult(true);
        };
        player.MediaFailed += (_, e) => Fail(T("系统无法播放此录像；可用默认应用打开。") + " " + e.ErrorException.Message);
        player.MediaEnded += (_, _) => { Pause(); Seek(0); };
        timer.Tick += (_, _) => UpdatePosition();
        opening.Tick += (_, _) => Fail(T("打开录像超时；可用默认应用打开。"));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not (ButtonBase or RangeBase)) { TogglePlayback(); e.Handled = true; }
        };
        lease = acquireLease(path);
        Loaded += (_, _) => { try { player.Source = new Uri(Path.GetFullPath(path)); player.Pause(); opening.Start(); } catch (Exception ex) { Fail(ex.Message); } };
        Closed += (_, _) => { closed = true; timer.Stop(); opening.Stop(); player.Close(); ready.TrySetResult(false); lease.Dispose(); };
    }
    internal void TogglePlayback()
    {
        if (!toggle.IsEnabled || closed) return;
        if (playing) Pause(); else { player.Play(); playing = true; toggle.Content = T("暂停"); }
    }
    internal void Pause() { player.Pause(); playing = false; toggle.Content = T("播放"); }
    internal void Seek(double seconds) { if (seek.IsEnabled) { player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, seek.Maximum)); UpdatePosition(); } }
    private void UpdatePosition()
    {
        if (!seek.IsEnabled) return;
        updating = true; if (!seek.IsMouseCaptureWithin) seek.Value = player.Position.TotalSeconds; updating = false;
        status.Text = TimeSpan.FromSeconds(seek.Value).ToString(@"hh\:mm\:ss") + " / " + TimeSpan.FromSeconds(seek.Maximum).ToString(@"hh\:mm\:ss");
    }
    private void Fail(string message)
    {
        if (closed) return;
        opening.Stop(); timer.Stop(); player.Close(); playing = false; seek.IsEnabled = toggle.IsEnabled = false;
        status.Text = message; ready.TrySetResult(false);
    }
}
