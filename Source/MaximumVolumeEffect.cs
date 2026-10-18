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
    public const int DurationMilliseconds=1800,SparkCount=28;
    public const double ArcStartDegrees=90,ArcSweepDegrees=270,EndStopDegrees=ArcStartDegrees+ArcSweepDegrees;
    private const double EmissionEnd=.76;
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
        var fade=Math.Clamp((progress-.45)/.55,0,1);
        return Math.Min(1,progress/.045)*(1-fade*fade*(3-2*fade));
    }
    public static double SparkOpacity(double progress)
    {
        return progress<=0||progress>=1?0:Math.Min(1,(1-progress)/.08);
    }
    private static double Random(int index,int burstSeed,int salt)
    {
        var value=Math.Sin(index*127.1+burstSeed*311.7+salt*74.7)*43758.5453;
        return value-Math.Floor(value);
    }
    internal static double SpawnDegrees(int index,int burstSeed=0)=>ArcStartDegrees+ArcSweepDegrees*Random(index,burstSeed,1);
    public static SparkStreak Spark(int index,double progress,int burstSeed=0)
    {
        var birth=Math.Clamp((index+.22*Math.Sin(index*13))*EmissionEnd/(SparkCount-1),0,EmissionEnd);
        if(index==SparkCount-1)birth=EmissionEnd;
        var seed=Random(index,burstSeed,2);
        var lifetime=Math.Min(.16+seed*.08,1-birth);
        if(index==SparkCount-1)lifetime=1-birth;
        var age=progress-birth;
        var alive=progress>=0&&progress<1&&age>=0&&age<lifetime;
        age=Math.Clamp(age,0,lifetime);
        var radial=SpawnDegrees(index,burstSeed)*Math.PI/180;
        // Follow the clockwise tangent of the drawn arc at this particle's own spawn point.
        var direction=radial+Math.PI/2+(seed-.5)*.14;
        var originX=60+Math.Cos(radial)*RingRadius;var originY=60+Math.Sin(radial)*RingRadius;
        var distance=(115+seed*70)*age*(DurationMilliseconds/1000d);
        var dx=Math.Cos(direction);var dy=Math.Sin(direction);
        var length=10+seed*6;
        var x=originX+dx*(distance+length);var y=originY+dy*(distance+length);
        var fade=Math.Clamp((age/lifetime-.55)/.45,0,1);
        var opacity=alive?Math.Min(1,age/.025)*(1-fade*fade):0;
        // Fixed-length line tails never collapse into circular particles. Each launch is independent.
        return new(x,y,originX+dx*distance,originY+dy*distance,opacity,birth,lifetime);
    }
}
