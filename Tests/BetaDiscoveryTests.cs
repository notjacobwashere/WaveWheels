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
        model.ApplyCatalog(catalog);Assert(model.ShowLatestBeta&&model.LatestBetaBuild?.Version=="1.0.0-beta.3"&&model.BetaOlderThanStable&&model.LatestBetaWarning.Contains("2.0.0"),"The latest beta must remain visible with a stable-version warning even if both installed and stable versions are newer.");
        var card=(Border)window.FindName("LatestBetaCard");card.DataContext=model;var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        Assert(card.Visibility==Visibility.Visible&&!model.CanInstallLatestBeta,"Beta discovery UI must be visible while isolated profiles retain installation protection.");
        model.ApplyCatalog(new ReleaseCatalog(null,Array.Empty<ReleaseUpdate>(),catalog.Stable));Assert(!model.ShowLatestBeta&&!model.BetaOlderThanStable,"Missing beta releases must clear the card and stale warning.");model.Dispose();
        var live=new MainViewModel(store,false);var oldBeta=UpdateTests.Fixture(new byte[]{1,2,3}) with {Version="1.0.0-beta.3",IsBeta=true};
        live.ApplyCatalog(new ReleaseCatalog(null,new[]{oldBeta},catalog.Stable));Assert(live.CanInstallLatestBeta,"An older beta must remain explicitly installable from the Beta channel.");
        live.ApplyCatalog(new ReleaseCatalog(null,new[]{oldBeta},new ReleaseUpdate("0.9.0","",null,false)));Assert(!live.BetaOlderThanStable,"A beta newer than stable must not show an old-version warning.");live.Dispose();
        Console.WriteLine("PASS: first-wheel configuration selection, Windows preview, latest-beta visibility below stable/current versions, warning lifecycle, draft exclusion, explicit older-beta installation, and no automatic downgrade.");
    }
}
