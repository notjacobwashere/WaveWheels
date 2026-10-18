using System;
using System.Collections.Generic;
using System.Linq;
using Forms=System.Windows.Forms;

namespace WaveWheels;

internal sealed record MonitorDescriptor(string DeviceName,string Id,string Label,string PreviousId="")
{
    public static MonitorDescriptor Capture(Forms.Screen screen,int index)
    {
        var identity=Native.DisplayIdentities(screen.DeviceName);
        return new(screen.DeviceName,identity.Id,$"Display {index+1}{(screen.Primary?" · primary":"")} · {screen.Bounds.Width}×{screen.Bounds.Height}",identity.PreviousId);
    }
}

internal static class MonitorSelection
{
    private static bool Equal(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    public static bool Matches(string saved,MonitorDescriptor monitor)=>saved.Length==0||Equal(saved,monitor.Id)||Equal(saved,monitor.DeviceName);
    public static string Resolve(string saved,IEnumerable<MonitorDescriptor> monitors)
    {
        if(saved.Length==0)return "";
        var snapshot=monitors.ToArray();
        var exact=snapshot.FirstOrDefault(m=>Equal(saved,m.Id));
        if(exact!=null)return exact.Id;
        var candidates=snapshot.Where(m=>Equal(saved,m.DeviceName)||m.PreviousId.Length>0&&Equal(saved,m.PreviousId)).Select(m=>m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Unknown/disconnected and ambiguous legacy IDs must not silently select a different display.
        return candidates.Length==1?candidates[0]:saved;
    }
}
