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

namespace WaveWheels;

public sealed class WheelControl : FrameworkElement
{
    public static readonly DependencyProperty WheelProperty = DependencyProperty.Register(nameof(Wheel), typeof(Wheel), typeof(WheelControl), new PropertyMetadata(null, OnWheelChanged));
    public static readonly DependencyProperty DisplayLevelProperty = DependencyProperty.Register(nameof(DisplayLevel), typeof(double), typeof(WheelControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public Wheel? Wheel { get => (Wheel?)GetValue(WheelProperty); set => SetValue(WheelProperty, value); }
    public double DisplayLevel { get => (double)GetValue(DisplayLevelProperty); set => SetValue(DisplayLevelProperty, value); }
    public event Action<Wheel>? MuteRequested;
    public event Action<Wheel, double>? VolumeRequested;
    public event Action<Wheel>? Selected;
    public Func<bool>? CanAdjustVolume { get; set; }
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(WheelControl), new PropertyMetadata(false, (d,e) => { var c = (WheelControl)d; c.Width = (bool)e.NewValue ? 120 : 300; c.InvalidateVisual(); }));
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    private bool dragging;
    private Point dragStart;
    private double dragLevel, acceleration = 1, scrollRemainder;
    private long lastScroll;
    private bool active;
    public WheelControl()
    {
        Width = 300; Height = 120; Focusable = true; Cursor = Cursors.Hand;
        MouseEnter += (_, _) => { active = true; Selected?.Invoke(Wheel!); AnimateScale(1.04); InvalidateVisual(); };
        MouseLeave += (_, _) => { active = false; AnimateScale(1); InvalidateVisual(); };
        Unloaded += (_, _) => { if (Wheel != null) Wheel.PropertyChanged -= ModelChanged; };
        Loaded += (_, _) => { if (Wheel != null) { Wheel.PropertyChanged -= ModelChanged; Wheel.PropertyChanged += ModelChanged; AnimateLevel(); } };
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
        control.AnimateLevel(); control.InvalidateVisual();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Wheel.Level)) AnimateLevel();
        InvalidateVisual();
        if (Wheel != null) System.Windows.Automation.AutomationProperties.SetName(this, Wheel.Name + ", " + Wheel.VolumeLabel);
    }
    private void AnimateLevel()
    {
        var target = Wheel?.Level ?? 0;
        if (!SystemParameters.ClientAreaAnimation) { BeginAnimation(DisplayLevelProperty, null); DisplayLevel = target; return; }
        var current = DisplayLevel;
        BeginAnimation(DisplayLevelProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(400)) { EasingFunction = new PowerEase { Power = 5, EasingMode = EasingMode.EaseOut } });
    }
    private void AnimateScale(double target)
    {
        var scale = RenderTransform as ScaleTransform;
        if (scale == null) { RenderTransformOrigin = new Point(0, .5); RenderTransform = scale = new ScaleTransform(1, 1); }
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 500 : 0)) { EasingFunction = new ExponentialEase { Exponent = 10, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    public void Adjust(double delta)
    {
        if (Wheel?.Available != true || CanAdjustVolume?.Invoke() == false) return;
        var now = Environment.TickCount64;
        if (now - lastScroll > 150) acceleration = 1;
        lastScroll = now;
        scrollRemainder += delta * .01 * acceleration;
        acceleration = Math.Min(5, acceleration * 1.8);
        var steps = (int)Math.Truncate((scrollRemainder + Math.Sign(scrollRemainder)*1e-9) / .01);
        if (steps == 0) return;
        scrollRemainder -= steps*.01;
        VolumeRequested?.Invoke(Wheel, Wheel.Level + steps*.01);
    }
    public void ResetScroll() { acceleration = 1; scrollRemainder = 0; lastScroll = 0; }
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
        var arc = Arc(new Point(60, 60), 43, 270 * Math.Clamp(DisplayLevel, 0, 1));
        if (w.Available)
        {
            for (var blur = 10; blur >= 2; blur -= 2) dc.DrawGeometry(null, new Pen(WithOpacity(accent, .035), blur), arc);
            dc.DrawGeometry(null, new Pen(WithOpacity(accent, .85), 2.3), arc);
            dc.DrawGeometry(null, new Pen(w.Muted ? WithOpacity(Brushes.White, .35) : Brushes.White, 1.15), arc);
        }
        DrawText(dc, w.Available ? (DisplayLevel >= .995 ? "MAX" : Math.Round(DisplayLevel * 100).ToString("0")) : "—", 22, new Point(60, 54), Brushes.White, true, true);
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
    private static Brush WithOpacity(Brush source, double opacity) { var brush = source.CloneCurrentValue(); brush.Opacity = opacity; return brush; }
    private void DrawText(DrawingContext dc, string value, double size, Point point, Brush brush, bool bold, bool centered, double max = 200)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = max, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new Point(centered ? point.X - text.Width/2 : point.X, centered ? point.Y - text.Height/2 : point.Y));
    }
    private static Geometry Arc(Point center, double radius, double degrees)
    {
        if (degrees <= .001) return Geometry.Empty;
        var end = (90 + degrees) * Math.PI / 180;
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
