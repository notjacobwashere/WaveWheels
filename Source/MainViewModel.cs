using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace WaveWheels;

public sealed class MainViewModel : Observable
{
    private readonly SettingsStore store;
    private readonly StartupRegistration startup = new();
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer saveTimer;
    private MixerSnapshot snapshot = new(false, "Looking for Wave Link…", "", Array.Empty<Channel>(), Array.Empty<Mix>());
    private readonly bool isolated;
    public Preferences Preferences { get; }
    public WaveLinkClient Client { get; } = new();
    public ObservableCollection<Wheel> Wheels { get; }
    public ObservableCollection<Choice> Channels { get; } = new();
    public ObservableCollection<Choice> MixChoices { get; } = new();
    private Wheel? selected;
    private string saveStatus = "Changes save automatically", message = "", portText;
    public string SaveStatus { get => saveStatus; private set => Set(ref saveStatus, value); }
    public string Message { get => message; set => Set(ref message, value); }
    public string Status => snapshot.Status;
    public string ConnectionLabel => snapshot.Connected ? "CONNECTED" : "WAITING FOR WAVE LINK";
    public bool Connected => snapshot.Connected;
    public string ChannelCount => snapshot.Connected ? $"{snapshot.Channels.Count} input channels available" : "Your bindings are kept while offline";
    public string SettingsPath => store.FilePath;
    public bool Isolated => isolated;
    public string PortText { get => portText; set => Set(ref portText, value); }
    public bool StartupEnabled
    {
        get { try { return !isolated && startup.IsRegistered; } catch { return false; } }
        set
        {
            try { if (isolated) throw new InvalidOperationException("Startup changes are disabled in the isolated test profile."); startup.SetEnabled(value); Message = value ? "WaveWheels will start quietly at Windows sign-in." : "Sign-in startup is off."; }
            catch (Exception ex) { Message = "Could not update startup: " + ex.Message; }
            Changed(); Changed(nameof(StartupDescription));
        }
    }
    public string StartupDescription { get { try { return isolated ? "Isolated test profile · startup registration disabled" : startup.Description; } catch { return "Windows startup registration is unavailable."; } } }
    public bool EdgeEnabled { get => Preferences.EdgeEnabled; set { if (value == Preferences.EdgeEnabled) return; Preferences.EdgeEnabled = value; Changed(); ScheduleSave(); LayoutChanged?.Invoke(); } }
    public int WheelSizePercent
    {
        get => Preferences.WheelSizePercent;
        set { value = Math.Clamp(value, 50, 180); if (value == Preferences.WheelSizePercent) return; Preferences.WheelSizePercent = value; Changed(); Changed(nameof(WheelSizeLabel)); Changed(nameof(PreviewWidth)); ScheduleSave(); AppearanceChanged?.Invoke(); }
    }
    public string WheelSizeLabel => WheelSizePercent + "%";
    public double PreviewWidth => Math.Min(360, 300 * WheelSizePercent / 100d);
    public bool AnimateWheels
    {
        get => Preferences.AnimateWheels;
        set { if (value == Preferences.AnimateWheels) return; Preferences.AnimateWheels = value; Changed(); ScheduleSave(); AppearanceChanged?.Invoke(); }
    }
    public int WheelCount { get => Wheels.Count; set { value = Math.Clamp(value, 1, 12); while (Wheels.Count < value) AddWheel(); while (Wheels.Count > value) RemoveWheel(Wheels[^1]); } }
    public Wheel? SelectedWheel
    {
        get => selected;
        set { if (Set(ref selected, value)) { UpdateMixChoices(); SelectionChanged?.Invoke(); } }
    }
    public event Action? LayoutChanged;
    public event Action? AppearanceChanged;
    public event Action? StateChanged;
    public event Action? SelectionChanged;
    public event Action? PreviewRequested;

