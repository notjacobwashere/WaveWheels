using System;

namespace WaveWheels;

internal sealed class ScrollAccumulator
{
    private double remainder,acceleration=1;
    private long lastScroll;
    private int direction,step=1;
    private bool accelerate=true;
    public int Adjust(double delta,int stepPercent,bool accelerationEnabled,long now)
    {
        if(!double.IsFinite(delta)||delta==0)return 0;
        stepPercent=Math.Clamp(stepPercent,1,10);
        if(step!=stepPercent||accelerate!=accelerationEnabled)Reset();
        step=stepPercent;accelerate=accelerationEnabled;
        var sign=Math.Sign(delta);
        if(now-lastScroll>150||sign!=direction)acceleration=1;
        if(direction!=0&&sign!=direction)remainder=0;
        direction=sign;lastScroll=now;
        // Accumulate notches, then apply the configured step. Fractional hardware input
        // must not turn a 10% step into an arbitrary 8% change.
        remainder+=Math.Clamp(delta,-1000,1000)*acceleration;
        acceleration=accelerate?Math.Min(5,acceleration*1.8):1;
        var result=(int)Math.Truncate(remainder+Math.Sign(remainder)*1e-7);
        remainder-=result;
        return result*step;
    }
    public void Reset() {remainder=0;acceleration=1;lastScroll=0;direction=0;}
}
