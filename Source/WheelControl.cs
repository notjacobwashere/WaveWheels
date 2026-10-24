using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WaveWheels;

public sealed class WheelControl : FrameworkElement
{
    public static readonly DependencyProperty WheelProperty = DependencyProperty.Register(nameof(Wheel), typeof(Wheel), typeof(WheelControl), new PropertyMetadata(null, OnWheelChanged));
    public static readonly DependencyProperty DisplayLevelProperty = DependencyProperty.Register(nameof(DisplayLevel), typeof(double), typeof(WheelControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public Wheel? Wheel { get => (Wheel?)GetValue(WheelProperty); set => SetValue(WheelProperty, value); }
    public double DisplayLevel { get => (double)GetValue(DisplayLevelProperty); set => SetValue(DisplayLevelProperty, value); }
    internal static readonly DependencyProperty MaximumEffectProgressProperty=DependencyProperty.Register("MaximumEffectProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EffectsEnabledProperty=DependencyProperty.Register(nameof(EffectsEnabled),typeof(bool),typeof(WheelControl),new PropertyMetadata(true,(d,e)=> {if(!(bool)e.NewValue) {var control=(WheelControl)d;control.StopMaximumEffect();control.StopNumberTransition();control.StopMuteFeedback();control.StopShiftFeedback();control.StopUnmuteFeedback();}}));
    public bool EffectsEnabled {get=>(bool)GetValue(EffectsEnabledProperty);set=>SetValue(EffectsEnabledProperty,value);}
    public static readonly DependencyProperty EffectsProperty=DependencyProperty.Register(nameof(Effects),typeof(EffectSettings),typeof(WheelControl),new PropertyMetadata(null,(d,e)=>
    {
        var control=(WheelControl)d;if(e.OldValue is EffectSettings old)old.PropertyChanged-=control.EffectsChanged;
        if(e.NewValue is EffectSettings next)next.PropertyChanged+=control.EffectsChanged;control.InvalidateVisual();
    }));
    private readonly EffectSettings defaultEffects=new();
    public EffectSettings Effects {get=>(EffectSettings?)GetValue(EffectsProperty)??defaultEffects;set=>SetValue(EffectsProperty,value);}
    private void EffectsChanged(object? sender,PropertyChangedEventArgs e)=>InvalidateVisual();
    private readonly MaximumVolumeTransition maximumTransition=new();
    private AnimationClock? maximumClock;
    private readonly double[] sparkArcLevels=new double[400];
    private AnimationClock? levelClock;
    internal double MaximumEffectProgress=>(double)GetValue(MaximumEffectProgressProperty);
    internal int MaximumEffectStarts {get;private set;}
    internal double MaximumEffectOpacity=>1;
    internal double MaximumEmissionCutoff {get;private set;}=1;
    private static readonly DependencyProperty MuteFeedbackProgressProperty=DependencyProperty.Register("MuteFeedbackProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    private AnimationClock? muteFeedbackClock;
    internal double MuteFeedbackProgress=>(double)GetValue(MuteFeedbackProgressProperty);
    internal int MuteFeedbackStarts {get;private set;}
    internal const int MuteFeedbackMilliseconds=1200;
    internal static double MuteShake(double progress) {var phase=Math.Clamp(progress*MuteFeedbackMilliseconds/520,0,1);return Math.Sin(phase*Math.PI*6)*7*Math.Pow(1-phase,1.2);}
    internal static double SlashSweep(double progress)=>1-Math.Pow(1-Math.Clamp(progress*MuteFeedbackMilliseconds/350,0,1),3);
    internal static double SlashOpacity(double progress) {var ms=progress*MuteFeedbackMilliseconds;return Math.Min(1,ms/50)*Math.Clamp((1200-ms)/350,0,1);}
    internal void SeekMuteFeedback(TimeSpan time)=>muteFeedbackClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    internal void StartMuteFeedback()
    {
        StopMuteFeedback();MuteFeedbackStarts++;
        muteFeedbackClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MuteFeedbackMilliseconds*100d/Effects.MuteSpeedPercent)){FillBehavior=FillBehavior.Stop}.CreateClock(true);ApplyAnimationClock(MuteFeedbackProgressProperty,muteFeedbackClock);
    }
    private void StopMuteFeedback() {muteFeedbackClock?.Controller?.Remove();muteFeedbackClock=null;BeginAnimation(MuteFeedbackProgressProperty,null);SetValue(MuteFeedbackProgressProperty,1d);}
    private static readonly DependencyProperty UnmuteProgressProperty=DependencyProperty.Register("UnmuteProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    private AnimationClock? unmuteClock;
    internal double UnmuteProgress=>(double)GetValue(UnmuteProgressProperty);
    internal int UnmuteFeedbackStarts {get;private set;}
    internal void SeekUnmuteFeedback(TimeSpan time)=>unmuteClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    private void StartUnmuteFeedback()
    {
        UnmuteFeedbackStarts++;unmuteClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(620)){FillBehavior=FillBehavior.Stop}.CreateClock(true);
        ApplyAnimationClock(UnmuteProgressProperty,unmuteClock);
    }
    private void StopUnmuteFeedback() {unmuteClock?.Controller?.Remove();unmuteClock=null;BeginAnimation(UnmuteProgressProperty,null);SetValue(UnmuteProgressProperty,1d);}
    internal static Color RecoveryColor(Color muted,Color normal,double progress)
    {
        var t=1-Math.Pow(1-Math.Clamp(progress,0,1),3);
        byte Blend(byte from,byte to)=>(byte)Math.Round(from+(to-from)*t);
        return Color.FromRgb(Blend(muted.R,normal.R),Blend(muted.G,normal.G),Blend(muted.B,normal.B));
    }
    private static readonly DependencyProperty ShiftFeedbackProgressProperty=DependencyProperty.Register("ShiftFeedbackProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    private AnimationClock? shiftFeedbackClock;
    private double shiftStrength,shiftDirection;
    private ShiftPose shiftInitial;
    internal readonly record struct ShiftPose(double X,double Y,double Angle,double Stretch);
    internal const int ShiftFeedbackMilliseconds=620;
    internal int ShiftFeedbackStarts {get;private set;}
    internal double ShiftFeedbackProgress=>(double)GetValue(ShiftFeedbackProgressProperty);
    internal ShiftPose CurrentShiftPose=>ShiftSpring(ShiftFeedbackProgress,shiftStrength,shiftDirection,shiftInitial);
    internal static ShiftPose ShiftSpring(double progress,double strength,double direction,ShiftPose initial=default)
    {
        var p=Math.Clamp(progress,0,1);var envelope=Math.Pow(1-p,2);var carry=Math.Exp(-10*p)*(1-p);
        return new(initial.X*carry+direction*3*Math.Sin(p*Math.PI*5)*envelope*strength,
            initial.Y*carry-8*Math.Sin(p*Math.PI*3)*envelope*strength,
            initial.Angle*carry+direction*5*Math.Sin(p*Math.PI*5)*envelope*strength,
            initial.Stretch*carry+.065*Math.Sin(p*Math.PI*4)*envelope*strength);
    }
    private void StartShiftFeedback(double change)
    {
        if(Math.Abs(change)<1e-7||!EffectsEnabled||!SystemParameters.ClientAreaAnimation)return;
        var pose=CurrentShiftPose;StopShiftFeedback();shiftInitial=pose;
        shiftStrength=Math.Clamp(Math.Abs(change)/.2,.35,1.5);shiftDirection=Math.Sign(change);ShiftFeedbackStarts++;
        shiftFeedbackClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(ShiftFeedbackMilliseconds)){FillBehavior=FillBehavior.Stop}.CreateClock(true);
        ApplyAnimationClock(ShiftFeedbackProgressProperty,shiftFeedbackClock);
    }
    internal void SeekShiftFeedback(TimeSpan time)=>shiftFeedbackClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    private void StopShiftFeedback()
    {
        shiftFeedbackClock?.Controller?.Remove();shiftFeedbackClock=null;BeginAnimation(ShiftFeedbackProgressProperty,null);
        SetValue(ShiftFeedbackProgressProperty,1d);shiftInitial=default;shiftStrength=0;
    }
    private static readonly DependencyProperty NumberProgressProperty=DependencyProperty.Register("NumberProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    internal const int NumberTransitionMilliseconds=150;
    private AnimationClock? numberClock;
    internal string NumberText {get;private set;}="—";
    internal string OutgoingNumberText {get;private set;}="—";
    internal int NumberDirection {get;private set;}=1;
    internal int NumberTransitionStarts {get;private set;}
    internal double NumberProgress=>(double)GetValue(NumberProgressProperty);
    private double numberLevel;
    public event Action<Wheel>? MuteRequested;
    public event Action<Wheel, double>? VolumeRequested;
    public event Action<Wheel>? Selected;
    public Func<bool>? CanAdjustVolume { get; set; }
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(WheelControl), new PropertyMetadata(false, (d,e) => { var c = (WheelControl)d; c.Width = (bool)e.NewValue ? 120 : 300; c.InvalidateVisual(); }));
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    private bool dragging;
    private Point dragStart;
    private double dragLevel;
    private readonly ScrollAccumulator scroll=new();
    private readonly ShiftVolumeSnap snap=new();
    public int ShiftStepPercent {get=>snap.StepPercent;set {snap.StepPercent=value;InvalidateVisual();}}
    private bool showShiftMarkers;
    public bool ShowShiftMarkers {get=>showShiftMarkers;set {if(showShiftMarkers==value)return;showShiftMarkers=value;InvalidateVisual();}}
    internal static Point PrecisionMarkerPoint(int percent)
    {
        var angle=(90+percent*2.7)*Math.PI/180;return new Point(60+Math.Cos(angle)*49,60+Math.Sin(angle)*49);
    }
    private readonly DispatcherTimer precisionTimer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    internal bool PrecisionMode {get;private set;}
    internal void SetPrecisionMode(bool enabled)
    {
        if(PrecisionMode==enabled)return;
        PrecisionMode=enabled;ResetScroll();InvalidateVisual();
    }
    public int ScrollStepPercent {get;set;}=1;
    public bool ScrollAccelerationEnabled {get;set;}=true;
    private bool active;
    private readonly DispatcherTimer hoverExitTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    internal bool HoverActive=>active;
    internal int HoverTransitions {get;private set;}
    internal void EnterHover()
    {
        hoverExitTimer.Stop();if(active)return;active=true;HoverTransitions++;
        SetPrecisionMode(Native.ShiftDown);precisionTimer.Start();Selected?.Invoke(Wheel!);AnimateScale(1.04);InvalidateVisual();
    }
    internal void LeaveHover()=>hoverExitTimer.Start();
    internal void CompleteHoverExit()
    {
        hoverExitTimer.Stop();if(IsMouseOver||IsMouseCaptured||!active)return;
        active=false;HoverTransitions++;SetMuteHovered(false);precisionTimer.Stop();SetPrecisionMode(false);AnimateScale(1);InvalidateVisual();
    }
    internal static readonly Point MuteCenter=new(60,82);
    private bool muteHovered;
    internal void SetMuteHovered(bool value) {if(muteHovered==value)return;muteHovered=value;InvalidateVisual();}
    internal static bool HitsMute(Point point)=>(point-MuteCenter).Length<16;
    internal Rect LabelBounds {get;private set;}
    internal static Color SurfaceColor(bool muted)=>muted?Color.FromRgb(38,5,13):Color.FromRgb(17,20,27);
    public WheelControl()
    {
        Width = 300; Height = 120; Focusable = true; Cursor = Cursors.Hand;
        precisionTimer.Tick+=(_,_)=>SetPrecisionMode(active&&Native.ShiftDown);
        MouseEnter += (_, _) => EnterHover();
        MouseLeave += (_, _) => LeaveHover();
        hoverExitTimer.Tick+=(_,_)=>CompleteHoverExit();
        Unloaded += (_, _) => { active=false;Effects.PropertyChanged-=EffectsChanged;hoverExitTimer.Stop();precisionTimer.Stop();SetPrecisionMode(false);StopMaximumEffect();StopNumberTransition();StopMuteFeedback();StopShiftFeedback();StopUnmuteFeedback();if (Wheel != null) Wheel.PropertyChanged -= ModelChanged; };
        Loaded += (_, _) => {Effects.PropertyChanged-=EffectsChanged;Effects.PropertyChanged+=EffectsChanged; if (Wheel != null) { Wheel.PropertyChanged -= ModelChanged; Wheel.PropertyChanged += ModelChanged; maximumTransition.Reset(Wheel.Available,Wheel.Level);ResetNumberDisplay();AnimateLevel(); } };
        MouseLeftButtonDown += Down;
        MouseMove += (_, e) => {SetMuteHovered(HitsMute(e.GetPosition(this))); if (dragging && Wheel?.Available == true && CanAdjustVolume?.Invoke() != false) VolumeRequested?.Invoke(Wheel, dragLevel - (e.GetPosition(this).Y - dragStart.Y) / 200); };
        MouseLeftButtonUp += (_, e) => { if (dragging) { dragging = false; ReleaseMouseCapture(); e.Handled = true; } };
        LostMouseCapture += (_, _) => dragging = false;
        MouseWheel += (_, e) => { if (Wheel?.Available == true) { Adjust(e.Delta / 120d); e.Handled = true; } };
        KeyDown += (_, e) => { if (Wheel == null) return; if (e.Key is Key.Up or Key.Right) { Adjust(1); e.Handled = true; } else if (e.Key is Key.Down or Key.Left) { Adjust(-1); e.Handled = true; } else if (e.Key is Key.M or Key.Space) { MuteRequested?.Invoke(Wheel); e.Handled = true; } };
    }
    private static void OnWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WheelControl)d;
        control.StopMuteFeedback();control.StopShiftFeedback();control.StopUnmuteFeedback();
        if (e.OldValue is Wheel old) old.PropertyChanged -= control.ModelChanged;
        if (e.NewValue is Wheel next) next.PropertyChanged += control.ModelChanged;
        control.StopMaximumEffect();control.maximumTransition.Reset(control.Wheel?.Available==true,control.Wheel?.Level??0);
        control.ResetScroll();
        control.ResetNumberDisplay();
        control.AnimateLevel(); control.InvalidateVisual();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Wheel.Level))
        {
            UpdateNumberDisplay();
            if(maximumTransition.Observe(Wheel?.Available==true,Wheel?.Level??0,EffectsEnabled&&SystemParameters.ClientAreaAnimation))StartMaximumEffect();
            else if(Wheel?.Level<1&&maximumClock!=null&&MaximumEffectProgress<1)StopSparkEmission();
            AnimateLevel();
        }
        else if(e.PropertyName==nameof(Wheel.Available)) {ResetNumberDisplay();maximumTransition.Reset(Wheel?.Available==true,Wheel?.Level??0);if(Wheel?.Available!=true) {StopMaximumEffect();StopMuteFeedback();StopShiftFeedback();StopUnmuteFeedback();}}
        else if(e.PropertyName==nameof(Wheel.Muted))
        {
            StopMuteFeedback();StopUnmuteFeedback();
            if(Wheel?.Available==true&&EffectsEnabled&&SystemParameters.ClientAreaAnimation) {if(Wheel.Muted)StartMuteFeedback();else StartUnmuteFeedback();}
        }
        InvalidateVisual();
        if (Wheel != null) System.Windows.Automation.AutomationProperties.SetName(this, Wheel.Name + ", " + Wheel.VolumeLabel);
    }
    internal void StartMaximumEffect()
    {
        StopMaximumEffect();Array.Fill(sparkArcLevels,double.NaN);MaximumEffectStarts++;
        maximumClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MaximumVolumeEffect.DurationMilliseconds)){FillBehavior=FillBehavior.Stop}.CreateClock(true);
        ApplyAnimationClock(MaximumEffectProgressProperty,maximumClock);
    }
    private void StopMaximumEffect() {MaximumEmissionCutoff=1;maximumClock?.Controller?.Remove();maximumClock=null;BeginAnimation(MaximumEffectProgressProperty,null);SetValue(MaximumEffectProgressProperty,1d);}
    private void StopSparkEmission()=>MaximumEmissionCutoff=Math.Min(MaximumEmissionCutoff,MaximumEffectProgress+300d/MaximumVolumeEffect.DurationMilliseconds);
    internal static Geometry WheelSilhouette(Rect label)
    {
        Geometry circle=new EllipseGeometry(new Point(60,60),56,56);
        if(label.IsEmpty)return circle;
        var tab=Geometry.Combine(new RectangleGeometry(label,13,13),new RectangleGeometry(new Rect(60,4,13,26)),GeometryCombineMode.Union,null);
        return Geometry.Combine(circle,tab,GeometryCombineMode.Union,null);
    }
    internal void SeekMaximumEffect(TimeSpan time)=>maximumClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    internal void SeekLevelAnimation(TimeSpan time)=>levelClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    internal static bool DisplaysMaximum(double level)=>level>=.995;
    private string LiveNumberText()=>Wheel?.Available==true?(DisplaysMaximum(Wheel.Level)?"MAX":Math.Round(Wheel.Level*100).ToString("0")):"—";
    private void ResetNumberDisplay()
    {
        StopNumberTransition();NumberText=OutgoingNumberText=LiveNumberText();numberLevel=Wheel?.Level??0;
    }
    private void StopNumberTransition() {numberClock?.Controller?.Remove();numberClock=null;BeginAnimation(NumberProgressProperty,null);SetValue(NumberProgressProperty,1d);}
    private void UpdateNumberDisplay()
    {
        var target=LiveNumberText();var level=Wheel?.Level??0;
        var direction=level>=numberLevel?1:-1;numberLevel=level;
        if(target==NumberText)return;
        var previous=NumberText;StopNumberTransition();OutgoingNumberText=previous;NumberText=target;NumberDirection=direction;
        if(Wheel?.Available!=true||previous=="—"||!EffectsEnabled||!SystemParameters.ClientAreaAnimation)return;
        // Replace the current transition rather than queueing numbers during rapid scrolling.
        NumberTransitionStarts++;
        numberClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(NumberTransitionMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop}.CreateClock(true);
        ApplyAnimationClock(NumberProgressProperty,numberClock);
    }
    internal void SeekNumberTransition(TimeSpan time)=>numberClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    private void DrawNumberLabel(DrawingContext dc,string text,double y,double opacity)
    {
        dc.PushOpacity(opacity);
        if(NumberRollLayout.CanSplit(text,text))
            foreach(var glyph in NumberRollLayout.Glyphs(text,text,1))DrawText(dc,glyph.Value.ToString(),22,new Point(glyph.X,y),Brushes.White,true,true);
        else DrawText(dc,text,22,new Point(60,y),Brushes.White,true,true);
        dc.Pop();
    }
    private void DrawNumber(DrawingContext dc)
    {
        var progress=NumberProgress;
        if(progress>=1) {DrawNumberLabel(dc,NumberText,54,1);return;}
        dc.PushClip(new RectangleGeometry(new Rect(22,32,76,43)));
        if(NumberRollLayout.CanSplit(OutgoingNumberText,NumberText))
            foreach(var glyph in NumberRollLayout.Glyphs(OutgoingNumberText,NumberText,progress,NumberDirection))
            {
                dc.PushOpacity(glyph.Opacity);DrawText(dc,glyph.Value.ToString(),22,new Point(glyph.X,54+glyph.OffsetY),Brushes.White,true,true);dc.Pop();
            }
        else
        {
            DrawNumberLabel(dc,OutgoingNumberText,54+NumberDirection*12*progress,1-progress);
            DrawNumberLabel(dc,NumberText,54-NumberDirection*12*(1-progress),progress);
        }
        dc.Pop();
    }
    private void AnimateLevel()
    {
        var target = Wheel?.Level ?? 0;
        if (!SystemParameters.ClientAreaAnimation) {levelClock=null;BeginAnimation(DisplayLevelProperty, null); DisplayLevel = target; return; }
        var current = DisplayLevel;
        levelClock=(AnimationClock)new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(400)) { EasingFunction = new PowerEase { Power = 5, EasingMode = EasingMode.EaseOut } }.CreateClock(true);
        ApplyAnimationClock(DisplayLevelProperty,levelClock);
    }
    private void AnimateScale(double target)
    {
        var scale = RenderTransform as ScaleTransform;
        if (scale == null) { RenderTransformOrigin = new Point(0, .5); RenderTransform = scale = new ScaleTransform(1, 1); }
        var animation = new DoubleAnimation(scale.ScaleX,target, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 500 : 0)) { EasingFunction = new ExponentialEase { Exponent = 10, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    public void Adjust(double delta)=>AdjustInput(delta,Native.ShiftDown,Environment.TickCount64);
    internal void AdjustInput(double delta,bool shift,long now)
    {
        if (Wheel?.Available != true || CanAdjustVolume?.Invoke() == false) return;
        SetPrecisionMode(shift);
        if(shift)
        {
            var before=Wheel.Level;var target=snap.Adjust(delta,before,now);
            if(target.HasValue) {VolumeRequested?.Invoke(Wheel,target.Value);StartShiftFeedback(Wheel.Level-before);}
            return;
        }
        if((Wheel.Level>=Wheel.MaximumVolumePercent/100d&&delta>0)||(Wheel.Level<=0&&delta<0)) {scroll.Reset();return;}
        var steps=scroll.Adjust(delta,ScrollStepPercent,ScrollAccelerationEnabled,now);
        if (steps == 0) return;
        var requested=Wheel.Level+steps*.01;
        VolumeRequested?.Invoke(Wheel,requested);
        // Discard acceleration and fractions consumed by a boundary (including a shared target's stricter cap).
        if(Wheel.Level<=0||Wheel.Level>=Wheel.MaximumVolumePercent/100d||Math.Abs(Wheel.Level-requested)>1e-7)scroll.Reset();
    }
    public void ResetScroll() {scroll.Reset();snap.Reset();}
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (Wheel == null) return;
        var p = e.GetPosition(this);
        if (HitsMute(p)) MuteRequested?.Invoke(Wheel);
        else if (Wheel.Available && CanAdjustVolume?.Invoke() != false) { dragStart = p; dragLevel = Wheel.Level; dragging = true; CaptureMouse(); }
        Selected?.Invoke(Wheel); e.Handled = true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = Wheel;
        if (w == null) return;
        var accent = w.Accent;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        // Move only the drawing: the stationary input surface prevents hover loss while bobbling.
        var spring=CurrentShiftPose;var springMatrix=Matrix.Identity;
        var recovery=Math.Sin(UnmuteProgress*Math.PI)*(1-UnmuteProgress);
        springMatrix.ScaleAt(1+recovery*.04,1+recovery*.04,60,60);
        springMatrix.ScaleAt(1+spring.Stretch,1-spring.Stretch,60,60);
        springMatrix.RotateAt(spring.Angle,60,60);springMatrix.Translate(spring.X,spring.Y);
        dc.PushTransform(new MatrixTransform(springMatrix));
        dc.PushTransform(new TranslateTransform(MuteShake(MuteFeedbackProgress)*Effects.ShakePercent/100d,0));
        var surfaceColor=EffectSettings.ColorOr(w.Muted?Effects.MutedColor:Effects.NormalColor,w.Muted?"#26050D":"#11141B");
        if(!w.Muted&&UnmuteProgress<1)surfaceColor=RecoveryColor(EffectSettings.ColorOr(Effects.MutedColor,"#26050D"),surfaceColor,UnmuteProgress);
        var surface=new SolidColorBrush(surfaceColor);
        FormattedText? label=null;
        if(!Compact)
        {
            label=new FormattedText(string.IsNullOrWhiteSpace(w.Name)?"UNTITLED":w.Name.ToUpperInvariant(),CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),12,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            label.MaxTextWidth=188;label.MaxLineCount=1;label.Trimming=TextTrimming.CharacterEllipsis;
            LabelBounds=new Rect(60,4,Math.Min(232,Math.Max(64,label.Width+44)),26);
        }
        else LabelBounds=Rect.Empty;
        var outline=WheelSilhouette(LabelBounds);
        if(!w.Muted&&recovery>0)
            for(var width=22;width>=4;width-=3)dc.DrawGeometry(null,new Pen(WithOpacity(accent,recovery*.14*Effects.BloomPercent/100d*Math.Exp(-width*width/180d)),width),outline);
        dc.DrawGeometry(surface,null,outline);
        if (active) dc.DrawGeometry(null,new Pen(WithOpacity(accent,.25),1),outline);
        DrawMaximumSparks(dc,accent);
        var arc = Arc(new Point(60, 60), MaximumVolumeEffect.RingRadius, MaximumVolumeEffect.ArcSweepDegrees * MaximumVolumeEffect.RenderedArcLevel(DisplayLevel));
        var glow=MaximumVolumeEffect.Glow(MaximumEffectProgress)*MaximumEffectOpacity*Effects.BloomPercent/100d;
        if (w.Available)
        {
            for(var width=56;width>=2;width-=2)
                dc.DrawGeometry(null,new Pen(WithOpacity(accent,(.006+glow*.10)*Math.Exp(-width*width/600d)),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},arc);
            dc.DrawGeometry(null, new Pen(WithOpacity(accent, Math.Min(1,.85+glow*.15)), 2.3+glow*1.2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round}, arc);
            dc.DrawGeometry(null, new Pen(w.Muted ? WithOpacity(Brushes.White, .35) : Brushes.White, 1.15){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round}, arc);
        }
        if(ShowShiftMarkers&&PrecisionMode&&w.Available)
            foreach(var percent in ShiftVolumeSnap.MarkerLevels(ShiftStepPercent))
            {
                var point=PrecisionMarkerPoint(percent);
                dc.DrawEllipse(surface,null,point,3.8,3.8);
                dc.DrawEllipse(WithOpacity(accent,percent*.01<=w.Level+1e-7?1:.5),null,point,2.2,2.2);
            }
        DrawNumber(dc);
        if (!Compact)
        {
            dc.DrawText(label!,new Point(92,4+(26-label!.Height)/2));
        }
        if(w.Muted)dc.DrawEllipse(new SolidColorBrush(EffectSettings.ColorOr(Effects.MutedButtonColor,"#460715")),null,MuteCenter,13,13);
        // Segoe Fluent Icons is provided by Windows; no third-party artwork is distributed.
        var icon = new FormattedText(w.Muted ? "\uE74F" : "\uE767", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe Fluent Icons"), 13, w.Available ? Brushes.White : Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var iconPosition=new Point(MuteCenter.X-icon.Width/2,MuteCenter.Y-icon.Height/2);
        if(muteHovered)
        {
            var tint=w.Muted?new SolidColorBrush(Color.FromRgb(232,78,104)):accent;
            var glyph=icon.BuildGeometry(iconPosition);
            for(var width=16;width>=2;width-=2)
                dc.DrawGeometry(null,new Pen(WithOpacity(tint,Math.Min(1,.16*Effects.BloomPercent/100d*Math.Exp(-width*width/65d))),width){LineJoin=PenLineJoin.Round},glyph);
        }
        dc.DrawText(icon,iconPosition);
        var muteProgress=MuteFeedbackProgress;
        if(w.Muted&&muteProgress>0&&muteProgress<1)
        {
            var opacity=SlashOpacity(muteProgress);
            var start=new Point(-4,-4);var end=start+new Vector(128,128)*SlashSweep(muteProgress);
            var red=new SolidColorBrush(Color.FromRgb(255,48,80));
            for(var width=10;width>=4;width-=2)dc.DrawLine(new Pen(WithOpacity(red,opacity*.07),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,end);
            dc.DrawLine(new Pen(WithOpacity(red,opacity),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,end);
        }
        dc.Pop();dc.Pop();
    }
    private void DrawMaximumSparks(DrawingContext dc,Brush accent)
    {
        var sparks=MaximumVolumeEffect.SparkOpacity(MaximumEffectProgress)*MaximumEffectOpacity;
        if(Wheel?.Available==true&&sparks>0)
            for(var i=0;i<Effects.SparkCount;i++)
            {
                var spark=MaximumVolumeEffect.Spark(i,MaximumEffectProgress,MaximumEffectStarts,1,Effects.SparkCount,Effects.SparkSpeedPercent/100d);
                var opacity=sparks*spark.Opacity;if(opacity<=0||spark.BirthTime>MaximumEmissionCutoff)continue;
                if(double.IsNaN(sparkArcLevels[i]))sparkArcLevels[i]=Math.Min(Wheel.Level,MaximumVolumeEffect.RenderedArcLevel(DisplayLevel));
                spark=MaximumVolumeEffect.Spark(i,MaximumEffectProgress,MaximumEffectStarts,sparkArcLevels[i],Effects.SparkCount,Effects.SparkSpeedPercent/100d);
                var head=new Point(spark.X,spark.Y);var tail=new Point(spark.TailX,spark.TailY);
                for(var width=6;width>=2;width-=2)
                    dc.DrawLine(new Pen(WithOpacity(accent,Math.Min(1,opacity*.16*Effects.BloomPercent/100d*Math.Exp(-width*width/14d))),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},tail,head);
                dc.DrawLine(new Pen(WithOpacity(accent,opacity),.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},tail,head);
            }
    }
    private static Brush WithOpacity(Brush source, double opacity) { var brush = source.CloneCurrentValue(); brush.Opacity = opacity; return brush; }
    private void DrawText(DrawingContext dc, string value, double size, Point point, Brush brush, bool bold, bool centered, double max = 200)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = max, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new Point(centered ? point.X - text.Width/2 : point.X, centered ? point.Y - text.Height/2 : point.Y));
    }
    private static Geometry Arc(Point center, double radius, double degrees)
    {
        if (degrees <= .001) return Geometry.Empty;
        var end = (MaximumVolumeEffect.ArcStartDegrees + degrees) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var c = geometry.Open()) { c.BeginFigure(new Point(center.X,center.Y+radius),false,false); c.ArcTo(new Point(center.X+Math.Cos(end)*radius,center.Y+Math.Sin(end)*radius),new Size(radius,radius),0,degrees>180,SweepDirection.Clockwise,true,false); }
        geometry.Freeze(); return geometry;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new WheelPeer(this);
    private sealed class WheelPeer : FrameworkElementAutomationPeer, IRangeValueProvider, IToggleProvider
    {
        private readonly WheelControl control;
        public WheelPeer(WheelControl owner) : base(owner) { control = owner; }
        protected override string GetClassNameCore() => "VolumeWheel";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface is PatternInterface.RangeValue or PatternInterface.Toggle ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => control.Wheel?.Available != true;
        public double LargeChange => 5; public double SmallChange => 1; public double Maximum => 100; public double Minimum => 0; public double Value => (control.Wheel?.Level ?? 0) * 100;
        public void SetValue(double value) { if (!IsReadOnly && control.Wheel != null) control.VolumeRequested?.Invoke(control.Wheel, Math.Clamp(value / 100,0,1)); }
        public ToggleState ToggleState => control.Wheel?.Muted == true ? ToggleState.On : ToggleState.Off;
        public void Toggle() { if (control.Wheel != null) control.MuteRequested?.Invoke(control.Wheel); }
    }
}
