using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WaveWheels;

internal sealed class ProfileAutomation
{
    private string? foreground,manualOverride;
    private long stableSince;
    public static string Normalize(string application)
    {
        var name=Path.GetFileName(application.Trim().Trim('"'));
        return (name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name[..^4]:name).ToLowerInvariant();
    }
    public static bool Matches(string rules,string process)=>rules.Split(new[]{',',';','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Any(rule=>Normalize(rule)==Normalize(process));
    public void ManualSelection()=>manualOverride=foreground;
    public void Reset() {foreground=null;manualOverride=null;stableSince=0;}
    public string? Evaluate(string? process,IEnumerable<WheelProfile> profiles,string manualProfileId,long now)
    {
        if(string.IsNullOrWhiteSpace(process))return null;
        var name=Normalize(process);
        if(foreground!=name) {foreground=name;stableSince=now;manualOverride=null;return null;}
        if(now-stableSince<500||manualOverride==name)return null;
        return profiles.FirstOrDefault(p=>Matches(p.Applications,name))?.Id??manualProfileId;
    }
    public static string? ForegroundProcess()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out var id);
        if(id==0||id==Environment.ProcessId)return null;
        try {using var process=Process.GetProcessById((int)id);return process.ProcessName;}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) {return null;}
    }
}
