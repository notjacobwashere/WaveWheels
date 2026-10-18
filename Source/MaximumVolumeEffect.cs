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

internal readonly record struct SparkStreak(double X,double Y,double TailX,double TailY,double Opacity,double BirthTime,double Lifetime);

internal static class MaximumVolumeEffect
{
    public const int DurationMilliseconds=1000,SparkCount=22;
    public const double ArcStartDegrees=90,ArcSweepDegrees=270,EndStopDegrees=ArcStartDegrees+ArcSweepDegrees;
    private const double EmissionEnd=.45;
    public const double RingRadius=43;
    public static double RenderedArcLevel(double level)
    {
        level=Math.Clamp(level,0,1);
        // Snap the subpixel tail to the physical end stop; drawing and the burst share this cue.
        return (1-level)*RingRadius*ArcSweepDegrees*Math.PI/180<=.35?1:level;
    }
    public static double Glow(double progress)
    {
        progress=Math.Clamp(progress,0,1);
        return Math.Min(1,progress/.08)*Math.Pow(1-progress,2);
    }
    public static double SparkOpacity(double progress)
    {
        return progress<=0||progress>=1?0:Math.Min(1,(1-progress)/.08);
    }
    public static SparkStreak Spark(int index,double progress,double endpointDegrees=EndStopDegrees)
    {
        var birth=Math.Clamp((index+.22*Math.Sin(index*13))*EmissionEnd/(SparkCount-1),0,EmissionEnd);
        if(index==SparkCount-1)birth=EmissionEnd;
        var seed=(index*37%101)/100d;
        // Every streak lives at least 550 ms; later launches naturally finish at the one-second cutoff.
        var lifetime=Math.Min(.55+seed*.45,1-birth);
        var age=progress-birth;
        var alive=progress>=0&&progress<1&&age>=0&&age<lifetime;
        age=Math.Clamp(age,0,lifetime);
        var radial=endpointDegrees*Math.PI/180;
        var direction=radial+(seed-.5)*.4;
        var originX=60+Math.Cos(radial)*RingRadius;var originY=60+Math.Sin(radial)*RingRadius;
        var distance=(30+seed*28)*age*(1-.2*age/lifetime);
        var dx=Math.Cos(direction);var dy=Math.Sin(direction);
        var length=8+seed*5;
        var x=originX+dx*(distance+length);var y=originY+dy*(distance+length);
        var opacity=alive?Math.Min(1,age/.02)*Math.Pow(1-age/lifetime,.65):0;
        // Fixed-length line tails never collapse into circular particles. Each launch is independent.
        return new(x,y,originX+dx*distance,originY+dy*distance,opacity,birth,lifetime);
    }
}
