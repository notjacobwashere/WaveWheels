using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaveWheels;

internal static class QualityOfLifeTests
{
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    internal static void Run(string directory)
    {
        var store=new SettingsStore(Path.Combine(directory,"qol-"+Guid.NewGuid().ToString("N")));
        var model=new MainViewModel(store,true);model.Update(new(true,"Fixture","Wave Link 3",Array.Empty<Channel>(),Array.Empty<Mix>()));
        var window=new SettingsWindow(model);
        foreach(var entry in SettingsSearch.Entries)
        {
            var page=(FrameworkElement)window.FindName(entry.Section+"Page");
            Assert(SettingsSearch.FindTarget(page,entry.Target)!=null,"Search target must exist: "+entry.Title+" / "+entry.Target);
        }
        Assert(SettingsSearch.Find("shift dots").Single().Target=="ShowShiftMarkersToggle","Multiple search terms must find the Shift dot setting.");
        Assert(SettingsSearch.Find("sparks").Length>=2,"Common plural search terms must find spark controls.");
        Assert(SettingsSearch.Find("SPARK density").First().Title=="Spark density"&&SettingsSearch.Find("boot").Any(e=>e.Section=="Startup"),"Search must support case-insensitive names and common keywords.");
        Assert(SettingsSearch.Find("   ").Length==0&&SettingsSearch.Find("no-such-setting").Length==0,"Blank and unmatched queries must not return unrelated settings.");
        window.NavigateToSetting(SettingsSearch.Find("shift dots").Single());Flush();
        Assert(((FrameworkElement)window.FindName("SettingsPage")).Visibility==Visibility.Visible,"A search result must navigate to the real page.");
        for(DependencyObject? node=(FrameworkElement)window.FindName("ShowShiftMarkersToggle");node!=null;node=LogicalTreeHelper.GetParent(node))if(node is Expander expander)Assert(expander.IsExpanded,"Search navigation must expand the target section.");
        var notesPanel=(Expander)window.FindName("ReleaseNotesPanel");var notesButton=(Button)window.FindName("ReleaseNotesToggleButton");Flush();
        Assert(!notesPanel.IsExpanded&&notesPanel.Visibility==Visibility.Collapsed,"Release notes must occupy no space when closed.");
        notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
        Assert(notesPanel.IsExpanded&&notesPanel.Visibility==Visibility.Visible&&notesButton.Content?.ToString()=="Hide release notes","Release notes button must open the section and show its active label.");
        notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
        Assert(!notesPanel.IsExpanded&&notesPanel.Visibility==Visibility.Collapsed&&notesButton.Content?.ToString()=="View release notes","The same button must fully collapse release notes.");
        var githubButton=(Button)window.FindName("ViewReleaseOnGitHubButton");var actions=(Panel)notesButton.Parent;
        Assert(actions.Children.IndexOf(githubButton)==actions.Children.IndexOf(notesButton)+1,"View on GitHub must follow the release notes button.");
        var notes="## Changes\n- **Improved** search\n- Fixed `colors`\n\n[Details](https://example.com/notes)\n![image](https://example.com/image.png)";
        var json=JsonSerializer.Serialize(new[]{new{tag_name="v3.0.0",prerelease=false,body=notes},new{tag_name="v2.0.0-beta.2",prerelease=true,body="Older beta notes"}});
        var catalog=ReleaseUpdates.ParseCatalog(json,model.AppVersion,false);
        Assert(catalog.Latest?.Notes==notes,"Release notes must be retained from GitHub metadata.");model.ApplyCatalog(catalog);
        Assert(model.ReleaseNotesTitle.Contains("3.0.0")&&model.ReleaseNotesText.Contains("• Improved search")&&!model.ReleaseNotesText.Contains("**"),"The normal update panel must format the offered release notes as readable text.");
        model.ShowReleaseNotes(catalog.Betas[0]);Assert(model.ReleaseNotesTitle.Contains("2.0.0 Beta 2")&&model.ReleaseNotesText=="Older beta notes","Archived beta notes must be selectable without changing installation state.");
        model.ShowReleaseNotes(catalog.Latest! with {Notes=""});Assert(model.ReleaseNotesText.Contains("No release notes"),"Empty notes need a clear fallback.");
        model.ShowReleaseNotes(catalog.Latest);window.ShowSection("Updates");((Expander)window.FindName("ReleaseNotesPanel")).IsExpanded=true;
        Flush();window.SeekInterfaceMotion(TimeSpan.FromSeconds(1));Save(window,Path.Combine(directory,"qol-release-notes.png"),1000,740);
        var search=new SettingsSearchWindow();search.Query.Text="spark";Assert(search.Results.Items.Count>=2&&search.Results.SelectedIndex==0,"The real search dialog must populate and select results while typing.");Save(search,Path.Combine(directory,"qol-settings-search.png"),590,510);
        var diagnostics=ProblemReport.Diagnostics("1.2.2","Stable",false,true,@"C:\Users\PrivateName\private-secret.log",0,false,true);
        Assert(!diagnostics.Contains("PrivateName")&&!diagnostics.Contains("private-secret")&&diagnostics.Contains("Connected")&&diagnostics.Contains("Automatic discovery"),"Report diagnostics must include connection state without copying raw paths or arbitrary status text.");
        var report=new ProblemReportWindow(diagnostics);var launches=0;Uri? draft=null;
        report.OpenDraft(uri=>{launches++;draft=uri;});Assert(launches==0,"A report without a title must not open a browser.");
        report.Summary.Text="Volume & mute #problem";report.Report.Text="My reviewed description\nUnicode: café 🎵\n"+diagnostics;
        report.OpenDraft(uri=>{launches++;draft=uri;});
        Assert(launches==1&&draft?.Host=="github.com"&&draft.AbsolutePath=="/notjacobwashere/WaveWheels/issues/new","Reporting must only open the repository's new-issue draft.");
        Assert(Uri.UnescapeDataString(draft!.Query).Contains("Volume & mute #problem")&&Uri.UnescapeDataString(draft.Query).Contains("My reviewed description"),"The draft must preserve reviewed text and correctly encode query punctuation.");
        report.Report.Text=new string('界',2000);report.OpenDraft(_=>launches++);Assert(launches==1,"Oversized reports must remain editable and copyable instead of silently truncating or opening a broken draft.");
        report.Report.Text=ProblemReport.Template(diagnostics);Save(report,Path.Combine(directory,"qol-problem-report.png"),680,680);
        model.Dispose();Console.WriteLine("PASS: settings search targets/navigation/expansion, local release notes and archive selection, empty-note fallback, reviewed GitHub drafts, diagnostics privacy, URL escaping, and oversized-report protection.");
    }
    private static void Flush(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Save(Window window,string path,int width,int height)
    {
        var root=(FrameworkElement)window.Content;window.Content=null;root.Resources=window.Resources;root.SetValue(Control.ForegroundProperty,window.Foreground);root.SetValue(Control.FontSizeProperty,window.FontSize);root.SetValue(Control.FontFamilyProperty,window.FontFamily);if(root is Panel panel)panel.Background=window.Background;if(root.DataContext==null)root.DataContext=window.DataContext;
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
