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
    public const int DurationMilliseconds=1800,SparkCount=180;
    public const double ArcStartDegrees=90,ArcSweepDegrees=270,EndStopDegrees=ArcStartDegrees+ArcSweepDegrees;
    private const double EmissionEnd=.9;
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
    internal static double TravelTime(double age,double lifetime)
    {
        age=Math.Clamp(age,0,lifetime);var half=lifetime*.5;
        if(age<=half)return age;
        var rate=3/half;
        return half+(1-Math.Exp(-rate*(age-half)))/rate;
    }
    public static SparkStreak Spark(int index,double progress,int burstSeed=0,double arcLevel=1,int count=SparkCount,double speedScale=1)
    {
        var birth=count<=1?0:Math.Clamp((index+.22*Math.Sin(index*13))*EmissionEnd/(count-1),0,EmissionEnd);
        if(count>1&&index==count-1)birth=EmissionEnd;
        var seed=Random(index,burstSeed,2);
        var lifetime=Math.Min((.09+seed*.04)/Math.Max(1,speedScale),1-birth);
        if(count>1&&index==count-1)lifetime=(1-birth)/Math.Max(1,speedScale);
        var age=progress-birth;
        var alive=progress>=0&&progress<1&&age>=0&&age<lifetime;
        age=Math.Clamp(age,0,lifetime);
        var radial=(ArcStartDegrees+(SpawnDegrees(index,burstSeed)-ArcStartDegrees)*Math.Clamp(arcLevel,0,1))*Math.PI/180;
        // Follow the clockwise tangent of the drawn arc at this particle's own spawn point.
        var direction=radial+Math.PI/2+(seed-.5)*.14;
        var originX=60+Math.Cos(radial)*RingRadius;var originY=60+Math.Sin(radial)*RingRadius;
        var distance=(240+seed*120)*speedScale*TravelTime(age,lifetime)*(DurationMilliseconds/1000d);
        var dx=Math.Cos(direction);var dy=Math.Sin(direction);
        var length=3+seed*3;
        var x=originX+dx*(distance+length);var y=originY+dy*(distance+length);
        var fade=Math.Clamp((age/lifetime-.55)/.45,0,1);
        var opacity=alive?Math.Min(1,age/.025)*(1-fade*fade):0;
        // Fixed-length line tails never collapse into circular particles. Each launch is independent.
        return new(x,y,originX+dx*distance,originY+dy*distance,opacity,birth,lifetime);
    }
}
