using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Clothesline.App;

internal sealed class GithubHoverCard : WebView2CompositionControl
{
    private static readonly Lazy<Task<CoreWebView2Environment>> environment = new(() => CoreWebView2Environment.CreateAsync(
        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "WebView2")));
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool initialized;
    private bool? pendingOpen;
    internal Task Ready => ready.Task;
    internal string State { get; private set; } = "new";
    internal Rect CardBounds { get; private set; }
    internal event Action? CardBoundsChanged;
    internal event Action? ProfileRequested;
    internal event Action? PointerEntered;
    internal event Action? PointerLeft;

    internal GithubHoverCard()
    {
        Width = 336; Height = 400;
        DefaultBackgroundColor = System.Drawing.Color.Transparent;
        Loaded += async (_, _) => await Initialize();
    }

    private async Task Initialize()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            await EnsureCoreWebView2Async(await environment.Value);
            CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CoreWebView2.Settings.IsStatusBarEnabled = false;
            CoreWebView2.Settings.IsZoomControlEnabled = false;
            CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (e.Uri != "about:blank") ProfileRequested?.Invoke(); };
            CoreWebView2.NavigationStarting += (_, e) => { if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return; e.Cancel = true; ProfileRequested?.Invoke(); };
            CoreWebView2.NavigationCompleted += (_, e) => { if (State == "navigating") State = "navigation " + (e.IsSuccess ? "ok" : e.WebErrorStatus.ToString()); };
            CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (!message.RootElement.TryGetProperty("type", out var type)) return;
                switch (type.GetString())
                {
                    case "open-profile": ProfileRequested?.Invoke(); break;
                    case "card-enter": PointerEntered?.Invoke(); break;
                    case "card-leave": PointerLeft?.Invoke(); break;
                    case "ready":
                        State = "ready";
                        ready.TrySetResult();
                        if (pendingOpen is bool open) PostOpen(open);
                        break;
                    case "script-start": State = "script-start"; break;
                    case "script-error": State = "script-error: " + (message.RootElement.TryGetProperty("message", out var text) ? text.GetString() : ""); break;
                    case "card-bounds":
                        var bounds = message.RootElement;
                        if (bounds.TryGetProperty("left", out var left) && bounds.TryGetProperty("top", out var top) && bounds.TryGetProperty("width", out var width) && bounds.TryGetProperty("height", out var height))
                        {
                            CardBounds = new Rect(left.GetDouble(), top.GetDouble(), width.GetDouble(), height.GetDouble());
                            CardBoundsChanged?.Invoke();
                        }
                        break;
                }
            };
            using var stream = typeof(GithubHoverCard).Assembly.GetManifestResourceStream("Cordnote.GithubWeb.html")
                ?? throw new InvalidOperationException("Missing GitHub card page.");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            State = "navigating";
            NavigateToString(html);
        }
        catch (Exception ex)
        {
            State = "init-failed: " + ex.Message;
            ready.TrySetResult();
        }
    }

    internal void SetOpen(bool open)
    {
        pendingOpen = open;
        if (State == "ready") PostOpen(open);
    }

    private void PostOpen(bool open)
    {
        if (CoreWebView2 is null) return;
        try { CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "card-open", open })); }
        catch { }
    }

    internal async Task<bool> CaptureAsync(string path)
    {
        try
        {
            if (await Task.WhenAny(ready.Task, Task.Delay(6000)) != ready.Task) { State = "capture-timeout"; return false; }
            if (CoreWebView2 is null) { State = "no-webview"; return false; }
            using var stream = File.Create(path);
            var capture = CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (await Task.WhenAny(capture, Task.Delay(4000)) != capture) { State = "capture-timeout"; return false; }
            await capture;
            State = "captured";
            return true;
        }
        catch (Exception ex) { State = "capture-failed: " + ex.Message; return false; }
    }
}
