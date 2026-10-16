using System.Runtime.InteropServices;
using DesktopLyrics.Utils;

namespace DesktopLyrics.Services;

// These rectangles are native screen pixels. WPF Left/Top are never compared with them.
public readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(PixelBounds other) => Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
    public PixelBounds Intersect(PixelBounds other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    public static PixelBounds From(Win32Helper.RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}

public sealed record DesktopDisplay(string Name, PixelBounds Bounds, PixelBounds WorkArea, PixelBounds Taskbar, double Scale, bool Primary);

public static class DisplayPlacement
{
    public static bool IsVisible(PixelBounds window, IEnumerable<DesktopDisplay> displays) => displays.Any(display => display.Bounds.Contains(window));

    public static PixelBounds OnTaskbar(DesktopDisplay display, double widthDip = 520, double heightDip = 40)
    {
        var bounds = display.Bounds;
        double scale = double.IsFinite(display.Scale) && display.Scale > 0 ? display.Scale : 1;
        int Pixels(double dip) => (int)Math.Round(dip * scale);
        var bar = display.Taskbar.Intersect(bounds);
        if (bar.Width < Pixels(100) || bar.Height < Pixels(16) || bar.Height > bounds.Height / 3)
        {
            // A vertical taskbar cannot fit horizontal lyrics. Place them along the work area's bottom.
            var area = display.WorkArea.Width > 0 && display.WorkArea.Height > 0 ? display.WorkArea : bounds;
            if (display.Taskbar.Height > bounds.Height / 3 && display.Taskbar.Width > 0)
                bar = new(area.Left, area.Bottom - Pixels(48), area.Right, area.Bottom);
            else if (display.WorkArea.Bottom < bounds.Bottom)
                bar = new(bounds.Left, display.WorkArea.Bottom, bounds.Right, bounds.Bottom);
            else if (display.WorkArea.Top > bounds.Top)
                bar = new(bounds.Left, bounds.Top, bounds.Right, display.WorkArea.Top);
            else
                bar = new(bounds.Left, bounds.Bottom - Pixels(48), bounds.Right, bounds.Bottom);
        }
        int margin = Math.Min(Pixels(24), Math.Max(0, bar.Width / 10));
        int width = Math.Clamp(Pixels(widthDip), 1, Math.Max(1, bar.Width - margin * 2));
        int height = Math.Clamp(Pixels(heightDip), 1, Math.Max(1, Math.Min(bar.Height - Pixels(4), bounds.Height)));
        int left = Math.Clamp(bar.Left + margin, bounds.Left, bounds.Right - width);
        int top = Math.Clamp(bar.Top + (bar.Height - height) / 2, bounds.Top, bounds.Bottom - height);
        return new(left, top, left + width, top + height);
    }

    public static IReadOnlyList<DesktopDisplay> GetDisplays()
    {
        var displays = new List<DesktopDisplay>();
        var taskbars = new List<PixelBounds>();
        EnumWindows((hwnd, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && Win32Helper.GetWindowRect(hwnd, out var rect)) taskbars.Add(PixelBounds.From(rect));
            return true;
        }, IntPtr.Zero);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref Win32Helper.RECT rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfo(handle, ref info)) return true;
            double scale = 1;
            try { if (GetDpiForMonitor(handle, 0, out uint x, out _) == 0 && x > 0) scale = x / 96d; }
            catch (EntryPointNotFoundException) { }
            var bounds = PixelBounds.From(info.Monitor);
            var taskbar = taskbars.FirstOrDefault(bar => bar.Intersect(bounds) is var overlap && overlap.Width > 0 && overlap.Height > 0);
            displays.Add(new(info.Device, bounds, PixelBounds.From(info.Work), taskbar, scale, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return displays;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref Win32Helper.RECT rect, IntPtr data);
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Win32Helper.RECT Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr handle, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
