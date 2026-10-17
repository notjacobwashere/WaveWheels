using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace WaveWheels;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point Point; public uint MouseDataValue, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public IntPtr HWnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Point; public uint Private; }
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? module);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static bool ShiftDown=>(GetAsyncKeyState(0x10)&0x8000)!=0;
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

public sealed class EdgeHook : IDisposable
{
    public Func<int,int,int,bool>? WheelInput;
    public Action<string>? Failed;
    private readonly Thread thread;
    private readonly Native.HookProc callback;
    private uint threadId;
    private IntPtr hook;
    public EdgeHook()
    {
        callback = OnHook;
        thread = new Thread(Run) { IsBackground = true, Name = "WaveWheels edge input" };
        thread.SetApartmentState(ApartmentState.STA);
    }
    public void Start() => thread.Start();
    private void Run()
    {
        threadId = Native.GetCurrentThreadId();
        hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) { Failed?.Invoke(new Win32Exception(Marshal.GetLastWin32Error()).Message); return; }
        try { while (Native.GetMessage(out var message, IntPtr.Zero,0,0) > 0) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); } }
        finally { Native.UnhookWindowsHookEx(hook); }
    }
    internal IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() == 0x20A)
        {
            try
            {
                var input = Marshal.PtrToStructure<Native.MouseData>(data);
                var delta = unchecked((short)(input.MouseDataValue >> 16));
                if (delta != 0 && WheelInput?.Invoke(input.Point.X,input.Point.Y,delta) == true) return new IntPtr(1);
            }
            catch (Exception ex) { Failed?.Invoke(ex.Message); }
        }
        return Native.CallNextHookEx(hook,code,message,data);
    }
    public void Dispose() { if (threadId != 0) Native.PostThreadMessage(threadId,0x12,UIntPtr.Zero,IntPtr.Zero); }
}
