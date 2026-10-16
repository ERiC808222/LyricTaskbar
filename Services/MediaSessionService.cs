using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services
{
    public class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        
        private readonly PlaybackClock _clock = new();
        private int _refreshGeneration;
        public long SeekVersion => _clock.SeekVersion;

        public event Action<MediaTrackInfo>? TrackChanged;
        public event Action<bool>? PlaybackStateChanged;

        public MediaTrackInfo CurrentTrack { get; private set; } = new();

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    _sessionManager.SessionsChanged += OnSessionsChanged;
                    UpdateCurrentSession(SelectSession(_sessionManager));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaSessionService] Failed to initialize SMTC: {ex.Message}");
            }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateCurrentSession(SelectSession(sender));
        }

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            UpdateCurrentSession(SelectSession(sender));
        }
        private static GlobalSystemMediaTransportControlsSession? SelectSession(GlobalSystemMediaTransportControlsSessionManager manager)
        {
            // Prefer the native Apple Music session so browser videos cannot replace its lyrics.
            var apple = manager.GetSessions().FirstOrDefault(session =>
                session.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));
            return apple ?? manager.GetCurrentSession();
        }

        private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => UpdateCurrentSession(session)); return; }
            if (ReferenceEquals(_currentSession, session)) return;
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _currentSession = session;
            _clock.Reset();
            _refreshGeneration++;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

                _ = RefreshMediaInfoAsync();
            }
            else
            {
                CurrentTrack = new MediaTrackInfo();
                TrackChanged?.Invoke(CurrentTrack);
                PlaybackStateChanged?.Invoke(false);
            }
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await RefreshMediaInfoAsync();
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            RefreshPlaybackState();
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            RefreshTimeline();
        }

        public async Task RefreshMediaInfoAsync()
        {
            var session = _currentSession;
            if (session == null) return;
            int generation = System.Threading.Interlocked.Increment(ref _refreshGeneration);
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (!ReferenceEquals(session, _currentSession)) return;
                BitmapImage? coverArt = null;
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        using var netStream = stream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        ms.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        coverArt = bitmap;
                    }
                    catch (Exception thumbEx)
                    {
                        Debug.WriteLine($"[MediaSessionService] Thumbnail load error: {thumbEx.Message}");
                    }
                }

                if (!ReferenceEquals(session, _currentSession) || generation != _refreshGeneration) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(session, _currentSession) || generation != _refreshGeneration) return;
                    var timeline = session.GetTimelineProperties();
                    var playback = session.GetPlaybackInfo();
                    bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var track = new MediaTrackInfo
                    {
                        Title = props?.Title ?? string.Empty, Artist = props?.Artist ?? string.Empty,
                        AlbumTitle = props?.AlbumTitle ?? string.Empty, IsPlaying = isPlaying,
                        Position = timeline.Position, Duration = timeline.EndTime,
                        SourceAppId = session.SourceAppUserModelId, Thumbnail = coverArt,
                        LastUpdatedTime = timeline.LastUpdatedTime.UtcDateTime
                    };
                    if (track.Title != CurrentTrack.Title || track.Artist != CurrentTrack.Artist || track.SourceAppId != CurrentTrack.SourceAppId) _clock.Reset();
                    CurrentTrack = track;
                    _clock.Update(timeline.Position, timeline.LastUpdatedTime, isPlaying, playback.PlaybackRate ?? 1);
                    TrackChanged?.Invoke(track);
                    PlaybackStateChanged?.Invoke(isPlaying);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaSessionService] Error refreshing media info: {ex.Message}");
            }
        }

        public async Task TogglePlayPauseAsync()
        {
            if (_currentSession != null)
            {
                try { await _currentSession.TryTogglePlayPauseAsync(); } catch { }
            }
        }

        public async Task PreviousTrackAsync()
        {
            if (_currentSession != null)
            {
                try { await _currentSession.TrySkipPreviousAsync(); } catch { }
            }
        }

        public async Task NextTrackAsync()
        {
            if (_currentSession != null)
            {
                try { await _currentSession.TrySkipNextAsync(); } catch { }
            }
        }

        private void RefreshPlaybackState() => RefreshTimeline();

        private void RefreshTimeline()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess()) { dispatcher.Invoke(RefreshTimeline); return; }
            if (_currentSession == null) return;
            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                var playback = _currentSession.GetPlaybackInfo();
                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                _clock.Update(timeline.Position, timeline.LastUpdatedTime, playing, playback.PlaybackRate ?? 1);
                CurrentTrack.Duration = timeline.EndTime;
                CurrentTrack.Position = timeline.Position;
                CurrentTrack.IsPlaying = playing;
                PlaybackStateChanged?.Invoke(playing);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        public TimeSpan GetCurrentAccuratePosition() => _clock.Position(CurrentTrack.Duration);
    }
}
