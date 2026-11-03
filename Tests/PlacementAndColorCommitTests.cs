using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WaveWheels;

internal static class PlacementAndColorCommitTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var saved=new ConfigurationPlacement("saved-monitor",120,80,1100,760,96,true);
        var work=new Rect(-2560,0,2560,1440);var fit=saved.Fit(work,96);
        Assert(fit==new Rect(-2440,80,1100,760),"Window restoration must retain monitor-relative location and size.");
        var scaled=saved.Fit(new Rect(1920,0,3840,2160),144);Assert(scaled.Width==1650&&scaled.Height==1140&&scaled.X==2100&&scaled.Y==120,"Window placement must scale with display DPI without choosing a different display.");
        var small=saved.Fit(new Rect(0,0,800,600),144);Assert(small.Left>=0&&small.Top>=0&&small.Right<=800&&small.Bottom<=600,"Changed resolutions must keep the restored window reachable.");
        Assert(!(saved with{X=double.NaN}).Valid&&!(saved with{Width=-1}).Valid,"Invalid saved geometry must not be used.");
        var store=new SettingsStore(Path.Combine(directory,"placement-and-color"));store.Save(new Preferences{MonitorId="saved-overlay-monitor",ScreenEdge="Right",PositionPercent=31,EdgeDistance=46,WheelSpacing=12});
        var model=new MainViewModel(store,true);model.RememberConfigurationPlacement(saved);model.Save();
        var read=store.Load();Assert(read.ConfigurationPlacement==saved&&read.MonitorId=="saved-overlay-monitor"&&read.ScreenEdge=="Right"&&read.PositionPercent==31&&read.EdgeDistance==46&&read.WheelSpacing==12,"Saving configuration window placement must not reset wheel placement or monitor preferences.");
        model.Update(new MixerSnapshot(true,"Fixture","Wave Link 3",Array.Empty<Channel>(),Array.Empty<Mix>()));
        var window=new SettingsWindow(model);var box=(TextBox)window.FindName("ColorBox");box.DataContext=model;
        var appearance=(StackPanel)((FrameworkElement)((FrameworkElement)box.Parent).Parent).Parent;
        var palette=(System.Windows.Controls.Primitives.UniformGrid)((Grid)((FrameworkElement)box.Parent).Parent).Children[0];
        var before=model.EditorWheel!.Color;((Button)palette.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(model.EditorWheel.Color==before&&box.Text=="#DA5DAD","Preset selection must stage a color without changing the wheel.");
        box.SetCurrentValue(TextBox.TextProperty,"#123456");box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Assert(model.EditorWheel.Color==before,"Leaving the wheel hex field must not apply it.");
        appearance.Children.OfType<Button>().Single(b=>b.Content?.ToString()=="Apply color").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(model.EditorWheel.Color=="#123456","The Apply color button must commit the staged wheel color.");
        foreach(var name in new[]{"NormalColor","MutedColor","MutedButtonColor"})
        {
            var property=typeof(EffectSettings).GetProperty(name)!;var field=(TextBox)window.FindName(name+"Box");field.DataContext=model;
            var old=(string)property.GetValue(model.Effects)!;field.SetCurrentValue(TextBox.TextProperty,"#654321");field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Assert((string)property.GetValue(model.Effects)! ==old,"Effect hex fields must not update live or apply on focus loss.");
            ((StackPanel)((FrameworkElement)field.Parent).Parent).Children.OfType<Button>().Single(b=>b.Tag?.ToString()==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert((string)property.GetValue(model.Effects)! =="#654321","Each effect Apply color button must commit its own field.");
        }
        model.Dispose();
        Console.WriteLine("PASS: saved configuration geometry/DPI/work-area recovery, independent wheel placement persistence, staged preset/hex colors, no focus-loss commit, and explicit Apply color actions.");
    }
}
