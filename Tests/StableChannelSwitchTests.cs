using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WaveWheels;

internal static class StableChannelSwitchTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var json="""[{"tag_name":"v1.1.0","prerelease":false},{"tag_name":"v1.2.1-beta.1","prerelease":true},{"tag_name":"v1.2.1-beta.3","prerelease":true}]""";
        Assert(ReleaseUpdates.ParseCatalog(json,"1.2.1-beta.1",true).Latest?.Version=="1.2.1-beta.3","A newer beta must be discovered from the installed beta.");
        Assert(ReleaseUpdates.ParseCatalog(json,"1.2.1-beta.1",false).Latest==null,"Older stable versions must remain intentional channel switches, not automatic updates.");
        var model=new MainViewModel(new SettingsStore(Path.Combine(directory,"stable-channel-switch")),false);
        var stable=UpdateTests.Fixture(new byte[]{1,2,3}) with {Version="1.1.0",IsBeta=false};
        var catalog=new ReleaseCatalog(null,new[]{new ReleaseUpdate("1.2.1-beta.1","",null,true)},stable);
        model.ApplyCatalog(catalog);
        Assert(model.ShowStableSwitch&&model.CanInstallStable&&!model.CanInstallUpdate&&model.StableSwitchLabel.Contains("1.1.0")&&model.UpdateStatus.Contains("1.1.0")&&model.UpdateStatus=="Latest stable · 1.1.0"&&!model.ShowBetaVersionWarning,"Stable channel must offer an older stable version directly without claiming the beta is up to date.");
        var window=new SettingsWindow(model);Assert(window.Title=="Wave Wheels","The configuration window title must contain only the app name.");var button=(Button)window.FindName("StableSwitchButton");button.DataContext=model;
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        Assert(button.Visibility==Visibility.Visible&&button.IsEnabled,"The stable switch must be visible and actionable outside Version history.");
        var checks=0;var installs=0;model.Preferences.AutoInstallUpdates=true;
        model.RunStartupUpdateCoreAsync(true,()=>{checks++;model.ApplyCatalog(catalog);return Task.CompletedTask;},()=>{installs++;return Task.CompletedTask;}).GetAwaiter().GetResult();Assert(checks==1&&installs==0,"A sign-in update check must not silently downgrade to stable.");
        var launched=0;model.InstallStableCoreAsync((release,progress,token)=>{Assert(release==stable,"Switching must download the stable release.");return Task.FromResult("fixture-never-execute.exe");},_=>launched++).GetAwaiter().GetResult();
        Assert(launched==1&&model.UpdateChannel=="Stable","Explicit stable switching must use the existing verified install pipeline.");
        model.ApplyCatalog(new ReleaseCatalog(null,Array.Empty<ReleaseUpdate>(),stable with {Version=model.AppVersion}));Assert(!model.ShowStableSwitch,"Already being on the stable release must hide the switch action.");model.Dispose();
        Console.WriteLine("PASS: newer-beta discovery, direct stable-channel switch from a higher beta, accurate update status, visible install action, explicit install handoff, and no automatic downgrade.");
    }
}
