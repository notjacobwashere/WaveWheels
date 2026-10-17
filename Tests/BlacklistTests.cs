using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WaveWheels;

internal static class BlacklistTests
{
    private static void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
    public static void Run(string directory)
    {
        Assert(ApplicationBlacklist.Matches(new[]{"game.exe"},"GAME")&&!ApplicationBlacklist.Matches(new[]{"game.exe"},"gamehelper")&&!ApplicationBlacklist.Matches(new[]{"game.exe"},"game.exe - Player"),"Blacklist matching must use exact executable names, not titles or substrings.");
        Assert(ApplicationBlacklist.Normalize("game.win64.shipping.exe")=="game.win64.shipping.exe"&&ApplicationBlacklist.Normalize("*.exe")==null&&ApplicationBlacklist.Normalize(@"C:\Apps\game.exe")==null,"Blacklist entries must preserve dotted names and reject wildcard/path input.");
        var vm=new MainViewModel(new SettingsStore(directory),true);var wheel=vm.Wheels[0];wheel.ChannelId="music";
        vm.Update(new(true,"Fixture mixer","Wave Link 3",new[]{new Channel("music","Music",.6,false,true,Array.Empty<ChannelMix>())},Array.Empty<Mix>()));
        var window=new SettingsWindow(vm);var root=(FrameworkElement)window.Content;window.Content=null;root.DataContext=vm;root.Resources=window.Resources;
        ((Button)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));root.UpdateLayout();
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);root.UpdateLayout();
        var box=(TextBox)window.FindName("BlacklistAppBox");box.SetCurrentValue(TextBox.TextProperty,"GAME.exe");
        var add=FindButtons(root).Single(b=>b.Content?.ToString()=="Add app");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.AddBlacklistedApplication("game.exe");Assert(vm.BlacklistedApplications.SequenceEqual(new[]{"game.exe"}),"The UI must add a normalized executable without duplicate entries.");
        var overlay=new OverlayWindow(vm);var previews=0;Action count=()=>previews++;vm.PreviewRequested+=count;
        vm.ApplyForegroundProcess("game",0);Assert(vm.IsApplicationBlocked,"Focused blacklisted apps must suppress interaction even with automatic profiles off.");
        vm.SetLevel(wheel,.9);vm.ToggleMute(wheel);vm.Preview();overlay.ShowAtCursor();
        Assert(wheel.Level==.6&&!wheel.Muted&&previews==0&&!overlay.IsVisible,"Blocked focus must suppress volume, mute, configuration preview, and tray preview.");
        var bounds=System.Windows.Forms.Screen.AllScreens[0].Bounds;
        Assert(!overlay.OnGlobalWheel(bounds.Left,bounds.Top,120),"Blacklisted focus must not consume edge scrolling from the underlying app.");
        vm.PreviewRequested-=overlay.ShowAtCursor;overlay.Close();
        vm.ApplyForegroundProcess("gamehelper",500);Assert(!vm.IsApplicationBlocked,"Other executables must remain unaffected.");vm.SetLevel(wheel,.7);Assert(wheel.Level==.7,"Normal volume control must resume for other apps.");
        vm.ApplyForegroundProcess("game",1000);
        ((CheckBox)window.FindName("BlacklistEnabledToggle")).SetCurrentValue(ToggleButton.IsCheckedProperty,false);
        Assert(!vm.ApplicationBlacklistEnabled&&!vm.IsApplicationBlocked,"Disabling the blacklist must immediately restore interaction.");vm.SetLevel(wheel,.8);Assert(wheel.Level==.8,"Disabled blacklist entries must not prevent changes.");
        vm.ApplicationBlacklistEnabled=true;Assert(vm.IsApplicationBlocked,"Re-enabling must reapply the current focused executable.");
        vm.SelectedBlacklistedApplication="game.exe";FindButtons(root).Single(b=>b.Content?.ToString()=="Remove selected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!vm.IsApplicationBlocked&&vm.BlacklistedApplications.Count==0,"Removing a focused entry must unblock immediately.");
        vm.AddBlacklistedApplication("game.exe");vm.AutomaticProfilesEnabled=true;vm.ActiveProfile!.Applications="game.exe";vm.ApplyForegroundProcess("game",2000);vm.ApplyForegroundProcess("game",2500);Assert(vm.IsApplicationBlocked,"Automatic profile evaluation must not bypass the blacklist.");
        vm.ApplicationBlacklistEnabled=false;vm.Save();var saved=new SettingsStore(directory).Load();Assert(!saved.ApplicationBlacklistEnabled&&saved.BlacklistedApplications.SequenceEqual(new[]{"game.exe"}),"Blacklist entries and toggle must persist without changing profiles.");
        vm.Dispose();Console.WriteLine("PASS: exact executable matching, UI add/remove, duplicate rejection, volume/mute/preview/edge suppression, unaffected apps, disable/re-enable, profile coexistence, and persistence.");
    }
    private static System.Collections.Generic.IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);if(child is Button button)yield return button;
            foreach(var nested in FindButtons(child))yield return nested;
        }
    }
}
