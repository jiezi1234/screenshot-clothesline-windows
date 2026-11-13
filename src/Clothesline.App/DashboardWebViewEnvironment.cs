using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Clothesline.App;

internal static class DashboardWebViewEnvironment
{
    private static readonly Lazy<Task<CoreWebView2Environment>> environment = new(() => CoreWebView2Environment.CreateAsync(
        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotClothesline", "WebView2")));

    internal static Task<CoreWebView2Environment> Get() => environment.Value;
}
