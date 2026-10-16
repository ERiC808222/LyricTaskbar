using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using DesktopLyrics.Models;
using DesktopLyrics.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace DesktopLyrics.ViewModels
{
    public class LyricsViewModel : INotifyPropertyChanged
    {
        private readonly MediaSessionService _mediaService;
        private readonly LyricsService _lyricsService;
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _syncTimer;

        private List<LrcLine> _currentLyrics = new();
        private CancellationTokenSource? _lyricsFetchCts;
        private int _currentLineIndex = -2;
        private string _trackIdentity = "";
        private bool _forceReload;
        private long _lastSeekVersion = -1;
        private int _lyricFrameVersion;
        private double _lineRemainingSeconds = 10;
        private bool _animateLyricChange;
        public int LyricFrameVersion { get => _lyricFrameVersion; private set => SetField(ref _lyricFrameVersion, value); }
        public double LineRemainingSeconds { get => _lineRemainingSeconds; private set => SetField(ref _lineRemainingSeconds, value); }
        public bool AnimateLyricChange { get => _animateLyricChange; private set => SetField(ref _animateLyricChange, value); }
        public bool EnableCrossfade => Settings.EnableCrossfade;
        public int CrossfadeDurationMilliseconds => Settings.CrossfadeDurationMilliseconds;
        public int MarqueeStartDelayMilliseconds => Settings.MarqueeStartDelayMilliseconds;
        public int LyricAdvanceMilliseconds => Settings.LyricAdvanceMilliseconds;
        public void SetDisplayTiming(int advance, bool crossfade, int duration, int marqueeDelay)
        {
            Settings.LyricAdvanceMilliseconds = Math.Clamp(advance, -10000, 10000);
            Settings.EnableCrossfade = crossfade;
            Settings.CrossfadeDurationMilliseconds = Math.Clamp(duration, 60, 300);
            Settings.MarqueeStartDelayMilliseconds = Math.Clamp(marqueeDelay, 0, 2000);
            foreach (var name in new[] { nameof(LyricAdvanceMilliseconds), nameof(EnableCrossfade), nameof(CrossfadeDurationMilliseconds), nameof(MarqueeStartDelayMilliseconds) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            OnSyncTick(null, EventArgs.Empty);
        }
        private int _trackFrameVersion;
        public int TrackFrameVersion { get => _trackFrameVersion; private set => SetField(ref _trackFrameVersion, value); }
        public LyricTransitionMode LyricTransition => Settings.LyricTransition;
        public bool EnableTrackTransition => Settings.EnableTrackTransition;
        public void SetTransitions(LyricTransitionMode mode, bool trackTransition)
        {
            Settings.LyricTransition = Enum.IsDefined(mode) ? mode : LyricTransitionMode.Crossfade;
            Settings.EnableTrackTransition = trackTransition;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyricTransition)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnableTrackTransition)));
        }
        public async Task SetLyricsLanguageAsync(LyricsLanguageMode language)
        {
            Settings.LyricsLanguage = language; _lyricsService.SearchLanguage = language;
            _settingsService.SaveSettings(); await ReloadLyricsAsync();
        }
        public void SaveDisplayTiming() => _settingsService.SaveSettings();
        public bool FullscreenAlwaysOnTop => Settings.FullscreenAlwaysOnTop;
        public void ToggleFullscreenTopmost()
        {
            Settings.FullscreenAlwaysOnTop = !Settings.FullscreenAlwaysOnTop;
            _settingsService.SaveSettings();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullscreenAlwaysOnTop)));
        }

        private string _primaryLyric = "Waiting for media playback...";
        private string _secondaryLyric = "Supports YouTube Music, Spotify & Browser Media";
        private string _trackTitle = "Not playing";
        private string _trackArtist = "YouTube Music";
        private ImageSource? _coverArt;
        private bool _isPlaying = false;
        private bool _isLocked = true;
        private string _sourceStatus = "Idle";
        private bool _isDualLine = true;
        private bool _showTranslation = true;
        private AppTheme _theme = AppTheme.Auto;
        private AppLanguage _language = AppLanguage.English;

        // Dynamic theme brushes
        private Brush _primaryTextBrush = new SolidColorBrush(Colors.White);
        private Brush _secondaryTextBrush = new SolidColorBrush(Color.FromRgb(170, 176, 190));
        private Brush _dividerBrush = new SolidColorBrush(Color.FromArgb(37, 255, 255, 255));
        private Brush _controlButtonBgBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        private Brush _controlIconBrush = new SolidColorBrush(Colors.White);

        public event PropertyChangedEventHandler? PropertyChanged;

        public string PrimaryLyric
        {
            get => _primaryLyric;
            set => SetField(ref _primaryLyric, value);
        }

        public string SecondaryLyric
        {
            get => _secondaryLyric;
            set => SetField(ref _secondaryLyric, value);
        }

        public string TrackTitle
        {
            get => _trackTitle;
            set => SetField(ref _trackTitle, value);
        }

        public string TrackArtist
        {
            get => _trackArtist;
            set => SetField(ref _trackArtist, value);
        }

        public ImageSource? CoverArt
        {
            get => _coverArt;
            set => SetField(ref _coverArt, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetField(ref _isPlaying, value);
        }

        public bool IsLocked
        {
            get => _isLocked;
            set => SetField(ref _isLocked, value);
        }

        public string SourceStatus
        {
            get => _sourceStatus;
            set => SetField(ref _sourceStatus, value);
        }

        public bool IsDualLine
        {
            get => _isDualLine;
            set => SetField(ref _isDualLine, value);
        }

        public bool ShowTranslation
        {
            get => _showTranslation;
            private set => SetField(ref _showTranslation, value);
        }
        public void ToggleTranslation()
        {
            ShowTranslation = !ShowTranslation;
            _settingsService.Settings.ShowTranslation = ShowTranslation;
            if (ShowTranslation) { IsDualLine = true; _settingsService.Settings.IsDualLine = true; }
            _currentLineIndex = -2;
            _settingsService.SaveSettings();
            OnSyncTick(null, EventArgs.Empty);
        }
        public void ImportLyrics(string original, string? translated)
        {
            var track = _mediaService.CurrentTrack;
            if (track.IsEmpty) throw new InvalidOperationException(I18n.PlayFirst);
            var lines = LrcParser.ParseBilingual(original, translated);
            if (!lines.Any(line => !string.IsNullOrWhiteSpace(line.Text))) throw new FormatException(I18n.InvalidLrc);
            new LocalLyricsStore().Save(track.Title, track.Artist, track.Duration, lines);
            _lyricsFetchCts?.Cancel();
            _currentLyrics = lines;
            _currentLineIndex = -2;
            SourceStatus = I18n.SyncedLines(lines.Count);
            OnSyncTick(null, EventArgs.Empty);
        }
        public async Task SetLyricsSourceAsync(LyricsSourceMode mode)
        {
            _settingsService.Settings.LyricsSource = mode;
            _lyricsService.SourceMode = mode;
            _settingsService.SaveSettings();
            await ReloadLyricsAsync();
        }

        public async Task ReloadLyricsAsync(bool removeLocal = false)
        {
            var track = _mediaService.CurrentTrack;
            if (removeLocal && !track.IsEmpty) new LocalLyricsStore().Remove(track.Title, track.Artist, track.Duration);
            _forceReload = true;
            _lyricsService.ClearCache();
            await _mediaService.RefreshMediaInfoAsync();
        }

        public AppTheme Theme
        {
            get => _theme;
            set
            {
                if (SetField(ref _theme, value))
                {
                    ApplyTheme();
                }
            }
        }

        public AppLanguage Language
        {
            get => _language;
            set
            {
                if (SetField(ref _language, value))
                {
                    I18n.CurrentLanguage = value;
                    UpdateLanguagePlaceholders();
                }
            }
        }

        public Brush PrimaryTextBrush
        {
            get => _primaryTextBrush;
            set => SetField(ref _primaryTextBrush, value);
        }

        public Brush SecondaryTextBrush
        {
            get => _secondaryTextBrush;
            set => SetField(ref _secondaryTextBrush, value);
        }

        public Brush DividerBrush
        {
            get => _dividerBrush;
            set => SetField(ref _dividerBrush, value);
        }

        public Brush ControlButtonBgBrush
        {
            get => _controlButtonBgBrush;
            set => SetField(ref _controlButtonBgBrush, value);
        }

        public Brush ControlIconBrush
        {
            get => _controlIconBrush;
            set => SetField(ref _controlIconBrush, value);
        }

        public AppSettings Settings => _settingsService.Settings;
        public int CurrentLineCount => _currentLyrics.Count;
        public int ActiveLineIndex => _currentLineIndex;
        public string SourceDiagnostic => _lyricsService.LastDiagnostic;

        public LyricsViewModel(MediaSessionService mediaService, LyricsService lyricsService, SettingsService settingsService)
        {
            _mediaService = mediaService;
            _lyricsService = lyricsService;
            _lyricsService.SourceMode = settingsService.Settings.LyricsSource;
            _lyricsService.SearchLanguage = settingsService.Settings.LyricsLanguage;
            _settingsService = settingsService;

            _isLocked = _settingsService.Settings.IsLocked;
            _isDualLine = _settingsService.Settings.IsDualLine;
            _showTranslation = _settingsService.Settings.ShowTranslation;
            _theme = _settingsService.Settings.Theme;
            _language = _settingsService.Settings.Language;
            I18n.CurrentLanguage = _language;

            _primaryLyric = I18n.WaitingForPlayback;
            _secondaryLyric = I18n.SupportedMediaSources;
            _sourceStatus = I18n.Idle;
            _trackTitle = I18n.NotPlaying;

            ApplyTheme();

            _mediaService.TrackChanged += OnTrackChanged;
            _mediaService.PlaybackStateChanged += OnPlaybackStateChanged;

            _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(25) // ~40 FPS smooth sync
            };
            _syncTimer.Tick += OnSyncTick;
            _syncTimer.Start();
        }

        public void SetTheme(AppTheme theme)
        {
            Theme = theme;
            _settingsService.Settings.Theme = theme;
            _settingsService.SaveSettings();
        }

        public void SetLanguage(AppLanguage lang)
        {
            Language = lang;
            _settingsService.Settings.Language = lang;
            _settingsService.SaveSettings();
            _currentLineIndex = -2;
            OnSyncTick(null, EventArgs.Empty);
        }

        public void UpdateLanguagePlaceholders()
        {
            if (_mediaService.CurrentTrack.IsEmpty)
            {
                PrimaryLyric = I18n.WaitingForPlayback;
                SecondaryLyric = I18n.SupportedMediaSources;
                SourceStatus = I18n.Idle;
                TrackTitle = I18n.NotPlaying;
            }
            else if (_currentLyrics.Count == 0)
            {
                PrimaryLyric = _mediaService.CurrentTrack.Title;
                SecondaryLyric = I18n.NoLyricsFound;
                SourceStatus = I18n.NoLyricsFound;
            }
        }

        public void ApplyTheme()
        {
            bool isLight = false;

            if (_theme == AppTheme.Light)
            {
                isLight = true;
            }
            else if (_theme == AppTheme.Dark)
            {
                isLight = false;
            }
            else // Auto
            {
                isLight = DetectWindowsLightTheme();
            }

            if (isLight)
            {
                PrimaryTextBrush = new SolidColorBrush(Color.FromRgb(24, 25, 28));
                SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(85, 94, 109));
                DividerBrush = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0));
                ControlButtonBgBrush = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
                ControlIconBrush = new SolidColorBrush(Color.FromRgb(24, 25, 28));
            }
            else
            {
                PrimaryTextBrush = new SolidColorBrush(Colors.White);
                SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(170, 176, 190));
                DividerBrush = new SolidColorBrush(Color.FromArgb(37, 255, 255, 255));
                ControlButtonBgBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
                ControlIconBrush = new SolidColorBrush(Colors.White);
            }
        }

        private static bool DetectWindowsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("SystemUsesLightTheme") is int val)
                {
                    return val == 1;
                }
            }
            catch { }
            return false;
        }

        public async Task TogglePlayPauseAsync()
        {
            await _mediaService.TogglePlayPauseAsync();
        }

        public async Task PreviousTrackAsync()
        {
            await _mediaService.PreviousTrackAsync();
        }

        public async Task NextTrackAsync()
        {
            await _mediaService.NextTrackAsync();
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            IsPlaying = isPlaying;
        }

        private async void OnTrackChanged(MediaTrackInfo track)
        {
            string identity = $"{track.SourceAppId}\0{track.Title}\0{track.Artist}";
            bool sameTrack = identity == _trackIdentity;
            TrackTitle = string.IsNullOrWhiteSpace(track.Title) ? I18n.NotPlaying : track.Title;
            TrackArtist = track.Artist;
            CoverArt = track.Thumbnail;
            IsPlaying = track.IsPlaying;
            if (sameTrack && !_forceReload) return;
            _trackIdentity = identity;
            _forceReload = false;
            _lyricsFetchCts?.Cancel();
            _lyricsFetchCts?.Dispose();
            _lyricsFetchCts = new CancellationTokenSource();
            var ct = _lyricsFetchCts.Token;

            if (!sameTrack)
            {
                TrackFrameVersion++;
                _currentLineIndex = -2;
                _currentLyrics = new();
                AnimateLyricChange = false;
                _lastSeekVersion = _mediaService.SeekVersion;
            }

            if (track.IsEmpty)
            {
                PrimaryLyric = I18n.WaitingForPlayback;
                SecondaryLyric = I18n.SupportedMediaSources;
                SourceStatus = I18n.Idle;
                _currentLyrics.Clear();
                return;
            }

            if (!sameTrack || _currentLyrics.Count == 0)
            {
                PrimaryLyric = track.Title;
                SecondaryLyric = string.IsNullOrWhiteSpace(track.Artist) ? I18n.FetchingLyrics : track.Artist;
            }
            SourceStatus = I18n.FetchingLyrics;

            try
            {
                var lyrics = await _lyricsService.FetchLyricsAsync(track.Title, track.Artist, track.Duration, ct);
                if (ct.IsCancellationRequested) return;

                if (lyrics != null && lyrics.Count > 0)
                {
                    _currentLyrics = lyrics;
                    _currentLineIndex = -2;
                    SourceStatus = $"{lyrics[0].SourceName} · {I18n.SyncedLines(lyrics.Count)}";
                    OnSyncTick(null, EventArgs.Empty);
                }
                else
                {
                    _currentLyrics.Clear();
                    PrimaryLyric = track.Title;
                    SecondaryLyric = string.IsNullOrWhiteSpace(track.Artist) ? I18n.NoLyricsFound : $"{track.Artist}";
                    SourceStatus = I18n.NoLyricsFound;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                SourceStatus = I18n.LyricsFetchFailed;
                SecondaryLyric = ex.Message;
            }
        }

        private void OnSyncTick(object? sender, EventArgs e)
        {
            try
            {
                var lyrics = _currentLyrics;
                if (!_isPlaying && (lyrics == null || lyrics.Count == 0))
                    return;

                var currentTime = LyricTiming.DisplayPosition(_mediaService.GetCurrentAccuratePosition(), LyricAdvanceMilliseconds);
                if (lyrics.Count == 0) return;
                bool seek = _lastSeekVersion != _mediaService.SeekVersion;
                _lastSeekVersion = _mediaService.SeekVersion;
                int activeIdx = LrcParser.FindActiveIndex(lyrics, currentTime);

                if (activeIdx != _currentLineIndex || seek)
                {
                    AnimateLyricChange = !seek && _currentLineIndex >= -1;
                    _currentLineIndex = activeIdx;
                    var next = activeIdx + 1 < lyrics.Count ? lyrics[activeIdx + 1].StartTime : _mediaService.CurrentTrack.Duration;
                    LineRemainingSeconds = Math.Max(.4, (next - currentTime).TotalSeconds);
                    LyricFrameVersion++;
                    if (activeIdx >= 0 && activeIdx < lyrics.Count)
                    {
                        PrimaryLyric = lyrics[activeIdx].Text;

                        SecondaryLyric = _isDualLine
                            ? LyricsPresentation.Secondary(lyrics, activeIdx, ShowTranslation)
                            : _trackArtist;
                    }
                    else { PrimaryLyric = TrackTitle; SecondaryLyric = TrackArtist; }
                }
            }
            catch
            {
                // Never let timer tick crash the application
            }
        }

        public void ToggleLock()
        {
            IsLocked = !IsLocked;
            _settingsService.Settings.IsLocked = IsLocked;
            _settingsService.SaveSettings();
        }

        public void ToggleDualLine()
        {
            IsDualLine = !IsDualLine;
            _currentLineIndex = -2;
            _settingsService.Settings.IsDualLine = IsDualLine;
            _settingsService.SaveSettings();
            OnSyncTick(null, EventArgs.Empty);
        }

        public void SavePosition(double left, double top, double width, double height)
        {
            _settingsService.Settings.Left = left;
            _settingsService.Settings.Top = top;
            _settingsService.Settings.Width = width;
            _settingsService.Settings.Height = height;
            _settingsService.SaveSettings();
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
