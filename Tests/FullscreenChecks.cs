using System.IO;
using DesktopLyrics.Services;
using DesktopLyrics.Utils;

internal static class FullscreenChecks
{
    public static void Run(Action<bool, string> check, string scratch)
    {
        var display = new Win32Helper.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        check(FullscreenPolicy.CoversDisplay(display, display, false), "borderless fullscreen covers the whole display");
        check(!FullscreenPolicy.CoversDisplay(new() { Right = 1920, Bottom = 1032 }, display, false), "ordinary work-area maximization is not fullscreen");
        check(!FullscreenPolicy.CoversDisplay(display, display, true), "captioned maximized windows are not mistaken for fullscreen with an auto-hidden taskbar");
        var secondary = new Win32Helper.RECT { Left = -2560, Top = -100, Right = 0, Bottom = 1340 };
        check(FullscreenPolicy.CoversDisplay(secondary, secondary, false), "fullscreen geometry works on a secondary display with negative coordinates");
        check(FullscreenPolicy.CoversDisplay(new() { Left = 1, Top = 1, Right = 1919, Bottom = 1079 }, display, false), "two-pixel border rounding does not prevent fullscreen detection");
        check(!FullscreenPolicy.CoversDisplay(new(), display, false) && !FullscreenPolicy.CoversDisplay(display, new(), false), "invalid native bounds cannot hide lyrics");
        check(FullscreenPolicy.ShouldHide(false, true, true) && !FullscreenPolicy.ShouldHide(true, true, true), "fullscreen topmost preference controls visibility");
        check(!FullscreenPolicy.ShouldHide(false, true, false), "fullscreen on another monitor does not hide lyrics");
        check(!FullscreenPolicy.ShouldHide(false, false, true), "leaving fullscreen requests lyric restoration");
        var folder = Path.Combine(scratch, "fullscreen-settings");
        var settings = new SettingsService(folder);
        check(!settings.Settings.FullscreenAlwaysOnTop, "fullscreen overlay defaults off for existing and new settings");
        settings.Settings.FullscreenAlwaysOnTop = true; settings.SaveSettings();
        check(new SettingsService(folder).Settings.FullscreenAlwaysOnTop, "enabled fullscreen preference persists after restart");
        settings.Settings.FullscreenAlwaysOnTop = false; settings.SaveSettings();
        check(!new SettingsService(folder).Settings.FullscreenAlwaysOnTop, "disabled fullscreen preference persists after restart");
    }
}
