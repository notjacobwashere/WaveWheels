using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WaveWheels;

internal static class ProfileTransfer
{
    internal sealed class Bundle
    {
        public string Format {get;set;}="WaveWheelsProfiles";
        public int Version {get;set;}=1;
        public List<WheelProfile> Profiles {get;set;}=new();
    }
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static Wheel CloneWheel(Wheel w)=>new(){Name=w.Name,Color=w.Color,ChannelId=w.ChannelId,MixId=w.MixId,MaximumVolumePercent=w.MaximumVolumePercent};
    public static WheelProfile Clone(WheelProfile profile)=>new(){Name=profile.Name,Applications=profile.Applications,Wheels=profile.Wheels.Select(CloneWheel).ToList()};
    public static string Serialize(IEnumerable<WheelProfile> profiles)=>JsonSerializer.Serialize(new Bundle{Profiles=profiles.Select(Clone).ToList()},Options);
    public static List<WheelProfile> Parse(string json)
    {
        if(json.Length>2*1024*1024)throw new InvalidDataException("Profile files must be smaller than 2 MB.");
        var bundle=JsonSerializer.Deserialize<Bundle>(json,Options)??throw new InvalidDataException("The profile file is empty.");
        // Require the marker explicitly; omitted JSON fields must not turn arbitrary files into imports.
        using var doc=JsonDocument.Parse(json);
        if(!doc.RootElement.EnumerateObject().Any(p=>p.Name.Equals("Format",StringComparison.OrdinalIgnoreCase)&&p.Value.ValueKind==JsonValueKind.String&&p.Value.GetString()=="WaveWheelsProfiles")||bundle.Format!="WaveWheelsProfiles"||bundle.Version!=1)throw new InvalidDataException("This is not a supported WaveWheels profile file.");
        if(bundle.Profiles==null||bundle.Profiles.Count is <1 or >20)throw new InvalidDataException("Import between 1 and 20 profiles.");
        foreach(var profile in bundle.Profiles)
        {
            if(profile==null||profile.Name.Length>40||profile.Applications.Length>2048||profile.Wheels==null||profile.Wheels.Count is <1 or >12)throw new InvalidDataException("A profile has invalid fields or wheel count.");
            foreach(var wheel in profile.Wheels)
                if(wheel==null||wheel.Name.Length>32||wheel.ChannelId.Length>512||wheel.MixId.Length>512||!Regex.IsMatch(wheel.Color,"^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException("A wheel has an invalid name, color, or binding.");
        }
        return bundle.Profiles.Select(Clone).ToList();
    }
}
