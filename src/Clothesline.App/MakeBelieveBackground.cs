using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Clothesline.App;

internal readonly record struct BackgroundRenderState(bool Ready, bool Paused, int Frames, double Time);

internal sealed class MakeBelieveBackground : WebView2CompositionControl
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool initialized;
    private bool paused = true;

    internal Task Ready => ready.Task;
    internal string State { get; private set; } = "new";

    internal MakeBelieveBackground()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        IsHitTestVisible = false;
        Focusable = false;
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 94, 166, 229);
        Loaded += async (_, _) => await Initialize();
    }

    private async Task Initialize()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            await EnsureCoreWebView2Async(await DashboardWebViewEnvironment.Get());
            CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CoreWebView2.Settings.AreDevToolsEnabled = false;
            CoreWebView2.Settings.IsStatusBarEnabled = false;
            CoreWebView2.Settings.IsZoomControlEnabled = false;
            CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
            };
            CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (!message.RootElement.TryGetProperty("type", out var type)) return;
                if (type.GetString() == "background-error" ||
                    (type.GetString() == "background-ready" && message.RootElement.TryGetProperty("webgl", out var supported) && !supported.GetBoolean()))
                {
                    State = "unsupported";
                    ready.TrySetResult();
                    Visibility = Visibility.Collapsed;
                    return;
                }
                if (type.GetString() != "background-ready") return;
                State = "ready";
                ready.TrySetResult();
                PostPaused();
            };
            using var stream = typeof(MakeBelieveBackground).Assembly.GetManifestResourceStream("Cordnote.MakeBelieveBackground.html")
                ?? throw new InvalidOperationException("Missing animated desktop background.");
            using var reader = new StreamReader(stream);
            State = "navigating";
            NavigateToString(await reader.ReadToEndAsync());
        }
        catch (Exception error)
        {
            State = "failed: " + error.Message;
            ready.TrySetResult();
            Visibility = Visibility.Collapsed;
        }
    }

    internal void SetPaused(bool value)
    {
        paused = value;
        if (State == "ready") PostPaused();
    }

    private void PostPaused()
    {
        if (CoreWebView2 is null) return;
        try { CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "background-paused", paused })); }
        catch { }
    }

    internal async Task<BackgroundRenderState> RenderStateAsync()
    {
        if (await Task.WhenAny(ready.Task, Task.Delay(6000)) != ready.Task || CoreWebView2 is null || State != "ready")
            return new(false, true, 0, 0);
        try
        {
            var json = await CoreWebView2.ExecuteScriptAsync("window.cordnoteBackgroundState()");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new(true, root.GetProperty("paused").GetBoolean(), root.GetProperty("frames").GetInt32(), root.GetProperty("time").GetDouble());
        }
        catch { return new(false, true, 0, 0); }
    }
}
