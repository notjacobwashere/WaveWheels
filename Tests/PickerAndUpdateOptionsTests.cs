using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveWheels;

internal static class PickerAndUpdateOptionsTests
{
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(MainViewModel model,SettingsWindow window,FrameworkElement root,string directory)
    {
        var picker=new ColorPickerWindow("#FFFB00");
        Assert(picker.SelectedHex=="#FFFB00","The picker must load the current opaque color.");
        picker.SetHsv(120,1,1);Assert(picker.SelectedHex=="#00FF00","Hue and saturation must produce the expected RGB color.");
        picker.SetRgb(22,33,44);Assert(picker.SelectedHex=="#16212C","RGB controls must synchronize the hex value.");
        Assert(!picker.CommitHex("#80112233")&&picker.SelectedHex=="#16212C","Alpha hex colors must be rejected without changing the selected color.");
        Assert(picker.CommitHex("fffb00")&&ColorPickerWindow.FromHsv(360,1,1)==Colors.Red,"Hex input and the hue wrap must remain synchronized.");
        var contents=(FrameworkElement)picker.Content;picker.Content=null;contents.Resources=picker.Resources;System.Windows.Documents.TextElement.SetForeground(contents,Brushes.White);System.Windows.Documents.TextElement.SetFontFamily(contents,new FontFamily("Segoe UI"));System.Windows.Documents.TextElement.SetFontSize(contents,14);contents.Measure(new Size(448,640));contents.Arrange(new Rect(0,0,448,640));contents.UpdateLayout();
        var bitmap=new RenderTargetBitmap(448,640,96,96,PixelFormats.Pbgra32);bitmap.Render(contents);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(directory,"color-picker.png")))png.Save(stream);
        var count=model.DisplayWheelCount;
        ((Button)window.FindName("IncreaseWheelCountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(model.DisplayWheelCount==Math.Min(12,count+1),"Plus must increase the actual wheel count.");
        ((Button)window.FindName("DecreaseWheelCountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(model.DisplayWheelCount==count,"Minus must decrease the actual wheel count.");
        var ids=model.Wheels.Select(w=>w.Id).ToArray();model.UseWindowsAudio=true;Assert(!model.Connected&&model.DisplayWheelCount==1&&ReferenceEquals(model.EditorWheel,model.WindowsWheel),"Manual Windows mode must show the independent Windows wheel.");
        var stale=model.Wheels[0];var level=stale.Level;model.SetLevel(stale,.12);Assert(stale.Level==level,"Manual Windows mode must reject stale Wave Link writes.");
        model.UseWindowsAudio=false;Assert(model.Connected&&ids.SequenceEqual(model.Wheels.Select(w=>w.Id)),"Returning to Wave Link must preserve its layout and bindings.");
        var vm=new MainViewModel(new SettingsStore(Path.Combine(directory,"repair-options")),false);
        var release=UpdateTests.Fixture(new byte[]{1,2,3}) with {Version=vm.AppVersion,IsBeta=false};var launches=0;var downloads=0;
        vm.RepairAppCoreAsync(()=>Task.FromResult(new ReleaseCatalog(null,Array.Empty<ReleaseUpdate>(),release)),(r,p,c)=>{downloads++;Assert(r==release,"Repair must choose the stable installer even when already current.");return Task.FromResult("fixture-never-execute.exe");},_=>launches++).GetAwaiter().GetResult();
        Assert(downloads==1&&launches==1&&vm.UpdateChannel=="Stable","Repair must use the existing verified install handoff for same-version reinstalls.");
        vm.RepairAppCoreAsync(()=>Task.FromException<ReleaseCatalog>(new InvalidOperationException("Unavailable")),(r,p,c)=>{downloads++;return Task.FromResult("");},_=>launches++).GetAwaiter().GetResult();Assert(downloads==1&&launches==1,"A failed repair check must not use an old cached installer.");
        var checks=0;var installs=0;vm.ApplyRelease(release with {Version="99.0.0"});vm.Preferences.AutoInstallUpdates=true;
        Task Check(){checks++;return Task.CompletedTask;}Task Install(){installs++;return Task.CompletedTask;}
        vm.RunStartupUpdateCoreAsync(false,Check,Install).GetAwaiter().GetResult();Assert(checks==0&&installs==0,"Manual launches must never auto-install.");
        vm.RunStartupUpdateCoreAsync(true,Check,Install).GetAwaiter().GetResult();Assert(checks==1&&installs==1,"Opted-in sign-in launches must check and install an available update.");
        vm.Preferences.AutoInstallUpdates=false;vm.RunStartupUpdateCoreAsync(true,Check,Install).GetAwaiter().GetResult();Assert(checks==1&&installs==1,"Disabled automatic updates must not run.");vm.Dispose();
        Console.WriteLine("PASS: opaque RGB/HSV/hex picker, wheel counter, manual audio mode and stale-write protection, repair reinstall/failure, and sign-in-only automatic updates.");
    }
}
