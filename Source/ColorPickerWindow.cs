using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WaveWheels;

internal sealed class ColorPickerWindow : Window
{
    private readonly Border swatch=new(){Width=54,Height=54,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,16,0)};
    private readonly TextBox hex=new(){MaxLength=7};
    private readonly TextBlock rgb=new(){Foreground=Brushes.White,Margin=new Thickness(0,0,0,8)};
    private readonly TextBlock error=new(){Foreground=new SolidColorBrush(Color.FromRgb(241,141,147)),FontSize=12,Margin=new Thickness(0,8,0,0)};
    private readonly Slider hue=new(){Minimum=0,Maximum=360,Margin=new Thickness(0,12,0,8)};
    private readonly Slider[] channels=new Slider[3];
    private readonly TextBox[] numbers=new TextBox[3];
    private readonly ColorPlane plane=new();
    private bool syncing;
    private double saturation,value,hueValue;
    internal string SelectedHex {get;private set;}="#FFFFFF";
    internal ColorPickerWindow(string initial)
    {
        Title="Choose color";Width=448;Height=680;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(16,24,32));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=14;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WaveWheels;component/Theme.xaml",UriKind.Relative)});
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();footer.Children.Add(cancel);
        var apply=new Button{Content="Apply color",Background=new SolidColorBrush(Color.FromRgb(115,220,200)),Foreground=new SolidColorBrush(Color.FromRgb(16,35,31))};
        apply.Click+=(_,_)=> {if(CommitHex(hex.Text))DialogResult=true;};footer.Children.Add(apply);
        var body=new StackPanel();root.Children.Add(body);
        body.Children.Add(new TextBlock{Text="Choose color",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,20)});
        var top=new DockPanel();body.Children.Add(top);DockPanel.SetDock(swatch,Dock.Left);top.Children.Add(swatch);
        var values=new StackPanel();top.Children.Add(values);values.Children.Add(rgb);values.Children.Add(hex);
        hex.LostKeyboardFocus+=(_,_)=>CommitHex(hex.Text);hex.KeyDown+=(_,e)=> {if(e.Key==Key.Enter){CommitHex(hex.Text);e.Handled=true;}};
        body.Children.Add(error);
        for(var i=0;i<3;i++)
        {
            var index=i;var row=new DockPanel{Margin=new Thickness(0,10,0,0)};body.Children.Add(row);
            var label=new TextBlock{Text=new[]{"R","G","B"}[i],Width=24,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(label,Dock.Left);row.Children.Add(label);
            numbers[i]=new TextBox{Width=54,MaxLength=3,Padding=new Thickness(8,6,8,6),Margin=new Thickness(0,0,12,0)};
            System.Windows.Automation.AutomationProperties.SetName(numbers[i],new[]{"Red","Green","Blue"}[i]);
            DockPanel.SetDock(numbers[i],Dock.Left);row.Children.Add(numbers[i]);
            channels[i]=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(channels[i]);
            channels[i].ValueChanged+=(_,_)=> {if(!syncing)SetRgb((byte)channels[0].Value,(byte)channels[1].Value,(byte)channels[2].Value);};
            void CommitChannel() {if(byte.TryParse(numbers[index].Text,out var n)){channels[index].Value=n;Refresh();}else {error.Text="Enter an RGB value from 0 to 255.";}}
            numbers[i].LostKeyboardFocus+=(_,_)=>CommitChannel();numbers[i].KeyDown+=(_,e)=>{if(e.Key==Key.Enter){CommitChannel();e.Handled=true;}};
        }
        plane.Height=188;plane.Margin=new Thickness(0,20,0,0);body.Children.Add(plane);
        plane.Picked+=(s,v)=>SetHsv(hueValue,s,v);
        body.Children.Add(hue);System.Windows.Automation.AutomationProperties.SetName(hue,"Hue");
        var rainbow=new LinearGradientBrush();var stops=new[]{Colors.Red,Colors.Yellow,Colors.Lime,Colors.Cyan,Colors.Blue,Colors.Magenta,Colors.Red};
        for(var i=0;i<stops.Length;i++)rainbow.GradientStops.Add(new GradientStop(stops[i],i/6d));
        hue.Background=rainbow;
        var template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'><Grid Height='24'><Border Height='12' CornerRadius='4' Background='{TemplateBinding Background}'/><Track x:Name='PART_Track' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Opacity='0'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='12' Height='22'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#404C59' BorderBrush='White' BorderThickness='2' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Opacity='0'/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>");
        hue.Template=template;hue.IsMoveToPointEnabled=true;
        foreach(var channel in channels){channel.Template=template;channel.IsMoveToPointEnabled=true;}
        hue.ValueChanged+=(_,_)=>{if(!syncing)SetHsv(hue.Value,saturation,value);};
        body.Children.Add(new TextBlock{Text="Hue",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(152,170,188))});
        CommitHex(EffectSettings.ValidColor(initial)?initial:"#FFFFFF");
    }
    internal bool CommitHex(string text)
    {
        text=text.Trim();if(!text.StartsWith('#'))text="#"+text;
        if(!EffectSettings.ValidColor(text)){error.Text="Use a six-digit hex color, such as #36BBD3.";return false;}
        var color=(Color)ColorConverter.ConvertFromString(text);SetRgb(color.R,color.G,color.B);return true;
    }
    internal void SetRgb(byte r,byte g,byte b)
    {
        var max=Math.Max(r,Math.Max(g,b))/255d;var min=Math.Min(r,Math.Min(g,b))/255d;var delta=max-min;
        if(delta>0) {var rr=r/255d;var gg=g/255d;var bb=b/255d;hueValue=60*(max==rr?((gg-bb)/delta+6)%6:max==gg?(bb-rr)/delta+2:(rr-gg)/delta+4);}
        saturation=max==0?0:delta/max;value=max;SelectedHex=$"#{r:X2}{g:X2}{b:X2}";Refresh();
    }
    internal void SetHsv(double h,double s,double v)
    {
        hueValue=Math.Clamp(h,0,360);saturation=Math.Clamp(s,0,1);value=Math.Clamp(v,0,1);
        var c=FromHsv(hueValue,saturation,value);SelectedHex=$"#{c.R:X2}{c.G:X2}{c.B:X2}";Refresh();
    }
    internal static Color FromHsv(double h,double s,double v)
    {
        h=((h%360)+360)%360/60;s=Math.Clamp(s,0,1);v=Math.Clamp(v,0,1);var c=v*s;var x=c*(1-Math.Abs(h%2-1));var m=v-c;
        var (r,g,b)=h switch {<1=>(c,x,0d),<2=>(x,c,0d),<3=>(0d,c,x),<4=>(0d,x,c),<5=>(x,0d,c),_=>(c,0d,x)};
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    private void Refresh()
    {
        syncing=true;try {
            var c=(Color)ColorConverter.ConvertFromString(SelectedHex);swatch.Background=new SolidColorBrush(c);hex.Text=SelectedHex;
            rgb.Text=$"rgb({c.R}, {c.G}, {c.B})";error.Text="";hue.Value=hueValue;
            var values=new[]{c.R,c.G,c.B};for(var i=0;i<3;i++){channels[i].Value=values[i];numbers[i].Text=values[i].ToString(CultureInfo.InvariantCulture);}
            channels[0].Background=new LinearGradientBrush(Color.FromRgb(0,c.G,c.B),Color.FromRgb(255,c.G,c.B),0);
            channels[1].Background=new LinearGradientBrush(Color.FromRgb(c.R,0,c.B),Color.FromRgb(c.R,255,c.B),0);
            channels[2].Background=new LinearGradientBrush(Color.FromRgb(c.R,c.G,0),Color.FromRgb(c.R,c.G,255),0);
            plane.Hue=hueValue;plane.Saturation=saturation;plane.Value=value;plane.InvalidateVisual();
        }finally{syncing=false;}
    }
    private sealed class ColorPlane : FrameworkElement
    {
        internal double Hue,Saturation,Value;
        internal event Action<double,double>? Picked;
        internal ColorPlane()
        {
            Cursor=Cursors.Cross;
            MouseLeftButtonDown+=(_,e)=>{CaptureMouse();Pick(e.GetPosition(this));e.Handled=true;};
            MouseMove+=(_,e)=>{if(IsMouseCaptured)Pick(e.GetPosition(this));};
            MouseLeftButtonUp+=(_,_)=>ReleaseMouseCapture();
        }
        private void Pick(Point p)=>Picked?.Invoke(Math.Clamp(p.X/Math.Max(1,ActualWidth),0,1),1-Math.Clamp(p.Y/Math.Max(1,ActualHeight),0,1));
        protected override void OnRender(DrawingContext dc)
        {
            var rect=new Rect(0,0,ActualWidth,ActualHeight);dc.DrawRectangle(new LinearGradientBrush(Colors.White,FromHsv(Hue,1,1),0),null,rect);
            dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,rect);
            var position=new Point(Saturation*ActualWidth,(1-Value)*ActualHeight);
            dc.DrawEllipse(null,new Pen(Brushes.Black,3),position,5,5);dc.DrawEllipse(null,new Pen(Brushes.White,1.5),position,5,5);
        }
    }
}
