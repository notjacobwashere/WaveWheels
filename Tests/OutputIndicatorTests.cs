using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using WaveWheels;
internal static class OutputIndicatorTests
{
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 internal static void Run(string directory)
 {
  var store=new SettingsStore(Path.Combine(directory,"output-indicators-"+Guid.NewGuid().ToString("N")));var model=new MainViewModel(store,true);
  var wheel=model.Wheels[0];wheel.ChannelId="music";
  MixerSnapshot State(bool mute)=>new(true,"Fixture","Wave Link 3",new[]{new Channel("music","Music",.5,false,true,new[]{new ChannelMix("a",.5,mute),new ChannelMix("b",.7,false)})},new[]{new Mix("a","Headphones"),new Mix("b","Stream")});
  model.Update(State(false));Assert(wheel.OutputMuteIndicators.Length==0,"Ordinary wheels must not show output badges.");
  wheel.RightClickAction="Mute output";wheel.RightClickOutput="a";
  Assert(wheel.OutputMuteIndicators.Length==1&&wheel.OutputMuteIndicators[0].Button=="R"&&wheel.OutputMuteIndicators[0].Muted==false,"Right-click output mute must expose its own live output state.");
  model.Update(State(true));Assert(wheel.OutputMuteIndicators[0].Muted==true&&!wheel.Muted,"External output mute must update the badge independently of whole-channel mute.");
  wheel.LeftClickAction="Mute output";wheel.LeftClickOutput="b";Assert(wheel.OutputMuteIndicators.Length==2&&wheel.OutputMuteIndicators[0].Button=="L"&&wheel.OutputMuteIndicators[0].Muted==false,"Separate left/right targets must show separate states.");
  var window=new SettingsWindow(model);model.ShowOutputMuteIndicators=false;
  Assert(!((WheelControl)window.FindName("SelectedPreview")).ShowOutputMuteIndicators,"The setting must hide indicators in the actual configuration preview.");model.Save();Assert(!store.Load().ShowOutputMuteIndicators,"The indicator preference must survive reload.");
  model.RestoreDefaults("Settings");Assert(model.ShowOutputMuteIndicators,"Restore defaults must enable output indicators.");
  using(var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.FilePath)))Assert(!json.RootElement.GetProperty("Wheels")[0].TryGetProperty("OutputMuteIndicators",out _),"Runtime output states must not be persisted.");
  var control=new WheelControl{Wheel=wheel,EffectsEnabled=false};control.Measure(new Size(300,120));control.Arrange(new Rect(0,0,300,120));
  foreach(var button in new[]{false,true}){control.UseMuteButton=button;control.InvalidateVisual();control.UpdateLayout();var bitmap=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);bitmap.Render(control);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,button?"indicators-with-button.png":"indicators.png"));encoder.Save(file);}
  wheel.RightClickOutput="missing";Assert(wheel.OutputMuteIndicators.Single(i=>i.Button=="R").Muted==null,"Unavailable outputs must show an unknown state rather than unmuted.");
  wheel.LeftClickAction="None";Assert(wheel.OutputMuteIndicators.Length==1,"Removing a click action must remove its badge.");
  model.Dispose();Console.WriteLine("PASS: output badge state, external changes, independent left/right targets, unavailable outputs, preview toggle, persistence, reset, and runtime-only status.");
 }
}
