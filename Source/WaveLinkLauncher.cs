using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WaveWheels;

internal sealed record WaveLinkInstallation(string Target,bool Packaged=false);
internal static class WaveLinkLauncher
{
    internal static WaveLinkInstallation? Find(string savedPath)
    {
        if(File.Exists(savedPath)&&Path.GetExtension(savedPath).Equals(".exe",StringComparison.OrdinalIgnoreCase))return new(savedPath);
        var paths=new List<string>();
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)})
            foreach(var relative in new[]{@"Elgato\WaveLink\WaveLink.exe",@"Elgato\Wave Link\WaveLink.exe",@"Programs\Elgato\WaveLink\WaveLink.exe"})paths.Add(Path.Combine(folder,relative));
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        try
        {
            using var root=RegistryKey.OpenBaseKey(hive,view);
            using var app=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\WaveLink.exe");
            if(app?.GetValue("") is string executable)paths.Add(executable.Trim('"'));
            using var uninstall=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if(uninstall!=null)foreach(var name in uninstall.GetSubKeyNames())
            {
                using var entry=uninstall.OpenSubKey(name);var display=entry?.GetValue("DisplayName") as string??"";
                if(!display.Contains("Wave Link",StringComparison.OrdinalIgnoreCase))continue;
                if(entry?.GetValue("InstallLocation") is string location)paths.Add(Path.Combine(location,"WaveLink.exe"));
            }
        }
        catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        var found=paths.FirstOrDefault(File.Exists);if(found!=null)return new(found);
        // Store installations: read the installed manifest rather than guessing its application ID.
        try
        {
            using var packages=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if(packages!=null)foreach(var name in packages.GetSubKeyNames().Where(n=>n.StartsWith("Elgato.WaveLink_",StringComparison.OrdinalIgnoreCase)))
            {
                using var package=packages.OpenSubKey(name);
                if(package?.GetValue("PackageRootFolder") is not string folder)continue;
                var manifest=XDocument.Load(Path.Combine(folder,"AppxManifest.xml"));
                var app=manifest.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Application"&&((string?)e.Attribute("Executable")??"").Contains("WaveLink",StringComparison.OrdinalIgnoreCase));
                var identity=manifest.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Identity");
                var id=(string?)app?.Attribute("Id");var packageName=(string?)identity?.Attribute("Name");
                if(id!=null&&packageName!=null)return new(packageName+"_"+name.Split('_')[^1]+"!"+id,true);
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Security.SecurityException) { }
        return null;
    }
    internal static void Start(WaveLinkInstallation installation)
    {
        Process.Start(installation.Packaged?new ProcessStartInfo("explorer.exe","shell:AppsFolder\\"+installation.Target){UseShellExecute=true}:new ProcessStartInfo(installation.Target){UseShellExecute=true});
    }
}
