using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using DesktopLyrics.Models;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Views;

namespace DesktopLyrics.Services;
public static class Diagnostics
{
    public static async Task RunAsync(string[] args)
    {
        if (args[0] == "--verify-visibility" && args.Length == 2)
        {
            await VisibilityDiagnostics.VerifyAsync(args[1]);
        }
        else if (args[0] == "--render-multilingual" && args.Length == 2)
        {
            await MultilingualDiagnostics.RenderAsync(args[1]);
        }
        else if (args[0] == "--probe-language" && args.Length == 6)
        {
            var language = Enum.Parse<LyricsLanguageMode>(args[1]);
            var service = new LyricsService { SearchLanguage = language };
            var lines = await service.FetchLyricsAsync(args[2], args[3], TimeSpan.FromSeconds(double.Parse(args[4], CultureInfo.InvariantCulture)));
            File.WriteAllText(args[5], JsonSerializer.Serialize(new { language = language.ToString(), title = args[2], artist = args[3],
                source = lines?.FirstOrDefault()?.SourceName, sourceUrl = lines?.FirstOrDefault()?.SourceUrl,
                lines = lines?.Count ?? 0, originalLines = lines?.Count(line => line.Text.Length > 0) ?? 0,
                translatedLines = lines?.Count(line => line.Translation.Length > 0) ?? 0,
                koreanLines = lines?.Count(line => LanguageSupport.HasKorean(line.Text)) ?? 0,
                tibetanLines = lines?.Count(line => LanguageSupport.HasTibetan(line.Text)) ?? 0, diagnostic = service.LastDiagnostic }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (args[0] == "--verify-fullscreen" && args.Length == 2)
        {
            await FullscreenDiagnostics.VerifyAsync(args[1]);
        }
        else if (args[0] == "--verify-autostart" && args.Length == 2)
        {
            AutoStartDiagnostics.Verify(args[1]);
        }
        else if (args[0] == "--render-motion-proof" && args.Length == 2)
        {
            await MotionDiagnostics.RunAsync(args[1]);
        }
        else if (args[0] == "--verify-timing-current" && args.Length == 2)
        {
            await MotionDiagnostics.VerifyNativeTimingAsync(args[1]);
        }
        else if (args[0] == "--diagnose-media" && args.Length == 2)
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var tracks = new List<object>();
            foreach (var session in manager.GetSessions())
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                tracks.Add(new { app = session.SourceAppUserModelId, title = props.Title, artist = props.Artist,
                    state = session.GetPlaybackInfo().PlaybackStatus.ToString(), seconds = timeline.Position.TotalSeconds,
                    duration = timeline.EndTime.TotalSeconds });
            }
            File.WriteAllText(args[1], JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if ((args[0] == "--probe" && args.Length == 5) || (args[0] == "--probe-source" && args.Length == 6))
        {
            var offset = args[0] == "--probe-source" ? 1 : 0;
            var mode = offset == 1 ? Enum.Parse<LyricsSourceMode>(args[1], true) : LyricsSourceMode.Auto;
            var lines = await new LyricsService(mode: mode).FetchLyricsAsync(args[1+offset], args[2+offset], TimeSpan.FromSeconds(double.Parse(args[3+offset], CultureInfo.InvariantCulture)));
            File.WriteAllText(args[4+offset], JsonSerializer.Serialize(new { title = args[1+offset], artist = args[2+offset], mode = mode.ToString(),
                source = lines?.FirstOrDefault()?.SourceName, sourceUrl = lines?.FirstOrDefault()?.SourceUrl,
                lines = lines?.Count ?? 0, translatedLines = lines?.Count(line => line.Translation.Length > 0) ?? 0,
                japaneseLines = lines?.Count(line => LyricMatcher.ContainsKana(line.Text)) ?? 0 }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (args[0] == "--parse-ttml" && args.Length == 3)
        {
            var lines = TtmlParser.Parse(File.ReadAllText(args[1]));
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { file = Path.GetFileName(args[1]), lines = lines.Count,
                originalLines = lines.Count(line => line.Text.Length > 0), translatedLines = lines.Count(line => line.Translation.Length > 0) }));
        }
        else if (args[0] == "--verify-current" && args.Length == 3)
        {
            var mode = Enum.Parse<LyricsSourceMode>(args[1], true);
            var settings = new SettingsService(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "verification-settings"));
            settings.Settings.LyricsSource = mode;
            var media = new MediaSessionService();
            var model = new LyricsViewModel(media, new LyricsService(), settings);
            await media.InitializeAsync();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (media.CurrentTrack.IsEmpty && watch.Elapsed.TotalSeconds < 5) await Task.Delay(100);
            while (model.SourceStatus == I18n.FetchingLyrics && watch.Elapsed.TotalSeconds < 55) await Task.Delay(100);
            int initialFrame = model.LyricFrameVersion;
            int initialLine = model.ActiveLineIndex;
            var initialTitle = media.CurrentTrack.Title;
            bool originallyPlaying = media.CurrentTrack.IsPlaying;
            var samples = new List<object>();
            int reversals = 0; double lastPosition = media.GetCurrentAccuratePosition().TotalSeconds;
            for (int i = 0; i < 40; i++)
            {
                if (i % 10 == 0) await media.RefreshMediaInfoAsync();
                await Task.Delay(100);
                var position = media.GetCurrentAccuratePosition().TotalSeconds;
                if (position < lastPosition - .001) reversals++;
                lastPosition = position;
                samples.Add(new { position, model.ActiveLineIndex, model.LyricFrameVersion, media.SeekVersion });
            }
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { mode = mode.ToString(), title = media.CurrentTrack.Title,
                artist = media.CurrentTrack.Artist, state = media.CurrentTrack.IsPlaying ? "Playing" : "Paused",
                position = media.GetCurrentAccuratePosition().TotalSeconds, duration = media.CurrentTrack.Duration.TotalSeconds,
                sourceStatus = model.SourceStatus, diagnostic = model.SourceDiagnostic,
                model.CurrentLineCount, model.ActiveLineIndex,
                metadataRefreshes = 4, reversals, samples,
                pausedMetadataStable = !originallyPlaying && media.CurrentTrack.Title == initialTitle && initialFrame == model.LyricFrameVersion && initialLine == model.ActiveLineIndex,
                hasDisplayedLyric = model.CurrentLineCount > 0 && model.ActiveLineIndex >= 0 && model.PrimaryLyric != media.CurrentTrack.Title,
                hasChineseTranslation = model.CurrentLineCount > 0 && model.SecondaryLyric != I18n.NoTranslation && model.SecondaryLyric.Length > 0 }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (args[0] == "--render-preview" && args.Length == 2)
        {
            // Render the real WPF taskbar content with original demonstration text, without opening a window.
            var model = new LyricsViewModel(new MediaSessionService(), new LyricsService(), new SettingsService());
            model.Theme = AppTheme.Dark;
            model.IsDualLine = true;
            model.PrimaryLyric = "窓の向こうに、今日の光が差す";
            model.SecondaryLyric = "窗外洒进了今天的阳光";
            model.IsPlaying = false;
            model.TrackTitle = "日中歌词预览";
            model.TrackArtist = "Apple Music";
            var window = new TaskbarLyricsWindow(model);
            var content = (FrameworkElement)window.Content;
            content.Width = 520; content.Height = 44;
            content.Measure(new System.Windows.Size(520, 44));
            content.Arrange(new Rect(0, 0, 520, 44));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1040, 88, 192, 192, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 30, 34)), null, new Rect(0, 0, 520, 44));
            bitmap.Render(background); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(args[1]); encoder.Save(stream);
            window.Close();
        }
        else throw new ArgumentException("Unknown diagnostic command.");
    }
}
