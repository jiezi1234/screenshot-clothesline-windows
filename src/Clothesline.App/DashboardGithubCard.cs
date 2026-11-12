using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Clothesline.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Clothesline.App;

// Native host only. React/Motion/Tailwind render the unchanged upstream TSX.
internal sealed class DashboardGithubCard : WebView2CompositionControl
{
    internal const string SourceHash = "464f2bcd45bec1aa08d5c6a2287d569dd532beecfe8d91d43e065f2f07e55abc";
    private const string Profile = "https://github.com/jiezi1234";
    private static readonly Lazy<Task<CoreWebView2Environment>> environment = new(() => CoreWebView2Environment.CreateAsync(
        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "WebView2")));
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string> reportError;
    private bool initialized;
    internal Task Ready => ready.Task;

    internal static DashboardGithubCard Create(Action<string> reportError) => new(reportError);
    private DashboardGithubCard(Action<string> reportError)
    {
        this.reportError = reportError;
        Width = 188; Height = 32;
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(30, 0, 0, 18);
        DefaultBackgroundColor = System.Drawing.Color.Transparent;
        AutomationProperties.SetName(this, "Great UI GitHub Card");
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
            CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; OpenProfile(e.Uri); };
            CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (e.Uri == "about:blank") return;
                e.Cancel = true; OpenProfile(e.Uri);
            };
            CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (!message.RootElement.TryGetProperty("type", out var type)) return;
                if (type.GetString() == "github-size" && message.RootElement.TryGetProperty("expanded", out var value))
                {
                    var expanded = value.ValueKind == JsonValueKind.True;
                    Width = expanded ? 440 : 188; Height = expanded ? 360 : 32;
                }
                if (type.GetString() == "github-ready" && message.RootElement.GetProperty("sourceHash").GetString() == SourceHash) ready.TrySetResult();
            };
            var window = Window.GetWindow(this);
            if (window is not null) window.Closed += (_, _) => Dispose();
            using var stream = typeof(DashboardGithubCard).Assembly.GetManifestResourceStream("Cordnote.GithubWeb.html")
                ?? throw new InvalidOperationException("Missing compiled Great UI component.");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            var config = "<script>window.cordnoteLabel=" + JsonSerializer.Serialize(UiText.T("关注我 ·")) + ";</script>";
            NavigateToString(html.Replace("<head>", "<head>" + config, StringComparison.Ordinal));
        }
        catch (Exception error)
        {
            ready.TrySetException(error);
            reportError("GitHub: " + error.Message);
        }
    }

    private void OpenProfile(string uri)
    {
        if (!string.Equals(uri.TrimEnd('/'), Profile, StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(Profile) { UseShellExecute = true }); }
        catch (Exception error) { reportError(error.Message); }
    }
}
