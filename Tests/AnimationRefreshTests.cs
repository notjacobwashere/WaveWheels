using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveWheels;

internal static class AnimationRefreshTests
{
    private static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var surface=new Border();var motion=new SurfaceMotion(surface);motion.Enter(1,true);motion.Seek(TimeSpan.FromMilliseconds(70));
        Assert(motion.Offset.Y>0&&motion.Offset.Y<12&&surface.Opacity>.78&&surface.Opacity<1,"Page motion must translate and fade without changing layout.");
        motion.Enter(-1,true);motion.Seek(TimeSpan.Zero);Assert(motion.Offset.Y==-12,"Fast tab changes must replace rather than queue page motion.");motion.Reset();Assert(motion.Offset.Y==0&&surface.Opacity==1,"Cancellation must restore a fully visible page.");motion.Enter(1,false);Assert(motion.Offset.Y==0&&surface.Opacity==1,"Disabled motion must not leave pages offset or transparent.");
        var root=new Grid();var button=new Button{Content="Action",Width=120,Height=40};root.Children.Add(button);InterfaceMotion.SetEnabled(root,true);root.Measure(new Size(200,100));root.Arrange(new Rect(0,0,200,100));root.UpdateLayout();
        InterfaceMotion.SetPressed(button,true);InterfaceMotion.SeekPress(button,TimeSpan.FromMilliseconds(75));
        var frame=(FrameworkElement)button.Template.FindName("Frame",button);
        Assert(button.RenderTransform.Value.IsIdentity,"Button feedback must not shrink the pointer target.");
        if(SystemParameters.ClientAreaAnimation)
        {
            Assert(Math.Abs(((ScaleTransform)frame.RenderTransform).ScaleX-.968)<.001,"Button artwork must compress on press.");
            var edgePoint=button.TranslatePoint(new Point(1,button.ActualHeight/2),root);DependencyObject? edgeHit=VisualTreeHelper.HitTest(root,edgePoint)?.VisualHit;
            while(edgeHit!=null&&edgeHit!=button)edgeHit=VisualTreeHelper.GetParent(edgeHit);
            Assert(edgeHit==button,"The original button edge must remain clickable while the artwork is compressed.");
            InterfaceMotion.SetPressed(button,false);InterfaceMotion.SeekPress(button,TimeSpan.FromMilliseconds(180));Assert(((ScaleTransform)frame.RenderTransform).ScaleX==1,"Buttons must settle after release.");
            InterfaceMotion.SetPressed(button,true);InterfaceMotion.SeekPress(button,TimeSpan.FromMilliseconds(75));InterfaceMotion.SetEnabled(root,false);Assert(((ScaleTransform)frame.RenderTransform).ScaleX==1,"Disabling animations must release an active press.");
        }
        Assert(WheelControl.ArcMilliseconds(.01)<WheelControl.ArcMilliseconds(.6)&&WheelControl.ArcMilliseconds(1)<=300,"Small arc changes must settle faster than large jumps.");
        var wheel=new Wheel{Name="Animation preview",Available=true,Level=.6,MaximumVolumePercent=80};var control=new WheelControl{Wheel=wheel,ScrollAccelerationEnabled=false};
        control.VolumeRequested+=(w,v)=>w.Level=Math.Clamp(v,0,w.MaximumVolumePercent/100d);control.MuteRequested+=w=>w.Muted=!w.Muted;
        control.Measure(new Size(300,120));control.Arrange(new Rect(0,0,300,120));control.EnterHover();control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(80));
        if(SystemParameters.ClientAreaAnimation)Assert(control.HoverAmount>0&&control.HoverAmount<1,"Hover outlines must blend into place.");
        control.AdjustInput(1,false,0);control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(45));
        if(SystemParameters.ClientAreaAnimation)
        {
            Assert(control.ActivityAmount>.99,"A real volume change must produce a short arc highlight.");
            var amount=control.ActivityAmount;control.AdjustInput(1,false,40);control.SeekInteractionFeedback(TimeSpan.Zero);Assert(Math.Abs(control.ActivityAmount-amount)<.001,"Rapid changes must retain the current highlight instead of flashing off.");
        }
        wheel.Level=.8;var requests=0;control.VolumeRequested+=(_,_)=>requests++;var bursts=control.MaximumEffectStarts;
        control.AdjustInput(1,false,300);control.AdjustInput(1,false,320);Assert(requests==0&&control.MaximumEffectStarts==bursts,"Limit feedback must not write audio or restart maximum sparks.");
        Assert(control.BoundaryFeedbackStarts==(SystemParameters.ClientAreaAnimation?1:0),"Held limit input must be rate-limited and respect reduced motion.");
        control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(90));Render(control,Path.Combine(directory,"wheel-limit-feedback.png"));
        control.TryMuteAt(new Point(60,60));Assert(wheel.Muted&&control.TapFeedbackStarts==(SystemParameters.ClientAreaAnimation?1:0),"Accepted wheel clicks must animate without changing the mute action.");
        control.EffectsEnabled=false;Assert(control.ActivityAmount==0&&control.BoundaryProgress==1&&control.TapProgress==1,"Disabling animations must clear every new feedback clock.");
        var before=control.BoundaryFeedbackStarts;control.AdjustInput(1,false,700);Assert(control.BoundaryFeedbackStarts==before,"Disabled motion must suppress limit animation.");
        control.EffectsEnabled=true;control.AdjustInput(1,false,1000);wheel.Available=false;Assert(control.BoundaryProgress==1&&control.ActivityAmount==0,"Disconnecting must clear active wheel feedback.");
        var first=NumberRollLayout.Glyphs("60","61",.65,1);var snapshot=NumberRollLayout.Dominant(first);var retarget=NumberRollLayout.Tween(snapshot,"62",0,1);
        foreach(var glyph in snapshot)Assert(retarget.Any(g=>g.Value==glyph.Value&&g.X==glyph.X&&Math.Abs(g.OffsetY-glyph.OffsetY)<.0001&&Math.Abs(g.Opacity-glyph.Opacity)<.0001),"Number retargeting must retain the dominant visible glyph's pose and opacity.");
        Assert(retarget.Count<=4&&NumberRollLayout.Travel>=24,"Number rollers must stay bounded and use separated digit tracks.");
        var numberWheel=new Wheel{Available=true,Level=.59};var number=new WheelControl{Wheel=numberWheel,EffectsEnabled=true};number.Measure(new Size(300,120));number.Arrange(new Rect(0,0,300,120));numberWheel.Level=.6;number.BeginAnimation(WheelControl.DisplayLevelProperty,null);number.DisplayLevel=.6;
        foreach(var ms in new[]{0,35,75,120,180}) {number.SeekNumberTransition(TimeSpan.FromMilliseconds(ms));Render(number,Path.Combine(directory,$"number-roll-{ms}.png"));}
        var vm=new MainViewModel(new SettingsStore(Path.Combine(directory,"interface-profile-"+Guid.NewGuid().ToString("N"))),true);var window=new SettingsWindow(vm);var pageRoot=(FrameworkElement)window.Content;window.Content=null;pageRoot.DataContext=vm;pageRoot.Resources=window.Resources;
        pageRoot.Measure(new Size(1160,860));pageRoot.Arrange(new Rect(0,0,1160,860));pageRoot.UpdateLayout();
        ((Button)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.SeekInterfaceMotion(TimeSpan.FromMilliseconds(70));
        var settings=(FrameworkElement)window.FindName("SettingsPage");Assert(settings.Visibility==Visibility.Visible,"Navigation must make the new page interactive immediately.");
        if(SystemParameters.ClientAreaAnimation)Assert(((TranslateTransform)settings.RenderTransform).Y>0,"Real navigation must use page motion.");
        ((Button)window.FindName("EffectsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(settings.Visibility==Visibility.Collapsed&&settings.Opacity==1&&((TranslateTransform)settings.RenderTransform).Y==0,"Switching pages mid-transition must reset the outgoing page.");
        vm.AnimateWheels=false;var effects=(FrameworkElement)window.FindName("EffectsPage");Assert(effects.Opacity==1&&((TranslateTransform)effects.RenderTransform).Y==0,"The app toggle must stop active navigation motion immediately.");
        vm.Dispose();
        Console.WriteLine("PASS: page/press cancellation, stable input targets, adaptive arcs, continuous highlights, bounded limit feedback, motion suppression, and number retarget continuity.");
    }
    private static void Render(WheelControl wheel,string path)
    {
        wheel.InvalidateVisual();wheel.UpdateLayout();var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.PushTransform(new TranslateTransform(48,48));dc.DrawDrawing(VisualTreeHelper.GetDrawing(wheel));dc.Pop();}
        var bitmap=new RenderTargetBitmap(396,216,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
