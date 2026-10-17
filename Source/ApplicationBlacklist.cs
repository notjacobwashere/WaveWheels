using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace WaveWheels;

internal static class ApplicationBlacklist
{
    public static string? Normalize(string? executable)
    {
        var name=(executable??"").Trim().Trim('"');
        if(name.Length==0||name.Length>160||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return null;
        if(!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))name+=".exe";
        if(name[..^4].Trim(' ','.').Length==0)return null;
        return name.ToLowerInvariant();
    }
    public static bool Matches(IEnumerable<string> entries,string? process)
    {
        var name=Normalize(process);return name!=null&&entries.Any(entry=>string.Equals(Normalize(entry),name,StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class ForegroundFocusMonitor : IDisposable
{
    private delegate void WinEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly WinEvent callback;
    private readonly IntPtr handle;
    public ForegroundFocusMonitor(Action changed)
    {
        callback=(_,_,_,_,_,_,_)=>changed();
        // Out-of-context foreground events run on the owning UI thread; polling remains a fallback.
        handle=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
    }
    public void Dispose() {if(handle!=IntPtr.Zero)UnhookWinEvent(handle);GC.KeepAlive(callback);}
}
