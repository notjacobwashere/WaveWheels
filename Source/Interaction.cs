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
            return new(false, hoveredIndex >= 0 ? hoveredIndex : Math.Max(0, selectedIndex));
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
    public const double StackWidth = 328;
    public static double StackHeight(int count) => Math.Clamp(count, 1, 12) * 120 + 24;
    public static OverlaySize Calculate(int count, int percent, double availableWidth, double availableHeight)
    {
        var height = StackHeight(count);
        var scale = Math.Min(Math.Clamp(percent, 50, 180) / 100d,
            Math.Min(Math.Max(1, availableHeight - 32) / height, Math.Max(1, availableWidth - 32) / 350));
        return new(350 * scale, height * scale, scale);
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
