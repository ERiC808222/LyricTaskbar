using DesktopLyrics.Services;

internal static class DisplayPlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            int barHeight = (int)(48 * scale);
            var display = new DesktopDisplay("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080 - barHeight), new(0, 1080 - barHeight, 1920, 1080), scale, true);
            var placement = DisplayPlacement.OnTaskbar(display);
            check(display.Taskbar.Contains(placement) && placement.Width == (int)(520 * scale) && placement.Height == (int)(40 * scale), $"reset fits actual taskbar pixels at {scale:P0} scaling");
            var oldReset = new PixelBounds((int)(24 * scale), (int)(1036 * scale), (int)(544 * scale), (int)(1076 * scale));
            check(DisplayPlacement.IsVisible(oldReset, new[] { display }) == (scale == 1), $"native bounds detect legacy offscreen reset at {scale:P0} scaling");
        }
        var negative = new DesktopDisplay("left", new(-2560, -200, 0, 1240), new(-2560, -200, 0, 1168), new(-2560, 1168, 0, 1240), 1.5, false);
        var leftPlacement = DisplayPlacement.OnTaskbar(negative);
        check(negative.Taskbar.Contains(leftPlacement) && leftPlacement.Left < 0, "negative monitor origin remains valid and visible");
        var above = new DesktopDisplay("above", new(0, -1440, 2560, 0), new(0, -1440, 2560, -96), new(0, -96, 2560, 0), 2, false);
        check(above.Taskbar.Contains(DisplayPlacement.OnTaskbar(above)), "taskbar on a monitor above the primary screen remains at negative coordinates");
        check(!DisplayPlacement.IsVisible(leftPlacement, new[] { above }), "disconnecting the previous monitor requests position recovery");
        var top = new DesktopDisplay("top", new(0, 0, 1920, 1080), new(0, 60, 1920, 1080), new(0, 0, 1920, 60), 1.25, true);
        check(top.Taskbar.Contains(DisplayPlacement.OnTaskbar(top)), "top taskbar is honored instead of snapping to screen bottom");
        var autoHide = top with { Taskbar = default, WorkArea = top.Bounds };
        check(autoHide.Bounds.Contains(DisplayPlacement.OnTaskbar(autoHide)), "auto-hidden taskbar fallback remains inside the monitor");
        var vertical = top with { Taskbar = new(0, 0, 60, 1080), WorkArea = new(60, 0, 1920, 1080) };
        check(vertical.WorkArea.Contains(DisplayPlacement.OnTaskbar(vertical)), "vertical taskbar places horizontal lyrics inside the work area");
        var small = new DesktopDisplay("small", new(0, 0, 600, 400), new(0, 0, 600, 352), new(0, 352, 600, 400), 2, true);
        check(small.Taskbar.Contains(DisplayPlacement.OnTaskbar(small)), "oversized lyrics are clamped to a small display");
        check(!DisplayPlacement.IsVisible(new(24, 1075, 674, 1125), new[] { top }) && !DisplayPlacement.IsVisible(default, new[] { top }), "clipped and invalid window rectangles cannot pass visibility validation");
    }
}
