using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WaveWheels;

public sealed class OverlayWindow : Window
{
    private readonly MainViewModel model;
    private readonly StackPanel stack = new() { Width = OverlaySizing.StackWidth, Margin = new Thickness(0) };
    private readonly Grid panel = new();
    private readonly OverlayMotion motion;
    private readonly Viewbox viewport = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<WheelControl> controls = new();
    private readonly List<OverlayMotion> rowMotions=new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly EdgeHook hook = new();
    private readonly OverlayInputRouter input = new();
    private volatile HitState hitState = new(false, true, 6, 0, Array.Empty<HitRow>(),"Left","",Array.Empty<ScreenTarget>());
    private ScreenTarget[] screens=Array.Empty<ScreenTarget>();
    private long screenRefresh;
    private long lastActivity;
    private int generation;
    private bool shown;
    private IntPtr handle;
    private Forms.Screen? activeScreen;
    private double activeDpi = 1;
    private sealed record HitRow(int Index, int Left, int Top, int Right, int Bottom, bool Available);
    private sealed record ScreenTarget(Forms.Screen Screen,EdgeRegion Region,MonitorDescriptor Monitor);
    private sealed record HitState(bool Visible, bool Enabled, int EdgeWidth, int Generation, HitRow[] Rows,string Edge,string MonitorId,ScreenTarget[] Screens);
    internal OverlayMotion Motion => motion;
    internal OverlayInputRouter Input => input;

