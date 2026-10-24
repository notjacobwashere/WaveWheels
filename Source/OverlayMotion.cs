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
    private readonly FrameworkElement surface;
    private string edge="Left";
    private DependencyProperty Axis=>edge is "Top" or "Bottom"?TranslateTransform.YProperty:TranslateTransform.XProperty;
    private double Offset=>edge is "Top" or "Bottom"?Slide.Y:Slide.X;
    private double HiddenOffset(double distance)=>edge is "Right" or "Bottom"?distance:-distance;

    public OverlayMotion(FrameworkElement surface)
    {
        this.surface=surface;
        var transforms = new TransformGroup();
        transforms.Children.Add(Scale); transforms.Children.Add(Slide);
        surface.RenderTransformOrigin = new Point(0, .5);
        surface.RenderTransform = transforms;
        // Keep the dials opaque: the entrance is physical translation and growth.
        surface.Opacity = 1;
    }
    public void SetEdge(string value)
    {
        if(edge==value)return;edge=value;
        surface.RenderTransformOrigin=edge switch {"Right"=>new Point(1,.5),"Top"=>new Point(.5,0),"Bottom"=>new Point(.5,1),_=>new Point(0,.5)};
        Set(0,1);
    }
    public void Enter(double width, bool fromHidden, bool enabled, Action completed,int delayMilliseconds=0)
    {
        if (fromHidden) Set(HiddenOffset(width), HiddenScale);
        Animate(0, 1, enabled ? EnterMilliseconds : 0, EasingMode.EaseOut, completed,delayMilliseconds);
    }
    public void Exit(double width, bool enabled, Action completed) =>
        Animate(HiddenOffset(width), HiddenScale, enabled ? ExitMilliseconds : 0, EasingMode.EaseIn, completed);
    public void PrepareHidden(double width) => Set(HiddenOffset(width), HiddenScale);

    private void Set(double x, double scale)
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Slide.X=Slide.Y=0;Slide.SetValue(Axis,x); Scale.ScaleX = Scale.ScaleY = scale;
    }
    private void Animate(double x, double scale, int milliseconds, EasingMode easing, Action completed,int delayMilliseconds=0)
    {
        if (milliseconds == 0) { Set(x, scale); completed(); return; }
        AnimationClock Clock(double from,double to)
        {
            var animation=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(milliseconds+delayMilliseconds)};
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if(delayMilliseconds>0)animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds+delayMilliseconds)),new CubicEase{EasingMode=easing}));
            return (AnimationClock)animation.CreateClock(true);
        }
        slideClock = Clock(Offset, x); xClock = Clock(Scale.ScaleX, scale); yClock = Clock(Scale.ScaleY, scale);
        slideClock.Completed += (_, _) => completed();
        Slide.ApplyAnimationClock(Axis, slideClock);
        Scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, xClock);
        Scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, yClock);
    }
    internal static int StaggerDelay(int index)=>Math.Min(210,Math.Max(0,index)*45);
    // Deterministic component verification of the same clocks used on the real overlay.
    internal void Seek(TimeSpan time)
    {
        foreach (var clock in new[] { slideClock, xClock, yClock })
            clock?.Controller?.SeekAlignedToLastTick(time, TimeSeekOrigin.BeginTime);
    }
}
