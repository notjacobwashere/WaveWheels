using System;

namespace WaveWheels;

internal sealed class MaximumVolumeTransition
{
    private bool available;
    private double level;
    public void Reset(bool connected,double value) {available=connected;level=value;}
    public bool Observe(bool connected,double value,bool animate)
    {
        var reached=available&&connected&&level<1&&value>=1;
        Reset(connected,value);return reached&&animate;
    }
}

internal static class MaximumVolumeEffect
{
    public const int DurationMilliseconds=1000,SparkCount=12;
    public const double SparkDuration=.55;
    public const double RingRadius=43;
    public static double RenderedArcLevel(double level)
    {
        level=Math.Clamp(level,0,1);
        // Snap the subpixel tail to the physical end stop; drawing and the burst share this cue.
        return (1-level)*RingRadius*1.5*Math.PI<=.35?1:level;
    }
    public static double Glow(double progress)
    {
        progress=Math.Clamp(progress,0,1);
        return Math.Min(1,progress/.08)*Math.Pow(1-progress,2);
    }
    public static double SparkOpacity(double progress)
    {
        var burst=Math.Clamp(progress/SparkDuration,0,1);
        return Math.Min(1,burst/.02)*Math.Pow(1-burst,2);
    }
    public static (double X,double Y,double TailX,double TailY) Spark(int index,double progress)
    {
        progress=Math.Clamp(progress/SparkDuration,0,1);
        var angle=index*Math.PI*2/SparkCount+.12*Math.Sin(index*7);
        var travel=1-Math.Pow(1-progress,3);
        var radius=44+(10+index%4)*travel;
        var tail=(2+index%3)*(1-progress);
        return (60+Math.Cos(angle)*radius,60+Math.Sin(angle)*radius,60+Math.Cos(angle)*(radius-tail),60+Math.Sin(angle)*(radius-tail));
    }
}
