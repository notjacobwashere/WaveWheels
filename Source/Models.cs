using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace WaveWheels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
    public void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Wheel : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string name = "New wheel", color = "#36BBD3", channelId = "", mixId = "";
    public string Name { get => name; set => Set(ref name, value ?? ""); }
    public string Color { get => color; set { if (Set(ref color, value ?? "")) Changed(nameof(Accent)); } }
    public string ChannelId { get => channelId; set => Set(ref channelId, value ?? ""); }
    public string MixId { get => mixId; set => Set(ref mixId, value ?? ""); }
    private double level;
    private bool muted, available;
    private string detail = "Choose a Wave Link channel";
    private int position;
    [JsonIgnore] public int Position { get => position; set { if (Set(ref position, value)) Changed(nameof(OrderLabel)); } }
    [JsonIgnore] public string OrderLabel => (Position + 1).ToString("00");
    [JsonIgnore] public double Level { get => level; set { if (Set(ref level, Math.Clamp(value, 0, 1))) Changed(nameof(VolumeLabel)); } }
    [JsonIgnore] public bool Muted { get => muted; set { if (Set(ref muted, value)) { Changed(nameof(MuteLabel)); Changed(nameof(VolumeLabel)); } } }
    [JsonIgnore] public bool Available { get => available; set { if (Set(ref available, value)) Changed(nameof(VolumeLabel)); } }
    [JsonIgnore] public string Detail { get => detail; set => Set(ref detail, value); }
    [JsonIgnore] public string VolumeLabel => Available ? $"{Math.Round(Level * 100):0}%{(Muted ? " · muted" : "")}" : "Unavailable";
    [JsonIgnore] public string MuteLabel => Muted ? "Unmute channel" : "Mute channel";
    [JsonIgnore] public Brush Accent
    {
        get { try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color)); } catch { return Brushes.DeepSkyBlue; } }
    }
}

public sealed class Preferences
{
    public int SchemaVersion { get; set; } = 1;
    public bool EdgeEnabled { get; set; } = true;
    public int EdgeWidth { get; set; } = 6;
    public int IdleMilliseconds { get; set; } = 1000;
    public int PortOverride { get; set; }
    public int WheelSizePercent { get; set; } = 100;
    public bool AnimateWheels { get; set; } = true;
    public List<Wheel> Wheels { get; set; } = new() {
        new Wheel { Name = "Music", Color = "#36BBD3" },
        new Wheel { Name = "Game", Color = "#DA5DAD" },
        new Wheel { Name = "Voice", Color = "#A99AFF" }
    };
}

public sealed class SettingsStore
{
    private readonly string directory;
    private readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string? Warning { get; private set; }
    public string FilePath => Path.Combine(directory, "settings.json");
    public SettingsStore(string directory) { this.directory = directory; }
    public Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), options) ?? new();
            p.Wheels ??= new();
            p.Wheels = p.Wheels.Where(w => w != null).Take(12).ToList();
            if (p.Wheels.Count == 0) p.Wheels.Add(new Wheel());
            var ids = new HashSet<string>();
            foreach (var w in p.Wheels) if (string.IsNullOrEmpty(w.Id) || !ids.Add(w.Id)) { w.Id = Guid.NewGuid().ToString("N"); ids.Add(w.Id); }
            p.EdgeWidth = Math.Clamp(p.EdgeWidth, 2, 32);
            p.IdleMilliseconds = Math.Clamp(p.IdleMilliseconds, 500, 5000);
            p.WheelSizePercent = Math.Clamp(p.WheelSizePercent, 50, 180);
            p.PortOverride = p.PortOverride is >= 1024 and <= 65535 ? p.PortOverride : 0;
            return p;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep an unreadable settings file as evidence instead of silently overwriting it.
            if (File.Exists(FilePath)) { try { File.Copy(FilePath, FilePath + ".recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), false); } catch { } }
            Warning = "Settings could not be read. A fresh layout is loaded; the original file has been preserved where possible.";
            return new();
        }
    }
    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, options));
        File.Move(temp, FilePath, true);
    }
}

public sealed record Mix(string Id, string Name);
public sealed record ChannelMix(string Id, double Level, bool Muted);
public sealed record Channel(string Id, string Name, double Level, bool Muted, bool Available, IReadOnlyList<ChannelMix> Mixes, bool HasOverallLevel = true);
public sealed record MixerSnapshot(bool Connected, string Status, string Protocol, IReadOnlyList<Channel> Channels, IReadOnlyList<Mix> Mixes);
public sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
public readonly record struct Target(string ChannelId, string MixId);
