using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WaveWheels;

internal static class MonitorPersistenceTests
{
    private const string A=@"\\?\DISPLAY#MONITOR_A#INSTANCE_A";
    private const string B=@"\\?\DISPLAY#MONITOR_B#INSTANCE_B";
    private const string LegacyA=@"MONITOR\MONITOR_A\OLD_INSTANCE";
    private static readonly MonitorDescriptor[] Original={new(@"\\.\DISPLAY42",A,"Display 1 · primary · 1920×1080",LegacyA),new(@"\\.\DISPLAY43",B,"Display 2 · 2560×1440")};
    private static readonly MonitorDescriptor[] Reordered={new(@"\\.\DISPLAY42",B,"Display 1 · primary · 2560×1440"),new(@"\\.\DISPLAY43",A,"Display 2 · 1920×1080",LegacyA)};
    private static void Assert(bool condition,string reason){if(!condition)throw new Exception(reason);}
    public static void Pure()
    {
        Assert(MonitorSelection.Resolve(@"\\.\DISPLAY42",Original)==A,"A legacy DISPLAYn value must migrate to its currently mapped hardware identity.");
        Assert(MonitorSelection.Resolve(A,Reordered)==A&&MonitorSelection.Matches(A,Reordered[1])&&!MonitorSelection.Matches(A,Reordered[0]),"Saved hardware selection must follow the same monitor after GDI numbering reverses.");
        Assert(MonitorSelection.Resolve(A.ToLowerInvariant(),Reordered)==A,"Windows display identities must match case-insensitively.");
        Assert(MonitorSelection.Resolve(LegacyA,Original)==A,"Earlier raw DeviceID values must migrate to the monitor interface identity.");
        Assert(MonitorSelection.Resolve("",Original)==""&&Original.All(m=>MonitorSelection.Matches("",m)),"All displays must continue matching every connected display.");
        Assert(MonitorSelection.Resolve(A,new[]{Reordered[0]})==A&&!MonitorSelection.Matches(A,Reordered[0]),"A disconnected monitor must remain saved without selecting another monitor.");
        Assert(MonitorSelection.Resolve(@"\\.\DISPLAY99",Original)==@"\\.\DISPLAY99","Unresolved legacy settings must survive until their mapping is available.");
        var fallback=new MonitorDescriptor(@"\\.\DISPLAY42",@"\\.\DISPLAY42","Virtual display");
        Assert(MonitorSelection.Resolve(fallback.Id,new[]{fallback})==fallback.Id&&MonitorSelection.Resolve(fallback.Id,Original)==A,"Unavailable hardware IDs must retain compatibility and retry migration later.");
        var ambiguous=new[]{Original[0],Original[1] with{PreviousId=LegacyA}};
        Assert(MonitorSelection.Resolve(LegacyA,ambiguous)==LegacyA,"Ambiguous historical identities must not be silently reassigned.");
        Assert(!ambiguous.Any(m=>MonitorSelection.Matches(LegacyA,m)),"Ambiguous historical aliases must not activate multiple monitors.");
        var native=System.Windows.Forms.Screen.AllScreens.Select(MonitorDescriptor.Capture).ToArray();
        Assert(native.Length>0&&native.All(m=>m.Id.Length>0&&m.Label.Length>0),"Native display capture must yield usable IDs/labels, including virtual-device fallbacks.");
        Console.WriteLine("PASS: monitor hardware IDs, DISPLAYn/raw-ID migration, renumbering, case handling, disconnect/reconnect, ambiguity, all-displays, and native enumeration.");
    }
    public static void ViewModel(string directory)
    {
        Directory.CreateDirectory(directory);var store=new SettingsStore(directory);var preferences=new Preferences{MonitorId=@"\\.\DISPLAY42",ScreenEdge="Right",WheelSizePercent=125,PositionPercent=35};preferences.Wheels[0].ChannelId="fixture-music";store.Save(preferences);
        var vm=new MainViewModel(store,true);vm.ApplyMonitorChoices(Original);Assert(vm.MonitorId==A&&vm.Monitors.Any(c=>c.Id==A)&&!vm.Monitors.Any(c=>c.Id==@"\\.\DISPLAY42"),"Monitor choices and saved selection must use hardware IDs after migration.");
        vm.Save();var saved=store.Load();Assert(saved.MonitorId==A&&saved.ScreenEdge=="Right"&&saved.PositionPercent==35&&saved.WheelSizePercent==125&&saved.Wheels[0].ChannelId=="fixture-music","Migration must persist without altering other placement/wheel preferences.");
        var window=new SettingsWindow(vm);var root=(FrameworkElement)window.Content;window.Content=null;root.DataContext=vm;root.Resources=window.Resources;
        ((Button)window.FindName("PlacementNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));Flush(root);
        var picker=(ComboBox)window.FindName("MonitorBox");Assert((string?)picker.SelectedValue==A,"Placement must initially select the saved hardware ID.");
        vm.ApplyMonitorChoices(Reordered);Flush(root);Assert(vm.MonitorId==A&&(string?)picker.SelectedValue==A&&vm.Monitors.Single(c=>c.Id==A).Name.StartsWith("Display 2"),"Renumbering must refresh the friendly choice without changing the selected hardware.");
        vm.ApplyMonitorChoices(new[]{Reordered[0]});Flush(root);Assert(vm.MonitorId==A&&(string?)picker.SelectedValue==A&&vm.Monitors.Single(c=>c.Id==A).Name.Contains("disconnected"),"Disconnected selection must remain visible and saved.");
        vm.ApplyMonitorChoices(Reordered);Flush(root);Assert(vm.MonitorId==A&&(string?)picker.SelectedValue==A&&!vm.Monitors.Single(c=>c.Id==A).Name.Contains("disconnected"),"Reconnect must restore the normal friendly choice.");
        vm.MonitorId=null!;Assert(vm.MonitorId==A,"Transient null selection must not erase a saved monitor.");
        picker.SetCurrentValue(Selector.SelectedValueProperty,"");window.ApplyMonitor();Assert(vm.MonitorId=="","Explicit All displays selection must remain functional.");
        vm.MonitorId=LegacyA;vm.ApplyMonitorChoices(Original);Flush(root);Assert(vm.MonitorId==A&&(string?)picker.SelectedValue==A,"Migration must also update an already open placement editor.");
        vm.Save();Assert(store.Load().MonitorId==A,"The final stable identity must survive reload.");vm.Dispose();
        Console.WriteLine("PASS: monitor migration persistence, unchanged wheel/placement settings, live placement choices, renumber/disconnect/reconnect, transient null protection, and explicit All displays.");
    }
    private static void Flush(FrameworkElement root)
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);root.UpdateLayout();
    }
}
