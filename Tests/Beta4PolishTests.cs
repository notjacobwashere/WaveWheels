using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaveWheels;

internal static class Beta4PolishTests
{
    private static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    private static void Click(SettingsWindow window,string name)=>((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static void Run(MainViewModel model,SettingsWindow window,FrameworkElement root,string directory)
    {
        Directory.CreateDirectory(directory);
        var nav=(StackPanel)window.FindName("PrimaryNavigation");
        Assert(((Border)window.FindName("WheelPreviewFrame")).Margin.Bottom>=12,"The preview and Appearance cards must have a visible gap.");
        Assert(nav.Children.OfType<Button>().Select(b=>b.Content.ToString()).SequenceEqual(new[]{"Wheels","Appearance","Settings","Updates"}),"Primary navigation must contain exactly four clearly named sections.");
        Click(window,"PlacementNav");Flush(root,1160,860);
        Assert(((FrameworkElement)window.FindName("AppearanceTabs")).Visibility==Visibility.Visible&&((Button)window.FindName("EffectsNav")).FontWeight==FontWeights.SemiBold,"Placement must stay inside the selected Appearance section.");
        Click(window,"ConnectionNav");Flush(root,1160,860);
        Assert(((FrameworkElement)window.FindName("PreferenceTabs")).Visibility==Visibility.Visible&&((Button)window.FindName("SettingsNav")).FontWeight==FontWeights.SemiBold,"Connection must stay inside Settings with its sibling tabs visible.");
        Click(window,"UpdatesNav");Flush(root,1160,860);
        Assert(((FrameworkElement)window.FindName("SectionTabs")).Visibility==Visibility.Collapsed&&((ComboBox)window.FindName("UpdateChannelPicker")).Visibility==Visibility.Visible,"Updates must show channel selection directly, without unrelated section tabs.");
        ((Expander)window.FindName("VersionHistory")).IsExpanded=false;
        Save(root,Path.Combine(directory,"updates-clean.png"),1160,860);
        var oldEdge=model.ScreenEdge;var oldSize=model.WheelSizePercent;
        var levels=model.DisplayWheels.Select(w=>(w.Id,w.Level,w.Muted)).ToArray();
        var bindings=model.Wheels.Select(w=>(w.Id,w.ChannelId,w.MixId)).ToArray();
        window.OpenSetup();Flush(root,1160,860);
        Assert(window.SetupStep==0&&((FrameworkElement)window.FindName("SetupPage")).Visibility==Visibility.Visible,"The replayable guide must start at Audio.");
        Save(root,Path.Combine(directory,"setup-audio.png"),1160,860);
        Click(window,"SetupNextButton");Flush(root,1000,740);
        Assert(window.SetupStep==1,"Continue must advance to Placement.");
        var monitors=(ComboBox)window.FindName("SetupMonitorBox");var previous=model.MonitorId;
        monitors.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,null);window.ApplySetupMonitor();
        Assert(model.MonitorId==previous,"A transient empty setup display selection must preserve the saved monitor.");
        model.ScreenEdge="Right";model.WheelSizePercent=115;
        Save(root,Path.Combine(directory,"setup-placement-minimum.png"),1000,740);
        Click(window,"SetupNextButton");Flush(root,1000,740);
        var demo=(WheelControl)window.FindName("SetupDemo");var before=demo.Wheel!.Level;
        demo.AdjustInput(1,false,0);demo.TryMuteAt(WheelControl.MuteCenter);
        Assert(demo.Wheel.Level>before&&demo.Wheel.Muted,"The sample wheel must allow volume and mute practice.");
        Assert(levels.SequenceEqual(model.DisplayWheels.Select(w=>(w.Id,w.Level,w.Muted)))&&bindings.SequenceEqual(model.Wheels.Select(w=>(w.Id,w.ChannelId,w.MixId))),"Practicing and navigating setup must not change live audio or channel bindings.");
        Assert(((Button)window.FindName("SetupNextButton")).Content.ToString()=="Finish setup","The last step must offer a clear finish action.");
        Save(root,Path.Combine(directory,"setup-try-minimum.png"),1000,740);
        Click(window,"SetupBackButton");Assert(window.SetupStep==1,"Back must return to Placement.");
        Click(window,"SetupNextButton");Click(window,"SetupNextButton");Flush(root,1160,860);
        Assert(((FrameworkElement)window.FindName("WheelsPage")).Visibility==Visibility.Visible&&model.ScreenEdge=="Right"&&model.WheelSizePercent==115,"Finishing setup must return to Wheels and keep explicit choices.");
        var saved=new SettingsStore(Path.GetDirectoryName(model.SettingsPath)!).Load();
        Assert(saved.ScreenEdge=="Right"&&saved.WheelSizePercent==115,"Setup choices must persist through the normal settings store.");
        window.OpenSetup();Assert(window.SetupStep==0&&model.ScreenEdge=="Right"&&model.WheelSizePercent==115,"Replaying setup must not reset preferences.");
        Click(window,"UpdatesNav");model.ScreenEdge=oldEdge;model.WheelSizePercent=oldSize;
        Click(window,"WheelsNav");Flush(root,1160,860);
        Bob(directory);
        Console.WriteLine("PASS: four-section navigation, selected parent tabs, visible update channel, guide back/finish/replay, persisted placement, isolated sample audio, and bounded volume bob.");
    }
    private static void Bob(string directory)
    {
        var wheel=new Wheel{Available=true,Level=.5};var control=new WheelControl{Wheel=wheel,ScrollAccelerationEnabled=false};
        control.VolumeRequested+=(w,value)=>w.Level=Math.Clamp(value,0,1);
        control.AdjustInput(1,false,0);control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(70));
        var offset=control.VolumeBobOffset;
        Assert(offset<0&&offset>-2.5&&control.VolumeBobStarts==1,"A real normal increase must produce a slight upward bob.");
        control.AdjustInput(-1,false,30);control.SeekInteractionFeedback(TimeSpan.Zero);
        Assert(Math.Abs(control.VolumeBobOffset-offset)<1e-7,"Rapid reversal must retarget from the visible bob position.");
        control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(320));Assert(control.VolumeBobOffset==0,"Volume bob must return to rest.");
        var starts=control.VolumeBobStarts;control.CanAdjustVolume=()=>false;control.AdjustInput(1,false,80);Assert(control.VolumeBobStarts==starts,"Blocked input must not bob.");
        control.CanAdjustVolume=()=>true;wheel.Level=1;control.AdjustInput(1,false,400);Assert(control.VolumeBobStarts==starts,"Capped input must use existing limit feedback, without a normal bob.");
        wheel.Level=.5;control.AdjustInput(1,true,1000);Assert(control.VolumeBobStarts==starts,"Shift changes must retain their stronger spring instead of stacking a second bob.");
        control.AdjustInput(-1,false,1100);control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(70));control.EffectsEnabled=false;Assert(control.VolumeBobOffset==0,"The animation toggle must clear a running bob.");
        control.EffectsEnabled=true;control.AdjustInput(1,false,1200);control.SeekInteractionFeedback(TimeSpan.FromMilliseconds(70));wheel.Available=false;Assert(control.VolumeBobOffset==0,"Disconnecting must clear the volume bob.");
    }
    private static void Flush(FrameworkElement root,int width,int height)
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        root.InvalidateMeasure();root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
    }
    private static void Save(FrameworkElement root,string path,int width,int height)
    {
        Flush(root,width,height);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
