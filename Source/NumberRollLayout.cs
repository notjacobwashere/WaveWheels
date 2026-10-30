using System;
using System.Collections.Generic;
using System.Linq;

namespace WaveWheels;

internal readonly record struct NumberGlyph(char Value,double X,double OffsetY,double Opacity,bool Animated);
internal static class NumberRollLayout
{
    internal const double Travel=26;
    private static bool Numeric(string text)=>text.Length is >=1 and <=2&&text.All(c=>c is >= '0' and <= '9');
    public static bool CanSplit(string previous,string current)=>Numeric(previous)&&Numeric(current);
    public static List<NumberGlyph> Glyphs(string previous,string current,double progress,int direction=1)
    {
        var source=new List<NumberGlyph>();var text=previous.PadLeft(2);
        for(var column=0;column<2;column++)if(text[column]!=' ')source.Add(new(text[column],53+column*14,0,1,false));
        return Tween(source,current,progress,direction);
    }
    internal static List<NumberGlyph> Dominant(IEnumerable<NumberGlyph> frame)=>frame.GroupBy(g=>g.X).Select(c=>c.OrderByDescending(g=>g.Opacity).First()).ToList();
    internal static List<NumberGlyph> Tween(IReadOnlyList<NumberGlyph> source,string current,double progress,int direction)
    {
        var result=new List<NumberGlyph>(4);var p=Math.Clamp(progress,0,1);var target=current.PadLeft(2);direction=direction<0?-1:1;
        for(var column=0;column<2;column++)
        {
            var x=53+column*14;var old=source.Where(g=>g.X==x).OrderByDescending(g=>g.Opacity).ToArray();var hasOld=old.Length>0;var from=hasOld?old[0]:default;var to=target[column];
            if(hasOld&&from.Value==to)
            {
                result.Add(new(to,x,from.OffsetY*(1-p),from.Opacity+(1-from.Opacity)*p,Math.Abs(from.OffsetY)>.001||from.Opacity<.999));continue;
            }
            if(hasOld)result.Add(new(from.Value,x,from.OffsetY+(-direction*Travel-from.OffsetY)*p,from.Opacity*(1-p),true));
            if(to!=' ')result.Add(new(to,x,direction*Travel*(1-p),p,true));
        }
        return result;
    }
}
