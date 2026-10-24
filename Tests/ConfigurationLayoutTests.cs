using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaveWheels;

internal static class ConfigurationLayoutTests
{
    internal static void Run(MainViewModel vm,SettingsWindow window,FrameworkElement root,string directory)
    {
        if(((Image)window.FindName("HeaderAppIcon")).Source==null||window.Icon==null)throw new Exception("Header and window must use the packaged application icon.");
        using var iconStream=Application.GetResourceStream(new Uri("pack://application:,,,/WaveWheels;component/Assets/WaveWheels.ico"))!.Stream;
        if(!iconStream.CanRead)throw new Exception("Packaged app icon is unavailable.");
        var oldSize=vm.WheelSizePercent;var oldName=vm.SelectedWheel!.Name;var oldAnimations=vm.AnimateWheels;
        vm.SelectedWheel.Name="A LONG AUDIO CHANNEL LABEL 12345";
        ((Button)window.FindName("WheelsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var stage=(FrameworkElement)window.FindName("WheelPreviewStage");var viewport=(Viewbox)window.FindName("WheelPreviewViewport");var preview=(WheelControl)window.FindName("SelectedPreview");
        foreach(var width in new[]{1000,1160})foreach(var size in new[]{50,100,180})
        {
            vm.WheelSizePercent=size;
            root.Measure(new Size(width,740));root.Arrange(new Rect(0,0,width,740));root.UpdateLayout();
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);root.UpdateLayout();
            var bounds=((FrameworkElement)viewport.Child).TransformToAncestor(stage).TransformBounds(new Rect(0,0,((FrameworkElement)viewport.Child).ActualWidth,216));
            if(bounds.Left<-.1||bounds.Top<-.1||bounds.Right>stage.ActualWidth+.1||bounds.Bottom>stage.ActualHeight+.1)throw new Exception($"Padded preview must fit at {width}px and {size}%: {bounds}, stage {stage.ActualWidth}x{stage.ActualHeight}.");
            preview.BeginAnimation(WheelControl.DisplayLevelProperty,null);preview.DisplayLevel=.72;preview.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,740,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(directory,$"preview-layout-{width}-{size}.png"));encoder.Save(output);
        }
        vm.AnimateWheels=false;vm.SelectedWheel.Level=.37;preview.AdjustInput(1,true,9000);
        if(Math.Abs(preview.DisplayLevel-preview.Wheel!.Level)>1e-7)throw new Exception("Disabling animations must update the volume arc immediately.");
        vm.SelectedWheel.Name=oldName;vm.WheelSizePercent=oldSize;vm.AnimateWheels=oldAnimations;
        root.Measure(new Size(1000,740));root.Arrange(new Rect(0,0,1000,740));root.UpdateLayout();
        Console.WriteLine("PASS: packaged icon, padded preview containment at minimum/normal widths and 50–180% sizes, long labels, and disabled animation behavior.");
    }
}
