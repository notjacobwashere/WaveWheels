using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WaveWheels;

internal static class UndoWheelTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var vm=new MainViewModel(new SettingsStore(Path.Combine(directory,"undo-wheel-profile")),true);
        vm.Update(new MixerSnapshot(true,"Fixture","Wave Link 3",new[]{new Channel("music","Music",.73,true,true,Array.Empty<ChannelMix>())},Array.Empty<Mix>()));
        var window=new SettingsWindow(vm);var button=(Button)window.FindName("UndoWheelDeletionButton");button.DataContext=vm;var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);
        button.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();Assert(!button.IsEnabled&&!vm.CanUndoWheelDeletion,"Undo must start disabled.");
        var original=vm.Wheels[1];original.Name="Restorable";original.Color="#123456";original.ChannelId="music";original.MaximumVolumePercent=65;
        vm.RemoveWheel(original);button.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();Assert(button.IsEnabled&&vm.CanUndoWheelDeletion,"A deletion must enable the actual undo button.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(ReferenceEquals(vm.Wheels[1],original)&&vm.SelectedWheel==original&&original.Name=="Restorable"&&original.Color=="#123456"&&original.ChannelId=="music"&&original.MaximumVolumePercent==65,"Undo must restore identity, position, selection, appearance, limit, and binding.");
        Assert(original.Level==.73&&original.Muted&&!vm.CanUndoWheelDeletion,"Restoration must use current mixer state and exhaust the undo entry.");
        var second=vm.Wheels[0];vm.RemoveWheel(original);vm.RemoveWheel(second);vm.UndoWheelDeletion();vm.UndoWheelDeletion();Assert(vm.Wheels[0]==second&&vm.Wheels[1]==original,"Repeated undo must restore deletions in reverse order.");
        vm.RemoveWheel(original);var profile=vm.ActiveProfile!;vm.AddProfile(true);Assert(!vm.CanUndoWheelDeletion,"A new profile must not inherit another profile's undo history.");vm.ActiveProfile=profile;Assert(vm.CanUndoWheelDeletion,"Switching back must restore that profile's undo availability.");
        vm.UseWindowsAudio=true;vm.UndoWheelDeletion();Assert(!vm.CanUndoWheelDeletion&&!vm.Wheels.Contains(original),"Windows mode must block Wave Link layout undo.");vm.UseWindowsAudio=false;
        vm.WheelCount=12;Assert(!vm.CanUndoWheelDeletion,"Undo must not exceed the twelve-wheel limit.");vm.WheelCount=11;vm.UndoWheelDeletion();Assert(vm.Wheels.Count==12,"Reducing the count must make its own deletion undoable.");
        vm.WheelCount=1;vm.RemoveWheel();Assert(vm.Wheels.Count==1,"Deleting the final wheel must remain blocked.");
        while(vm.CanUndoWheelDeletion)vm.UndoWheelDeletion();vm.Save();var saved=new SettingsStore(Path.GetDirectoryName(vm.SettingsPath)!).Load();Assert(saved.Wheels.Select(w=>w.Id).SequenceEqual(vm.Wheels.Select(w=>w.Id)),"Restored layouts must persist through the normal settings store.");
        vm.Dispose();
        Console.WriteLine("PASS: undo button state, restored wheel identity/layout/selection/current audio, repeated deletions, profile isolation, Windows-mode guard, capacity limits, and persistence.");
    }
}
