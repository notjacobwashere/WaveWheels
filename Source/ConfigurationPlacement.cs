using System;
using System.Windows;

namespace WaveWheels;

public sealed record ConfigurationPlacement(string MonitorId,double X,double Y,double Width,double Height,double Dpi,bool Maximized=false)
{
    internal bool Valid=>!string.IsNullOrEmpty(MonitorId)&&double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Width)&&double.IsFinite(Height)&&double.IsFinite(Dpi)&&Width>0&&Height>0&&Dpi>=48&&Dpi<=768;
    internal Rect Fit(Rect work,double dpi)
    {
        var scale=dpi/Dpi;var width=Math.Min(work.Width,Math.Max(300,Width*scale));var height=Math.Min(work.Height,Math.Max(240,Height*scale));
        return new Rect(Math.Clamp(work.X+X*scale,work.Left,work.Right-width),Math.Clamp(work.Y+Y*scale,work.Top,work.Bottom-height),width,height);
    }
}
