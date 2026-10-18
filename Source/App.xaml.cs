using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WaveWheels;

public partial class App : Application
{
    private MainViewModel? model;
    private SettingsWindow? configuration;
    private OverlayWindow? overlay;
    private Forms.NotifyIcon? tray;
    private Mutex? instance;
    private EventWaitHandle? settingsSignal;
    private EventWaitHandle? exitSignal;
    private RegisteredWaitHandle? signalWait;
    private RegisteredWaitHandle? exitWait;
    private static string? logPath;
    public static bool Exiting { get; private set; }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataIndex=Array.IndexOf(e.Args,"--data-dir");
        var isolated=dataIndex>=0 && dataIndex+1<e.Args.Length;
        var data=isolated?Path.GetFullPath(e.Args[dataIndex+1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WaveWheels");
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToUpperInvariant())))[..16];
        if(e.Args.Contains("--shutdown"))
        {
            // Setup requests a graceful exit so pending preferences are saved before upgrades.
            try { using var signal=EventWaitHandle.OpenExisting(@"Local\WaveWheels-Exit-"+identity);signal.Set(); }
            catch(WaitHandleCannotBeOpenedException) { }
            Shutdown();return;
        }
        instance=new Mutex(true,@"Local\WaveWheels-"+identity,out var isNew);
        settingsSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\WaveWheels-Settings-"+identity);
        if(!isNew)
        {
            // Older releases otherwise swallow a launch of the new executable silently.
            var older = FindDifferentRunningVersion();
            if (older != null)
                MessageBox.Show($"WaveWheels {older} is still running. Quit WaveWheels from its system tray menu, then open this 0.9.0-beta.2 version again. Your saved wheels will be kept.", "WaveWheels 0.9.0-beta.2 update", MessageBoxButton.OK, MessageBoxImage.Information);
            else if(e.Args.Contains("--settings")) settingsSignal.Set();
            Shutdown();return;
        }
        try {Directory.CreateDirectory(data);logPath=Path.Combine(data,"app.log");}catch{ }
        DispatcherUnhandledException+=(_,error)=> { WriteLog(error.Exception.ToString()); if(model!=null)model.Message="An operation failed. See app.log for details.";error.Handled=true; };
        model=new MainViewModel(new SettingsStore(data),isolated);
        model.UpdateInstallationStarted+=()=>Shutdown();
        exitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\WaveWheels-Exit-"+identity);
        overlay=new OverlayWindow(model);
        CreateTray();
        model.UpdateAvailable+=version=> { if(tray!=null) {tray.BalloonTipTitle="WaveWheels update";tray.BalloonTipText="Version "+version+" is available. Open Updates in configuration to download it.";tray.ShowBalloonTip(8000);} };
        signalWait=ThreadPool.RegisterWaitForSingleObject(settingsSignal,(_,_)=>Dispatcher.BeginInvoke(OpenConfiguration),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exitSignal,(_,_)=>Dispatcher.BeginInvoke(()=>Shutdown()),null,Timeout.Infinite,false);
        model.Start();overlay.StartHook();
        if(e.Args.Contains("--settings"))OpenConfiguration();
        if(e.Args.Contains("--show-overlay"))overlay.ShowAtCursor();
        // No StartupUri: normal and sign-in launches remain in the system tray.
        WriteLog("Started " + (isolated?"isolated profile":"normal profile"));
    }
    private void CreateTray()
    {
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Open configuration · 0.9.0-beta.2",null,(_,_)=>Dispatcher.BeginInvoke(OpenConfiguration));
        menu.Items.Add("Show wheels",null,(_,_)=>Dispatcher.BeginInvoke(()=>overlay?.ShowAtCursor()));
        var profilesMenu=new Forms.ToolStripMenuItem("Profiles");
        void RefreshProfiles()
        {
            while(profilesMenu.DropDownItems.Count>0) {var item=profilesMenu.DropDownItems[0];profilesMenu.DropDownItems.RemoveAt(0);item.Dispose();}
            foreach(var profile in model!.Profiles)
            {
                var item=new Forms.ToolStripMenuItem(profile.Name) {Checked=profile==model.ActiveProfile};
                item.Click+=(_,_)=>model.ActiveProfile=profile;profilesMenu.DropDownItems.Add(item);
            }
        }
        RefreshProfiles();menu.Items.Add(profilesMenu);
        foreach(var profile in model!.Profiles)profile.PropertyChanged+=(_,_)=>RefreshProfiles();
        model.Profiles.CollectionChanged+=(_,e)=> {if(e.NewItems!=null)foreach(WheelProfile profile in e.NewItems)profile.PropertyChanged+=(_,_)=>RefreshProfiles();RefreshProfiles();};
        model.PropertyChanged+=(_,e)=> {if(e.PropertyName==nameof(model.ActiveProfile))RefreshProfiles();};
        menu.Items.Add("Check for updates",null,async(_,_)=>await model.CheckForUpdatesAsync());
        var enabled=new Forms.ToolStripMenuItem("Enable edge scrolling"){Checked=model!.EdgeEnabled,CheckOnClick=true};
        enabled.CheckedChanged+=(_,_)=> {model.EdgeEnabled=enabled.Checked;};
        model.PropertyChanged+=(_,e)=> {if(e.PropertyName==nameof(model.EdgeEnabled))enabled.Checked=model.EdgeEnabled;};
        menu.Items.Add(enabled);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit WaveWheels",null,(_,_)=>Dispatcher.BeginInvoke(()=>Shutdown()));
        tray=new Forms.NotifyIcon {Text="WaveWheels 0.9.0-beta.2 · scroll at the left screen edge",Icon=CreateIcon(),ContextMenuStrip=menu,Visible=true};
        tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(OpenConfiguration);};
    }
    private static string? FindDifferentRunningVersion()
    {
        foreach(var process in Process.GetProcessesByName("WaveWheels"))
        {
            using(process)
            {
                if(process.Id == Environment.ProcessId) continue;
                try
                {
                    var version = process.MainModule?.FileVersionInfo;
                    if(version != null && (version.FileMajorPart != 0 || version.FileMinorPart != 8 || version.FileBuildPart != 1))
                        return $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
                }
                catch { }
            }
        }
        return null;
    }
    private static Icon CreateIcon()
    {
        using var bitmap=new Bitmap(32,32);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var background=new SolidBrush(System.Drawing.Color.FromArgb(18,30,46));
            g.FillEllipse(background,0,0,32,32);
            using var pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(89,217,231),3.4f);
            g.DrawArc(pen,5,5,22,22,90,270);
            using var dot=new SolidBrush(System.Drawing.Color.FromArgb(225,239,250));
            g.FillEllipse(dot,13,13,6,6);
        }
        var h=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}
    }
    private void OpenConfiguration()
    {
        if(model==null)return;
        configuration??=new SettingsWindow(model);
        configuration.Show();configuration.WindowState=WindowState.Normal;configuration.Activate();
    }
    public static void WriteLog(string line)
    {
        try {if(logPath==null)return;lock(typeof(App)){if(File.Exists(logPath)&&new FileInfo(logPath).Length>1_000_000)File.Move(logPath,logPath+".previous",true);File.AppendAllText(logPath,DateTimeOffset.Now.ToString("O")+" "+line+Environment.NewLine);}}catch{ }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Exiting=true;signalWait?.Unregister(null);exitWait?.Unregister(null);overlay?.Close();configuration?.Close();model?.Dispose();
        if(tray!=null){tray.Visible=false;tray.Icon?.Dispose();tray.Dispose();}
        settingsSignal?.Dispose();exitSignal?.Dispose();instance?.Dispose();base.OnExit(e);
    }
}
