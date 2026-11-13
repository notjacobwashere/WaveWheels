using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WaveWheels;
internal static class ClickActionTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var store=new SettingsStore(Path.Combine(directory,"click-actions-"+Guid.NewGuid().ToString("N")));var model=new MainViewModel(store,true);
        var wheel=model.Wheels[0];wheel.ChannelId="music";wheel.MixId="a";model.SelectedWheel=wheel;
        MixerSnapshot Snapshot(bool overall)=>new(true,"Fixture","Wave Link 3",new[]{new Channel("music","Music",.5,false,true,new[]{new ChannelMix("a",.3,false),new ChannelMix("b",.7,false)},overall)},new[]{new Mix("a","Headphones"),new Mix("b","Stream")});
        model.Update(Snapshot(false));
        Assert(wheel.LeftClickAction=="Mute channel"&&wheel.RightClickAction=="None","Existing and fresh wheels must default to whole-channel left mute and no right action.");
        var control=new WheelControl{Wheel=wheel};control.ClickRequested+=model.ExecuteClick;
        Assert(control.TryClickAt(new Point(60,60),true)&&!wheel.Muted,"Default right-click must not mute.");
        control.TryClickAt(new Point(60,60),false);Assert(wheel.Muted,"Default left click must mute.");wheel.MixId="b";Assert(wheel.Muted,"Whole-channel mute must cover every output when the protocol has no overall target.");
        model.ExecuteClick(wheel,false);Assert(!wheel.Muted,"A second whole-channel click must unmute all outputs.");wheel.MixId="a";Assert(!wheel.Muted,"Whole-channel unmute must include the other output.");
        wheel.RightClickAction="Mute output";wheel.RightClickOutput="b";model.ExecuteClick(wheel,true);Assert(!wheel.Muted,"Output mute must leave the selected other output unchanged.");wheel.MixId="b";Assert(wheel.Muted,"Output action must target the specified output.");model.ExecuteClick(wheel,true);Assert(!wheel.Muted,"Output mute must toggle from the output's own state.");
        wheel.RightClickOutput="missing";model.ExecuteClick(wheel,true);Assert(!wheel.Muted&&model.Message.Contains("unavailable"),"Unavailable outputs must not fall back to whole-channel muting.");
        model.Update(Snapshot(true));wheel.MixId="a";model.ExecuteClick(wheel,false);Assert(wheel.Muted,"Output wheels must show effective whole-channel mute.");wheel.MixId="";Assert(wheel.Muted,"A protocol with overall mute must target the whole channel even when the wheel is scoped to an output.");
        wheel.RightClickAction="Set volume";wheel.RightClickVolume=90;wheel.MaximumVolumePercent=40;model.ExecuteClick(wheel,true);Assert(wheel.Level==.4,"Preset volume must respect the configured cap.");
        wheel.LeftClickAction="Set volume";wheel.LeftClickVolume=15;model.ExecuteClick(wheel,false);Assert(wheel.Level==.15,"Left and right volume presets must be independent.");
        wheel.LeftClickVolume=-1;wheel.RightClickVolume=200;Assert(wheel.LeftClickVolume==0&&wheel.RightClickVolume==100,"Stored preset volumes must be bounded.");
        wheel.LeftClickAction="bad";Assert(wheel.LeftClickAction=="None","Unknown actions must fail closed.");
        wheel.LeftClickAction="Mute output";wheel.LeftClickOutput="a";wheel.RightClickVolume=35;model.Save();var saved=store.Load().Wheels[0];
        Assert(saved.LeftClickAction=="Mute output"&&saved.LeftClickOutput=="a"&&saved.RightClickVolume==35,"Click settings must survive save/load.");
        var copied=ProfileTransfer.CloneWheel(wheel);Assert(copied.LeftClickAction==wheel.LeftClickAction&&copied.LeftClickOutput=="a"&&copied.RightClickVolume==35,"Profile copies must preserve click settings.");
        var imported=ProfileTransfer.Parse(ProfileTransfer.Serialize(new[]{model.ActiveProfile!}))[0].Wheels[0];Assert(imported.LeftClickOutput=="a"&&imported.RightClickVolume==35,"Profile export/import must preserve actions.");
        var window=new SettingsWindow(model);Assert(window.FindName("LeftClickSection") is Expander&&window.FindName("RightClickSection") is Expander,"Both click actions must have their own wheel sections.");
        var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);
        ((ComboBox)window.FindName("LeftClickActionBox")).SelectedItem="Set volume";Assert(wheel.LeftClickAction=="Set volume","The left action selector must edit the selected wheel.");
        wheel.LeftClickAction="Mute output";wheel.LeftClickOutput="a";wheel.RightClickAction="Set volume";
        var panel=new StackPanel{Margin=new Thickness(24),DataContext=model,Resources=window.Resources};panel.SetValue(Control.ForegroundProperty,System.Windows.Media.Brushes.White);
        foreach(var name in new[]{"LeftClickSection","RightClickSection"}){var section=(Expander)window.FindName(name);((Panel)section.Parent).Children.Remove(section);section.IsExpanded=true;panel.Children.Add(section);}
        var surface=new Border{Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16,24,32)),Child=panel};surface.Measure(new Size(520,650));surface.Arrange(new Rect(0,0,520,650));surface.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(520,650,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(directory,"click-action-sections.png")))encoder.Save(output);
        model.UseWindowsAudio=true;var windows=model.WindowsWheel;windows.Available=true;windows.RightClickAction="Mute output";model.ExecuteClick(windows,true);Assert(model.Message.Contains("requires Wave Link"),"Windows audio must explain unsupported output-specific muting.");
        model.RestoreDefaults("Wheels");Assert(windows.LeftClickAction=="Mute channel"&&windows.RightClickAction=="None","Restore defaults must reset click actions.");
        model.Dispose();Console.WriteLine("PASS: per-button routing, whole-channel/output mute isolation and toggling, protocol fallback, presets/caps, unavailable targets, persistence, profile transfer, Windows fallback, and click action sections.");
    }
}
