using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WaveWheels;

internal sealed record DownloadProgress(long Received,long Total)
{
    public double Percent=>100d*Received/Total;
}

internal static class UpdateInstaller
{
    public const long MaximumBytes=256L*1024*1024;
    public static async Task<string> DownloadAsync(ReleaseUpdate update,string cache,IProgress<DownloadProgress> progress,CancellationToken token,HttpClient? transport=null)
    {
        var asset=update.Installer??throw new InvalidOperationException("This release has no verified Windows installer. Open the release page instead.");
        // Validate again at the execution boundary, even when called independently of release discovery.
        if(ReleaseVersion.Parse(update.Version)==null)throw new InvalidDataException("Invalid release version.");
        var expectedName=$"WaveWheels-{update.Version}-Setup.exe";
        var prefix="https://github.com/notjacobwashere/WaveWheels/releases/download/";
        if(asset.Name!=expectedName||!Uri.TryCreate(asset.Url,UriKind.Absolute,out var uri)||!asset.Url.StartsWith(prefix,StringComparison.Ordinal)||uri.AbsolutePath.Split('/').Length!=7||!uri.AbsolutePath.EndsWith("/"+expectedName,StringComparison.Ordinal)||uri.Query.Length!=0||uri.Fragment.Length!=0||asset.Size<=0||asset.Size>MaximumBytes||!System.Text.RegularExpressions.Regex.IsMatch(asset.Sha256,"^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("The release installer metadata is invalid.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));token=deadline.Token;
        var directory=Path.Combine(Path.GetFullPath(cache),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var partial=Path.Combine(directory,asset.Name+".partial");
        var target=Path.Combine(directory,asset.Name);
        var ownsClient=transport==null;
        var client=transport??new HttpClient {Timeout=TimeSpan.FromMinutes(10)};
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,asset.Url);
            request.Headers.UserAgent.ParseAdd("WaveWheels/"+ReleaseUpdates.CurrentVersion);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("The download must use HTTPS.");
            if(response.Content.Headers.ContentLength is long length&&length!=asset.Size)throw new InvalidDataException("The installer download size does not match the release.");
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                var buffer=new byte[81920];long received=0;progress.Report(new(0,asset.Size));
                while(true)
                {
                    var count=await input.ReadAsync(buffer,token).ConfigureAwait(false);if(count==0)break;
                    received+=count;if(received>asset.Size)throw new InvalidDataException("The installer download exceeds its expected size.");
                    hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);
                    progress.Report(new(received,asset.Size));
                }
                if(received!=asset.Size||!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(asset.Sha256)))throw new InvalidDataException("Installer verification failed. Nothing was installed. Try again or open the release page.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();File.Move(partial,target);return target;
        }
        catch
        {
            // Only files in this newly created attempt directory are removed.
            try {File.Delete(partial);File.Delete(target);Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
        finally {if(ownsClient)client.Dispose();}
    }
    internal static ProcessStartInfo StartInfo(string installer,string? installedDirectory)
    {
        var start=new ProcessStartInfo(Path.GetFullPath(installer)){UseShellExecute=true};
        foreach(var argument in new[]{"/SILENT","/NORESTART","/SP-","/UPDATEFROMAPP=1"})start.ArgumentList.Add(argument);
        if(installedDirectory!=null)start.ArgumentList.Add("/DIR="+Path.GetFullPath(installedDirectory));
        return start;
    }
    public static void Launch(string installer)
    {
        // Upgrade the remembered installation in place; portable copies use Setup's default location.
        var installed=File.Exists(Path.Combine(AppContext.BaseDirectory,"unins000.exe"))?AppContext.BaseDirectory:null;
        using var process=Process.Start(StartInfo(installer,installed))??throw new IOException("Windows did not start Setup.");
    }
}
