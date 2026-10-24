using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaveWheels;

internal static class AudioModeTests
{
    private static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    private sealed class FakeAudio:IWindowsMasterAudio
    {
        internal WindowsAudioState State=new(true,.47,false,"Windows default playback device");
        internal int Writes;
        public WindowsAudioState Read()=>State;
        public WindowsAudioState SetLevel(double level) {Writes++;return State=State with{Level=level};}
        public WindowsAudioState SetMuted(bool muted) {Writes++;return State=State with{Muted=muted};}
    }
    internal static void Run(string directory)
    {
        var fake=new FakeAudio();var store=new SettingsStore(directory);var vm=new MainViewModel(store,true,fake);
        vm.RefreshWindowsAudio();var fallback=vm.WindowsWheel;var original=vm.Wheels.Select(w=>w.Id).ToArray();
        Assert(!vm.Connected&&vm.DisplayWheels.Single()==fallback&&vm.EditorWheel==fallback&&fallback.Level==.47,"Disconnected startup must show exactly the live Windows wheel.");
        vm.AddWheel();vm.WheelCount=8;vm.RemoveWheel();Assert(vm.Wheels.Count==original.Length&&vm.DisplayWheelCount==1,"Offline wheel-count commands must preserve the hidden Wave Link layout.");
        fallback.Name="My speakers";fallback.Color="#DA5DAD";fallback.MaximumVolumePercent=75;vm.SetLevel(fallback,1);vm.ToggleMute(fallback);
        Assert(fake.State.Level==.75&&fake.State.Muted&&fallback.Muted,"Windows volume and mute must use the real backend contract and respect its wheel cap.");
        vm.AddBlacklistedApplication("blocked.exe");vm.ApplyForegroundProcess("blocked.exe",0);var writes=fake.Writes;vm.SetLevel(fallback,.2);vm.ToggleMute(fallback);Assert(fake.Writes==writes,"The blacklist must protect Windows volume and mute too.");vm.ApplicationBlacklistEnabled=false;
        var saved=vm.Wheels[0];saved.ChannelId="music";saved.Name="Wave music";
        var connected=new MixerSnapshot(true,"Fixture mixer","Wave Link 3",new[]{new Channel("music","Music",.62,false,true,Array.Empty<ChannelMix>())},Array.Empty<Mix>());
        vm.Update(connected);
        Assert(vm.DisplayWheels.SequenceEqual(vm.Wheels)&&!vm.DisplayWheels.Contains(fallback)&&saved.Level==.62&&fake.Writes==writes,"Reconnection must restore only Wave Link wheels without copying Windows levels.");
        vm.SetLevel(fallback,.3);vm.ToggleMute(fallback);Assert(fake.Writes==writes,"Stale Windows controls must not write after reconnection.");
        vm.Update(new(false,"Wave Link unavailable","",Array.Empty<Channel>(),Array.Empty<Mix>()));vm.RefreshWindowsAudio();
        Assert(vm.DisplayWheels.Single()==fallback&&fallback.Name=="My speakers"&&fallback.Color=="#DA5DAD"&&fallback.Muted,"Disconnect must restore the same customized Windows wheel.");
        vm.SetLevel(saved,.1);Assert(saved.Level==.62&&fake.Writes==writes,"Stale Wave Link controls must not change Windows volume.");
        fake.State=new(false,0,false,"No output device");vm.RefreshWindowsAudio();writes=fake.Writes;vm.SetLevel(fallback,.1);Assert(!fallback.Available&&fake.Writes==writes,"Missing playback devices must disable volume writes.");
        fake.State=new(true,.28,false,"New default playback device");vm.RefreshWindowsAudio();Assert(fallback.Available&&fallback.Level==.28&&!fallback.Muted,"A changed/default playback device must recover automatically without carrying over old volume/mute.");
        vm.Save();var loaded=store.Load();Assert(loaded.WindowsWheel.Name=="My speakers"&&loaded.WindowsWheel.Color=="#DA5DAD"&&loaded.Wheels.Select(w=>w.Id).SequenceEqual(original)&&loaded.Wheels[0].Name=="Wave music","Fallback and Wave Link layouts must persist separately.");
        Assert(!JsonSerializer.Serialize(loaded.WindowsWheel).Contains("\"Level\""),"Live Windows volume/mute values must not be restored from disk.");
        var window=new SettingsWindow(vm);var root=(FrameworkElement)window.Content;window.Content=null;root.DataContext=vm;root.Resources=window.Resources;
        void Flush() {root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));root.UpdateLayout();var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);root.UpdateLayout();}
        System.Windows.Documents.TextElement.SetForeground(root,new SolidColorBrush(Color.FromRgb(243,240,250)));
        System.Windows.Documents.TextElement.SetFontFamily(root,new FontFamily("Segoe UI"));System.Windows.Documents.TextElement.SetFontSize(root,14);
        Flush();var list=(ListBox)window.FindName("WheelList");
        Assert(list.Items.Count==1&&ReferenceEquals(list.Items[0],fallback)&&!((ComboBox)window.FindName("CountBox")).IsEnabled&&!((ComboBox)window.FindName("ChannelBox")).IsEnabled,"Offline UI must show one fallback wheel with Wave Link-only controls disabled.");
        Assert(((WheelControl)window.FindName("SelectedPreview")).Wheel==fallback,"The editor preview must show the Windows wheel while offline.");
        var preview=(WheelControl)window.FindName("SelectedPreview");preview.BeginAnimation(WheelControl.DisplayLevelProperty,null);preview.DisplayLevel=fallback.Level;root.UpdateLayout();
        Save(root,Path.Combine(Path.GetDirectoryName(directory)!,"windows-audio-mode.png"));
        ((Button)window.FindName("ConnectionNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();Assert(window.FindName("StartWaveLinkButton") is Button,"Connection must provide an installed-app launch/locate control.");Save(root,Path.Combine(Path.GetDirectoryName(directory)!,"windows-audio-connection.png"));
        vm.Update(connected);Flush();Assert(list.Items.Count==original.Length&&((ComboBox)window.FindName("CountBox")).IsEnabled,"Reconnecting must restore the actual editor wheel list and controls.");
        vm.Dispose();Console.WriteLine("PASS: Windows fallback backend, caps/mute, stale-write guards, disconnect/reconnect, separate settings, device loss/recovery and mode UI.");
    }
    private static void Save(FrameworkElement root,string path)
    {
        var bitmap=new RenderTargetBitmap(1160,860,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
