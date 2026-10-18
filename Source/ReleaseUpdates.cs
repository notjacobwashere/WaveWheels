using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveWheels;

public sealed record InstallerAsset(string Name,string Url,long Size,string Sha256);
public sealed record ReleaseUpdate(string Version, string Url,InstallerAsset? Installer=null,bool IsBeta=false);
internal sealed record ReleaseCatalog(ReleaseUpdate? Latest,IReadOnlyList<ReleaseUpdate> Betas,ReleaseUpdate? Stable=null);
internal sealed record ReleaseVersion(Version Core,string Suffix):IComparable<ReleaseVersion>
{
    public static ReleaseVersion? Parse(string text)
    {
        var match=Regex.Match(text,@"^[vV]?(\d+\.\d+(?:\.\d+){0,2})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$");
        return match.Success&&Version.TryParse(match.Groups[1].Value,out var v)?new(new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision)),match.Groups[2].Value):null;
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if(other==null)return 1;var core=Core.CompareTo(other.Core);if(core!=0)return core;
        if(Suffix==other.Suffix)return 0;if(Suffix.Length==0)return 1;if(other.Suffix.Length==0)return -1;
        var a=Suffix.Split('.');var b=other.Suffix.Split('.');
        for(var i=0;i<Math.Min(a.Length,b.Length);i++)
        {
            var an=a[i].All(char.IsDigit);var bn=b[i].All(char.IsDigit);int compare;
            if(an&&bn) {var x=a[i].TrimStart('0');var y=b[i].TrimStart('0');compare=x.Length.CompareTo(y.Length);if(compare==0)compare=string.CompareOrdinal(x,y);}
            else compare=an!=bn?(an?-1:1):string.CompareOrdinal(a[i],b[i]);
            if(compare!=0)return compare;
        }
        return a.Length.CompareTo(b.Length);
    }
}
internal static class ReleaseUpdates
{
    public const string ReleasesUrl="https://github.com/notjacobwashere/WaveWheels/releases/latest";
    public static string CurrentVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??typeof(App).Assembly.GetName().Version?.ToString(3)??"0.10.0-beta.1";
    public static ReleaseUpdate? Parse(string json,Version current)
    {
        using var doc=JsonDocument.Parse(json);var release=Read(doc.RootElement);
        return release!=null&&!release.IsBeta&&ReleaseVersion.Parse(release.Version)!.CompareTo(ReleaseVersion.Parse(current.ToString()))>0?release:null;
    }
    private static ReleaseUpdate? Read(JsonElement root)
    {
        if(root.TryGetProperty("draft",out var draft)&&draft.GetBoolean())return null;
        var tag=root.GetProperty("tag_name").GetString()??"";
        var version=ReleaseVersion.Parse(tag);if(version==null)return null;
        var text=tag.TrimStart('v','V').Split('+')[0];
        var beta=version.Suffix.Length>0||(root.TryGetProperty("prerelease",out var pre)&&pre.GetBoolean());
        var name=$"WaveWheels-{text}-Setup.exe";
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
        return new(text,"https://github.com/notjacobwashere/WaveWheels/releases/tag/"+Uri.EscapeDataString(tag),installer,beta);
    }
    internal static ReleaseCatalog ParseCatalog(string json,string current,bool beta)
    {
        using var doc=JsonDocument.Parse(json);
        var releases=doc.RootElement.EnumerateArray().Select(Read).Where(r=>r!=null).Cast<ReleaseUpdate>().OrderByDescending(r=>ReleaseVersion.Parse(r.Version)).ToArray();
        var installed=ReleaseVersion.Parse(current)??throw new FormatException("Invalid installed version.");
        return new(releases.FirstOrDefault(r=>(beta||!r.IsBeta)&&ReleaseVersion.Parse(r.Version)!.CompareTo(installed)>0),releases.Where(r=>r.IsBeta).ToArray(),releases.FirstOrDefault(r=>!r.IsBeta));
    }
    public static async Task<ReleaseCatalog> CheckAsync(bool beta,CancellationToken token)
    {
        using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WaveWheels/"+CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var entries=new List<JsonElement>();
        for(var page=1;page<=5;page++)
        {
            var json=await client.GetStringAsync($"https://api.github.com/repos/notjacobwashere/WaveWheels/releases?per_page=100&page={page}",token).ConfigureAwait(false);
            using var doc=JsonDocument.Parse(json);var count=0;foreach(var item in doc.RootElement.EnumerateArray()) {entries.Add(item.Clone());count++;}if(count<100)break;
        }
        return ParseCatalog(JsonSerializer.Serialize(entries),CurrentVersion,beta);
    }
}
