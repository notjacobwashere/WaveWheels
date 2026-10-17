using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Collections.Generic;
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
    public ObservableCollection<WheelProfile> Profiles { get; }
    public ObservableCollection<Choice> Monitors { get; } = new();
    public string[] Edges { get; } = new[]{"Left","Right","Top","Bottom"};
    private WheelProfile? activeProfile;
    private bool switchingProfile;
    private readonly ProfileAutomation automation=new();
    private readonly DispatcherTimer foregroundTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private string automaticProfileStatus="Automatic switching is off.";
    public string AutomaticProfileStatus {get=>automaticProfileStatus;private set=>Set(ref automaticProfileStatus,value);}
    public bool AutomaticProfilesEnabled {get=>Preferences.AutomaticProfilesEnabled;set {if(value==Preferences.AutomaticProfilesEnabled)return;Preferences.AutomaticProfilesEnabled=value;automation.Reset();Changed();AutomaticProfileStatus=value?"Waiting for a focused app…":"Automatic switching is off.";ScheduleSave();}}
    public int ScrollStepPercent {get=>Preferences.ScrollStepPercent;set {value=Math.Clamp(value,1,10);if(value==Preferences.ScrollStepPercent)return;Preferences.ScrollStepPercent=value;Changed();Changed(nameof(ScrollStepLabel));ScheduleSave();InputSettingsChanged?.Invoke();}}
    public string ScrollStepLabel=>ScrollStepPercent+"% per notch";
    public int ShiftStepPercent {get=>Preferences.ShiftStepPercent;set {value=Math.Clamp(value,5,50);if(value==Preferences.ShiftStepPercent)return;Preferences.ShiftStepPercent=value;Changed();Changed(nameof(ShiftStepLabel));ScheduleSave();InputSettingsChanged?.Invoke();}}
    public string ShiftStepLabel=>ShiftStepPercent+"% while holding Shift";
    public bool ShowShiftMarkers {get=>Preferences.ShowShiftMarkers;set {if(value==Preferences.ShowShiftMarkers)return;Preferences.ShowShiftMarkers=value;Changed();ScheduleSave();InputSettingsChanged?.Invoke();}}
    public bool ScrollAccelerationEnabled {get=>Preferences.ScrollAccelerationEnabled;set {if(value==Preferences.ScrollAccelerationEnabled)return;Preferences.ScrollAccelerationEnabled=value;Changed();ScheduleSave();InputSettingsChanged?.Invoke();}}
    public int IdleMilliseconds {get=>Preferences.IdleMilliseconds;set {value=Math.Clamp(value,500,5000);if(value==Preferences.IdleMilliseconds)return;Preferences.IdleMilliseconds=value;Changed();Changed(nameof(IdleLabel));ScheduleSave();}}
    public string IdleLabel=>(IdleMilliseconds/1000d).ToString("0.0")+" seconds";
    public event Action? InputSettingsChanged;
    public WheelProfile? ActiveProfile { get=>activeProfile; set { if(value!=null&&value!=activeProfile&&Profiles.Contains(value))SwitchProfile(value); } }
    public string ScreenEdge { get=>Preferences.ScreenEdge;set { if(!Edges.Contains(value)||value==Preferences.ScreenEdge)return;Preferences.ScreenEdge=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public string MonitorId { get=>Preferences.MonitorId;set { value??="";if(value==Preferences.MonitorId)return;Preferences.MonitorId=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public int PositionPercent { get=>Preferences.PositionPercent;set { value=Math.Clamp(value,0,100);if(value==Preferences.PositionPercent)return;Preferences.PositionPercent=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public int WheelSpacing { get=>Preferences.WheelSpacing;set { value=Math.Clamp(value,0,36);if(value==Preferences.WheelSpacing)return;Preferences.WheelSpacing=value;Changed();ScheduleSave();LayoutChanged?.Invoke(); } }
    private readonly CancellationTokenSource updateLifetime=new();
    private readonly DispatcherTimer updateTimer=new() { Interval=TimeSpan.FromHours(6) };
    private string updateStatus="Check GitHub for a newer version.",updateUrl=ReleaseUpdates.ReleasesUrl;
    private bool checkingUpdates;
    private ReleaseUpdate? availableUpdate;
    private bool installingUpdate;
    private double updateProgress;
    private CancellationTokenSource? installCancellation;
    public string AppVersion=>ReleaseUpdates.CurrentVersion;
    public string UpdateStatus { get=>updateStatus;private set=>Set(ref updateStatus,value); }
    public bool CheckingUpdates { get=>checkingUpdates;private set {if(Set(ref checkingUpdates,value))UpdateActionsChanged();} }
    public bool InstallingUpdate {get=>installingUpdate;private set {if(Set(ref installingUpdate,value))UpdateActionsChanged();}}
    public double UpdateProgress {get=>updateProgress;private set=>Set(ref updateProgress,value);}
    public bool CanCheckUpdates=>!CheckingUpdates&&!InstallingUpdate;
    public bool CanInstallUpdate=>!isolated&&!CheckingUpdates&&!InstallingUpdate&&availableUpdate?.Installer!=null;
    public event Action? UpdateInstallationStarted;
    private void UpdateActionsChanged() {Changed(nameof(CanCheckUpdates));Changed(nameof(CanInstallUpdate));}
    public bool CheckUpdatesAutomatically { get=>Preferences.CheckUpdatesAutomatically;set { Preferences.CheckUpdatesAutomatically=value;Changed();ScheduleSave(); } }
    public event Action<string>? UpdateAvailable;
    private string notifiedVersion="";
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
        notifiedVersion=Preferences.LastNotifiedUpdate;
        if(Preferences.Profiles.Count==0)Preferences.Profiles.Add(new WheelProfile { Wheels=Preferences.Wheels });
        Profiles=new(Preferences.Profiles);
        activeProfile=Profiles.FirstOrDefault(p=>p.Id==Preferences.ActiveProfileId)??Profiles[0];
        if(!Profiles.Any(p=>p.Id==Preferences.ManualProfileId))Preferences.ManualProfileId=activeProfile.Id;
        AutomaticProfileStatus=AutomaticProfilesEnabled?"Waiting for a focused app…":"Automatic switching is off.";
        foreach(var profile in Profiles)profile.PropertyChanged+=ProfileChanged;
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
        RefreshMonitors();
        updateTimer.Tick+=async(_,_)=> { if(CheckUpdatesAutomatically)await CheckForUpdatesAsync(); };
        foregroundTimer.Tick+=(_,_)=> {if(AutomaticProfilesEnabled)ApplyForegroundProcess(ProfileAutomation.ForegroundProcess(),Environment.TickCount64);};
    }
    public void Start() { Client.Start(Preferences.PortOverride);if(!isolated) {foregroundTimer.Start();updateTimer.Start();if(CheckUpdatesAutomatically)_=CheckForUpdatesAsync();} }
    public void RefreshMonitors()
    {
        var choices=System.Windows.Forms.Screen.AllScreens.Select((s,i)=>new Choice(s.DeviceName,$"Display {i+1}{(s.Primary?" · primary":"")} · {s.Bounds.Width}×{s.Bounds.Height}")).ToList();
        choices.Insert(0,new("","All displays"));
        if(MonitorId.Length>0&&!choices.Any(x=>x.Id==MonitorId))choices.Add(new(MonitorId,"Saved display · disconnected"));
        ReplaceChoices(Monitors,choices.ToArray());
    }
    private void ProfileChanged(object? sender,PropertyChangedEventArgs e) {if(e.PropertyName==nameof(WheelProfile.Applications))automation.Reset();ScheduleSave();}
    internal void ApplyForegroundProcess(string? process,long now)
    {
        if(!AutomaticProfilesEnabled)return;
        var target=automation.Evaluate(process,Profiles,Preferences.ManualProfileId,now);
        if(target==null)return;
        var profile=Profiles.FirstOrDefault(p=>p.Id==target);
        if(profile==null)return;
        if(profile!=activeProfile)SwitchProfile(profile,true);
        AutomaticProfileStatus=ProfileAutomation.Matches(profile.Applications,process??"")?$"Using {profile.Name} for {process}":$"Using your manual layout · {profile.Name}";
    }
    private void SwitchProfile(WheelProfile profile,bool automatic=false)
    {
        if(!automatic) {Preferences.ManualProfileId=profile.Id;automation.ManualSelection();AutomaticProfileStatus="Manual layout · "+profile.Name;}
        if(activeProfile!=null)activeProfile.Wheels=Wheels.ToList();
        switchingProfile=true;
        foreach(var w in Wheels)w.PropertyChanged-=WheelChanged;
        Wheels.Clear();foreach(var w in profile.Wheels) {Wheels.Add(w);w.PropertyChanged+=WheelChanged;}
        switchingProfile=false;activeProfile=profile;Preferences.ActiveProfileId=profile.Id;
        SelectedWheel=Wheels.FirstOrDefault();UpdatePositions();Update(snapshot);
        Changed(nameof(ActiveProfile));Changed(nameof(WheelCount));ScheduleSave();LayoutChanged?.Invoke();
    }
    public void AddProfile(bool duplicate)
    {
        if(Profiles.Count>=20) {Message="You can keep up to 20 profiles.";return;}
        var profile=new WheelProfile { Name=duplicate?(ActiveProfile?.Name??"Profile")+" copy":"Profile "+(Profiles.Count+1),Wheels=duplicate?Wheels.Select(ProfileTransfer.CloneWheel).ToList():new(){new Wheel {Name="New wheel"}} };
        profile.PropertyChanged+=ProfileChanged;Profiles.Add(profile);ActiveProfile=profile;
    }
    public void RemoveProfile()
    {
        if(Profiles.Count<2||activeProfile==null) {Message="Keep at least one profile.";return;}
        var old=activeProfile;ActiveProfile=Profiles.First(p=>p!=old);old.PropertyChanged-=ProfileChanged;Profiles.Remove(old);ScheduleSave();
    }
    public void ExportProfiles(string path,bool all)
    {
        if(activeProfile!=null)activeProfile.Wheels=Wheels.ToList();
        File.WriteAllText(path,ProfileTransfer.Serialize(all?Profiles:Profiles.Where(p=>p==activeProfile)));
        Message=all?"Exported all profiles.":"Exported "+activeProfile?.Name+".";
    }
    public void ImportProfiles(string path)
    {
        if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Profile files must be smaller than 2 MB.");
        var imported=ProfileTransfer.Parse(File.ReadAllText(path));
        if(Profiles.Count+imported.Count>20)throw new InvalidDataException("Import would exceed 20 profiles. No profiles were imported.");
        var names=new HashSet<string>(Profiles.Select(p=>p.Name),StringComparer.OrdinalIgnoreCase);
        foreach(var profile in imported)
        {
            var original=profile.Name;var number=2;
            while(!names.Add(profile.Name))profile.Name=original[..Math.Min(original.Length,34)]+$" ({number++})";
            profile.PropertyChanged+=ProfileChanged;Profiles.Add(profile);
        }
        ActiveProfile=imported[0];ScheduleSave();Message=$"Imported {imported.Count} profile(s). Check the channel bindings on this PC.";
    }
    public async Task CheckForUpdatesAsync()
    {
        if(!CanCheckUpdates)return;CheckingUpdates=true;UpdateStatus="Checking GitHub…";
        try { var update=await ReleaseUpdates.CheckAsync(updateLifetime.Token);ApplyRelease(update);if(update!=null&&notifiedVersion!=update.Version) {notifiedVersion=update.Version;Preferences.LastNotifiedUpdate=update.Version;ScheduleSave();UpdateAvailable?.Invoke(update.Version);} }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or FormatException or System.Collections.Generic.KeyNotFoundException) {UpdateStatus="Couldn't check GitHub. Try again later.";}
        finally {CheckingUpdates=false;}
    }
    internal void ApplyRelease(ReleaseUpdate? update)
    {
        availableUpdate=update;updateUrl=update?.Url??ReleaseUpdates.ReleasesUrl;
        UpdateStatus=update==null?$"You're up to date · {AppVersion}":$"Version {update.Version} is available"+(update.Installer==null?" · use the release page to download":"");
        UpdateActionsChanged();
    }
    public Task InstallUpdateAsync()=>InstallUpdateCoreAsync((update,progress,token)=>UpdateInstaller.DownloadAsync(update,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WaveWheels","updates"),progress,token),UpdateInstaller.Launch);
    internal async Task InstallUpdateCoreAsync(Func<ReleaseUpdate,IProgress<DownloadProgress>,CancellationToken,Task<string>> download,Action<string> launch)
    {
        if(!CanInstallUpdate||availableUpdate==null)return;
        InstallingUpdate=true;UpdateProgress=0;UpdateStatus="Downloading the Windows installer…";
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);installCancellation=cancellation;
        var downloading=true;
        try
        {
            var progress=new Progress<DownloadProgress>(value=> {if(downloading&&!cancellation.IsCancellationRequested&&InstallingUpdate) {UpdateProgress=value.Percent;UpdateStatus=$"Downloading update · {value.Percent:0}%";}});
            var installer=await download(availableUpdate,progress,cancellation.Token);
            downloading=false;UpdateProgress=100;
            cancellation.Token.ThrowIfCancellationRequested();
            Save();if(SaveStatus!="All changes saved")throw new IOException("Your settings could not be saved. Setup was not started.");
            UpdateStatus="Installer verified. Opening Setup…";
            launch(installer);
            // Exit only after Windows accepts the verified installer. Failed/canceled downloads leave us running.
            UpdateInstallationStarted?.Invoke();
        }
        catch(OperationCanceledException) {UpdateStatus="Update canceled. Your current version is still running.";}
        catch(Exception ex) when(ex is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        {UpdateStatus="Update failed: "+ex.Message+" You can retry or open the release page.";}
        finally {downloading=false;installCancellation=null;InstallingUpdate=false;}
    }
    public void CancelUpdate()=>installCancellation?.Cancel();
    public void OpenReleasePage() {try {Process.Start(new ProcessStartInfo(updateUrl) {UseShellExecute=true});}catch(Exception ex){Message="Could not open the browser: "+ex.Message;} }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if(switchingProfile)return;
        if (e.OldItems != null) foreach (Wheel w in e.OldItems) w.PropertyChanged -= WheelChanged;
        if (e.NewItems != null) foreach (Wheel w in e.NewItems) w.PropertyChanged += WheelChanged;
        UpdatePositions(); Changed(nameof(WheelCount)); ScheduleSave(); Update(snapshot); LayoutChanged?.Invoke();
    }
    private void WheelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Wheel.Name) or nameof(Wheel.Color) or nameof(Wheel.ChannelId) or nameof(Wheel.MixId) or nameof(Wheel.MaximumVolumePercent))) return;
        if(e.PropertyName==nameof(Wheel.MaximumVolumePercent)) {ScheduleSave();StateChanged?.Invoke();return;}
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
        choices.Insert(0, new("", "None"));
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
        if (!Wheels.Contains(wheel)||!wheel.Available||!double.IsFinite(level)) return;
        var cap=Wheels.Where(w=>w.ChannelId==wheel.ChannelId&&w.MixId==wheel.MixId).Select(w=>w.MaximumVolumePercent).DefaultIfEmpty(wheel.MaximumVolumePercent).Min()/100d;
        level = Math.Round(Math.Clamp(level, 0, cap), 2);
        foreach (var other in Wheels.Where(w => w.ChannelId == wheel.ChannelId && w.MixId == wheel.MixId)) other.Level = level;
        Client.QueueLevel(new(wheel.ChannelId, wheel.MixId), level);
    }
    public void ToggleMute(Wheel wheel)
    {
        if (!Wheels.Contains(wheel)||!wheel.Available) return;
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
        try { Preferences.Wheels = Wheels.ToList();if(activeProfile!=null) {activeProfile.Wheels=Preferences.Wheels;Preferences.ActiveProfileId=activeProfile.Id;}Preferences.Profiles=Profiles.ToList(); store.Save(Preferences); SaveStatus = "All changes saved"; }
        catch (Exception ex) { SaveStatus = "Could not save changes"; Message = ex.Message; App.WriteLog("Settings: " + ex.Message); }
    }
    public void Dispose() { foregroundTimer.Stop();updateTimer.Stop();updateLifetime.Cancel();Save(); Client.Dispose(); }
}
