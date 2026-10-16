using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopLyrics.Models;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Views;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;

namespace DesktopLyrics.Services;

public static class MotionDiagnostics
{
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    public static void Capture(FrameworkElement content, string path, int width, int height)
    {
        content.Measure(new System.Windows.Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 30, 34)), null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static async Task Flush() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    public static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var view = new LyricsPresenter { PrimaryText = "窓の向こうに、今日の光が差す", SecondaryText = "窗外洒进了今天的阳光", FadeMilliseconds = 160,
            IsDualLine = true, ForceAnimationForDiagnostics = true, PrimaryBrush = Brushes.White, SecondaryBrush = Brushes.LightGray,
            Margin = new Thickness(16, 0, 16, 0) };
        // A private offscreen surface drives the real WPF animation clock without taking focus.
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(28, 30, 34)) }; grid.Children.Add(view);
        var host = new Window { Content = grid, Width = 500, Height = 56, Left = -12000, Top = -12000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
        host.Show(); await Flush();
        var checks = new List<string>(); var frames = new List<object>();
        void Frame(string name)
        {
            Capture(grid, Path.Combine(folder, name + ".png"), 500, 56);
            frames.Add(new { name, view.IncomingOpacity, view.OutgoingOpacity, view.CommitCount, view.MarqueeStartCount, view.ScrollX });
        }
        try
        {
            Frame("01-before"); int before = view.CommitCount;
            view.PrimaryText = "新しい一行が、静かに重なる"; view.SecondaryText = "下一句轻轻浮现"; view.FrameVersion++;
            await Flush(); Frame("02-start");
            Require(view.CommitCount == before + 1, "paired texts must commit exactly once"); checks.Add("original and translation commit together");
            await Task.Delay(65); Frame("03-mid-fade");
            Require(view.IncomingOpacity > .05 && view.IncomingOpacity < .95 && view.OutgoingOpacity > .05, "both layers must be visible during crossfade");
            Require(Math.Abs(view.IncomingOpacity + view.OutgoingOpacity - 1) < .025, "fade opacity must not dip to an empty frame"); checks.Add("real WPF crossfade has both layers and no opacity gap");
            await Task.Delay(180); Frame("04-after");
            Require(view.IncomingOpacity > .999 && view.OutgoingOpacity < .001 && view.DisplayedSecondary == "下一句轻轻浮现", "completed transition must display the current bilingual pair"); checks.Add("fade finishes with the current pair");
            view.PrimaryText = "一行目"; view.SecondaryText = "第一句"; view.FrameVersion++; await Flush(); await Task.Delay(30);
            view.PrimaryText = "二行目"; view.SecondaryText = "第二句"; view.FrameVersion++; await Flush(); await Task.Delay(220);
            Require(view.DisplayedPrimary == "二行目" && view.DisplayedSecondary == "第二句" && view.IncomingOpacity > .999 && view.OutgoingOpacity < .001, "interrupted transitions must not restore older lyrics"); checks.Add("rapid interruptions never restore the previous pair");
            view.EnableCrossfade = false; view.PrimaryText = "すぐに切り替える"; view.SecondaryText = "关闭渐变后立即切换"; view.FrameVersion++; await Flush();
            Require(view.IncomingOpacity > .999 && view.OutgoingOpacity < .001, "disabled fade must switch immediately"); checks.Add("disabling fade switches immediately");
            view.PrimaryText = string.Concat(Enumerable.Repeat("長い行が滑らかに流れてゆく、", 5)); view.SecondaryText = "长句滚动测试";
            view.MarqueeDelayMilliseconds = 0; view.LineSeconds = 5; view.FrameVersion++; await Flush(); await Task.Delay(140);
            int scrollStarts = view.MarqueeStartCount; double firstX = view.ScrollX;
            view.PrimaryBrush = Brushes.LightBlue; await Flush(); await Task.Delay(120);
            Require(firstX < -1 && view.ScrollX < firstX && view.MarqueeStartCount == scrollStarts, "unrelated updates must not reset a running marquee"); checks.Add("marquee keeps moving through unrelated property updates"); Frame("05-marquee");
            view.AnimateChange = false; view.EnableCrossfade = true; view.PrimaryText = "前の位置へ戻る"; view.SecondaryText = "手动回退时立即显示对应歌词"; view.FrameVersion++; await Flush();
            Require(view.IncomingOpacity > .999 && view.OutgoingOpacity < .001, "manual seeks must not fade through obsolete lines"); checks.Add("seek updates are immediate");
            // Capture a complete real-clock transition for the delivered animation preview.
            view.AnimateChange = true; view.PrimaryText = "窓の向こうに、今日の光が差す"; view.SecondaryText = "窗外洒进了今天的阳光"; view.FrameVersion++;
            await Flush(); await Task.Delay(220);
            Capture(grid, Path.Combine(folder, "animation-00.png"), 500, 56);
            view.PrimaryText = "新しい一行が、静かに重なる"; view.SecondaryText = "下一句轻轻浮现"; view.FrameVersion++;
            await Flush();
            for (int i = 1; i <= 10; i++)
            {
                Capture(grid, Path.Combine(folder, $"animation-{i:00}.png"), 500, 56);
                await Task.Delay(20);
            }
            var model = new LyricsViewModel(new MediaSessionService(), new LyricsService(), new SettingsService(Path.Combine(folder, "preview-settings")))
            { Theme = AppTheme.Dark, IsDualLine = true, PrimaryLyric = "大人になっても、夢は続いてゆく", SecondaryLyric = "长大以后，梦想仍在继续", TrackTitle = "展示演示", TrackArtist = "Apple Music" };
            var actual = new TaskbarLyricsWindow(model); await Flush();
            Capture((FrameworkElement)actual.Content, Path.Combine(folder, "taskbar-no-progress.png"), 520, 44); actual.Close();
            var settings = new TimingSettingsWindow(model); await Flush();
            var settingsContent = (FrameworkElement)settings.Content;
            settingsContent.Measure(new System.Windows.Size(470, double.PositiveInfinity));
            Capture(settingsContent, Path.Combine(folder, "display-settings.png"), 470, (int)Math.Ceiling(settingsContent.DesiredSize.Height)); settings.Close();
            File.WriteAllText(Path.Combine(folder, "motion-verification.json"), JsonSerializer.Serialize(new { passed = true, checks, frames, notes = "Real WPF clock and screenshots; demonstration lyrics are original text. Native continuous playback not included." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { host.Close(); }
    }

    public static async Task VerifyNativeTimingAsync(string path)
    {
        // Uses the real read-only media session with original demonstration LRC in isolated diagnostic data.
        var media = new MediaSessionService(); await media.InitializeAsync();
        for (int i = 0; i < 50 && media.CurrentTrack.IsEmpty; i++) await Task.Delay(100);
        Require(!media.CurrentTrack.IsEmpty, "No native media session available");
        var track = media.CurrentTrack; double position = media.GetCurrentAccuratePosition().TotalSeconds;
        Require(!track.IsPlaying && position > 1, "This deterministic native timing check needs a paused track");
        var demo = new List<LrcLine>
        {
            new() { StartTime = TimeSpan.FromSeconds(position - .2), Text = "テストの一行目", Translation = "同步测试第一句" },
            new() { StartTime = TimeSpan.FromSeconds(position + .2), Text = "テストの二行目", Translation = "同步测试第二句" }
        };
        new LocalLyricsStore().Save(track.Title, track.Artist, track.Duration, demo);
        var model = new LyricsViewModel(media, new LyricsService(), new SettingsService()) { IsDualLine = true };
        await media.RefreshMediaInfoAsync(); await Flush(); await Task.Delay(100);
        Require(model.ActiveLineIndex == 0 && model.PrimaryLyric == demo[0].Text, "Native paused position must select the correct demonstration line");
        model.SetDisplayTiming(300, true, 160, 150);
        Require(model.ActiveLineIndex == 1 && model.SecondaryLyric == demo[1].Translation, "Advance must switch original and translation together");
        model.SetDisplayTiming(-500, true, 160, 150);
        Require(model.ActiveLineIndex == -1 && model.PrimaryLyric == track.Title, "Delay before the first cue must clear a stale lyric");
        model.SetDisplayTiming(0, true, 160, 150);
        int frame = model.LyricFrameVersion; string status = model.SourceStatus;
        for (int i = 0; i < 4; i++) { await media.RefreshMediaInfoAsync(); await Task.Delay(100); }
        Require(frame == model.LyricFrameVersion && status == model.SourceStatus && model.ActiveLineIndex == 0, "Repeated native metadata must retain lyrics and scroll frame");
        Require(Math.Abs(media.GetCurrentAccuratePosition().TotalSeconds - position) < .001 && !media.CurrentTrack.IsPlaying, "Timing controls must not seek or resume the song");
        File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, track.Title, state = "Paused", position,
            syntheticDemonstrationLyrics = true, isolatedDataFolder = SettingsService.DataFolder,
            checks = new[] { "native paused position selects the correct cue", "advance switches both texts", "delay before first cue clears stale lyrics", "four metadata refreshes retain frame and source", "timing controls do not change playback" },
            limitations = "Continuous playback and real lyric timing were not exercised." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
