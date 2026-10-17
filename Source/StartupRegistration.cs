using System;
using System.IO;
using Microsoft.Win32;

namespace WaveWheels;

public sealed class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "WaveWheels";
    public bool IsRegistered
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(EntryName) is string; }
    }
    public string Description
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            if (IsRegistered && key?.GetValue(EntryName) is byte[] bytes && bytes.Length > 0 && bytes[0] == 3)
                return "Registered, but disabled by Windows. Enable WaveWheels in Task Manager → Startup apps.";
            return IsRegistered ? "Registered for your Windows sign-in. Manage it in Task Manager → Startup apps." : "Off. Enable to add WaveWheels to Task Manager → Startup apps.";
        }
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new IOException("The executable path could not be resolved.");
            if (!exe.EndsWith("WaveWheels.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Launch WaveWheels.exe before registering startup.");
            var command = "\"" + exe + "\"";
            if (command.Length > 260) throw new IOException("Move the app to a shorter folder path before enabling startup.");
            key.SetValue(EntryName, command, RegistryValueKind.String);
        }
        else key.DeleteValue(EntryName, false);
    }
}
