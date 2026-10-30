using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WaveWheels;

internal static class ChannelEditorSmoke
{
    private static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++){var child=VisualTreeHelper.GetChild(element,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static void Flush(FrameworkElement root)
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);root.UpdateLayout();
    }
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);var store=new SettingsStore(directory);var settings=new Preferences();settings.Wheels[0].ChannelId="music";settings.Wheels[0].MixId="personal";store.Save(settings);
        var vm=new MainViewModel(store,true);
        MixerSnapshot Fixture(string name)=>new(true,"Fixture mixer","Wave Link 3",new[]{new Channel("music",name,.6,false,true,new[]{new ChannelMix("personal",.6,false)})},new[]{new Mix("personal","Headphones")});
        vm.Update(Fixture("Music"));var first=vm.SelectedWheel;var window=new SettingsWindow(vm);var root=(FrameworkElement)window.Content;window.Content=null;root.DataContext=vm;root.Resources=window.Resources;root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));Flush(root);
        Descendants(root).OfType<Expander>().Single(e=>e.Header?.ToString()=="Audio controls").IsExpanded=true;Flush(root);
        var channel=(ComboBox)window.FindName("ChannelBox");var mix=(ComboBox)window.FindName("MixBox");
        Assert((string?)channel.SelectedValue=="music"&&(string?)mix.SelectedValue=="personal"&&Descendants(channel).OfType<TextBlock>().Any(t=>t.Text=="Music"),"Opening audio controls must show the saved channel and mix without switching wheels.");
        Assert(((WheelControl)window.FindName("SelectedPreview")).Wheel==first&&vm.SelectedWheel==first,"The initial preview must remain bound to the selected wheel.");
        vm.Update(Fixture("Music renamed"));Flush(root);Assert((string?)channel.SelectedValue=="music"&&Descendants(channel).OfType<TextBlock>().Any(t=>t.Text=="Music renamed"),"Refreshing channel choices must not leave a blank selection.");
        Assert(vm.Channels.Any(c=>c.Id==""&&c.Name=="None")&&!vm.Channels.Any(c=>c.Name.Contains("Choose an input")),"The unbound option must be explicitly named None.");
        channel.SetCurrentValue(Selector.SelectedValueProperty,"");window.ApplyChannel();Flush(root);Assert(vm.SelectedWheel!.ChannelId==""&&(string?)channel.SelectedValue==""&&Descendants(channel).OfType<TextBlock>().Any(t=>t.Text=="None"),"Selecting None must unbind and remain visibly selected.");
        Assert(vm.ShiftStepPercent==20,"Existing profiles must retain the default 20% Shift step.");
        Assert(!vm.ShowShiftMarkers,"Shift dots must be hidden by default.");
        ((CheckBox)window.FindName("ShowShiftMarkersToggle")).SetCurrentValue(ToggleButton.IsCheckedProperty,true);Flush(root);
        Assert(vm.ShowShiftMarkers&&((WheelControl)window.FindName("SelectedPreview")).ShowShiftMarkers,"The marker toggle must update the live preview.");
        var slider=(Slider)window.FindName("ShiftStepSlider");var normal=vm.ScrollStepPercent;slider.SetCurrentValue(Slider.ValueProperty,35d);Flush(root);
        Assert(vm.ShiftStepPercent==35&&((WheelControl)window.FindName("SelectedPreview")).ShiftStepPercent==35&&vm.ScrollStepPercent==normal,"Shift settings must update preview input independently of normal scrolling.");
        vm.Save();Assert(store.Load().ShiftStepPercent==35&&store.Load().ShowShiftMarkers,"Shift step and dot visibility must persist.");vm.ShiftStepPercent=100;Assert(vm.ShiftStepPercent==50,"Shift settings must be bounded.");
        vm.Dispose();Console.WriteLine("PASS: immediate channel/mix/preview binding, refreshed choices, selectable None, and bounded/persisted independent Shift settings.");
    }
}
