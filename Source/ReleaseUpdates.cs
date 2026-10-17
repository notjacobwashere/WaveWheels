using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WaveWheels;

internal sealed record InstallerAsset(string Name,string Url,long Size,string Sha256);
internal sealed record ReleaseUpdate(string Version, string Url,InstallerAsset? Installer=null);
internal static class ReleaseUpdates
{
    public const string ReleasesUrl="https://github.com/notjacobwashere/WaveWheels/releases/latest";
    public static string CurrentVersion => typeof(App).Assembly.GetName().Version?.ToString(3)??"0.7.1";
    public static ReleaseUpdate? Parse(string json,Version current)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.TryGetProperty("draft",out var draft)&&draft.GetBoolean()||root.TryGetProperty("prerelease",out var pre)&&pre.GetBoolean())return null;
        var tag=root.GetProperty("tag_name").GetString()??"";
        if(!Version.TryParse(tag.TrimStart('v','V'),out var version))return null;
        Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
        if(Normalize(version)<=Normalize(current))return null;
        var name=$"WaveWheels-{version.ToString()}-Setup.exe";
        var url="https://github.com/notjacobwashere/WaveWheels/releases/download/"+Uri.EscapeDataString(tag)+"/"+name;
        InstallerAsset? installer=null;
        if(root.TryGetProperty("assets",out var assets)&&assets.ValueKind==JsonValueKind.Array)
            foreach(var asset in assets.EnumerateArray())
            {
                if(!asset.TryGetProperty("name",out var assetName)||assetName.GetString()!=name||!asset.TryGetProperty("browser_download_url",out var download)||download.GetString()!=url)continue;
                if(!asset.TryGetProperty("size",out var size)||!size.TryGetInt64(out var length)||length<=0||length>UpdateInstaller.MaximumBytes)continue;
                if(!asset.TryGetProperty("digest",out var digest)||digest.ValueKind!=JsonValueKind.String)continue;
                var hash=digest.GetString()??"";
                if(!System.Text.RegularExpressions.Regex.IsMatch(hash,"^sha256:[0-9a-fA-F]{64}$"))continue;
                installer=new(name,url,length,hash[7..].ToLowerInvariant());break;
            }
        return new(version.ToString(),"https://github.com/notjacobwashere/WaveWheels/releases/tag/"+Uri.EscapeDataString(tag),installer);
    }
    public static async Task<ReleaseUpdate?> CheckAsync(CancellationToken token)
    {
        using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WaveWheels/"+CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var json=await client.GetStringAsync("https://api.github.com/repos/notjacobwashere/WaveWheels/releases/latest",token).ConfigureAwait(false);
        return Parse(json,new Version(CurrentVersion));
    }
}
