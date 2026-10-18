using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WaveWheels;

public sealed class EffectSettings:Observable
{
    private int bloom=100,sparks=180,speed=100,shake=100,muteSpeed=100;
    private string normal="#11141B",muted="#26050D",button="#460715";
    public int BloomPercent {get=>bloom;set=>Set(ref bloom,Math.Clamp(value,0,200));}
    public int SparkCount {get=>sparks;set=>Set(ref sparks,Math.Clamp(value,0,400));}
    public int SparkSpeedPercent {get=>speed;set=>Set(ref speed,Math.Clamp(value,25,300));}
    public int ShakePercent {get=>shake;set=>Set(ref shake,Math.Clamp(value,0,200));}
    public int MuteSpeedPercent {get=>muteSpeed;set=>Set(ref muteSpeed,Math.Clamp(value,50,200));}
    public string NormalColor {get=>normal;set=>Set(ref normal,value??"");}
    public string MutedColor {get=>muted;set=>Set(ref muted,value??"");}
    public string MutedButtonColor {get=>button;set=>Set(ref button,value??"");}
    public static bool ValidColor(string? text)=>text!=null&&Regex.IsMatch(text,"^#[0-9a-fA-F]{6}$");
    internal static Color ColorOr(string text,string fallback)=>(Color)ColorConverter.ConvertFromString(ValidColor(text)?text:fallback);
    public void Normalize()
    {
        BloomPercent=bloom;SparkCount=sparks;SparkSpeedPercent=speed;ShakePercent=shake;MuteSpeedPercent=muteSpeed;
        if(!ValidColor(normal))NormalColor="#11141B";if(!ValidColor(muted))MutedColor="#26050D";if(!ValidColor(button))MutedButtonColor="#460715";
    }
    public void Reset() {var d=new EffectSettings();BloomPercent=d.BloomPercent;SparkCount=d.SparkCount;SparkSpeedPercent=d.SparkSpeedPercent;ShakePercent=d.ShakePercent;MuteSpeedPercent=d.MuteSpeedPercent;NormalColor=d.NormalColor;MutedColor=d.MutedColor;MutedButtonColor=d.MutedButtonColor;}
}

public sealed class HotkeyBinding:Observable
{
    private string shortcut="",action="Toggle wheels",wheelId="";
    public string Shortcut {get=>shortcut;set=>Set(ref shortcut,value??"");}
    public string Action {get=>action;set=>Set(ref action,value??"");}
    public string WheelId {get=>wheelId;set=>Set(ref wheelId,value??"");}
    public HotkeyBinding Copy()=>new(){Shortcut=Shortcut,Action=Action,WheelId=WheelId};
    public static List<HotkeyBinding> Defaults()=>new(){new(){Action="Toggle wheels"},new(){Action="Toggle mute"},new(){Action="Next profile"},new(){Action="Previous profile"}};
}
