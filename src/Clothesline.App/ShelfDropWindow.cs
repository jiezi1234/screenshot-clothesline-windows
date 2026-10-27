using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed class ShelfDropWindow : OverlayWindow
{
    private readonly TextBlock hint = new() { FontSize = 13, Foreground = DemoAssets.Brush("#294535"), TextWrapping = TextWrapping.Wrap };
    private readonly Border marker = new() { Width = 3, Background = DemoAssets.Brush("#315E49"), CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Canvas markers = new() { IsHitTestVisible = false };
    internal ShelfDropWindow() : base("Clothesline · Drop", false)
    {
        // A nonzero alpha gives OLE a real hit-test surface, only while a drag is near the shelf.
        var content = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 49, 94, 73)) };
        content.Children.Add(new Border { BorderBrush = DemoAssets.Brush("#60315E49"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), IsHitTestVisible = false });
        content.Children.Add(new Border { Child = hint, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(10, 2, 10, 0), CornerRadius = new CornerRadius(9),
            Background = DemoAssets.Brush("#F9FAF7"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        markers.Children.Add(marker); content.Children.Add(markers); Content = content;
    }
    internal void Feedback(string text, PixelRect bounds, double scale, int? markerX)
    {
        hint.Text = text;
        marker.Visibility = markerX is null ? Visibility.Collapsed : Visibility.Visible;
        if (markerX is int x) { Canvas.SetLeft(marker, (x - bounds.X) / scale); Canvas.SetTop(marker, 42); marker.Height = System.Math.Max(8, bounds.Height / scale - 50); }
    }
}
