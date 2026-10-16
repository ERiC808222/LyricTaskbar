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
using FontFamily = System.Windows.Media.FontFamily;

namespace DesktopLyrics.Services;

public static class MultilingualDiagnostics
{
    private static async Task Flush() => await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static ImageSource DemoCover(Color color)
    {
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 68, 68));
            draw.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), null, new System.Windows.Point(34, 34), 22, 22);
        }
        var bitmap = new RenderTargetBitmap(68, 68, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static async Task RenderAsync(string folder)
    {
        Directory.CreateDirectory(folder); var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        var media = new MediaSessionService(); var settings = new SettingsService(Path.Combine(folder, "isolated-settings"));
        var model = new LyricsViewModel(media, new LyricsService(), settings) { Theme = AppTheme.Dark, TrackTitle = "Multilingual demo", TrackArtist = "LyricTaskbar", CoverArt = DemoCover(Color.FromRgb(230, 82, 94)) };
        var samples = new Dictionary<string, (string Original, string Chinese)>
        {
            ["korean"] = ("봄빛이 창가에 머물러요", "春光停在窗边"), ["tibetan"] = ("བོད་སྐད་ཀྱི་གླུ་དབྱངས་སྙན་མོ།", "藏语歌声悠扬"),
            ["french"] = ("La lumière entre dans la pièce", "光洒进房间"), ["spanish"] = ("La luz entra por la ventana", "光从窗外洒进来")
        };
        foreach (var (language, sample) in samples)
        {
            model.PrimaryLyric = sample.Original; model.SecondaryLyric = sample.Chinese;
            var window = new TaskbarLyricsWindow(model); await Flush();
            MotionDiagnostics.Capture((FrameworkElement)window.Content, Path.Combine(folder, language + ".png"), 620, 44); window.Close();
        }
        var typeface = new Typeface(new FontFamily("Microsoft Himalaya"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(typeface.TryGetGlyphTypeface(out var glyphs) && samples["tibetan"].Original.Where(c => !char.IsWhiteSpace(c)).All(c => glyphs.CharacterToGlyphMap.ContainsKey(c)), "Tibetan sample has actual glyph coverage in Microsoft Himalaya");
        var presenter = new LyricsPresenter { PrimaryText = samples["korean"].Original, SecondaryText = samples["korean"].Chinese, ForceAnimationForDiagnostics = true, FadeMilliseconds = 260 };
        var track = new TrackInfoPresenter { Title = "Before", Artist = "Artist A", Cover = DemoCover(Color.FromRgb(230, 82, 94)), ForceAnimationForDiagnostics = true, DurationMilliseconds = 260 };
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(28, 30, 34)) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(presenter, 1); root.Children.Add(track); root.Children.Add(presenter);
        var host = new Window { Content = root, Width = 620, Height = 44, Left = -15000, Top = -15000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show(); await Flush();
        try
        {
            foreach (var mode in Enum.GetValues<LyricTransitionMode>())
            {
                MotionDiagnostics.Capture(root, Path.Combine(folder, mode + "-animation-00.png"), 620, 44);
                presenter.TransitionMode = mode; presenter.PrimaryText = "Next line " + mode; presenter.SecondaryText = "下一句演示"; presenter.FrameVersion++; await Flush();
                await Task.Delay(55);
                Check(presenter.IncomingOpacity > 0 && presenter.OutgoingOpacity > 0, mode + " runs a paired fade");
                if (mode == LyricTransitionMode.SlideFromLeft) Check(presenter.IncomingX < -.1, "left entrance animates a render transform");
                if (mode == LyricTransitionMode.SlideFromBottom) Check(presenter.IncomingY > .1, "bottom entrance animates a render transform");
                if (mode == LyricTransitionMode.RevealFromLeft) Check(presenter.HasRevealMask, "left reveal uses an opacity mask");
                MotionDiagnostics.Capture(root, Path.Combine(folder, mode + "-mid.png"), 620, 44);
                for (int i = 1; i <= 10; i++)
                {
                    MotionDiagnostics.Capture(root, Path.Combine(folder, $"{mode}-animation-{i:00}.png"), 620, 44);
                    await Task.Delay(20);
                }
                await Task.Delay(280);
                Check(presenter.IncomingOpacity > .999 && presenter.OutgoingOpacity < .001 && !presenter.HasRevealMask && Math.Abs(presenter.IncomingX) < .001 && Math.Abs(presenter.IncomingY) < .001, mode + " leaves no animation or mask residue");
            }
            int before = track.CommitCount;
            MotionDiagnostics.Capture(root, Path.Combine(folder, "track-animation-00.png"), 620, 44);
            var newCover = DemoCover(Color.FromRgb(70, 135, 229)); track.Title = "After"; track.Artist = "Artist B"; track.Cover = newCover; track.FrameVersion++; await Flush();
            Check(track.CommitCount == before + 1 && track.DisplayedTitle == "After" && track.DisplayedCover == newCover, "cover title and artist commit as one snapshot");
            await Task.Delay(70);
            Check(track.IncomingOpacity > 0 && track.OutgoingOpacity > 0 && Math.Abs(track.IncomingOpacity + track.OutgoingOpacity - 1) < .02, "track metadata crossfade has no empty frame");
            MotionDiagnostics.Capture(root, Path.Combine(folder, "track-change-mid.png"), 620, 44); await Task.Delay(280);
            track.Title = "Album A"; track.Cover = DemoCover(Color.FromRgb(230, 82, 94)); track.FrameVersion++; await Flush(); await Task.Delay(280);
            MotionDiagnostics.Capture(root, Path.Combine(folder, "track-animation-00.png"), 620, 44);
            track.Title = "Album B"; track.Cover = DemoCover(Color.FromRgb(70, 135, 229)); track.FrameVersion++; await Flush();
            for (int i = 1; i <= 10; i++) { MotionDiagnostics.Capture(root, Path.Combine(folder, $"track-animation-{i:00}.png"), 620, 44); await Task.Delay(25); }
            track.Title = "Quick one"; track.FrameVersion++; await Flush(); await Task.Delay(25); track.Title = "Quick two"; track.FrameVersion++; await Flush(); await Task.Delay(310);
            Check(track.DisplayedTitle == "Quick two" && track.OutgoingOpacity < .001, "interrupted track transitions cannot restore stale metadata");
            before = track.CommitCount; track.Cover = DemoCover(Color.FromRgb(70, 135, 229)); await Flush();
            Check(track.CommitCount == before, "repeated cover refreshes do not restart whole-track fades");
            var cover = new RoundedAlbumArt { Width = 34, Height = 34, Source = DemoCover(Color.FromRgb(255, 0, 0)) };
            MotionDiagnostics.Capture(cover, Path.Combine(folder, "rounded-cover.png"), 34, 34);
            Check(cover.Clip is RectangleGeometry clip && clip.RadiusX == 6 && clip.RadiusY == 6, "image content is clipped to six-pixel rounded corners");
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.Korean, AppLanguage.Tibetan, AppLanguage.French, AppLanguage.Spanish })
            {
                model.Language = language;
                var dialog = new TimingSettingsWindow(model); await Flush(); var content = (FrameworkElement)dialog.Content;
                content.Measure(new System.Windows.Size(470, double.PositiveInfinity));
                MotionDiagnostics.Capture(content, Path.Combine(folder, "settings-" + language + ".png"), 470, (int)Math.Ceiling(content.DesiredSize.Height)); dialog.Close();
            }
            File.WriteAllText(Path.Combine(folder, "verification.json"), JsonSerializer.Serialize(new { passed = true, checks, samples = samples.Keys, notes = "Original demonstration text and synthetic album images; real WPF rendering and animation clocks." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { host.Close(); }
    }
}
