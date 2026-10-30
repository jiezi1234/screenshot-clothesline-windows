using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed partial class HoverPreviewWindow : OverlayWindow
{
    private readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Border frame;
    internal BitmapSource? PreviewImage => image.Source as BitmapSource;
    public HoverPreviewWindow() : base("Clothesline · Preview", false)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        frame = new Border { Margin = new Thickness(6), Padding = new Thickness(5), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = DemoAssets.Brush("#D6DED6"), Background = DemoAssets.Brush("#F9FAF7"), Child = image,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18, Color = Colors.Black } };
        Content = frame; InitializeEditor(out undo, out redo, out retry);
    }
    internal void SetImage(BitmapSource source) => image.Source = source;
    public override void Dismiss() { base.Dismiss(); if (!IsEditing) image.Source = null; }
}

/// One transient preview; only the current hover may finish an asynchronous load.
internal sealed class ShelfHoverPreview : IDisposable
{
    internal const int DwellMilliseconds = 400;
    private readonly HoverPreviewWindow window = new();
    internal HoverPreviewWindow Window => window;
    private readonly Func<string, CancellationToken, Task<BitmapSource>> load;
    private CardWindow? candidate, suppressed;
    private long entered, lastInside;
    private PixelRect travel;
    private bool closing, closeRequested, forceClose, hiddenByShelf;
    private bool attempted, disposed;
    private int generation;
    private CancellationTokenSource? loading;
    internal Func<DemoItem, CancellationToken, Task<ImageEditSession?>>? StartEdit { get; set; }
    internal bool IsEditing => window.IsEditing;
    internal bool TryCloseEditor() => window.TryEndEditing();
    internal ShelfHoverPreview(Func<string, CancellationToken, Task<BitmapSource>>? loader = null)
    {
        load = loader ?? ImageDecoder.ReadPreview;
        window.CloseRequested += () => _ = ClosePreview(true);
        window.Saved += () => { if (!disposed && hiddenByShelf && !window.Dirty) ReleaseCleanPreview(); };
    }
    internal bool Contains(PixelPoint cursor) => window.IsVisible && Native.WindowFromPoint(new Native.Point { X = cursor.X, Y = cursor.Y }) == new System.Windows.Interop.WindowInteropHelper(window).Handle;
    internal bool KeepsShelfOpen(long now, PixelPoint cursor) => (window.IsEditing && window.IsVisible) || Contains(cursor) || (window.IsVisible && now - lastInside <= 450 && travel.Contains(cursor));
    internal bool Hold(long now, PixelPoint cursor, bool escape, CardWindow? overCard)
        => window.IsVisible && UpdatePointer(now, Contains(cursor), overCard is not null && overCard == candidate, overCard is not null, travel.Contains(cursor), escape);
    internal bool UpdatePointer(long now, bool inside, bool overSource, bool overCard, bool inTravel, bool escape)
    {
        if (disposed) return false;
        if (escape) { _ = ClosePreview(true); return window.IsEditing; }
        if (inside || overSource || window.PointerInteraction) { lastInside = now; closeRequested = false; return true; }
        if (!overCard && now - lastInside <= 450 && inTravel) { closeRequested = false; return true; }
        closeRequested = true;
        if (window.Saving || closing || window.SaveFailed) return true;
        _ = ClosePreview(false); return window.IsEditing;
    }
    private async Task ClosePreview(bool force)
    {
        if (disposed) return;
        if (closing) { forceClose |= force; return; }
        closing = true; closeRequested = true; forceClose = force; var previous = candidate;
        try
        {
            if (await window.SaveAndDismiss(() => !disposed && (forceClose || closeRequested)))
            { Clear(); if (forceClose) suppressed = previous; }
        }
        finally { closing = false; forceClose = false; }
    }
    internal void Track(CardWindow? card, MonitorInfo monitor, IReadOnlyList<PixelRect> occupied, long now, bool escape, bool animate)
    {
        if (disposed || window.IsEditing) return;
        if (card is null || card.Item.IsVideo || card.Retiring || !card.IsVisible) { Reset(); return; }
        if (suppressed != card) suppressed = null;
        if (escape) { Clear(); suppressed = card; return; }
        if (suppressed == card) return;
        if (candidate != card) { Clear(); candidate = card; entered = now; }
        lastInside = now;
        if (attempted || now - entered < DwellMilliseconds) return;
        attempted = true;
        var handle = new System.Windows.Interop.WindowInteropHelper(card).Handle;
        if (!Native.GetWindowRect(handle, out var native)) return;
        var anchor = native.Pixels;
        // Move the hidden HWND to the correct display before measuring its actual DPI.
        var scale = Native.MoveAndGetScale(window, monitor);
        hiddenByShelf = false;
        if (!Show(card.Item.Image, anchor, monitor.WorkArea, scale, occupied, animate)) return;
        loading = new CancellationTokenSource();
        _ = Load(card, generation, loading.Token, anchor, monitor.WorkArea, scale, occupied, animate);
    }
    private bool Show(BitmapSource image, PixelRect anchor, PixelRect work, double scale, IReadOnlyList<PixelRect> occupied, bool animate)
    {
        if (HoverPreviewGeometry.Place(anchor, work, scale, StartEdit is null ? image.PixelWidth : 620, StartEdit is null ? image.PixelHeight : 520, occupied) is not PixelRect rect) { window.Dismiss(); return false; }
        var left = Math.Min(rect.X, anchor.X); var top = Math.Min(rect.Y, anchor.Y);
        travel = new(left, top, Math.Max(rect.Right, anchor.Right) - left, Math.Max(rect.Bottom, anchor.Bottom) - top);
        window.SetImage(image); window.Present(rect, animate); return true;
    }
    private async Task Load(CardWindow card, int request, CancellationToken cancellation, PixelRect anchor, PixelRect work, double scale, IReadOnlyList<PixelRect> occupied, bool animate)
    {
        try
        {
            if (StartEdit is { } start)
            {
                var session = await start(card.Item, cancellation);
                if (session is not null)
                {
                    if (disposed || request != generation || candidate != card || cancellation.IsCancellationRequested) { session.Dispose(); return; }
                    Show(session.Document.Original, anchor, work, scale, occupied, animate);
                    window.SetSession(session); return;
                }
            }
            var image = await load(card.Item.Path, cancellation);
            if (!disposed && request == generation && candidate == card && !cancellation.IsCancellationRequested)
                Show(image, anchor, work, scale, occupied, animate);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && request == generation) window.Dismiss(); }
    }
    internal void ClearFor(CardWindow card) { if (candidate == card || suppressed == card) _ = ClosePreview(true); }
    internal bool ReleaseCleanPreview()
    {
        if (window.Dirty || window.Saving || window.PointerInteraction) return false;
        window.TryEndEditing(); Clear(); return true;
    }
    internal void Reset() { if (disposed || window.IsEditing) return; Clear(); suppressed = null; }
    internal void Hide() { hiddenByShelf = true; window.Canvas.Cancel(); if (!ReleaseCleanPreview()) window.Dismiss(); }
    internal void Resume(MonitorInfo monitor)
    {
        if (!window.IsEditing || window.IsVisible) return;
        hiddenByShelf = false;
        var scale = Native.MoveAndGetScale(window, monitor); var work = monitor.WorkArea;
        var width = Math.Min(work.Width, (int)(620 * scale)); var height = Math.Min(work.Height, (int)(520 * scale));
        window.Present(new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height), false);
    }
    private void Clear()
    {
        generation++; candidate = null; attempted = false;
        loading?.Cancel(); loading?.Dispose(); loading = null;
        if (window.IsVisible || window.PreviewImage is not null) window.Dismiss();
    }
    internal void StopMotion() { if (!disposed) window.StopMotion(); }
    public void Dispose() { if (disposed) return; window.TryEndEditing(() => true); Clear(); disposed = true; window.ClosePermanently(); }
}
