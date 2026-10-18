using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WaveWheels;

internal static class UpdateTests
{
    private static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
    internal static ReleaseUpdate Fixture(byte[] bytes)
    {
        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new("0.7.0","https://github.com/notjacobwashere/WaveWheels/releases/tag/v0.7.0",new("WaveWheels-0.7.0-Setup.exe","https://github.com/notjacobwashere/WaveWheels/releases/download/v0.7.0/WaveWheels-0.7.0-Setup.exe",bytes.Length,hash));
    }
    private sealed class Handler:HttpMessageHandler
    {
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply {get;set;}=null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Reply(request,token);
    }
    private sealed class ProgressSink:IProgress<DownloadProgress>
    {
        public DownloadProgress? Last;
        public void Report(DownloadProgress value)=>Last=value;
    }
    private static async Task Reject(Func<Task> operation)
    {
        try {await operation();}catch(Exception ex) when(ex is InvalidDataException or IOException or HttpRequestException or OperationCanceledException) {return;}
        throw new Exception("An invalid or canceled installer was accepted.");
    }
    public static async Task Downloads()
    {
        var bytes=Encoding.UTF8.GetBytes("Fixture installer bytes. Never execute this file.");var update=Fixture(bytes);var asset=update.Installer!;
        var json=JsonSerializer.Serialize(new{tag_name="v0.7.0",draft=false,prerelease=false,assets=new[]{new{name=asset.Name,browser_download_url=asset.Url,size=asset.Size,digest="sha256:"+asset.Sha256}}});
        Assert(ReleaseUpdates.Parse(json,new Version(0,6,0))?.Installer==asset,"Only the exact official Windows installer and valid digest should enable installation.");
        Assert(ReleaseUpdates.Parse(json.Replace("https://github.com/","https://evil.invalid/"),new Version(0,6,0))?.Installer==null,"Off-repository installers must be rejected.");
        Assert(ReleaseUpdates.Parse(json.Replace("sha256:","sha1:"),new Version(0,6,0))?.Installer==null,"Missing trusted SHA-256 must leave the release-page fallback.");
        Assert(ReleaseUpdates.Parse("{\"tag_name\":\"v1.0\"}",new Version(0,6,0))!=null,"Two-part version tags must still support notification links.");
        string Release(string tag,bool pre=false,bool draft=false)=>JsonSerializer.Serialize(new {tag_name=tag,prerelease=pre,draft,assets=Array.Empty<object>()});
        var history="["+string.Join(",",Release("v0.9.0-beta.2",true),Release("v0.9.0-beta.10",true),Release("v0.8.2"),Release("v1.0.0",true,true),Release("v0.8.0",true),Release("v0.9.0-rc.1"),Release("nightly"))+"]";
        Assert(ReleaseUpdates.ParseCatalog(history,"0.8.1",false).Latest?.Version=="0.8.2","Stable must exclude flagged and suffix prereleases and drafts.");
        var catalog=ReleaseUpdates.ParseCatalog(history,"0.8.1",true);
        Assert(catalog.Stable?.Version=="0.8.2"&&ReleaseUpdates.ParseCatalog(history,"0.9.0-beta.10",false).Stable?.Version=="0.8.2","Latest stable must remain available for explicit rollback from a newer beta.");
        Assert(catalog.Latest?.Version=="0.9.0-rc.1"&&catalog.Betas.Count==4,"Beta channel must include prereleases and retain old versions in its archive.");
        Assert(catalog.Betas[1].Version=="0.9.0-beta.10"&&catalog.Betas[2].Version=="0.9.0-beta.2","Beta identifiers must sort numerically rather than lexically.");
        Assert(ReleaseUpdates.ParseCatalog(history,"0.9.0",true).Latest==null,"Older betas must never be offered as automatic upgrades.");
        Assert(ReleaseUpdates.ParseCatalog("["+Release("v0.9.0")+"]","0.9.0-beta.10",true).Latest?.Version=="0.9.0","Stable must supersede its own prereleases on the beta channel.");
        Assert(ReleaseUpdates.ParseCatalog("["+Release("v0.9.1",true)+"]","0.9.0",true).Latest?.IsBeta==true,"Numeric version tags marked prerelease must count as betas.");
        var betaJson=json.Replace("0.7.0","0.9.0-beta.10").Replace("\"prerelease\":false","\"prerelease\":true");
        var betaUpdate=ReleaseUpdates.ParseCatalog("["+betaJson+"]","0.8.1",true).Latest!;
        Assert(betaUpdate.Installer!=null,"Suffix beta tags must retain verified installers.");
        var directory=Path.Combine(AppContext.BaseDirectory,"update-download-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var progress=new ProgressSink();
        using var handler=new Handler();using var client=new HttpClient(handler);
        handler.Reply=(request,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});
        var file=await UpdateInstaller.DownloadAsync(update,directory,progress,CancellationToken.None,client);
        var betaFile=await UpdateInstaller.DownloadAsync(betaUpdate,directory,progress,CancellationToken.None,client);
        File.Delete(betaFile);Directory.Delete(Path.GetDirectoryName(betaFile)!);
        Assert(File.ReadAllBytes(file).SequenceEqual(bytes)&&progress.Last?.Percent==100,"The verified download and determinate progress must match the release.");
        Assert(!Directory.EnumerateFiles(directory,"*.partial",SearchOption.AllDirectories).Any(),"Successful downloads must finalize the partial file.");
        var attempts=Directory.GetDirectories(directory).Length;
        await Reject(()=>UpdateInstaller.DownloadAsync(update with{Installer=asset with{Sha256=new string('0',64)}},directory,progress,CancellationToken.None,client));
        await Reject(()=>UpdateInstaller.DownloadAsync(update with{Installer=asset with{Size=asset.Size+1}},directory,progress,CancellationToken.None,client));
        await Reject(()=>UpdateInstaller.DownloadAsync(update with{Installer=asset with{Url="http://evil.invalid/setup.exe"}},directory,progress,CancellationToken.None,client));
        handler.Reply=(request,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){RequestMessage=request,Content=new ByteArrayContent(Array.Empty<byte>())});
        await Reject(()=>UpdateInstaller.DownloadAsync(update,directory,progress,CancellationToken.None,client));
        using var cancellation=new CancellationTokenSource();
        handler.Reply=async(request,token)=> {cancellation.Cancel();await Task.Delay(50,token);return new HttpResponseMessage(HttpStatusCode.OK);};
        await Reject(()=>UpdateInstaller.DownloadAsync(update,directory,progress,cancellation.Token,client));
        Assert(Directory.GetDirectories(directory).Length==attempts,"Failed and canceled attempts must remove partial files and attempt folders.");
        var start=UpdateInstaller.StartInfo(file,@"C:\Fixture Path\WaveWheels");
        Assert(start.UseShellExecute&&start.ArgumentList.Contains("/UPDATEFROMAPP=1")&&start.ArgumentList.Contains("/DIR=C:\\Fixture Path\\WaveWheels")&&!start.ArgumentList.Contains("/VERYSILENT"),"The installer must use shell handoff, preserve install location, and request reopening with visible progress.");
        Assert(!UpdateInstaller.StartInfo(file,null).ArgumentList.Any(a=>a.StartsWith("/DIR=")),"Portable builds must use Setup's default installation location.");
        File.Delete(file);Directory.Delete(Path.GetDirectoryName(file)!);Directory.Delete(directory);
        Console.WriteLine("PASS: official update asset validation, hashes, download progress, failures/cancellation cleanup, and installer handoff arguments.");
    }
    public static void ViewModel(string directory)
    {
        var store=new SettingsStore(directory);var vm=new MainViewModel(store,false);var update=Fixture(new byte[]{1,2,3});
        vm.ApplyRelease(update);Assert(vm.CanInstallUpdate&&vm.CanCheckUpdates,"A verified available installer must enable the action.");
        var launches=0;var exits=0;vm.UpdateInstallationStarted+=()=>exits++;
        vm.InstallUpdateCoreAsync((_,_,_)=>Task.FromException<string>(new IOException("fixture download failed")),_=>launches++).GetAwaiter().GetResult();
        Assert(launches==0&&exits==0&&vm.CanInstallUpdate&&vm.UpdateStatus.Contains("failed"),"Download failures must leave the app running and allow retry.");
        vm.InstallUpdateCoreAsync((_,_,_)=>Task.FromException<string>(new InvalidDataException("fixture verification failed")),_=>launches++).GetAwaiter().GetResult();
        Assert(launches==0&&exits==0&&vm.UpdateStatus.Contains("verification failed"),"Verification failures must show an error without starting Setup.");
        vm.InstallUpdateCoreAsync((_,_,_)=>Task.FromResult("fixture"),_=>throw new System.ComponentModel.Win32Exception("launch failed")).GetAwaiter().GetResult();
        Assert(exits==0&&vm.CanInstallUpdate,"A failed Windows handoff must not exit the app.");
        vm.InstallUpdateCoreAsync(async(_,_,token)=> {Assert(vm.InstallingUpdate&&!vm.CanCheckUpdates&&!vm.CanInstallUpdate,"Concurrent actions must be blocked while downloading.");vm.CancelUpdate();await Task.Delay(1,token);return "never";},_=>launches++).GetAwaiter().GetResult();
        Assert(exits==0&&launches==0&&vm.UpdateStatus.Contains("canceled"),"Cancellation must retain the current app and permit retry.");
        vm.Wheels[0].Name="Saved before handoff";
        vm.InstallUpdateCoreAsync((_,_,_)=>Task.FromResult("fixture"),_=> {Assert(store.Load().Wheels[0].Name=="Saved before handoff","Pending settings must save before launching Setup.");Assert(exits==0,"Exit must follow successful handoff.");launches++;}).GetAwaiter().GetResult();
        Assert(launches==1&&exits==1,"Successful handoff must request exactly one graceful exit.");
        vm.ApplyRelease(update with{Installer=null});Assert(!vm.CanInstallUpdate,"A release without a verified installer must only offer browser fallback.");
        vm.Preferences.UpdateChannel="Beta";
        vm.ApplyCatalog(new(null,new[]{update with{IsBeta=true}}));
        Assert(vm.CanInstallBeta&&!vm.CanInstallUpdate,"Older archived betas must have an explicit install action independent of automatic upgrades.");
        vm.Save();Assert(store.Load().UpdateChannel=="Beta","The selected update channel must survive reload.");
        vm.ApplyCatalog(new(null,Array.Empty<ReleaseUpdate>(),update));
        Assert(vm.CanInstallStable,"An older stable installer must be offered as an explicit rollback.");
        vm.InstallStableCoreAsync((selected,_,_)=> {Assert(selected==update,"Stable rollback must download the selected stable release.");return Task.FromResult("fixture");},_=> {Assert(store.Load().UpdateChannel=="Stable","Rollback must persist Stable before installer handoff.");launches++;}).GetAwaiter().GetResult();
        Assert(vm.UpdateChannel=="Stable"&&launches==2&&exits==2,"Returning to stable must use the verified installer lifecycle.");
        vm.ApplyCatalog(new(null,Array.Empty<ReleaseUpdate>(),update with{Installer=null}));Assert(!vm.CanInstallStable,"Stable rollback requires a verified installer.");
        vm.Dispose();var isolated=new MainViewModel(new SettingsStore(Path.Combine(directory,"isolated")),true);isolated.ApplyRelease(update);Assert(!isolated.CanInstallUpdate,"Isolated profiles must never modify the installed app.");isolated.Dispose();
        Console.WriteLine("PASS: updater lifecycle, save-before-launch, no exit on errors/cancel, retry, and isolated-profile protection.");
    }
    public static async Task StageRelease(string jsonPath,string cache)
    {
        var update=ReleaseUpdates.Parse(File.ReadAllText(jsonPath),new Version(0,0,0))??throw new Exception("No stable release found.");
        var path=await UpdateInstaller.DownloadAsync(update,cache,new ProgressSink(),CancellationToken.None);
        Console.WriteLine("VERIFIED RELEASE: "+update.Version+" "+path);
    }
}
