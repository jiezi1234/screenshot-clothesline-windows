using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal sealed class ShelfDropWindow : OverlayWindow
{
    private readonly TextBlock hint = new() { FontSize = 13, Foreground = DemoAssets.Brush("#294535"), TextWrapping = TextWrapping.Wrap };
    private readonly Canvas previews = new() { IsHitTestVisible = false };
    private readonly Dictionary<string, Sprite> sprites = new();
    private PixelRect? placement;
    private int previewLayer;
    internal int PlacementCount { get; private set; }
    internal int SnapshotCount { get; private set; }
    internal int PreviewCount => sprites.Values.Count(s => s.Image.Visibility == Visibility.Visible);
    internal bool PreviewAnimating => sprites.Values.Any(s => s.Moving);
    internal (double X, double Angle, double TargetAngle) PreviewState(string id) => (sprites[id].Translation.X, sprites[id].Rotation.Angle, sprites[id].TargetAngle);
    internal ShelfDropWindow() : base("Clothesline · Drop", false)
    {
        // A nonzero alpha gives OLE a real hit-test surface, only while a drag is near the shelf.
        var content = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 49, 94, 73)) };
        content.Children.Add(new Border { BorderBrush = DemoAssets.Brush("#30315E49"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), IsHitTestVisible = false });
        content.Children.Add(previews);
        content.Children.Add(new Border { Child = hint, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(10, 2, 10, 0), CornerRadius = new CornerRadius(9),
            Background = DemoAssets.Brush("#F9FAF7"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        Content = content;
    }
    public override void Present(PixelRect rect, bool animate)
    {
        // DragOver can arrive much faster than frames. Keep one stationary HWND during a drag.
        if (IsVisible && placement == rect) return;
        base.Present(rect, animate); placement = rect; PlacementCount++;
    }
    internal void InvalidatePlacement() => placement = null;
    public override void Dismiss() { ClearPreview(); placement = null; base.Dismiss(); }
    internal void Feedback(string text)
    {
        if (hint.Text != text) hint.Text = text;
    }
    internal void BeginPreviewFrame() { previewLayer = 0; foreach (var sprite in sprites.Values) sprite.Used = false; }
    internal void Preview(CardWindow card, PixelRect origin, PixelRect destination, PixelRect bounds, double scale, double angle, bool animate, bool held = false)
    {
        var key = card.Item.EntryId ?? card.Item.Path;
        if (!sprites.TryGetValue(key, out var sprite))
        {
            card.StopMotion(); card.UpdateLayout();
            var bitmap = new RenderTargetBitmap(Math.Max(1, origin.Width), Math.Max(1, origin.Height), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render((FrameworkElement)card.Content); bitmap.Freeze(); SnapshotCount++;
            sprite = new Sprite(bitmap, origin.Width / scale, origin.Height / scale, (origin.X - bounds.X) / scale, (origin.Y - bounds.Y) / scale);
            sprites.Add(key, sprite); previews.Children.Add(sprite.Image);
        }
        sprite.Used = true; sprite.Image.Visibility = Visibility.Visible;
        sprite.Image.Opacity = held ? .96 : 1; Panel.SetZIndex(sprite.Image, held ? int.MaxValue : previewLayer++);
        sprite.Move((destination.X - bounds.X) / scale, (destination.Y - bounds.Y) / scale, angle, animate);
    }
    internal void EndPreviewFrame()
    {
        foreach (var sprite in sprites.Values.Where(s => !s.Used)) { sprite.Stop(); sprite.Image.Visibility = Visibility.Collapsed; }
    }
    internal void ClearPreview()
    {
        foreach (var sprite in sprites.Values) sprite.Stop();
        previews.Children.Clear(); sprites.Clear();
    }
    private sealed class Sprite
    {
        public Image Image { get; }
        public TranslateTransform Translation { get; } = new();
        public RotateTransform Rotation { get; }
        public double TargetAngle { get; private set; }
        private double targetX, targetY;
        private int generation;
        public bool Used { get; set; }
        public bool Moving => Translation.HasAnimatedProperties || Rotation.HasAnimatedProperties;
        public Sprite(ImageSource image, double width, double height, double x, double y)
        {
            targetX = x; targetY = y; Translation.X = x; Translation.Y = y;
            Rotation = new RotateTransform(0, width / 2, height * .04);
            Image = new Image { Source = image, Width = width, Height = height, Stretch = Stretch.Fill,
                RenderTransform = new TransformGroup { Children = new TransformCollection { Rotation, Translation } } };
        }
        public void Move(double x, double y, double angle, bool animate)
        {
            if (x == targetX && y == targetY && angle == TargetAngle && (animate || !Moving)) return;
            generation++;
            targetX = x; targetY = y; TargetAngle = angle;
            Tween(Translation, TranslateTransform.XProperty, Translation.X, x, animate);
            Tween(Translation, TranslateTransform.YProperty, Translation.Y, y, animate);
            Tween(Rotation, RotateTransform.AngleProperty, Rotation.Angle, angle, animate);
        }
        private void Tween(Animatable target, DependencyProperty property, double from, double to, bool animate)
        {
            target.BeginAnimation(property, null); target.SetValue(property, to);
            if (!animate || Math.Abs(from - to) < .01) return;
            var current = generation;
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => { if (current == generation) target.BeginAnimation(property, null); };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
        public void Stop()
        {
            generation++;
            Translation.BeginAnimation(TranslateTransform.XProperty, null); Translation.BeginAnimation(TranslateTransform.YProperty, null);
            Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
