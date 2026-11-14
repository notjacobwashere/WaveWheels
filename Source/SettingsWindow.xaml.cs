using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace WaveWheels;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel model;
    private Point start;
    private Wheel? dragWheel;
    private Wheel? dragging;
    private DropMarker? dropMarker;
    private AdornerLayer? dropLayer;
    private int insertion;
    private long lastAutoScroll;
    private bool editorRefreshQueued;
    private string currentSection="Wheels";
    private readonly Dictionary<FrameworkElement,SurfaceMotion> pageMotion=new();
    private SurfaceMotion? modeMotion;
    private bool placementReady;
    private bool savedDisplayMissing;
    private string? pendingMonitor,pendingSetupMonitor;
    public SettingsWindow(MainViewModel model)
    {
        this.model = model;
        InitializeComponent();
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.K&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){OpenSettingsSearch(this,new RoutedEventArgs());e.Handled=true;}};
        LeftClickOutputBox.SelectionChanged+=(_,_)=>{if(LeftClickOutputBox.IsDropDownOpen||LeftClickOutputBox.IsKeyboardFocusWithin)CommitClickOutput(false);};
        RightClickOutputBox.SelectionChanged+=(_,_)=>{if(RightClickOutputBox.IsDropDownOpen||RightClickOutputBox.IsKeyboardFocusWithin)CommitClickOutput(true);};
        MonitorBox.DropDownOpened+=(_,_)=>pendingMonitor=null;
        SetupMonitorBox.DropDownOpened+=(_,_)=>pendingSetupMonitor=null;
        SetupMonitorBox.SelectionChanged+=(_,_)=>{if(SetupMonitorBox.IsDropDownOpen&&!model.IsRefreshingMonitorChoices)pendingSetupMonitor=SetupMonitorBox.SelectedValue as string;};
        var savedPlacement=model.Preferences.ConfigurationPlacement;
        Loaded+=(_,_)=> {RestoreConfigurationPlacement(savedPlacement);placementReady=true;};
        LocationChanged+=(_,_)=>CaptureConfigurationPlacement();SizeChanged+=(_,_)=>CaptureConfigurationPlacement();
        StateChanged+=(_,_)=> {if(placementReady&&WindowState!=WindowState.Minimized&&model.Preferences.ConfigurationPlacement is {} saved)model.RememberConfigurationPlacement(saved with{Maximized=WindowState==WindowState.Maximized});};
        DataContext = model;
        foreach(var page in new FrameworkElement[]{WheelsPage,SettingsPage,EffectsPage,ProfilesPage,PlacementPage,UpdatesPage,StartupPage,ConnectionPage,SetupPage})pageMotion[page]=new SurfaceMotion(page);
        modeMotion=new SurfaceMotion(AudioModeBanner);
        var previousConnection=model.Connected;
        model.PropertyChanged+=(_,e)=> {if(e.PropertyName==nameof(model.Connected)&&previousConnection!=model.Connected) {previousConnection=model.Connected;modeMotion.Enter(-1,MotionEnabled);if(!model.Connected&&currentSection=="Profiles")ShowSection("Wheels");}};
        model.AppearanceChanged+=()=> {if(!MotionEnabled) {foreach(var motion in pageMotion.Values)motion.Reset();modeMotion.Reset();}};
        model.SelectionChanged+=QueueEditorBindings;
        model.Channels.CollectionChanged+=(_,_)=>QueueEditorBindings();
        model.ClickOutputs.CollectionChanged+=(_,_)=>QueueEditorBindings();
        model.MixChoices.CollectionChanged+=(_,_)=>QueueEditorBindings();
        model.Monitors.CollectionChanged+=(_,_)=>QueueEditorBindings();
        Loaded+=(_,_)=>QueueEditorBindings();
        IsVisibleChanged+=(_,_)=> {if(IsVisible)QueueEditorBindings();};
        QueueEditorBindings();
        SelectedPreview.VolumeRequested += model.SetLevel;
        SelectedPreview.MuteRequested += model.ToggleMute;SelectedPreview.ClickRequested+=model.ExecuteClick;
        EffectsDemo.Wheel=new Wheel{Name="Preview",Color="#DA5DAD",Available=true,Level=.68};
        EffectsDemo.VolumeRequested+=(wheel,value)=>wheel.Level=value;
        EffectsDemo.MuteRequested+=wheel=>wheel.Muted=!wheel.Muted;
        SetupDemo.Wheel=new Wheel{Name="Sample",Color="#73DCC8",Available=true,Level=.5};
        SetupDemo.VolumeRequested+=(wheel,value)=>wheel.Level=Math.Clamp(value,0,1);
        SetupDemo.MuteRequested+=wheel=>wheel.Muted=!wheel.Muted;
        void PreviewInputSettings() {SelectedPreview.ScrollStepPercent=model.ScrollStepPercent;SelectedPreview.ShiftStepPercent=model.ShiftStepPercent;SelectedPreview.ShowShiftMarkers=model.ShowShiftMarkers;SelectedPreview.ScrollAccelerationEnabled=model.ScrollAccelerationEnabled;SelectedPreview.ResetScroll();}
        PreviewInputSettings();model.InputSettingsChanged+=PreviewInputSettings;
        RefreshNavigation();
        Closing += (_,e) => { if (!App.Exiting) { e.Cancel = true; model.Save(); Hide(); } };
        SourceInitialized += (_,_) => { try { var preference=2;Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,33,ref preference,4); } catch { } };
        Activated += (_,_) => { model.RefreshMonitors();model.Changed(nameof(model.StartupEnabled)); model.Changed(nameof(model.StartupDescription)); };
    }
    internal void CommitClickOutput(bool right)
    {
        if(model.IsRefreshingClickOutputs||!model.Connected||model.EditorWheel is not {} wheel)return;
        var box=right?RightClickOutputBox:LeftClickOutputBox;
        if(box.SelectedItem is not Choice choice||string.IsNullOrEmpty(choice.Id)||!model.ClickOutputs.Any(o=>o.Id==choice.Id))return;
        var id=choice.Id;
        if(right)wheel.RightClickOutput=id;else wheel.LeftClickOutput=id;
    }
    private void LeftClickOutputClosed(object? sender,EventArgs e)=>CommitClickOutput(false);
    private void RightClickOutputClosed(object? sender,EventArgs e)=>CommitClickOutput(true);
    private void QueueEditorBindings()
    {
        if(editorRefreshQueued)return;editorRefreshQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind,new Action(()=>
        {
            editorRefreshQueued=false;
            // Items may arrive after SelectedValue was first evaluated, or be replaced on discovery.
            // Reapply the saved IDs after the choice list settles, without writing transient nulls back.
            if(!LeftClickOutputBox.IsDropDownOpen)LeftClickOutputBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            if(!RightClickOutputBox.IsDropDownOpen)RightClickOutputBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            if(!ChannelBox.IsDropDownOpen)ChannelBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            if(!MixBox.IsDropDownOpen)MixBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            if(!MonitorBox.IsDropDownOpen)MonitorBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
            if(!SetupMonitorBox.IsDropDownOpen)SetupMonitorBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
        }));
    }
    private void CaptureConfigurationPlacement()
    {
        if(!placementReady||savedDisplayMissing||!IsVisible||WindowState!=WindowState.Normal)return;
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero||!Native.GetWindowRect(hwnd,out var bounds))return;
        var screen=System.Windows.Forms.Screen.FromHandle(hwnd);var work=screen.WorkingArea;var monitor=MonitorDescriptor.Capture(screen,0);
        model.RememberConfigurationPlacement(new(monitor.Id,bounds.Left-work.Left,bounds.Top-work.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,Native.GetDpiForWindow(hwnd)));
    }
    private void RestoreConfigurationPlacement(ConfigurationPlacement? saved)
    {
        if(saved?.Valid!=true)return;
        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>MonitorSelection.Matches(saved.MonitorId,MonitorDescriptor.Capture(s,0)));
        // If the saved display is unplugged, keep its identity and use a visible temporary location.
        savedDisplayMissing=screen==null;screen??=System.Windows.Forms.Screen.PrimaryScreen;if(screen==null)return;
        var work=screen.WorkingArea;var point=new Native.Point{X=work.Left+work.Width/2,Y=work.Top+work.Height/2};
        Native.GetDpiForMonitor(Native.MonitorFromPoint(point,2),0,out var dpi,out _);if(dpi==0)dpi=96;
        var bounds=saved.Fit(new Rect(work.X,work.Y,work.Width,work.Height),dpi);
        MinWidth=Math.Min(MinWidth,work.Width*96d/dpi);MinHeight=Math.Min(MinHeight,work.Height*96d/dpi);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,(int)Math.Round(bounds.X),(int)Math.Round(bounds.Y),(int)Math.Round(bounds.Width),(int)Math.Round(bounds.Height),0x14);
        if(saved.Maximized)WindowState=WindowState.Maximized;
    }
    internal void RaiseAfterUpdate()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            if(!IsVisible)return;
            var hwnd=new WindowInteropHelper(this).Handle;
            try {Topmost=true;Activate();Native.SetForegroundWindow(hwnd);}
            finally {Topmost=false;}
            Native.SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x43);Native.SetForegroundWindow(hwnd);
        }));
    }
    internal static bool IsHeaderDragSource(DependencyObject? source,DependencyObject header)
    {
        for(var element=source;element!=null;element=GetAncestorNode(element))
        {
            if(element==header)return true;
            // Include template children and content elements, so interactive descendants keep their input.
            if(element is Control or Hyperlink)return false;
        }
        return false;
    }
    internal bool HandleHeaderPress(DependencyObject? source,int clickCount,Action drag)
    {
        if(!IsHeaderDragSource(source,TitleBar))return false;
        savedDisplayMissing=false;
        if(clickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        else drag();
        return true;
    }
    private void TitleDrag(object sender,MouseButtonEventArgs e)
    {
        if(!e.Handled&&e.LeftButton==MouseButtonState.Pressed)
            e.Handled=HandleHeaderPress(e.OriginalSource as DependencyObject,e.ClickCount,()=>DragMove());
    }
    private void Minimize(object sender,RoutedEventArgs e) => WindowState=WindowState.Minimized;
    private void HideConfig(object sender,RoutedEventArgs e) { model.Save(); Hide(); }
    private void Navigate(object sender,RoutedEventArgs e)
    {
        var section=(sender as Button)?.Tag as string;
        if(section==null)return;
        ShowSection(section);
    }
    private void OpenSettingsSearch(object sender,RoutedEventArgs e)
    {
        var dialog=new SettingsSearchWindow(){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.SelectedSetting is {} setting)NavigateToSetting(setting);
    }
    internal void NavigateToSetting(SettingSearchEntry setting)
    {
        if((setting.Section=="Profiles"||setting.Target is "ChannelBox" or "MixBox")&&!model.Connected){model.Message="Connect Wave Link to manage channel profiles.";ShowSection("Connection");return;}
        ShowSection(setting.Section);
        var page=pageMotion.Keys.FirstOrDefault(p=>p.Name==setting.Section+"Page");
        var target=page==null?null:SettingsSearch.FindTarget(page,setting.Target);
        if(target==null)return;
        for(DependencyObject? node=target;node!=null;node=LogicalTreeHelper.GetParent(node))if(node is Expander expander)expander.IsExpanded=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=> {target.BringIntoView();if(target.Focusable)target.Focus();}));
    }
    private void ViewReleaseNotes(object sender,RoutedEventArgs e) {ReleaseNotesPanel.IsExpanded=true;ReleaseNotesPanel.BringIntoView();}
    private void ViewBetaNotes(object sender,RoutedEventArgs e) {if(model.SelectedBetaBuild is {} release){model.ShowReleaseNotes(release);ViewReleaseNotes(sender,e);}}
    private void ReportProblem(object sender,RoutedEventArgs e)=>new ProblemReportWindow(model.ProblemDiagnostics){Owner=this}.ShowDialog();
    internal void ShowSection(string section)
    {
        if(section==currentSection)return;
        var order=new[]{"Wheels","Profiles","Effects","Placement","Settings","Startup","Connection","Updates","Setup"};
        var direction=Array.IndexOf(order,section)>=Array.IndexOf(order,currentSection)?1:-1;
        foreach(var motion in pageMotion.Values)motion.Reset();
        currentSection=section;
        WheelsPage.Visibility=section=="Wheels"?Visibility.Visible:Visibility.Collapsed;
        StartupPage.Visibility=section=="Startup"?Visibility.Visible:Visibility.Collapsed;
        SettingsPage.Visibility=section=="Settings"?Visibility.Visible:Visibility.Collapsed;
        ConnectionPage.Visibility=section=="Connection"?Visibility.Visible:Visibility.Collapsed;
        ProfilesPage.Visibility=section=="Profiles"?Visibility.Visible:Visibility.Collapsed;
        PlacementPage.Visibility=section=="Placement"?Visibility.Visible:Visibility.Collapsed;
        UpdatesPage.Visibility=section=="Updates"?Visibility.Visible:Visibility.Collapsed;
        EffectsPage.Visibility=section=="Effects"?Visibility.Visible:Visibility.Collapsed;
        SetupPage.Visibility=section=="Setup"?Visibility.Visible:Visibility.Collapsed;
        var activePage=pageMotion.Keys.FirstOrDefault(p=>p.Name==section+"Page");if(activePage!=null)pageMotion[activePage].Enter(direction,MotionEnabled);
        RefreshNavigation();
    }
    private void RefreshNavigation()
    {
        var group=currentSection switch {"Profiles"=>"Wheels","Placement"=>"Effects","Startup" or "Connection"=>"Settings",_=>currentSection};
        SectionTabs.Visibility=group is "Updates" or "Setup"?Visibility.Collapsed:Visibility.Visible;
        WheelTabs.Visibility=group=="Wheels"?Visibility.Visible:Visibility.Collapsed;
        AppearanceTabs.Visibility=group=="Effects"?Visibility.Visible:Visibility.Collapsed;
        PreferenceTabs.Visibility=group=="Settings"?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in new[]{WheelsNav,EffectsNav,SettingsNav,UpdatesNav,WheelEditorTab,ProfilesNav,EffectsTab,PlacementNav,ControlsTab,StartupNav,ConnectionNav})
        {
            var primary=button==WheelsNav||button==EffectsNav||button==SettingsNav||button==UpdatesNav;
            var active=(string)button.Tag==(primary?group:currentSection);
            button.Background=new SolidColorBrush(active?Color.FromRgb(32,53,57):Colors.Transparent);
            button.Foreground=new SolidColorBrush(active?Color.FromRgb(163,230,217):Color.FromRgb(152,170,188));
            button.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,active?"Selected":"");
        }
    }
    internal int SetupStep {get;private set;}
    internal void OpenSetup()
    {
        if(model.IsPreviewing)model.Preview();
        SetupStep=0;UpdateSetupStep();ShowSection("Setup");
        App.WriteLog("Opened setup guide");
    }
    private void OpenSetup(object sender,RoutedEventArgs e)=>OpenSetup();
    private void SetupNext(object sender,RoutedEventArgs e) {if(SetupStep==2)FinishSetup(sender,e);else {SetupStep++;UpdateSetupStep();}}
    private void SetupBack(object sender,RoutedEventArgs e) {SetupStep=Math.Max(0,SetupStep-1);UpdateSetupStep();}
    private void FinishSetup(object sender,RoutedEventArgs e) {model.Save();ShowSection("Wheels");}
    private void UpdateSetupStep()
    {
        SetupAudioStep.Visibility=SetupStep==0?Visibility.Visible:Visibility.Collapsed;
        SetupPlacementStep.Visibility=SetupStep==1?Visibility.Visible:Visibility.Collapsed;
        SetupTryStep.Visibility=SetupStep==2?Visibility.Visible:Visibility.Collapsed;
        SetupStepLabel.Text=$"{SetupStep+1} of 3 · "+new[]{"Audio","Placement","Try it"}[SetupStep];
        SetupBackButton.Visibility=SetupStep>0?Visibility.Visible:Visibility.Collapsed;
        SetupNextButton.Content=SetupStep==2?"Finish setup":"Continue";
    }
    private void SetupMonitorClosed(object? sender,EventArgs e)
    {
        if(pendingSetupMonitor is string id&&model.Monitors.Any(m=>m.Id==id))model.MonitorId=id;
        pendingSetupMonitor=null;QueueEditorBindings();
    }
    internal void ApplySetupMonitor()
    {
        if(SetupMonitorBox.SelectedValue is string id&&model.Monitors.Any(m=>m.Id==id))model.MonitorId=id;
        QueueEditorBindings();
    }
    private bool MotionEnabled=>model.InterfaceAnimationsEnabled;
    internal void SeekInterfaceMotion(TimeSpan time) {foreach(var motion in pageMotion.Values)motion.Seek(time);modeMotion?.Seek(time);}
    private void OpenActions(object sender,RoutedEventArgs e) {if(sender is Button {ContextMenu: {} menu} button) {menu.PlacementTarget=button;menu.IsOpen=true;}}
    private void RestoreDefaults(object sender,RoutedEventArgs e)=>model.RestoreDefaults((string)((FrameworkElement)sender).Tag);
    private void PreviewMaxEffect(object sender,RoutedEventArgs e)
    {
        EffectsDemo.Wheel!.Level=1;
        if(model.AnimateWheels)EffectsDemo.StartMaximumEffect();
    }
    private void PreviewMuteEffect(object sender,RoutedEventArgs e)=>EffectsDemo.Wheel!.Muted=!EffectsDemo.Wheel.Muted;
    private void NewProfile(object sender,RoutedEventArgs e)=>model.AddProfile(false);
    private void DuplicateProfile(object sender,RoutedEventArgs e)=>model.AddProfile(true);
    private void RemoveProfile(object sender,RoutedEventArgs e)=>model.RemoveProfile();
    private void AddProfileApp(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Choose the app for this profile",Filter="Windows applications (*.exe)|*.exe",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=true||model.ActiveProfile==null)return;
        var app=Path.GetFileName(dialog.FileName);
        if(!ProfileAutomation.Matches(model.ActiveProfile.Applications,app))model.ActiveProfile.Applications+=(model.ActiveProfile.Applications.Trim().Length==0?"":", ")+app;
    }
    private void AddBlacklistedApp(object sender,RoutedEventArgs e)=>model.AddBlacklistedApplication();
    private void RemoveBlacklistedApp(object sender,RoutedEventArgs e)=>model.RemoveBlacklistedApplication();
    private void BrowseBlacklistedApp(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Choose an app to blacklist",Filter="Windows applications (*.exe)|*.exe",CheckFileExists=true};
        if(dialog.ShowDialog(this)==true)model.AddBlacklistedApplication(Path.GetFileName(dialog.FileName));
    }
    private void ExportProfile(object sender,RoutedEventArgs e)=>Export(false);
    private void ExportAllProfiles(object sender,RoutedEventArgs e)=>Export(true);
    private void Export(bool all)
    {
        var name=all?"WaveWheels-profiles":model.ActiveProfile?.Name??"Profile";
        foreach(var character in Path.GetInvalidFileNameChars())name=name.Replace(character,'_');
        var dialog=new SaveFileDialog {Title="Export WaveWheels profiles",Filter="WaveWheels profiles (*.wavewheels.json)|*.wavewheels.json",DefaultExt=".wavewheels.json",FileName=name+".wavewheels.json"};
        if(dialog.ShowDialog(this)!=true)return;
        try {model.ExportProfiles(dialog.FileName,all);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {model.Message="Export failed: "+ex.Message;}
    }
    private void ImportProfiles(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Import WaveWheels profiles",Filter="WaveWheels profiles (*.wavewheels.json;*.json)|*.wavewheels.json;*.json",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=true)return;
        try {model.ImportProfiles(dialog.FileName);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException) {model.Message="Import failed: "+ex.Message;}
    }
    private async void CheckUpdates(object sender,RoutedEventArgs e)=>await model.CheckForUpdatesAsync();
    private async void RepairApp(object sender,RoutedEventArgs e)=>await model.RepairAppAsync();
    private async void InstallUpdate(object sender,RoutedEventArgs e)=>await model.InstallUpdateAsync();
    private void CancelUpdate(object sender,RoutedEventArgs e)=>model.CancelUpdate();
    private async void InstallBeta(object sender,RoutedEventArgs e)=>await model.InstallBetaAsync();
    internal void SelectFirstWheelForPreview()=>model.EditorWheel=model.DisplayWheels.FirstOrDefault();
    private void OpenBetaRelease(object sender,RoutedEventArgs e)=>model.OpenBetaReleasePage();
    private async void InstallStable(object sender,RoutedEventArgs e)=>await model.InstallStableAsync();
    private void OpenRelease(object sender,RoutedEventArgs e)=>model.OpenReleasePage();
    private void Add(object sender,RoutedEventArgs e) => model.AddWheel();
    private void IncreaseWheelCount(object sender,RoutedEventArgs e)=>model.DisplayWheelCount=Math.Min(12,model.DisplayWheelCount+1);
    private void DecreaseWheelCount(object sender,RoutedEventArgs e)=>model.DisplayWheelCount=Math.Max(1,model.DisplayWheelCount-1);
    private void Remove(object sender,RoutedEventArgs e) => model.RemoveWheel();
    private void UndoWheelDeletion(object sender,RoutedEventArgs e)=>model.UndoWheelDeletion();
    private void RowUp(object sender,RoutedEventArgs e) { if(sender is Button {Tag:Wheel wheel}) { model.SelectedWheel=wheel; AnimateReorder(()=>model.Move(-1)); } }
    private void RowDown(object sender,RoutedEventArgs e) { if(sender is Button {Tag:Wheel wheel}) { model.SelectedWheel=wheel; AnimateReorder(()=>model.Move(1)); } }
    private void Preview(object sender,RoutedEventArgs e) => model.Preview();
    private void Reconnect(object sender,RoutedEventArgs e) => model.Reconnect();
    private void FindAndStartWaveLink(object sender,RoutedEventArgs e)
    {
        if(model.Isolated) {model.Message="Launching Wave Link is disabled in an isolated profile.";return;}
        var installed=WaveLinkLauncher.Find(model.Preferences.WaveLinkExecutable);
        if(installed!=null)model.StartWaveLink(installed);else {model.Message="Wave Link was not found automatically. Select its executable if it is installed.";LocateWaveLink(sender,e);}
    }
    private void LocateWaveLink(object sender,RoutedEventArgs e)
    {
        if(model.Isolated)return;
        var dialog=new OpenFileDialog{Title="Locate Wave Link to start it",Filter="Wave Link (WaveLink.exe)|WaveLink.exe|Applications (*.exe)|*.exe",CheckFileExists=true};
        if(dialog.ShowDialog(this)==true)model.StartWaveLink(new(dialog.FileName));
    }
    private void SetColor(object sender,RoutedEventArgs e) {if(sender is Button {Tag:string color})ColorBox.Text=color;}
    private void ApplyEffectColor(object sender,RoutedEventArgs e)
    {
        var target=(sender as FrameworkElement)?.Tag as string;
        var field=target switch {"NormalColor"=>NormalColorBox,"MutedColor"=>MutedColorBox,_=>MutedButtonColorBox};
        var text=field.Text.Trim();if(!text.StartsWith('#'))text="#"+text;
        if(!EffectSettings.ValidColor(text)){model.Message="Use a six-digit hex color, such as #36BBD3.";return;}
        switch(target){case "NormalColor":model.Effects.NormalColor=text.ToUpperInvariant();break;case "MutedColor":model.Effects.MutedColor=text.ToUpperInvariant();break;default:model.Effects.MutedButtonColor=text.ToUpperInvariant();break;}
        field.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
    private void PickColor(object sender,RoutedEventArgs e)
    {
        var target=(sender as FrameworkElement)?.Tag as string;var wheel=model.EditorWheel;
        var initial=target switch {"NormalColor"=>model.Effects.NormalColor,"MutedColor"=>model.Effects.MutedColor,"MutedButtonColor"=>model.Effects.MutedButtonColor,_=>wheel?.Color??"#FFFFFF"};
        var field=target switch {"NormalColor"=>NormalColorBox,"MutedColor"=>MutedColorBox,"MutedButtonColor"=>MutedButtonColorBox,_=>ColorBox};
        if(EffectSettings.ValidColor(field.Text.Trim()))initial=field.Text.Trim();
        var picker=new ColorPickerWindow(initial){Owner=this};
        if(picker.ShowDialog()!=true)return;
        switch(target) {case "NormalColor":model.Effects.NormalColor=picker.SelectedHex;break;case "MutedColor":model.Effects.MutedColor=picker.SelectedHex;break;case "MutedButtonColor":model.Effects.MutedButtonColor=picker.SelectedHex;break;default:if(wheel!=null)wheel.Color=picker.SelectedHex;break;}
    }
    private void ApplyHex(object sender,RoutedEventArgs e)
    {
        var text=ColorBox.Text.Trim();
        if(!text.StartsWith('#'))text="#"+text;
        if(Regex.IsMatch(text,"^#[0-9A-Fa-f]{6}$") && model.EditorWheel!=null) model.EditorWheel.Color=text.ToUpperInvariant();
        else model.Message="Use a six-digit hex color, such as #36BBD3.";
        ColorBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
    private void ChannelSelected(object sender,SelectionChangedEventArgs e) { if(ChannelBox.IsDropDownOpen)ApplyChannel(); }
    private void ChannelClosed(object? sender,EventArgs e) => ApplyChannel();
    internal void ApplyChannel() { if(model.Connected&&ChannelBox.SelectedValue is string id && model.SelectedWheel!=null && id!=model.SelectedWheel.ChannelId)model.SelectedWheel.ChannelId=id; }
    private void MixSelected(object sender,SelectionChangedEventArgs e) { if(MixBox.IsDropDownOpen)ApplyMix(); }
    private void MixClosed(object? sender,EventArgs e) => ApplyMix();
    private void MonitorSelected(object sender,SelectionChangedEventArgs e) {if(MonitorBox.IsDropDownOpen&&!model.IsRefreshingMonitorChoices)pendingMonitor=MonitorBox.SelectedValue as string;}
    private void MonitorClosed(object? sender,EventArgs e) {if(pendingMonitor is string id&&model.Monitors.Any(m=>m.Id==id))model.MonitorId=id;pendingMonitor=null;QueueEditorBindings();}
    internal void ApplyMonitor() {if(MonitorBox.SelectedValue is string id&&model.Monitors.Any(m=>m.Id==id))model.MonitorId=id;}
    private void ApplyMix() { if(model.Connected&&MixBox.SelectedValue is string id && model.SelectedWheel!=null)model.SelectedWheel.MixId=id; }
    private void DragStart(object sender,MouseButtonEventArgs e)
    {
        if(!model.Connected)return;
        start=e.GetPosition(WheelList);dragWheel=null;
        var item=ItemsControl.ContainerFromElement(WheelList,e.OriginalSource as DependencyObject) as ListBoxItem;
        for(var element=e.OriginalSource as DependencyObject; element!=null&&element!=item; element=GetAncestorNode(element))
            if(element is FrameworkElement {Tag:"DragHandle"} && item?.DataContext is Wheel wheel) { dragWheel=wheel; model.SelectedWheel=wheel; break; }
    }
    private void DragMove(object sender,MouseEventArgs e)
    {
        if(dragWheel==null||e.LeftButton!=MouseButtonState.Pressed)return;
        var distance=e.GetPosition(WheelList)-start;
        if(Math.Abs(distance.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(distance.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var wheel=dragWheel;dragWheel=null;dragging=wheel;
        var item=WheelList.ItemContainerGenerator.ContainerFromItem(wheel) as ListBoxItem;
        if(item!=null)item.Opacity=.4;
        model.Message="Moving "+wheel.Name+" · drop on the highlighted line";
        try { DragDrop.DoDragDrop(WheelList,new DataObject(typeof(Wheel),wheel),DragDropEffects.Move); }
        finally {dragging=null;if(item!=null)item.Opacity=1;ClearDropMarker();}
        e.Handled=true;
    }
    private void DragOverWheel(object sender,DragEventArgs e)
    {
        if(!model.Connected||!e.Data.GetDataPresent(typeof(Wheel))) {e.Effects=DragDropEffects.None;return;}
        var point=e.GetPosition(WheelList);
        var scroll=FindChild<ScrollViewer>(WheelList);
        if(scroll!=null&&Environment.TickCount64-lastAutoScroll>45)
        {
            if(point.Y<38)scroll.ScrollToVerticalOffset(scroll.VerticalOffset-18);
            else if(point.Y>WheelList.ActualHeight-38)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+18);
            lastAutoScroll=Environment.TickCount64;
        }
        insertion=InsertionAt(point.Y);
        ShowDropIndicator(insertion);
        e.Effects=DragDropEffects.Move;e.Handled=true;
    }
    internal double? DropIndicatorY => dropMarker?.LineY;
    internal void ShowDropIndicator(int gap)
    {
        insertion=Math.Clamp(gap,0,model.Wheels.Count);
        dropLayer??=AdornerLayer.GetAdornerLayer(WheelList);
        if(dropLayer!=null)
        {
            if(dropMarker==null) {dropMarker=new DropMarker(WheelList);dropLayer.Add(dropMarker);}
            double y;
            if(insertion<model.Wheels.Count&&WheelList.ItemContainerGenerator.ContainerFromIndex(insertion) is ListBoxItem next)y=next.TranslatePoint(new Point(0,0),WheelList).Y-5;
            else if(WheelList.ItemContainerGenerator.ContainerFromIndex(model.Wheels.Count-1) is ListBoxItem last)y=last.TranslatePoint(new Point(0,last.ActualHeight),WheelList).Y+5;
            else y=2;
            dropMarker.LineY=Math.Clamp(y,2,Math.Max(2,WheelList.ActualHeight-3));dropMarker.InvalidateVisual();
        }
    }
    internal int InsertionAt(double y)
    {
        for(var i=0;i<model.Wheels.Count;i++)
            if(WheelList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item && y<item.TranslatePoint(new Point(0,item.ActualHeight/2),WheelList).Y)return i;
        return model.Wheels.Count;
    }
    private void DragLeaveWheel(object sender,DragEventArgs e)
    {
        var p=e.GetPosition(WheelList);
        if(p.X<0||p.Y<0||p.X>WheelList.ActualWidth||p.Y>WheelList.ActualHeight)ClearDropMarker();
    }
    private void DropWheel(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(Wheel)) is not Wheel wheel||!model.Wheels.Contains(wheel))return;
        insertion=InsertionAt(e.GetPosition(WheelList).Y);
        AnimateReorder(()=>model.MoveToInsertion(wheel,insertion));
        model.Message=wheel.Name+" moved to position "+(model.Wheels.IndexOf(wheel)+1)+".";
        ClearDropMarker();
        e.Handled=true;
    }
    private void ReorderKey(object sender,KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if((Keyboard.Modifiers&ModifierKeys.Alt)==0||key is not (Key.Up or Key.Down))return;
        AnimateReorder(()=>model.Move(key==Key.Up?-1:1));e.Handled=true;
    }
    private void AnimateReorder(Action change)
    {
        var before=new Dictionary<string,double>();
        foreach(var wheel in model.Wheels)if(WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is ListBoxItem item)before[wheel.Id]=item.TranslatePoint(new Point(0,0),WheelList).Y;
        change();WheelList.UpdateLayout();
        foreach(var wheel in model.Wheels)if(WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is ListBoxItem item)item.RenderTransform=Transform.Identity;
        if(!MotionEnabled)return;
        foreach(var wheel in model.Wheels)
        {
            if(!before.TryGetValue(wheel.Id,out var old)||WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is not ListBoxItem item)continue;
            var delta=old-item.TranslatePoint(new Point(0,0),WheelList).Y;
            if(Math.Abs(delta)<1)continue;
            var transform=new TranslateTransform();item.RenderTransform=transform;
            transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(delta,0,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
        }
    }
    internal void ClearDropMarker() {if(dropMarker!=null)dropLayer?.Remove(dropMarker);dropMarker=null;}
    private static DependencyObject? GetAncestorNode(DependencyObject element)=>element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
    private static T? FindChild<T>(DependencyObject element) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++){var child=VisualTreeHelper.GetChild(element,i);if(child is T found)return found;var nested=FindChild<T>(child);if(nested!=null)return nested;}
        return null;
    }
    private sealed class DropMarker:Adorner
    {
        public double LineY {get;set;}
        public DropMarker(UIElement element):base(element) {IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext dc)
        {
            var a=new Point(8,LineY);var b=new Point(Math.Max(10,AdornedElement.RenderSize.Width-8),LineY);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(45,216,174,255)),8),a,b);
            var brush=new SolidColorBrush(Color.FromRgb(216,174,255));dc.DrawLine(new Pen(brush,2),a,b);dc.DrawEllipse(brush,null,a,3,3);dc.DrawEllipse(brush,null,b,3,3);
        }
    }
}
