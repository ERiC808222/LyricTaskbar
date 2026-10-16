using System;
using System.Windows.Media;

namespace DesktopLyrics.Models
{
    public enum AppTheme
    {
        Auto = 0,
        Dark = 1,
        Light = 2
    }

    public enum AppLanguage
    {
        English = 0,
        Chinese = 1,
        Japanese = 2,
        Korean = 3, Tibetan = 4, French = 5, Spanish = 6
    }

    public enum LyricsSourceMode { Auto = 0, AppleMusic = 1, Amll = 2, Original = 3 }
    public enum LyricsLanguageMode { Auto = 0, Japanese = 1, Korean = 2, Tibetan = 3, French = 4, Spanish = 5, English = 6, Chinese = 7 }
    public enum LyricTransitionMode { Crossfade = 0, SlideFromLeft = 1, SlideFromBottom = 2, RevealFromLeft = 3 }

    public class LrcLine
    {
        public TimeSpan StartTime { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;

        public override string ToString() => $"[{StartTime:mm\\:ss\\.ff}] {Text}";
    }

    public class MediaTrackInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumTitle { get; set; } = string.Empty;
        public bool IsPlaying { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
        public string SourceAppId { get; set; } = string.Empty;
        public ImageSource? Thumbnail { get; set; }
        public DateTime LastUpdatedTime { get; set; } = DateTime.UtcNow;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Title);

        public override string ToString() => $"{Title} - {Artist} ({(IsPlaying ? "Playing" : "Paused")})";
    }

    public class AppSettings
    {
        public double Left { get; set; } = -1;
        public double Top { get; set; } = -1;
        public double Width { get; set; } = 520;
        public double Height { get; set; } = 44;
        public bool IsLocked { get; set; } = true;
        public bool FullscreenAlwaysOnTop { get; set; } = false;
        public bool IsDualLine { get; set; } = true;
        public bool ShowTranslation { get; set; } = true;
        public LyricsSourceMode LyricsSource { get; set; } = LyricsSourceMode.Auto;
        public AppTheme Theme { get; set; } = AppTheme.Auto;
        public AppLanguage Language { get; set; } = AppLanguage.Chinese;
        public double FontSize { get; set; } = 16.5;
        public string TextColor { get; set; } = "#FFFFFF";
        public string AccentColor { get; set; } = "#FF4081";
        public int LyricAdvanceMilliseconds { get; set; } = 0;
        public bool EnableCrossfade { get; set; } = true;
        public LyricTransitionMode LyricTransition { get; set; } = LyricTransitionMode.Crossfade;
        public bool EnableTrackTransition { get; set; } = true;
        public LyricsLanguageMode LyricsLanguage { get; set; } = LyricsLanguageMode.Auto;
        public int CrossfadeDurationMilliseconds { get; set; } = 160;
        public int MarqueeStartDelayMilliseconds { get; set; } = 150;
        public bool AutoSearchLyrics { get; set; } = true;
    }
}
