using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Clothesline.App;

/// Finite motion inside a fixed HWND. The five-DIP card margin contains the small swing.
internal sealed class CardMotion
{
    private readonly ScaleTransform scale = new(1, 1);
    private readonly RotateTransform rotation = new();
    private readonly TranslateTransform translation = new();
    private int generation;
    public bool Running => scale.HasAnimatedProperties || rotation.HasAnimatedProperties || translation.HasAnimatedProperties;
    public CardMotion(FrameworkElement content)
    {
        content.RenderTransformOrigin = new Point(.5, .04);
        content.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, rotation, translation } };
    }
    public void Enter()
    {
        Stop(); var current = generation;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(.97, 1, 220));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(.97, 1, 220));
        translation.BeginAnimation(TranslateTransform.YProperty, Ease(3, 0, 220));
        var swing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620), FillBehavior = FillBehavior.Stop };
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(-.9, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        swing.KeyFrames.Add(new EasingDoubleKeyFrame(.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        swing.KeyFrames.Add(new EasingDoubleKeyFrame(-.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        swing.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        swing.Completed += (_, _) => { if (generation == current) Stop(); };
        rotation.BeginAnimation(RotateTransform.AngleProperty, swing);
    }
    public void Leave()
    {
        Stop();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(1, .96, 140));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(1, .96, 140));
        translation.BeginAnimation(TranslateTransform.YProperty, Ease(0, 3, 140));
    }
    private static DoubleAnimation Ease(double from, double to, int milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    public void Stop()
    {
        generation++;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        rotation.BeginAnimation(RotateTransform.AngleProperty, null); translation.BeginAnimation(TranslateTransform.YProperty, null);
        scale.ScaleX = scale.ScaleY = 1; rotation.Angle = translation.Y = 0;
    }
}
