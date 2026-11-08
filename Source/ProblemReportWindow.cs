using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaveWheels;
internal static class ProblemReport
{
    internal static string Diagnostics(string version,string channel,bool windowsMode,bool connected,string protocol,int port,bool performance,bool animations)
    {
        var connection=connected?"Connected":"Disconnected";
        var mode=windowsMode?"Windows Audio (manual)":connected?"Wave Link":"Windows Audio (fallback)";
        // Deliberately excludes logs, device/channel names, executable paths, and user or machine identifiers.
        var knownProtocol=protocol.Contains("3",StringComparison.Ordinal)?"Wave Link 3":protocol.Contains("2",StringComparison.Ordinal)?"Wave Link 2":"Not detected";
        return $"- App: {version}\n- OS: {RuntimeInformation.OSDescription}\n- Architecture: {RuntimeInformation.ProcessArchitecture}\n- Update channel: {channel}\n- Audio mode: {mode}\n- Wave Link: {connection}\n- Protocol: {knownProtocol}\n- Port: {(port==0?"Automatic discovery":port.ToString())}\n- Animations enabled: {animations}\n- Performance mode: {performance}";
    }
    internal static string Template(string diagnostics)=>"## Problem\nDescribe what happened.\n\n## Steps to reproduce\n1. \n\n## Expected behavior\n\n\n## App details\n"+diagnostics;
    internal static Uri DraftUri(string title,string body)
    {
        title=title.Trim();
        if(title.Length==0||title.Length>120||title.Contains('\n')||title.Contains('\r'))throw new InvalidOperationException("Enter a short, single-line title.");
        if(string.IsNullOrWhiteSpace(body))throw new InvalidOperationException("Describe the problem before opening GitHub.");
        var url="https://github.com/notjacobwashere/WaveWheels/issues/new?title="+Uri.EscapeDataString(title)+"&body="+Uri.EscapeDataString(body);
        if(url.Length>7800)throw new InvalidOperationException("This report is too long for a browser draft. Use Copy report, then paste it into a new GitHub issue.");
        return new Uri(url);
    }
}
internal sealed class ProblemReportWindow:Window
{
    internal readonly TextBox Summary=new(){MaxLength=120};
    internal readonly TextBox Report=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=6000};
    private readonly TextBlock status=new(){Foreground=new SolidColorBrush(Color.FromRgb(244,177,111)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    internal ProblemReportWindow(string diagnostics)
    {
        Title="Report a problem";Width=680;Height=680;MinWidth=480;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;DialogAppearance.Apply(this);
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Report a problem",FontSize=23,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text="Review and edit the report before opening a GitHub draft. App and connection details are included; logs and personal identifiers are excluded.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(152,170,188)),Margin=new Thickness(0,10,0,18)});
        top.Children.Add(new TextBlock{Text="Title",Margin=new Thickness(0,0,0,7)});top.Children.Add(Summary);AutomationProperties.SetName(Summary,"Problem title");
        top.Children.Add(new TextBlock{Text="Report",Margin=new Thickness(0,16,0,7)});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(status);
        bottom.Children.Add(new TextBlock{Text="Nothing is submitted until you submit the issue on GitHub.",Foreground=new SolidColorBrush(Color.FromRgb(152,170,188)),FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,10)});
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();buttons.Children.Add(cancel);
        var copy=new Button{Content="Copy report"};copy.Click+=(_,_)=> {try{Clipboard.SetText(Summary.Text.Trim()+"\n\n"+Report.Text);status.Text="Report copied.";}catch(Exception ex)when(ex is ExternalException or System.Threading.ThreadStateException){status.Text="Could not copy the report. Select and copy the text manually.";}};buttons.Children.Add(copy);
        var open=new Button{Content="Open GitHub draft",Background=new SolidColorBrush(Color.FromRgb(115,220,200)),Foreground=new SolidColorBrush(Color.FromRgb(16,35,31)),IsEnabled=false};
        open.Click+=(_,_)=>OpenDraft(uri=>Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true}));buttons.Children.Add(open);
        Summary.TextChanged+=(_,_)=>{open.IsEnabled=!string.IsNullOrWhiteSpace(Summary.Text);status.Text="";};Report.TextChanged+=(_,_)=>status.Text="";Report.Text=ProblemReport.Template(diagnostics);AutomationProperties.SetName(Report,"Editable problem report");root.Children.Add(Report);
        Loaded+=(_,_)=>Summary.Focus();
    }
    internal void OpenDraft(Action<Uri> launch)
    {
        try {var uri=ProblemReport.DraftUri(Summary.Text,Report.Text);launch(uri);status.Text="GitHub draft opened. Review and submit it in your browser.";}
        catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){status.Text=ex.Message;}
    }
}
