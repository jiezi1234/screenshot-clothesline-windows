using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed class TextHoverWindow : OverlayWindow
{
    internal TextBox TextView { get; } = new() { IsReadOnly = true, Focusable = false, ContextMenu = null, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16, Padding = new Thickness(0), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Foreground = DemoAssets.Brush("#294535"), FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI") };
    private readonly TextPaperSurface paper = new(TextPaperStyle.Notebook);
    private readonly DockPanel panel = new();
    private readonly TextBlock heading = new() { Text = T("文字预览 · 滚轮查看全文"), Margin = new Thickness(0, 0, 0, 16), FontSize = 12 };
    internal TextPaperStyle PaperStyle => paper.PaperStyle;
    internal string PreviewText => TextView.Text;
    internal TextHoverWindow() : base("Clothesline · Text Preview", false)
    {
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading); panel.Children.Add(TextView);
        paper.Children.Add(panel); paper.Margin = new Thickness(4); paper.TrackTextLines(TextView); Content = paper;
        SetText("", TextPaperStyle.Notebook);
        AutomationProperties.SetName(TextView, T("文字内容"));
    }
    internal void SetText(string text, TextPaperStyle style = TextPaperStyle.Notebook)
    {
        var palette = TextPaper.Palette(style); paper.PaperStyle = style; panel.Margin = TextPaper.Padding(style, false);
        heading.Foreground = DemoAssets.Brush(palette.Quiet); TextView.Foreground = DemoAssets.Brush(palette.Ink);
        TextView.FontFamily = new FontFamily(palette.Font); TextView.FontSize = style == TextPaperStyle.Sticky ? 18 : 16;
        TextView.Text = text; TextView.ScrollToHome();
    }
    public override void Dismiss() { base.Dismiss(); TextView.Clear(); }
}

internal sealed class ShelfTextHover : IDisposable
{
    private readonly TextHoverWindow window = new();
    private CardWindow? candidate, suppressed;
    private long entered, lastInside;
    private PixelRect travel;
    private bool attempted, disposed;
    internal bool Contains(PixelPoint cursor)
    {
        if (!window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        return Native.WindowFromPoint(new Native.Point { X = cursor.X, Y = cursor.Y }) == handle;
    }
    internal bool KeepsShelfOpen(long now, PixelPoint cursor) => Contains(cursor) || (window.IsVisible && now - lastInside <= 450 && travel.Contains(cursor));
    internal bool Hold(long now, PixelPoint cursor, bool escape, bool overCard)
    {
        if (!window.IsVisible) return false;
        if (escape) { var previous = candidate; Clear(); suppressed = previous; return false; }
        if (Contains(cursor)) { lastInside = now; return true; }
        return !overCard && now - lastInside <= 450 && travel.Contains(cursor);
    }
    internal void Track(CardWindow? card, MonitorInfo monitor, IReadOnlyList<PixelRect> occupied, long now, bool escape, bool animate)
    {
        if (disposed) return;
        if (card is not { Item.Text: not null, Item.IsText: true } || !card.IsVisible || card.Retiring) { Reset(); return; }
        if (suppressed != card) suppressed = null;
        if (escape) { Clear(); suppressed = card; return; }
        if (suppressed == card) return;
        if (candidate != card) { Clear(); candidate = card; entered = now; }
        lastInside = now;
        if (attempted || now - entered < ShelfHoverPreview.DwellMilliseconds) return;
        attempted = true;
        if (!Native.GetWindowRect(new WindowInteropHelper(card).Handle, out var anchor)) return;
        var scale = Native.MoveAndGetScale(window, monitor);
        if (HoverPreviewGeometry.Place(anchor.Pixels, monitor.WorkArea, scale, 560, 440, occupied) is not PixelRect rect) return;
        window.SetText(card.Item.Text, card.Item.PaperStyle); window.Present(rect, animate);
        var left = Math.Min(rect.X, anchor.Left); var top = Math.Min(rect.Y, anchor.Top);
        travel = new(left, top, Math.Max(rect.Right, anchor.Right) - left, Math.Max(rect.Bottom, anchor.Bottom) - top);
    }
    internal void ClearFor(CardWindow card) { if (candidate == card || suppressed == card) Reset(); }
    internal void Reset() { if (disposed) return; Clear(); suppressed = null; }
    private void Clear() { candidate = null; attempted = false; if (window.IsVisible || window.PreviewText.Length > 0) window.Dismiss(); }
    internal void StopMotion() { if (!disposed) window.StopMotion(); }
    public void Dispose() { if (disposed) return; Clear(); disposed = true; window.Close(); }
}