    public MainViewModel(SettingsStore store, bool isolated)
    {
        this.store = store; this.isolated = isolated;
        Preferences = store.Load();
        dispatcher = Application.Current.Dispatcher;
        portText = Preferences.PortOverride > 0 ? Preferences.PortOverride.ToString() : "";
        Wheels = new(Preferences.Wheels);
        UpdatePositions();
        foreach (var wheel in Wheels) wheel.PropertyChanged += WheelChanged;
        Wheels.CollectionChanged += CollectionChanged;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        saveTimer.Tick += (_, _) => Save();
        Client.Updated += data => dispatcher.BeginInvoke(() => Update(data));
        Client.Log = line => App.WriteLog(line);
        SelectedWheel = Wheels.FirstOrDefault();
        Message = store.Warning ?? "Scroll at the left edge of a screen to bring up your wheels.";
        Update(snapshot);
    }
    public void Start() => Client.Start(Preferences.PortOverride);
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Wheel w in e.OldItems) w.PropertyChanged -= WheelChanged;
        if (e.NewItems != null) foreach (Wheel w in e.NewItems) w.PropertyChanged += WheelChanged;
        UpdatePositions(); Changed(nameof(WheelCount)); ScheduleSave(); Update(snapshot); LayoutChanged?.Invoke();
    }
    private void WheelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Wheel.Name) or nameof(Wheel.Color) or nameof(Wheel.ChannelId) or nameof(Wheel.MixId))) return;
        if (e.PropertyName == nameof(Wheel.ChannelId) && sender is Wheel wheel)
        {
            var channel = snapshot.Channels.FirstOrDefault(c => c.Id == wheel.ChannelId);
            wheel.MixId = channel?.HasOverallLevel == true ? "" : channel?.Mixes.FirstOrDefault()?.Id ?? "";
        }
        UpdateMixChoices(); SyncWheels(); ScheduleSave(); StateChanged?.Invoke();
    }
    internal void Update(MixerSnapshot data)
    {
        snapshot = data;
        var choices = data.Channels.Select(c => new Choice(c.Id, c.Name)).ToList();
        choices.Insert(0, new("", "Choose an input channel…"));
        foreach (var w in Wheels.Where(w => w.ChannelId.Length > 0)) if (!choices.Any(c => c.Id == w.ChannelId)) choices.Add(new(w.ChannelId, "Saved channel · currently unavailable"));
        ReplaceChoices(Channels, choices.ToArray());
        UpdateMixChoices(); SyncWheels();
        Changed(nameof(Status)); Changed(nameof(Connected)); Changed(nameof(ConnectionLabel)); Changed(nameof(ChannelCount)); StateChanged?.Invoke();
    }
    private void SyncWheels()
    {
        foreach (var w in Wheels)
        {
            var channel = snapshot.Channels.FirstOrDefault(c => c.Id == w.ChannelId);
            var mix = channel?.Mixes.FirstOrDefault(m => m.Id == w.MixId);
            w.Available = snapshot.Connected && channel?.Available == true && (w.MixId.Length > 0 ? mix != null : channel.HasOverallLevel);
            if (w.Available) { w.Level = mix?.Level ?? channel!.Level; w.Muted = mix?.Muted ?? channel!.Muted; }
            var scope = w.MixId.Length == 0 ? "Whole channel" : snapshot.Mixes.FirstOrDefault(m => m.Id == w.MixId)?.Name ?? "Saved mix";
            w.Detail = channel == null ? (w.ChannelId.Length == 0 ? "Not bound yet" : "Saved channel · unavailable") : channel.Name + " · " + scope;
        }
    }
    private void UpdateMixChoices()
    {
        var channel = snapshot.Channels.FirstOrDefault(c => c.Id == selected?.ChannelId);
        var choices = (channel?.Mixes ?? Array.Empty<ChannelMix>()).Select(m => new Choice(m.Id, snapshot.Mixes.FirstOrDefault(x => x.Id == m.Id)?.Name ?? m.Id)).ToList();
        if (channel?.HasOverallLevel != false) choices.Insert(0, new("", "Whole channel · all mixes"));
        if (selected?.MixId.Length > 0 && !choices.Any(c => c.Id == selected.MixId)) choices.Add(new(selected.MixId, "Saved mix · unavailable"));
        ReplaceChoices(MixChoices, choices.ToArray());
    }
    private static void ReplaceChoices(ObservableCollection<Choice> list, Choice[] next)
    {
        if (list.SequenceEqual(next)) return;
        // Diff in place; retain selected ComboBox items whenever their IDs still exist.
        for (var i = list.Count - 1; i >= 0; i--) if (!next.Contains(list[i])) list.RemoveAt(i);
        foreach (var item in next) if (!list.Contains(item)) list.Add(item);
    }
    public void AddWheel()
    {
        if (Wheels.Count >= 12) return;
        var colors = new[] { "#36BBD3", "#DA5DAD", "#A99AFF", "#63CEA4", "#F4B16F" };
        var w = new Wheel { Name = "Wheel " + (Wheels.Count + 1), Color = colors[Wheels.Count % colors.Length] };
        Wheels.Add(w); SelectedWheel = w;
    }
    public void RemoveWheel(Wheel? wheel = null)
    {
        wheel ??= SelectedWheel;
        if (wheel == null || Wheels.Count <= 1) { Message = "Keep at least one wheel in your layout."; return; }
        var i = Wheels.IndexOf(wheel); Wheels.Remove(wheel); SelectedWheel = Wheels[Math.Min(i, Wheels.Count - 1)];
    }
    public void Move(int delta) { if (SelectedWheel != null) MoveTo(SelectedWheel, Wheels.IndexOf(SelectedWheel) + delta); }
    public void MoveTo(Wheel wheel, int index)
    {
        var old = Wheels.IndexOf(wheel); index = Math.Clamp(index, 0, Wheels.Count - 1);
        if (old < 0 || old == index) return;
        Wheels.Move(old, index); SelectedWheel = wheel;
    }
    public void MoveToInsertion(Wheel wheel, int insertion)
    {
        var destination = WheelOrdering.Destination(Wheels.IndexOf(wheel), insertion, Wheels.Count);
        if (destination >= 0) MoveTo(wheel, destination);
    }
    private void UpdatePositions() { for (var i = 0; i < Wheels.Count; i++) Wheels[i].Position = i; }
    public void SetLevel(Wheel wheel, double level)
    {
        if (!wheel.Available) return;
        level = Math.Round(Math.Clamp(level, 0, 1), 2);
        foreach (var other in Wheels.Where(w => w.ChannelId == wheel.ChannelId && w.MixId == wheel.MixId)) other.Level = level;
        Client.QueueLevel(new(wheel.ChannelId, wheel.MixId), level);
    }
    public void ToggleMute(Wheel wheel)
    {
        if (!wheel.Available) return;
        var value = !wheel.Muted;
        foreach (var other in Wheels.Where(w => w.ChannelId == wheel.ChannelId && w.MixId == wheel.MixId)) { other.Muted = value; other.Changed(nameof(Wheel.VolumeLabel)); }
        Client.QueueMute(new(wheel.ChannelId, wheel.MixId), value);
    }
    public void Reconnect()
    {
        if (!string.IsNullOrWhiteSpace(PortText) && (!int.TryParse(PortText, out var port) || port is < 1024 or > 65535)) { Message = "Enter a port between 1024 and 65535, or leave it blank for automatic discovery."; return; }
        Preferences.PortOverride = int.TryParse(PortText, out var p) ? p : 0;
        ScheduleSave(); Client.Reconnect(Preferences.PortOverride); Message = "Checking the local Wave Link connection…";
    }
    public void Preview() => PreviewRequested?.Invoke();
    private void ScheduleSave() { SaveStatus = "Saving…"; saveTimer.Stop(); saveTimer.Start(); }
    public void Save()
    {
        saveTimer.Stop();
        try { Preferences.Wheels = Wheels.ToList(); store.Save(Preferences); SaveStatus = "All changes saved"; }
        catch (Exception ex) { SaveStatus = "Could not save changes"; Message = ex.Message; App.WriteLog("Settings: " + ex.Message); }
    }
    public void Dispose() { Save(); Client.Dispose(); }
}
