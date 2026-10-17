using System;
using System.Collections.Generic;

namespace WaveWheels;

internal sealed class ShiftVolumeSnap
{
    internal const int CooldownMilliseconds=180;
    private long lastStep;
    private bool stepped;
    private int stepPercent=20;
    public int StepPercent {get=>stepPercent;set {value=Math.Clamp(value,5,50);if(stepPercent!=value) {stepPercent=value;Reset();}}}
    public static IEnumerable<int> MarkerLevels(int step)
    {
        step=Math.Clamp(step,5,50);
        yield return 0;
        for(var percent=step;percent<100;percent+=step)yield return percent;
        yield return 100;
    }
    public double? Adjust(double delta,double level,long now)
    {
        if(!double.IsFinite(delta)||delta==0||!double.IsFinite(level)||stepped&&now-lastStep<CooldownMilliseconds)return null;
        var percent=Math.Clamp(level,0,1)*100;
        var next=delta>0?(Math.Floor((percent+1e-7)/StepPercent)+1)*StepPercent:(Math.Ceiling((percent-1e-7)/StepPercent)-1)*StepPercent;
        var target=Math.Clamp(next,0,100)/100;
        if(Math.Abs(target-level)<1e-7)return null;
        stepped=true;lastStep=now;return target;
    }
    public void Reset() {stepped=false;lastStep=0;}
}
