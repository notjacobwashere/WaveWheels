using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using WaveWheels;

internal static class Program
{
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    private static async Task<MixerSnapshot> Wait(ConcurrentQueue<MixerSnapshot> snapshots,Func<MixerSnapshot,bool> predicate,int timeout=12000)
    {
        var until=DateTime.UtcNow.AddMilliseconds(timeout);
        while(DateTime.UtcNow<until) {while(snapshots.TryDequeue(out var s))if(predicate(s))return s;await Task.Delay(25);}
        throw new Exception("Timed out waiting for the expected mixer state.");
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--render")
            {
                UiRendering.Run(Path.GetFullPath(args[1]));Console.WriteLine("PASS: window render, real collection reordering, selection retention, size/motion persistence and animation clocks.");return 0;
            }
            var router=new OverlayInputRouter();
            var opening=router.Route(2,2,0);
            Assert(opening==new ScrollRoute(true,-1),"The first event must reveal without targeting any wheel.");
            for(var now=20;now<=2000;now+=20)Assert(router.Route(2,2,now)==new ScrollRoute(false,-1),"Every event of a long opening gesture must leave all volumes unchanged.");
            Assert(!router.CanAdjust(2249),"Direct wheel input must remain blocked until the full quiet gap.");
            Assert(router.Route(2,0,2250)==new ScrollRoute(false,2),"The first scroll after a quiet gap must adjust the hovered wheel.");
            Assert(router.Route(0,2,2260)==new ScrollRoute(false,0),"Once armed, continuous scrolling must work on the top wheel too.");
            Assert(router.Route(-1,2,2270)==new ScrollRoute(false,-1),"The edge must never adjust the previously hovered wheel.");
            Assert(opening.Index==-1,"A queued opening decision must remain ignored even after activation.");
            router.Close();Assert(router.Route(1,2,3000)==new ScrollRoute(true,-1),"Every reopening must reveal without adjusting.");
            for(var now=3020;now<=5000;now+=20)router.ObserveUnclaimedScroll(now);
            Assert(router.Route(0,0,5100).Index==-1,"Moving away while continuing the opening scroll must not bypass the pause.");
            Assert(router.Route(0,0,5350).Index==0,"A complete pause after returning must allow adjustment.");
            router.Close();router.Preview();
            Assert(router.Route(1,0,6000)==new ScrollRoute(false,1),"An explicit preview must allow adjustment immediately.");
            Console.WriteLine("PASS: opening burst suppression, quiet-gap activation, off-overlay scrolling, queued decisions, reopen reset, and preview.");
            var region=new EdgeRegion(-1920,-200,0,880,1.5);
            Assert(region.IsTrigger(-1920,0,"Left",6)&&region.IsTrigger(-1912,0,"Left",6)&&!region.IsTrigger(-1911,0,"Left",6),"The physical outermost edge and scaled strip must be included.");
            Assert(region.IsTrigger(-1,0,"Right",6)&&region.IsTrigger(-100,-200,"Top",6)&&region.IsTrigger(-100,879,"Bottom",6),"All four edges must include their outermost pixel.");
            Assert(!region.IsTrigger(0,0,"Right",6)&&!region.IsTrigger(-100,880,"Bottom",6),"Adjacent displays must not share boundary pixels.");
            var current=new Version(0,4,0,0);
            Assert(ReleaseUpdates.Parse("""{"tag_name":"v0.4.1","draft":false,"prerelease":false,"html_url":"https://evil.invalid"}""",current)?.Url=="https://github.com/notjacobwashere/WaveWheels/releases/tag/v0.4.1","Update links must be restricted to this repository.");
            Assert(ReleaseUpdates.Parse("""{"tag_name":"v0.4.0"}""",current)==null&&ReleaseUpdates.Parse("""{"tag_name":"v0.5.0","prerelease":true}""",current)==null,"Current versions and prereleases must not trigger notifications.");
            Console.WriteLine("PASS: physical monitor edges, DPI scaling, display boundaries, update versions and trusted links.");
            Assert(WheelOrdering.Destination(0,4,4)==3&&WheelOrdering.Destination(3,0,4)==0,"Reorder must work at both ends.");
            Assert(WheelOrdering.Destination(1,1,4)==1&&WheelOrdering.Destination(1,2,4)==1&&WheelOrdering.Destination(1,3,4)==2,"Insertion before/after must not have downward off-by-one errors.");
            var small=OverlaySizing.Calculate(3,50,1920,1080);var large=OverlaySizing.Calculate(3,150,1920,1080);var fitted=OverlaySizing.Calculate(12,180,1920,1080);
            Assert(Math.Abs(large.Width/small.Width-3)<.001&&fitted.Height<=1048,"Wheel sizing must scale proportionally and fit long stacks to the screen.");
            Console.WriteLine("PASS: directional insertion semantics and proportional screen-fitted wheel sizing.");
            if(args.Length==2 && args[0]=="--probe")
            {
                using var probe=new WaveLinkClient();var results=new ConcurrentQueue<MixerSnapshot>();probe.Updated+=results.Enqueue;probe.Start(int.Parse(args[1]));
                var state=await Wait(results,s=>s.Connected);Console.WriteLine($"READ-ONLY PROBE: {state.Protocol}; {state.Channels.Count} channels; {state.Mixes.Count} mixes.");return 0;
            }
            var node=(JsonObject)JsonNode.Parse("""{"id":"music","name":"Music","level":0.6,"isMuted":false,"mixes":[{"mixId":"personal","level":0.4,"isMuted":false},{"mixId":"stream","level":0.8,"isMuted":true}]}""")!;
            WaveLinkClient.Merge(node,(JsonObject)JsonNode.Parse("""{"mixes":[{"mixId":"personal","isMuted":true}]}""")!);
            var decoded=WaveLinkClient.Decode(node,false);
            Assert(decoded.Mixes.Count==2&&decoded.Mixes[0].Level==.4&&decoded.Mixes[0].Muted&&decoded.Mixes[1].Level==.8,"Partial notifications must preserve other fields and mixes.");
            Console.WriteLine("PASS: partial notification merge preserves unrelated mix levels.");
            var legacy=WaveLinkClient.Decode((JsonObject)JsonNode.Parse("""{"mixId":"game","mixerName":"Game","localVolumeIn":34,"streamVolumeIn":76,"isLocalInMuted":false,"isStreamInMuted":true,"isAvailable":true}""")!,true);
            Assert(legacy.Mixes[0].Level==.34&&legacy.Mixes[1].Level==.76&&!legacy.HasOverallLevel,"Legacy levels must normalize correctly.");
            Console.WriteLine("PASS: legacy channel normalization and independent mute states.");
            Exception? wheelError=null;
            var uiThread=new Thread(()=> {try {
                var wheel=new Wheel {Available=true,Level=.5};
                var control=new WheelControl {Wheel=wheel};
                control.VolumeRequested+=(w,v)=>w.Level=Math.Round(v,2);
                control.Adjust(.25);control.Adjust(.25);
                Assert(wheel.Level==.5,"Small wheel deltas should accumulate instead of rounding each event up.");
                control.Adjust(.25);
                Assert(wheel.Level==.51,"Accumulated fractional deltas should eventually change the level.");
                control.CanAdjustVolume=()=>false;control.Adjust(12);Assert(wheel.Level==.51,"Disabled controls must not change or accumulate volume.");
                control.CanAdjustVolume=()=>true;control.ResetScroll();control.Adjust(1);Assert(wheel.Level==.52,"Reset must start with fresh scroll state.");
                var changed=false;wheel.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(Wheel.VolumeLabel))changed=true;};wheel.Muted=true;
                Assert(changed,"External mute updates must refresh the visible volume label.");
            }catch(Exception ex){wheelError=ex;}});
            uiThread.SetApartmentState(ApartmentState.STA);uiThread.Start();uiThread.Join();if(wheelError!=null)throw wheelError;
            Console.WriteLine("PASS: high-resolution scroll accumulation and external mute label refresh.");
            var dir=Path.GetFullPath(args.Length>1?args[1]:"test-profile");Directory.CreateDirectory(dir);
            var store=new SettingsStore(dir);var settings=new Preferences();settings.Wheels[0].Name="Saved Music";settings.Wheels[0].ChannelId="music";settings.Wheels[0].MixId="personal";settings.Wheels.Reverse();store.Save(settings);var saved=store.Load();
            Assert(saved.Wheels[2].Name=="Saved Music"&&saved.Wheels[2].ChannelId=="music"&&saved.Wheels[2].MixId=="personal","Saved names/order/bindings must survive restart.");
            Assert(!File.ReadAllText(store.FilePath).Contains("VolumeLabel"),"Runtime state must not be persisted.");
            Console.WriteLine("PASS: atomic settings round trip preserves ordering and target identity.");
            if(args.Length==0) {Console.WriteLine("No simulator port supplied; network tests skipped.");return 0;}
            var port=int.Parse(args[0]);
            using var client=new WaveLinkClient();var snapshots=new ConcurrentQueue<MixerSnapshot>();client.Updated+=snapshots.Enqueue;client.Log=Console.WriteLine;client.Start(port);
            var initial=await Wait(snapshots,s=>s.Connected&&s.Channels.Count==2);
            var isLegacy=initial.Protocol.Contains("legacy");var mixId=isLegacy?"local":"personal";
            Console.WriteLine("PASS: handshake, protocol detection, channel and mix discovery ("+initial.Protocol+").");
            if(!isLegacy)
            {
                client.QueueLevel(new("music",""),.61);
                var whole=await Wait(snapshots,s=>s.Connected&&s.Channels.First(c=>c.Id=="music").Level==.61);
                Assert(whole.Channels.First(c=>c.Id=="music").Mixes[0].Level==.4,"Whole-channel gain must preserve the per-mix fader.");
                client.QueueMute(new("music",""),true);
                await Wait(snapshots,s=>s.Connected&&s.Channels.First(c=>c.Id=="music").Muted);
                client.QueueMute(new("music",""),false);
                await Wait(snapshots,s=>s.Connected&&!s.Channels.First(c=>c.Id=="music").Muted);
                Console.WriteLine("PASS: whole-channel volume and mute stay separate from per-mix values.");
            }
            client.QueueLevel(new("music",mixId),.42);
            var changed=await Wait(snapshots,s=>s.Connected&&s.Channels.First(c=>c.Id=="music").Mixes.First(m=>m.Id==mixId).Level==.42);
            Assert(changed.Channels.First(c=>c.Id=="music").Mixes.First(m=>m.Id=="stream").Level==.8,"Changing personal must not change stream.");
            Console.WriteLine("PASS: volume setter changes only the selected mix.");
            client.QueueMute(new("music",mixId),true);
            var muted=await Wait(snapshots,s=>s.Connected&&s.Channels.First(c=>c.Id=="music").Mixes.First(m=>m.Id==mixId).Muted);
            Assert(muted.Channels.First(c=>c.Id=="music").Mixes.First(m=>m.Id==mixId).Level==.42,"Muting must retain level.");
            Console.WriteLine("PASS: mute retains saved volume.");
            // The simulator sends an external change, removes the channel, and drops the connection after mute.
            await Wait(snapshots,s=>s.Connected&&s.Channels.Any(c=>c.Id=="music"&&c.Mixes.Any(m=>m.Id==mixId&&m.Level==.27&&!m.Muted)));
            Console.WriteLine("PASS: external volume/mute notifications synchronize.");
            await Wait(snapshots,s=>s.Connected&&!s.Channels.Any(c=>c.Id=="music"));
            Console.WriteLine("PASS: removed input channel is not presented as live.");
            await Wait(snapshots,s=>!s.Connected);
            await Wait(snapshots,s=>s.Connected&&s.Channels.Count==2);
            Console.WriteLine("PASS: disconnect is reported and reconnect rediscovers channels.");
            return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine("FAIL: "+ex);return 1;}
    }
}
