using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveWheels;

internal static class MaximumEffectTests
{
    private static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
    public static void Run(string directory)
    {
        Exception? error=null;var thread=new Thread(()=>
        {
            try
            {
                var state=new MaximumVolumeTransition();state.Reset(true,.99);
                Assert(state.Observe(true,1,true)&&!state.Observe(true,1,true),"Only an actual transition to 100% may trigger.");
                state.Observe(true,.9,true);Assert(state.Observe(true,1,true),"Dropping below maximum must rearm the effect.");
                state.Reset(false,.9);Assert(!state.Observe(true,1,true),"Reconnect at maximum must not burst.");
                state.Reset(true,.6);Assert(!state.Observe(true,.65,true),"A lower per-wheel cap must not count as 100%.");
                state.Reset(true,.99);Assert(!state.Observe(true,1,false),"Reduced motion must suppress the effect.");
                Assert(MaximumVolumeEffect.Glow(0)==0&&MaximumVolumeEffect.Glow(.1)>.9&&MaximumVolumeEffect.Glow(.6)>.8&&MaximumVolumeEffect.Glow(.85)>.15&&MaximumVolumeEffect.Glow(1)==0,"The brighter glow must hold and linger before fading cleanly.");
                foreach(var progress in new[]{.08,.25,.5,.7,.85,.95})
                    Assert(Enumerable.Range(0,MaximumVolumeEffect.SparkCount).Any(i=>MaximumVolumeEffect.Spark(i,progress).Opacity>0),"Staggered glowing sparks must remain visible throughout the extended effect.");
                foreach(var burst in new[]{0,1,7})
                {
                    var angles=Enumerable.Range(0,MaximumVolumeEffect.SparkCount).Select(i=>MaximumVolumeEffect.SpawnDegrees(i,burst)).ToArray();
                    Assert(angles.Distinct().Count()==MaximumVolumeEffect.SparkCount&&angles.Min()<140&&angles.Max()>310&&angles.All(a=>a>=90&&a<=360),"Randomized origins must cover the drawn 270-degree bar.");
                    foreach(var i in Enumerable.Range(0,MaximumVolumeEffect.SparkCount))
                    {
                        var angle=angles[i]*Math.PI/180;var radial=new Vector(Math.Cos(angle),Math.Sin(angle));var tangent=new Vector(-radial.Y,radial.X);var origin=new Point(60+radial.X*43,60+radial.Y*43);
                        var birth=MaximumVolumeEffect.Spark(i,0,burst).BirthTime;var launch=MaximumVolumeEffect.Spark(i,birth,burst);
                        Assert((new Point(launch.TailX,launch.TailY)-origin).Length<1e-7,"Every streak must begin on its own randomized point on the bar.");
                        var fast=MaximumVolumeEffect.Spark(i,birth+.05,burst);
                        Assert((new Point(fast.TailX,fast.TailY)-origin).Length>0&&(birth>=.4||(new Point(fast.TailX,fast.TailY)-origin).Length>=20),"Sparks must move at least 21 DIPs in 90 ms rather than hovering over the bar.");
                        var travel=new Point(launch.X,launch.Y)-new Point(launch.TailX,launch.TailY);var forward=Vector.Multiply(travel,tangent);
                        Assert(forward>2.9&&Math.Abs(Vector.CrossProduct(travel,tangent))<forward*.08,"Each spark must follow the clockwise tangent at its own origin.");
                        Assert(launch.BirthTime+launch.Lifetime<=1.000001&&launch.Lifetime*MaximumVolumeEffect.DurationMilliseconds>=160,"Fast particles must still finish within the effect clock.");
                    }
                }
                Assert(MaximumVolumeEffect.SpawnDegrees(0,0)!=MaximumVolumeEffect.SpawnDegrees(0,1),"Repeated bursts must vary their spawn pattern.");
                foreach(var life in new[]{.09,.1,.13})
                {
                    var earlyTravel=MaximumVolumeEffect.TravelTime(life*.2,life)-MaximumVolumeEffect.TravelTime(life*.1,life);
                    var lateTravel=MaximumVolumeEffect.TravelTime(life*.9,life)-MaximumVolumeEffect.TravelTime(life*.8,life);
                    Assert(lateTravel>0&&lateTravel<earlyTravel*.2,"Every spark must slow near the end of its own lifespan, regardless of launch time.");
                }
                var silhouette=WheelControl.WheelSilhouette(new Rect(60,4,144,26));
                Assert(!silhouette.StrokeContains(new Pen(Brushes.White,1),new Point(100,20))&&silhouette.StrokeContains(new Pen(Brushes.White,1),new Point(150,4)),"The hover outline must wrap the label exterior without crossing the wheel-label join.");
                Assert(Enumerable.Range(0,MaximumVolumeEffect.SparkCount).All(i=>MaximumVolumeEffect.Spark(i,1).Opacity==0&&MaximumVolumeEffect.Spark(i,1.1).Opacity==0),"All streaks must stop cleanly without looping.");
                var app=new ComponentApplication();app.InitializeComponent();
                var snap=new ShiftVolumeSnap();
                Assert(snap.Adjust(10,.33,0)==.4&&snap.Adjust(10,.4,179)==null&&snap.Adjust(10,.4,180)==.6,"Shift scroll must choose one adjacent 20% step and enforce the cooldown even for large deltas.");
                snap.Reset();Assert(snap.Adjust(-1,.4,0)==.2&&snap.Adjust(-1,.2,220)==0&&snap.Adjust(-1,0,440)==null,"Downward snapping and zero bounds must be exact.");
                snap.Reset();Assert(snap.Adjust(1,.8,0)==1&&snap.Adjust(1,1,220)==null,"The upper step must reach 100% once.");
                snap.StepPercent=30;Assert(snap.Adjust(1,.33,0)==.6&&ShiftVolumeSnap.MarkerLevels(30).SequenceEqual(new[]{0,30,60,90,100}),"Markers and snaps must share the configured grid, including both endpoints.");snap.Reset();Assert(snap.Adjust(-1,1,0)==.9,"Downward snapping from the endpoint must return to the last configured marker.");
                foreach(var increment in new[]{5,10,20,35,50})
                {
                    var gridMarkers=ShiftVolumeSnap.MarkerLevels(increment).ToArray();var grid=new ShiftVolumeSnap{StepPercent=increment};
                    Assert(gridMarkers.First()==0&&gridMarkers.Last()==100&&gridMarkers.Distinct().Count()==gridMarkers.Length,"Marker grids must contain unique endpoints for each step size.");
                    for(var i=1;i<gridMarkers.Length;i++)Assert(Math.Abs(grid.Adjust(1,gridMarkers[i-1]/100d,i*200)!.Value-gridMarkers[i]/100d)<1e-7,"Every upward snap target must match the next labeled marker.");
                }
                var wheel=new Wheel{Name="Music",Color="#36BBD3",Available=true,Level=.99};var control=new WheelControl{Wheel=wheel};var requests=0;
                control.VolumeRequested+=(w,value)=> {requests++;w.Level=Math.Clamp(value,0,1);};
                control.AdjustInput(1,false,0);var starts=control.MaximumEffectStarts;
                Assert(starts==(SystemParameters.ClientAreaAnimation?1:0),"The effect must start immediately when actual volume reaches maximum, respecting reduced motion.");
                if(SystemParameters.ClientAreaAnimation)Assert(control.DisplayLevel<1,"The effect must begin before the bar animation reaches its target.");
                control.SeekLevelAnimation(TimeSpan.FromMilliseconds(400));Assert(control.MaximumEffectStarts==starts,"Bar animation completion must not retrigger the effect.");
                for(var i=0;i<20;i++)control.AdjustInput(1,false,10+i);
                Assert(requests==1&&control.MaximumEffectStarts==starts,"Capped input must not queue redundant writes or restart the animation.");
                wheel.Level=.9;control.SeekLevelAnimation(TimeSpan.FromMilliseconds(400));wheel.Level=1;control.SeekLevelAnimation(TimeSpan.FromMilliseconds(400));Assert(control.MaximumEffectStarts==starts+(SystemParameters.ClientAreaAnimation?1:0),"A new genuine crossing must trigger again after its count-up.");
                var prior=control.MaximumEffectStarts;wheel.Level=.8;control.SeekLevelAnimation(TimeSpan.FromMilliseconds(400));wheel.Level=1;wheel.Level=.7;control.SeekLevelAnimation(TimeSpan.FromMilliseconds(400));Assert(control.MaximumEffectStarts==prior+(SystemParameters.ClientAreaAnimation?1:0),"Every genuine maximum crossing must trigger immediately, even before the next level change.");
                control.EffectsEnabled=false;wheel.Level=.9;wheel.Level=1;Assert(control.MaximumEffectProgress==1,"Disabling animation must clear the effect immediately.");
                var count=control.MaximumEffectStarts;control.Wheel=new Wheel{Available=true,Level=1};Assert(control.MaximumEffectStarts==count,"Binding a wheel already at maximum must not burst.");
                control.Wheel=wheel;control.EffectsEnabled=true;control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=1;
                control.Measure(new Size(300,120));control.Arrange(new Rect(0,0,300,120));control.StartMaximumEffect();
                Directory.CreateDirectory(directory);
                foreach(var ms in new[]{0,80,200,500,800,1100,1400,1650,1800})
                {
                    control.SeekMaximumEffect(TimeSpan.FromMilliseconds(ms));
                    control.InvalidateVisual();control.UpdateLayout();
                    Assert(Math.Abs(control.MaximumEffectProgress-ms/(double)MaximumVolumeEffect.DurationMilliseconds)<.001,"The real WPF animation clock must span the configured 1.8 seconds.");
                    for(var i=0;i<MaximumVolumeEffect.SparkCount;i++) {var s=MaximumVolumeEffect.Spark(i,ms/(double)MaximumVolumeEffect.DurationMilliseconds);Assert(s.X>=-48&&s.X<=348&&s.Y>=-48&&s.Y<=168,"Sparks must stay inside the expanded transparent effect gutter.");}
                    var padded=new DrawingVisual();using(var drawing=padded.RenderOpen()) {drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(24,22,30)),null,new Rect(0,0,396,216));drawing.PushTransform(new TranslateTransform(48,48));drawing.DrawDrawing(VisualTreeHelper.GetDrawing(control));drawing.Pop();}
                    var frame=new RenderTargetBitmap(396,216,96,96,PixelFormats.Pbgra32);frame.Render(padded);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using var file=File.Create(Path.Combine(directory,$"maximum-{ms:0000}.png"));encoder.Save(file);
                }
                Assert(control.MaximumEffectProgress==1,"The completed clock must leave no residual glow or particles.");
                control.Wheel=new Wheel{Name="A",Color="#DA5DAD",Available=true,Level=.42};control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=.42;
                double shortWidth=0;
                foreach(var scenario in new[]{"short","reference","long","muted","hover"})
                {
                    control.Wheel.Name=scenario=="short"?"A":scenario=="long"?"THIS IS A VERY LONG WHEEL LABEL 32":"EVERYTHING ELSE";
                    control.Wheel.Muted=scenario=="muted";control.SetMuteHovered(scenario=="hover");control.InvalidateVisual();control.UpdateLayout();
                    var labelFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);labelFrame.Render(control);
                    var bounds=control.LabelBounds;
                    var topJoin=new byte[4];labelFrame.CopyPixels(new Int32Rect(67,4,1,1),topJoin,4,0);
                    Assert(topJoin[3]>240,"The label top must stay flush with the wheel top without a rounded-corner notch.");
                    Assert(bounds.X==60&&bounds.Y==4&&bounds.Right<=292&&bounds.Height==26,"Label placement must join the upper-right edge and fit the control.");
                    if(scenario=="short")shortWidth=bounds.Width;else Assert(bounds.Width>shortWidth,"Label backgrounds must grow with their measured text.");
                    var background=new byte[4];labelFrame.CopyPixels(new Int32Rect(32,60,1,1),background,4,0);
                    var expected=WheelControl.SurfaceColor(control.Wheel.Muted);
                    Assert(background[2]==expected.R&&background[1]==expected.G&&background[0]==expected.B,"Muted wheels must render their reddish base surface.");
                    var labelEncoder=new PngBitmapEncoder();labelEncoder.Frames.Add(BitmapFrame.Create(labelFrame));using var labelFile=File.Create(Path.Combine(directory,$"label-{scenario}.png"));labelEncoder.Save(labelFile);
                }
                Assert(WheelControl.MuteCenter.Y<88&&WheelControl.HitsMute(WheelControl.MuteCenter)&&!WheelControl.HitsMute(new Point(60,103)),"Raised mute artwork and its click target must agree.");
                control.Wheel.Muted=false;control.Wheel.Level=1;control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=1;control.StartMaximumEffect();control.SeekMaximumEffect(TimeSpan.FromMilliseconds(300));control.EnterHover();
                control.InvalidateVisual();control.UpdateLayout();var overlapFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);overlapFrame.Render(control);
                var aboveLabel=new byte[4];overlapFrame.CopyPixels(new Int32Rect(75,20,1,1),aboveLabel,4,0);
                Assert(aboveLabel[0]>120&&aboveLabel[1]>120&&aboveLabel[2]>120,"The label background must never cover the bar at the join.");
                var overlapEncoder=new PngBitmapEncoder();overlapEncoder.Frames.Add(BitmapFrame.Create(overlapFrame));using(var overlapFile=File.Create(Path.Combine(directory,"label-max-hover.png")))overlapEncoder.Save(overlapFile);
                control.CompleteHoverExit();
                control.Wheel.Muted=false;var muteStarts=control.MuteFeedbackStarts;control.Wheel.Muted=true;
                Assert(control.MuteFeedbackStarts==muteStarts+(SystemParameters.ClientAreaAnimation?1:0),"Muting must trigger one feedback animation, respecting reduced motion.");
                if(!SystemParameters.ClientAreaAnimation)control.StartMuteFeedback();
                var originalTransform=control.RenderTransform.Value;
                Assert(Math.Abs(WheelControl.MuteShake(40d/1200))>5&&WheelControl.MuteShake(520d/1200)==0,"The stronger shake must settle after approximately half a second.");
                Assert(WheelControl.SlashSweep(350d/1200)==1&&WheelControl.SlashOpacity(350d/1200)==1&&WheelControl.SlashOpacity(850d/1200)==1&&WheelControl.SlashOpacity(1)==0,"The slash must finish its sweep, hold for 500 ms, then fade cleanly.");
                Assert(WheelControl.SlashSweep(100d/1200)>(WheelControl.SlashSweep(350d/1200)-WheelControl.SlashSweep(250d/1200))*5,"The slash sweep must ease down near its endpoint.");
                foreach(var time in new[]{0,40,150,350,520,850,1050,1200})
                {
                    control.SeekMuteFeedback(TimeSpan.FromMilliseconds(time));control.InvalidateVisual();control.UpdateLayout();
                    Assert(Math.Abs(control.MuteFeedbackProgress-time/(double)WheelControl.MuteFeedbackMilliseconds)<.001&&Math.Abs(WheelControl.MuteShake(control.MuteFeedbackProgress))<=7,"Stronger mute feedback must remain bounded and settle cleanly.");
                    Assert(control.RenderTransform.Value==originalTransform,"Mute shaking must affect artwork without moving the pointer hit area.");
                    var mutedFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);mutedFrame.Render(control);var mutedEncoder=new PngBitmapEncoder();mutedEncoder.Frames.Add(BitmapFrame.Create(mutedFrame));using var mutedFile=File.Create(Path.Combine(directory,$"mute-feedback-{time:000}.png"));mutedEncoder.Save(mutedFile);
                }
                control.Wheel.Muted=false;Assert(control.MuteFeedbackProgress==1,"Unmuting must clear any old slash or shake.");
                control.EnterHover();var hoverTransitions=control.HoverTransitions;
                for(var rapid=0;rapid<30;rapid++) {control.LeaveHover();control.EnterHover();control.Wheel.Level=rapid%2==0?.41:.43;}
                Assert(control.HoverActive&&control.HoverTransitions==hoverTransitions,"Transient mouse-leave/re-enter and rapid volume updates must not restart hover scaling.");
                control.CompleteHoverExit();Assert(!control.HoverActive&&control.HoverTransitions==hoverTransitions+1,"A sustained pointer exit must still shrink the wheel once.");
                control.SetMuteHovered(false);control.Wheel=wheel;
                wheel.Level=1;control.StartMaximumEffect();control.SeekMaximumEffect(TimeSpan.FromMilliseconds(200));wheel.Level=.99;
                control.SeekMaximumEffect(TimeSpan.FromMilliseconds(240));Assert(control.MaximumEffectOpacity==1,"Dropping below maximum must leave existing sparks at their natural opacity.");
                Assert(Math.Abs(control.MaximumEmissionCutoff-500d/1800)<.001,"New spark emission must continue for 300 ms after volume drops.");
                wheel.Level=.98;Assert(Math.Abs(control.MaximumEmissionCutoff-500d/1800)<.001,"Further volume updates must not move the emission cutoff or alter existing particles.");
                var beforeFreshBurst=control.MaximumEffectStarts;wheel.Level=1;
                Assert(control.MaximumEffectStarts==beforeFreshBurst+(SystemParameters.ClientAreaAnimation?1:0),"Fresh maximum crossings must respect Windows reduced-motion settings.");
                if(!SystemParameters.ClientAreaAnimation)control.StartMaximumEffect();
                Assert(control.MaximumEffectOpacity==1&&control.MaximumEmissionCutoff==1,"Starting a new burst must restore full opacity and emission.");
                control.StartMaximumEffect();control.Wheel=new Wheel{Available=true,Level=.5};Assert(control.MaximumEffectProgress==1,"Profile/rebinding changes must clear old effects.");
                control.Wheel=wheel;wheel.Level=.33;control.AdjustInput(120,true,2000);Assert(wheel.Level==.4&&control.PrecisionMode,"Shift input must still snap the real control.");
                control.AdjustInput(120,true,2100);Assert(wheel.Level==.4,"Rapid repeated Shift input must not skip through steps.");
                control.AdjustInput(120,true,2220);Assert(wheel.Level==.6,"A new Shift step must work after cooldown.");
                control.ScrollAccelerationEnabled=false;control.AdjustInput(1,false,2230);Assert(wheel.Level==.61&&!control.PrecisionMode,"Releasing Shift must restore ordinary scroll increments without residual acceleration.");
                control.CanAdjustVolume=()=>false;control.AdjustInput(1,true,2500);Assert(wheel.Level==.61,"Shift must not bypass opening-scroll protection.");control.CanAdjustVolume=null;
                control.EffectsEnabled=false;control.SetPrecisionMode(true);control.BeginAnimation(WheelControl.DisplayLevelProperty,null);control.DisplayLevel=wheel.Level;control.InvalidateVisual();control.UpdateLayout();
                var shiftFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);shiftFrame.Render(control);var shiftedPixels=new byte[300*120*4];shiftFrame.CopyPixels(shiftedPixels,300*4,0);
                control.SetPrecisionMode(false);control.InvalidateVisual();control.UpdateLayout();var plainFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);plainFrame.Render(control);var plainPixels=new byte[shiftedPixels.Length];plainFrame.CopyPixels(plainPixels,300*4,0);Assert(shiftedPixels.SequenceEqual(plainPixels),"Shift mode must not draw any dots or labels.");
                var shiftEncoder=new PngBitmapEncoder();shiftEncoder.Frames.Add(BitmapFrame.Create(shiftFrame));using(var shiftFile=File.Create(Path.Combine(directory,"shift-no-markers.png")))shiftEncoder.Save(shiftFile);
                control.ShowShiftMarkers=true;control.SetPrecisionMode(true);control.InvalidateVisual();control.UpdateLayout();
                var dotsFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);dotsFrame.Render(control);var dotPixels=new byte[plainPixels.Length];dotsFrame.CopyPixels(dotPixels,300*4,0);
                Assert(!dotPixels.SequenceEqual(plainPixels),"Enabling Shift dots must render the snap positions.");
                var dotPositions=ShiftVolumeSnap.MarkerLevels(control.ShiftStepPercent).Select(WheelControl.PrecisionMarkerPoint).ToArray();
                for(var pixel=0;pixel<300*120;pixel++)if(Enumerable.Range(0,4).Any(c=>dotPixels[pixel*4+c]!=plainPixels[pixel*4+c]))
                    Assert(dotPositions.Any(p=>(p-new Point(pixel%300+.5,pixel/300+.5)).Length<5.5),"Enabled markers must draw dots only, with no numeric labels.");
                control.SetPrecisionMode(false);control.InvalidateVisual();control.UpdateLayout();var releasedFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);releasedFrame.Render(control);var releasedPixels=new byte[plainPixels.Length];releasedFrame.CopyPixels(releasedPixels,300*4,0);Assert(releasedPixels.SequenceEqual(plainPixels),"Enabled dots must disappear when Shift mode ends.");
                foreach(var step in new[]{1,10})foreach(var accelerated in new[]{false,true})
                {
                    var target=new Wheel{Available=true,Level=.95};var input=new WheelControl{Wheel=target,ScrollStepPercent=step,ScrollAccelerationEnabled=accelerated};
                    input.VolumeRequested+=(w,value)=>w.Level=Math.Round(Math.Clamp(value,0,w.MaximumVolumePercent/100d),2);
                    for(var i=0;i<40;i++)input.AdjustInput(1,false,3000+i*10);
                    Assert(target.Level==1,"The boundary fixture must reach 100%.");
                    input.AdjustInput(-1,false,3400);Assert(Math.Abs(target.Level-(1-step*.01))<1e-7,"Reversing from 100% must use exactly the configured step.");
                    target.Level=.01;input.ResetScroll();input.AdjustInput(-1,false,4000);Assert(target.Level==0,"The lower boundary must clamp.");
                    input.AdjustInput(1,false,4010);Assert(Math.Abs(target.Level-step*.01)<1e-7,"Reversing from zero must discard prior accumulated input.");
                    target.MaximumVolumePercent=65;target.Level=.64;input.ResetScroll();input.AdjustInput(1,false,4500);input.AdjustInput(-1,false,4510);Assert(Math.Abs(target.Level-(.65-step*.01))<1e-7,"Per-wheel limits must also consume clamped input.");
                }
                var fractional=new ScrollAccumulator();Assert(fractional.Adjust(-.8,10,false,0)==0&&fractional.Adjust(-.2,10,false,10)==-10,"Configured steps must accumulate fractional notches instead of emitting an unexpected 8% change.");
                var numberWheel=new Wheel{Available=true,Level=.5};var number=new WheelControl{Wheel=numberWheel};number.Measure(new Size(300,120));number.Arrange(new Rect(0,0,300,120));
                Assert(number.NumberText=="50"&&number.NumberTransitionStarts==0,"Initial binding must display the live value without rolling.");
                numberWheel.Level=.6;Assert(number.NumberText=="60"&&number.OutgoingNumberText=="50"&&number.NumberDirection==1,"Increasing volume must bring the new live value in from above.");
                var rolls=number.NumberTransitionStarts;numberWheel.Level=.6;Assert(number.NumberTransitionStarts==rolls,"Unchanged volume must not animate.");
                number.SeekNumberTransition(TimeSpan.FromMilliseconds(75));
                if(SystemParameters.ClientAreaAnimation)Assert(number.NumberProgress>0&&number.NumberProgress<1,"The number must roll/fade between its endpoints.");
                number.InvalidateVisual();number.UpdateLayout();var numberFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);numberFrame.Render(number);var numberEncoder=new PngBitmapEncoder();numberEncoder.Frames.Add(BitmapFrame.Create(numberFrame));using(var numberFile=File.Create(Path.Combine(directory,"rolling-number.png")))numberEncoder.Save(numberFile);
                number.SeekNumberTransition(TimeSpan.FromMilliseconds(150));Assert(number.NumberProgress==1,"Numbers must settle within 150 ms.");
                numberWheel.Level=.4;Assert(number.NumberDirection==-1&&number.NumberText=="40","Decreases must roll in the inverse direction.");
                numberWheel.Level=.41;numberWheel.Level=.42;Assert(number.NumberText=="42"&&numberWheel.Level==.42,"Rapid input must retarget immediately without queuing or blocking volume updates.");
                number.EffectsEnabled=false;numberWheel.Level=.43;Assert(number.NumberProgress==1&&number.NumberText=="43","Disabling animation must immediately show the latest number.");
                number.Wheel=new Wheel{Available=true,Level=1};Assert(number.NumberText=="MAX"&&number.NumberProgress==1,"Rebinding must reset stale numbers and show MAX without rolling.");
                foreach(var progress in new[]{0d,.25,.5,.9,1})
                {
                    var glyphs=NumberRollLayout.Glyphs("60","61",progress);var tens=glyphs.Single(g=>g.Value=='6');
                    Assert(!tens.Animated&&tens.X==53&&tens.OffsetY==0&&tens.Opacity==1,"Unchanged tens must remain stationary and fully opaque for the entire transition.");
                    Assert(glyphs.Where(g=>g.Value!='6').All(g=>g.Animated&&g.X==67),"Only the changed units column may roll.");
                    var units=NumberRollLayout.Glyphs("9","19",progress).Single(g=>g.Value=='9');Assert(!units.Animated&&units.X==67&&units.OffsetY==0&&units.Opacity==1,"Adding a tens digit must leave unchanged units in place.");
                }
                var inverse=NumberRollLayout.Glyphs("61","60",.5,-1);Assert(inverse.Single(g=>g.Value=='1').OffsetY<0&&inverse.Single(g=>g.Value=='0').OffsetY>0,"Scrolling down must roll changed digits upward, with incoming digits arriving from below.");
                var carry=NumberRollLayout.Glyphs("19","20",.5,1);Assert(carry.Single(g=>g.Value=='2').OffsetY<0&&carry.Single(g=>g.Value=='0').OffsetY<0,"Scrolling up across a carry must roll both changed digits downward together, entering from above.");
                var reverseCarry=NumberRollLayout.Glyphs("20","19",.5,-1);Assert(reverseCarry.Where(g=>g.Value is '1' or '9').All(g=>g.OffsetY>0),"Scrolling down across a borrow must reverse both changed digits together.");
                Assert(!NumberRollLayout.CanSplit("99","MAX")&&NumberRollLayout.Glyphs("59","60",.5).All(g=>g.Animated),"Whole display type changes and carries must animate every changed portion.");
                number.EffectsEnabled=true;number.Wheel=new Wheel{Available=true,Level=.6};number.Wheel.Level=.61;number.SeekNumberTransition(TimeSpan.FromMilliseconds(50));number.InvalidateVisual();number.UpdateLayout();
                var digitFrame=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);digitFrame.Render(number);var digitEncoder=new PngBitmapEncoder();digitEncoder.Frames.Add(BitmapFrame.Create(digitFrame));using(var digitFile=File.Create(Path.Combine(directory,"rolling-digit.png")))digitEncoder.Save(digitFile);
                Console.WriteLine("PASS: max pulse/clocks, Shift markers/cooldown, boundary reversals with default and 10% steps/acceleration/caps, and fractional-notch quantization.");
                Console.WriteLine("PASS: randomized arc origins, tangent spark travel, extended glow, bounded paths, fixed-length tails, and clean stop.");
                Console.WriteLine("PASS: 150 ms directional number roll/fade, unchanged-value suppression, immediate retargeting, reduced motion, and rebind reset.");
                Console.WriteLine("PASS: stationary unchanged digits across all frames, per-column transitions/carries, and stable units on digit-count changes.");
                ChannelEditorSmoke.Run(Path.Combine(directory,"channel-editor-profile-"+Guid.NewGuid().ToString("N")));
                BlacklistTests.Run(Path.Combine(directory,"blacklist-profile-"+Guid.NewGuid().ToString("N")));
            }
            catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
