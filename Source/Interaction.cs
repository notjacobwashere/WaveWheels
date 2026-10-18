using System;

namespace WaveWheels;

internal readonly record struct ScrollRoute(bool Reveal, int Index);

// Decide on the hook thread so ignored opening events cannot become volume writes
// when the dispatcher eventually processes them. Every opening scroll extends the pause.
internal sealed class OverlayInputRouter
{
    internal const int PauseMilliseconds = 250;
    private readonly object sync = new();
    private bool open, waiting;
    private long lastOpeningScroll;
    public ScrollRoute Route(int hoveredIndex, int selectedIndex, long now)
    {
        lock (sync)
        {
            if (!open) { open = true; waiting = true; lastOpeningScroll = now; return new(true, -1); }
            if (waiting && now - lastOpeningScroll < PauseMilliseconds)
            {
                lastOpeningScroll = now;
                return new(false, -1);
            }
            waiting = false;
            // The activation strip only reveals/keeps the overlay open. It is never a fader.
            return new(false, hoveredIndex);
        }
    }
    public bool CanAdjust(long now)
    {
        lock (sync)
        {
            if (waiting && now - lastOpeningScroll >= PauseMilliseconds) waiting = false;
            return open && !waiting;
        }
    }
    public void ObserveUnclaimedScroll(long now)
    {
        lock (sync)
        {
            if (!open || !waiting) return;
            if (now - lastOpeningScroll < PauseMilliseconds) lastOpeningScroll = now;
            else waiting = false;
        }
    }
    public void Preview() { lock (sync) { open = true; waiting = false; } }
    public void Close() { lock (sync) { open = false; waiting = false; } }
}

internal readonly record struct OverlaySize(double Width, double Height, double Scale);
internal static class OverlaySizing
{
    public const double EffectPadding=48,StackWidth=300+EffectPadding*2;
    public static double StackHeight(int count, int spacing = 0) => Math.Clamp(count, 1, 12) * 120 + EffectPadding*2 + (Math.Clamp(count,1,12)-1)*Math.Clamp(spacing,0,36);
    public static OverlaySize Calculate(int count, int percent, double availableWidth, double availableHeight, int spacing = 0)
    {
        var height = StackHeight(count, spacing);
        var scale = Math.Min(Math.Clamp(percent, 50, 180) / 100d,
            Math.Min(Math.Max(1, availableHeight - 32) / height, Math.Max(1, availableWidth - 32) / StackWidth));
        return new(StackWidth * scale, height * scale, scale);
    }
}

internal readonly record struct EdgeRegion(int Left, int Top, int Right, int Bottom, double Dpi)
{
    public bool Contains(int x,int y) => x>=Left&&x<Right&&y>=Top&&y<Bottom;
    public bool IsTrigger(int x,int y,string edge,int width)
    {
        if(!Contains(x,y))return false;
        var pixels=Math.Max(1,(int)Math.Ceiling(width*Dpi));
        return edge switch { "Right"=>x>=Right-pixels,"Top"=>y<Top+pixels,"Bottom"=>y>=Bottom-pixels,_=>x<Left+pixels };
    }
}

internal static class WheelOrdering
{
    // insertion is a gap in the original list: 0 before the first item, count after the last.
    public static int Destination(int source, int insertion, int count)
    {
        if (count <= 0 || source < 0 || source >= count) return -1;
        insertion = Math.Clamp(insertion, 0, count);
        return Math.Clamp(insertion > source ? insertion - 1 : insertion, 0, count - 1);
    }
}
