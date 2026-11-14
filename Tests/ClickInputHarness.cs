using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WaveWheels;
internal static class ClickInputHarness
{
 internal static void Run(string directory)
 {
  var thread=new Thread(()=>{
   var app=new ComponentApplication();app.InitializeComponent();
   var model=new MainViewModel(new SettingsStore(directory),true);
   var wheel=model.Wheels[0];wheel.ChannelId="music";wheel.MixId="";wheel.Name="Mouse test";wheel.RightClickAction="Mute output";wheel.RightClickOutput="stream";
   var observed=model.Wheels[1];observed.ChannelId="music";observed.MixId="stream";
   model.Update(new(true,"Test mixer","Wave Link 3",new[]{new Channel("music","Music",.5,false,true,new[]{new ChannelMix("personal",.5,false),new ChannelMix("stream",.5,false)})},new[]{new Mix("personal","Headphones"),new Mix("stream","Stream")}));
   var window=new Window{Title="Wave Wheels input test",Width=580,Height=380,Background=new SolidColorBrush(Color.FromRgb(16,24,32)),Foreground=Brushes.White,WindowStartupLocation=WindowStartupLocation.CenterScreen};
   var grid=new StackPanel{Margin=new Thickness(28)};window.Content=grid;
   var status=new TextBlock{Text="Right-click the wheel. Actions: 0. Stream muted: False",FontSize=16,Margin=new Thickness(0,0,0,30)};grid.Children.Add(status);
   var control=new WheelControl{Wheel=wheel,HorizontalAlignment=HorizontalAlignment.Left};grid.Children.Add(control);var actions=0;
   control.ClickRequested+=(w,right)=>{model.ExecuteClick(w,right);actions++;status.Text=$"Button: {(right?"Right":"Left")}. Actions: {actions}. Stream muted: {observed.Muted}";};
   window.Closed+=(_,_)=>{model.Dispose();app.Shutdown();};app.Run(window);
  });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
 }
}
