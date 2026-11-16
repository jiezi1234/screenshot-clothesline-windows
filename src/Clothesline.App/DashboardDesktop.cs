using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class Dashboard
{
    private readonly List<TextBlock> tabLabels = new();
    private ContentControl? pageHost;
    private TextBlock? pageTitle;
    private TextBlock? pageLead;
    private DashboardPage currentPage;
    private bool syncingTabs;

    private FrameworkElement CreateDesktop(ShelfController controller, Action quit)
    {
        var desk = new Grid { ClipToBounds = true, Background = DeskBackground() };
        desk.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, desk)) { try { DragMove(); } catch (InvalidOperationException) { } } };
        var frame = new Grid { Margin = new Thickness(24, 20, 58, 22) };
        frame.ColumnDefinitions.Add(new ColumnDefinition());
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var board = CreateBoard(controller, quit, out var github, out var donate);
        frame.Children.Add(board);
        var tabs = CreateTabs();
        Grid.SetColumn(tabs, 1); frame.Children.Add(tabs);
        var overlayHost = new Grid();
        Grid.SetColumnSpan(overlayHost, 2);
        Panel.SetZIndex(overlayHost, 20);
        frame.Children.Add(overlayHost);
        desk.Children.Add(frame);
        CreateHoverCards(github, donate, overlayHost);
        return desk;
    }

    private static Brush DeskBackground()
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowBrush;
        var brush = new RadialGradientBrush(Color.FromRgb(0xF5, 0xF2, 0xEF), Color.FromRgb(0xEA, 0xE6, 0xE1))
        { GradientOrigin = new Point(0.5, 0), Center = new Point(0.5, 0.1), RadiusX = 1.1, RadiusY = 1.4 };
        brush.Freeze(); return brush;
    }

    private Grid CreateBoard(ShelfController controller, Action quit, out Button github, out Button donate)
    {
        var shell = new Grid();
        shell.Children.Add(new Border { Background = Brush("PaperEdgeBrush"), CornerRadius = new CornerRadius(4, 14, 14, 4), RenderTransform = new TranslateTransform(8, 6) });
        var boardGrid = new Grid { Margin = new Thickness(80, 42, 48, 26) };
        boardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        boardGrid.RowDefinitions.Add(new RowDefinition());
        boardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
        var meta = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition());
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var week = Text(T("第 {0} 周", ISOWeek.GetWeekOfYear(DateTime.Today)), 10.5);
        week.Foreground = Brush("AccentBrush"); week.FontWeight = FontWeights.SemiBold;
        var rule = new Border { Height = 1, Background = Brush("AccentSoftBrush"), Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var badge = Text(T("控制面板"), 10.5);
        badge.Foreground = Brush("AccentBrush"); badge.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(badge, 2);
        meta.Children.Add(week); meta.Children.Add(rule); meta.Children.Add(badge);
        header.Children.Add(meta);

        pageTitle = new TextBlock { Text = titles[(int)DashboardPage.Shelf], FontFamily = DisplayFont(), FontSize = 38, Foreground = Brush("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(pageTitle);
        var date = new TextBlock { Text = DateTime.Today.ToString("D", UiText.Culture), FontFamily = DisplayFont(), FontStyle = FontStyles.Italic, FontSize = 17, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) };
        header.Children.Add(date);
        pageLead = Text(PageDescription(DashboardPage.Shelf), 13, true, new Thickness(0, 16, 0, 0));
        pageLead.FontStyle = FontStyles.Italic;
        header.Children.Add(pageLead);
        Grid.SetRow(header, 0); boardGrid.Children.Add(header);

        pageHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Focusable = false, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(pageHost, 1); boardGrid.Children.Add(pageHost);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var toggle = Button(T("显示 / 收起"), controller.Toggle);
        var import = Button(T("导入图片或录像…"), Import);
        github = Button("GitHub", ShowGithubCard);
        donate = Button(T("捐赠"), ShowDonateCard);
        var hide = Button(T("收起控制面板"), Hide);
        var exit = Button(T("退出"), quit);
        foreach (var action in new[] { toggle, import, pauseCollection, github, donate, hide, exit, discard, save })
        {
            action.MinHeight = 30; action.Padding = new Thickness(12, 4, 12, 4); action.FontSize = 12; action.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(action);
        }
        AutomationProperties.SetName(github, "GitHub"); AutomationProperties.SetName(donate, T("捐赠"));

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        draftStatus.FontSize = 11.5; draftStatus.Foreground = Brush("AccentBrush"); draftStatus.Margin = new Thickness(0, 0, 0, 2);
        status.FontSize = 11.5; status.Foreground = Brush("MutedBrush"); status.TextTrimming = TextTrimming.CharacterEllipsis; status.TextWrapping = TextWrapping.NoWrap;
        info.Children.Add(draftStatus); info.Children.Add(status);
        var footer = new Grid();
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.Children.Add(info);
        actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(actions, 1); footer.Children.Add(actions);
        saveBar.Child = footer; saveBar.BorderBrush = Brush("LineBrush"); saveBar.BorderThickness = new Thickness(0, 1, 0, 0);
        saveBar.Padding = new Thickness(0, 12, 0, 0); saveBar.Margin = new Thickness(0, 16, 0, 0); saveBar.Visibility = Visibility.Visible;
        Grid.SetRow(saveBar, 2); boardGrid.Children.Add(saveBar);

        var content = new Grid(); content.Children.Add(boardGrid); content.Children.Add(Spine());
        var board = new Border { Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(4, 14, 14, 4), SnapsToDevicePixels = true, Child = content };
        if (!SystemParameters.HighContrast) board.Effect = new DropShadowEffect { Color = Color.FromRgb(0x5A, 0x4A, 0x42), BlurRadius = 38, ShadowDepth = 10, Direction = 270, Opacity = .16 };
        shell.Children.Add(board);
        return shell;
    }

    private FrameworkElement Spine()
    {
        var spine = new Grid { Width = 58, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 46, 0, 46), IsHitTestVisible = false };
        var ringBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xD5, 0xD1, 0xCC), 0));
        ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFC, 0xFB, 0xFA), .3));
        ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB8, 0xB3, 0xAE), .8));
        ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9E, 0x9A, 0x95), 1));
        ringBrush.Freeze();
        for (var i = 0; i < 7; i++)
        {
            spine.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var cell = new Grid();
            var ring = new Border { Width = 46, Height = 8, CornerRadius = new CornerRadius(4), Background = ringBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            if (!SystemParameters.HighContrast) ring.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = .18 };
            var hole = new Ellipse { Width = 14, Height = 14, Fill = Brush("CanvasBrush"), Stroke = Brush("LineStrongBrush"), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 0, 0) };
            cell.Children.Add(ring); cell.Children.Add(hole);
            Grid.SetRow(cell, i); spine.Children.Add(cell);
        }
        return spine;
    }

    private FrameworkElement CreateTabs()
    {
        navigation.Items.Clear(); tabLabels.Clear();
        navigation.Background = Brushes.Transparent; navigation.BorderThickness = new Thickness(0); navigation.SelectionMode = SelectionMode.Single;
        navigation.ItemContainerStyle = (Style)Resources["BookmarkTab"];
        AutomationProperties.SetName(navigation, T("控制面板导航"));
        navigation.VerticalAlignment = VerticalAlignment.Stretch;
        navigation.Margin = new Thickness(-10, 56, 0, 20);
        ScrollViewer.SetVerticalScrollBarVisibility(navigation, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);
        for (var i = 0; i < titles.Length; i++)
        {
            var label = Text(titles[i], 11.5);
            label.LayoutTransform = new RotateTransform(90);
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(0);
            var item = new ListBoxItem { Content = label, Padding = new Thickness(14, 10, 14, 10) };
            tabLabels.Add(label); navigation.Items.Add(item);
        }
        navigation.SelectionChanged += (_, e) => { if (!syncingTabs && e.Source == navigation && navigation.SelectedIndex >= 0) Navigate((DashboardPage)navigation.SelectedIndex); };
        return navigation;
    }

    private void UpdateTabStyles()
    {
        for (var i = 0; i < tabLabels.Count; i++)
        {
            var selected = i == (int)currentPage;
            tabLabels[i].Foreground = Brush(selected ? "AccentBrush" : "MutedBrush");
            tabLabels[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
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

    private void UpdateDesktopSaveVisibility() => saveBar.Visibility = Visibility.Visible;

    internal FrameworkElement? ActiveDesktopContent => pageHost?.Content as FrameworkElement;
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
