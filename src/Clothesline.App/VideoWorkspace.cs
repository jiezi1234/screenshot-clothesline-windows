using System;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Clothesline.Core;
using Clothesline.Storage;

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
    private readonly TextBox startTime = new() { Text = "0", Width = 90, Margin = new Thickness(6) };
    private readonly TextBox endTime = new() { Text = "0", Width = 90, Margin = new Thickness(6) };
    private readonly Button export = new() { Content = T("导出片段并挂上…"), IsEnabled = false, Padding = new Thickness(12, 7, 12, 7) };
    private readonly Button cancel = new() { Content = T("取消导出"), Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock exportStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 8) };
    private readonly WrapPanel trimControls = new() { Margin = new Thickness(12, 0, 12, 0), IsEnabled = false };
    private readonly Func<VideoTrimRequest, Window, IProgress<double>, CancellationToken, Task<SavedVideo?>>? save;
    private readonly string sourcePath, sourceSignature;
    private CancellationTokenSource? exporting;
    private bool closeAfterExport, previewSegment;
    private bool updating, playing, closed;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<bool> Ready => ready.Task;
    internal TimeSpan Position => player.Position;
    internal bool IsPlaying => playing;
    internal bool TimerRunning => timer.IsEnabled;
    internal bool Exporting => exporting is not null;
    public VideoWorkspace(string path, Func<string, IDisposable> acquireLease, Func<VideoTrimRequest, Window, IProgress<double>, CancellationToken, Task<SavedVideo?>>? save = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("原文件已失效，无法打开。请在历史中检查路径。"), path);
        sourcePath = path; sourceSignature = CollectionEngine.Signature(path); this.save = save;
        Title = T("录像预览与裁剪") + " · " + Path.GetFileName(path); Width = 960; Height = 720; MinWidth = 740; MinHeight = 560;
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
        footer.Children.Add(controls); footer.Children.Add(status);
        trimControls.Children.Add(new TextBlock { Text = T("开始（秒）"), VerticalAlignment = VerticalAlignment.Center }); trimControls.Children.Add(startTime);
        AddTrimButton(T("当前为起点"), () => SetRange(player.Position.TotalSeconds, Range().End));
        trimControls.Children.Add(new TextBlock { Text = T("结束（秒）"), VerticalAlignment = VerticalAlignment.Center }); trimControls.Children.Add(endTime);
        AddTrimButton(T("当前为终点"), () => SetRange(Range().Start, player.Position.TotalSeconds));
        AddTrimButton(T("预览片段"), PreviewSegment); AddTrimButton(T("重置选区"), () => SetRange(0, seek.Maximum));
        AutomationProperties.SetName(startTime, T("裁剪开始时间（秒）")); AutomationProperties.SetName(endTime, T("裁剪结束时间（秒）"));
        startTime.TextChanged += (_, _) => ValidateRange(); endTime.TextChanged += (_, _) => ValidateRange();
        footer.Children.Add(trimControls);
        var saving = new WrapPanel { Margin = new Thickness(12, 8, 12, 0) }; saving.Children.Add(export); saving.Children.Add(cancel); footer.Children.Add(saving);
        footer.Children.Add(new TextBlock { Text = T("导出新的 H.264/AAC MP4 副本，保留原录像；精确裁剪会重新编码。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 0), FontSize = 11 });
        footer.Children.Add(exportStatus); root.Children.Add(footer);
        root.Children.Add(new Border { Background = Brushes.Black, Child = player }); Content = root;
        UiAccessibility.Install(this);
        // Playback time updates four times a second; only export results are live announcements.
        UiAccessibility.LiveStatus(this, exportStatus);
        status.Text = T("正在打开录像…");
        toggle.Click += (_, _) => TogglePlayback();
        export.Click += async (_, _) => await ExportSelection(); cancel.Click += (_, _) => exporting?.Cancel();
        seek.ValueChanged += (_, _) => { if (!updating && seek.IsEnabled) player.Position = TimeSpan.FromSeconds(seek.Value); };
        player.MediaOpened += (_, _) =>
        {
            if (closed) return; opening.Stop();
            if (!player.NaturalDuration.HasTimeSpan || player.NaturalDuration.TimeSpan <= TimeSpan.Zero || player.NaturalVideoWidth == 0) { Fail(T("录像没有可播放的视频轨道。")); return; }
            seek.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds; seek.IsEnabled = toggle.IsEnabled = true;
            trimControls.IsEnabled = true; SetRange(0, seek.Maximum);
            timer.Start(); UpdatePosition(); ready.TrySetResult(true);
        };
        player.MediaFailed += (_, e) => Fail(T("系统无法播放此录像；可用默认应用打开。") + " " + e.ErrorException.Message);
        player.MediaEnded += (_, _) =>
        {
            var end = 0.0; if (previewSegment) { try { end = Range().End; } catch (ArgumentException) { } }
            Pause(); Seek(end);
        };
        timer.Tick += (_, _) => UpdatePosition();
        opening.Tick += (_, _) => Fail(T("打开录像超时；可用默认应用打开。"));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not (ButtonBase or RangeBase or TextBoxBase)) { TogglePlayback(); e.Handled = true; }
        };
        lease = acquireLease(path);
        Loaded += (_, _) => { try { player.Source = new Uri(Path.GetFullPath(path)); player.Pause(); opening.Start(); } catch (Exception ex) { Fail(ex.Message); } };
        Closing += (_, e) => { if (Exporting) { e.Cancel = true; closeAfterExport = true; exporting!.Cancel(); } };
        Closed += (_, _) => { closed = true; timer.Stop(); opening.Stop(); player.Close(); ready.TrySetResult(false); lease.Dispose(); };
    }
    private void AddTrimButton(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(7, 4, 7, 4) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { exportStatus.Text = ex.Message; } }; trimControls.Children.Add(button);
    }
    internal VideoTrimRange Range()
    {
        if (!double.TryParse(startTime.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var start) || !double.TryParse(endTime.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
            throw new ArgumentException(T("请输入有效的秒数，可使用小数点。"));
        return VideoTrimRange.Create(start, end, seek.Maximum);
    }
    internal void SetRange(double start, double end)
    {
        startTime.Text = start.ToString("0.###", CultureInfo.InvariantCulture); endTime.Text = end.ToString("0.###", CultureInfo.InvariantCulture); ValidateRange();
    }
    private void ValidateRange()
    {
        if (Exporting || !seek.IsEnabled) return;
        try { var range = Range(); export.IsEnabled = save is not null; exportStatus.Text = T("保留 {0:0.###} 秒", range.Seconds); }
        catch (ArgumentException ex) { export.IsEnabled = false; exportStatus.Text = ex.Message; }
    }
    internal void PreviewSegment()
    {
        var range = Range(); Pause(); player.Play(); Seek(range.Start); previewSegment = true; playing = true; toggle.Content = T("暂停");
    }
    internal async Task ExportSelection()
    {
        if (save is null || Exporting || !export.IsEnabled) return;
        var range = Range(); Pause(); exporting = new CancellationTokenSource(); export.IsEnabled = false; trimControls.IsEnabled = false; cancel.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<double>(value => { if (!closed && Exporting) exportStatus.Text = T("正在导出并验证… {0:0}%", value); });
            var saved = await save(new(sourcePath, sourceSignature, range), this, progress, exporting.Token);
            exportStatus.Text = saved is null ? T("已取消选择保存位置。") : (saved.Hung ? T("片段已保存并挂上：") : T("副本已保存，挂上未完成：")) + saved.Path + (saved.Warning.Length > 0 ? "\n" + saved.Warning : "");
        }
        catch (OperationCanceledException) { exportStatus.Text = T("导出已取消，原录像保留。" ); }
        catch (Exception ex) { exportStatus.Text = T("录像导出失败：") + ex.Message; }
        finally
        {
            exporting.Dispose(); exporting = null; cancel.Visibility = Visibility.Collapsed; trimControls.IsEnabled = seek.IsEnabled;
            export.IsEnabled = seek.IsEnabled && save is not null; if (closeAfterExport) Close();
        }
    }
    internal void TogglePlayback()
    {
        if (!toggle.IsEnabled || closed) return;
        if (playing) Pause(); else { player.Play(); playing = true; toggle.Content = T("暂停"); }
    }
    internal void Pause() { player.Pause(); playing = false; previewSegment = false; toggle.Content = T("播放"); }
    internal void Seek(double seconds) { if (seek.IsEnabled) { player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, seek.Maximum)); UpdatePosition(); } }
    private void UpdatePosition()
    {
        if (!seek.IsEnabled) return;
        if (previewSegment) { try { if (player.Position.TotalSeconds >= Range().End) { var end = Range().End; Pause(); Seek(end); } } catch (ArgumentException) { Pause(); } }
        updating = true; if (!seek.IsMouseCaptureWithin) seek.Value = player.Position.TotalSeconds; updating = false;
        status.Text = TimeSpan.FromSeconds(seek.Value).ToString(@"hh\:mm\:ss") + " / " + TimeSpan.FromSeconds(seek.Maximum).ToString(@"hh\:mm\:ss");
    }
    private void Fail(string message)
    {
        if (closed) return;
        opening.Stop(); timer.Stop(); player.Close(); playing = false; seek.IsEnabled = toggle.IsEnabled = trimControls.IsEnabled = export.IsEnabled = false;
        status.Text = message; ready.TrySetResult(false);
    }
}
