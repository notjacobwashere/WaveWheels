using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaveWheels;

internal static class RedesignTests
{
    internal static void Run(MainViewModel model,SettingsWindow window,FrameworkElement root)
    {
        ((Button)window.FindName("ProfilesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
        var menu=Children(root).OfType<Button>().First(b=>b.Content?.ToString()=="Manage profile  ▾").ContextMenu;
        if(menu.Items.Count!=6)throw new Exception("Profile management must retain all six actions.");
        var count=model.Profiles.Count;var previous=model.ActiveProfile;
        menu.Items.OfType<MenuItem>().Single(i=>i.Header.ToString()=="Duplicate layout").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if(model.Profiles.Count!=count+1||model.ActiveProfile==previous)throw new Exception("The profile menu must duplicate the active layout.");
        menu.Items.OfType<MenuItem>().Single(i=>i.Header.ToString()=="Remove profile").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if(model.Profiles.Count!=count)throw new Exception("The profile menu must remove the selected layout.");
        ((Button)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
        var group=Children((FrameworkElement)window.FindName("SettingsPage")).OfType<Expander>().Single(e=>e.Header.ToString()=="Scroll and Shift steps");group.IsExpanded=true;root.UpdateLayout();
        var slider=(Slider)window.FindName("ScrollStepSlider");slider.SetCurrentValue(Slider.ValueProperty,4d);if(model.ScrollStepPercent!=4)throw new Exception("Grouped input settings must still update the actual model.");model.ScrollStepPercent=1;
        ((Button)window.FindName("UpdatesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
        if(((Button)window.FindName("CancelUpdateButton")).Visibility!=Visibility.Collapsed||((ProgressBar)window.FindName("UpdateDownloadProgress")).Visibility!=Visibility.Collapsed)throw new Exception("Idle updates must hide download-only controls.");
        var versions=Children((FrameworkElement)window.FindName("UpdatesPage")).OfType<Expander>().Single(e=>e.Header.ToString()=="Versions and recovery");versions.IsExpanded=true;root.UpdateLayout();
        if(window.FindName("InstallStableButton") is not Button||window.FindName("BetaBuildList") is not ListBox)throw new Exception("Version recovery must retain stable rollback and beta archive controls.");
        Console.WriteLine("PASS: profile action-menu commands, grouped input settings, hidden idle download controls, and version recovery access.");
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var next in Children(child))yield return next;}
    }
}
