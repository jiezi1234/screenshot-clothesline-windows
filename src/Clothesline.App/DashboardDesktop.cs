using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private DashboardPage currentSection = DashboardPage.Sources;

    private FrameworkElement CreateDesktop(ShelfController controller, Action quit)
    {
        var desk = new Grid { Background = Brush("CanvasBrush"), ClipToBounds = true };
        desk.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        desk.RowDefinitions.Add(new RowDefinition());
        desk.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Button HeaderAction(string name, string glyph, string? label, Action run, string style)
        {
            var button = Button(name, run, style);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(icon);
            if (label is not null) content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            button.Content = content; button.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Height = 29; button.MinHeight = 29; button.Margin = new Thickness(2, 0, 0, 0);
            button.Padding = new Thickness(label is null ? 9 : 12, 0, label is null ? 9 : 12, 0);
            return button;
        }
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        pauseCollection.Height = pauseCollection.MinHeight = 29;
        pauseCollection.Padding = new Thickness(8, 0, 8, 0);
        pauseCollection.Margin = new Thickness(0);
        commands.Children.Add(pauseCollection);
        commands.Children.Add(HeaderAction(T("显示 / 收起"), "\uE890", null, controller.Toggle, "QuietButton"));
        commands.Children.Add(HeaderAction(T("导入图片或录像…"), "\uE710", T("导入"), Import, "PrimaryButton"));
        commands.Children.Add(new Border { Background = Brush("LineBrush"), Width = 1, Height = 16, Margin = new Thickness(8, 0, 4, 0) });
        commands.Children.Add(HeaderAction(T("收起控制面板"), "\uE921", null, Hide, "QuietButton"));
        commands.Children.Add(HeaderAction(T("退出"), "\uE8BB", null, quit, "QuietButton"));
        var titleBar = new Border { Child = commands, Padding = new Thickness(12, 5, 10, 5), Background = Brush("SurfaceBrush"), BorderBrush = Brush("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBar.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
        desk.Children.Add(titleBar);

        var body = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star), MaxHeight = 430 });
        body.RowDefinitions.Add(new RowDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumnSpan(nativeSettings, 2); body.Children.Add(nativeSettings);

        void AddLibrary(DashboardPage page, FrameworkElement panel, int column)
        {
            var surface = Card(titles[(int)page], panel);
            surface.Margin = new Thickness(column == 0 ? 2 : 5, 8, column == 0 ? 5 : 2, 6);
            sections.Add(page, surface);
            Grid.SetRow(surface, 1); Grid.SetColumn(surface, column); body.Children.Add(surface);
        }
        AddLibrary(DashboardPage.Shelf, ShelfListPanel(), 0);
        AddLibrary(DashboardPage.History, HistoryPanel(), 1);
        Grid.SetColumnSpan(settingsWeb, 2); Grid.SetRowSpan(settingsWeb, 2);
        Panel.SetZIndex(settingsWeb, 1); body.Children.Add(settingsWeb);
        var formats = new ImageFormatPanel(compact: true);
        sections.Add(DashboardPage.Formats, formats);
        Grid.SetRow(body, 1); desk.Children.Add(body);

        var github = Button("GitHub", ShowGithubCard, "QuietButton");
        var donate = Button(T("捐赠"), ShowDonateCard, "QuietButton");
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(github); links.Children.Add(donate);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(discard); actions.Children.Add(save);
        foreach (var button in new[] { github, donate, discard, save })
        {
            button.MinHeight = 28; button.Margin = new Thickness(0, 0, 3, 0);
            button.Padding = new Thickness(8, 2, 8, 2);
        }
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 12, 0) };
        draftStatus.FontSize = 12; draftStatus.Foreground = Brush("AccentBrush");
        status.FontSize = 12; status.Foreground = Brush("MutedBrush"); status.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(draftStatus); info.Children.Add(status);
        var footer = new DockPanel(); DockPanel.SetDock(links, Dock.Left); DockPanel.SetDock(formats, Dock.Left); DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(links); footer.Children.Add(formats); footer.Children.Add(actions); footer.Children.Add(info);
        saveBar.Child = footer; saveBar.Background = Brush("SurfaceBrush"); saveBar.BorderBrush = Brush("LineBrush");
        saveBar.BorderThickness = new Thickness(0, 1, 0, 0); saveBar.Padding = new Thickness(8, 5, 7, 5);
        Grid.SetRow(saveBar, 2); desk.Children.Add(saveBar);

        CreateHoverCards(github, donate, desk);
        Loaded += async (_, _) => await StartSettingsWeb();
        Closed += (_, _) => { webClosed = true; mediaClosing.Cancel(); settingsWeb.Dispose(); mediaClosing.Dispose(); };
        return desk;
    }

    private sealed class HoverCardSlot
    {
        internal HoverCardSlot(Button button, GreatUIHoverCard card, Canvas overlay) { Button = button; Card = card; Overlay = overlay; }
        internal Button Button { get; }
        internal GreatUIHoverCard Card { get; }
        internal Canvas Overlay { get; }
        internal DispatcherTimer? Timer { get; set; }
        internal long OutsideSince { get; set; }
    }
    private HoverCardSlot? githubCard;
    private HoverCardSlot? donateCard;
    private void CreateHoverCards(Button github, Button donate, Grid root)
    {
        githubCard = CreateHoverCard(github, root, "github", OpenGithubProfile);
        donateCard = CreateHoverCard(donate, root, "donate", null);
        IsVisibleChanged += (_, _) => { if (!IsVisible) { CloseGithubCard(); CloseDonateCard(); } };
        Closed += (_, _) => { HideCard(githubCard); HideCard(donateCard); };
    }
    private HoverCardSlot CreateHoverCard(Button button, Grid root, string mode, Action? openProfile)
    {
        var overlay = new Canvas { Visibility = Visibility.Visible, IsHitTestVisible = false };
        Panel.SetZIndex(overlay, 10);
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count)); root.Children.Add(overlay);
        var card = new GreatUIHoverCard(mode) { IsHitTestVisible = false };
        var slot = new HoverCardSlot(button, card, overlay);
        if (openProfile is not null) card.ProfileRequested += openProfile;
        card.CardBoundsChanged += () => { if (overlay.IsHitTestVisible) ShowCard(slot); };
        overlay.Children.Add(card);
        Canvas.SetLeft(card, 0); Canvas.SetTop(card, 0);
        button.MouseEnter += (_, _) => { ShowCard(slot); UpdateCardPointer(slot, Mouse.GetPosition(button)); };
        button.MouseMove += (_, e) => UpdateCardPointer(slot, e.GetPosition(button));
        slot.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        // WebView2 and other windows need not update WPF's cached mouse position.
        slot.Timer.Tick += (_, _) => { if (Native.GetCursorPos(out var cursor)) CheckCardHover(slot, new Point(cursor.X, cursor.Y), Environment.TickCount64); };
        return slot;
    }
    private void ShowGithubCard() => ShowCard(githubCard);
    private void ShowDonateCard() => ShowCard(donateCard);
    private void ShowCard(HoverCardSlot? slot)
    {
        if (!IsVisible || slot is null) return;
        var anchor = slot.Button.TranslatePoint(new Point(slot.Button.ActualWidth / 2, 0), slot.Overlay);
        var bounds = slot.Card.CardBounds;
        var overlayWidth = slot.Overlay.ActualWidth > 0 ? slot.Overlay.ActualWidth : ActualWidth;
        double left, top;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            var targetWidth = bounds.Width + 16; var targetHeight = bounds.Height + 16;
            if (Math.Abs(slot.Card.Width - targetWidth) > .5) slot.Card.Width = targetWidth;
            if (Math.Abs(slot.Card.Height - targetHeight) > .5) slot.Card.Height = targetHeight;
            left = anchor.X - (bounds.Left + bounds.Width / 2);
            top = anchor.Y - 16 - (bounds.Top + bounds.Height);
            var minLeft = 8 - bounds.Left; var maxLeft = overlayWidth - 8 - bounds.Left - bounds.Width;
            left = maxLeft >= minLeft ? Math.Clamp(left, minLeft, maxLeft) : minLeft;
            top = Math.Max(8 - bounds.Top, top);
        }
        else
        {
            left = Math.Max(8, anchor.X - slot.Card.Width / 2);
            top = Math.Max(8, anchor.Y - slot.Card.Height - 16);
        }
        Canvas.SetLeft(slot.Card, left);
        Canvas.SetTop(slot.Card, top);
        slot.Overlay.IsHitTestVisible = true;
        slot.Card.IsHitTestVisible = true;
        slot.Card.SetOpen(true);
        slot.OutsideSince = 0;
        // Closing the dashboard or opening the profile stops polling. Every new
        // hover must restart it, including after the dashboard is revealed again.
        slot.Timer?.Start();
    }
    private void CheckCardHover(HoverCardSlot? slot, Point screenPosition, long now)
    {
        if (slot is null) return;
        if (!IsVisible) { HideCard(slot); return; }
        if (!slot.Overlay.IsHitTestVisible) return;
        var overButton = new Rect(0, 0, slot.Button.ActualWidth, slot.Button.ActualHeight);
        var overCard = slot.Card.CardBounds;
        if (overCard.IsEmpty || overCard.Width <= 0 || overCard.Height <= 0)
            overCard = new Rect(0, 0, slot.Card.ActualWidth, slot.Card.ActualHeight);
        var buttonPosition = slot.Button.PointFromScreen(screenPosition);
        if (overButton.Contains(buttonPosition)) { UpdateCardPointer(slot, buttonPosition); slot.OutsideSince = 0; return; }
        if (overCard.Contains(slot.Card.PointFromScreen(screenPosition))) { slot.OutsideSince = 0; return; }
        if (slot.OutsideSince == 0) { slot.OutsideSince = now; return; }
        if (now - slot.OutsideSince >= 220) HideCard(slot);
    }
    private void UpdateCardPointer(HoverCardSlot? slot, Point buttonPosition)
    {
        if (slot is null || slot.Button.ActualWidth <= 0 || slot.Button.ActualHeight <= 0) return;
        slot.Card.SetPointer(new Point(
            Math.Clamp((buttonPosition.X / slot.Button.ActualWidth) * 2 - 1, -1, 1),
            Math.Clamp((buttonPosition.Y / slot.Button.ActualHeight) * 2 - 1, -1, 1)));
    }
    private void HideCard(HoverCardSlot? slot)
    {
        if (slot is null) return;
        slot.Timer?.Stop(); slot.OutsideSince = 0;
        slot.Card.SetPointer(new Point());
        slot.Overlay.IsHitTestVisible = false;
        slot.Card.IsHitTestVisible = false;
        slot.Card.SetOpen(false);
    }
    private void CloseGithubCard() => HideCard(githubCard);
    private void CloseDonateCard() => HideCard(donateCard);
    private void OpenGithubProfile()
    {
        CloseGithubCard();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/jiezi1234") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }


    internal FrameworkElement? ActiveDesktopContent => sections.GetValueOrDefault(currentSection);
    internal bool GithubCardOpen => githubCard?.Overlay.IsHitTestVisible == true;
    internal Task GithubCardReady => githubCard?.Card.Ready ?? Task.CompletedTask;
    internal string GithubCardState => githubCard?.Card.State ?? "none";
    internal Rect GithubCardBounds => githubCard?.Card.CardBounds ?? Rect.Empty;
    internal Point GithubCardPosition => githubCard is null ? default : new Point(Canvas.GetLeft(githubCard.Card), Canvas.GetTop(githubCard.Card));
    internal Point GithubButtonCenter => githubCard is null ? default : githubCard.Button.TranslatePoint(new Point(githubCard.Button.ActualWidth / 2, 0), githubCard.Overlay);
    internal void CloseGithubCardForDiagnostics() => CloseGithubCard();
    internal void ShowGithubCardForDiagnostics() { ShowGithubCard(); githubCard?.Timer?.Stop(); }
    internal bool GithubHoverTracking => githubCard?.Timer?.IsEnabled == true;
    internal void CheckGithubHoverForDiagnostics(Point screenPosition, long now) => CheckCardHover(githubCard, screenPosition, now);
    internal void UpdateGithubPointerForDiagnostics(Point buttonPosition) => UpdateCardPointer(githubCard, buttonPosition);
    internal Task<string> GithubCardTransformForDiagnostics() => githubCard?.Card.CardTransformAsync() ?? Task.FromResult("");
    internal Point GithubCardScreenCenter => githubCard is null ? default : githubCard.Card.PointToScreen(new Point(githubCard.Card.CardBounds.Left + githubCard.Card.CardBounds.Width / 2, githubCard.Card.CardBounds.Top + githubCard.Card.CardBounds.Height / 2));
    internal bool DonateCardOpen => donateCard?.Overlay.IsHitTestVisible == true;
    internal Task DonateCardReady => donateCard?.Card.Ready ?? Task.CompletedTask;
    internal string DonateCardState => donateCard?.Card.State ?? "none";
    internal void ShowDonateCardForDiagnostics() { ShowDonateCard(); donateCard?.Timer?.Stop(); }
    internal void CloseDonateCardForDiagnostics() => CloseDonateCard();
    internal void UpdateDonatePointerForDiagnostics(Point buttonPosition) => UpdateCardPointer(donateCard, buttonPosition);
    internal Task<string> DonateCardTransformForDiagnostics() => donateCard?.Card.CardTransformAsync() ?? Task.FromResult("");
}
