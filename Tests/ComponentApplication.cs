using System.Windows;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Markup;

// WPF may dispatch its startup callback when an offscreen test pumps bindings.
// Keep tests out of the real tray, mutex/signals, mixer, and global input lifecycle.
internal sealed class ComponentApplication : Application
{
    public void InitializeComponent()
    {
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var xaml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"AppResources.xaml"));
        var resources=xaml.Root!.Element(XName.Get("Application.Resources","http://schemas.microsoft.com/winfx/2006/xaml/presentation"))!;
        Resources=(ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"+string.Concat(resources.Elements().Select(e=>e.ToString()))+"</ResourceDictionary>");
    }
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}
