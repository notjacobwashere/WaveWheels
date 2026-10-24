using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveWheels;

internal static class CustomizationTests
{
    private static void Assert(bool condition,string reason) {if(!condition)throw new Exception(reason);}
    internal static void Run(string directory)
    {
        var defaults=new EffectSettings();
        Assert(defaults.BloomPercent==100&&defaults.SparkCount==180&&defaults.SparkSpeedPercent==100&&defaults.ShakePercent==100&&defaults.MuteSpeedPercent==100&&defaults.NormalColor=="#11141B"&&defaults.MutedColor=="#26050D"&&defaults.MutedButtonColor=="#460715","Effects defaults must match the existing visual constants.");
        var store=new SettingsStore(directory);var vm=new MainViewModel(store,true);vm.Update(new(true,"Test mixer","",Array.Empty<Channel>(),Array.Empty<Mix>()));
        vm.Effects.BloomPercent=200;vm.Effects.SparkCount=400;vm.Effects.SparkSpeedPercent=300;vm.Effects.ShakePercent=200;vm.Effects.MuteSpeedPercent=50;vm.Effects.NormalColor="#123456";
        vm.Save();Assert(store.Load().Effects.SparkCount==400&&store.Load().Effects.NormalColor=="#123456","Effects must persist independently of profiles.");
        var control=new WheelControl{Wheel=new Wheel {Name="Effects",Level=1,Available=true},Effects=vm.Effects};control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=1;control.Measure(new Size(300,120));control.Arrange(new Rect(0,0,300,120));control.StartMaximumEffect();control.SeekMaximumEffect(TimeSpan.FromMilliseconds(100));control.UpdateLayout();
        var frame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);frame.Render(control);
        var pixel=new byte[4];frame.CopyPixels(new Int32Rect(60,70,1,1),pixel,4,0);Assert(pixel[2]==0x12&&pixel[1]==0x34&&pixel[0]==0x56,"Effects colors must reach the actual wheel renderer.");
        vm.RestoreDefaults("Effects");Assert(JsonSerializer.Serialize(vm.Effects)==JsonSerializer.Serialize(defaults),"Effects reset must restore every original default.");
        vm.Effects.NormalColor="invalid";vm.Save();Assert(store.Load().Effects.NormalColor=="#11141B","Invalid saved colors must recover to their original defaults.");vm.RestoreDefaults("Effects");
        var wheel=vm.Wheels[0];wheel.ChannelId="fixture";wheel.MixId="";wheel.Available=true;wheel.Level=.5;wheel.MaximumVolumePercent=60;
        vm.ScreenEdge="Right";vm.EdgeDistance=100;vm.WheelSizePercent=145;vm.Effects.SparkCount=300;
        vm.RestoreDefaults("Placement");Assert(vm.ScreenEdge=="Left"&&vm.EdgeDistance==32&&vm.WheelSizePercent==145&&vm.Effects.SparkCount==300,"Placement reset must not change other pages.");
        vm.RestoreDefaults("Settings");Assert(vm.WheelSizePercent==100&&vm.ScrollStepPercent==1&&vm.ShiftStepPercent==20&&vm.Effects.SparkCount==300&&vm.BlacklistedApplications.Count==0,"Settings reset must remain scoped and restore original values.");
        vm.SelectedWheel=wheel;wheel.Name="Custom";wheel.Color="#FFFFFF";vm.RestoreDefaults("Wheels");Assert(wheel.ChannelId=="fixture"&&wheel.Name=="Music"&&wheel.MaximumVolumePercent==100,"Wheel reset must preserve channel bindings and identity.");
        var migrationStore=new SettingsStore(Path.Combine(directory,"beta1-migration"));
        Directory.CreateDirectory(Path.GetDirectoryName(migrationStore.FilePath)!);
        var previous=JsonSerializer.Serialize(vm.Preferences);
        File.WriteAllText(migrationStore.FilePath,"{\"HotkeysEnabled\":true,\"Hotkeys\":[{\"Shortcut\":\"Ctrl+Alt+W\",\"Action\":\"Toggle wheels\"}],"+previous[1..]);
        var migrated=migrationStore.Load();
        Assert(migrationStore.Warning==null&&migrated.Wheels[0].ChannelId=="fixture"&&migrated.Effects.SparkCount==300,"Beta 1 hotkey settings must be ignored without resetting wheels or effects.");
        migrationStore.Save(migrated);
        Assert(!File.ReadAllText(migrationStore.FilePath).Contains("Hotkeys"),"Removed shortcut settings must not be written back.");
        vm.Dispose();Console.WriteLine("PASS: effects defaults/live rendering/persistence and scoped resets.");
    }
}
