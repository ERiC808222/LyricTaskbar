using System.Runtime.InteropServices;
using System.Text;
using DesktopLyrics.Utils;

namespace DesktopLyrics.Services;

public static class FullscreenPolicy
{
    public static bool CoversDisplay(Win32Helper.RECT frame, Win32Helper.RECT display, bool maximizedWithCaption)
    {
        if (maximizedWithCaption || frame.Width <= 0 || frame.Height <= 0 || display.Width <= 0 || display.Height <= 0) return false;
        const int tolerance = 2;
        return frame.Left <= display.Left + tolerance && frame.Top <= display.Top + tolerance &&
            frame.Right >= display.Right - tolerance && frame.Bottom >= display.Bottom - tolerance;
    }
    public static bool ShouldHide(bool keepOnTopInFullscreen, bool foregroundFullscreen, bool sameDisplay) =>
        !keepOnTopInFullscreen && foregroundFullscreen && sameDisplay;
}

/// <summary>Read-only foreground and monitor detection in native screen coordinates.</summary>
public static class FullscreenService
{
    public static IntPtr ForegroundWindow => GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetStyle32(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Win32Helper.RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public Win32Helper.RECT Monitor, Work;
        public uint Flags;
    }
    public static bool ForegroundCoversLyricsDisplay(IntPtr lyricsWindow)
    {
        IntPtr foreground = GetForegroundWindow();
        if (lyricsWindow == IntPtr.Zero || foreground == IntPtr.Zero || foreground == lyricsWindow || foreground == GetShellWindow() ||
            !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        var className = new StringBuilder(128); GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var foregroundDisplay = MonitorFromWindow(foreground, 2); // nearest display
        if (foregroundDisplay == IntPtr.Zero || foregroundDisplay != MonitorFromWindow(lyricsWindow, 2)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(foregroundDisplay, ref info)) return false;
        Win32Helper.RECT frame;
        if (DwmGetWindowAttribute(foreground, 9, out frame, Marshal.SizeOf<Win32Helper.RECT>()) != 0 && !Win32Helper.GetWindowRect(foreground, out frame)) return false;
        long style = IntPtr.Size == 8 ? GetStyle64(foreground, -16).ToInt64() : GetStyle32(foreground, -16);
        const long caption = 0x00C00000;
        bool normalMaximized = IsZoomed(foreground) && (style & caption) == caption;
        return FullscreenPolicy.CoversDisplay(frame, info.Monitor, normalMaximized);
    }
}
