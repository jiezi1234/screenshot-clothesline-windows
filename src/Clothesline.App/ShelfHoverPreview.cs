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

internal sealed class HoverPreviewWindow : OverlayWindow
{
    private readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    internal BitmapSource? PreviewImage => image.Source as BitmapSource;
    public HoverPreviewWindow() : base("Clothesline · Preview", true)
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Content = new Border { Margin = new Thickness(6), Padding = new Thickness(5), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = DemoAssets.Brush("#D6DED6"), Background = DemoAssets.Brush("#F9FAF7"), Child = image,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18, Color = Colors.Black } };
    }
    internal void SetImage(BitmapSource source) => image.Source = source;
    public override void Dismiss() { base.Dismiss(); image.Source = null; }
}

/// One transient preview; only the current hover may finish an asynchronous load.
internal sealed class ShelfHoverPreview : IDisposable
{
    internal const int DwellMilliseconds = 400;
    private readonly HoverPreviewWindow window = new();
    private readonly Func<string, CancellationToken, Task<BitmapSource>> load;
    private CardWindow? candidate, suppressed;
    private long entered;
    private bool attempted, disposed;
    private int generation;
    private CancellationTokenSource? loading;
    internal ShelfHoverPreview(Func<string, CancellationToken, Task<BitmapSource>>? loader = null) => load = loader ?? ImageDecoder.ReadPreview;
    internal void Track(CardWindow? card, MonitorInfo monitor, IReadOnlyList<PixelRect> occupied, long now, bool escape, bool animate)
    {
        if (disposed) return;
        if (card is null || card.Item.IsVideo || card.Retiring || !card.IsVisible) { Reset(); return; }
        if (suppressed != card) suppressed = null;
        if (escape) { Clear(); suppressed = card; return; }
        if (suppressed == card) return;
        if (candidate != card) { Clear(); candidate = card; entered = now; }
        if (attempted || now - entered < DwellMilliseconds) return;
        attempted = true;
        var handle = new System.Windows.Interop.WindowInteropHelper(card).Handle;
        if (!Native.GetWindowRect(handle, out var native)) return;
        var anchor = native.Pixels;
        // Move the hidden HWND to the correct display before measuring its actual DPI.
        var scale = Native.MoveAndGetScale(window, monitor);
        if (!Show(card.Item.Image, anchor, monitor.WorkArea, scale, occupied, animate)) return;
        loading = new CancellationTokenSource();
        _ = Load(card, generation, loading.Token, anchor, monitor.WorkArea, scale, occupied, animate);
    }
    private bool Show(BitmapSource image, PixelRect anchor, PixelRect work, double scale, IReadOnlyList<PixelRect> occupied, bool animate)
    {
        if (HoverPreviewGeometry.Place(anchor, work, scale, image.PixelWidth, image.PixelHeight, occupied) is not PixelRect rect) { window.Dismiss(); return false; }
        window.SetImage(image); window.Present(rect, animate); return true;
    }
    private async Task Load(CardWindow card, int request, CancellationToken cancellation, PixelRect anchor, PixelRect work, double scale, IReadOnlyList<PixelRect> occupied, bool animate)
    {
        try
        {
            var image = await load(card.Item.Path, cancellation);
            if (!disposed && request == generation && candidate == card && !cancellation.IsCancellationRequested)
                Show(image, anchor, work, scale, occupied, animate);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && request == generation) window.Dismiss(); }
    }
    internal void ClearFor(CardWindow card) { if (candidate == card || suppressed == card) Reset(); }
    internal void Reset() { if (disposed) return; Clear(); suppressed = null; }
    private void Clear()
    {
        generation++; candidate = null; attempted = false;
        loading?.Cancel(); loading?.Dispose(); loading = null;
        if (window.IsVisible || window.PreviewImage is not null) window.Dismiss();
    }
    internal void StopMotion() { if (!disposed) window.StopMotion(); }
    public void Dispose() { if (disposed) return; Clear(); disposed = true; window.Close(); }
}
