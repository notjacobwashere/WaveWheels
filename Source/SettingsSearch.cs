using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WaveWheels;

internal sealed record SettingSearchEntry(string Title,string Section,string Target,string Keywords)
{
    public string Location=>Section switch {"Effects"=>"Appearance / Effects","Placement"=>"Appearance / Placement","Settings"=>"Settings / Controls","Startup"=>"Settings / Startup","Connection"=>"Settings / Connection",_=>Section};
}
internal static class SettingsSearch
{
    internal static readonly SettingSearchEntry[] Entries={
        new("Wheel size","Settings","WheelSizeSlider","scale zoom bigger smaller"),
        new("Animations","Settings","Animate wheels","motion enable disable toggle"),
        new("Shift increment dots","Settings","ShowShiftMarkersToggle","markers precision step"),
        new("Shift volume step","Settings","ShiftStepSlider","increment precision faster"),
        new("Scroll volume step","Settings","ScrollStepSlider","increment wheel sensitivity"),
        new("Scroll acceleration","Settings","Accelerate fast scrolling","speed sensitivity"),
        new("Wheel hide delay","Settings","HideDelaySlider","timeout idle disappear"),
        new("Dedicated mute button","Settings","UseMuteButtonToggle","speaker click unmute"),
        new("Application exclusions","Settings","BlacklistEnabledToggle","blacklist games pause disable"),
        new("Start with Windows","Startup","Start with Windows","startup boot sign-in launch"),
        new("Performance mode","Effects","PerformanceModeToggle","animations off cpu gpu motion"),
        new("Individual animations","Effects","Animations","hover opening closing smooth numbers bob shift mute unmute limit click"),
        new("Opening and closing animation","Effects","Opening and closing","staggered slide grow motion overlay"),
        new("Page and button animations","Effects","Page and button transitions","interface motion navigation press"),
        new("Hover animation","Effects","Hover feedback","highlight outline motion"),
        new("Smooth volume animation","Effects","Smooth volume bar","arc animation motion"),
        new("Sliding numbers","Effects","Sliding numbers","digits animation percentage roll"),
        new("Volume bob animation","Effects","Volume bob","bounce animation"),
        new("Shift spring animation","Effects","Shift spring","bounce jiggle animation"),
        new("Mute animation","Effects","Mute shake and slash","red shake animation"),
        new("Unmute animation","Effects","Unmute recovery","recovery animation"),
        new("Volume-limit animation","Effects","Volume-limit recoil","recoil maximum overshoot animation"),
        new("Click animation","Effects","Click feedback","tap press animation"),
        new("Maximum animation","Effects","Maximum glow and sparks","bloom particles animation"),
        new("Glow intensity","Effects","Bloom intensity","bloom brightness"),
        new("Spark density","Effects","Spark density","sparks particles maximum count"),
        new("Spark speed","Effects","Spark speed","sparks particles maximum velocity"),
        new("Muted colors","Effects","MutedColorBox","background red colour hex"),
        new("Muted button color","Effects","MutedButtonColorBox","red speaker colour hex"),
        new("Wheel background color","Effects","NormalColorBox","colour hex surface"),
        new("Wheel color","Wheels","ColorBox","colour accent hex picker"),
        new("Left-click action","Wheels","LeftClickSection","mute output volume click"),
        new("Right-click action","Wheels","RightClickSection","mute output volume click"),
        new("Wheel count","Wheels","CountBox","add remove number wheels"),
        new("Wheel label","Wheels","Wheel name","rename title name"),
        new("Wave Link input channel","Wheels","ChannelBox","bind connection input"),
        new("Mix scope","Wheels","MixBox","monitor stream personal mix"),
        new("Undo wheel deletion","Wheels","UndoWheelDeletionButton","restore remove recover"),
        new("Profiles","Profiles","ProfilesPage","automatic applications layouts import export"),
        new("Screen and edge","Placement","Screen and edge","left right top bottom display monitor"),
        new("Position along edge","Placement","Position along edge","vertical horizontal offset center placement"),
        new("Display","Placement","MonitorBox","screen monitor"),
        new("Distance from screen edge","Placement","EdgeDistanceSlider","position inset placement"),
        new("Wheel spacing","Placement","Wheel spacing","gap distance layout"),
        new("Windows Audio mode","Connection","ConnectionPage","manual fallback wave link connect reconnect locate start"),
        new("Connection port","Connection","Local port override","advanced wave link reconnect"),
        new("Release channel","Updates","UpdateChannelPicker","beta stable updates"),
        new("Automatic updates","Updates","Automatic updates","startup install download"),
        new("Update notifications","Updates","Notify me about updates","notification alerts"),
        new("Repair app","Updates","Repair app","reinstall stable install"),
        new("Release notes","Updates","ReleaseNotesPanel","changelog changes new version"),
        new("Version history","Updates","VersionHistory","beta archive older downgrade stable"),
    };
    internal static SettingSearchEntry[] Find(string query)
    {
        var terms=query.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(terms.Length==0)return Array.Empty<SettingSearchEntry>();
        return Entries.Where(e=>terms.All(t=>(e.Title+" "+e.Location+" "+e.Target+" "+e.Keywords).Contains(t,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e=>e.Title.Contains(query.Trim(),StringComparison.OrdinalIgnoreCase)).ThenBy(e=>e.Title).ToArray();
    }
    internal static FrameworkElement? FindTarget(DependencyObject root,string target)
    {
        if(root is FrameworkElement element&&(element.Name==target||AutomationProperties.GetName(element)==target||element is ContentControl c&&Equals(c.Content,target)||element is Expander e&&Equals(e.Header,target)))return element;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())if(FindTarget(child,target) is {} found)return found;
        return null;
    }
}
internal sealed class SettingsSearchWindow:Window
{
    internal SettingSearchEntry? SelectedSetting {get;private set;}
    internal readonly TextBox Query=new(){MaxLength=100};
    internal readonly ListBox Results=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly TextBlock status=new(){Foreground=new SolidColorBrush(Color.FromRgb(152,170,188)),Margin=new Thickness(0,10,0,12)};
    internal SettingsSearchWindow()
    {
        Title="Search settings";Width=590;Height=510;MinWidth=440;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        DialogAppearance.Apply(this);
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Search settings",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)});
        top.Children.Add(Query);AutomationProperties.SetName(Query,"Search settings");top.Children.Add(status);
        var template=new DataTemplate(typeof(SettingSearchEntry));
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(MarginProperty,new Thickness(8,6,8,6));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Title"));title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);row.AppendChild(title);
        var location=new FrameworkElementFactory(typeof(TextBlock));location.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Location"));location.SetValue(TextBlock.FontSizeProperty,12d);location.SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color.FromRgb(152,170,188)));location.SetValue(MarginProperty,new Thickness(0,5,0,0));row.AppendChild(location);
        template.VisualTree=row;Results.ItemTemplate=template;root.Children.Add(Results);
        Query.TextChanged+=(_,_)=> {var results=SettingsSearch.Find(Query.Text);Results.ItemsSource=results;Results.SelectedIndex=results.Length>0?0:-1;status.Text=string.IsNullOrWhiteSpace(Query.Text)?"Search by setting name or keyword.":results.Length==0?"No matching settings.":$"{results.Length} {(results.Length==1?"result":"results")} · Enter to open";};
        Query.KeyDown+=(_,e)=> {if(e.Key==Key.Down&&Results.Items.Count>0){Results.Focus();e.Handled=true;}else if(e.Key==Key.Enter){Choose();e.Handled=true;}};
        Results.KeyDown+=(_,e)=> {if(e.Key==Key.Enter){Choose();e.Handled=true;}};
        Results.MouseDoubleClick+=(_,e)=> {if(Results.SelectedItem!=null&&ItemsControl.ContainerFromElement(Results,e.OriginalSource as DependencyObject)!=null)Choose();};
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.Escape){Close();e.Handled=true;}};
        Loaded+=(_,_)=>Query.Focus();status.Text="Search by setting name or keyword.";
    }
    private void Choose(){if(Results.SelectedItem is SettingSearchEntry entry){SelectedSetting=entry;DialogResult=true;}}
}
internal static class DialogAppearance
{
    internal static void Apply(Window window)
    {
        window.ShowInTaskbar=false;
        window.Icon=BitmapFrame.Create(new Uri("pack://application:,,,/WaveWheels;component/Assets/WaveWheels.ico"));
        window.SourceInitialized+=(_,_)=> {try{var dark=1;var corners=2;var handle=new WindowInteropHelper(window).Handle;Native.DwmSetWindowAttribute(handle,20,ref dark,4);Native.DwmSetWindowAttribute(handle,33,ref corners,4);}catch{}};
        window.Background=new SolidColorBrush(Color.FromRgb(16,24,32));window.Foreground=new SolidColorBrush(Color.FromRgb(233,240,245));window.FontFamily=new FontFamily("Segoe UI");window.FontSize=14;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WaveWheels;component/Theme.xaml",UriKind.Relative)});
    }
}
