using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WaveWheels;

public sealed class EffectSettings:Observable
{
    private bool performanceMode;
    public bool PerformanceMode {get=>performanceMode;set=>Set(ref performanceMode,value);}
    private bool overlayEnabled=true;
    public bool OverlayEnabled {get=>overlayEnabled;set=>Set(ref overlayEnabled,value);}
    private bool interfaceEnabled=true;
    public bool InterfaceEnabled {get=>interfaceEnabled;set=>Set(ref interfaceEnabled,value);}
    private bool hoverEnabled=true;
    public bool HoverEnabled {get=>hoverEnabled;set=>Set(ref hoverEnabled,value);}
    private bool arcEnabled=true;
    public bool ArcEnabled {get=>arcEnabled;set=>Set(ref arcEnabled,value);}
    private bool numbersEnabled=true;
    public bool NumbersEnabled {get=>numbersEnabled;set=>Set(ref numbersEnabled,value);}
    private bool volumeBobEnabled=true;
    public bool VolumeBobEnabled {get=>volumeBobEnabled;set=>Set(ref volumeBobEnabled,value);}
    private bool shiftEnabled=true;
    public bool ShiftEnabled {get=>shiftEnabled;set=>Set(ref shiftEnabled,value);}
    private bool muteEnabled=true;
    public bool MuteEnabled {get=>muteEnabled;set=>Set(ref muteEnabled,value);}
    private bool unmuteEnabled=true;
    public bool UnmuteEnabled {get=>unmuteEnabled;set=>Set(ref unmuteEnabled,value);}
    private bool limitEnabled=true;
    public bool LimitEnabled {get=>limitEnabled;set=>Set(ref limitEnabled,value);}
    private bool clickEnabled=true;
    public bool ClickEnabled {get=>clickEnabled;set=>Set(ref clickEnabled,value);}
    private bool maximumEnabled=true;
    public bool MaximumEnabled {get=>maximumEnabled;set=>Set(ref maximumEnabled,value);}
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
    public void Reset() {var d=new EffectSettings();PerformanceMode=false;OverlayEnabled=true;InterfaceEnabled=true;HoverEnabled=true;ArcEnabled=true;NumbersEnabled=true;VolumeBobEnabled=true;ShiftEnabled=true;MuteEnabled=true;UnmuteEnabled=true;LimitEnabled=true;ClickEnabled=true;MaximumEnabled=true;BloomPercent=d.BloomPercent;SparkCount=d.SparkCount;SparkSpeedPercent=d.SparkSpeedPercent;ShakePercent=d.ShakePercent;MuteSpeedPercent=d.MuteSpeedPercent;NormalColor=d.NormalColor;MutedColor=d.MutedColor;MutedButtonColor=d.MutedButtonColor;}
}
