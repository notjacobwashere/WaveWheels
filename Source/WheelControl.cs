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
    public static readonly DependencyProperty DisplayLevelProperty = DependencyProperty.Register(nameof(DisplayLevel), typeof(double), typeof(WheelControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender,(d,e)=>((WheelControl)d).FinishMaximumArrival()));
    public Wheel? Wheel { get => (Wheel?)GetValue(WheelProperty); set => SetValue(WheelProperty, value); }
    public double DisplayLevel { get => (double)GetValue(DisplayLevelProperty); set => SetValue(DisplayLevelProperty, value); }
    internal static readonly DependencyProperty MaximumEffectProgressProperty=DependencyProperty.Register("MaximumEffectProgress",typeof(double),typeof(WheelControl),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EffectsEnabledProperty=DependencyProperty.Register(nameof(EffectsEnabled),typeof(bool),typeof(WheelControl),new PropertyMetadata(true,(d,e)=> {if(!(bool)e.NewValue) {var control=(WheelControl)d;control.StopMaximumEffect();control.StopNumberTransition();}}));
    public bool EffectsEnabled {get=>(bool)GetValue(EffectsEnabledProperty);set=>SetValue(EffectsEnabledProperty,value);}
    private readonly MaximumVolumeTransition maximumTransition=new();
    private AnimationClock? maximumClock;
    private AnimationClock? levelClock;
    private bool maximumPending;
    internal double MaximumEffectProgress=>(double)GetValue(MaximumEffectProgressProperty);
    internal int MaximumEffectStarts {get;private set;}
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
    public WheelControl()
    {
        Width = 300; Height = 120; Focusable = true; Cursor = Cursors.Hand;
        precisionTimer.Tick+=(_,_)=>SetPrecisionMode(active&&Native.ShiftDown);
        MouseEnter += (_, _) => { active = true;SetPrecisionMode(Native.ShiftDown);precisionTimer.Start();Selected?.Invoke(Wheel!); AnimateScale(1.04); InvalidateVisual(); };
        MouseLeave += (_, _) => { active = false;precisionTimer.Stop();SetPrecisionMode(false);AnimateScale(1); InvalidateVisual(); };
        Unloaded += (_, _) => { active=false;precisionTimer.Stop();SetPrecisionMode(false);StopMaximumEffect();StopNumberTransition();if (Wheel != null) Wheel.PropertyChanged -= ModelChanged; };
        Loaded += (_, _) => { if (Wheel != null) { Wheel.PropertyChanged -= ModelChanged; Wheel.PropertyChanged += ModelChanged; maximumTransition.Reset(Wheel.Available,Wheel.Level);ResetNumberDisplay();AnimateLevel(); } };
        MouseLeftButtonDown += Down;
        MouseMove += (_, e) => { if (dragging && Wheel?.Available == true && CanAdjustVolume?.Invoke() != false) VolumeRequested?.Invoke(Wheel, dragLevel - (e.GetPosition(this).Y - dragStart.Y) / 200); };
        MouseLeftButtonUp += (_, e) => { if (dragging) { dragging = false; ReleaseMouseCapture(); e.Handled = true; } };
        LostMouseCapture += (_, _) => dragging = false;
        MouseWheel += (_, e) => { if (Wheel?.Available == true) { Adjust(e.Delta / 120d); e.Handled = true; } };
        KeyDown += (_, e) => { if (Wheel == null) return; if (e.Key is Key.Up or Key.Right) { Adjust(1); e.Handled = true; } else if (e.Key is Key.Down or Key.Left) { Adjust(-1); e.Handled = true; } else if (e.Key is Key.M or Key.Space) { MuteRequested?.Invoke(Wheel); e.Handled = true; } };
    }
    private static void OnWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (WheelControl)d;
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
            if(Wheel?.Level<1)maximumPending=false;
            if(maximumTransition.Observe(Wheel?.Available==true,Wheel?.Level??0,EffectsEnabled&&SystemParameters.ClientAreaAnimation))maximumPending=true;
            AnimateLevel();
        }
        else if(e.PropertyName==nameof(Wheel.Available)) {ResetNumberDisplay();maximumTransition.Reset(Wheel?.Available==true,Wheel?.Level??0);if(Wheel?.Available!=true)StopMaximumEffect();}
        InvalidateVisual();
        if (Wheel != null) System.Windows.Automation.AutomationProperties.SetName(this, Wheel.Name + ", " + Wheel.VolumeLabel);
    }
    internal void StartMaximumEffect()
    {
        StopMaximumEffect();MaximumEffectStarts++;
        maximumClock=(AnimationClock)new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MaximumVolumeEffect.DurationMilliseconds)){FillBehavior=FillBehavior.Stop}.CreateClock(true);
        ApplyAnimationClock(MaximumEffectProgressProperty,maximumClock);
    }
    private void StopMaximumEffect() {maximumPending=false;maximumClock?.Controller?.Remove();maximumClock=null;BeginAnimation(MaximumEffectProgressProperty,null);SetValue(MaximumEffectProgressProperty,1d);}
    internal void SeekMaximumEffect(TimeSpan time)=>maximumClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    internal void SeekLevelAnimation(TimeSpan time)=>levelClock?.Controller?.SeekAlignedToLastTick(time,TimeSeekOrigin.BeginTime);
    private void FinishMaximumArrival()
    {
        if(maximumPending&&MaximumVolumeEffect.RenderedArcLevel(DisplayLevel)==1&&Wheel?.Available==true&&Wheel.Level>=1&&EffectsEnabled&&SystemParameters.ClientAreaAnimation)
        {maximumPending=false;StartMaximumEffect();}
    }
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
        FinishMaximumArrival();
    }
    private void AnimateScale(double target)
    {
        var scale = RenderTransform as ScaleTransform;
        if (scale == null) { RenderTransformOrigin = new Point(0, .5); RenderTransform = scale = new ScaleTransform(1, 1); }
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 500 : 0)) { EasingFunction = new ExponentialEase { Exponent = 10, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    public void Adjust(double delta)=>AdjustInput(delta,Native.ShiftDown,Environment.TickCount64);
    internal void AdjustInput(double delta,bool shift,long now)
    {
        if (Wheel?.Available != true || CanAdjustVolume?.Invoke() == false) return;
        SetPrecisionMode(shift);
        if(shift) {var target=snap.Adjust(delta,Wheel.Level,now);if(target.HasValue)VolumeRequested?.Invoke(Wheel,target.Value);return;}
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
        if ((p - new Point(60, 88)).Length < 17) MuteRequested?.Invoke(Wheel);
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
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(17, 20, 27)), null, new Point(60, 60), 56, 56);
        if (active) dc.DrawEllipse(null, new Pen(accent.CloneCurrentValue(), 1) { Brush = WithOpacity(accent, .18) }, new Point(60,60),57,57);
        var arc = Arc(new Point(60, 60), MaximumVolumeEffect.RingRadius, MaximumVolumeEffect.ArcSweepDegrees * MaximumVolumeEffect.RenderedArcLevel(DisplayLevel));
        var glow=MaximumVolumeEffect.Glow(MaximumEffectProgress);
        if (w.Available)
        {
            for (var blur = 10; blur >= 2; blur -= 2) dc.DrawGeometry(null, new Pen(WithOpacity(accent, .035+glow*.16), blur+glow*10), arc);
            dc.DrawGeometry(null, new Pen(WithOpacity(accent, .85+glow*.15), 2.3+glow*1.2), arc);
            dc.DrawGeometry(null, new Pen(w.Muted ? WithOpacity(Brushes.White, .35) : Brushes.White, 1.15), arc);
        }
        if(ShowShiftMarkers&&PrecisionMode&&w.Available)
            foreach(var percent in ShiftVolumeSnap.MarkerLevels(ShiftStepPercent))
            {
                var point=PrecisionMarkerPoint(percent);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(17,20,27)),null,point,3.8,3.8);
                dc.DrawEllipse(WithOpacity(accent,percent*.01<=w.Level+1e-7?1:.5),null,point,2.2,2.2);
            }
        DrawMaximumSparks(dc,accent);
        DrawNumber(dc);
        if (!Compact)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17,20,27)), null, new Rect(124, 42, 168, 26),13,13);
            DrawText(dc, string.IsNullOrWhiteSpace(w.Name) ? "UNTITLED" : w.Name.ToUpperInvariant(), 12, new Point(208,55), Brushes.White, true, true, 145);
            DrawText(dc, w.Available ? (w.Muted ? "MUTED" : "LIVE") : "UNAVAILABLE", 9, new Point(208,80), WithOpacity(accent,.9), true, true);
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(17,20,27)), new Pen(WithOpacity(accent,w.Muted ? .9 : .2),1),new Point(60,88),14,14);
        // Segoe Fluent Icons is provided by Windows; no third-party artwork is distributed.
        var icon = new FormattedText(w.Muted ? "\uE74F" : "\uE767", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe Fluent Icons"), 13, w.Available ? Brushes.White : Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(icon,new Point(60-icon.Width/2,88-icon.Height/2));
    }
    private void DrawMaximumSparks(DrawingContext dc,Brush accent)
    {
        var sparks=MaximumVolumeEffect.SparkOpacity(MaximumEffectProgress);
        if(Wheel?.Available==true&&sparks>0)
            for(var i=0;i<MaximumVolumeEffect.SparkCount;i++)
            {
                var spark=MaximumVolumeEffect.Spark(i,MaximumEffectProgress,MaximumEffectStarts);var head=new Point(spark.X,spark.Y);var tail=new Point(spark.TailX,spark.TailY);
                var opacity=sparks*spark.Opacity;if(opacity<=0)continue;
                var tip=tail+(head-tail)*.65;
                dc.DrawLine(new Pen(WithOpacity(accent,opacity*.09),8){StartLineCap=PenLineCap.Flat,EndLineCap=PenLineCap.Flat},tail,head);
                dc.DrawLine(new Pen(WithOpacity(accent,opacity*.22),4){StartLineCap=PenLineCap.Flat,EndLineCap=PenLineCap.Flat},tail,head);
                dc.DrawLine(new Pen(WithOpacity(accent,opacity*.5),2.2){StartLineCap=PenLineCap.Flat,EndLineCap=PenLineCap.Flat},tail,head);
                dc.DrawLine(new Pen(WithOpacity(accent,opacity*.9),1.1){StartLineCap=PenLineCap.Flat,EndLineCap=PenLineCap.Flat},tail,head);
                dc.DrawLine(new Pen(WithOpacity(Brushes.White,opacity*.95),.55){StartLineCap=PenLineCap.Flat,EndLineCap=PenLineCap.Flat},tip,head);
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
