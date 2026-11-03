using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WaveWheels;

internal static class AnimationOptionsTests
{
    private static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    internal static void Run(MainViewModel model,SettingsWindow window,FrameworkElement root,string directory)
    {
        var effects=model.Effects;
        var oldAnimations=model.AnimateWheels;model.AnimateWheels=true;
        model.AnimateNumbers=false;model.AnimateVolumeBob=false;
        effects.PerformanceMode=true;
        foreach(var property in typeof(MainViewModel).GetProperties().Where(p=>p.Name.StartsWith("Animate")&&p.PropertyType==typeof(bool)))
            Assert(!(bool)property.GetValue(model)!,"Performance mode must report every animation as off.");
        model.AnimateNumbers=true;
        Assert(!effects.NumbersEnabled&&!effects.VolumeBobEnabled,"Locked controls must preserve the user's saved choices.");
        var store=new SettingsStore(Path.Combine(directory,"animation-options"));store.Save(model.Preferences);
        var saved=store.Load();Assert(saved.Effects.PerformanceMode&&!saved.Effects.NumbersEnabled&&!saved.Effects.VolumeBobEnabled&&saved.Effects.MuteEnabled,"Performance mode and individual preferences must persist separately.");
        ((Button)window.FindName("EffectsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        root.InvalidateMeasure();root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));root.UpdateLayout();
        var options=(StackPanel)window.FindName("AnimationOptionsPanel");
        Assert(!options.IsEnabled&&options.Children.OfType<CheckBox>().Count()==12&&options.Children.OfType<CheckBox>().All(c=>c.IsChecked==false),"Performance mode must lock all twelve visible switches off.");
        effects.PerformanceMode=false;
        Assert(!model.AnimateNumbers&&!model.AnimateVolumeBob&&model.AnimateMute&&model.AnimationControlsEnabled,"Turning performance mode off must restore the user's choices.");
        var wheel=new Wheel{Available=true,Level=.5};var control=new WheelControl{Wheel=wheel,Effects=effects,ScrollAccelerationEnabled=false};control.VolumeRequested+=(w,v)=>w.Level=Math.Clamp(v,0,1);
        control.AdjustInput(1,false,0);Assert(control.VolumeBobStarts==0&&control.NumberTransitionStarts==0,"Disabled bob and number animations must not start on volume input.");
        effects.ArcEnabled=false;wheel.Level=.7;Assert(control.DisplayLevel==.7,"Disabled arc animation must update immediately.");
        effects.MaximumEnabled=false;wheel.Level=1;control.StartMaximumEffect();Assert(control.MaximumEffectStarts==0,"Disabled max effects must not start from level changes or preview commands.");
        effects.MuteEnabled=false;control.StartMuteFeedback();Assert(control.MuteFeedbackStarts==0,"Disabled mute feedback must not start.");
        effects.Reset();control.AdjustInput(-1,false,100);control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(70));
        effects.PerformanceMode=true;
        Assert(control.VolumeBobOffset==0&&control.BoundaryProgress==1&&control.MaximumEffectProgress==1&&control.MuteFeedbackProgress==1&&control.NumberProgress==1&&control.DisplayLevel==wheel.Level,"Performance mode must cancel active clocks and display the current audio state.");
        effects.Reset();Assert(typeof(EffectSettings).GetProperties().Where(p=>p.Name.EndsWith("Enabled")).All(p=>(bool)p.GetValue(effects)!)&&!effects.PerformanceMode,"Restore defaults must restore animation choices and exit performance mode.");
        model.AnimateWheels=oldAnimations;
        Console.WriteLine("PASS: twelve animation switches, locked performance-mode UI, persistence and restoration, immediate cancellation, and independent wheel feedback.");
    }
}
