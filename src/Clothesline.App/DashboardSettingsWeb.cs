using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Clothesline.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private readonly WebView2CompositionControl settingsWeb = new() { Visibility = Visibility.Hidden, IsHitTestVisible = false, Focusable = false };
    private readonly TaskCompletionSource<bool> settingsLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool webConnected, webClosed;
    private int draftVersion;
    private DashboardPage? requestedSection;

    private async Task StartSettingsWeb()
    {
        try
        {
            await settingsWeb.EnsureCoreWebView2Async(await DashboardWebViewEnvironment.Get());
            if (webClosed) return;
            var core = settingsWeb.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // NavigateToString starts a data: navigation and gives the loaded document an about:blank origin.
            bool initialNavigation = true;
            core.NavigationStarting += (_, e) =>
            {
                e.Cancel = !initialNavigation || !(e.Uri == "about:blank" || e.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase));
                initialNavigation = false;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            RegisterThumbnailRequests(core);
            core.ProcessFailed += (_, _) => ShowNativeSettings();
            core.WebMessageReceived += OnSettingsMessage;
            using var stream = typeof(Dashboard).Assembly.GetManifestResourceStream("Cordnote.SettingsWeb.html")!;
            using var reader = new StreamReader(stream);
            core.NavigateToString(reader.ReadToEnd());
        }
        catch (Exception) { if (!webClosed) ShowNativeSettings(); }
    }

    private void ShowNativeSettings()
    {
        webConnected = false;
        settingsWeb.Visibility = Visibility.Collapsed;
        SetNativePanels(Visibility.Visible);
        settingsLoaded.TrySetResult(false);
    }

    private void SetNativePanels(Visibility visibility)
    {
        nativeSettings.Visibility = sections[DashboardPage.Shelf].Visibility = sections[DashboardPage.History].Visibility = visibility;
    }

    private void PostSettings(object message)
    {
        if (webConnected && !webClosed) settingsWeb.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    private void SendSettingsDraft()
    {
        draftVersion++;
        PostSettings(new { type = "draft", version = draftVersion, values = settingsById.ToDictionary(pair => pair.Key, pair => pair.Value.Value) });
    }

    private void SendSettingInfo()
    {
        var shortcutState = "unavailable";
        if (library?.Desktop is { } desktop)
        {
            shortcutState = desktop.ShortcutRegistrationFailed ? "failed" : "registered";
            if (library.Settings.ShowShortcut.Length == 0 && library.Settings.HideShortcut.Length == 0) shortcutState = "off";
        }
        PostSettings(new { type = "info", values = new { capacity = capacity.Text, capacityCount = library?.Capacity, shortcutStatus = interactionHint.Text, shortcutState } });
    }

    private void OnSettingsMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (webClosed || e.Source != "about:blank") return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var msg = document.RootElement;
            switch (msg.GetProperty("type").GetString())
            {
                case "ready":
                    webConnected = true;
                    PostSettings(new { type = "init", lang = UiText.Language, browse = T("选择…"), groups = settingsGroups.Select(g => new {
                        id = g.Id, title = g.Title, fields = g.Fields.Select(f => new { id = f.Id, label = f.Label, help = f.Help, kind = f.Kind, choices = f.Choices }) }) });
                    SendSettingsDraft(); SendSettingInfo(); SendDashboardMedia();
                    break;
                case "mounted":
                    SetNativePanels(Visibility.Collapsed);
                    settingsWeb.Visibility = Visibility.Visible;
                    settingsWeb.IsHitTestVisible = settingsWeb.Focusable = true;
                    settingsLoaded.TrySetResult(true);
                    if (requestedSection is { } page) { requestedSection = null; Navigate(page); }
                    break;
                case "media": RunMediaAction(msg); break;
                case "text-preview": _ = SendTextPreview(msg); break;
                case "hide": Hide(); break;
                case "edit":
                case "activate":
                    if (msg.GetProperty("version").GetInt32() != draftVersion || !settingsById.TryGetValue(msg.GetProperty("id").GetString() ?? "", out var field)) return;
                    if (msg.GetProperty("type").GetString() == "activate")
                    {
                        if (field.Folder && field.Control is TextBox folder && ChooseFolder(folder))
                            PostSettings(new { type = "info", values = new Dictionary<string, string> { [field.Id] = folder.Text } });
                        else if (field.Control is Button button) button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        return;
                    }
                    var value = msg.GetProperty("value");
                    switch (field.Control)
                    {
                        case TextBox box: box.Text = value.GetString() ?? ""; break;
                        case CheckBox check: check.IsChecked = value.GetBoolean(); break;
                        case ComboBox choice when value.TryGetInt32(out var index) && index >= 0 && index < choice.Items.Count:
                            choice.SelectedIndex = index; break;
                    }
                    break;
            }
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    internal Task<bool> SettingsReady => settingsLoaded.Task;
    internal Task<string> InspectSettings(string script) => settingsWeb.CoreWebView2.ExecuteScriptAsync(script);
}
