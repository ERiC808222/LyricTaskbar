namespace DesktopLyrics.Services;

public static class LyricTiming
{
    public static TimeSpan DisplayPosition(TimeSpan position, int advanceMilliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, position.TotalMilliseconds + Math.Clamp(advanceMilliseconds, -10000, 10000)));
    public static (double Distance, double Delay, double Duration) Marquee(double textWidth, double availableWidth, int delayMs, double lineSeconds)
    {
        double distance = Math.Max(0, textWidth - availableWidth);
        if (distance <= 2 || availableWidth <= 0) return (0, 0, 0);
        double delay = Math.Clamp(delayMs, 0, 2000) / 1000.0;
        double remaining = Math.Max(.4, lineSeconds - delay - .15);
        double duration = Math.Clamp(distance / 40, distance / 180, Math.Max(distance / 180, remaining));
        return (distance, delay, Math.Max(.1, duration));
    }
}
