using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class TextHoverWindow : OverlayWindow
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
        InitializeEditor();
    }
    internal void SetText(string text, TextPaperStyle style = TextPaperStyle.Notebook)
    {
        if (IsEditing) return;
        var palette = TextPaper.Palette(style); paper.PaperStyle = style; panel.Margin = TextPaper.Padding(style, false);
        heading.Foreground = DemoAssets.Brush(palette.Quiet); TextView.Foreground = DemoAssets.Brush(palette.Ink);
        TextView.FontFamily = new FontFamily(palette.Font); TextView.FontSize = style == TextPaperStyle.Sticky ? 18 : 16;
        TextView.Text = text; TextView.ScrollToHome();
    }
    public override void Dismiss() { base.Dismiss(); if (!IsEditing) { TextView.Clear(); previewItem = null; UpdateEditorTools(); } }
}

internal sealed class ShelfTextHover : IDisposable
{
    private readonly TextHoverWindow window = new();
    private CardWindow? candidate, suppressed;
    private long entered, lastInside;
    private PixelRect travel;
    private bool attempted, disposed;
    internal bool IsEditing => window.IsEditing;
    internal string? EditingId => window.EditingId;
    internal Func<DemoItem, TextEditSession?>? StartEdit { set => window.StartEdit = value; }
    internal bool TryCloseEditor() => window.TryEndEditing();
    internal bool FocusEditor(string id)
    {
        if (!window.IsEditing || window.EditingId != id) return false;
        window.FocusEditor(); return true;
    }
    internal bool Contains(PixelPoint cursor)
    {
        if (!window.IsVisible) return false;
        var handle = new WindowInteropHelper(window).Handle;
        return Native.WindowFromPoint(new Native.Point { X = cursor.X, Y = cursor.Y }) == handle;
    }
    internal bool KeepsShelfOpen(long now, PixelPoint cursor) => (window.IsEditing && window.IsVisible) || Contains(cursor) || (window.IsVisible && now - lastInside <= 450 && travel.Contains(cursor));
    internal bool Hold(long now, PixelPoint cursor, bool escape, bool overCard)
    {
        if (!window.IsVisible) return false;
        if (window.IsEditing) return true; // Keyboard handling owns Esc while editing; leaving never loses a draft.
        if (escape) { var previous = candidate; Clear(); suppressed = previous; return false; }
        if (Contains(cursor)) { lastInside = now; return true; }
        return !overCard && now - lastInside <= 450 && travel.Contains(cursor);
    }
    internal void Track(CardWindow? card, MonitorInfo monitor, IReadOnlyList<PixelRect> occupied, long now, bool escape, bool animate)
    {
        if (disposed || window.IsEditing) return;
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
        window.SetPreview(card.Item); window.Present(rect, animate);
        var left = Math.Min(rect.X, anchor.Left); var top = Math.Min(rect.Y, anchor.Top);
        travel = new(left, top, Math.Max(rect.Right, anchor.Right) - left, Math.Max(rect.Bottom, anchor.Bottom) - top);
    }
    internal void ClearFor(CardWindow card) { if (candidate == card || suppressed == card) Reset(); }
    internal void Reset() { if (disposed || window.IsEditing) return; Clear(); suppressed = null; }
    internal void Hide() { if (window.IsEditing) window.Dismiss(); else Reset(); }
    internal void Resume(MonitorInfo monitor)
    {
        if (!window.IsEditing || window.IsVisible) return;
        var scale = Native.MoveAndGetScale(window, monitor);
        var work = monitor.WorkArea; var width = Math.Min(work.Width, (int)(560 * scale)); var height = Math.Min(work.Height, (int)(440 * scale));
        window.Present(new PixelRect(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height), false);
    }
    private void Clear() { candidate = null; attempted = false; if (window.IsVisible || window.PreviewText.Length > 0) window.Dismiss(); }
    internal void StopMotion() { if (!disposed) window.StopMotion(); }
    public void Dispose() { if (disposed) return; window.TryEndEditing(() => true); Clear(); disposed = true; window.ClosePermanently(); }
}
