using System;
using System.Runtime.InteropServices;

namespace WaveWheels;

internal readonly record struct WindowsAudioState(bool Available,double Level,bool Muted,string Status);
internal interface IWindowsMasterAudio
{
    WindowsAudioState Read();
    WindowsAudioState SetLevel(double level);
    WindowsAudioState SetMuted(bool muted);
}

// Core Audio's default render endpoint follows the user's current Windows output device.
// Objects live only for one operation, so device switches and service restarts cannot leave a stale endpoint.
internal sealed class WindowsMasterAudio:IWindowsMasterAudio
{
    public WindowsAudioState Read()=>Access(null,null);
    public WindowsAudioState SetLevel(double level)=>Access(Math.Clamp(level,0,1),null);
    public WindowsAudioState SetMuted(bool muted)=>Access(null,muted);
    private static WindowsAudioState Access(double? level,bool? muted)
    {
        object? enumerator=null,device=null,volume=null;
        try
        {
            enumerator=new DeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0,1,out var endpoint));device=endpoint;
            var iid=typeof(IEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(endpoint.Activate(ref iid,23,IntPtr.Zero,out var activated));volume=activated;
            var audio=(IEndpointVolume)activated;var context=Guid.Empty;
            if(level.HasValue)Marshal.ThrowExceptionForHR(audio.SetMasterVolumeLevelScalar((float)level.Value,ref context));
            if(muted.HasValue)Marshal.ThrowExceptionForHR(audio.SetMute(muted.Value,ref context));
            Marshal.ThrowExceptionForHR(audio.GetMasterVolumeLevelScalar(out var current));
            Marshal.ThrowExceptionForHR(audio.GetMute(out var isMuted));
            return new(true,current,isMuted,"Windows default playback device");
        }
        catch(Exception ex) when(ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {return new(false,0,false,"No Windows playback device available. Connect an output device; WaveWheels will retry automatically.");}
        finally
        {
            foreach(var value in new[]{volume,device,enumerator})if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);
        }
    }
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
    }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        [PreserveSig]int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig]int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig]int GetChannelCount(out uint channels);
        [PreserveSig]int SetMasterVolumeLevel(float level,ref Guid context);
        [PreserveSig]int SetMasterVolumeLevelScalar(float level,ref Guid context);
        [PreserveSig]int GetMasterVolumeLevel(out float level);
        [PreserveSig]int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]int SetChannelVolumeLevel(uint channel,float level,ref Guid context);
        [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);
        [PreserveSig]int GetChannelVolumeLevel(uint channel,out float level);
        [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float level);
        [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);
        [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
    }
}
