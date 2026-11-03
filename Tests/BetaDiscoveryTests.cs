using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WaveWheels;

internal static class BetaDiscoveryTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var store=new SettingsStore(Path.Combine(directory,"beta-discovery"));store.Save(new Preferences{UpdateChannel="Beta"});
        var model=new MainViewModel(store,true);var window=new SettingsWindow(model);
        model.Update(new MixerSnapshot(true,"Fixture","Wave Link 3",Array.Empty<Channel>(),Array.Empty<Mix>()));
        model.SelectedWheel=model.Wheels[^1];window.SelectFirstWheelForPreview();Assert(model.SelectedWheel==model.Wheels[0]&&model.EditorWheel==model.Wheels[0],"Opening configuration must select the first wheel.");
        model.UseWindowsAudio=true;window.SelectFirstWheelForPreview();Assert(model.EditorWheel==model.WindowsWheel,"Opening Windows mode must preview the Windows wheel.");
        var catalog=ReleaseUpdates.ParseCatalog("""[{"tag_name":"v2.0.0","prerelease":false},{"tag_name":"v1.0.0-beta.3","prerelease":true},{"tag_name":"v1.0.0-beta.2","prerelease":true},{"tag_name":"v3.0.0-beta.1","draft":true,"prerelease":true}]""","2.0.0",true);
        Assert(catalog.Latest==null,"Automatic updates must not offer an older beta as a newer release.");
        model.ApplyCatalog(catalog);
        Assert(model.LatestBetaBuild?.Version=="1.0.0-beta.3"&&model.UpdateStatus.Contains("1.0.0-beta.3"),"The normal update panel must show the latest beta even when stable is newer.");
        Assert(window.FindName("LatestBetaCard")==null,"Latest beta must not have a separate section.");
        Assert(MainViewModel.BetaWarningFor(model.LatestBetaBuild,"2.0.0").Contains("installed stable version (2.0.0)"),"An older beta must warn about the installed stable build.");
        Assert(MainViewModel.BetaWarningFor(model.LatestBetaBuild,"0.9.0")==""&&MainViewModel.BetaWarningFor(model.LatestBetaBuild,"2.0.0-beta.1")=="","Newer betas and installed beta builds must not show the stable-build warning.");
        var warning=(TextBlock)window.FindName("BetaVersionWarning");warning.DataContext=model;
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        Assert(warning.Visibility==Visibility.Collapsed,"An installed beta must not show a stable-build warning.");
        model.ApplyCatalog(new ReleaseCatalog(null,Array.Empty<ReleaseUpdate>(),catalog.Stable));Assert(!model.ShowBetaVersionWarning&&model.UpdateStatus=="No beta releases found.","Missing beta releases must clear stale details.");model.Dispose();
        var live=new MainViewModel(store,false);var oldBeta=UpdateTests.Fixture(new byte[]{1,2,3}) with {Version="1.0.0-beta.3",IsBeta=true};
        live.ApplyCatalog(new ReleaseCatalog(null,new[]{oldBeta},catalog.Stable));Assert(live.CanInstallUpdate,"An older beta must be explicitly installable through the normal update action.");
        live.Preferences.AutoInstallUpdates=true;var installs=0;
        live.RunStartupUpdateCoreAsync(true,()=>System.Threading.Tasks.Task.CompletedTask,()=>{installs++;return System.Threading.Tasks.Task.CompletedTask;}).GetAwaiter().GetResult();
        Assert(installs==0,"Automatic updates must never install an older beta.");
        live.ApplyCatalog(new ReleaseCatalog(null,new[]{oldBeta with {Version=live.AppVersion}},catalog.Stable));Assert(!live.CanInstallUpdate&&live.UpdateStatus.Contains("up to date"),"The installed beta must not be offered for reinstallation.");live.Dispose();
        Console.WriteLine("PASS: first-wheel preview, integrated beta discovery, installed-stable warning, no duplicate section, explicit older-beta action, and no automatic downgrade.");
    }
}
