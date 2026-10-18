using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WaveWheels;

internal sealed record HotkeyCommand(uint Modifiers,uint Key,HotkeyBinding Binding);
internal sealed class GlobalHotkeys:IDisposable
{
    internal static readonly string[] Actions={"Toggle wheels","Toggle mute","Volume up","Volume down","Next profile","Previous profile"};
    private readonly HwndSource? source;
    private readonly Action<HotkeyBinding> execute;
    private readonly Func<int,uint,uint,bool> register;
    private readonly Action<int> unregister;
    private List<HotkeyCommand> active=new();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window,int id);
    public GlobalHotkeys(Action<HotkeyBinding> execute)
    {
        this.execute=execute;
        source=new HwndSource(new HwndSourceParameters("WaveWheels hotkeys"){ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0});source.AddHook(Hook);
        register=(id,modifiers,key)=>RegisterHotKey(source.Handle,id,modifiers,key);unregister=id=>UnregisterHotKey(source.Handle,id);
    }
    internal GlobalHotkeys(Action<HotkeyBinding> execute,Func<int,uint,uint,bool> register,Action<int> unregister) {this.execute=execute;this.register=register;this.unregister=unregister;}
    internal static bool TryPlan(IEnumerable<HotkeyBinding> bindings,out List<HotkeyCommand> plan,out string error)
    {
        plan=new();error="";var used=new HashSet<(uint,uint)>();
        foreach(var binding in bindings.Take(21))
        {
            if(plan.Count>=20) {error="Use at most 20 shortcuts.";return false;}
            if(string.IsNullOrWhiteSpace(binding.Shortcut))continue;
            if(!Actions.Contains(binding.Action)) {error="Choose an action for every shortcut.";return false;}
            var parts=binding.Shortcut.Split('+',StringSplitOptions.TrimEntries);uint modifiers=0;
            for(var i=0;i<parts.Length-1;i++)
            {
                var flag=parts[i].ToUpperInvariant() switch {"CTRL" or "CONTROL"=>2u,"ALT"=>1u,"SHIFT"=>4u,"WIN" or "WINDOWS"=>8u,_=>0u};
                if(flag==0||(modifiers&flag)!=0) {error="Use Ctrl, Alt, Shift, or Win followed by a key, such as Ctrl+Alt+W.";return false;}modifiers|=flag;
            }
            if((modifiers&11)==0||!Enum.TryParse<Key>(parts[^1],true,out var key)||!Enum.IsDefined(key)||key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {error="Each shortcut needs Ctrl, Alt, or Win plus a key, such as Ctrl+Alt+W.";return false;}
            var virtualKey=(uint)KeyInterop.VirtualKeyFromKey(key);if(virtualKey==0) {error="That key cannot be used as a shortcut.";return false;}
            if(!used.Add((modifiers,virtualKey))) {error="Two actions use the same shortcut. Choose a different combination.";return false;}
            plan.Add(new(modifiers,virtualKey,binding.Copy()));
        }
        return true;
    }
    public bool Apply(IReadOnlyList<HotkeyCommand> commands,out string error)
    {
        var previous=active.ToList();Clear();error="";
        for(var i=0;i<commands.Count;i++)
        {
            var command=commands[i];if(register(i+1,command.Modifiers|0x4000,command.Key)) {active.Add(command);continue;}
            error=$"{command.Binding.Shortcut} is unavailable or reserved by Windows. Choose another shortcut.";
            Clear();
            for(var j=0;j<previous.Count;j++)
                if(register(j+1,previous[j].Modifiers|0x4000,previous[j].Key))active.Add(previous[j]);
                else {Clear();error+=" Previous shortcuts could not be restored; apply them again.";break;}
            return false;
        }
        return true;
    }
    private IntPtr Hook(IntPtr window,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x312) {Dispatch(wp.ToInt32());handled=true;}return IntPtr.Zero;
    }
    internal void Dispatch(int id) {var index=id-1;if(index>=0&&index<active.Count)execute(active[index].Binding.Copy());}
    private void Clear() {for(var i=0;i<active.Count;i++)unregister(i+1);active.Clear();}
    public void Dispose() {Clear();source?.RemoveHook(Hook);source?.Dispose();}
}
