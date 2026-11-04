using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clothesline.App;

internal sealed class WeChatScreenshotCollector : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly Func<string, CancellationToken, Task> accept;
    private readonly Func<bool> weChatRunning;
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource closing = new();
    private volatile bool enabled;

    public event Action<string>? Status;

    public WeChatScreenshotCollector(Func<string, CancellationToken, Task> accept, string? folder = null, Func<bool>? weChatRunning = null)
    {
        this.accept = accept;
        this.weChatRunning = weChatRunning ?? IsWeChatRunning;
        folder ??= Path.GetTempPath();
        watcher = new FileSystemWatcher(folder, "ScreenShot_*.png")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 16384
        };
        watcher.Created += (_, e) => Queue(e.FullPath);
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => Queue(e.FullPath);
        watcher.Error += (_, _) => Status?.Invoke(T("微信截图目录监听中断；重新启用收集或重启应用后恢复。"));
    }

    public void Enable(bool value)
    {
        enabled = value;
        watcher.EnableRaisingEvents = value;
    }

    internal static bool Matches(string path)
        => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).StartsWith("ScreenShot_", StringComparison.OrdinalIgnoreCase);

    private void Queue(string path)
    {
        if (!enabled || !Matches(path) || !weChatRunning() || !pending.TryAdd(path, 0)) return;
        _ = Collect(path);
    }

    private async Task Collect(string path)
    {
        Exception? last = null;
        try
        {
            for (var attempt = 0; attempt < 10 && enabled && !closing.IsCancellationRequested; attempt++)
            {
                try
                {
                    await Task.Delay(100 + Math.Min(700, attempt * 100), closing.Token).ConfigureAwait(false);
                    if (!File.Exists(path)) return;
                    var before = Signature(path);
                    await Task.Delay(80, closing.Token).ConfigureAwait(false);
                    if (before != Signature(path)) continue;
                    await accept(path, closing.Token).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    last = ex;
                }
            }
            if (last is not null && enabled) Status?.Invoke(T("微信截图暂未收集：") + last.Message);
        }
        catch (OperationCanceledException) { }
        finally { pending.TryRemove(path, out _); }
    }

    private static string Signature(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private static bool IsWeChatRunning()
    {
        foreach (var name in new[] { "Weixin", "WeChat" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Any(p => !p.HasExited)) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    public void Dispose()
    {
        enabled = false;
        watcher.EnableRaisingEvents = false;
        closing.Cancel();
        watcher.Dispose();
    }
}
