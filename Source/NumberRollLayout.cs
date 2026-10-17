using System;
using System.Collections.Generic;

namespace WaveWheels;

internal readonly record struct NumberGlyph(char Value,double X,double OffsetY,double Opacity,bool Animated);
internal static class NumberRollLayout
{
    private static bool Numeric(string text)
    {
        if(text.Length is <1 or >2)return false;
        foreach(var character in text)if(character is <'0' or >'9')return false;
        return true;
    }
    public static bool CanSplit(string previous,string current)=>Numeric(previous)&&Numeric(current);
    public static List<NumberGlyph> Glyphs(string previous,string current,double progress,int direction=1)
    {
        var result=new List<NumberGlyph>(4);progress=Math.Clamp(progress,0,1);
        direction=direction<0?-1:1;
        var from=previous.PadLeft(2);var to=current.PadLeft(2);
        // Stable tabular columns keep the units stationary even when a tens digit appears/disappears.
        for(var column=0;column<2;column++)
        {
            var x=53+column*14;
            if(from[column]==to[column]) {if(to[column]!=' ')result.Add(new(to[column],x,0,1,false));continue;}
            if(from[column]!=' ')result.Add(new(from[column],x,direction*12*progress,1-progress,true));
            if(to[column]!=' ')result.Add(new(to[column],x,-direction*12*(1-progress),progress,true));
        }
        return result;
    }
}
