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
    public EffectSettings Effects=>Preferences.Effects;
    private readonly IWindowsMasterAudio windowsAudio;
    private readonly bool canWriteWindowsAudio;
    private readonly DispatcherTimer windowsAudioTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    public Wheel WindowsWheel=>Preferences.WindowsWheel;
    public IEnumerable<Wheel> DisplayWheels=>Connected?Wheels:new[]{WindowsWheel};
    public int DisplayWheelCount {get=>Connected?Wheels.Count:1;set {if(Connected)WheelCount=value;}}
    public Wheel? EditorWheel {get=>Connected?SelectedWheel:WindowsWheel;set {if(Connected)SelectedWheel=value;}}
    public string AudioModeTitle=>Connected?"WAVE LINK CONNECTED":"WINDOWS AUDIO MODE";
    public string AudioModeDescription=>Connected?"Wave Link channels are active. Windows Audio is used automatically if the connection is lost.":"Wave Link is disconnected. Windows Audio controls the default output. Connect Wave Link to restore your saved channel wheels.";
    public string ModeAccent=>Connected?"#8ED7BB":"#F4B16F";
    public string WindowsAudioStatus=>Connected?"Wave Link controls are active. Your Windows wheel is saved for automatic fallback.":WindowsWheel.Detail;
    public string WheelArrangementHint=>Connected?"Drag the handle · Alt + ↑ / ↓ to move selection":"One Windows volume wheel · connect Wave Link to add more";
    internal void RefreshWindowsAudio() {if(!Connected)ApplyWindowsAudio(windowsAudio.Read());}
    private void ApplyWindowsAudio(WindowsAudioState state)
    {
        WindowsWheel.Available=state.Available;WindowsWheel.Detail=state.Status;
        if(state.Available) {WindowsWheel.Level=state.Level;WindowsWheel.Muted=state.Muted;}
        Changed(nameof(WindowsAudioStatus));StateChanged?.Invoke();
    }
    internal void StartWaveLink(WaveLinkInstallation installation)
    {
        if(isolated){Message="Launching other apps is disabled in an isolated test profile.";return;}
        try {WaveLinkLauncher.Start(installation);if(!installation.Packaged)Preferences.WaveLinkExecutable=installation.Target;ScheduleSave();Client.Reconnect(Preferences.PortOverride);Message="Wave Link is starting. Your saved wheels will return automatically when it connects.";}
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) {Message="Could not start Wave Link: "+ex.Message;}
    }

    public string EffectColorStatus=>EffectSettings.ValidColor(Effects.NormalColor)&&EffectSettings.ValidColor(Effects.MutedColor)&&EffectSettings.ValidColor(Effects.MutedButtonColor)?"Use #RRGGBB hex colors.":"Enter a valid #RRGGBB color. Invalid values use the default.";
    public void RestoreDefaults(string page)
    {
        var d=new Preferences();
        switch(page)
        {
            case "Effects":Effects.Reset();break;
            case "Placement":ScreenEdge=d.ScreenEdge;MonitorId=d.MonitorId;PositionPercent=d.PositionPercent;WheelSpacing=d.WheelSpacing;EdgeDistance=d.EdgeDistance;break;
            case "Settings":WheelSizePercent=d.WheelSizePercent;AnimateWheels=d.AnimateWheels;ScrollStepPercent=d.ScrollStepPercent;ShiftStepPercent=d.ShiftStepPercent;ShowShiftMarkers=d.ShowShiftMarkers;UseMuteButton=d.UseMuteButton;ScrollAccelerationEnabled=d.ScrollAccelerationEnabled;IdleMilliseconds=d.IdleMilliseconds;ApplicationBlacklistEnabled=d.ApplicationBlacklistEnabled;BlacklistedApplications.Clear();RefreshInteractionPolicy();break;
            case "Wheels":if(!Connected) {WindowsWheel.Name="Windows Audio";WindowsWheel.Color="#63CEA4";WindowsWheel.MaximumVolumePercent=100;}else if(SelectedWheel!=null) {var index=Wheels.IndexOf(SelectedWheel);var w=d.Wheels[Math.Max(0,index)%d.Wheels.Count];SelectedWheel.Name=index<3?w.Name:$"Wheel {index+1}";SelectedWheel.Color=w.Color;SelectedWheel.MaximumVolumePercent=100;}break;
            case "Profiles":AutomaticProfilesEnabled=false;if(ActiveProfile!=null)ActiveProfile.Applications="";break;
            case "Startup":StartupEnabled=false;break;
            case "Connection":PortText="";Reconnect();break;
            case "Updates":CheckUpdatesAutomatically=true;UpdateChannel="Stable";break;
        }
        ScheduleSave();
    }
    public WaveLinkClient Client { get; } = new();
    public ObservableCollection<Wheel> Wheels { get; }
    public ObservableCollection<WheelProfile> Profiles { get; }
    public ObservableCollection<string> BlacklistedApplications {get;}
    private ForegroundFocusMonitor? foregroundMonitor;
    private string? focusedExecutable;
    private bool isApplicationBlocked;
    private string newBlacklistedApplication="";
    public string NewBlacklistedApplication {get=>newBlacklistedApplication;set=>Set(ref newBlacklistedApplication,value);}
    public string? SelectedBlacklistedApplication {get;set;}
    public bool IsApplicationBlocked {get=>isApplicationBlocked;private set {if(Set(ref isApplicationBlocked,value))ApplicationBlockChanged?.Invoke();}}
    public bool ApplicationBlacklistEnabled {get=>Preferences.ApplicationBlacklistEnabled;set {if(value==Preferences.ApplicationBlacklistEnabled)return;Preferences.ApplicationBlacklistEnabled=value;Changed();ScheduleSave();RefreshInteractionPolicy();}}
    public string BlacklistStatus=>!ApplicationBlacklistEnabled?"Blacklist is off. Wheels work in all apps.":IsApplicationBlocked?"Wheels paused for "+focusedExecutable:BlacklistedApplications.Count==0?"No apps blocked. Add an executable name below.":"Blacklist is on. Wheels are available in the current app.";
    public event Action? ApplicationBlockChanged;
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
    public bool UseMuteButton {get=>Preferences.UseMuteButton;set {if(value==Preferences.UseMuteButton)return;Preferences.UseMuteButton=value;Changed();ScheduleSave();InputSettingsChanged?.Invoke();}}
    public bool ScrollAccelerationEnabled {get=>Preferences.ScrollAccelerationEnabled;set {if(value==Preferences.ScrollAccelerationEnabled)return;Preferences.ScrollAccelerationEnabled=value;Changed();ScheduleSave();InputSettingsChanged?.Invoke();}}
    public int IdleMilliseconds {get=>Preferences.IdleMilliseconds;set {value=Math.Clamp(value,500,5000);if(value==Preferences.IdleMilliseconds)return;Preferences.IdleMilliseconds=value;Changed();Changed(nameof(IdleLabel));ScheduleSave();}}
    public string IdleLabel=>(IdleMilliseconds/1000d).ToString("0.0")+" seconds";
    public event Action? InputSettingsChanged;
    public WheelProfile? ActiveProfile { get=>activeProfile; set { if(value!=null&&value!=activeProfile&&Profiles.Contains(value))SwitchProfile(value); } }
    public string ScreenEdge { get=>Preferences.ScreenEdge;set { if(!Edges.Contains(value)||value==Preferences.ScreenEdge)return;Preferences.ScreenEdge=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public string MonitorId { get=>Preferences.MonitorId;set {if(value==null||value==Preferences.MonitorId)return;Preferences.MonitorId=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public int PositionPercent { get=>Preferences.PositionPercent;set { value=Math.Clamp(value,0,100);if(value==Preferences.PositionPercent)return;Preferences.PositionPercent=value;Changed();ScheduleSave();AppearanceChanged?.Invoke(); } }
    public int WheelSpacing { get=>Preferences.WheelSpacing;set { value=Math.Clamp(value,0,36);if(value==Preferences.WheelSpacing)return;Preferences.WheelSpacing=value;Changed();ScheduleSave();LayoutChanged?.Invoke(); } }
    public int EdgeDistance {get=>Preferences.EdgeDistance;set {value=Math.Clamp(value,0,200);if(value==Preferences.EdgeDistance)return;Preferences.EdgeDistance=value;Changed();ScheduleSave();AppearanceChanged?.Invoke();}}
    private bool isPreviewing;
    public bool IsPreviewing=>isPreviewing;
    public string PreviewButtonLabel=>IsPreviewing?"■  Stop preview":"↗  Preview wheels";
    internal void SetPreviewActive(bool value) {if(Set(ref isPreviewing,value,nameof(IsPreviewing)))Changed(nameof(PreviewButtonLabel));}
    private readonly CancellationTokenSource updateLifetime=new();
    private readonly DispatcherTimer updateTimer=new() { Interval=TimeSpan.FromHours(6) };
    private string updateStatus="Check GitHub for a newer version.",updateUrl=ReleaseUpdates.ReleasesUrl;
    private bool checkingUpdates;
    private ReleaseUpdate? availableUpdate;
    private bool installingUpdate;
    private double updateProgress;
    private CancellationTokenSource? installCancellation;
    public string AppVersion=>ReleaseUpdates.CurrentVersion;
    public string[] UpdateChannels {get;}={"Stable","Beta"};
    public string UpdateChannel
    {
        get=>Preferences.UpdateChannel;
        set {if(!CanCheckUpdates||!UpdateChannels.Contains(value)||value==Preferences.UpdateChannel)return;Preferences.UpdateChannel=value;Changed();Changed(nameof(BetaChannelEnabled));ScheduleSave();ApplyRelease(null);UpdateStatus="Channel changed. Checking GitHub…";_=CheckForUpdatesAsync();}
    }
    public bool BetaChannelEnabled=>UpdateChannel=="Beta";
    private ReleaseUpdate? stableRelease;
    public string StableReleaseLabel=>stableRelease==null?"Check for updates to find the latest stable release.":$"Latest stable · {stableRelease.Version}";
    public bool CanInstallStable=>!isolated&&CanCheckUpdates&&stableRelease?.Installer!=null&&stableRelease.Version!=AppVersion;
    public Task InstallStableAsync()=>InstallStableCoreAsync(DownloadUpdate,UpdateInstaller.Launch);
    internal Task InstallStableCoreAsync(Func<ReleaseUpdate,IProgress<DownloadProgress>,CancellationToken,Task<string>> download,Action<string> launch)
    {
        if(!CanInstallStable)return Task.CompletedTask;
        Preferences.UpdateChannel="Stable";Changed(nameof(UpdateChannel));Changed(nameof(BetaChannelEnabled));ScheduleSave();
        ApplyRelease(stableRelease);return InstallUpdateCoreAsync(download,launch);
    }
    public ObservableCollection<ReleaseUpdate> BetaBuilds {get;}=new();
    private ReleaseUpdate? selectedBetaBuild;
    public ReleaseUpdate? SelectedBetaBuild {get=>selectedBetaBuild;set {if(Set(ref selectedBetaBuild,value))UpdateActionsChanged();}}
    public bool CanInstallBeta=>!isolated&&CanCheckUpdates&&BetaChannelEnabled&&SelectedBetaBuild?.Installer!=null;
    public async Task InstallBetaAsync()
    {
        if(!CanInstallBeta||SelectedBetaBuild==null)return;
        ApplyRelease(SelectedBetaBuild);await InstallUpdateAsync();
    }
    public void OpenBetaReleasePage() {try {if(SelectedBetaBuild!=null)Process.Start(new ProcessStartInfo(SelectedBetaBuild.Url){UseShellExecute=true});}catch(Exception ex){Message="Could not open the browser: "+ex.Message;}}
    public string UpdateStatus { get=>updateStatus;private set=>Set(ref updateStatus,value); }
    public bool CheckingUpdates { get=>checkingUpdates;private set {if(Set(ref checkingUpdates,value))UpdateActionsChanged();} }
    public bool InstallingUpdate {get=>installingUpdate;private set {if(Set(ref installingUpdate,value))UpdateActionsChanged();}}
    public double UpdateProgress {get=>updateProgress;private set=>Set(ref updateProgress,value);}
    public bool CanCheckUpdates=>!CheckingUpdates&&!InstallingUpdate;
    public bool CanInstallUpdate=>!isolated&&!CheckingUpdates&&!InstallingUpdate&&availableUpdate?.Installer!=null;
    public event Action? UpdateInstallationStarted;
    private void UpdateActionsChanged() {Changed(nameof(CanCheckUpdates));Changed(nameof(CanInstallUpdate));Changed(nameof(CanInstallBeta));Changed(nameof(CanInstallStable));}
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
    public string ConnectionLabel => snapshot.Connected ? "WAVE LINK" : "WINDOWS AUDIO";
    public bool Connected => snapshot.Connected;
    public string ChannelCount => snapshot.Connected ? $"{snapshot.Channels.Count} input channels available" : "1 Windows volume wheel active · Wave Link layouts are saved";
    public string SettingsPath => store.FilePath;
    public bool Isolated => isolated;
    public string PortText { get => portText; set => Set(ref portText, value); }
    public bool StartupEnabled
    {
        get { try { return !isolated && startup.IsRegistered; } catch { return false; } }
        set
        {
            try { if (isolated) throw new InvalidOperationException("Startup changes are disabled in the isolated test profile."); startup.SetEnabled(value); Message = value ? "Launch at Windows sign-in enabled." : "Sign-in startup is off."; }
            catch (Exception ex) { Message = "Could not update startup: " + ex.Message; }
            Changed(); Changed(nameof(StartupDescription));
        }
    }
    public string StartupDescription { get { try { return isolated ? "Isolated test profile · startup registration disabled" : startup.Description; } catch { return "Windows startup registration is unavailable."; } } }
    public int WheelSizePercent
    {
        get => Preferences.WheelSizePercent;
        set { value = Math.Clamp(value, 50, 180); if (value == Preferences.WheelSizePercent) return; Preferences.WheelSizePercent = value; Changed(); Changed(nameof(WheelSizeLabel)); Changed(nameof(PreviewWidth));Changed(nameof(PreviewHeight)); ScheduleSave(); AppearanceChanged?.Invoke(); }
    }
    public string WheelSizeLabel => WheelSizePercent + "%";
    public double PreviewCanvasWidth
    {
        get
        {
            var name=string.IsNullOrWhiteSpace(EditorWheel?.Name)?"UNTITLED":EditorWheel.Name.ToUpperInvariant();
            var text=new System.Windows.Media.FormattedText(name,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),12,System.Windows.Media.Brushes.White,1){MaxTextWidth=188,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis};
            return Math.Max(120,60+Math.Min(232,Math.Max(64,text.Width+44)))+OverlaySizing.EffectPadding*2;
        }
    }
    public double PreviewWidth => PreviewCanvasWidth*WheelSizePercent/100d;
    private void RefreshPreviewSize() {Changed(nameof(PreviewCanvasWidth));Changed(nameof(PreviewWidth));}
    public double PreviewHeight=>Math.Clamp(216*WheelSizePercent/100d,150,248);
    public bool AnimateWheels
    {
        get => Preferences.AnimateWheels;
        set { if (value == Preferences.AnimateWheels) return; Preferences.AnimateWheels = value; Changed(); ScheduleSave(); AppearanceChanged?.Invoke(); }
    }
    public int WheelCount { get => Wheels.Count; set { if(!Connected)return;value = Math.Clamp(value, 1, 12); while (Wheels.Count < value) AddWheel(); while (Wheels.Count > value) RemoveWheel(Wheels[^1]); } }
    public Wheel? SelectedWheel
    {
        get => selected;
        set { if (Set(ref selected, value)) { Changed(nameof(EditorWheel));RefreshPreviewSize();UpdateMixChoices(); SelectionChanged?.Invoke(); } }
    }
    public event Action? LayoutChanged;
    public event Action? AppearanceChanged;
    public event Action? StateChanged;
    public event Action? SelectionChanged;
    public event Action? PreviewRequested;

    public MainViewModel(SettingsStore store, bool isolated):this(store,isolated,null) { }
    internal MainViewModel(SettingsStore store,bool isolated,IWindowsMasterAudio? audio)
    {
        windowsAudio=audio??new WindowsMasterAudio();canWriteWindowsAudio=!isolated||audio!=null;
        this.store = store; this.isolated = isolated;
        Preferences = store.Load();
        Effects.PropertyChanged+=(_,_)=> {ScheduleSave();Changed(nameof(EffectColorStatus));AppearanceChanged?.Invoke();};
        BlacklistedApplications=new(Preferences.BlacklistedApplications);
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
        BlacklistedApplications.CollectionChanged+=(_,_)=> {ScheduleSave();RefreshInteractionPolicy();};
        Client.Updated += data => dispatcher.BeginInvoke(() => Update(data));
        Client.Log = line => App.WriteLog(line);
        SelectedWheel = Wheels.FirstOrDefault();
        Message = store.Warning ?? "Scroll at the selected screen edge to open audio controls.";
        WindowsWheel.Detail="Waiting for the Windows playback device…";
        WindowsWheel.PropertyChanged+=WindowsWheelChanged;
        windowsAudioTimer.Tick+=(_,_)=>RefreshWindowsAudio();
        Update(snapshot);
        RefreshMonitors();
        updateTimer.Tick+=async(_,_)=> { if(CheckUpdatesAutomatically)await CheckForUpdatesAsync(); };
        foregroundTimer.Tick+=(_,_)=>RefreshForeground();
    }
    public void Start() {if(!isolated) {RefreshWindowsAudio();windowsAudioTimer.Start();} Client.Start(Preferences.PortOverride);if(!isolated) {foregroundMonitor=new(()=>dispatcher.BeginInvoke(RefreshForeground));RefreshForeground();foregroundTimer.Start();updateTimer.Start();if(CheckUpdatesAutomatically)_=CheckForUpdatesAsync();} }
    private void RefreshForeground()
    {
        if(AutomaticProfilesEnabled||ApplicationBlacklistEnabled&&BlacklistedApplications.Count>0)ApplyForegroundProcess(ProfileAutomation.ForegroundProcess(),Environment.TickCount64);
        else UpdateBlacklist(null);
    }
    private void UpdateBlacklist(string? process)
    {
        focusedExecutable=process;
        IsApplicationBlocked=ApplicationBlacklistEnabled&&ApplicationBlacklist.Matches(BlacklistedApplications,process);
        Changed(nameof(BlacklistStatus));
    }
    internal bool RefreshInteractionPolicy()
    {
        UpdateBlacklist(!isolated&&ApplicationBlacklistEnabled&&BlacklistedApplications.Count>0?ProfileAutomation.ForegroundProcess():focusedExecutable);
        return !IsApplicationBlocked;
    }
    public void AddBlacklistedApplication(string? executable=null)
    {
        var name=ApplicationBlacklist.Normalize(executable??NewBlacklistedApplication);
        if(name==null) {Message="Enter an executable name, for example game.exe. Use Browse for a full path.";return;}
        if(BlacklistedApplications.Contains(name)) {Message="That executable is already in the blacklist.";return;}
        if(BlacklistedApplications.Count>=50) {Message="You can blacklist up to 50 applications.";return;}
        BlacklistedApplications.Add(name);NewBlacklistedApplication="";Message="Added "+name+" to the blacklist.";
    }
    public void RemoveBlacklistedApplication() {if(SelectedBlacklistedApplication is string name)BlacklistedApplications.Remove(name);}
    public void RefreshMonitors()
    {
        ApplyMonitorChoices(System.Windows.Forms.Screen.AllScreens.Select(MonitorDescriptor.Capture).ToArray());
    }
    internal void ApplyMonitorChoices(IReadOnlyList<MonitorDescriptor> monitors)
    {
        var selected=MonitorSelection.Resolve(MonitorId,monitors);
        var choices=monitors.Select(m=>new Choice(m.Id,m.Label)).DistinctBy(c=>c.Id,StringComparer.OrdinalIgnoreCase).ToList();
        choices.Insert(0,new("","All displays"));
        if(selected.Length>0&&!choices.Any(x=>string.Equals(x.Id,selected,StringComparison.OrdinalIgnoreCase)))choices.Add(new(selected,"Saved display · disconnected"));
        ReplaceChoices(Monitors,choices.ToArray());
        if(selected!=MonitorId)
        {
            // Identity migration does not change geometry; refresh callers already update placement/hit data.
            Preferences.MonitorId=selected;Changed(nameof(MonitorId));ScheduleSave();
        }
    }
    private void ProfileChanged(object? sender,PropertyChangedEventArgs e) {if(e.PropertyName==nameof(WheelProfile.Applications))automation.Reset();ScheduleSave();}
    internal void ApplyForegroundProcess(string? process,long now)
    {
        UpdateBlacklist(process);
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
        try { var catalog=await ReleaseUpdates.CheckAsync(BetaChannelEnabled,updateLifetime.Token);ApplyCatalog(catalog);var update=catalog.Latest;if(update!=null&&notifiedVersion!=update.Version) {notifiedVersion=update.Version;Preferences.LastNotifiedUpdate=update.Version;ScheduleSave();UpdateAvailable?.Invoke(update.Version);} }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or FormatException or System.Collections.Generic.KeyNotFoundException) {UpdateStatus="Couldn't check GitHub. Try again later.";}
        finally {CheckingUpdates=false;}
    }
    internal void ApplyRelease(ReleaseUpdate? update)
    {
        availableUpdate=update;updateUrl=update?.Url??ReleaseUpdates.ReleasesUrl;
        UpdateStatus=update==null?$"You're up to date · {AppVersion}":$"Version {update.Version} is available"+(update.Installer==null?" · use the release page to download":"");
        UpdateActionsChanged();
    }
    internal void ApplyCatalog(ReleaseCatalog catalog)
    {
        stableRelease=catalog.Stable;Changed(nameof(StableReleaseLabel));
        SelectedBetaBuild=null;BetaBuilds.Clear();foreach(var build in catalog.Betas)BetaBuilds.Add(build);SelectedBetaBuild=BetaBuilds.FirstOrDefault();ApplyRelease(catalog.Latest);
    }
    private static Task<string> DownloadUpdate(ReleaseUpdate update,IProgress<DownloadProgress> progress,CancellationToken token)=>UpdateInstaller.DownloadAsync(update,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WaveWheels","updates"),progress,token);
    public Task InstallUpdateAsync()=>InstallUpdateCoreAsync(DownloadUpdate,UpdateInstaller.Launch);
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
        UpdatePositions(); Changed(nameof(WheelCount));Changed(nameof(DisplayWheelCount)); ScheduleSave(); Update(snapshot); LayoutChanged?.Invoke();
    }
    private void WindowsWheelChanged(object? sender,PropertyChangedEventArgs e) {if(e.PropertyName is nameof(Wheel.Name) or nameof(Wheel.Color) or nameof(Wheel.MaximumVolumePercent))ScheduleSave();if(e.PropertyName==nameof(Wheel.Name))RefreshPreviewSize();}
    private void WheelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(Wheel.Name))RefreshPreviewSize();
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
        var modeChanged=snapshot.Connected!=data.Connected;
        snapshot = data;
        var choices = data.Channels.Select(c => new Choice(c.Id, c.Name)).ToList();
        choices.Insert(0, new("", "None"));
        foreach (var w in Wheels.Where(w => w.ChannelId.Length > 0)) if (!choices.Any(c => c.Id == w.ChannelId)) choices.Add(new(w.ChannelId, "Saved channel · currently unavailable"));
        ReplaceChoices(Channels, choices.ToArray());
        UpdateMixChoices(); SyncWheels();
        Changed(nameof(Status)); Changed(nameof(Connected)); Changed(nameof(ConnectionLabel)); Changed(nameof(ChannelCount));
        Changed(nameof(DisplayWheels));Changed(nameof(DisplayWheelCount));Changed(nameof(EditorWheel));RefreshPreviewSize();Changed(nameof(AudioModeTitle));Changed(nameof(AudioModeDescription));Changed(nameof(ModeAccent));Changed(nameof(WindowsAudioStatus));Changed(nameof(WheelArrangementHint));
        if(modeChanged) {if(!Connected&&!isolated)RefreshWindowsAudio();SelectionChanged?.Invoke();LayoutChanged?.Invoke();Message=Connected?"Wave Link reconnected. Your saved Wave Link wheels are active.":"Wave Link disconnected. Switched to your saved Windows audio wheel.";}
        StateChanged?.Invoke();
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
        if (!Connected||Wheels.Count >= 12) return;
        var colors = new[] { "#36BBD3", "#DA5DAD", "#A99AFF", "#63CEA4", "#F4B16F" };
        var w = new Wheel { Name = "Wheel " + (Wheels.Count + 1), Color = colors[Wheels.Count % colors.Length] };
        Wheels.Add(w); SelectedWheel = w;
    }
    public void RemoveWheel(Wheel? wheel = null)
    {
        if(!Connected)return;
        wheel ??= SelectedWheel;
        if (wheel == null || Wheels.Count <= 1) { Message = "Keep at least one wheel in your layout."; return; }
        var i = Wheels.IndexOf(wheel); Wheels.Remove(wheel); SelectedWheel = Wheels[Math.Min(i, Wheels.Count - 1)];
    }
    public void Move(int delta) { if (SelectedWheel != null) MoveTo(SelectedWheel, Wheels.IndexOf(SelectedWheel) + delta); }
    public void MoveTo(Wheel wheel, int index)
    {
        if(!Connected)return;
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
        if(ReferenceEquals(wheel,WindowsWheel))
        {
            if(Connected||!canWriteWindowsAudio||!wheel.Available||!double.IsFinite(level)||!RefreshInteractionPolicy())return;
            ApplyWindowsAudio(windowsAudio.SetLevel(Math.Clamp(level,0,wheel.MaximumVolumePercent/100d)));return;
        }
        if(!Connected)return;
        if (!Wheels.Contains(wheel)||!wheel.Available||!double.IsFinite(level)||!RefreshInteractionPolicy()) return;
        var cap=Wheels.Where(w=>w.ChannelId==wheel.ChannelId&&w.MixId==wheel.MixId).Select(w=>w.MaximumVolumePercent).DefaultIfEmpty(wheel.MaximumVolumePercent).Min()/100d;
        level = Math.Round(Math.Clamp(level, 0, cap), 2);
        foreach (var other in Wheels.Where(w => w.ChannelId == wheel.ChannelId && w.MixId == wheel.MixId)) other.Level = level;
        Client.QueueLevel(new(wheel.ChannelId, wheel.MixId), level);
    }
    public void ToggleMute(Wheel wheel)
    {
        if(ReferenceEquals(wheel,WindowsWheel))
        {
            if(!Connected&&canWriteWindowsAudio&&wheel.Available&&RefreshInteractionPolicy())ApplyWindowsAudio(windowsAudio.SetMuted(!wheel.Muted));return;
        }
        if(!Connected)return;
        if (!Wheels.Contains(wheel)||!wheel.Available||!RefreshInteractionPolicy()) return;
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
    public void Preview() {if(IsPreviewing||RefreshInteractionPolicy()) {SetPreviewActive(!IsPreviewing);PreviewRequested?.Invoke();}}
    private void ScheduleSave() { SaveStatus = "Saving…"; saveTimer.Stop(); saveTimer.Start(); }
    public void Save()
    {
        saveTimer.Stop();
        try {Preferences.BlacklistedApplications=BlacklistedApplications.ToList(); Preferences.Wheels = Wheels.ToList();if(activeProfile!=null) {activeProfile.Wheels=Preferences.Wheels;Preferences.ActiveProfileId=activeProfile.Id;}Preferences.Profiles=Profiles.ToList(); store.Save(Preferences); SaveStatus = "All changes saved"; }
        catch (Exception ex) { SaveStatus = "Could not save changes"; Message = ex.Message; App.WriteLog("Settings: " + ex.Message); }
    }
    public void Dispose() {windowsAudioTimer.Stop();WindowsWheel.PropertyChanged-=WindowsWheelChanged;foregroundMonitor?.Dispose(); foregroundTimer.Stop();updateTimer.Stop();updateLifetime.Cancel();Save(); Client.Dispose(); }
}
