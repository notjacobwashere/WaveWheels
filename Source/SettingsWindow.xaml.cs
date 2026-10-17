using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;

namespace WaveWheels;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel model;
    private Point start;
    private Wheel? dragWheel;
    private Wheel? dragging;
    private DropMarker? dropMarker;
    private AdornerLayer? dropLayer;
    private int insertion;
    private long lastAutoScroll;
    public SettingsWindow(MainViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        CountBox.ItemsSource = Enumerable.Range(1,12);
        SelectedPreview.VolumeRequested += model.SetLevel;
        SelectedPreview.MuteRequested += model.ToggleMute;
        void PreviewInputSettings() {SelectedPreview.ScrollStepPercent=model.ScrollStepPercent;SelectedPreview.ScrollAccelerationEnabled=model.ScrollAccelerationEnabled;SelectedPreview.ResetScroll();}
        PreviewInputSettings();model.InputSettingsChanged+=PreviewInputSettings;
        Closing += (_,e) => { if (!App.Exiting) { e.Cancel = true; model.Save(); Hide(); } };
        SourceInitialized += (_,_) => { try { var preference=2;Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,33,ref preference,4); } catch { } };
        Activated += (_,_) => { model.RefreshMonitors();model.Changed(nameof(model.StartupEnabled)); model.Changed(nameof(model.StartupDescription)); };
    }
    private void TitleDrag(object sender,MouseButtonEventArgs e) { if(e.ClickCount==2) WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.LeftButton==MouseButtonState.Pressed) DragMove(); }
    private void Minimize(object sender,RoutedEventArgs e) => WindowState=WindowState.Minimized;
    private void HideConfig(object sender,RoutedEventArgs e) { model.Save(); Hide(); }
    private void Navigate(object sender,RoutedEventArgs e)
    {
        var section=(sender as Button)?.Tag as string;
        WheelsPage.Visibility=section=="Wheels"?Visibility.Visible:Visibility.Collapsed;
        StartupPage.Visibility=section=="Startup"?Visibility.Visible:Visibility.Collapsed;
        SettingsPage.Visibility=section=="Settings"?Visibility.Visible:Visibility.Collapsed;
        ConnectionPage.Visibility=section=="Connection"?Visibility.Visible:Visibility.Collapsed;
        ProfilesPage.Visibility=section=="Profiles"?Visibility.Visible:Visibility.Collapsed;
        PlacementPage.Visibility=section=="Placement"?Visibility.Visible:Visibility.Collapsed;
        UpdatesPage.Visibility=section=="Updates"?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in new[]{WheelsNav,SettingsNav,ProfilesNav,PlacementNav,UpdatesNav,StartupNav,ConnectionNav}) { var active=(string)button.Tag==section;button.Background=new SolidColorBrush(active?Color.FromRgb(39,33,50):Colors.Transparent);button.Foreground=new SolidColorBrush(active?Color.FromRgb(223,201,255):Color.FromRgb(151,138,167)); }
    }
    private void NewProfile(object sender,RoutedEventArgs e)=>model.AddProfile(false);
    private void DuplicateProfile(object sender,RoutedEventArgs e)=>model.AddProfile(true);
    private void RemoveProfile(object sender,RoutedEventArgs e)=>model.RemoveProfile();
    private void AddProfileApp(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Choose the app for this profile",Filter="Windows applications (*.exe)|*.exe",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=true||model.ActiveProfile==null)return;
        var app=Path.GetFileName(dialog.FileName);
        if(!ProfileAutomation.Matches(model.ActiveProfile.Applications,app))model.ActiveProfile.Applications+=(model.ActiveProfile.Applications.Trim().Length==0?"":", ")+app;
    }
    private void ExportProfile(object sender,RoutedEventArgs e)=>Export(false);
    private void ExportAllProfiles(object sender,RoutedEventArgs e)=>Export(true);
    private void Export(bool all)
    {
        var name=all?"WaveWheels-profiles":model.ActiveProfile?.Name??"Profile";
        foreach(var character in Path.GetInvalidFileNameChars())name=name.Replace(character,'_');
        var dialog=new SaveFileDialog {Title="Export WaveWheels profiles",Filter="WaveWheels profiles (*.wavewheels.json)|*.wavewheels.json",DefaultExt=".wavewheels.json",FileName=name+".wavewheels.json"};
        if(dialog.ShowDialog(this)!=true)return;
        try {model.ExportProfiles(dialog.FileName,all);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {model.Message="Export failed: "+ex.Message;}
    }
    private void ImportProfiles(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Import WaveWheels profiles",Filter="WaveWheels profiles (*.wavewheels.json;*.json)|*.wavewheels.json;*.json",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=true)return;
        try {model.ImportProfiles(dialog.FileName);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException) {model.Message="Import failed: "+ex.Message;}
    }
    private async void CheckUpdates(object sender,RoutedEventArgs e)=>await model.CheckForUpdatesAsync();
    private void OpenRelease(object sender,RoutedEventArgs e)=>model.OpenReleasePage();
    private void Add(object sender,RoutedEventArgs e) => model.AddWheel();
    private void Remove(object sender,RoutedEventArgs e) => model.RemoveWheel();
    private void RowUp(object sender,RoutedEventArgs e) { if(sender is Button {Tag:Wheel wheel}) { model.SelectedWheel=wheel; AnimateReorder(()=>model.Move(-1)); } }
    private void RowDown(object sender,RoutedEventArgs e) { if(sender is Button {Tag:Wheel wheel}) { model.SelectedWheel=wheel; AnimateReorder(()=>model.Move(1)); } }
    private void Preview(object sender,RoutedEventArgs e) => model.Preview();
    private void Reconnect(object sender,RoutedEventArgs e) => model.Reconnect();
    private void SetColor(object sender,RoutedEventArgs e) { if(model.SelectedWheel!=null && sender is Button b && b.Tag is string color)model.SelectedWheel.Color=color; }
    private void ApplyHex(object sender,RoutedEventArgs e)
    {
        var text=ColorBox.Text.Trim();
        if(!text.StartsWith('#'))text="#"+text;
        if(Regex.IsMatch(text,"^#[0-9A-Fa-f]{6}$") && model.SelectedWheel!=null) model.SelectedWheel.Color=text.ToUpperInvariant();
        else model.Message="Use a six-digit hex color, such as #36BBD3.";
        ColorBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
    private void ColorKey(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) {ApplyHex(sender,e);e.Handled=true;} }
    private void ChannelSelected(object sender,SelectionChangedEventArgs e) { if(ChannelBox.IsDropDownOpen)ApplyChannel(); }
    private void ChannelClosed(object? sender,EventArgs e) => ApplyChannel();
    private void ApplyChannel() { if(ChannelBox.SelectedValue is string id && model.SelectedWheel!=null && id!=model.SelectedWheel.ChannelId)model.SelectedWheel.ChannelId=id; }
    private void MixSelected(object sender,SelectionChangedEventArgs e) { if(MixBox.IsDropDownOpen)ApplyMix(); }
    private void MixClosed(object? sender,EventArgs e) => ApplyMix();
    private void ApplyMix() { if(MixBox.SelectedValue is string id && model.SelectedWheel!=null)model.SelectedWheel.MixId=id; }
    private void DragStart(object sender,MouseButtonEventArgs e)
    {
        start=e.GetPosition(WheelList);dragWheel=null;
        var item=ItemsControl.ContainerFromElement(WheelList,e.OriginalSource as DependencyObject) as ListBoxItem;
        for(var element=e.OriginalSource as DependencyObject; element!=null&&element!=item; element=GetAncestorNode(element))
            if(element is FrameworkElement {Tag:"DragHandle"} && item?.DataContext is Wheel wheel) { dragWheel=wheel; model.SelectedWheel=wheel; break; }
    }
    private void DragMove(object sender,MouseEventArgs e)
    {
        if(dragWheel==null||e.LeftButton!=MouseButtonState.Pressed)return;
        var distance=e.GetPosition(WheelList)-start;
        if(Math.Abs(distance.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(distance.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var wheel=dragWheel;dragWheel=null;dragging=wheel;
        var item=WheelList.ItemContainerGenerator.ContainerFromItem(wheel) as ListBoxItem;
        if(item!=null)item.Opacity=.4;
        model.Message="Moving "+wheel.Name+" · drop on the highlighted line";
        try { DragDrop.DoDragDrop(WheelList,new DataObject(typeof(Wheel),wheel),DragDropEffects.Move); }
        finally {dragging=null;if(item!=null)item.Opacity=1;ClearDropMarker();}
        e.Handled=true;
    }
    private void DragOverWheel(object sender,DragEventArgs e)
    {
        if(!e.Data.GetDataPresent(typeof(Wheel))) {e.Effects=DragDropEffects.None;return;}
        var point=e.GetPosition(WheelList);
        var scroll=FindChild<ScrollViewer>(WheelList);
        if(scroll!=null&&Environment.TickCount64-lastAutoScroll>45)
        {
            if(point.Y<38)scroll.ScrollToVerticalOffset(scroll.VerticalOffset-18);
            else if(point.Y>WheelList.ActualHeight-38)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+18);
            lastAutoScroll=Environment.TickCount64;
        }
        insertion=InsertionAt(point.Y);
        ShowDropIndicator(insertion);
        e.Effects=DragDropEffects.Move;e.Handled=true;
    }
    internal double? DropIndicatorY => dropMarker?.LineY;
    internal void ShowDropIndicator(int gap)
    {
        insertion=Math.Clamp(gap,0,model.Wheels.Count);
        dropLayer??=AdornerLayer.GetAdornerLayer(WheelList);
        if(dropLayer!=null)
        {
            if(dropMarker==null) {dropMarker=new DropMarker(WheelList);dropLayer.Add(dropMarker);}
            double y;
            if(insertion<model.Wheels.Count&&WheelList.ItemContainerGenerator.ContainerFromIndex(insertion) is ListBoxItem next)y=next.TranslatePoint(new Point(0,0),WheelList).Y-5;
            else if(WheelList.ItemContainerGenerator.ContainerFromIndex(model.Wheels.Count-1) is ListBoxItem last)y=last.TranslatePoint(new Point(0,last.ActualHeight),WheelList).Y+5;
            else y=2;
            dropMarker.LineY=Math.Clamp(y,2,Math.Max(2,WheelList.ActualHeight-3));dropMarker.InvalidateVisual();
        }
    }
    internal int InsertionAt(double y)
    {
        for(var i=0;i<model.Wheels.Count;i++)
            if(WheelList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item && y<item.TranslatePoint(new Point(0,item.ActualHeight/2),WheelList).Y)return i;
        return model.Wheels.Count;
    }
    private void DragLeaveWheel(object sender,DragEventArgs e)
    {
        var p=e.GetPosition(WheelList);
        if(p.X<0||p.Y<0||p.X>WheelList.ActualWidth||p.Y>WheelList.ActualHeight)ClearDropMarker();
    }
    private void DropWheel(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(Wheel)) is not Wheel wheel||!model.Wheels.Contains(wheel))return;
        insertion=InsertionAt(e.GetPosition(WheelList).Y);
        AnimateReorder(()=>model.MoveToInsertion(wheel,insertion));
        model.Message=wheel.Name+" moved to position "+(model.Wheels.IndexOf(wheel)+1)+".";
        ClearDropMarker();
        e.Handled=true;
    }
    private void ReorderKey(object sender,KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if((Keyboard.Modifiers&ModifierKeys.Alt)==0||key is not (Key.Up or Key.Down))return;
        AnimateReorder(()=>model.Move(key==Key.Up?-1:1));e.Handled=true;
    }
    private void AnimateReorder(Action change)
    {
        var before=new Dictionary<string,double>();
        foreach(var wheel in model.Wheels)if(WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is ListBoxItem item)before[wheel.Id]=item.TranslatePoint(new Point(0,0),WheelList).Y;
        change();WheelList.UpdateLayout();
        foreach(var wheel in model.Wheels)if(WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is ListBoxItem item)item.RenderTransform=Transform.Identity;
        if(!SystemParameters.ClientAreaAnimation)return;
        foreach(var wheel in model.Wheels)
        {
            if(!before.TryGetValue(wheel.Id,out var old)||WheelList.ItemContainerGenerator.ContainerFromItem(wheel) is not ListBoxItem item)continue;
            var delta=old-item.TranslatePoint(new Point(0,0),WheelList).Y;
            if(Math.Abs(delta)<1)continue;
            var transform=new TranslateTransform();item.RenderTransform=transform;
            transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(delta,0,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
        }
    }
    internal void ClearDropMarker() {if(dropMarker!=null)dropLayer?.Remove(dropMarker);dropMarker=null;}
    private static DependencyObject? GetAncestorNode(DependencyObject element)=>element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
    private static T? FindChild<T>(DependencyObject element) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++){var child=VisualTreeHelper.GetChild(element,i);if(child is T found)return found;var nested=FindChild<T>(child);if(nested!=null)return nested;}
        return null;
    }
    private sealed class DropMarker:Adorner
    {
        public double LineY {get;set;}
        public DropMarker(UIElement element):base(element) {IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext dc)
        {
            var a=new Point(8,LineY);var b=new Point(Math.Max(10,AdornedElement.RenderSize.Width-8),LineY);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(45,216,174,255)),8),a,b);
            var brush=new SolidColorBrush(Color.FromRgb(216,174,255));dc.DrawLine(new Pen(brush,2),a,b);dc.DrawEllipse(brush,null,a,3,3);dc.DrawEllipse(brush,null,b,3,3);
        }
    }
}
