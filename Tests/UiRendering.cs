using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaveWheels;

internal static class UiRendering
{
    public static void Run(string path)
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                var app=new ComponentApplication();app.InitializeComponent();
                Console.WriteLine("UI: initialized resources");
                var store=new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"render-profile-"+Guid.NewGuid().ToString("N")));
                var vm=new MainViewModel(store,true);
                Console.WriteLine("UI: loaded profile and monitor choices");
                while(vm.Wheels.Count>3)vm.RemoveWheel(vm.Wheels[^1]);
                while(vm.Wheels.Count<3)vm.AddWheel();
                vm.Wheels[0].Name="Music";vm.Wheels[0].Color="#36BBD3";vm.Wheels[0].ChannelId="music";vm.Wheels[0].MixId="personal";
                vm.Wheels[1].Name="Game";vm.Wheels[1].Color="#DA5DAD";vm.Wheels[1].ChannelId="game";vm.Wheels[1].MixId="personal";
                vm.Wheels[2].Name="Voice";vm.Wheels[2].Color="#A99AFF";vm.Wheels[2].ChannelId="voice";vm.Wheels[2].MixId="personal";
                vm.Update(new(true,"Preview data · no mixer connection","Wave Link 3",new[]{
                    new Channel("music","Music",.68,false,true,new[]{new ChannelMix("personal",.68,false)}),
                    new Channel("game","Game",.82,false,true,new[]{new ChannelMix("personal",.82,false)}),
                    new Channel("voice","Voice chat",.55,false,true,new[]{new ChannelMix("personal",.55,false)})
                },new[]{new Mix("personal","Headphones")}));
                vm.SelectedWheel=vm.Wheels[0];vm.WheelSizePercent=100;vm.AnimateWheels=true;vm.ScreenEdge="Left";vm.MonitorId="";vm.PositionPercent=50;vm.WheelSpacing=0;vm.EdgeEnabled=true;vm.CheckUpdatesAutomatically=true;
                vm.ScrollStepPercent=1;vm.ScrollAccelerationEnabled=true;vm.IdleMilliseconds=1000;vm.AutomaticProfilesEnabled=false;
                foreach(var wheel in vm.Wheels)wheel.MaximumVolumePercent=100;
                vm.Message="Preview with example channels · configuration rendered without desktop input";
                var window=new SettingsWindow(vm);
                UpdateTests.ViewModel(Path.Combine(Path.GetDirectoryName(path)!,"update-lifecycle-"+Guid.NewGuid().ToString("N")));
                Console.WriteLine("UI: constructed studio");
                var root=(FrameworkElement)window.Content;
                window.Content=null;root.DataContext=vm;root.Resources=window.Resources;
                System.Windows.Documents.TextElement.SetFontFamily(root,new FontFamily("Segoe UI"));
                System.Windows.Documents.TextElement.SetFontSize(root,14);
                System.Windows.Documents.TextElement.SetForeground(root,new SolidColorBrush(Color.FromRgb(243,240,250)));
                root.Measure(new Size(1160,860));root.Arrange(new Rect(0,0,1160,860));root.UpdateLayout();
                Console.WriteLine("UI: arranged studio");
                var previewToggle=(Button)window.FindName("PreviewToggleButton");
                previewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
                if(!vm.IsPreviewing||previewToggle.Content?.ToString()!="■  Stop preview")throw new Exception("Preview must toggle on and change its label.");
                previewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
                if(vm.IsPreviewing||previewToggle.Content?.ToString()!="↗  Preview wheels")throw new Exception("Preview must toggle off and restore its label.");
                if(vm.EdgeDistance!=32||OverlaySizing.EdgeOffset(32,1,1)!=-16||OverlaySizing.EdgeOffset(80,1.5,2)!=96)throw new Exception("Edge distance default and DPI scaling failed.");
                ((Slider)window.FindName("EdgeDistanceSlider")).SetCurrentValue(Slider.ValueProperty,60d);vm.Save();
                if(vm.EdgeDistance!=60||store.Load().EdgeDistance!=60)throw new Exception("Placement distance must bind and persist.");vm.EdgeDistance=32;
                TestHeader(window,root);
                var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
                foreach(var wheel in Descendants(root).OfType<WheelControl>()) {wheel.BeginAnimation(WheelControl.DisplayLevelProperty,null);wheel.DisplayLevel=wheel.Wheel?.Level??0;}
                root.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1160,860,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
                var list=(ListBox)window.FindName("WheelList");
                if(list.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem firstItem)throw new Exception("Wheel list items were not generated.");
                var top=firstItem.TranslatePoint(new Point(0,0),list).Y;
                if(window.InsertionAt(top+2)!=0||window.InsertionAt(top+firstItem.ActualHeight-2)!=1)throw new Exception("Drop hit testing must distinguish the top and bottom halves of a row.");
                window.ShowDropIndicator(2);root.UpdateLayout();
                if(window.DropIndicatorY==null)throw new Exception("The reorder drop indicator is missing its adorner layer.");
                Save(root,Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"-reorder.png"),1160,860);
                window.ClearDropMarker();
                var settingsNav=(Button)window.FindName("SettingsNav");
                if(!settingsNav.Content.ToString()!.Contains("Settings"))throw new Exception("An explicitly named Settings tab is required.");
                settingsNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                root.UpdateLayout();
                if(((FrameworkElement)window.FindName("SettingsPage")).Visibility!=Visibility.Visible)throw new Exception("Settings navigation did not open the page.");
                foreach(var section in new[]{"Profiles","Placement","Effects","Hotkeys","Updates"})
                {
                    ((Button)window.FindName(section+"Nav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
                    if(((FrameworkElement)window.FindName(section+"Page")).Visibility!=Visibility.Visible)throw new Exception(section+" navigation failed.");
                    if(section=="Profiles"&&!Descendants((ComboBox)window.FindName("ProfilePicker")).OfType<TextBlock>().Any(t=>t.Text==vm.ActiveProfile!.Name))throw new Exception("The profile picker must show its friendly name.");
                    if(section=="Effects")
                    {
                        if(((WheelControl)window.FindName("EffectsDemo")).Effects!=vm.Effects)throw new Exception("Effects preview must share the live settings object.");
                        vm.Effects.BloomPercent=150;
                        Descendants((FrameworkElement)window.FindName("EffectsPage")).OfType<Button>().Single(b=>b.Content?.ToString()=="Restore defaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(vm.Effects.BloomPercent!=100)throw new Exception("Effects Restore defaults button did not reset actual settings.");
                    }
                    if(section=="Hotkeys"&&Descendants((FrameworkElement)window.FindName("HotkeyRows")).OfType<ComboBox>().First().Items.Count!=6)throw new Exception("Hotkey rows must display the available actions.");
                    if(section=="Updates")
                    {
                        if(((Button)window.FindName("InstallUpdateButton")).IsEnabled||((Button)window.FindName("CancelUpdateButton")).IsEnabled)throw new Exception("Update buttons must be disabled before discovery and outside downloads.");
                        if(!Descendants((FrameworkElement)window.FindName("UpdatesPage")).OfType<Button>().Any(b=>b.Content?.ToString()=="Open release page"))throw new Exception("The release-page fallback must remain available.");
                        var betaStore=new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"beta-render-"+Guid.NewGuid().ToString("N")));
                        betaStore.Save(new Preferences {UpdateChannel="Beta"});
                        var betaVm=new MainViewModel(betaStore,true);
                        betaVm.ApplyCatalog(new(null,new[]{UpdateTests.Fixture(new byte[]{1,2,3}) with {IsBeta=true},new ReleaseUpdate("0.6.0-beta.2","https://github.com/notjacobwashere/WaveWheels/releases/tag/v0.6.0-beta.2",null,true)}));
                        root.DataContext=betaVm;root.UpdateLayout();
                        if(((ComboBox)window.FindName("UpdateChannelPicker")).SelectedItem?.ToString()!="Beta"||((ListBox)window.FindName("BetaBuildList")).Items.Count!=2||((Button)window.FindName("InstallBetaButton")).IsEnabled)throw new Exception("Beta channel/archive bindings or isolated install protection failed.");
                        Save(root,Path.Combine(Path.GetDirectoryName(path)!,"updates-beta.png"),1160,860);
                        ((ScrollViewer)window.FindName("UpdatesPage")).ScrollToEnd();root.UpdateLayout();
                        Save(root,Path.Combine(Path.GetDirectoryName(path)!,"updates-beta-archive.png"),1160,860);
                        betaVm.Dispose();root.DataContext=vm;((ScrollViewer)window.FindName("UpdatesPage")).ScrollToTop();root.UpdateLayout();
                    }
                    Save(root,Path.Combine(Path.GetDirectoryName(path)!,section.ToLowerInvariant()+".png"),1160,860);
                }
                settingsNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
                Save(root,Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"-settings.png"),1160,860);
                var settingsPage=(ScrollViewer)window.FindName("SettingsPage");settingsPage.ScrollToEnd();root.UpdateLayout();
                Save(root,Path.Combine(Path.GetDirectoryName(path)!,"scroll-controls.png"),1160,860);
                ((Slider)window.FindName("ScrollStepSlider")).SetCurrentValue(Slider.ValueProperty,3d);
                ((Slider)window.FindName("HideDelaySlider")).SetCurrentValue(Slider.ValueProperty,2500d);
                if(vm.ScrollStepPercent!=3||vm.IdleMilliseconds!=2500||((WheelControl)window.FindName("SelectedPreview")).ScrollStepPercent!=3)throw new Exception("Tuning controls did not update both the view model and preview input.");
                vm.ScrollStepPercent=1;vm.IdleMilliseconds=1000;settingsPage.ScrollToTop();
                var sizeSlider=(Slider)window.FindName("WheelSizeSlider");
                sizeSlider.SetCurrentValue(Slider.ValueProperty,145d);
                if(vm.WheelSizePercent!=145)throw new Exception("The Settings size slider did not change the actual view model.");
                ((Button)window.FindName("WheelsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                root.Measure(new Size(1000,740));root.Arrange(new Rect(0,0,1000,740));root.UpdateLayout();
                Save(root,Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"-compact.png"),1000,740);
                // Exercise the actual collection move and persistence code without a visible window.
                var first=vm.Wheels[0];var original=vm.Wheels.Select(w=>w.Id).ToArray();
                vm.MoveToInsertion(first,vm.Wheels.Count);
                if(vm.Wheels[^1]!=first||vm.SelectedWheel!=first||vm.Wheels.Select(w=>w.Id).Distinct().Count()!=3)throw new Exception("Reordering lost identity or selection.");
                for(var i=0;i<vm.Wheels.Count;i++)if(vm.Wheels[i].Position!=i)throw new Exception("Order numbers did not update.");
                vm.WheelSizePercent=145;vm.AnimateWheels=false;vm.Save();var restored=store.Load();
                if(restored.WheelSizePercent!=145||restored.AnimateWheels||restored.Wheels[^1].Id!=first.Id)throw new Exception("Size, animation, or order did not survive save.");
                vm.MoveToInsertion(first,0);if(!vm.Wheels.Select(w=>w.Id).SequenceEqual(original))throw new Exception("Upward insertion did not restore the original order.");
                VerifyOverlay(vm,Path.GetDirectoryName(path)!);
                var baseline=vm.ActiveProfile!;var ids=vm.Wheels.Select(w=>w.Id).ToArray();
                vm.AddProfile(true);var copy=vm.ActiveProfile!;copy.Name="Gaming";
                if(vm.Wheels.Count!=3||vm.Wheels.Any(w=>ids.Contains(w.Id)))throw new Exception("Duplicate profiles must clone layout with independent wheel identities.");
                vm.Wheels[0].Name="Gaming only";vm.ActiveProfile=baseline;
                if(vm.Wheels[0].Name=="Gaming only"||!vm.Wheels.Select(w=>w.Id).SequenceEqual(ids))throw new Exception("Profile switching leaked edits or lost bindings.");
                vm.ActiveProfile=copy;vm.ScreenEdge="Right";vm.PositionPercent=25;vm.WheelSpacing=12;vm.CheckUpdatesAutomatically=false;vm.Save();
                var profileSaved=store.Load();
                if(profileSaved.ActiveProfileId!=copy.Id||profileSaved.Profiles.Count<2||profileSaved.Wheels[0].Name!="Gaming only"||profileSaved.ScreenEdge!="Right"||profileSaved.PositionPercent!=25||profileSaved.WheelSpacing!=12||profileSaved.CheckUpdatesAutomatically)throw new Exception("Profiles, placement, or update preference did not survive reload.");
                vm.RemoveProfile();if(vm.ActiveProfile!=baseline)throw new Exception("Removing a profile did not preserve the remaining layout.");
                NewFeatures.ViewModel(Path.Combine(Path.GetDirectoryName(path)!,"new-features-profile-"+Guid.NewGuid().ToString("N")));
                MonitorPersistenceTests.ViewModel(Path.Combine(Path.GetDirectoryName(path)!,"monitor-profile-"+Guid.NewGuid().ToString("N")));
                CustomizationTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"customization-profile-"+Guid.NewGuid().ToString("N")));
                vm.Dispose();
            }
            catch(Exception ex){error=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void VerifyOverlay(MainViewModel vm,string directory)
    {
        var overlay=new OverlayWindow(vm);
        // Exercise the real native callback and overlay handler without injecting desktop input.
        overlay.Input.Preview();
        using(var hook=new EdgeHook())
        {
            hook.WheelInput=overlay.OnGlobalWheel;
            var monitor=System.Windows.Forms.Screen.AllScreens[0];
            var data=new Native.MouseData {Point=new Native.Point {X=monitor.Bounds.Left,Y=monitor.Bounds.Top},MouseDataValue=120u<<16};
            var memory=Marshal.AllocHGlobal(Marshal.SizeOf<Native.MouseData>());
            try {Marshal.StructureToPtr(data,memory,false);if(hook.OnHook(0,new IntPtr(0x20A),memory)!=new IntPtr(1))throw new Exception("Outermost edge scrolling must be consumed by the native hook.");}
            finally {Marshal.FreeHGlobal(memory);}
        }
        overlay.Input.Close();
        var surface=(Grid)overlay.Content;overlay.Content=null;
        surface.Measure(new Size(350,384));surface.Arrange(new Rect(0,0,350,384));surface.UpdateLayout();
        if(surface.Background!=null)throw new Exception("The overlay must have no background panel.");
        foreach(var wheel in Descendants(surface).OfType<WheelControl>()) {wheel.BeginAnimation(WheelControl.DisplayLevelProperty,null);wheel.DisplayLevel=wheel.Wheel!.Level;}
        var motion=overlay.Motion;
        motion.Enter(350,true,true,()=>{});motion.Seek(TimeSpan.Zero);
        if(motion.Slide.X> -349 || Math.Abs(motion.Scale.ScaleX-.72)>.001 || surface.Opacity!=1)throw new Exception("Entrance must start off-screen, reduced in size, and fully opaque.");
        motion.Seek(TimeSpan.FromMilliseconds(100));
        if(motion.Slide.X is <= -350 or >= -10 || motion.Scale.ScaleX is <= .72 or >= 1)throw new Exception("Entrance must move and grow between frames.");
        Save(surface,Path.Combine(directory,"overlay-enter-100ms.png"),350,384);
        motion.Seek(TimeSpan.FromMilliseconds(220));
        Save(surface,Path.Combine(directory,"overlay-enter-220ms.png"),350,384);
        motion.Seek(TimeSpan.FromMilliseconds(380));
        if(Math.Abs(motion.Slide.X)>.001||Math.Abs(motion.Scale.ScaleX-1)>.001)throw new Exception("Entrance must finish at the configured size and position.");
        Save(surface,Path.Combine(directory,"overlay.png"),350,384);
        var bitmap=new RenderTargetBitmap(350,384,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(345,180,1,1),pixel,4,0);
        if(pixel[3]!=0)throw new Exception("The desktop around the wheels must remain transparent.");
        motion.Exit(350,true,()=>{});motion.Seek(TimeSpan.FromMilliseconds(130));
        if(motion.Slide.X>= -20||motion.Scale.ScaleX>=.99||surface.Opacity!=1)throw new Exception("Exit must slide and shrink without fading.");
        Save(surface,Path.Combine(directory,"overlay-exit-130ms.png"),350,384);
        var reversingX=motion.Slide.X;var reversingScale=motion.Scale.ScaleX;
        motion.Enter(350,false,true,()=>{});motion.Seek(TimeSpan.Zero);
        if(Math.Abs(motion.Slide.X-reversingX)>.001||Math.Abs(motion.Scale.ScaleX-reversingScale)>.001)throw new Exception("Reopening during exit must reverse from the current pose.");
        var completed=false;motion.Exit(350,false,()=>completed=true);
        if(!completed||motion.Slide.X!= -350||motion.Scale.ScaleX!=.72)throw new Exception("Disabling motion must finish immediately.");
        foreach(var edge in new[]{"Right","Top","Bottom"})
        {
            motion.SetEdge(edge);motion.Enter(350,true,true,()=>{});motion.Seek(TimeSpan.Zero);
            var offset=edge=="Right"?motion.Slide.X:motion.Slide.Y;
            if(offset!=(edge=="Top"?-350:350))throw new Exception("Entrance must originate from the selected edge.");
            motion.Seek(TimeSpan.FromMilliseconds(380));
            if(Math.Abs(motion.Slide.X)>.001||Math.Abs(motion.Slide.Y)>.001)throw new Exception("Edge entrance must finish at its resting position.");
        }
        // Drive the real wheel controls using the same router used by the global hook.
        var controls=Descendants(surface).OfType<WheelControl>().ToArray();var router=overlay.Input;
        long now=0;
        foreach(var control in controls)
        {
            if(control.ToolTip!=null)throw new Exception("Wheel hover tooltips must be removed.");
            control.CanAdjustVolume=()=>router.CanAdjust(now);
        }
        var before=vm.Wheels.Select(w=>w.Level).ToArray();
        for(now=0;now<=2000;now+=20)
        {
            var route=router.Route(2,2,now);
            if(route.Index>=0)controls[route.Index].Adjust(1);
            // Protect the control's own wheel/drag path as well as the global hook path.
            foreach(var control in controls)control.Adjust(.25);
        }
        if(!vm.Wheels.Select(w=>w.Level).SequenceEqual(before))throw new Exception("The entire opening gesture must leave every bound channel unchanged.");
        now=2250;var armed=router.Route(2,0,now);controls[armed.Index].Adjust(1);
        if(vm.Wheels[2].Level!=Math.Round(before[2]+.01,2)||vm.Wheels[0].Level!=before[0]||vm.Wheels[1].Level!=before[1])throw new Exception("The first post-pause scroll must adjust only its target, with no accumulated opening input.");
        now=2260;armed=router.Route(0,2,now);controls[armed.Index].Adjust(1);
        if(vm.Wheels[0].Level!=Math.Round(before[0]+.01,2)||vm.Wheels[1].Level!=before[1])throw new Exception("Top-wheel adjustment must work after activation.");
        router.Close();now=3000;var reopened=router.Route(0,0,now);
        if(!reopened.Reveal||reopened.Index!=-1)throw new Exception("Reopening must reset input suppression.");
        overlay.Close();
    }
    private static void TestHeader(SettingsWindow window,FrameworkElement root)
    {
        var header=(Border)window.FindName("TitleBar");var drags=0;
        foreach(var point in new[]{new Point(12,32),new Point(38,32),new Point(130,32),new Point(420,32),new Point(760,32),new Point(980,32)})
        {
            var source=VisualTreeHelper.HitTest(root,point)?.VisualHit;
            if(!SettingsWindow.IsHeaderDragSource(source,header)||!window.HandleHeaderPress(source,1,()=>drags++))throw new Exception("Header hit test failed at "+point+" on "+source?.GetType().Name);
        }
        if(drags!=6)throw new Exception("Header presses must initiate exactly one drag each.");
        foreach(var button in Descendants(header).OfType<Button>())
        {
            var source=Descendants(button).LastOrDefault()??button;
            if(window.HandleHeaderPress(source,1,()=>drags++)||window.HandleHeaderPress(source,2,()=>drags++))throw new Exception("Caption buttons and template descendants must not drag or maximize.");
        }
        var host=(Grid)header.Child;
        foreach(var control in new Control[]{new TextBox(),new ComboBox(),new Slider(),new CheckBox(),new Button()})
        {
            host.Children.Add(control);
            if(window.HandleHeaderPress(control,1,()=>drags++))throw new Exception("Interactive header controls must keep their input.");
            host.Children.Remove(control);
        }
        var link=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("link"));var text=new TextBlock(link);host.Children.Add(text);
        if(window.HandleHeaderPress(link.Inlines.FirstInline,1,()=>drags++))throw new Exception("Header hyperlinks must not initiate dragging.");host.Children.Remove(text);
        if(window.HandleHeaderPress(VisualTreeHelper.HitTest(root,new Point(400,150))?.VisualHit,1,()=>drags++)||window.HandleHeaderPress((Button)window.FindName("SettingsNav"),1,()=>drags++))throw new Exception("Body content and navigation must never drag the window.");
        window.HandleHeaderPress(header,2,()=>drags++);if(window.WindowState!=WindowState.Maximized||drags!=6)throw new Exception("Double-clicking the header must maximize without dragging.");
        window.HandleHeaderPress(header,2,()=>drags++);if(window.WindowState!=WindowState.Normal)throw new Exception("Double-clicking again must restore.");
        Console.WriteLine("PASS: full-width header hit testing/drag dispatch, interactive descendants, body/navigation exclusion, and maximize/restore.");
    }
    private static void Save(FrameworkElement root,string path,int width,int height)
    {
        root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
