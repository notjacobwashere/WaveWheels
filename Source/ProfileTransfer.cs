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
    public static WheelProfile Clone(WheelProfile profile)
    {
        // Use the persisted profile contract so newly saved settings are included automatically.
        var copy = JsonSerializer.SerializeToElement(profile, Options).Deserialize<WheelProfile>(Options)!;
        AssignNewIds(copy);
        return copy;
    }

    private static void AssignNewIds(WheelProfile profile)
    {
        profile.Id = Guid.NewGuid().ToString("N");
        foreach (var wheel in profile.Wheels) wheel.Id = Guid.NewGuid().ToString("N");
    }
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
                if(wheel==null||wheel.Name.Length>32||wheel.ChannelId.Length>512||wheel.MixId.Length>512||wheel.LeftClickOutput.Length>512||wheel.RightClickOutput.Length>512||!Regex.IsMatch(wheel.Color,"^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException("A wheel has an invalid name, color, or binding.");
        }
        foreach (var profile in bundle.Profiles) AssignNewIds(profile);
        return bundle.Profiles;
    }
}
