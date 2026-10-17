using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WaveWheels;

internal sealed class OverlayMotion
{
    internal const int EnterMilliseconds = 380, ExitMilliseconds = 260;
    internal const double HiddenScale = .72;
    internal TranslateTransform Slide { get; } = new();
    internal ScaleTransform Scale { get; } = new(1, 1);
    private AnimationClock? slideClock, xClock, yClock;

    public OverlayMotion(FrameworkElement surface)
    {
        var transforms = new TransformGroup();
        transforms.Children.Add(Scale); transforms.Children.Add(Slide);
        surface.RenderTransformOrigin = new Point(0, .5);
        surface.RenderTransform = transforms;
        // Keep the dials opaque: the entrance is physical translation and growth.
        surface.Opacity = 1;
    }
    public void Enter(double width, bool fromHidden, bool enabled, Action completed)
    {
        if (fromHidden) Set(-width, HiddenScale);
        Animate(0, 1, enabled ? EnterMilliseconds : 0, EasingMode.EaseOut, completed);
    }
    public void Exit(double width, bool enabled, Action completed) =>
        Animate(-width, HiddenScale, enabled ? ExitMilliseconds : 0, EasingMode.EaseIn, completed);
    public void PrepareHidden(double width) => Set(-width, HiddenScale);

    private void Set(double x, double scale)
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Slide.X = x; Scale.ScaleX = Scale.ScaleY = scale;
    }
    private void Animate(double x, double scale, int milliseconds, EasingMode easing, Action completed)
    {
        if (milliseconds == 0) { Set(x, scale); completed(); return; }
        AnimationClock Clock(double from, double to) => (AnimationClock)new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new CubicEase { EasingMode = easing } }.CreateClock(true);
        slideClock = Clock(Slide.X, x); xClock = Clock(Scale.ScaleX, scale); yClock = Clock(Scale.ScaleY, scale);
        slideClock.Completed += (_, _) => completed();
        Slide.ApplyAnimationClock(TranslateTransform.XProperty, slideClock);
        Scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, xClock);
        Scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, yClock);
    }
    // Deterministic component verification of the same clocks used on the real overlay.
    internal void Seek(TimeSpan time)
    {
        foreach (var clock in new[] { slideClock, xClock, yClock })
            clock?.Controller?.SeekAlignedToLastTick(time, TimeSeekOrigin.BeginTime);
    }
}
