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
        var duplicateButton = (Button)window.FindName("DuplicateProfileButton");
        var removeButton = (Button)window.FindName("RemoveProfileButton");
        var actions = (Panel)duplicateButton.Parent;
        if (actions.Children.IndexOf(removeButton) != actions.Children.IndexOf(duplicateButton) + 1)
            throw new Exception("Remove profile must follow Duplicate layout.");
        var count = model.Profiles.Count;
        var previous = model.ActiveProfile!;
        var previousRules = previous.Applications;
        previous.Applications = "music.exe, game.exe";
        var originalWheels = model.Wheels.ToArray();

        duplicateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var copy = model.ActiveProfile!;
        if (model.Profiles.Count != count + 1 || copy.Id == previous.Id || copy.Applications != previous.Applications ||
            !model.Wheels.Select(w => w.Name).SequenceEqual(originalWheels.Select(w => w.Name)) ||
            model.Wheels.Zip(originalWheels).Any(pair => pair.First.Id == pair.Second.Id))
            throw new Exception("Duplicate layout must copy app rules and the ordered wheels with fresh IDs.");
        model.Wheels[0].Name = "Independent copy";
        copy.Applications = "";
        if (originalWheels[0].Name == "Independent copy" || previous.Applications != "music.exe, game.exe")
            throw new Exception("Editing a duplicate must not edit the original layout.");
        previous.Applications = previousRules;
        removeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(model.Profiles.Count!=count)throw new Exception("The profile menu must remove the selected layout.");
        ((Button)window.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
        var group=Children((FrameworkElement)window.FindName("SettingsPage")).OfType<Expander>().Single(e=>e.Header.ToString()=="Scroll and Shift steps");group.IsExpanded=true;root.UpdateLayout();
        var slider=(Slider)window.FindName("ScrollStepSlider");slider.SetCurrentValue(Slider.ValueProperty,4d);if(model.ScrollStepPercent!=4)throw new Exception("Grouped input settings must still update the actual model.");model.ScrollStepPercent=1;
        ((Button)window.FindName("UpdatesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
        if(((Button)window.FindName("CancelUpdateButton")).Visibility!=Visibility.Collapsed||((ProgressBar)window.FindName("UpdateDownloadProgress")).Visibility!=Visibility.Collapsed)throw new Exception("Idle updates must hide download-only controls.");
        var versions=(Expander)window.FindName("VersionHistory");versions.IsExpanded=true;root.UpdateLayout();
        if(window.FindName("InstallStableButton") is not Button||window.FindName("BetaBuildList") is not ListBox)throw new Exception("Version recovery must retain stable rollback and beta archive controls.");
        var archiveInstall=(Button)window.FindName("InstallBetaButton");var stableInstall=(Button)window.FindName("InstallStableButton");
        if(archiveInstall.Content?.ToString()!=stableInstall.Content?.ToString()||((Panel)archiveInstall.Parent).Children[0]!=archiveInstall||Children((FrameworkElement)window.FindName("VersionHistory")).OfType<Button>().Count(b=>b.Content?.ToString()=="View notes")!=1)throw new Exception("Archive install labels and order must match without duplicate controls.");
        Console.WriteLine("PASS: profile action-menu commands, grouped input settings, hidden idle download controls, and version recovery access.");
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var next in Children(child))yield return next;}
    }
}
