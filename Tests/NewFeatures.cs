using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using WaveWheels;

internal static class NewFeatures
{
    private static void Assert(bool value,string reason) {if(!value)throw new Exception(reason);}
    private static void Reject(Action action)
    {
        try {action();}catch(Exception ex) when(ex is InvalidDataException or JsonException) {return;}
        throw new Exception("An invalid profile import was accepted.");
    }
    public static void Pure()
    {
        var scroll=new ScrollAccumulator();
        Assert(scroll.Adjust(1,3,false,0)==3&&scroll.Adjust(1,3,false,50)==3&&scroll.Adjust(-1,3,false,80)==-3,"Disabled acceleration must produce consistent steps in either direction.");
        scroll.Reset();for(var i=0;i<3;i++)Assert(scroll.Adjust(.25,1,false,i*20)==0,"Fractional scroll input must accumulate.");
        Assert(scroll.Adjust(.25,1,false,60)==1,"One complete high-resolution notch must produce one step.");
        scroll.Reset();Assert(scroll.Adjust(1,1,true,0)==1&&scroll.Adjust(1,1,true,20)==1&&scroll.Adjust(1,1,true,40)>1,"Fast scrolling must accelerate when enabled.");
        Assert(scroll.Adjust(-1,1,true,60)==-1&&scroll.Adjust(1,1,true,500)==1,"Direction changes and pauses must reset acceleration.");
        scroll.Reset();scroll.Adjust(.5,1,false,0);Assert(scroll.Adjust(1,2,false,20)==2,"Changing tuning must discard old fractional input.");
        Assert(scroll.Adjust(double.NaN,2,false,30)==0,"Non-finite scroll input must be ignored.");
        var baseline=new WheelProfile{Id="base",Name="Default",Wheels=new(){new Wheel()}};
        var gaming=new WheelProfile{Id="game",Name="Gaming",Applications="game.exe, othergame.exe",Wheels=new(){new Wheel{Name="Game",ChannelId="fake-game",MixId="personal",MaximumVolumePercent=65}}};
        var streaming=new WheelProfile{Id="stream",Name="Streaming",Applications="obs64.exe",Wheels=new(){new Wheel()}};
        var profiles=new[]{baseline,gaming,streaming};var automation=new ProfileAutomation();
        Assert(ProfileAutomation.Matches(gaming.Applications,"GAME")&&!ProfileAutomation.Matches(gaming.Applications,"gamehelper"),"Rules must match whole executable names, case-insensitively.");
        Assert(ProfileAutomation.Matches("game.win64.shipping.exe","game.win64.shipping"),"Dotted executable names must retain their base name.");
        Assert(automation.Evaluate("game",profiles,"base",0)==null&&automation.Evaluate("game",profiles,"base",499)==null&&automation.Evaluate("game",profiles,"base",500)=="game","Focus must stabilize before activating a matching profile.");
        Assert(automation.Evaluate(null,profiles,"base",600)==null,"The configuration window must not force a fallback.");
        automation.ManualSelection();Assert(automation.Evaluate("game",profiles,"stream",1000)==null,"Manual selection must take priority for the current app.");
        automation.Evaluate("obs64",profiles,"base",1100);Assert(automation.Evaluate("obs64",profiles,"base",1600)=="stream","Focus changes must release manual overrides.");
        automation.Evaluate("explorer",profiles,"base",1700);Assert(automation.Evaluate("explorer",profiles,"base",2200)=="base","Unmatched apps must restore the manual layout.");
        var json=ProfileTransfer.Serialize(new[]{gaming});var imported=ProfileTransfer.Parse(json);
        Assert(imported[0].Name=="Gaming"&&imported[0].Applications==gaming.Applications&&imported[0].Wheels[0].MaximumVolumePercent==65&&imported[0].Wheels[0].ChannelId=="fake-game","Exports must retain rules, limits, and bindings.");
        Assert(imported[0].Id!=gaming.Id&&imported[0].Wheels[0].Id!=gaming.Wheels[0].Id,"Imported identities must be independent.");
        Assert(!json.Contains("\"Level\"")&&!json.Contains("\"Muted\"")&&!json.Contains("\"Available\""),"Live mixer values must not be exported.");
        Reject(()=>ProfileTransfer.Parse("{}"));Reject(()=>ProfileTransfer.Parse(json.Replace("\"Version\": 1","\"Version\": 99")));Reject(()=>ProfileTransfer.Parse(new string(' ',2*1024*1024+1)));
        Reject(()=>ProfileTransfer.Parse(json.Replace("#36BBD3","not-a-color")));
        Console.WriteLine("PASS: scroll tuning, focus debounce, manual override/fallback, portable profile identity, rules/limits, and invalid imports.");
    }
    public static void ViewModel(string directory)
    {
        Directory.CreateDirectory(directory);
        var store=new SettingsStore(directory);var vm=new MainViewModel(store,true);
        vm.WheelCount=2;var music=vm.Wheels[0];var game=vm.Wheels[1];
        music.ChannelId="fake-music";music.MixId="personal";game.ChannelId="fake-game";game.MixId="personal";
        vm.Update(new(true,"Fixture mixer","Wave Link 3",new[]{new Channel("fake-music","Music",.8,false,true,new[]{new ChannelMix("personal",.8,false)}),new Channel("fake-game","Game",.7,false,true,new[]{new ChannelMix("personal",.7,false)})},new[]{new Mix("personal","Headphones")}));
        music.MaximumVolumePercent=60;Assert(music.Level==.8,"Changing a limit must not change the mixer automatically.");
        vm.SetLevel(music,1);Assert(music.Level==.6&&game.Level==.7,"App writes must respect the wheel limit without changing other targets.");
        vm.SetLevel(music,double.NaN);Assert(music.Level==.6,"Non-finite levels must not alter volume.");
        vm.AddWheel();var duplicate=vm.SelectedWheel!;duplicate.ChannelId="fake-music";duplicate.MixId="personal";duplicate.MaximumVolumePercent=40;
        var control=new WheelControl{Wheel=music};control.VolumeRequested+=vm.SetLevel;
        var peer=FrameworkElementAutomationPeer.CreatePeerForElement(control)!;
        ((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!).SetValue(100);
        Assert(music.Level==.4&&duplicate.Level==.4,"All app input paths and duplicate bindings must use the strictest active limit.");
        var baseline=vm.ActiveProfile!;vm.AddProfile(true);var gaming=vm.ActiveProfile!;gaming.Name="Gaming";gaming.Applications="fakegame.exe";
        vm.ActiveProfile=baseline;vm.AutomaticProfilesEnabled=true;
        var before=vm.Wheels.Select(w=>w.Level).ToArray();vm.ApplyForegroundProcess("fakegame",0);vm.ApplyForegroundProcess("fakegame",500);
        Assert(vm.ActiveProfile==gaming&&vm.Preferences.ManualProfileId==baseline.Id,"Automatic selection must preserve the fallback profile.");
        var activeLevels=vm.Wheels.Select(w=>w.Level).ToArray();vm.SetLevel(music,1);vm.ToggleMute(music);
        Assert(vm.Wheels.Select(w=>w.Level).SequenceEqual(activeLevels)&&!vm.Wheels.Any(w=>w.Muted),"Stale controls from a previous profile must not change the new layout's channels.");
        vm.ApplyForegroundProcess("explorer",1000);vm.ApplyForegroundProcess("explorer",1500);Assert(vm.ActiveProfile==baseline,"View-model automation must return to the prior manual layout.");
        var file=Path.Combine(directory,"profiles.wavewheels.json");vm.ExportProfiles(file,true);var count=vm.Profiles.Count;vm.ImportProfiles(file);
        Assert(vm.Profiles.Count==count*2&&vm.Profiles.Select(p=>p.Id).Distinct().Count()==vm.Profiles.Count,"Import must append independent profiles.");
        Assert(vm.Profiles.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==vm.Profiles.Count,"Import must disambiguate existing profile names.");
        var invalid=Path.Combine(directory,"invalid.json");File.WriteAllText(invalid,"{}");count=vm.Profiles.Count;Reject(()=>vm.ImportProfiles(invalid));Assert(vm.Profiles.Count==count,"Invalid files must leave every existing profile intact.");
        vm.ScrollStepPercent=3;vm.ScrollAccelerationEnabled=false;vm.IdleMilliseconds=2500;vm.Save();var saved=store.Load();
        Assert(saved.ScrollStepPercent==3&&!saved.ScrollAccelerationEnabled&&saved.IdleMilliseconds==2500&&saved.AutomaticProfilesEnabled&&saved.Profiles.Any(p=>p.Applications=="fakegame.exe")&&saved.Wheels.Any(w=>w.MaximumVolumePercent==60),"New preferences and imported limits must survive reload.");
        vm.Dispose();Console.WriteLine("PASS: volume caps, duplicate targets, automation integration, additive imports, and new preference persistence.");
    }
}
