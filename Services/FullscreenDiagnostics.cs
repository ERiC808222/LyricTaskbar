using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Views;

namespace DesktopLyrics.Services;

public static class FullscreenDiagnostics
{
    public static async Task VerifyAsync(string path)
    {
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "fullscreen-isolated-settings");
        var settings = new SettingsService(folder);
        settings.Settings.FullscreenAlwaysOnTop = false;
        settings.Settings.Left = Math.Max(20000, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 1000);
        settings.Settings.Top = Math.Max(20000, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 1000);
        var model = new LyricsViewModel(new MediaSessionService(), new LyricsService(), settings);
        bool simulatedFullscreen = false;
        var window = new TaskbarLyricsWindow(model) { FullscreenProbeOverride = () => simulatedFullscreen, PreserveOffscreenForDiagnostics = true };
        var foreground = FullscreenService.ForegroundWindow;
        try
        {
            window.Show(); await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            Check(window.IsVisible && window.Topmost, "lyrics are visible and topmost on the normal desktop");
            var left = window.Left; var top = window.Top;
            simulatedFullscreen = true; await Task.Delay(350);
            Check(!window.IsVisible && !window.Topmost, "disabled fullscreen overlay hides and removes topmost status");
            model.ToggleFullscreenTopmost(); await Task.Delay(80);
            Check(window.IsVisible && window.Topmost, "enabling fullscreen overlay restores the real WPF window immediately");
            model.ToggleFullscreenTopmost(); await Task.Delay(80);
            Check(!window.IsVisible, "disabling overlay during fullscreen hides the real window immediately");
            simulatedFullscreen = false; await Task.Delay(350);
            Check(window.IsVisible && window.Topmost && window.Left == left && window.Top == top, "leaving fullscreen restores lyrics without moving the window");
            Check(FullscreenService.ForegroundWindow == foreground, "hiding and restoring lyrics does not steal foreground focus");
            Check(!new SettingsService(folder).Settings.FullscreenAlwaysOnTop, "the last user preference is saved");
            simulatedFullscreen = true; await Task.Delay(350); window.Close(); await Task.Delay(350);
            Check(!window.IsVisible, "closing a hidden window stops the watcher without resurrecting it");
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, checks,
                method = "Real offscreen WPF window and timer with injected fullscreen detection; foreground focus and settings persistence checked.",
                limitations = "Did not take over the desktop or test exclusive fullscreen games." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (window.IsVisible) window.Close(); }
    }
}
