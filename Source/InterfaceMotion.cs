using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WaveWheels;

// Page motion leaves layout and keyboard focus unchanged. Button templates retain a stationary input surface.
internal sealed class SurfaceMotion
{
    private readonly FrameworkElement surface;
    internal TranslateTransform Offset {get;}=new();
    private AnimationClock? positionClock,opacityClock;
    internal const int DurationMilliseconds=210;
    internal SurfaceMotion(FrameworkElement surface) {this.surface=surface;surface.RenderTransform=Offset;}
    internal void Enter(int direction,bool enabled)
    {
        Reset();if(!enabled)return;
        positionClock=(AnimationClock)new DoubleAnimation(direction*12,0,TimeSpan.FromMilliseconds(DurationMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}}.CreateClock(true);
        opacityClock=(AnimationClock)new DoubleAnimation(.78,1,TimeSpan.FromMilliseconds(150)).CreateClock(true);
        Offset.ApplyAnimationClock(TranslateTransform.YProperty,positionClock);surface.ApplyAnimationClock(UIElement.OpacityProperty,opacityClock);
    }
    internal void Reset()
    {
        positionClock?.Controller?.Remove();opacityClock?.Controller?.Remove();positionClock=opacityClock=null;
        Offset.BeginAnimation(TranslateTransform.YProperty,null);Offset.Y=0;surface.BeginAnimation(UIElement.OpacityProperty,null);surface.Opacity=1;
    }
    internal void Seek(TimeSpan time) {positionClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);opacityClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);}
}

public static class InterfaceMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement,PressState> presses=new();
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(InterfaceMotion),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits,(d,e)=> {if(!(bool)e.NewValue&&d is FrameworkElement element&&presses.TryGetValue(element,out var state))state.Reset();}));
    public static bool GetEnabled(DependencyObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element,bool value)=>element.SetValue(EnabledProperty,value);
    public static readonly DependencyProperty PressFeedbackProperty=DependencyProperty.RegisterAttached("PressFeedback",typeof(bool),typeof(InterfaceMotion),new PropertyMetadata(false,(d,e)=>
    {
        if(d is not Button button)return;
        if((bool)e.NewValue) {button.PreviewMouseLeftButtonDown+=Press;button.PreviewMouseLeftButtonUp+=Release;button.MouseLeave+=Release;button.LostMouseCapture+=Release;button.Unloaded+=Release;}
        else {button.PreviewMouseLeftButtonDown-=Press;button.PreviewMouseLeftButtonUp-=Release;button.MouseLeave-=Release;button.LostMouseCapture-=Release;button.Unloaded-=Release;}
    }));
    public static bool GetPressFeedback(DependencyObject element)=>(bool)element.GetValue(PressFeedbackProperty);
    public static void SetPressFeedback(DependencyObject element,bool value)=>element.SetValue(PressFeedbackProperty,value);
    private sealed class PressState
    {
        internal readonly ScaleTransform Scale=new(1,1);
        private AnimationClock? x,y;
        internal PressState(FrameworkElement frame) {frame.RenderTransformOrigin=new Point(.5,.5);frame.RenderTransform=Scale;}
        internal void Animate(bool down)
        {
            var target=down ? .968 : 1;
            AnimationClock Clock(double from)=>(AnimationClock)new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(down?75:180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}}.CreateClock(true);
            x=Clock(Scale.ScaleX);y=Clock(Scale.ScaleY);Scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,x);Scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,y);
        }
        internal void Reset() {x?.Controller?.Remove();y?.Controller?.Remove();Scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);Scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);Scale.ScaleX=Scale.ScaleY=1;}
        internal void Seek(TimeSpan time) {x?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);y?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);}
    }
    private static void Press(object sender,MouseButtonEventArgs e)=>SetPressed((Button)sender,true);
    private static void Release(object sender,RoutedEventArgs e)=>SetPressed((Button)sender,false);
    internal static void SetPressed(Button button,bool down)
    {
        if(button.Template?.FindName("Frame",button) is not FrameworkElement frame)return;
        if(!GetEnabled(button)||!SystemParameters.ClientAreaAnimation||!button.IsEnabled) {if(presses.TryGetValue(frame,out var old))old.Reset();return;}
        presses.GetValue(frame,f=>new PressState(f)).Animate(down);
    }
    internal static void SeekPress(Button button,TimeSpan time) {if(button.Template?.FindName("Frame",button) is FrameworkElement frame&&presses.TryGetValue(frame,out var state))state.Seek(time);}
}
