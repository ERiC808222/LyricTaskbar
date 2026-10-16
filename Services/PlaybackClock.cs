using System.Diagnostics;

namespace DesktopLyrics.Services;

/// <summary>Monotonic playback interpolation. Small corrections slow the clock instead of rewinding it.</summary>
public sealed class PlaybackClock
{
    private readonly Func<double> _now;
    private readonly Func<DateTimeOffset> _utcNow;
    private double _anchor, _anchorTime, _rate = 1, _correctionEnd;
    private bool _playing, _initialized;
    private TimeSpan _report;
    private DateTimeOffset _stamp;
    public long SeekVersion { get; private set; }
    public PlaybackClock(Func<double>? now = null, Func<DateTimeOffset>? utcNow = null)
    {
        _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }
    public void Reset() { _initialized = false; _playing = false; _anchor = 0; SeekVersion++; }
    private double Estimate(double now)
    {
        if (!_playing) return _anchor;
        double elapsed = Math.Max(0, now - _anchorTime);
        double corrected = Math.Clamp(_correctionEnd - _anchorTime, 0, elapsed);
        return _anchor + (elapsed - corrected * .2) * _rate;
    }
    public TimeSpan Position(TimeSpan duration)
    {
        double seconds = Math.Max(0, Estimate(_now()));
        if (duration > TimeSpan.Zero) seconds = Math.Min(seconds, duration.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
    public void Update(TimeSpan reported, DateTimeOffset stamp, bool playing, double rate = 1)
    {
        double now = _now(), current = Estimate(now);
        rate = double.IsFinite(rate) && rate > 0 ? rate : 1;
        bool stateChanged = playing != _playing;
        bool duplicate = _initialized && reported == _report && stamp == _stamp;
        // Out-of-order notifications must not replace a newer timeline.
        if (_initialized && stamp < _stamp && !stateChanged) return;
        if (duplicate && !stateChanged && rate == _rate) return;
        // Some players refresh LastUpdatedTime while repeating the same coarse position.
        // Re-anchoring that position would rewind the lyric at every notification.
        if (_initialized && playing && _playing && reported == _report && rate == _rate)
        {
            if (stamp > _stamp) _stamp = stamp;
            return;
        }
        double target = Math.Max(0, reported.TotalSeconds);
        // A resume notification often repeats the paused timeline: never count the paused age.
        if (playing && (!_initialized || !stateChanged) && !duplicate)
            target += Math.Clamp((_utcNow() - stamp).TotalSeconds, 0, 2) * rate;
        if (duplicate) target = current;
        _correctionEnd = now;
        if (_initialized && playing && _playing && target < current && current - target <= .75)
        {
            // Keep moving forward at 80% speed until a small producer correction is absorbed.
            _correctionEnd = now + (current - target) / (.2 * rate);
            target = current;
        }
        else if (_initialized && Math.Abs(target - current) > .75) SeekVersion++;
        _anchor = target; _anchorTime = now; _rate = rate; _playing = playing;
        if (!_initialized || stamp >= _stamp) { _report = reported; _stamp = stamp; }
        _initialized = true;
    }
}
