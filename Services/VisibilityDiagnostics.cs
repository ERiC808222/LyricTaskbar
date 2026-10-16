using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopLyrics.Utils;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Views;

namespace DesktopLyrics.Services;

public static class VisibilityDiagnostics
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    private static async Task Flush() => await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    public static async Task VerifyAsync(string path)
    {
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        var realDisplays = DisplayPlacement.GetDisplays();
        var realPrimary = realDisplays.First(display => display.Primary);
        // Use a virtual display well outside the user's desktop. All window operations
        // are real; only monitor geometry and fullscreen detection are injected.
        int taskbarHeight = (int)Math.Round(48 * realPrimary.Scale);
        var display = new DesktopDisplay("offscreen diagnostic display", new(20000, 20000, 21920, 21080),
            new(20000, 20000, 21920, 21080 - taskbarHeight), new(20000, 21080 - taskbarHeight, 21920, 21080), realPrimary.Scale, true);
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "visibility-isolated-settings");
        var settings = new SettingsService(folder);
        settings.Settings.Left = display.Bounds.Left / display.Scale + 24;
        settings.Settings.Top = display.Bounds.Top / display.Scale + 1036;
        settings.Settings.IsLocked = true;
        settings.Settings.FullscreenAlwaysOnTop = true;
        settings.Settings.LyricAdvanceMilliseconds = 500;
        var model = new LyricsViewModel(new MediaSessionService(), new LyricsService(), settings) { TrackTitle = "Visibility test", PrimaryLyric = "显示检查", SecondaryLyric = "原设置保持" };
        bool fullscreen = false;
        var window = new TaskbarLyricsWindow(model) { DisplaysOverride = () => new[] { display }, FullscreenProbeOverride = () => fullscreen };
        var foreground = FullscreenService.ForegroundWindow;
        try
        {
            window.Show(); await Flush();
            var hwnd = new WindowInteropHelper(window).Handle;
            bool Visible() => window.IsVisible && IsWindowVisible(hwnd) && display.Bounds.Contains(window.NativeBounds);
            Check(Visible(), "startup recovers legacy offscreen position using native pixels even when position is locked");
            for (int i = 0; i < 8; i++) { window.RestoreAfterTrayAction(reset: true); await Flush(); }
            Check(Visible() && display.Taskbar.Contains(window.NativeBounds), "repeated reset actions remain visible within the target taskbar");
            var stable = window.NativeBounds;
            await App.RefreshLyricsFromTrayAsync(window, async () => { await Task.Delay(30); }); await Flush();
            Check(Visible() && window.NativeBounds == stable, "refresh preserves an already valid position");
            Win32Helper.SetWindowPos(hwnd, Win32Helper.HWND_TOPMOST, 30000, 30000, 0, 0, Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOACTIVATE);
            await App.RefreshLyricsFromTrayAsync(window, () => { Check(Visible(), "refresh restores offscreen lyrics before awaiting the lyrics source"); return Task.CompletedTask; });
            window.Hide(); await App.RefreshLyricsFromTrayAsync(window, () => Task.CompletedTask);
            Check(Visible(), "refresh restores a hidden WPF window without requiring a program restart");
            ShowWindow(hwnd, 0);
            Check(!IsWindowVisible(hwnd), "native-hidden HWND is reproduced independently of WPF visibility");
            await App.RefreshLyricsFromTrayAsync(window, () => Task.CompletedTask);
            Check(Visible(), "refresh restores native visibility when WPF still thinks the window is shown");
            try { await App.RefreshLyricsFromTrayAsync(window, () => { window.Hide(); return Task.FromException(new IOException("Simulated source failure")); }); }
            catch (IOException) { }
            Check(Visible(), "a failed lyrics refresh still runs visibility recovery");
            var saved = new SettingsService(folder).Settings;
            Check(saved.Top == window.Top && saved.Left == window.Left && saved.Width == window.Width
                && Math.Abs(saved.Top * display.Scale - stable.Top) < 1 && Math.Abs(saved.Left * display.Scale - stable.Left) < 1,
                "corrected position persists using WPF coordinates instead of native pixels");
            Check(saved.LyricAdvanceMilliseconds == 500 && saved.IsLocked && saved.FullscreenAlwaysOnTop && model.PrimaryLyric == "显示检查",
                "recovery preserves lyric text, timing, lock and fullscreen settings");
            model.ToggleFullscreenTopmost(); fullscreen = true;
            await App.RefreshLyricsFromTrayAsync(window, () => Task.CompletedTask);
            Check(!window.IsVisible, "manual recovery respects the user's fullscreen hiding preference");
            window.RestoreAfterTrayAction(reset: true);
            Check(!window.IsVisible && !window.Topmost, "resetting a fullscreen-hidden window preserves its visibility and topmost policy");
            fullscreen = false; await Task.Delay(350);
            Check(Visible(), "leaving fullscreen restores the recovered window");
            using var icon = App.CreateAppIcon();
            Check(icon.Width == 32 && icon.Height == 32, "tray icon loads from the same embedded multi-size executable icon asset");
            Check(FullscreenService.ForegroundWindow == foreground, "reset, refresh and recovery never steal foreground focus");
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, checks, realDisplays,
                actualScale = realPrimary.Scale, recoveredNativeBounds = stable, savedWpfPosition = new { saved.Left, saved.Top, saved.Width, saved.Height },
                method = "Real WPF window, native HWND hiding, shared tray handlers and injected offscreen display; no user application or music playback was changed." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }
    }
}