    public OverlayWindow(MainViewModel model)
    {
        this.model = model;
        Width = 350; Height = 430; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Title = "WaveWheels volume overlay";
        // No backdrop: only the wheel artwork and labels are painted over the desktop.
        panel.Background = null;
        motion = new OverlayMotion(panel);
        viewport.Child = stack; panel.Children.Add(viewport); Content = panel;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var styles = Native.GetWindowLongPtr(handle,-20).ToInt64();
            Native.SetWindowLongPtr(handle,-20,new IntPtr(styles | 0x08000000 | 0x00000080));
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
        model.LayoutChanged += Rebuild;
        model.StateChanged += UpdateHits;
        model.ApplicationBlockChanged+=ApplyApplicationBlock;
        model.AppearanceChanged += ApplyAppearance;
        model.InputSettingsChanged += ApplyInputSettings;
        model.PreviewRequested += TogglePreview;
        hook.WheelInput = OnGlobalWheel;
        hook.Failed = error => Dispatcher.BeginInvoke(() => { model.Message = "Edge scrolling is unavailable: " + error; App.WriteLog("Hook: " + error); });
        MouseMove += (_, _) => lastActivity = Environment.TickCount64;
        MouseDown += (_, _) => lastActivity = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            if(Environment.TickCount64-screenRefresh>2000)RefreshScreens();
            if (!shown) return;
            UpdateHits(); // Actual animated positions, including monitor/DPI and hover transforms.
            if(model.IsPreviewing)return;
            if (IsMouseOver || controls.Any(c => c.IsMouseCaptured)) { lastActivity = Environment.TickCount64; return; }
            if (Environment.TickCount64 - lastActivity >= model.Preferences.IdleMilliseconds) Dismiss();
        };
        Closed += (_, _) => { hook.Dispose(); timer.Stop(); };
        Rebuild(); ApplyAppearance(); timer.Start();
    }
    public void StartHook() => hook.Start();
    private void Rebuild()
    {
        generation++; // Drop queued input that targeted the previous layout/profile.
        stack.Children.Clear(); controls.Clear();rowMotions.Clear(); stack.Children.Add(new Border { Height = OverlaySizing.EffectPadding });
        foreach (var wheel in model.DisplayWheels)
        {
            var c = new WheelControl { Wheel = wheel, Effects=model.Effects, EffectsEnabled=model.AnimateWheels,UseMuteButton=model.UseMuteButton,ScrollStepPercent=model.ScrollStepPercent,ShiftStepPercent=model.ShiftStepPercent,ShowShiftMarkers=model.ShowShiftMarkers,ScrollAccelerationEnabled=model.ScrollAccelerationEnabled,Margin = new Thickness(OverlaySizing.EffectPadding,0,OverlaySizing.EffectPadding,controls.Count<model.DisplayWheelCount-1?model.WheelSpacing:0), CanAdjustVolume = () => !model.IsApplicationBlocked&&input.CanAdjust(Environment.TickCount64) };
            c.VolumeRequested += model.SetLevel; c.MuteRequested += model.ToggleMute;
            c.Selected += w => { model.EditorWheel = w; lastActivity = Environment.TickCount64; };
            var row=new Border{Child=c};var rowMotion=new OverlayMotion(row);rowMotion.SetEdge(model.ScreenEdge);
            controls.Add(c);rowMotions.Add(rowMotion);stack.Children.Add(row);
        }
        stack.Children.Add(new Border { Height = OverlaySizing.EffectPadding });
        if (shown) Position();
        UpdateHits();
    }
    private void ApplyAppearance()
    {
        foreach(var control in controls)control.EffectsEnabled=model.AnimateWheels;
        RefreshScreens();motion.SetEdge(model.ScreenEdge);foreach(var row in rowMotions)row.SetEdge(model.ScreenEdge);
        if(!model.AnimateWheels||!SystemParameters.ClientAreaAnimation)
        {
            foreach(var row in rowMotions)row.Enter(70,false,false,()=>{});
            if(shown)motion.Enter(MotionDistance,false,false,()=>{});
        }
        var selected=model.MonitorId.Length>0?screens.FirstOrDefault(s=>MonitorSelection.Matches(model.MonitorId,s.Monitor)):null;
        if(shown&&selected!=null) {activeScreen=selected.Screen;activeDpi=selected.Region.Dpi;}
        if (shown) Position();
    }
    private void ApplyInputSettings() {foreach(var control in controls) {control.UseMuteButton=model.UseMuteButton;control.ScrollStepPercent=model.ScrollStepPercent;control.ShiftStepPercent=model.ShiftStepPercent;control.ShowShiftMarkers=model.ShowShiftMarkers;control.ScrollAccelerationEnabled=model.ScrollAccelerationEnabled;control.ResetScroll();}}
    private void RefreshScreens()
    {
        screens=Forms.Screen.AllScreens.Select((s,i)=>new ScreenTarget(s,new EdgeRegion(s.Bounds.Left,s.Bounds.Top,s.Bounds.Right,s.Bounds.Bottom,ScaleFor(s.Bounds.Left+s.Bounds.Width/2,s.Bounds.Top+s.Bounds.Height/2)),MonitorDescriptor.Capture(s,i))).ToArray();
        model.ApplyMonitorChoices(screens.Select(s=>s.Monitor).ToArray());
        screenRefresh=Environment.TickCount64;UpdateHits();
    }
    internal bool OnGlobalWheel(int x, int y, int delta)
    {
        var state = hitState;
        if (!state.Enabled) return false;
        var target=state.Screens.FirstOrDefault(s=>s.Region.IsTrigger(x,y,state.Edge,state.EdgeWidth)&&MonitorSelection.Matches(state.MonitorId,s.Monitor));
        // The activation strip takes priority even when animated/expanded row bounds overlap it.
        var row = target==null&&state.Visible ? state.Rows.FirstOrDefault(r => x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom) : null;
        if(target==null&&row==null)
        {
            input.ObserveUnclaimedScroll(Environment.TickCount64);return false;
        }
        var route = input.Route(row?.Index ?? -1, -1, Environment.TickCount64);
        Dispatcher.BeginInvoke(() =>
        {
            if(!model.RefreshInteractionPolicy())return;
            if (route.Reveal)
            {
                if(target!=null)Reveal(target.Screen,target.Region.Dpi);
            }
            else if (!shown || (state.Visible && state.Generation != generation)) return;
            lastActivity = Environment.TickCount64;
            var index = route.Index;
            if (index >= 0 && index < controls.Count) { model.EditorWheel = controls[index].Wheel; controls[index].Adjust(delta/120d); }
        });
        return true;
    }
    private static double ScaleFor(int x,int y)
    {
        try { Native.GetDpiForMonitor(Native.MonitorFromPoint(new Native.Point { X=x,Y=y },2),0,out var dpi,out _); return dpi > 0 ? dpi/96d : 1; } catch { return 1; }
    }
    public void ShowAtCursor()
    {
        if(!model.RefreshInteractionPolicy())return;
        Native.GetCursorPos(out var point); input.Preview();
        RefreshScreens();var target=screens.FirstOrDefault(s=>model.MonitorId.Length>0?MonitorSelection.Matches(model.MonitorId,s.Monitor):s.Region.Contains(point.X,point.Y))??screens.FirstOrDefault();
        if(target!=null)Reveal(target.Screen,target.Region.Dpi);else model.SetPreviewActive(false);
    }
    private void TogglePreview() {if(model.IsPreviewing)ShowAtCursor();else Dismiss();}
    private void Position()
    {
        if (activeScreen == null) return;
        var size = OverlaySizing.Calculate(model.DisplayWheelCount, model.WheelSizePercent, activeScreen.WorkingArea.Width/activeDpi, activeScreen.WorkingArea.Height/activeDpi,model.WheelSpacing);
        Width = size.Width; Height = size.Height;
        viewport.Width = OverlaySizing.StackWidth*size.Scale;
        viewport.Height = OverlaySizing.StackHeight(model.DisplayWheelCount,model.WheelSpacing)*size.Scale;
        var area=activeScreen.WorkingArea;var bounds=activeScreen.Bounds;var w=(int)Math.Ceiling(Width*activeDpi);var h=(int)Math.Ceiling(Height*activeDpi);var fraction=model.PositionPercent/100d;
        var horizontal=model.ScreenEdge is "Top" or "Bottom";
        var x=horizontal?area.Left+(int)((area.Width-w)*fraction):model.ScreenEdge=="Right"?bounds.Right-w:bounds.Left;
        var y=horizontal?(model.ScreenEdge=="Bottom"?bounds.Bottom-h:bounds.Top):area.Top+(int)((area.Height-h)*fraction);
        var inset=OverlaySizing.EdgeOffset(model.EdgeDistance,size.Scale,activeDpi);
        if(horizontal)y+=model.ScreenEdge=="Bottom"?-inset:inset;else x+=model.ScreenEdge=="Right"?-inset:inset;
        Native.SetWindowPos(handle,new IntPtr(-1),x,y,w,h,0x0010|0x0040);
        UpdateLayout(); UpdateHits();
    }
    private void Reveal(Forms.Screen screen,double dpi)
    {
        if(!model.RefreshInteractionPolicy())return;
        generation++; activeScreen = screen; activeDpi = dpi;
        var wasHidden = !IsVisible;
        model.EditorWheel = model.DisplayWheels.FirstOrDefault();
        if (wasHidden)
        {
            // Set the hidden pose before Show, preventing a full-size flash on the first frame.
            motion.PrepareHidden(MotionDistance);
            foreach(var row in rowMotions)row.PrepareHidden(70);
            Show();
        }
        shown = true; Position(); lastActivity = Environment.TickCount64;
        foreach (var c in controls) c.ResetScroll();
        var animate=model.AnimateWheels&&SystemParameters.ClientAreaAnimation;
        motion.Enter(MotionDistance, wasHidden, animate, () => { });
        if(wasHidden)for(var i=0;i<rowMotions.Count;i++)rowMotions[i].Enter(70,true,animate,()=>{},OverlayMotion.StaggerDelay(i));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateHits);
    }
    private void Dismiss()
    {
        var current = ++generation;
        shown = false;model.SetPreviewActive(false); input.Close(); UpdateHits();
        motion.Exit(MotionDistance, model.AnimateWheels&&SystemParameters.ClientAreaAnimation, () => { if (generation == current && !shown) Hide(); });
    }
    private double MotionDistance=>model.ScreenEdge is "Top" or "Bottom"?Height:Width;
    private void ApplyApplicationBlock()
    {
        if(model.IsApplicationBlocked)
        {
            generation++;shown=false;model.SetPreviewActive(false);input.Close();
            foreach(var control in controls) {if(control.IsMouseCaptured)control.ReleaseMouseCapture();control.ResetScroll();}
            if(IsVisible)Hide();
        }
        UpdateHits();
    }
    private void UpdateHits()
    {
        var rows = new List<HitRow>();
        if (shown && IsVisible)
        {
            var origin=PointToScreen(new Point(0,0));var end=PointToScreen(new Point(ActualWidth,ActualHeight));
            for(var i=0;i<controls.Count;i++)
            {
                var c=controls[i]; if(!c.IsArrangeValid) continue;
                var a=c.PointToScreen(new Point(0,0));var b=c.PointToScreen(new Point(c.ActualWidth,c.ActualHeight));
                var left=(int)Math.Max(origin.X,a.X-8);var top=(int)Math.Max(origin.Y,a.Y);
                var right=(int)Math.Min(end.X,b.X+8);var bottom=(int)Math.Min(end.Y,b.Y);
                if(right>left&&bottom>top)rows.Add(new(i,left,top,right,bottom,c.Wheel?.Available==true));
            }
        }
        hitState = new(shown,!model.IsApplicationBlocked,model.Preferences.EdgeWidth,generation,rows.ToArray(),model.ScreenEdge,model.MonitorId,screens);
    }
    private IntPtr WindowProc(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x21) {handled=true;return new IntPtr(3);}
        if(message==0x84)
        {
            var packed=lp.ToInt64();var x=unchecked((short)(packed&0xffff));var y=unchecked((short)((packed>>16)&0xffff));
            if(!hitState.Rows.Any(r=>x>=r.Left&&x<=r.Right&&y>=r.Top&&y<=r.Bottom)) {handled=true;return new IntPtr(-1);}
        }
        return IntPtr.Zero;
    }
}
