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
        var store=new SettingsStore(directory);var vm=new MainViewModel(store,true);
        vm.Effects.BloomPercent=200;vm.Effects.SparkCount=400;vm.Effects.SparkSpeedPercent=300;vm.Effects.ShakePercent=200;vm.Effects.MuteSpeedPercent=50;vm.Effects.NormalColor="#123456";
        vm.Save();Assert(store.Load().Effects.SparkCount==400&&store.Load().Effects.NormalColor=="#123456","Effects must persist independently of profiles.");
        var control=new WheelControl{Wheel=new Wheel {Name="Effects",Level=1,Available=true},Effects=vm.Effects};control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=1;control.Measure(new Size(300,120));control.Arrange(new Rect(0,0,300,120));control.StartMaximumEffect();control.SeekMaximumEffect(TimeSpan.FromMilliseconds(100));control.UpdateLayout();
        var frame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);frame.Render(control);
        var pixel=new byte[4];frame.CopyPixels(new Int32Rect(60,70,1,1),pixel,4,0);Assert(pixel[2]==0x12&&pixel[1]==0x34&&pixel[0]==0x56,"Effects colors must reach the actual wheel renderer.");
        vm.RestoreDefaults("Effects");Assert(JsonSerializer.Serialize(vm.Effects)==JsonSerializer.Serialize(defaults),"Effects reset must restore every original default.");
        vm.Effects.NormalColor="invalid";vm.Save();Assert(store.Load().Effects.NormalColor=="#11141B","Invalid saved colors must recover to their original defaults.");vm.RestoreDefaults("Effects");
        var wheel=vm.Wheels[0];wheel.ChannelId="fixture";wheel.MixId="";wheel.Available=true;wheel.Level=.5;wheel.MaximumVolumePercent=60;
        vm.ExecuteHotkey(new(){Action="Volume up",WheelId=wheel.Id});Assert(wheel.Level==.51,"Volume hotkeys must use configured steps.");
        wheel.Level=.6;vm.ExecuteHotkey(new(){Action="Volume up",WheelId=wheel.Id});Assert(wheel.Level==.6,"Hotkeys must respect wheel caps.");
        vm.ExecuteHotkey(new(){Action="Toggle mute",WheelId=wheel.Id});Assert(wheel.Muted,"Mute hotkeys must target the saved wheel identity.");
        vm.ExecuteHotkey(new(){Action="Toggle wheels"});Assert(vm.IsPreviewing,"Reveal hotkeys must toggle the preview.");vm.ExecuteHotkey(new(){Action="Toggle wheels"});Assert(!vm.IsPreviewing,"Reveal hotkeys must also dismiss it.");
        var originalProfile=vm.ActiveProfile;vm.AddProfile(false);vm.ExecuteHotkey(new(){Action="Previous profile"});Assert(vm.ActiveProfile==originalProfile,"Profile hotkeys must cycle layouts.");
        wheel=vm.Wheels[0];wheel.Available=true;wheel.Level=.5;
        vm.AddBlacklistedApplication("blocked.exe");vm.ApplyForegroundProcess("blocked.exe",0);
        vm.ExecuteHotkey(new(){Action="Volume up",WheelId=wheel.Id});Assert(wheel.Level==.5,"App blacklist must suppress hotkey volume changes.");vm.ApplicationBlacklistEnabled=false;
        vm.ScreenEdge="Right";vm.EdgeDistance=100;vm.WheelSizePercent=145;vm.Effects.SparkCount=300;
        vm.RestoreDefaults("Placement");Assert(vm.ScreenEdge=="Left"&&vm.EdgeDistance==32&&vm.WheelSizePercent==145&&vm.Effects.SparkCount==300,"Placement reset must not change other pages.");
        vm.RestoreDefaults("Settings");Assert(vm.WheelSizePercent==100&&vm.ScrollStepPercent==1&&vm.ShiftStepPercent==20&&vm.Effects.SparkCount==300&&vm.BlacklistedApplications.Count==0,"Settings reset must remain scoped and restore original values.");
        vm.SelectedWheel=wheel;wheel.Name="Custom";wheel.Color="#FFFFFF";vm.RestoreDefaults("Wheels");Assert(wheel.ChannelId=="fixture"&&wheel.Name=="Music"&&wheel.MaximumVolumePercent==100,"Wheel reset must preserve channel bindings and identity.");
        Assert(GlobalHotkeys.TryPlan(new[]{new HotkeyBinding{Shortcut="Ctrl+Alt+W"}},out var plan,out _)&&plan.Count==1&&plan[0].Modifiers==3,"Hotkey parsing must normalize modifiers and keys.");
        Assert(!GlobalHotkeys.TryPlan(new[]{new HotkeyBinding{Shortcut="Ctrl+Alt+W"},new HotkeyBinding{Shortcut="alt+control+w"}},out _,out _),"Duplicate hotkeys must be rejected regardless of modifier order.");
        foreach(var invalid in new[]{"W","Shift+W","Ctrl+Ctrl+W","Ctrl+Unknown","Ctrl+"})Assert(!GlobalHotkeys.TryPlan(new[]{new HotkeyBinding{Shortcut=invalid}},out _,out _),"Invalid shortcut accepted: "+invalid);
        var registered=new Dictionary<int,uint>();var dispatches=0;uint blocked=0;
        using(var manager=new GlobalHotkeys(_=>dispatches++,(id,modifiers,key)=> {Assert((modifiers&0x4000)!=0,"Hotkeys must suppress key-repeat.");if(key==blocked)return false;registered[id]=key;return true;},id=>registered.Remove(id)))
        {
            Assert(manager.Apply(plan,out _),"Initial hotkey registration failed.");manager.Dispatch(1);Assert(dispatches==1,"Registered hotkey must dispatch exactly once.");
            GlobalHotkeys.TryPlan(new[]{new HotkeyBinding{Shortcut="Ctrl+Alt+Q"}},out var conflict,out _);blocked=conflict[0].Key;
            Assert(!manager.Apply(conflict,out var conflictError)&&conflictError.Contains("unavailable")&&registered[1]==plan[0].Key,"A conflict must restore previously applied registrations.");
            manager.Dispatch(1);Assert(dispatches==2,"Previous action must remain available after a conflict.");
            manager.Apply(Array.Empty<HotkeyCommand>(),out _);manager.Dispatch(1);Assert(registered.Count==0&&dispatches==2,"Disabled shortcuts must unregister and stop dispatching.");
        }
        vm.Hotkeys.Clear();vm.Hotkeys.Add(new(){Shortcut="Ctrl+Alt+W"});vm.HotkeysEnabled=true;vm.ApplyHotkeys();vm.Save();Assert(store.Load().HotkeysEnabled&&store.Load().Hotkeys[0].Shortcut=="Ctrl+Alt+W","Only applied hotkey configuration must persist.");
        vm.RestoreDefaults("Hotkeys");vm.Save();Assert(!store.Load().HotkeysEnabled&&store.Load().Hotkeys.All(h=>h.Shortcut.Length==0),"Hotkey reset must disable and clear combinations.");
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true")
        {
            GlobalHotkeys.TryPlan(new[]{new HotkeyBinding{Shortcut="Ctrl+Alt+Shift+F23"}},out var nativePlan,out _);
            using var first=new GlobalHotkeys(_=>{});using var second=new GlobalHotkeys(_=>{});
            Assert(first.Apply(nativePlan,out _)&&!second.Apply(nativePlan,out _),"Native registration must detect occupied shortcuts on the disposable runner.");
            first.Apply(Array.Empty<HotkeyCommand>(),out _);Assert(second.Apply(nativePlan,out _),"Native shortcut disposal must free the combination.");
        }
        vm.Dispose();Console.WriteLine("PASS: effects defaults/live rendering/persistence, scoped resets, hotkey validation/dispatch/conflict rollback, cap/blacklist guards, and registration cleanup.");
    }
}
